namespace ForestBot;

// ------------------------------------------------------------------
// Settings, all from the environment (bot/README.md lists them). Secrets
// (tokens, API keys) are read here and never logged.
// ------------------------------------------------------------------
public sealed class BotConfig
{
    public string DiscordToken;
    /// "gemini:gemini-3.8-flash,mistral:mistral-medium-latest" - order = preference.
    public string Models;
    public string ThinkingLevel;
    /// A folder laid out like the repo: knowledge/, docs/ (the build copies them to kb/).
    public string KnowledgeRoot;
    /// The decompiled Assembly-CSharp folder (private); empty = no code tools.
    public string CodeRoot;
    /// bot.db, the vector cache, logs.
    public string DataDir;
    /// model.onnx + vocab.txt; empty = keyword search only.
    public string EmbedModelDir;
    /// Channel ids the bot answers in (empty = everywhere it can read); DMs on/off.
    public HashSet<ulong> Channels = new HashSet<ulong>();
    public bool AllowDms;
    /// Where research-queue items are posted (0 = only stored).
    public ulong QueueChannel;
    public int PerHour;
    public int PerDay;
    /// A guild to register /ask in instantly (testing); 0 = global (takes up to an hour the first time).
    public ulong TestGuild;

    /// The revision of the site's settings applied (0 = the .env values only).
    public long SettingsRev;

    public Func<string, string> Env = Environment.GetEnvironmentVariable;

    public static BotConfig FromEnvironment()
    {
        BotConfig c = new BotConfig();
        string baseDir = AppContext.BaseDirectory;
        c.DiscordToken = c.Get("FOREST_BOT_DISCORD_TOKEN");
        c.LoadLive();
        c.KnowledgeRoot = c.Get("FOREST_BOT_KNOWLEDGE") ?? Path.Combine(baseDir, "kb");
        c.DataDir = c.Get("FOREST_BOT_DATA") ?? Path.Combine(baseDir, "data");
        c.CodeRoot = c.Get("FOREST_BOT_CODE") ?? DefaultCodeRoot();
        c.EmbedModelDir = c.Get("FOREST_BOT_EMBED_MODEL") ?? Path.Combine(c.DataDir, "embed");
        ulong.TryParse(c.Get("FOREST_BOT_TEST_GUILD"), out c.TestGuild);
        return c;
    }

    /// The settings the site's Bot tab can change, from the environment (the defaults).
    private void LoadLive()
    {
        Models = Get("FOREST_BOT_MODELS") ?? "gemini:gemini-3.8-flash,gemini:gemini-3.5-flash-lite,mistral:mistral-medium-latest";
        ThinkingLevel = Get("FOREST_BOT_THINKING");
        HashSet<ulong> channels = new HashSet<ulong>();
        foreach (string s in (Get("FOREST_BOT_CHANNELS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (ulong.TryParse(s, out ulong id)) channels.Add(id);
        Channels = channels;
        AllowDms = Get("FOREST_BOT_DMS") != "off";
        ulong.TryParse(Get("FOREST_BOT_QUEUE_CHANNEL"), out QueueChannel);
        PerHour = Int(Get("FOREST_BOT_PER_HOUR"), 15);
        PerDay = Int(Get("FOREST_BOT_PER_DAY"), 60);
    }

    /// Applies the site's settings over the .env defaults, live (no restart). A setting the
    /// site does not have (or holds an unusable value for) keeps its .env value, so a setting
    /// removed on the site goes back to the default. The channel set is replaced as a whole.
    public void ApplySettings(System.Text.Json.Nodes.JsonObject s, long rev)
    {
        LoadLive();
        if (s["channels"] is System.Text.Json.Nodes.JsonArray list && list.Count > 0)
        {
            HashSet<ulong> channels = new HashSet<ulong>();
            foreach (var n in list)
                if (n is System.Text.Json.Nodes.JsonValue v && ulong.TryParse(v.ToString().Trim('"'), out ulong id)) channels.Add(id);
            if (channels.Count > 0) Channels = channels;
        }
        if (s["dms"] is System.Text.Json.Nodes.JsonValue d && d.TryGetValue(out bool dms)) AllowDms = dms;
        if (s["perHour"] is System.Text.Json.Nodes.JsonValue h && h.TryGetValue(out int ph) && ph > 0) PerHour = ph;
        if (s["perDay"] is System.Text.Json.Nodes.JsonValue pd && pd.TryGetValue(out int day) && day > 0) PerDay = day;
        if (s["models"] is System.Text.Json.Nodes.JsonValue m && m.TryGetValue(out string models) && !string.IsNullOrWhiteSpace(models)) Models = models;
        if (s["thinking"] is System.Text.Json.Nodes.JsonValue t && t.TryGetValue(out string th) && !string.IsNullOrWhiteSpace(th)) ThinkingLevel = th;
        if (s["queueChannel"] is System.Text.Json.Nodes.JsonValue q && ulong.TryParse(q.ToString().Trim('"'), out ulong qc) && qc != 0) QueueChannel = qc;
        SettingsRev = rev;
    }

    /// Environment first; on Windows also the User scope (the author's
    /// variables are User-scope and tool shells do not inherit them).
    public string Get(string name)
    {
        string v = Env(name);
        if (string.IsNullOrEmpty(v) && OperatingSystem.IsWindows())
            v = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
        return string.IsNullOrEmpty(v) ? null : v;
    }

    /// The author's local copy (docs/knowledge-bot.md *Decompiled code*).
    private static string DefaultCodeRoot()
    {
        string local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        return string.IsNullOrEmpty(local) ? null : Path.Combine(local, "ForestOverlay", "game-src", "Assembly-CSharp");
    }

    private static int Int(string s, int fallback) => int.TryParse(s, out int v) ? v : fallback;
}
