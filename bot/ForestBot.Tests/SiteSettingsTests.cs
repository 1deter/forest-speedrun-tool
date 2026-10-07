using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ForestBot.Llm;
using Xunit;

namespace ForestBot.Tests;

// The site's live bot settings (T-0028): applied with no restart, the last good ones
// cached and used at start, the .env values when the site has none or fails, and the
// report of version / revision / channels.
public class SiteSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "forest-bot-test-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string> _env = new Dictionary<string, string>();
    private readonly List<string> _log = new List<string>();

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private BotConfig Config()
    {
        _env["FOREST_BOT_CHANNELS"] = "100,200";
        _env["FOREST_BOT_PER_HOUR"] = "15";
        _env["FOREST_BOT_MODELS"] = "gemini:flash";
        BotConfig c = new BotConfig { DataDir = _dir, Env = n => _env.TryGetValue(n, out string v) ? v : null };
        c.ApplySettings(new JsonObject(), 0);   // the .env defaults
        return c;
    }

    private sealed class FakeSite : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = """{"rev":1,"settings":{}}""";
        public bool Throw;
        public readonly List<(HttpMethod method, string path, string token, string body)> Seen = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add((request.Method, request.RequestUri.AbsolutePath, request.Headers.TryGetValues("X-Bot-Token", out var t) ? t.First() : null,
                request.Content == null ? null : await request.Content.ReadAsStringAsync(ct)));
            if (Throw) throw new HttpRequestException("down");
            return new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        }
    }

    private SiteSettings Site(BotConfig c, FakeSite fake) =>
        new SiteSettings(c, new HttpClient(fake), "https://site.test/", "bot-secret", "9.9", _log.Add);

    [Fact]
    public void Env_values_when_the_site_has_nothing()
    {
        BotConfig c = Config();
        Assert.Equal(new ulong[] { 100, 200 }, c.Channels.OrderBy(x => x));
        Assert.Equal(15, c.PerHour);
        Assert.True(c.AllowDms);
        Assert.Equal("gemini:flash", c.Models);
    }

    [Fact]
    public async Task Poll_applies_live_with_the_bot_token_and_caches()
    {
        BotConfig c = Config();
        FakeSite fake = new FakeSite
        {
            Body = """{"rev":4,"settings":{"channels":["300"],"dms":false,"perHour":3,"models":"gemini:pro,mistral:m","thinking":"low","queueChannel":"555"}}""",
        };
        SiteSettings site = Site(c, fake);
        int applied = 0;
        site.Applied = () => applied++;

        Assert.True(await site.PollAsync(CancellationToken.None));
        Assert.Equal(("/api/bot/settings", "bot-secret"), (fake.Seen[0].path, fake.Seen[0].token));
        Assert.Equal(new ulong[] { 300 }, c.Channels);
        Assert.False(c.AllowDms);
        Assert.Equal(3, c.PerHour);
        Assert.Equal(60, c.PerDay);   // not set on the site: the default
        Assert.Equal("gemini:pro,mistral:m", c.Models);
        Assert.Equal("low", c.ThinkingLevel);
        Assert.Equal(555UL, c.QueueChannel);
        Assert.Equal(4, site.Rev);
        Assert.Equal(1, applied);
        Assert.True(File.Exists(Path.Combine(_dir, SiteSettings.CacheFile)));

        // The same revision again: nothing to re-announce (no model rebuild).
        Assert.True(await site.PollAsync(CancellationToken.None));
        Assert.Equal(1, applied);

        // A setting removed on the site goes back to the .env value.
        fake.Body = """{"rev":5,"settings":{"dms":false}}""";
        Assert.True(await site.PollAsync(CancellationToken.None));
        Assert.Equal(new ulong[] { 100, 200 }, c.Channels.OrderBy(x => x));
        Assert.Equal(15, c.PerHour);
        Assert.Equal("gemini:flash", c.Models);
        Assert.False(c.AllowDms);
        Assert.Equal(2, applied);
    }

    [Fact]
    public async Task Failed_fetch_keeps_the_last_good_settings()
    {
        BotConfig c = Config();
        FakeSite fake = new FakeSite { Body = """{"rev":2,"settings":{"perHour":7}}""" };
        SiteSettings site = Site(c, fake);
        Assert.True(await site.PollAsync(CancellationToken.None));

        fake.Throw = true;
        Assert.False(await site.PollAsync(CancellationToken.None));
        fake.Throw = false;
        fake.Status = HttpStatusCode.Forbidden;
        Assert.False(await site.PollAsync(CancellationToken.None));
        fake.Status = HttpStatusCode.OK;
        fake.Body = "<html>not json</html>";
        Assert.False(await site.PollAsync(CancellationToken.None));
        fake.Body = """{"rev":3}""";   // no settings object
        Assert.False(await site.PollAsync(CancellationToken.None));

        Assert.Equal(7, c.PerHour);
        Assert.Equal(2, site.Rev);
        // and the cache still holds the good answer
        Assert.Contains("\"perHour\":7", File.ReadAllText(Path.Combine(_dir, SiteSettings.CacheFile)));
    }

    [Fact]
    public async Task Cache_is_used_at_start_before_any_fetch()
    {
        BotConfig first = Config();
        FakeSite fake = new FakeSite { Body = """{"rev":9,"settings":{"channels":["42"],"perDay":11}}""" };
        Assert.True(await Site(first, fake).PollAsync(CancellationToken.None));

        // A new process: only the cache, the site never asked.
        BotConfig second = Config();
        FakeSite none = new FakeSite { Throw = true };
        SiteSettings site = Site(second, none);
        Assert.True(site.LoadCached());
        Assert.Empty(none.Seen);
        Assert.Equal(new ulong[] { 42 }, second.Channels);
        Assert.Equal(11, second.PerDay);
        Assert.Equal(9, site.Rev);

        // No cache, or a broken one: the .env values.
        File.WriteAllText(Path.Combine(_dir, SiteSettings.CacheFile), "{broken");
        BotConfig third = Config();
        Assert.False(Site(third, none).LoadCached());
        Assert.Equal(new ulong[] { 100, 200 }, third.Channels.OrderBy(x => x));
        File.Delete(Path.Combine(_dir, SiteSettings.CacheFile));
        Assert.False(Site(Config(), none).LoadCached());
    }

    [Fact]
    public void Unusable_values_keep_the_env_ones()
    {
        BotConfig c = Config();
        c.ApplySettings((JsonObject)JsonNode.Parse("""{"channels":["x"],"perHour":0,"perDay":-1,"queueChannel":"abc","models":" "}"""), 6);
        Assert.Equal(new ulong[] { 100, 200 }, c.Channels.OrderBy(x => x));
        Assert.Equal(15, c.PerHour);
        Assert.Equal(60, c.PerDay);
        Assert.Equal(0UL, c.QueueChannel);
        Assert.Equal("gemini:flash", c.Models);
        Assert.Equal(6, c.SettingsRev);
    }

    [Fact]
    public async Task Report_sends_version_revision_and_channels()
    {
        BotConfig c = Config();
        FakeSite fake = new FakeSite { Body = """{"rev":4,"settings":{}}""" };
        SiteSettings site = Site(c, fake);
        await site.PollAsync(CancellationToken.None);
        await site.ReportAsync(new[] { new SeenChannel(11, "general", "QA"), new SeenChannel(12, "bot", "QA") }, CancellationToken.None);

        var post = fake.Seen.Last();
        Assert.Equal((HttpMethod.Post, "/api/bot/report", "bot-secret"), (post.method, post.path, post.token));
        JsonObject body = (JsonObject)JsonNode.Parse(post.body);
        Assert.Equal("9.9", (string)body["version"]);
        Assert.Equal(4, (int)body["rev"]);
        Assert.Equal("11", (string)body["channels"][0]["id"]);
        Assert.Equal("general", (string)body["channels"][0]["name"]);
        Assert.Equal("QA", (string)body["channels"][1]["guild"]);
    }

    [Fact]
    public async Task Run_polls_then_reports_only_when_it_sees_channels()
    {
        BotConfig c = Config();
        FakeSite fake = new FakeSite();
        SiteSettings site = Site(c, fake);
        using CancellationTokenSource stop = new CancellationTokenSource();
        List<SeenChannel> seen = new List<SeenChannel>();   // not connected yet
        Task run = site.RunAsync(() => seen, stop.Token);
        await Task.Delay(300);
        stop.Cancel();
        await run;
        Assert.Contains(fake.Seen, s => s.method == HttpMethod.Get);
        Assert.DoesNotContain(fake.Seen, s => s.method == HttpMethod.Post);
    }

    [Fact]
    public void Config_missing_token_means_no_site_sync()
    {
        BotConfig c = Config();
        Assert.Null(SiteSettings.FromConfig(c, new HttpClient(), "1", _log.Add));
        _env["FOREST_BOT_TOKEN"] = "t";
        Assert.NotNull(SiteSettings.FromConfig(c, new HttpClient(), "1", _log.Add));
    }

    [Fact]
    public void ModelChain_replace_swaps_the_order_in_place()
    {
        ModelChain chain = ModelChain.FromSpec("", new HttpClient(), _ => null, null, _log.Add);
        Assert.Empty(chain.Models);
        ModelChain other = ModelChain.FromSpec("gemini:a,gemini:b", new HttpClient(), n => n == "GEMINI_API_KEY" ? "k" : null, null, _log.Add);
        chain.Replace(other.Models);
        Assert.Equal(new[] { "gemini:a", "gemini:b" }.Length, chain.Models.Count);
        Assert.Equal(other.Models[0].Name, chain.Pick().Name);
    }
}
