using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ForestSite.Tests;

// Live sessions (T-0488): the relay itself, with a clock of our own...
public sealed class LiveSessionTests
{
    private DateTime _now = new(2026, 11, 28, 18, 0, 0, DateTimeKind.Utc);
    private LiveSessions New() => new(() => _now);

    [Fact]
    public void LinesGoOnceAndOutputReadsByOffset()
    {
        var live = New();
        var o = live.Open("1.2.3.4");
        Assert.Matches("^[A-HJ-NP-Z2-9]{6}$", o.Code);
        Assert.Matches("^[0-9a-f]{48}$", o.Secret);

        Assert.Equal(LiveSessions.SendResult.Ok, live.Send(o.Code.ToLowerInvariant(), "status\r\nplayer"));
        Assert.Equal(LiveSessions.Access.Ok, live.TakeInput(o.Secret, out string lines));
        Assert.Equal("status\nplayer\n", lines);
        live.TakeInput(o.Secret, out lines);
        Assert.Equal("", lines);

        live.AddOutput(o.Secret, "> #1 status\n< #1 ok (1 ms)\n");
        var first = live.ReadOutput(o.Code, 0);
        Assert.Equal("> #1 status\n< #1 ok (1 ms)\n", first.Text);
        Assert.True(first.Open);
        live.AddOutput(o.Secret, "é\n");
        var next = live.ReadOutput(o.Code, first.Offset);
        Assert.Equal("é\n", next.Text);
        Assert.Equal(first.Offset + 3, next.Offset);   // bytes, not characters
    }

    [Fact]
    public void WrongSecretIsUnknownAndClosedIsGone()
    {
        var live = New();
        var o = live.Open("a");
        Assert.Equal(LiveSessions.Access.Unknown, live.TakeInput("nope", out _));
        Assert.Equal(LiveSessions.Access.Unknown, live.TakeInput(null, out _));
        Assert.Equal(LiveSessions.Access.Ok, live.CloseByRunner(o.Secret));
        Assert.Equal(LiveSessions.Access.Closed, live.TakeInput(o.Secret, out _));
        Assert.Equal(LiveSessions.SendResult.Closed, live.Send(o.Code, "status"));
        var read = live.ReadOutput(o.Code, 0);
        Assert.False(read.Open);
        Assert.Contains("stopped by the runner", read.Text);
        Assert.Equal(LiveSessions.SendResult.Unknown, live.Send("ZZZZZZ", "status"));
    }

    [Fact]
    public void EndsOnIdleOnSilenceAndAfterFourHours()
    {
        var live = New();
        var idle = live.Open("a");
        _now += TimeSpan.FromMinutes(1);
        live.TakeInput(idle.Secret, out _);
        _now += TimeSpan.FromMinutes(29.5);
        live.TakeInput(idle.Secret, out _);
        _now += TimeSpan.FromMinutes(1);      // 31.5 min since the last line sent
        Assert.Equal(LiveSessions.Access.Closed, live.TakeInput(idle.Secret, out _));
        Assert.Contains("30 minutes with no command", live.ReadOutput(idle.Code, 0).Text);

        var quiet = live.Open("b");
        _now += TimeSpan.FromMinutes(3);      // the game never polled
        Assert.Contains("the game stopped answering", live.ReadOutput(quiet.Code, 0).Text);

        var longOne = live.Open("c");
        for (int i = 0; i < 4 * 60 + 2; i++)   // just past 4 h, inside the 10 min it stays readable
        {
            _now += TimeSpan.FromMinutes(1);
            live.Send(longOne.Code, "status");
            live.TakeInput(longOne.Secret, out _);
        }
        Assert.Contains("4 hours in all", live.ReadOutput(longOne.Code, 0).Text);

        // A closed one is readable for 10 minutes, then gone.
        _now += TimeSpan.FromMinutes(11);
        Assert.Null(live.ReadOutput(longOne.Code, 0));
    }

