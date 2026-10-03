using System.Text;
using ForestBot.Llm;

namespace ForestBot.Agent;

/// One finished answer.
public sealed class Answer
{
    public string Text = "";
    /// What the model said it used (SOURCES line), else what it read.
    public List<string> Sources = new List<string>();
    /// What the tools actually returned (for logs / the research queue).
    public List<string> Read = new List<string>();
    public string Status = "answered";
    public string Model;
    public int ToolCalls;
    public int InputTokens;
    public int OutputTokens;
    public List<string> Trace = new List<string>();
    /// No model could answer (all out of quota) - Text is the busy message.
    public bool Busy;
}

/// A question and answer earlier in the conversation.
public sealed class Turn
{
    public string Question;
    public string Answer;
}

// ------------------------------------------------------------------
// The research-and-answer loop: the model calls tools until it writes a
// final answer, capped at MaxSteps rounds (then it is told to answer
// with what it has). A model out of quota rests and the question starts
// over on the next one. History = earlier questions and answers as
// text only (bounded), never earlier tool output.
// ------------------------------------------------------------------
public sealed class Answerer
{
    public int MaxSteps = 8;
    public int MaxOutputTokens = 8192;
    public int HistoryTurns = 4;

    private readonly ModelChain _models;
    private readonly Tools _tools;
    private readonly string _system;
    private readonly Action<string> _log;

    public Answerer(ModelChain models, Tools tools, string systemPrompt, Action<string> log)
    {
        _models = models;
        _tools = tools;
        _system = systemPrompt;
        _log = log ?? (_ => { });
    }

    public async Task<Answer> AskAsync(string question, IReadOnlyList<Turn> history, CancellationToken ct)
    {
        bool overloaded = false;
        for (int attempt = 0; attempt < Math.Max(1, _models.Models.Count); attempt++)
        {
            IChatModel model = _models.Pick();
            if (model == null) break;
            try
            {
                return await RunAsync(model, question, history, ct);
            }
            catch (ModelUnavailableException e)
            {
                _models.Rest(model, e.RetryAfter, e.Message);
                overloaded |= e.Overloaded;
            }
        }
        TimeSpan wait = _models.NextAvailable();
        string when = wait.TotalMinutes >= 2 ? "in about " + (int)Math.Ceiling(wait.TotalMinutes) + " minutes." : "in a minute.";
        return new Answer
        {
            Busy = true, Status = "busy",
            Text = overloaded
                ? "The AI service I use is overloaded right now - please try again " + when
                : "I'm out of free capacity right now - please try again " + when,
        };
    }

    private async Task<Answer> RunAsync(IChatModel model, string question, IReadOnlyList<Turn> history, CancellationToken ct)
    {
        Answer answer = new Answer { Model = model.Name };
        List<ChatMessage> msgs = new List<ChatMessage>();
        int start = Math.Max(0, (history?.Count ?? 0) - HistoryTurns);
        for (int i = start; i < (history?.Count ?? 0); i++)
        {
            msgs.Add(ChatMessage.User(history[i].Question));
            msgs.Add(ChatMessage.Assistant(history[i].Answer));
        }
        msgs.Add(ChatMessage.User(question));
        List<ToolSpec> specs = _tools.Specs();
        HashSet<string> read = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int step = 0; ; step++)
        {
            bool last = step >= MaxSteps;
            if (last) msgs.Add(ChatMessage.User("(Research limit reached - write your final answer now from what you have read, with the SOURCES and STATUS lines.)"));
            ChatResult r = await model.CompleteAsync(_system, msgs, last ? null : specs, MaxOutputTokens, ct);
            answer.InputTokens += r.InputTokens;
            answer.OutputTokens += r.OutputTokens;

            if (r.ToolCalls.Count == 0 || last)
            {
                if (string.IsNullOrWhiteSpace(r.Text))
                {
                    if (!last && step < MaxSteps) { msgs.Add(ChatMessage.User("(Your reply was empty - write the answer.)")); continue; }
                    r.Text = "I couldn't put an answer together for that one. It has been noted for research.\nSTATUS: not_documented";
                }
                Finish(answer, r.Text, read);
                return answer;
            }

            msgs.Add(new ChatMessage { Role = Role.Assistant, Text = r.Text, ToolCalls = r.ToolCalls, Raw = r.Raw, RawProvider = r.Provider });
            foreach (ToolCall call in r.ToolCalls)
            {
                string result = _tools.Run(call, read);
                answer.ToolCalls++;
                answer.Trace.Add(call.Name + "(" + call.Args.ToJsonString() + ") -> " + result.Length + " chars");
                msgs.Add(ChatMessage.ToolResult(call, result));
            }
        }
    }

    /// Strips the SOURCES / STATUS lines into fields.
    public static void Finish(Answer answer, string text, ICollection<string> read)
    {
        answer.Read = read.ToList();
        List<string> keep = new List<string>();
        foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.Trim().Trim('*', '_', '`').Trim();
            if (line.StartsWith(Prompt.SourcesTag, StringComparison.OrdinalIgnoreCase))
            {
                foreach (string s in line.Substring(Prompt.SourcesTag.Length).Split(','))
                {
                    string t = s.Trim().Trim('`', '[', ']', '.');
                    if (t.Length > 0 && !answer.Sources.Contains(t)) answer.Sources.Add(t);
                }
                continue;
            }
            if (line.StartsWith(Prompt.StatusTag, StringComparison.OrdinalIgnoreCase))
            {
                string st = line.Substring(Prompt.StatusTag.Length).Trim().ToLowerInvariant();
                foreach (string known in new[] { "answered", "partial", "not_documented", "off_topic" })
                    if (st.StartsWith(known)) answer.Status = known;
                continue;
            }
            keep.Add(raw);
        }
        if (answer.Sources.Count == 0) answer.Sources.AddRange(answer.Read);
        answer.Text = string.Join("\n", keep).Trim();
    }

    /// The sources as one Discord subtext line: cards by name, the rest
    /// short.
    public static string SourcesLine(Answer a, Knowledge.Corpus corpus)
    {
        if (a.Sources.Count == 0) return "";
        List<string> parts = new List<string>();
        foreach (string s in a.Sources)
        {
            string label = s;
            if (s.StartsWith("card:"))
            {
                string id = s.Substring(5);
                int h = id.IndexOf('#');
                if (h >= 0) id = id.Substring(0, h);
                label = corpus.Cards.TryGetValue(id, out Knowledge.Card c) ? c.Title : id;
            }
            else if (corpus.ById.TryGetValue(s, out Knowledge.Chunk ch)) label = ch.Title;
            else if (s.StartsWith("code:")) label = "code: " + s.Substring(5);
            if (!parts.Contains(label)) parts.Add(label);
        }
        StringBuilder b = new StringBuilder("-# Sources: ");
        b.Append(string.Join(" · ", parts.Take(8)));
        if (parts.Count > 8) b.Append(" · +" + (parts.Count - 8));
        return b.ToString();
    }
}
