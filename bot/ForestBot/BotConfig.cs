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

    public Func<string, string> Env = Environment.GetEnvironmentVariable;

    public static BotConfig FromEnvironment()
    {
        BotConfig c = new BotConfig();
        string baseDir = AppContext.BaseDirectory;
        c.DiscordToken = c.Get("FOREST_BOT_DISCORD_TOKEN");
        c.Models = c.Get("FOREST_BOT_MODELS") ?? "gemini:gemini-3.8-flash,gemini:gemini-3.5-flash-lite,mistral:mistral-medium-latest";
        c.ThinkingLevel = c.Get("FOREST_BOT_THINKING");
        c.KnowledgeRoot = c.Get("FOREST_BOT_KNOWLEDGE") ?? Path.Combine(baseDir, "kb");
        c.DataDir = c.Get("FOREST_BOT_DATA") ?? Path.Combine(baseDir, "data");
        c.CodeRoot = c.Get("FOREST_BOT_CODE") ?? DefaultCodeRoot();
        c.EmbedModelDir = c.Get("FOREST_BOT_EMBED_MODEL") ?? Path.Combine(c.DataDir, "embed");
        foreach (string s in (c.Get("FOREST_BOT_CHANNELS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (ulong.TryParse(s, out ulong id)) c.Channels.Add(id);
        c.AllowDms = c.Get("FOREST_BOT_DMS") != "off";
        ulong.TryParse(c.Get("FOREST_BOT_QUEUE_CHANNEL"), out c.QueueChannel);
        ulong.TryParse(c.Get("FOREST_BOT_TEST_GUILD"), out c.TestGuild);
        c.PerHour = Int(c.Get("FOREST_BOT_PER_HOUR"), 15);
        c.PerDay = Int(c.Get("FOREST_BOT_PER_DAY"), 60);
        return c;
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
