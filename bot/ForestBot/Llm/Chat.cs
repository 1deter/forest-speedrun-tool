using System.Text.Json.Nodes;

namespace ForestBot.Llm;

/// A tool the model may call: name, description, JSON-schema parameters.
public sealed class ToolSpec
{
    public string Name;
    public string Description;
    /// A JSON schema object ({"type":"object","properties":{...},"required":[...]}).
    public JsonObject Parameters;
}

public sealed class ToolCall
{
    /// The provider's id for the call (OpenAI-style); Gemini has none - we
    /// make one so results pair up.
    public string Id;
    public string Name;
    public JsonObject Args = new JsonObject();

    public string Arg(string name)
    {
        JsonNode n = Args[name];
        if (n == null) return null;
        return n is JsonValue v && v.TryGetValue(out string s) ? s : n.ToJsonString();
    }
}

public enum Role { User, Assistant, Tool }

public sealed class ChatMessage
{
    public Role Role;
    public string Text;
    /// Assistant: the calls it made.
    public List<ToolCall> ToolCalls = new List<ToolCall>();
    /// Tool: which call this answers.
    public string ToolCallId;
    public string ToolName;
    /// Assistant: the provider's own message, sent back verbatim (Gemini's
    /// thought signatures must return unchanged); null = build from Text.
    public JsonNode Raw;
    /// Which provider wrote Raw - a fallback model rebuilds from Text.
    public string RawProvider;

    public static ChatMessage User(string text) => new ChatMessage { Role = Role.User, Text = text };
    public static ChatMessage Assistant(string text) => new ChatMessage { Role = Role.Assistant, Text = text };
    public static ChatMessage ToolResult(ToolCall call, string text) =>
        new ChatMessage { Role = Role.Tool, ToolCallId = call.Id, ToolName = call.Name, Text = text };
}

public sealed class ChatResult
{
    public string Text = "";
    public List<ToolCall> ToolCalls = new List<ToolCall>();
    public JsonNode Raw;
    public string Provider;
    public string Model;
    public string FinishReason;
    public int InputTokens;
    public int OutputTokens;
}

/// The provider is out of quota or overloaded - try another, or later.
public sealed class ModelUnavailableException : Exception
{
    public TimeSpan RetryAfter;
    /// True = overloaded (5xx, temporary); false = out of quota (429).
    public bool Overloaded;
    public ModelUnavailableException(string message, TimeSpan retryAfter, bool overloaded = false) : base(message)
    {
        RetryAfter = retryAfter;
        Overloaded = overloaded;
    }
}

// ------------------------------------------------------------------
// A model API call with two quick retries on 5xx ("high demand" spikes
// are usually seconds long - seen on the first live test, 2026-10-03).
// 429 is not retried here: the chain rests that model instead.
// ------------------------------------------------------------------
public static class Retry
{
    public static TimeSpan[] Delays = { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5) };

    public static async Task<(HttpResponseMessage resp, string text)> SendAsync(HttpClient http, Func<HttpRequestMessage> make, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            using HttpRequestMessage req = make();
            HttpResponseMessage resp = await http.SendAsync(req, ct);
            string text = await resp.Content.ReadAsStringAsync(ct);
            if ((int)resp.StatusCode < 500 || attempt >= Delays.Length) return (resp, text);
            resp.Dispose();
            await Task.Delay(Delays[attempt], ct);
        }
    }
}

public interface IChatModel
{
    /// "gemini:gemini-3.8-flash"
    string Name { get; }
    Task<ChatResult> CompleteAsync(string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolSpec> tools,
        int maxOutputTokens, CancellationToken ct);
}
