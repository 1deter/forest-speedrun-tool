using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace ForestBot.Llm;

// ------------------------------------------------------------------
// The OpenAI-compatible chat API (POST <base>/chat/completions, Bearer
// key) - spoken by Mistral, Groq, Cerebras, OpenRouter, GitHub Models and
// OpenAI itself, so any of them is a config line. Tool calls come back as
// message.tool_calls (arguments as a JSON string) and are answered with
// role "tool" messages. Shaping is pure and tested.
// ------------------------------------------------------------------
public sealed class OpenAiChat : IChatModel
{
    /// Known providers' base URLs (the key's environment variable is
    /// <PROVIDER>_API_KEY).
    public static readonly Dictionary<string, string> BaseUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["mistral"] = "https://api.mistral.ai/v1",
        ["groq"] = "https://api.groq.com/openai/v1",
        ["cerebras"] = "https://api.cerebras.ai/v1",
        ["openrouter"] = "https://openrouter.ai/api/v1",
        ["github"] = "https://models.github.ai/inference",
        ["openai"] = "https://api.openai.com/v1",
    };

    private readonly HttpClient _http;
    private readonly string _provider;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly string _key;

    public string Name => _provider + ":" + _model;

    public OpenAiChat(HttpClient http, string provider, string baseUrl, string model, string key)
    {
        _http = http;
        _provider = provider;
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
        _key = key;
    }

    public async Task<ChatResult> CompleteAsync(string system, IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolSpec> tools, int maxOutputTokens, CancellationToken ct)
    {
        JsonObject body = BuildRequest(_model, system, messages, tools, maxOutputTokens);
        using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/chat/completions");
        req.Headers.Add("Authorization", "Bearer " + _key);
        req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using HttpResponseMessage resp = await _http.SendAsync(req, ct);
        string text = await resp.Content.ReadAsStringAsync(ct);
        if (resp.StatusCode == HttpStatusCode.TooManyRequests || (int)resp.StatusCode >= 500)
            throw new ModelUnavailableException(Name + ": HTTP " + (int)resp.StatusCode + " " + GeminiChat.Brief(text),
                resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(60));
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(Name + ": HTTP " + (int)resp.StatusCode + " " + GeminiChat.Brief(text));
        ChatResult r = ParseResponse(text);
        r.Provider = _provider;
        r.Model = _model;
        return r;
    }

    public static JsonObject BuildRequest(string model, string system, IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolSpec> tools, int maxOutputTokens)
    {
        JsonArray msgs = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = system ?? "" } };
        foreach (ChatMessage m in messages)
        {
            switch (m.Role)
            {
                case Role.User:
                    msgs.Add(new JsonObject { ["role"] = "user", ["content"] = m.Text ?? "" });
                    break;
                case Role.Tool:
                    msgs.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = m.ToolCallId, ["name"] = m.ToolName, ["content"] = m.Text ?? "" });
                    break;
                default:
                    JsonObject a = new JsonObject { ["role"] = "assistant", ["content"] = m.Text ?? "" };
                    if (m.ToolCalls.Count > 0)
                    {
                        JsonArray calls = new JsonArray();
                        foreach (ToolCall c in m.ToolCalls)
                            calls.Add(new JsonObject
                            {
                                ["id"] = c.Id, ["type"] = "function",
                                ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.Args.ToJsonString() },
                            });
                        a["tool_calls"] = calls;
                    }
                    msgs.Add(a);
                    break;
            }
        }
        JsonObject body = new JsonObject { ["model"] = model, ["messages"] = msgs, ["max_tokens"] = maxOutputTokens };
        if (tools != null && tools.Count > 0)
        {
            JsonArray ts = new JsonArray();
            foreach (ToolSpec t in tools)
                ts.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject { ["name"] = t.Name, ["description"] = t.Description, ["parameters"] = t.Parameters.DeepClone() },
                });
            body["tools"] = ts;
            body["tool_choice"] = "auto";
        }
        return body;
    }

    public static ChatResult ParseResponse(string json)
    {
        ChatResult r = new ChatResult();
        JsonNode root = JsonNode.Parse(json);
        JsonNode choice = root?["choices"]?[0];
        r.FinishReason = choice?["finish_reason"]?.GetValue<string>();
        JsonNode msg = choice?["message"];
        JsonNode content = msg?["content"];
        if (content is JsonValue) r.Text = content.GetValue<string>() ?? "";
        else if (content is JsonArray parts)   // some providers send content parts
        {
            StringBuilder b = new StringBuilder();
            foreach (JsonNode p in parts) if (p?["text"] != null) b.Append(p["text"].GetValue<string>());
            r.Text = b.ToString();
        }
        int n = 0;
        foreach (JsonNode tc in msg?["tool_calls"]?.AsArray() ?? new JsonArray())
        {
            JsonObject args = new JsonObject();
            string raw = tc?["function"]?["arguments"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(raw))
                try { if (JsonNode.Parse(raw) is JsonObject o) args = o; } catch { /* a malformed call reads as no arguments */ }
            r.ToolCalls.Add(new ToolCall { Id = tc?["id"]?.GetValue<string>() ?? "call_" + (n++), Name = tc?["function"]?["name"]?.GetValue<string>(), Args = args });
        }
        JsonNode usage = root?["usage"];
        if (usage != null)
        {
            r.InputTokens = usage["prompt_tokens"]?.GetValue<int>() ?? 0;
            r.OutputTokens = usage["completion_tokens"]?.GetValue<int>() ?? 0;
        }
        return r;
    }
}