    [Fact]
    public void OneSessionPerAddressAndAFullSiteSaysSo()
    {
        var live = New();
        var old = live.Open("same");
        var fresh = live.Open("same");
        Assert.Equal(LiveSessions.Access.Closed, live.TakeInput(old.Secret, out _));
        Assert.Equal(LiveSessions.Access.Ok, live.TakeInput(fresh.Secret, out _));

        for (int i = 1; i < LiveSessions.MaxOpen; i++) Assert.NotNull(live.Open("ip" + i));
        Assert.Null(live.Open("one too many"));
    }

    [Fact]
    public void CapsHold()
    {
        var live = New();
        var o = live.Open("a");
        Assert.Equal(LiveSessions.SendResult.TooMuch, live.Send(o.Code, new string('x', LiveSessions.MaxPending + 1)));

        string line = new string('y', 1023) + "\n";
        for (int i = 0; i < 1500; i++) live.AddOutput(o.Secret, line);   // 1.5 MB
        var all = live.ReadOutput(o.Code, 0);
        Assert.True(all.Trimmed);
        Assert.Equal(1500L * 1024, all.Offset);
        Assert.True(Encoding.UTF8.GetByteCount(all.Text) <= LiveSessions.MaxOutput);
        Assert.EndsWith(line, all.Text);
        Assert.StartsWith("y", all.Text);   // cut at a line

        byte[] png = Png(10);
        for (int i = 0; i < LiveSessions.MaxFiles + 2; i++) live.AddFile(o.Secret, "s" + i, png);
        Assert.Null(live.File(o.Code, "s0"));
        Assert.NotNull(live.File(o.Code, "s" + (LiveSessions.MaxFiles + 1)));
    }

    [Theory]
    [InlineData("mcp-120000-001", true)]
    [InlineData("a_b-C9", true)]
    [InlineData("", false)]
    [InlineData("../x", false)]
    [InlineData("a.png", false)]
    public void PictureNames(string name, bool ok) => Assert.Equal(ok, LiveSessions.ValidName(name));

    internal static byte[] Png(int extra)
    {
        var b = new byte[8 + extra];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0);
        return b;
    }
}

// ...and over HTTP, as the game and the author's MCP use it.
[Collection("site")]
public sealed class LiveApiTests : IDisposable
{
    private readonly string _data;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _http;

