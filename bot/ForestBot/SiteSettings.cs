using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ForestBot;

/// A channel the bot can see, as it reports it to the site.
public sealed record SeenChannel(ulong Id, string Name, string Guild);

// ------------------------------------------------------------------
// Live settings from the site's Bot tab (docs/knowledge-bot.md *Bot settings
// page*, T-0028). About once a minute the bot reads GET <site>/api/bot/settings
// with its own token (X-Bot-Token) and applies the answer to BotConfig with
// no restart. A fetch that fails keeps what it had; the last good answer is
// cached in DataDir (site-settings.json) and used at start, before the first
// fetch. A setting the site does not have = the .env value. It also reports
// (POST /api/bot/report): its version, the revision it runs and the channels
// it can see, so the page lists them by name and shows what is applied.
// Secrets never pass through here.
// ------------------------------------------------------------------
public sealed class SiteSettings
{
    public const string CacheFile = "site-settings.json";
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly BotConfig _cfg;
    private readonly HttpClient _http;
    private readonly string _siteUrl, _token, _version;
    private readonly Action<string> _log;

    /// Called after settings were applied (the models are rebuilt from the new order).
    public Action Applied = () => { };

    /// The revision running now (0 = none from the site).
    public long Rev { get; private set; }

    public SiteSettings(BotConfig cfg, HttpClient http, string siteUrl, string token, string version, Action<string> log)
    {
        _cfg = cfg;
        _http = http;
        _siteUrl = (siteUrl ?? "").TrimEnd('/');
        _token = token;
        _version = version ?? "";
        _log = log ?? (_ => { });
    }

    /// Null when FOREST_BOT_TOKEN is not set (the .env values rule alone).
    public static SiteSettings FromConfig(BotConfig cfg, HttpClient http, string version, Action<string> log)
    {
        string token = cfg.Get("FOREST_BOT_TOKEN");
        if (token == null) return null;
        return new SiteSettings(cfg, http, cfg.Get("FOREST_BOT_SITE_URL") ?? "https://forest.deter.cloud", token, version, log);
    }

    private string CachePath => Path.Combine(_cfg.DataDir, CacheFile);

    /// Applies the cached last good settings (at start). False when there are none.
    public bool LoadCached()
    {
        try
        {
            if (!File.Exists(CachePath)) return false;
            return Apply(File.ReadAllText(CachePath), false);
        }
        catch (Exception e) { _log("Site settings: cache unreadable (" + e.Message + ") - .env values"); return false; }
    }

    /// One poll. True when the site answered with settings (applied, and cached).
    public async Task<bool> PollAsync(CancellationToken ct)
    {
        try
        {
            using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, _siteUrl + "/api/bot/settings");
            req.Headers.Add("X-Bot-Token", _token);
            using HttpResponseMessage res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) { _log("Site settings: the site answered " + (int)res.StatusCode + " - keeping the last ones"); return false; }
            string text = await res.Content.ReadAsStringAsync(ct);
            if (!Apply(text, true)) return false;
            Directory.CreateDirectory(_cfg.DataDir);
            string tmp = CachePath + ".tmp";
            File.WriteAllText(tmp, text);
            File.Move(tmp, CachePath, true);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) { _log("Site settings: fetch failed (" + e.Message + ") - keeping the last ones"); return false; }
    }

    /// Parses `{rev, settings}` and applies it. False (nothing changed) when it does not parse.
    private bool Apply(string text, bool announce)
    {
        JsonObject o;
        try { o = JsonNode.Parse(text) as JsonObject; } catch { o = null; }
        if (o == null || o["settings"] is not JsonObject settings) { _log("Site settings: unreadable answer - keeping the last ones"); return false; }
        long rev = o["rev"] is JsonValue v && v.TryGetValue(out long r) ? r : 0;
        bool changed = rev != Rev || !announce;
        _cfg.ApplySettings(settings, rev);
        Rev = rev;
        if (changed)
        {
            if (announce) _log("Site settings: revision " + rev + " applied");
            Applied();
        }
        return true;
    }

    /// Tells the site what runs: version, revision, the channels it sees.
    public async Task ReportAsync(IEnumerable<SeenChannel> channels, CancellationToken ct)
    {
        try
        {
            var body = new
            {
                version = _version, rev = Rev,
                channels = channels.Select(c => new { id = c.Id.ToString(), name = c.Name, guild = c.Guild }).ToList(),
            };
            using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, _siteUrl + "/api/bot/report") { Content = JsonContent.Create(body) };
            req.Headers.Add("X-Bot-Token", _token);
            using HttpResponseMessage res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) _log("Site settings: report refused (" + (int)res.StatusCode + ")");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) { _log("Site settings: report failed (" + e.Message + ")"); }
    }

    /// Poll, then report, every minute until stopped. `seen` is null / empty until Discord is connected
    /// (an empty list is not reported: it would blank the page's channel list).
    public async Task RunAsync(Func<IReadOnlyList<SeenChannel>> seen, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await PollAsync(ct);
            IReadOnlyList<SeenChannel> list = seen();
            if (list != null && list.Count > 0) await ReportAsync(list, ct);
            try { await Task.Delay(Interval, ct); } catch (OperationCanceledException) { return; }
        }
    }
}
