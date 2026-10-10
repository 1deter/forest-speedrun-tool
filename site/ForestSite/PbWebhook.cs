using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace ForestSite;

// ------------------------------------------------------------------
// A Discord post when an uploaded run sets a runner's new PB on a
// community spot or a run spot (backlog: author, QA #general 2026-10-02).
// FOREST_DISCORD_WEBHOOK (env / appsettings) is the webhook's URL; unset =
// off. PBs on runners' own spots too when the owner switches them on in
// /admin's PB posts tab, in that channel or their own (PbSettings, T-0232).
// Uploads only queue a line here: a slow, failing or rate-limited
// webhook never fails or holds up an upload (docs/website.md *Discord*).
// ------------------------------------------------------------------

/// The decisions, pure (tested).
public static class PbNews
{
    /// A new PB in this upload: the fastest attempt it added, when that is
    /// faster than the runner's best before it (or the runner had none) and
    /// it is not under review. A flagged fastest attempt posts nothing - not
    /// even a slower one beside it (the runner's best is the flagged one).
    public static float? NewPb(float previousBest, IEnumerable<(float duration, bool flagged)> added)
    {
        float best = float.PositiveInfinity;
        bool flagged = false;
        foreach (var (d, f) in added)
            if (d > 0f && d < best) { best = d; flagged = f; }
        if (float.IsPositiveInfinity(best) || flagged) return null;
        if (!float.IsNaN(previousBest) && !(best < previousBest)) return null;
        return best;
    }

    /// Which spots are announced: a community route, or a spot whose
    /// `run = ` names a published run category (a run spot). A runner's
    /// own practice spot is not by default - anyone can make one, so it is a
    /// way to post anything into the channel; the owner can switch those on
    /// (PbSettings), and the caps still hold.
    public static bool Announces(bool communityRoute, string runCategory, Func<string, bool> published) =>
        communityRoute || (!string.IsNullOrWhiteSpace(runCategory) && published != null && published(runCategory.Trim()));

    /// The post: plain text, one line + the link.
    public static string Message(string runner, string spot, float time, float previousBest, string url)
    {
        var sb = new StringBuilder();
        // Clipped as the site stores them (a spot's name in an upload is not
        // clipped before this): a long one would push the post past
        // Discord's 2,000 characters and it would be refused.
        sb.Append(Escape(Clip(runner, 40))).Append(float.IsNaN(previousBest) ? " finished " : " set a new PB on ").Append(Escape(Clip(spot, 80))).Append(": ")
          .Append(Time(time));
        if (float.IsNaN(previousBest)) sb.Append(" (their first run)");
        else sb.Append(" (").Append(Time(previousBest - time)).Append(" faster than ").Append(Time(previousBest)).Append(')');
        return sb.Append('\n').Append(url).ToString();
    }

    /// Data/SplitTable's format, three decimals: 9.500, 1:02.345, 1:02:03.456.
    public static string Time(double seconds)
    {
        long ms = (long)Math.Round(Math.Abs(seconds) * 1000.0);
        long whole = ms / 1000, frac = ms % 1000;
        long h = whole / 3600, m = whole / 60 % 60, s = whole % 60;
        string text = h > 0 ? h + ":" + m.ToString("00") + ":" + s.ToString("00")
                    : m > 0 ? m + ":" + s.ToString("00") : s.ToString(CultureInfo.InvariantCulture);
        return text + "." + frac.ToString("000");
    }

    private static string Clip(string s, int n)
    {
        s = (s ?? "").Trim();
        return s.Length > n ? s.Substring(0, n) : s;
    }

    /// A runner's text as plain text in Discord: markdown and masked links
    /// escaped. Mentions are switched off in the post itself (allowed_mentions).
    public static string Escape(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s ?? "")
        {
            if (c == '\n' || c == '\r') { sb.Append(' '); continue; }
            if ("\\*_~`|>[]()#<:-".IndexOf(c) >= 0) sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// The page the post links: the spot at that route, the run focused.
    public static string RunLink(string siteUrl, string segmentId, string route, long runId) =>
        SiteUrl(siteUrl) + "/spot/" + Uri.EscapeDataString(segmentId) + "/" + Uri.EscapeDataString(route) + "?run=" + runId;

    public static string SiteUrl(string url) => string.IsNullOrWhiteSpace(url) ? "https://forest.deter.cloud" : url.Trim().TrimEnd('/');

    /// Where a PB goes: the webhook URL, or null for no post. A community /
    /// run spot's goes to the official webhook (empty = off); a runner's own
    /// spot's only when the owner switched those on - to the same webhook,
    /// or to their own one.
    public static string Target(bool official, PbSettings.Values s, string officialUrl)
    {
        string url = official ? officialUrl : !s.RunnerSpots ? null : s.OwnChannel ? s.RunnerSpotsWebhook : officialUrl;
        return string.IsNullOrWhiteSpace(url) ? null : url.Trim();
    }
}

/// The sender: a small queue, one post at a time, spaced and capped.
public sealed class PbWebhook
{
    public const int QueueSize = 20;
    public const int PerHour = 30;
    /// One runner's posts an hour, on top of PerHour (security audit,
    /// 2026-10-04): any registered runner finishing community / run spots
    /// could otherwise fill the channel with their own name. A real runner
    /// sets a few PBs an hour at most.
    public const int PerRunnerPerHour = 5;

