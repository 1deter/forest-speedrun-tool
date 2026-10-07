using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ForestSite.Tests;

// The knowledge bot's settings (T-0028): the owner edits, the bot reads with its
// own token and reports back.
[Collection("site")]
public sealed class BotSettingsTests : IDisposable
{
    private readonly string _data;

    public BotSettingsTests()
    {
        _data = Path.Combine(Path.GetTempPath(), "forest-site-test-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("FOREST_BOT_TOKEN", null);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_data, true); } catch { }
    }

    [Fact]
    public void Save_ValidatesAndBumpsTheRevision()
    {
        var b = new BotSettings(new Store(_data));
        Assert.Equal(0, b.Current().rev);

        var (rev, error) = b.Save("""{"channels":["123","123",456],"dms":false,"perHour":5,"models":"gemini:a,gemini:b","thinking":"low","queueChannel":"789"}""");
        Assert.Null(error);
        Assert.Equal(1, rev);
        var (s, r) = b.Current();
        Assert.Equal(1, r);
        Assert.Equal(new[] { "123", "456" }, s["channels"].AsArray().Select(x => (string)x));
        Assert.False((bool)s["dms"]);
        Assert.Equal(5, (int)s["perHour"]);
        Assert.Null(s["perDay"]);

        // An empty text field drops the setting (the bot's .env default applies).
        Assert.Null(b.Save("""{"models":"","thinking":"","queueChannel":""}""").error);
        Assert.Null(b.Current().settings["models"]);

        foreach (string bad in new[]
                 {
                     "not json", "[]", """{"channels":"1"}""", """{"channels":["x"]}""", """{"channels":["1 OR 1"]}""", """{"dms":"yes"}""",
                     """{"perHour":0}""", """{"perHour":1001}""", """{"perDay":"a"}""", """{"models":"a b"}""", """{"models":"x;rm"}""",
                     """{"thinking":"extreme"}""", """{"queueChannel":"abc"}""", """{"token":"x"}""", """{"discordToken":"x"}""",
                 })
            Assert.NotNull(b.Save(bad).error);
        Assert.Equal(2, b.Current().rev);   // refused saves change nothing

        var many = "[" + string.Join(",", Enumerable.Range(1, BotSettings.MaxChannels + 1).Select(i => "\"" + i + "\"")) + "]";
        Assert.NotNull(b.Save("{\"channels\":" + many + "}").error);
    }

    [Fact]
    public void Report_KeepsOnlyValidChannels_Clipped()
    {
        var b = new BotSettings(new Store(_data));
        Assert.Null(b.Report());
        Assert.Null(b.SaveReport("""{"version":"1.2","rev":3,"channels":[{"id":"11","name":"general","guild":"G"},{"id":"zz","name":"bad"},{"id":"12","name":"%NAME%"}]}"""
            .Replace("%NAME%", new string('n', 500))));
        var r = b.Report();
        Assert.Equal(3, (int)r["rev"]);
        Assert.Equal(2, r["channels"].AsArray().Count);
        Assert.Equal(100, ((string)r["channels"][1]["name"]).Length);
        Assert.NotNull(b.SaveReport("nope"));
    }

    private HttpRequestMessage Req(HttpMethod m, string path, string header, string token, string body = null)
    {
        var msg = new HttpRequestMessage(m, path);
        if (token != null) msg.Headers.Add(header, token);
        if (body != null) msg.Content = new StringContent(body, Encoding.UTF8);
        return msg;
    }

    [Fact]
    public async Task Api_OwnerSaves_BotReadsAndReports_OthersRefused()
    {
        Environment.SetEnvironmentVariable("FOREST_DATA", _data);
        Environment.SetEnvironmentVariable("FOREST_SRC_SYNC", "off");
        Environment.SetEnvironmentVariable("FOREST_ADMIN_TOKEN", "admin-secret");
        Environment.SetEnvironmentVariable("FOREST_BOT_TOKEN", "bot-secret");
        using var factory = new WebApplicationFactory<Program>();
        using var http = factory.CreateClient();

        // The owner only.
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Req(HttpMethod.Get, "/api/admin/bot", "X-Admin-Token", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Req(HttpMethod.Get, "/api/admin/bot", "X-Admin-Token", "bot-secret"))).StatusCode);
        var made = await (await http.SendAsync(Req(HttpMethod.Post, "/api/admin/admins", "X-Admin-Token", "admin-secret", "maks"))).Content.ReadFromJsonAsync<JsonObject>();
        string named = (string)made["token"];
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Req(HttpMethod.Get, "/api/admin/bot", "X-Admin-Token", named))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Req(HttpMethod.Put, "/api/admin/bot", "X-Admin-Token", named, "{}"))).StatusCode);

        var put = await http.SendAsync(Req(HttpMethod.Put, "/api/admin/bot", "X-Admin-Token", "admin-secret", """{"channels":["42"],"dms":false}"""));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.SendAsync(Req(HttpMethod.Put, "/api/admin/bot", "X-Admin-Token", "admin-secret", """{"channels":["a"]}"""))).StatusCode);

        // The bot: its own token, not an admin's; the admin token is no bot token.
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Req(HttpMethod.Get, "/api/bot/settings", "X-Bot-Token", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Req(HttpMethod.Get, "/api/bot/settings", "X-Bot-Token", "admin-secret"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Req(HttpMethod.Get, "/api/bot/settings", "X-Admin-Token", "bot-secret"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Req(HttpMethod.Post, "/api/bot/report", "X-Bot-Token", "nope", "{}"))).StatusCode);
        var got = await (await http.SendAsync(Req(HttpMethod.Get, "/api/bot/settings", "X-Bot-Token", "bot-secret"))).Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(1, (int)got["rev"]);
        Assert.Equal("42", (string)got["settings"]["channels"][0]);
        // The bot's token opens nothing else.
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Req(HttpMethod.Get, "/api/admin/runners", "X-Bot-Token", "bot-secret"))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(Req(HttpMethod.Post, "/api/bot/report", "X-Bot-Token", "bot-secret",
            """{"version":"0.9","rev":1,"channels":[{"id":"42","name":"bot-test","guild":"QA"}]}"""))).StatusCode);
        var view = await (await http.SendAsync(Req(HttpMethod.Get, "/api/admin/bot", "X-Admin-Token", "admin-secret"))).Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("bot-test", (string)view["report"]["channels"][0]["name"]);
        Assert.Equal(1, (int)view["report"]["rev"]);
        Assert.True((bool)view["botToken"]);

        // The save is in the activity log.
        var log = await (await http.SendAsync(Req(HttpMethod.Get, "/api/admin/log", "X-Admin-Token", "admin-secret"))).Content.ReadFromJsonAsync<JsonArray>();
        Assert.Contains(log, l => (string)l["action"] == "PUT /api/admin/bot");
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task Api_NoBotTokenConfigured_BotEndpointsRefuse()
    {
        Environment.SetEnvironmentVariable("FOREST_DATA", _data);
        Environment.SetEnvironmentVariable("FOREST_SRC_SYNC", "off");
        Environment.SetEnvironmentVariable("FOREST_ADMIN_TOKEN", "admin-secret");
        Environment.SetEnvironmentVariable("FOREST_BOT_TOKEN", null);
        using var factory = new WebApplicationFactory<Program>();
        using var http = factory.CreateClient();
        // An empty header must not match an empty (unset) token.
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Req(HttpMethod.Get, "/api/bot/settings", "X-Bot-Token", ""))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/api/bot/settings")).StatusCode);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }
}
