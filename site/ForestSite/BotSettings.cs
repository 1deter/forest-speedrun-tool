using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ForestSite;

// ------------------------------------------------------------------
// The knowledge bot's settings (docs/knowledge-bot.md *Bot settings page*,
// T-0028). The owner edits them on /admin; the bot polls GET /api/bot/settings
// with its own token (FOREST_BOT_TOKEN) and applies them without a restart.
// The bot also reports back (POST /api/bot/report): its version, which
// revision it applied, and the channels it can see (the page lists them by
// name). Secrets (the Discord token, model keys) never come through here.
//
// A field left out means "the bot's .env default"; `channels` once saved is the whole
// list (an empty one = no channel). Stored as one JSON text.
// ------------------------------------------------------------------
public sealed class BotSettings
{
    private static readonly Regex Snowflake = new(@"\A[0-9]{1,20}\z", RegexOptions.Compiled);
    private static readonly Regex ModelList = new(@"\A[A-Za-z0-9:._,/-]{1,300}\z", RegexOptions.Compiled);

    public const int MaxChannels = 50, MaxReported = 500;

    private readonly Store _store;

    public BotSettings(Store store)
    {
        _store = store;
        _store.Update("CREATE TABLE IF NOT EXISTS bot_settings (k TEXT PRIMARY KEY, v TEXT NOT NULL)");
    }

    private string Get(string k) => _store.Scalar("SELECT v FROM bot_settings WHERE k = $k", ("$k", k)) as string;
    private void Put(string k, string v) =>
        _store.Update("INSERT INTO bot_settings (k, v) VALUES ($k, $v) ON CONFLICT(k) DO UPDATE SET v = $v", ("$k", k), ("$v", v));

    /// The saved settings (an empty object before the first save) and their revision.
    public (JsonObject settings, long rev) Current()
    {
        string text = Get("settings");
        JsonObject o = null;
        try { o = text == null ? null : JsonNode.Parse(text) as JsonObject; } catch { }
        long.TryParse(Get("rev"), NumberStyles.None, CultureInfo.InvariantCulture, out long rev);
        return (o ?? new JsonObject(), rev);
    }

    /// Validates and saves; returns the new revision, or an error message.
    public (long rev, string error) Save(string body)
    {
        JsonObject input;
        try { input = JsonNode.Parse(body ?? "") as JsonObject; } catch { input = null; }
        if (input == null) return (0, "expected a JSON object");

        var clean = new JsonObject();
        foreach (var kv in input)
        {
            JsonNode v = kv.Value;
            switch (kv.Key)
            {
                case "channels":
                    if (v is not JsonArray a || a.Count > MaxChannels) return (0, "channels: a list of at most " + MaxChannels);
                    var ids = new JsonArray();
                    foreach (var n in a)
                    {
                        string id = Text(n);
                        if (id == null || !Snowflake.IsMatch(id)) return (0, "channels: ids are digits");
                        if (!ids.Any(x => Text(x) == id)) ids.Add(id);
                    }
                    clean["channels"] = ids;
                    break;
                case "dms":
                    if (v is not JsonValue dv || !dv.TryGetValue(out bool dms)) return (0, "dms: true or false");
                    clean["dms"] = dms;
                    break;
                case "perHour": case "perDay":
                    int max = kv.Key == "perHour" ? 1000 : 10000;
                    if (v == null) break;
                    if (v is not JsonValue nv || !nv.TryGetValue(out int count) || count < 1 || count > max) return (0, kv.Key + ": 1 to " + max);
                    clean[kv.Key] = count;
                    break;
                case "models":
                    string models = v == null ? "" : Text(v);
                    if (models == null || (models.Length > 0 && !ModelList.IsMatch(models))) return (0, "models: provider:model names, comma separated");
                    if (models.Length > 0) clean["models"] = models;
                    break;
                case "thinking":
                    string t = v == null ? "" : Text(v);
                    if (t is not ("" or "low" or "high")) return (0, "thinking: low or high");
                    if (t.Length > 0) clean["thinking"] = t;
                    break;
                case "queueChannel":
                    string q = v == null ? "" : Text(v);
                    if (q == null || (q.Length > 0 && !Snowflake.IsMatch(q))) return (0, "queueChannel: a channel id");
                    if (q.Length > 0) clean["queueChannel"] = q;
                    break;
                default:
                    return (0, "unknown setting: " + (kv.Key.Length > 30 ? kv.Key.Substring(0, 30) : kv.Key));
            }
        }
        Put("settings", clean.ToJsonString());
        long.TryParse(Get("rev"), NumberStyles.None, CultureInfo.InvariantCulture, out long rev);
        rev++;
        Put("rev", rev.ToString(CultureInfo.InvariantCulture));
        return (rev, null);
    }

    private static string Text(JsonNode n) => n is JsonValue v && v.TryGetValue(out string s) ? s
        : n is JsonValue v2 && v2.TryGetValue(out long l) ? l.ToString(CultureInfo.InvariantCulture) : null;

    /// The bot's report: version, the revision it applied, the channels it sees.
    public string SaveReport(string body)
    {
        JsonObject o;
        try { o = JsonNode.Parse(body ?? "") as JsonObject; } catch { o = null; }
        if (o == null) return "expected a JSON object";
        string version = Clip(Text(o["version"]), 40);
        long.TryParse(Text(o["rev"]) ?? "0", NumberStyles.None, CultureInfo.InvariantCulture, out long rev);
        var channels = new JsonArray();
        if (o["channels"] is JsonArray list)
            foreach (var c in list.Take(MaxReported))
            {
                string id = Text(c?["id"]);
                if (id == null || !Snowflake.IsMatch(id)) continue;
                channels.Add(new JsonObject
                {
                    ["id"] = id, ["name"] = Clip(Text(c["name"]), 100), ["guild"] = Clip(Text(c["guild"]), 100),
                });
            }
        // When the bot applied that revision (its own clock); anything unparsable = unknown.
        string applied = DateTimeOffset.TryParse(Text(o["appliedAt"]), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when)
            ? when.UtcDateTime.ToString("o", CultureInfo.InvariantCulture) : "";
        var answersIn = new JsonArray();
        if (o["answersIn"] is JsonArray ans)
            foreach (var a in ans.Take(MaxReported))
                if (Text(a) is { } aid && Snowflake.IsMatch(aid)) answersIn.Add(aid);
        bool allChannels = o["allChannels"] is JsonValue av && av.TryGetValue(out bool all) && all;
        var report = new JsonObject
        {
            ["allChannels"] = allChannels, ["answersIn"] = answersIn,
            ["at"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), ["version"] = version, ["rev"] = rev,
            ["appliedAt"] = applied, ["channels"] = channels,
        };
        Put("report", report.ToJsonString());
        return null;
    }

    private static string Clip(string s, int max) => s == null ? "" : s.Length > max ? s.Substring(0, max) : s;

    public JsonNode Report()
    {
        string text = Get("report");
        try { return text == null ? null : JsonNode.Parse(text); } catch { return null; }
    }
}
