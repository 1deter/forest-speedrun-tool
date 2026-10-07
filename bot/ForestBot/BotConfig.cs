namespace ForestBot;

// ------------------------------------------------------------------
// Settings, all from the environment (bot/README.md lists them). Secrets
// (tokens, API keys) are read here and never logged.
// ------------------------------------------------------------------
public sealed class BotConfig
{
    public string DiscordToken;
    /// A folder laid out like the repo: knowledge/, docs/ (the build copies them to kb/).
    public string KnowledgeRoot;
    /// The decompiled Assembly-CSharp folder (private); empty = no code tools.
    public string CodeRoot;
    /// bot.db, the vector cache, logs.
    public string DataDir;
    /// model.onnx + vocab.txt; empty = keyword search only.
    public string EmbedModelDir;
    /// The settings the site's Bot tab can change, as ONE immutable snapshot: a poll builds
    /// a new one aside and swaps the reference, so a message never sees a half-applied mix.
    public sealed class LiveSettings
    {
        /// "gemini:gemini-3.8-flash,mistral:mistral-medium-latest" - order = preference.
        public string Models;
        public string ThinkingLevel;
        /// Channel ids the bot answers in. With AllChannels it is empty and the bot answers
        /// everywhere it can read (the .env default); without it an empty set = no channel.
        public IReadOnlySet<ulong> Channels = new HashSet<ulong>();
        public bool AllChannels = true;
        public bool AllowDms = true;
        /// Where research-queue items are posted (0 = only stored).
        public ulong QueueChannel;
        public int PerHour = 15;
        public int PerDay = 60;
        /// The revision of the site's settings applied (0 = the .env values only).
        public long Rev;
    }

    private volatile LiveSettings _live = new LiveSettings();
    public LiveSettings Live => _live;

    public string Models => _live.Models;
    public string ThinkingLevel => _live.ThinkingLevel;
    public IReadOnlySet<ulong> Channels => _live.Channels;
    public bool AllChannels => _live.AllChannels;
    public bool AllowDms => _live.AllowDms;
    public ulong QueueChannel => _live.QueueChannel;
    public int PerHour => _live.PerHour;
    public int PerDay => _live.PerDay;
    public long SettingsRev => _live.Rev;

    /// A guild to register /ask in instantly (testing); 0 = global (takes up to an hour the first time).
    public ulong TestGuild;

    public Func<string, string> Env = Environment.GetEnvironmentVariable;

    public static BotConfig FromEnvironment()
    {
        BotConfig c = new BotConfig();
        string baseDir = AppContext.BaseDirectory;
        c.DiscordToken = c.Get("FOREST_BOT_DISCORD_TOKEN");
        c._live = c.EnvDefaults();
        c.KnowledgeRoot = c.Get("FOREST_BOT_KNOWLEDGE") ?? Path.Combine(baseDir, "kb");
        c.DataDir = c.Get("FOREST_BOT_DATA") ?? Path.Combine(baseDir, "data");
        c.CodeRoot = c.Get("FOREST_BOT_CODE") ?? DefaultCodeRoot();
        c.EmbedModelDir = c.Get("FOREST_BOT_EMBED_MODEL") ?? Path.Combine(c.DataDir, "embed");
        ulong.TryParse(c.Get("FOREST_BOT_TEST_GUILD"), out c.TestGuild);
        return c;
    }

    /// The .env values (the defaults), as a fresh snapshot.
    private LiveSettings EnvDefaults()
    {
        HashSet<ulong> channels = new HashSet<ulong>();
        foreach (string s in (Get("FOREST_BOT_CHANNELS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (ulong.TryParse(s, out ulong id)) channels.Add(id);
        ulong.TryParse(Get("FOREST_BOT_QUEUE_CHANNEL"), out ulong queue);
        return new LiveSettings
        {
            Models = Get("FOREST_BOT_MODELS") ?? "gemini:gemini-3.8-flash,gemini:gemini-3.5-flash-lite,mistral:mistral-medium-latest",
            ThinkingLevel = Get("FOREST_BOT_THINKING"),
            Channels = channels, AllChannels = channels.Count == 0,
            AllowDms = Get("FOREST_BOT_DMS") != "off",
            QueueChannel = queue,
            PerHour = Int(Get("FOREST_BOT_PER_HOUR"), 15), PerDay = Int(Get("FOREST_BOT_PER_DAY"), 60),
        };
    }

    /// Applies the site's settings over the .env defaults, live (no restart): the new values
    /// are built aside and swapped in as one snapshot. A setting the site does not have (or
    /// holds an unusable value for) keeps its .env value. Channels: a `channels` list in the
    /// saved settings is the whole answer - an empty list is NO channel (DMs follow `dms`);
    /// settings never saved (no `channels` key) keep the .env channels.
    public void ApplySettings(System.Text.Json.Nodes.JsonObject s, long rev)
    {
        LiveSettings n = EnvDefaults();
        if (s["channels"] is System.Text.Json.Nodes.JsonArray list)
        {
            HashSet<ulong> channels = new HashSet<ulong>();
            foreach (var item in list)
                if (item is System.Text.Json.Nodes.JsonValue v && ulong.TryParse(v.ToString().Trim('"'), out ulong id)) channels.Add(id);
            n.Channels = channels;
            n.AllChannels = false;
        }
        if (s["dms"] is System.Text.Json.Nodes.JsonValue d && d.TryGetValue(out bool dms)) n.AllowDms = dms;
        if (s["perHour"] is System.Text.Json.Nodes.JsonValue h && h.TryGetValue(out int ph) && ph > 0) n.PerHour = ph;
        if (s["perDay"] is System.Text.Json.Nodes.JsonValue pd && pd.TryGetValue(out int day) && day > 0) n.PerDay = day;
        if (s["models"] is System.Text.Json.Nodes.JsonValue m && m.TryGetValue(out string models) && !string.IsNullOrWhiteSpace(models)) n.Models = models;
        if (s["thinking"] is System.Text.Json.Nodes.JsonValue t && t.TryGetValue(out string th) && !string.IsNullOrWhiteSpace(th)) n.ThinkingLevel = th;
        if (s["queueChannel"] is System.Text.Json.Nodes.JsonValue q && ulong.TryParse(q.ToString().Trim('"'), out ulong qc) && qc != 0) n.QueueChannel = qc;
        n.Rev = rev;
        _live = n;
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