    private readonly Channel<(string url, string message)> _queue = Channel.CreateBounded<(string, string)>(new BoundedChannelOptions(QueueSize)
        { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    private readonly Queue<DateTime> _sent = new();
    private readonly Dictionary<string, Queue<DateTime>> _byRunner = new();

    /// What this sender posts, for its log lines ("Discord webhook" /
    /// "Discord webhook (runners' spots)"). Never the URL: it is a secret.
    public string Name { get; }
    /// The webhook's URL a post goes to when Enqueue names none; empty = off.
    public string Url { get; set; }
    /// The site's public address, for the run links.
    public string SiteUrl { get; set; }
    /// Where warnings go (Program sets the app's logger).
    public Action<string> Log { get; set; } = _ => { };
    /// The HTTP client the posts go through (tests give it a fake handler).
    public HttpClient Http { get; set; } = new() { Timeout = TimeSpan.FromSeconds(10) };
    /// The pause after each post (Discord's rate limits; tests shorten it).
    public TimeSpan Gap { get; set; } = TimeSpan.FromSeconds(2);
    /// The last messages queued, newest last (tests, diagnostics).
    public readonly List<string> Recent = new();
    /// The clock for the per-runner cap (tests move it).
    public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    public bool On => !string.IsNullOrWhiteSpace(Url);

    public PbWebhook(string url, string siteUrl, string name = "Discord webhook")
    {
        Url = url ?? "";
        SiteUrl = PbNews.SiteUrl(siteUrl);
        Name = name;
        _ = Task.Run(Loop);
    }

    /// Queues a post to `url` (default: Url); false when there is no URL,
    /// the runner (their id, when given) has had PerRunnerPerHour posts in
    /// the last hour, or the queue is full (dropped, logged).
    public bool Enqueue(string message, string runner = null, string url = null)
    {
        url = string.IsNullOrWhiteSpace(url) ? Url : url.Trim();
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (runner != null && !RunnerSlot(runner))
        {
            Log(Name + ": runner " + runner + " had " + PerRunnerPerHour + " posts in the last hour, one dropped.");
            return false;
        }
        lock (Recent)
        {
            Recent.Add(message);
            if (Recent.Count > QueueSize) Recent.RemoveAt(0);
        }
        if (_queue.Writer.TryWrite((url, message))) return true;
        Log(Name + ": queue full, a PB post dropped.");
        return false;
    }

    /// Takes one of the runner's posts this hour; false when they have none
    /// left. Runners with nothing in the last hour are forgotten.
    private bool RunnerSlot(string runner)
    {
        DateTime now = Now();
        lock (_byRunner)
        {
            foreach (string r in _byRunner.Where(kv => now - kv.Value.Last() > TimeSpan.FromHours(1)).Select(kv => kv.Key).ToList())
                _byRunner.Remove(r);
            if (!_byRunner.TryGetValue(runner, out var times)) _byRunner[runner] = times = new Queue<DateTime>();
            while (times.Count > 0 && now - times.Peek() > TimeSpan.FromHours(1)) times.Dequeue();
            if (times.Count >= PerRunnerPerHour) return false;
            times.Enqueue(now);
            return true;
        }
    }

    private async Task Loop()
    {
        await foreach (var (url, message) in _queue.Reader.ReadAllAsync())
        {
            try { await Deliver(url, message); }
            catch (Exception ex) { Log(Name + ": " + ex.Message); }
        }
    }

    private async Task Deliver(string url, string message)
    {
        // At most PerHour posts an hour; past that, dropped (a flood of PBs
        // is either a bug or abuse - the site keeps every run anyway).
        DateTime now = DateTime.UtcNow;
        while (_sent.Count > 0 && now - _sent.Peek() > TimeSpan.FromHours(1)) _sent.Dequeue();
        if (_sent.Count >= PerHour) { Log(Name + ": " + PerHour + " posts in the last hour, one dropped."); return; }
        _sent.Enqueue(now);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            var (status, retryAfter) = await Post(url, message);
            if (status >= 200 && status < 300) break;
            if (status == 429 && attempt == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(retryAfter, 1, 60)));
                continue;
            }
            Log(Name + ": post failed (" + (status == 0 ? "no answer: " + _lastError : "HTTP " + status) + "), dropped.");
            break;
        }
        await Task.Delay(Gap);
    }

    private string _lastError = "";

    /// Posts one message: the HTTP status (0 = no answer) and Discord's
    /// retry-after in seconds for a 429.
    private async Task<(int, double)> Post(string url, string message)
    {
        // allowed_mentions: none - a runner named "@everyone" pings nobody.
        var body = new JsonObject { ["content"] = message, ["allowed_mentions"] = new JsonObject { ["parse"] = new JsonArray() } };
        try
        {
            using var r = await Http.PostAsJsonAsync(url, body);
            double retry = 0;
            if ((int)r.StatusCode == 429)
            {
                if (r.Headers.RetryAfter?.Delta is TimeSpan d) retry = d.TotalSeconds;
                else if (double.TryParse(r.Headers.TryGetValues("Retry-After", out var v) ? v.FirstOrDefault() : null,
                                         NumberStyles.Float, CultureInfo.InvariantCulture, out double s)) retry = s;
            }
            return ((int)r.StatusCode, retry);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            return (0, 0);
        }
    }
}
