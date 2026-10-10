using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ForestSite;

// ------------------------------------------------------------------
// PB posts from runners' own spots (T-0232; author, 2026-10-08: "site
// toggles for runner-spot PB posts, plus a choice of whether they go to the
// same channel as official-run PBs or a separate one"). The owner sets them
// on /admin's PB posts tab (GET / PUT /api/admin/pbposts, owner only); one
// JSON in `pb_settings`, a revision per save, like BotSettings. Off until
// saved on. The official channel stays FOREST_DISCORD_WEBHOOK (.env).
//
// The own channel's webhook URL is a secret: it is shown to the owner's
// page only, never logged (the Activity log names the request path, not
// its body), and only a discord.com webhook address is accepted - the site
// posts to no other host (no SSRF through the admin page).
// ------------------------------------------------------------------
public sealed class PbSettings
{
    /// https://discord.com/api/webhooks/<id>/<token> (also discordapp.com,
    /// the ptb. / canary. hosts and an /api/v10 path - what Discord's
    /// "Copy Webhook URL" gives).
    private static readonly Regex DiscordWebhook = new(
        @"\Ahttps://(?:(?:ptb|canary)\.)?discord(?:app)?\.com/api(?:/v[0-9]{1,2})?/webhooks/[0-9]{1,20}/[A-Za-z0-9_-]{1,100}\z",
        RegexOptions.Compiled);

    /// What the owner chose. RunnerSpots: post PBs on runners' own spots;
    /// OwnChannel: to RunnerSpotsWebhook instead of the official channel.
    public sealed record Values(bool RunnerSpots, bool OwnChannel, string RunnerSpotsWebhook)
    {
        public static readonly Values Off = new(false, false, "");
    }

    private readonly Store _store;
    private Values _current;

    public PbSettings(Store store)
    {
        _store = store;
        _store.Update("CREATE TABLE IF NOT EXISTS pb_settings (k TEXT PRIMARY KEY, v TEXT NOT NULL)");
        _current = Load();
    }

    private string Get(string k) => _store.Scalar("SELECT v FROM pb_settings WHERE k = $k", ("$k", k)) as string;
    private void Put(string k, string v) =>
        _store.Update("INSERT INTO pb_settings (k, v) VALUES ($k, $v) ON CONFLICT(k) DO UPDATE SET v = $v", ("$k", k), ("$v", v));

    /// The settings in force (read on every PB, kept in memory).
    public Values Current => _current;

    public long Rev
    {
        get
        {
            long.TryParse(Get("rev"), NumberStyles.None, CultureInfo.InvariantCulture, out long rev);
            return rev;
        }
    }

    private Values Load()
    {
        JsonObject o = null;
        try { o = Get("settings") is { } text ? JsonNode.Parse(text) as JsonObject : null; } catch { }
        if (o == null) return Values.Off;
        return new Values(Bool(o["runnerSpots"]), Str(o["channel"]) == "own", Str(o["webhook"]) ?? "");
    }

    /// The page's JSON: { runnerSpots, channel: "same" | "own", webhook }.
    /// Only for the owner (it holds the secret).
    public static JsonObject ToJson(Values v) => new()
    {
        ["runnerSpots"] = v.RunnerSpots, ["channel"] = v.OwnChannel ? "own" : "same", ["webhook"] = v.RunnerSpotsWebhook,
    };

    /// Validates and saves { runnerSpots, channel, webhook }; returns the new
    /// revision, or an error message (nothing saved). A webhook is kept while
    /// the posts are off or go to the same channel, so switching back does
    /// not mean pasting it again; their own channel needs one.
    public (long rev, string error) Save(string body)
    {
        JsonObject input;
        try { input = JsonNode.Parse(body ?? "") as JsonObject; } catch { input = null; }
        if (input == null) return (0, "expected a JSON object");
        foreach (var kv in input)
            if (kv.Key is not ("runnerSpots" or "channel" or "webhook"))
                return (0, "unknown setting: " + (kv.Key.Length > 30 ? kv.Key.Substring(0, 30) : kv.Key));

        if (input["runnerSpots"] is not JsonValue rv || !rv.TryGetValue(out bool on)) return (0, "runnerSpots: true or false");
        string channel = Str(input["channel"]) ?? "same";
        if (channel is not ("same" or "own")) return (0, "channel: same or own");
        string webhook = (Str(input["webhook"]) ?? "").Trim();
        if (webhook.Length > 0 && !DiscordWebhook.IsMatch(webhook))
            return (0, "webhook: a Discord webhook URL (https://discord.com/api/webhooks/...)");
        if (on && channel == "own" && webhook.Length == 0) return (0, "webhook: their own channel needs a webhook URL");

        var v = new Values(on, channel == "own", webhook);
        Put("settings", ToJson(v).ToJsonString());
        long rev = Rev + 1;
        Put("rev", rev.ToString(CultureInfo.InvariantCulture));
        _current = v;
        return (rev, null);
    }

    private static bool Bool(JsonNode n) => n is JsonValue v && v.TryGetValue(out bool b) && b;
    private static string Str(JsonNode n) => n is JsonValue v && v.TryGetValue(out string s) ? s : null;
}

/// The two senders and the choice between them. Each kind has its own
/// queue and caps, so a flood of runners' spot PBs never crowds out a
/// community / run spot's post - even when both go to one channel.
public sealed class PbPosts
{
    public PbWebhook Official { get; }
    public PbWebhook RunnerSpots { get; }
    public PbSettings Settings { get; }

    public PbPosts(PbWebhook official, PbWebhook runnerSpots, PbSettings settings)
    {
        Official = official;
        RunnerSpots = runnerSpots;
        Settings = settings;
    }

    /// Queues the PB's post where the settings send it; false when it is
    /// not posted (off, capped, queue full).
    public bool Post(Runs.PbFound pb, string runnerId)
    {
        string url = PbNews.Target(pb.Official, Settings.Current, Official.Url);
        if (url == null) return false;
        PbWebhook sender = pb.Official ? Official : RunnerSpots;
        var message = PbNews.Embed(pb, PbNews.RunLink(sender.SiteUrl, pb.Segment, pb.Route, pb.RunId), DateTime.UtcNow);
        return sender.Enqueue(message, runnerId, url);
    }

    /// The startup line's word for runners' spots: off / same channel / own channel.
    public string RunnerSpotsState()
    {
        var s = Settings.Current;
        return !s.RunnerSpots ? "off" : s.OwnChannel ? "own channel" : "same channel";
    }
}
