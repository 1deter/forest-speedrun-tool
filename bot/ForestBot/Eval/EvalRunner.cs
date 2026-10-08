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
// so it paces itself, and waits out a short quota rest (a per-minute
// limit) instead of scoring the question "busy" - the first full run
// lost half its questions that way (2026-10-03). A question still busy
// after that (a daily quota) is skipped and counted, never scored as
// wrong (gotcha 94). CI runs a subset after each bot deploy
// (.github/workflows/bot.yml, warn-only).
// ------------------------------------------------------------------
public sealed class EvalRunner
{
    private readonly ModelChain _models;
    private readonly Answerer _answerer;
    private readonly string _knowledgeVersion, _dataDir;
    private readonly Action<string> _out;
    public TimeSpan Pause = TimeSpan.FromSeconds(8);
    /// The longest quota rest waited out; a longer one (a daily quota) is busy.
    public TimeSpan MaxWait = TimeSpan.FromMinutes(5);

    public EvalRunner(Brain brain, Action<string> output)
        : this(brain.Models, brain.Answerer, brain.Corpus.Version, brain.Config.DataDir, output) { }

    /// The parts it uses (tests pass a scripted model).
    public EvalRunner(ModelChain models, Answerer answerer, string knowledgeVersion, string dataDir, Action<string> output)
    {
        _models = models;
        _answerer = answerer;
        _knowledgeVersion = knowledgeVersion;
        _dataDir = dataDir;
        _out = output;
    }

    /// No model had quota left for the judge: the question is busy, not wrong.
    private sealed class JudgeBusyException : Exception { }

