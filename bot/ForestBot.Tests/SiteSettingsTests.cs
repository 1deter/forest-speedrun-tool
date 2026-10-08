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
        c.ApplySettings((JsonObject)JsonNode.Parse("""{"channels":"x","perHour":0,"perDay":-1,"queueChannel":"abc","models":" "}"""), 6);
        Assert.Equal(new ulong[] { 100, 200 }, c.Channels.OrderBy(x => x));
        Assert.Equal(15, c.PerHour);
        Assert.Equal(60, c.PerDay);
        Assert.Equal(0UL, c.QueueChannel);
        Assert.Equal("gemini:flash", c.Models);
        Assert.Equal(6, c.SettingsRev);
    }

    [Fact]
    public void Saved_channels_are_the_whole_list_never_saved_keeps_env()
    {
        BotConfig c = Config();
        // Never saved: the site has nothing - the .env channels.
        c.ApplySettings(new JsonObject(), 0);
        Assert.False(c.AllChannels);
        Assert.Equal(new ulong[] { 100, 200 }, c.Channels.OrderBy(x => x));

        // Saved with none ticked: no channel at all (not the .env ones, not everywhere); DMs follow dms.
        c.ApplySettings((JsonObject)JsonNode.Parse("""{"channels":[],"dms":true}"""), 1);
        Assert.False(c.AllChannels);
        Assert.Empty(c.Channels);
        Assert.True(c.AllowDms);
        c.ApplySettings((JsonObject)JsonNode.Parse("""{"channels":[],"dms":false}"""), 2);
        Assert.False(c.AllowDms);

        // Empty .env channels and nothing saved: everywhere it can read.
        _env["FOREST_BOT_CHANNELS"] = "";
        c.ApplySettings(new JsonObject(), 3);
        Assert.True(c.AllChannels);
    }

    [Fact]
    public async Task Apply_swaps_one_snapshot_readers_never_see_a_mix()
    {
        BotConfig c = Config();   // .env: channels 100,200, 15 an hour
        JsonObject site = (JsonObject)JsonNode.Parse("""{"channels":["7"],"perHour":3,"dms":false}""");
        using CancellationTokenSource stop = new CancellationTokenSource();
        string bad = null;
        Task reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                BotConfig.LiveSettings l = c.Live;
                bool env = l.Channels.Contains(100), sitev = l.Channels.Contains(7);
                // a snapshot is either all .env (100,200 / 15 / dms on) or all the site's (7 / 3 / dms off)
                if (!(env && !sitev && l.PerHour == 15 && l.AllowDms && l.Channels.Count == 2 && !l.AllChannels)
                    && !(sitev && !env && l.PerHour == 3 && !l.AllowDms && l.Channels.Count == 1 && !l.AllChannels))
                { bad = "mixed: " + string.Join(",", l.Channels) + " " + l.PerHour + " dms=" + l.AllowDms + " all=" + l.AllChannels; return; }
            }
        });
        for (int i = 0; i < 20000 && bad == null; i++)
            c.ApplySettings(i % 2 == 0 ? site : new JsonObject(), i);
        stop.Cancel();
        await reader;
        Assert.Null(bad);
    }

    [Fact]
    public void Build_id_names_the_deploy_or_dev()
    {
        // bot.yml publishes with -p:SourceRevisionId=<sha>; the SDK makes the informational version "1.0.0+<sha>".
        const string Sha = "0123456789abcdef0123456789abcdef01234567";
        Assert.Equal("0123456789ab", ForestBot.Gateway.DiscordBot.BuildId("1.0.0+" + Sha));
        Assert.Equal("abc", ForestBot.Gateway.DiscordBot.BuildId("2.1+abc"));
        Assert.Equal("dev", ForestBot.Gateway.DiscordBot.BuildId("1.0.0"));
        Assert.Equal("dev", ForestBot.Gateway.DiscordBot.BuildId("1.0.0+"));
        Assert.Equal("dev", ForestBot.Gateway.DiscordBot.BuildId(""));
        Assert.Equal("dev", ForestBot.Gateway.DiscordBot.BuildId(null));
        Assert.False(string.IsNullOrWhiteSpace(ForestBot.Gateway.DiscordBot.BuildId()));
    }

    [Fact]
    public void Unusable_model_order_keeps_the_current_chain()
    {
        Func<string, string> keys = n => n == "GEMINI_API_KEY" ? "k" : null;
        ModelChain chain = ModelChain.FromSpec("gemini:a,gemini:b", new HttpClient(), keys, null, _log.Add);
        string[] before = chain.Models.Select(m => m.Name).ToArray();

        // A typo / unknown provider / missing key: nothing usable.
        ModelChain bad = ModelChain.FromSpec("nonesuch:x,mistral:m", new HttpClient(), keys, null, _log.Add);
        Assert.Empty(bad.Models);
        Assert.False(chain.ReplaceIfAny(bad.Models, "nonesuch:x,mistral:m"));
        Assert.Equal(before, chain.Models.Select(m => m.Name));
        Assert.Contains(_log, l => l.Contains("model order 'nonesuch:x,mistral:m' has no usable model - keeping"));

        // A usable one replaces it.
        ModelChain good = ModelChain.FromSpec("gemini:c", new HttpClient(), keys, null, _log.Add);
        Assert.True(chain.ReplaceIfAny(good.Models, "gemini:c"));
        Assert.Single(chain.Models);
    }

    [Fact]
    public async Task Report_names_the_channels_it_answers_in_now()
    {
        BotConfig c = Config();   // .env: 100,200, never saved
        FakeSite fake = new FakeSite { Body = """{"rev":0,"settings":{}}""" };
        SiteSettings site = Site(c, fake);
        await site.PollAsync(CancellationToken.None);
        await site.ReportAsync(new[] { new SeenChannel(100, "a", "G") }, CancellationToken.None);
        JsonObject body = (JsonObject)JsonNode.Parse(fake.Seen.Last().body);
        Assert.False((bool)body["allChannels"]);
        Assert.Equal(new[] { "100", "200" }, body["answersIn"].AsArray().Select(x => (string)x).OrderBy(x => x));

        // Saved on the site: the applied set; everywhere when .env has none and nothing is saved.
        fake.Body = """{"rev":2,"settings":{"channels":["7"]}}""";
        await site.PollAsync(CancellationToken.None);
        await site.ReportAsync(new[] { new SeenChannel(7, "a", "G") }, CancellationToken.None);
        body = (JsonObject)JsonNode.Parse(fake.Seen.Last().body);
        Assert.Equal(new[] { "7" }, body["answersIn"].AsArray().Select(x => (string)x));
        _env["FOREST_BOT_CHANNELS"] = "";
        fake.Body = """{"rev":3,"settings":{}}""";
        await site.PollAsync(CancellationToken.None);
        await site.ReportAsync(new[] { new SeenChannel(7, "a", "G") }, CancellationToken.None);
        body = (JsonObject)JsonNode.Parse(fake.Seen.Last().body);
        Assert.True((bool)body["allChannels"]);
        Assert.Empty(body["answersIn"].AsArray());
    }

    [Fact]
    public async Task Report_carries_the_time_the_revision_was_applied()
    {
        BotConfig c = Config();
        FakeSite fake = new FakeSite { Body = """{"rev":4,"settings":{}}""" };
        SiteSettings site = Site(c, fake);
        DateTime t = new DateTime(2026, 10, 7, 12, 34, 0, DateTimeKind.Utc);
        site.Now = () => t;
        Assert.Null(site.AppliedAt);
        await site.PollAsync(CancellationToken.None);
        t = t.AddMinutes(5);
        await site.PollAsync(CancellationToken.None);   // same revision: the apply time stays
        await site.ReportAsync(new[] { new SeenChannel(1, "a", "G") }, CancellationToken.None);
        JsonObject body = (JsonObject)JsonNode.Parse(fake.Seen.Last().body);
        Assert.Equal(new DateTime(2026, 10, 7, 12, 34, 0, DateTimeKind.Utc), DateTime.Parse((string)body["appliedAt"], null, System.Globalization.DateTimeStyles.RoundtripKind));
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
    public async Task Report_sends_each_channels_category()
    {
        BotConfig c = Config();
        FakeSite fake = new FakeSite { Body = """{"rev":1,"settings":{}}""" };
        SiteSettings site = Site(c, fake);
        await site.PollAsync(CancellationToken.None);
        await site.ReportAsync(new[] { new SeenChannel(11, "general", "QA", "Forest", 2, 0), new SeenChannel(12, "rules", "QA") }, CancellationToken.None);
        JsonObject body = (JsonObject)JsonNode.Parse(fake.Seen.Last().body);
        Assert.Equal("Forest", (string)body["channels"][0]["category"]);
        Assert.Equal("", (string)body["channels"][1]["category"]);
    }

    [Fact]
    public void Channels_are_listed_in_Discords_sidebar_order()
    {
        // Two servers (kept in first-seen order); in each, no category first, then categories by
        // position; inside one, text before voice, then by position.
        List<SeenChannel> sorted = SeenChannel.InDiscordOrder(new[]
        {
            new SeenChannel(1, "general", "A", "Forest", 5, 0),
            new SeenChannel(2, "Room 1", "A", "Forest", 5, 0, Voice: true),
            new SeenChannel(3, "welcome", "A"),
            new SeenChannel(4, "general", "A", "Info", 1, 3),
            new SeenChannel(5, "rules", "A", "Info", 1, 0),
            new SeenChannel(6, "general", "B"),
            new SeenChannel(7, "bot-talk", "A", "Forest", 5, 2),
        });
        Assert.Equal(new ulong[] { 3, 5, 4, 1, 7, 2, 6 }, sorted.Select(s => s.Id));
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