    public LiveApiTests()
    {
        _data = Path.Combine(Path.GetTempPath(), "forest-site-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("FOREST_DATA", _data);
        Environment.SetEnvironmentVariable("FOREST_SRC_SYNC", "off");
        Environment.SetEnvironmentVariable("FOREST_ADMIN_TOKEN", "admin-secret");
        _factory = new WebApplicationFactory<Program>();
        _http = _factory.CreateClient();
    }

    public void Dispose()
    {
        _http.Dispose();
        _factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_data, true); } catch { }
    }

    private HttpRequestMessage Game(HttpMethod m, string path, string secret, HttpContent body = null)
    {
        var r = new HttpRequestMessage(m, path) { Content = body };
        r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return r;
    }

    private HttpRequestMessage Author(HttpMethod m, string path, HttpContent body = null, string token = "admin-secret")
    {
        var r = new HttpRequestMessage(m, path) { Content = body };
        r.Headers.Add("X-Admin-Token", token);
        return r;
    }

    [Fact]
    public async Task RoundTrip()
    {
        var open = await _http.PostAsync("/api/live/open", null);
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
        var j = JsonNode.Parse(await open.Content.ReadAsStringAsync());
        string code = (string)j["code"], secret = (string)j["secret"];
        Assert.Equal(1800, (int)j["idle"]);

        // The author lists it and sends two lines.
        var list = await _http.SendAsync(Author(HttpMethod.Get, "/api/admin/live"));
        Assert.Contains(code, await list.Content.ReadAsStringAsync());
        var sent = await _http.SendAsync(Author(HttpMethod.Post, "/api/admin/live/" + code + "/send", new StringContent("status\nshot mcp-1\n")));
        Assert.Equal(HttpStatusCode.NoContent, sent.StatusCode);

        // The game takes them and answers.
        var inp = await _http.SendAsync(Game(HttpMethod.Get, "/api/live/in", secret));
        Assert.Equal("status\nshot mcp-1\n", await inp.Content.ReadAsStringAsync());
        var outp = await _http.SendAsync(Game(HttpMethod.Post, "/api/live/out", secret, new StringContent("> #1 status\n< #1 ok (2 ms)\n")));
        Assert.Equal(HttpStatusCode.NoContent, outp.StatusCode);
        var pic = new ByteArrayContent(LiveSessionTests.Png(32));
        pic.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var file = await _http.SendAsync(Game(HttpMethod.Post, "/api/live/file?name=mcp-1", secret, pic));
        Assert.Equal(HttpStatusCode.NoContent, file.StatusCode);

        // The author reads the output from an offset, and the picture.
        var read = await _http.SendAsync(Author(HttpMethod.Get, "/api/admin/live/" + code + "/out?after=0"));
        Assert.Equal("> #1 status\n< #1 ok (2 ms)\n", await read.Content.ReadAsStringAsync());
        Assert.Equal("open", read.Headers.GetValues("X-Live-State").Single());
        string offset = read.Headers.GetValues("X-Live-Offset").Single();
        var again = await _http.SendAsync(Author(HttpMethod.Get, "/api/admin/live/" + code + "/out?after=" + offset));
        Assert.Equal("", await again.Content.ReadAsStringAsync());
        var png = await _http.SendAsync(Author(HttpMethod.Get, "/api/admin/live/" + code + "/file/mcp-1"));
        Assert.Equal("image/png", png.Content.Headers.ContentType.MediaType);
        Assert.Equal(40, (await png.Content.ReadAsByteArrayAsync()).Length);

        // The runner's Stop: the game is told 410, the author sees it closed.
        Assert.Equal(HttpStatusCode.NoContent, (await _http.SendAsync(Game(HttpMethod.Post, "/api/live/close", secret))).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await _http.SendAsync(Game(HttpMethod.Get, "/api/live/in", secret))).StatusCode);
        var closed = await _http.SendAsync(Author(HttpMethod.Get, "/api/admin/live/" + code + "/out?after=" + offset));
        Assert.Equal("closed", closed.Headers.GetValues("X-Live-State").Single());
        Assert.Contains("stopped by the runner", await closed.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Gone, (await _http.SendAsync(Author(HttpMethod.Post, "/api/admin/live/" + code + "/send", new StringContent("status")))).StatusCode);
    }

    [Fact]
    public async Task RefusesStrangers()
    {
        var j = JsonNode.Parse(await (await _http.PostAsync("/api/live/open", null)).Content.ReadAsStringAsync());
        string code = (string)j["code"];

        Assert.Equal(HttpStatusCode.Forbidden, (await _http.SendAsync(Game(HttpMethod.Get, "/api/live/in", "0123456789abcdef0123456789abcdef0123456789abcdef"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.GetAsync("/api/live/in")).StatusCode);
        // The code alone is nothing without the admin token.
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.SendAsync(Author(HttpMethod.Post, "/api/admin/live/" + code + "/send", new StringContent("status"), "wrong"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.GetAsync("/api/admin/live/" + code + "/out")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.SendAsync(Author(HttpMethod.Get, "/api/admin/live/ZZZZZZ/out"))).StatusCode);

        string secret = (string)j["secret"];
        var notPng = await _http.SendAsync(Game(HttpMethod.Post, "/api/live/file?name=x", secret, new ByteArrayContent(new byte[] { 1, 2, 3 })));
        Assert.Equal(HttpStatusCode.BadRequest, notPng.StatusCode);
        var badName = await _http.SendAsync(Game(HttpMethod.Post, "/api/live/file?name=..%2Fx", secret, new ByteArrayContent(LiveSessionTests.Png(1))));
        Assert.Equal(HttpStatusCode.BadRequest, badName.StatusCode);
    }
}