    /// Runs the questions, writes the report to the data folder (and to
    /// `summaryPath` too when given, e.g. $GITHUB_STEP_SUMMARY) and returns
    /// the score over the answered questions.
    public async Task<EvalScore> RunAsync(string questionsPath, ICollection<string> only, CancellationToken ct, string summaryPath = null)
    {
        List<EvalQuestion> questions = EvalQuestions.Select(EvalQuestions.Parse(File.ReadAllText(questionsPath)), only);
        StringBuilder report = new StringBuilder("# Eval " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + "Z, knowledge " + _knowledgeVersion + "\n\n");
        EvalScore score = new EvalScore();

        foreach (EvalQuestion q in questions)
        {
            Answer a = await AskAsync(q.Question, new List<Turn>(), ct);
            (int ok, int n, string detail) = (0, 0, null);
            if (!a.Busy)
                try { (ok, n, detail) = await JudgeAsync(q.Question, a, q.Must, q.Not, q.Cards, ct); }
                catch (JudgeBusyException) { detail = null; }
            if (detail != null) (ok, n, detail) = AddLength(a.Text, q.MaxLength, q.MinLength, ok, n, detail);
            if (detail == null)
            {
                score.Busy++;
                string why = a.Busy ? "no model available (quota / overloaded / timeout)" : "no model available for the judge";
                _out(q.Id + ": busy - skipped (" + why + ")");
                report.Append("## ").Append(q.Id).Append(": busy - skipped (").Append(why).Append(")\n**Q:** ").Append(q.Question).Append("\n\n");
                continue;
            }
            string thenDetail = "", thenChecks = null;
            if (q.Then != null)
            {
                await Task.Delay(Pause, ct);
                Answer b = await AskAsync(q.Then, new List<Turn> { new Turn { Question = q.Question, Answer = a.Text } }, ct);
                thenDetail = "\n**Follow-up:** " + q.Then + "\n- busy - skipped\n";
                if (!b.Busy)
                    try
                    {
                        (int ok2, int n2, string d2) = await JudgeAsync(q.Then, b, q.ThenMust, new List<string>(), new List<string>(), ct);
                        (ok2, n2, d2) = AddLength(b.Text, q.ThenMaxLength, q.ThenMinLength, ok2, n2, d2);
                        ok += ok2; n += n2;
                        thenChecks = d2;
                        thenDetail = "\n**Follow-up:** " + q.Then + "\n" + d2 + "\n<details>\n\n" + b.Text + "\n\n</details>\n";
                    }
                    catch (JudgeBusyException) { }
            }
            score.Passed += ok; score.Total += n; score.Answered++;
            string line = q.Id + ": " + ok + "/" + n + "  (" + a.Model + ", " + a.ToolCalls + " lookups, " + a.Status + ")";
            _out(line);
            PrintFailed(detail, "");
            if (thenChecks != null) PrintFailed(thenChecks, "follow-up: ");
            report.Append("## ").Append(line).Append("\n**Q:** ").Append(q.Question).Append('\n').Append(detail)
                  .Append("\n<details>\n\n").Append(a.Text).Append("\n\n").Append("sources: ").Append(string.Join(", ", a.Sources))
                  .Append("\n\n</details>\n").Append(thenDetail).Append('\n');
            await Task.Delay(Pause, ct);
        }

        string summary = score.Line() + "  (models: " + string.Join(" -> ", _models.Models.Select(m => m.Name)) + ")";
        _out(summary);
        report.Insert(0, summary + "\n\n");
        string path = Path.Combine(_dataDir, "eval-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmm") + ".md");
        Directory.CreateDirectory(_dataDir);
        File.WriteAllText(path, report.ToString());
        if (!string.IsNullOrEmpty(summaryPath)) File.AppendAllText(summaryPath, report.ToString());
        _out("Report: " + path);
        return score;
    }

    /// Each unticked check under the score line, so the CI log says why a
    /// question lost points (the step summary needs a GitHub sign-in).
    private void PrintFailed(string detail, string prefix)
    {
        foreach (string l in detail.Split('\n'))
            if (l.StartsWith("- [ ]") || l.StartsWith("- judge failed")) _out("    " + prefix + l);
    }

    /// A length limit counts as one more fact.
    private static (int ok, int n, string detail) AddLength(string text, int max, int min, int ok, int n, string detail)
    {
        (bool good, string line) = EvalQuestion.CheckLength(text, max, min);
        if (line == null) return (ok, n, detail);
        return (ok + (good ? 1 : 0), n + 1, detail + line + "\n");
    }

    /// An answer, retried while every model rests for less than MaxWait.
    private async Task<Answer> AskAsync(string question, List<Turn> history, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            Answer a = await _answerer.AskAsync(question, history, ct);
            if (!a.Busy || attempt >= 5 || !await WaitForModelAsync(ct)) return a;
        }
    }

    /// Sleeps until the soonest resting model is back; false if that is
    /// longer than MaxWait.
    private async Task<bool> WaitForModelAsync(CancellationToken ct)
    {
        TimeSpan wait = _models.NextAvailable();
        if (wait > MaxWait) return false;
        _out("  (quota rest - waiting " + (int)wait.TotalSeconds + " s)");
        await Task.Delay(wait + TimeSpan.FromSeconds(2), ct);
        return true;
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
            ChatResult r = null;
            for (int attempt = 0; r == null; attempt++)
            {
                IChatModel judge = _models.Pick();
                if (judge == null)
                {
                    if (attempt >= 5 || !await WaitForModelAsync(ct)) throw new JudgeBusyException();
                    continue;
                }
                try
                {
                    r = await judge.CompleteAsync("You are a strict, fair grader. Output JSON only.",
                        new List<ChatMessage> { ChatMessage.User(prompt.ToString()) }, null, 1024, ct);
                }
                catch (ModelUnavailableException u) { _models.Rest(judge, u.RetryAfter, u.Message); }
            }
            string json = r.Text.Trim();
            int s = json.IndexOf('{'), e = json.LastIndexOf('}');
            JsonNode node = JsonNode.Parse(json.Substring(s, e - s + 1));
            for (int i = 0; i < m.Length; i++) m[i] = node?["must"]?[i]?.GetValue<bool>() == true;
            for (int i = 0; i < n.Length; i++) n[i] = node?["not"]?[i]?.GetValue<bool>() == true;
        }
        catch (Exception ex) when (ex is not JudgeBusyException) { detail.Append("- judge failed: ").Append(ex.Message).Append('\n'); }

        for (int i = 0; i < m.Length; i++) { total++; if (m[i]) ok++; detail.Append(m[i] ? "- [x] " : "- [ ] ").Append(must[i]).Append('\n'); }
        for (int i = 0; i < n.Length; i++) { total++; if (!n[i]) ok++; detail.Append(!n[i] ? "- [x] did not: " : "- [ ] DID: ").Append(not[i]).Append('\n'); }
        return (ok, total, detail.ToString());
    }
}

/// Facts passed over facts judged, from answered questions only; busy
/// questions are counted apart (gotcha 94).
public sealed class EvalScore
{
    public int Passed, Total, Answered, Busy;

    public double Percent => Total == 0 ? 0 : 100.0 * Passed / Total;

    /// "Score: 7/9 = 77.8%, 3 answered, 1 busy skipped" - bot.yml reads the percent.
    public string Line() =>
        "Score: " + Passed + "/" + Total + " = " + Percent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%, " +
        Answered + " answered, " + Busy + " busy skipped";
}
