using System.Text;
using System.Text.Json.Nodes;
using ForestBot.Agent;
using ForestBot.Llm;

namespace ForestBot.Eval;

// ------------------------------------------------------------------
// Scores the bot on knowledge/eval/questions.md: each question (and its
// follow-up) is answered for real, then a judge call checks every "must"
// fact and "not" item against the answer. Writes a report to the data
// folder and prints the score. Run it before swapping models or after a
// large knowledge change; it spends quota (~3-6 requests a question),
// so it paces itself.
// ------------------------------------------------------------------
public sealed class EvalRunner
{
    private readonly Brain _brain;
    private readonly Action<string> _out;
    public TimeSpan Pause = TimeSpan.FromSeconds(8);

    public EvalRunner(Brain brain, Action<string> output)
    {
        _brain = brain;
        _out = output;
    }

    public async Task<double> RunAsync(string questionsPath, ICollection<string> only, CancellationToken ct)
    {
        List<EvalQuestion> questions = EvalQuestions.Parse(File.ReadAllText(questionsPath));
        if (only != null && only.Count > 0) questions = questions.Where(q => only.Contains(q.Id)).ToList();
        StringBuilder report = new StringBuilder("# Eval " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + "Z, knowledge " + _brain.Corpus.Version + "\n\n");
        int passed = 0, total = 0;

        foreach (EvalQuestion q in questions)
        {
            Answer a = await _brain.Answerer.AskAsync(q.Question, new List<Turn>(), ct);
            (int ok, int n, string detail) = await JudgeAsync(q.Question, a, q.Must, q.Not, q.Cards, ct);
            string thenDetail = "";
            if (q.Then != null && !a.Busy)
            {
                await Task.Delay(Pause, ct);
                Answer b = await _brain.Answerer.AskAsync(q.Then, new List<Turn> { new Turn { Question = q.Question, Answer = a.Text } }, ct);
                (int ok2, int n2, string d2) = await JudgeAsync(q.Then, b, q.ThenMust, new List<string>(), new List<string>(), ct);
                ok += ok2; n += n2;
                thenDetail = "\n**Follow-up:** " + q.Then + "\n" + d2 + "\n<details>\n\n" + b.Text + "\n\n</details>\n";
            }
            passed += ok; total += n;
            string line = q.Id + ": " + ok + "/" + n + "  (" + a.Model + ", " + a.ToolCalls + " lookups, " + a.Status + ")";
            _out(line);
            report.Append("## ").Append(line).Append("\n**Q:** ").Append(q.Question).Append('\n').Append(detail)
                  .Append("\n<details>\n\n").Append(a.Text).Append("\n\n").Append("sources: ").Append(string.Join(", ", a.Sources))
                  .Append("\n\n</details>\n").Append(thenDetail).Append('\n');
            await Task.Delay(Pause, ct);
        }

        double score = total == 0 ? 0 : (double)passed / total;
        string summary = "Score: " + passed + "/" + total + " = " + (score * 100).ToString("0.0") + "%  (models: " +
                         string.Join(" -> ", _brain.Models.Models.Select(m => m.Name)) + ")";
        _out(summary);
        report.Insert(0, summary + "\n\n");
        string path = Path.Combine(_brain.Config.DataDir, "eval-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmm") + ".md");
        Directory.CreateDirectory(_brain.Config.DataDir);
        File.WriteAllText(path, report.ToString());
        _out("Report: " + path);
        return score;
    }

    /// Asks the judge which facts the answer states. Expected cards count
    /// as facts too (did it read them).
    private async Task<(int ok, int total, string detail)> JudgeAsync(string question, Answer a, List<string> must,
        List<string> not, List<string> cards, CancellationToken ct)
    {
        StringBuilder detail = new StringBuilder();
        int ok = 0, total = 0;
        foreach (string card in cards)
        {
            bool read = a.Read.Any(r => r.Equals("card:" + card, StringComparison.OrdinalIgnoreCase) || r.StartsWith("card:" + card + "#", StringComparison.OrdinalIgnoreCase))
                        || a.Sources.Any(s => s.Contains(card, StringComparison.OrdinalIgnoreCase));
            total++; if (read) ok++;
            detail.Append(read ? "- [x] read " : "- [ ] did not read ").Append(card).Append('\n');
        }
        if (a.Busy) { detail.Append("- busy - not judged\n"); return (ok, total + must.Count + not.Count, detail.ToString()); }
        if (must.Count + not.Count == 0) return (ok, total, detail.ToString());

        StringBuilder prompt = new StringBuilder();
        prompt.Append("Judge an answer to a question about the game The Forest. For each REQUIRED fact, true if the answer states it (same meaning, any wording; numbers must match). ")
              .Append("For each FORBIDDEN item, true if the answer does it. Reply with JSON only: {\"must\":[true/false...],\"not\":[true/false...]}.\n\n")
              .Append("QUESTION: ").Append(question).Append("\n\nANSWER:\n").Append(a.Text).Append("\n\nREQUIRED:\n");
        for (int i = 0; i < must.Count; i++) prompt.Append(i + 1).Append(". ").Append(must[i]).Append('\n');
        prompt.Append("\nFORBIDDEN:\n");
        for (int i = 0; i < not.Count; i++) prompt.Append(i + 1).Append(". ").Append(not[i]).Append('\n');

        bool[] m = new bool[must.Count], n = new bool[not.Count];
        try
        {
            IChatModel judge = _brain.Models.Pick() ?? throw new InvalidOperationException("no model for the judge");
            ChatResult r = await judge.CompleteAsync("You are a strict, fair grader. Output JSON only.",
                new List<ChatMessage> { ChatMessage.User(prompt.ToString()) }, null, 1024, ct);
            string json = r.Text.Trim();
            int s = json.IndexOf('{'), e = json.LastIndexOf('}');
            JsonNode node = JsonNode.Parse(json.Substring(s, e - s + 1));
            for (int i = 0; i < m.Length; i++) m[i] = node?["must"]?[i]?.GetValue<bool>() == true;
            for (int i = 0; i < n.Length; i++) n[i] = node?["not"]?[i]?.GetValue<bool>() == true;
        }
        catch (Exception ex) { detail.Append("- judge failed: ").Append(ex.Message).Append('\n'); }

        for (int i = 0; i < m.Length; i++) { total++; if (m[i]) ok++; detail.Append(m[i] ? "- [x] " : "- [ ] ").Append(must[i]).Append('\n'); }
        for (int i = 0; i < n.Length; i++) { total++; if (!n[i]) ok++; detail.Append(!n[i] ? "- [x] did not: " : "- [ ] DID: ").Append(not[i]).Append('\n'); }
        return (ok, total, detail.ToString());
    }
}
