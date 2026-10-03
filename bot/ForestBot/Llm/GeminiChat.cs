using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace ForestBot.Llm;

// ------------------------------------------------------------------
// Gemini's own REST API: POST v1beta/models/<model>:generateContent,
// key in `x-goog-api-key`. Function calling: tools.functionDeclarations;
// the model's turn comes back as parts (text / functionCall, each maybe
// carrying a thoughtSignature) and is sent back VERBATIM in the next
// request - thinking models refuse a turn whose signatures were dropped.
// Tool results go back as a user turn of functionResponse parts.
// The request / response shaping is pure (BuildRequest / ParseResponse)
// and tested.
// ------------------------------------------------------------------
public sealed class GeminiChat : IChatModel
{
    public const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/models/";
    /// Ids we make for calls that came without one.
    public const string LocalIdPrefix = "local_";

    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _key;
    private readonly string _thinkingLevel;

    public string Name => "gemini:" + _model;

    /// `thinkingLevel`: "low" / "high" for models that take it, or null for
    /// the model's default.
    public GeminiChat(HttpClient http, string model, string key, string thinkingLevel = null)
    {
        _http = http;
        _model = model;
        _key = key;
        _thinkingLevel = thinkingLevel;
    }

    public async Task<ChatResult> CompleteAsync(string system, IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolSpec> tools, int maxOutputTokens, CancellationToken ct)
    {
        string json = BuildRequest(system, messages, tools, maxOutputTokens, _thinkingLevel).ToJsonString();
        (HttpResponseMessage resp, string text) = await Retry.SendAsync(_http, () =>
        {
            HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, Endpoint + _model + ":generateContent");
            req.Headers.Add("x-goog-api-key", _key);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return req;
        }, ct);
        using HttpResponseMessage held = resp;
        bool overloaded = (int)resp.StatusCode >= 500;
        if (resp.StatusCode == HttpStatusCode.TooManyRequests || overloaded)
            throw new ModelUnavailableException(Name + ": HTTP " + (int)resp.StatusCode + QuotaIds(text) + " " + Brief(text),
                overloaded ? TimeSpan.FromSeconds(20) : RetryAfter(resp, text), overloaded);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(Name + ": HTTP " + (int)resp.StatusCode + " " + Brief(text));
        ChatResult r = ParseResponse(text);
        r.Provider = "gemini";
        r.Model = _model;
        return r;
    }

    public static JsonObject BuildRequest(string system, IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolSpec> tools, int maxOutputTokens, string thinkingLevel)
    {
        JsonArray contents = new JsonArray();
        JsonArray pendingResults = null;
        foreach (ChatMessage m in messages)
        {
            if (m.Role == Role.Tool)
            {
                pendingResults ??= new JsonArray();
                JsonObject fr = new JsonObject
                {
                    ["name"] = m.ToolName,
                    ["response"] = new JsonObject { ["content"] = m.Text ?? "" },
                };
                // The model's own call id, when it gave one, pairs the result.
                if (m.ToolCallId != null && !m.ToolCallId.StartsWith(LocalIdPrefix)) fr["id"] = m.ToolCallId;
                pendingResults.Add(new JsonObject { ["functionResponse"] = fr });
                continue;
            }
            if (pendingResults != null)
            {
                contents.Add(new JsonObject { ["role"] = "user", ["parts"] = pendingResults });
                pendingResults = null;
            }
            if (m.Role == Role.User)
                contents.Add(new JsonObject { ["role"] = "user", ["parts"] = new JsonArray { new JsonObject { ["text"] = m.Text ?? "" } } });
            else if (m.Raw != null && m.RawProvider == "gemini")
                contents.Add(m.Raw.DeepClone());
            else
            {
                JsonArray parts = new JsonArray();
                if (!string.IsNullOrEmpty(m.Text)) parts.Add(new JsonObject { ["text"] = m.Text });
                foreach (ToolCall c in m.ToolCalls)
                    parts.Add(new JsonObject { ["functionCall"] = new JsonObject { ["name"] = c.Name, ["args"] = c.Args.DeepClone() } });
                if (parts.Count == 0) parts.Add(new JsonObject { ["text"] = "" });
                contents.Add(new JsonObject { ["role"] = "model", ["parts"] = parts });
            }
        }
        if (pendingResults != null) contents.Add(new JsonObject { ["role"] = "user", ["parts"] = pendingResults });

        JsonObject gen = new JsonObject { ["maxOutputTokens"] = maxOutputTokens };
        if (!string.IsNullOrEmpty(thinkingLevel)) gen["thinkingConfig"] = new JsonObject { ["thinkingLevel"] = thinkingLevel };

        JsonObject body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray { new JsonObject { ["text"] = system ?? "" } } },
            ["contents"] = contents,
            ["generationConfig"] = gen,
        };
        if (tools != null && tools.Count > 0)
        {
            JsonArray decls = new JsonArray();
            foreach (ToolSpec t in tools)
                decls.Add(new JsonObject { ["name"] = t.Name, ["description"] = t.Description, ["parameters"] = t.Parameters.DeepClone() });
            body["tools"] = new JsonArray { new JsonObject { ["functionDeclarations"] = decls } };
            body["toolConfig"] = new JsonObject { ["functionCallingConfig"] = new JsonObject { ["mode"] = "AUTO" } };
        }
        return body;
    }

    public static ChatResult ParseResponse(string json)
    {
        ChatResult r = new ChatResult();
        JsonNode root = JsonNode.Parse(json);
        JsonNode cand = root?["candidates"]?[0];
        r.FinishReason = cand?["finishReason"]?.GetValue<string>();
        JsonNode content = cand?["content"];
        if (content != null)
        {
            r.Raw = content.DeepClone();
            if (r.Raw is JsonObject o && o["role"] == null) o["role"] = "model";
            StringBuilder text = new StringBuilder();
            int n = 0;
            foreach (JsonNode part in content["parts"]?.AsArray() ?? new JsonArray())
            {
                if (part?["thought"]?.GetValue<bool>() == true) continue;   // thought summaries are not the answer
                if (part?["text"] != null) text.Append(part["text"].GetValue<string>());
                JsonNode fc = part?["functionCall"];
                if (fc != null)
                    r.ToolCalls.Add(new ToolCall
                    {
                        Id = fc["id"]?.GetValue<string>() ?? LocalIdPrefix + (n++),
                        Name = fc["name"]?.GetValue<string>(),
                        Args = fc["args"] as JsonObject != null ? (JsonObject)fc["args"].DeepClone() : new JsonObject(),
                    });
            }
            r.Text = text.ToString();
        }
        else if (root?["promptFeedback"]?["blockReason"] != null)
            r.FinishReason = "BLOCKED:" + root["promptFeedback"]["blockReason"].GetValue<string>();
        JsonNode usage = root?["usageMetadata"];
        if (usage != null)
        {
            r.InputTokens = usage["promptTokenCount"]?.GetValue<int>() ?? 0;
            r.OutputTokens = (usage["candidatesTokenCount"]?.GetValue<int>() ?? 0) + (usage["thoughtsTokenCount"]?.GetValue<int>() ?? 0);
        }
        return r;
    }

    private static TimeSpan RetryAfter(HttpResponseMessage resp, string body)
    {
        if (resp.Headers.RetryAfter?.Delta is TimeSpan d) return d;
        // Gemini puts it in the error details: "retryDelay": "37s"
        int i = body.IndexOf("\"retryDelay\"", StringComparison.Ordinal);
        if (i >= 0)
        {
            int q1 = body.IndexOf('"', body.IndexOf(':', i) + 1);
            int q2 = q1 < 0 ? -1 : body.IndexOf('"', q1 + 1);
            if (q2 > q1 && double.TryParse(body.Substring(q1 + 1, q2 - q1 - 1).TrimEnd('s'),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double s))
                return TimeSpan.FromSeconds(Math.Max(1, s));
        }
        return TimeSpan.FromSeconds(60);
    }

    /// The quotas a 429 names (" [GenerateRequestsPerDayPerProjectPerModel-FreeTier]"):
    /// a per-day quota and a per-minute one need different handling, and
    /// the name sits past what Brief keeps.
    internal static string QuotaIds(string body)
    {
        List<string> ids = new List<string>();
        for (int i = body.IndexOf("\"quotaId\"", StringComparison.Ordinal); i >= 0;
             i = body.IndexOf("\"quotaId\"", i + 1, StringComparison.Ordinal))
        {
            int q1 = body.IndexOf('"', body.IndexOf(':', i) + 1);
            int q2 = q1 < 0 ? -1 : body.IndexOf('"', q1 + 1);
            if (q2 > q1 && !ids.Contains(body.Substring(q1 + 1, q2 - q1 - 1))) ids.Add(body.Substring(q1 + 1, q2 - q1 - 1));
        }
        return ids.Count == 0 ? "" : " [" + string.Join(", ", ids) + "]";
    }

    internal static string Brief(string s)
    {
        s = (s ?? "").Replace('\n', ' ');
        return s.Length <= 300 ? s : s.Substring(0, 300) + "...";
    }
}
