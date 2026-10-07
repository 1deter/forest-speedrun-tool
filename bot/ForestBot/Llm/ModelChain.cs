namespace ForestBot.Llm;

// ------------------------------------------------------------------
// The configured models in order of preference (FOREST_BOT_MODELS):
// a model out of quota rests until its retry time and the next one
// answers. All resting = "busy" (the caller says so to the runner).
// One question stays on one model once it has started using tools - a
// switch mid-question rebuilds the history from text (the other
// provider cannot read the first one's raw turns).
// ------------------------------------------------------------------
public sealed class ModelChain
{
    private readonly List<IChatModel> _models;
    private readonly Dictionary<string, DateTime> _restUntil = new Dictionary<string, DateTime>();
    private readonly object _lock = new object();
    private readonly Action<string> _log;

    public IReadOnlyList<IChatModel> Models => _models;

    public ModelChain(IEnumerable<IChatModel> models, Action<string> log)
    {
        _models = new List<IChatModel>(models);
        _log = log ?? (_ => { });
    }

    /// Spec "gemini:gemini-3.8-flash, mistral:mistral-medium-latest,
    /// openai-compatible:<model>@<base url>". Keys from the environment:
    /// GEMINI_API_KEY, <PROVIDER>_API_KEY. A model whose key is missing is
    /// skipped (and logged).
    public static ModelChain FromSpec(string spec, HttpClient http, Func<string, string> env, string thinkingLevel, Action<string> log)
    {
        List<IChatModel> models = new List<IChatModel>();
        foreach (string raw in (spec ?? "").Split(','))
        {
            string item = raw.Trim();
            if (item.Length == 0) continue;
            int colon = item.IndexOf(':');
            if (colon <= 0) { log("Models: '" + item + "' is not provider:model - skipped"); continue; }
            string provider = item.Substring(0, colon).Trim().ToLowerInvariant();
            string model = item.Substring(colon + 1).Trim();
            if (provider == "gemini")
            {
                string key = env("GEMINI_API_KEY");
                if (string.IsNullOrEmpty(key)) { log("Models: " + item + " skipped - no GEMINI_API_KEY"); continue; }
                models.Add(new GeminiChat(http, model, key, thinkingLevel));
                continue;
            }
            string baseUrl;
            int at = model.IndexOf('@');
            if (at > 0) { baseUrl = model.Substring(at + 1); model = model.Substring(0, at); }
            else if (!OpenAiChat.BaseUrls.TryGetValue(provider, out baseUrl))
            {
                log("Models: unknown provider '" + provider + "' (give model@base-url) - skipped");
                continue;
            }
            string keyName = provider.ToUpperInvariant().Replace('-', '_') + "_API_KEY";
            string k = env(keyName);
            if (string.IsNullOrEmpty(k)) { log("Models: " + item + " skipped - no " + keyName); continue; }
            models.Add(new OpenAiChat(http, provider, baseUrl, model, k));
        }
        log("Models: " + (models.Count == 0 ? "none configured" : string.Join(" -> ", models.Select(m => m.Name))));
        return new ModelChain(models, log);
    }

    /// Swaps the models in place (live settings: a new order). Resting times are kept by name.
    public void Replace(IEnumerable<IChatModel> models)
    {
        lock (_lock)
        {
            _models.Clear();
            _models.AddRange(models);
        }
    }

    /// The first model not resting, or null.
    public IChatModel Pick(IChatModel preferred = null)
    {
        lock (_lock)
        {
            DateTime now = DateTime.UtcNow;
            if (preferred != null && !Resting(preferred, now)) return preferred;
            foreach (IChatModel m in _models)
                if (!Resting(m, now)) return m;
            return null;
        }
    }

    public void Rest(IChatModel model, TimeSpan howLong, string why)
    {
        lock (_lock) _restUntil[model.Name] = DateTime.UtcNow + howLong;
        _log("Models: " + model.Name + " rests " + (int)howLong.TotalSeconds + " s (" + why + ")");
    }

    /// When the soonest resting model is back (for the "busy" message).
    public TimeSpan NextAvailable()
    {
        lock (_lock)
        {
            DateTime now = DateTime.UtcNow, best = DateTime.MaxValue;
            foreach (IChatModel m in _models)
                if (_restUntil.TryGetValue(m.Name, out DateTime t) && t < best) best = t;
            return best == DateTime.MaxValue ? TimeSpan.Zero : best - now;
        }
    }

    private bool Resting(IChatModel m, DateTime now) => _restUntil.TryGetValue(m.Name, out DateTime t) && t > now;
}
