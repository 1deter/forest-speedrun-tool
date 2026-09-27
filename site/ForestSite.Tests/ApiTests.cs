using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using ForestOverlay.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using UnityEngine;
using Xunit;

namespace ForestSite.Tests;

public sealed class ApiTests : IDisposable
{
    private readonly string _data;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _http;

    public ApiTests()
    {
        _data = Path.Combine(Path.GetTempPath(), "forest-site-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("FOREST_DATA", _data);
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

    private static Segment TestSegment(string id = "s-0123456789ab")
    {
        var s = new Segment { Id = id, Name = "Test dash", Category = "Test" };
        s.Start = new Trigger { Kind = TriggerKind.Zone, Position = new Vector3(0, 0, 0), Radius = 3 };
        s.Checkpoints.Add(new Trigger { Kind = TriggerKind.Zone, Position = new Vector3(0, 0, 20), Radius = 3 });
        s.CheckpointNames.Add("Rock");
        s.End = new Trigger { Kind = TriggerKind.Zone, Position = new Vector3(0, 0, 40), Radius = 3 };
        return s;
    }

    private static string RunText(Segment seg, string runner, float duration, float split, int minute = 0, string route = null)
    {
        var a = new Attempt
        {
            AnchorLabel = seg.Id, Route = route ?? seg.RouteFingerprint(), Duration = duration,
            RecordedUtc = new DateTime(2026, 9, 27, 12, minute, 0, DateTimeKind.Utc), Completed = true,
            Splits = new[] { split }, RunnerId = runner, RunnerName = "Runner " + runner.Substring(2, 4),
        };
        for (int i = 0; i <= 10; i++)
            a.Samples.Add(new RunSample { T = duration * i / 10, P = new Vector3(0, 0, 4 * i), Speed = 6 });
        return AttemptFormat.Write(a);
    }

    private static string Bundle(Segment seg, params string[] runs)
    {
        var b = new SegmentBundle { Segment = seg, Exported = "now", PluginVersion = "test" };
        b.Attempts.AddRange(runs);
        return b.Write();
    }

    private async Task<string> Register(string runner)
    {
        var r = await _http.PostAsJsonAsync("/api/register", new { runner, name = "Runner" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonObject>())["token"].GetValue<string>();
    }

    private async Task<HttpResponseMessage> Upload(string token, string body, string path = "/api/runs")
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };
        if (token != null) msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _http.SendAsync(msg);
    }

    private const string A = "r-00000000000000aa";
    private const string B = "r-00000000000000bb";

    [Fact]
    public async Task Register_OwnsTheId()
    {
        await Register(A);
        var again = await _http.PostAsJsonAsync("/api/register", new { runner = A, name = "Thief" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var bad = await _http.PostAsJsonAsync("/api/register", new { runner = "76561198000000000", name = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task BoardText_EachRunnersBest_ForThePlugin()
    {
        Segment seg = TestSegment();
        string ta = await Register(A), tb = await Register(B);
        await Upload(ta, Bundle(seg, RunText(seg, A, 10f, 4f, 1), RunText(seg, A, 9f, 4.5f, 2)));
        await Upload(tb, Bundle(seg, RunText(seg, B, 9.5f, 3.5f)));

        string url = SiteBoard.Url("", seg.Id, seg.RouteFingerprint());
        List<BoardEntry> board = SiteBoard.Parse(await _http.GetStringAsync(url));
        Assert.Equal(2, board.Count);
        Assert.Equal(A, board[0].RunnerId);
        Assert.Equal(new[] { 4.5f, 9f }, board[0].Splits);
        Assert.Equal(B, board[1].RunnerId);

        // The run file behind an entry is an ordinary .run (the ghost).
        string run = await _http.GetStringAsync(SiteBoard.RunFileUrl("", board[0].RunId));
        Assert.Equal(9f, AttemptFormat.Parse(run.Split('\n')).Duration, 3);

        // Another route, or no spot at all: an empty board, not an error.
        Assert.Empty(SiteBoard.Parse(await _http.GetStringAsync(SiteBoard.Url("", seg.Id, "other"))));
        Assert.Empty(SiteBoard.Parse(await _http.GetStringAsync(SiteBoard.Url("", "s-none", "x"))));
    }

    [Fact]
    public async Task OwnersRenameReachesTheSite_OthersDoNot()
    {
        Segment seg = TestSegment();
        string ta = await Register(A), tb = await Register(B);
        await Upload(ta, Bundle(seg, RunText(seg, A, 10f, 4f, 1)));

        // The owner renames it in game and runs again.
        seg.Name = "Renamed dash"; seg.Category = "Mine"; seg.Notes = "new description";
        await Upload(ta, Bundle(seg, RunText(seg, A, 9f, 4f, 2)));
        // Someone else's copy under the same id changes nothing.
        seg.Name = "Stolen"; seg.Notes = "theirs";
        await Upload(tb, Bundle(seg, RunText(seg, B, 9.5f, 4f, 3)));

        JsonNode spot = (await _http.GetFromJsonAsync<JsonArray>("/api/spots")).Single(s => s["id"].GetValue<string>() == seg.Id);
        Assert.Equal("Renamed dash", spot["name"].GetValue<string>());
        Assert.Equal("Mine", spot["category"].GetValue<string>());
        Assert.Equal("Runner 0000", spot["by"].GetValue<string>());
        var detail = await _http.GetFromJsonAsync<JsonObject>("/api/spots/" + seg.Id);
        Assert.Equal("new description", detail["notes"].GetValue<string>());
        Assert.Equal("Runner 0000", detail["by"].GetValue<string>());
    }

    [Fact]
    public async Task Upload_ThenBoardShowsEachRunnersBest()
    {
        Segment seg = TestSegment();
        string ta = await Register(A), tb = await Register(B);

        var up = await Upload(ta, Bundle(seg, RunText(seg, A, 10f, 4f, 1), RunText(seg, A, 9f, 4.5f, 2)));
        Assert.Equal(HttpStatusCode.OK, up.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Upload(tb, Bundle(seg, RunText(seg, B, 9.5f, 3.5f)))).StatusCode);

        var spots = await _http.GetFromJsonAsync<JsonArray>("/api/spots");
        JsonNode spot = spots.Single(s => s["id"].GetValue<string>() == seg.Id);
        Assert.Equal(3, spot["runs"].GetValue<int>());
        Assert.Equal(2, spot["runners"].GetValue<int>());
        Assert.Equal(9.0, spot["best"].GetValue<double>());

        var detail = await _http.GetFromJsonAsync<JsonObject>("/api/spots/" + seg.Id);
        JsonNode route = detail["routes"][0];
        Assert.Equal(seg.RouteFingerprint(), route["route"].GetValue<string>());
        Assert.Equal("Rock", route["splitNames"][0].GetValue<string>());
        Assert.Equal("End", route["splitNames"][1].GetValue<string>());
        JsonArray board = route["board"].AsArray();
        Assert.Equal(2, board.Count);
        Assert.Equal(A, board[0]["runner"].GetValue<string>());
        Assert.Equal(9.0, board[0]["duration"].GetValue<double>());
        Assert.Equal(2, board[0]["attempts"].GetValue<int>());
        // Golds across everyone: 3.5 (B) to the rock, 4.5 (A's 9.0 - 4.5) after.
        Assert.Equal(3.5, route["bestSegments"][0].GetValue<double>());
        Assert.Equal(4.5, route["bestSegments"][1].GetValue<double>());
        Assert.Equal(8.0, route["sumOfBest"].GetValue<double>());

        long id = board[0]["id"].GetValue<long>();
        var run = await _http.GetFromJsonAsync<JsonObject>("/api/runs/" + id);
        Assert.Equal(11, run["path"].AsArray().Count);
        Assert.Equal(40.0, run["path"][10][3].GetValue<double>());

        string file = await _http.GetStringAsync("/api/runs/" + id + "/file");
        Assert.Contains("duration|9.000", file);
    }

    [Fact]
    public async Task Upload_SameAttemptTwice_IsStoredOnce()
    {
        Segment seg = TestSegment();
        string ta = await Register(A);
        string body = Bundle(seg, RunText(seg, A, 10f, 4f));
        await Upload(ta, body);
        var second = await (await Upload(ta, body)).Content.ReadFromJsonAsync<JsonObject>();
        Assert.Empty(second["added"].AsArray());
        Assert.Single(second["existing"].AsArray());
    }

    [Fact]
    public async Task Upload_Refusals()
    {
        Segment seg = TestSegment();
        string ta = await Register(A);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Upload(null, Bundle(seg, RunText(seg, A, 10f, 4f)))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Upload("ft_nope", Bundle(seg, RunText(seg, A, 10f, 4f)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(ta, "not a bundle")).StatusCode);

        // Another route version, another runner's attempt: nothing stored.
        var wrongRoute = await Upload(ta, Bundle(seg, RunText(seg, A, 10f, 4f, route: "deadbeef")));
        Assert.Equal((HttpStatusCode)422, wrongRoute.StatusCode);
        Assert.Contains("another version", await wrongRoute.Content.ReadAsStringAsync());
        Assert.Equal((HttpStatusCode)422, (await Upload(ta, Bundle(seg, RunText(seg, B, 10f, 4f)))).StatusCode);

        var board = await _http.GetFromJsonAsync<JsonObject>("/api/spots/" + seg.Id);
        Assert.Empty(board["routes"][0]["board"].AsArray());
    }

    [Fact]
    public async Task MovedZone_IsANewRoute_OldRunsKeptApart()
    {
        Segment v1 = TestSegment();
        string ta = await Register(A);
        await Upload(ta, Bundle(v1, RunText(v1, A, 10f, 4f, 1)));

        Segment v2 = TestSegment();
        v2.End.Position = new Vector3(0, 0, 50);
        await Upload(ta, Bundle(v2, RunText(v2, A, 12f, 4f, 2)));

        var detail = await _http.GetFromJsonAsync<JsonObject>("/api/spots/" + v1.Id);
        JsonArray routes = detail["routes"].AsArray();
        Assert.Equal(2, routes.Count);
        Assert.True(routes[0]["current"].GetValue<bool>());
        Assert.Equal(v2.RouteFingerprint(), routes[0]["route"].GetValue<string>());
        Assert.Equal(12.0, routes[0]["board"][0]["duration"].GetValue<double>());
        Assert.Equal(10.0, routes[1]["board"][0]["duration"].GetValue<double>());
    }

    [Fact]
    public async Task FarFasterThanTheBest_IsFlagged()
    {
        Segment seg = TestSegment();
        string ta = await Register(A);
        await Upload(ta, Bundle(seg, RunText(seg, A, 10f, 4f, 1), RunText(seg, A, 10.5f, 4f, 2), RunText(seg, A, 11f, 4f, 3)));
        await Upload(ta, Bundle(seg, RunText(seg, A, 5f, 2f, 4)));

        var admin = new HttpRequestMessage(HttpMethod.Get, "/api/admin/flagged");
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.SendAsync(admin)).StatusCode);
        admin = new HttpRequestMessage(HttpMethod.Get, "/api/admin/flagged");
        admin.Headers.Add("X-Admin-Token", "admin-secret");
        var flagged = await (await _http.SendAsync(admin)).Content.ReadFromJsonAsync<JsonArray>();
        Assert.Single(flagged);
        Assert.Equal(5.0, flagged[0]["duration"].GetValue<double>());
    }

    [Fact]
    public async Task CommunityPacks_AreListedWithoutRuns()
    {
        var spots = await _http.GetFromJsonAsync<JsonArray>("/api/spots");
        Assert.Contains(spots, s => s["community"].GetValue<bool>() && s["id"].GetValue<string>() == "s-a558e9927461");
    }

    [Fact]
    public async Task Page_LinksHashedAssets_ApiMissesAreJson()
    {
        var page = await _http.GetAsync("/");
        string html = await page.Content.ReadAsStringAsync();
        Assert.Equal("no-cache", page.Headers.CacheControl.ToString());
        Assert.Matches(@"src=""/app\.js\?v=[0-9a-f]{10}""", html);
        Assert.Matches(@"href=""/style\.css\?v=[0-9a-f]{10}""", html);

        Assert.Equal(html, await _http.GetStringAsync("/spot/anything"));
        // Old ids have dots: the fallback alone would take them for files.
        Assert.Equal(html, await _http.GetStringAsync("/spot/spot.my.new-spot-3/abc123"));
        Assert.Equal(html, await _http.GetStringAsync("/admin/runners"));
        Assert.Equal(html, await _http.GetStringAsync("/about"));
        var missing = await _http.GetAsync("/api/nope");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Contains("no such endpoint", await missing.Content.ReadAsStringAsync());

        var asset = await _http.GetAsync("/app.js?v=1");
        Assert.Contains("immutable", asset.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Submission_ReachesTheAuthor()
    {
        string ta = await Register(A);
        var r = await Upload(ta, Bundle(TestSegment("s-bbbbbbbbbbbb")), "/api/submissions");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var list = new HttpRequestMessage(HttpMethod.Get, "/api/admin/submissions");
        list.Headers.Add("X-Admin-Token", "admin-secret");
        var subs = await (await _http.SendAsync(list)).Content.ReadFromJsonAsync<JsonArray>();
        Assert.Equal("s-bbbbbbbbbbbb", subs[0]["segment"].GetValue<string>());
    }

    private async Task<JsonNode> Admin(string path)
    {
        var msg = new HttpRequestMessage(HttpMethod.Get, path);
        msg.Headers.Add("X-Admin-Token", "admin-secret");
        var r = await _http.SendAsync(msg);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.Content.ReadFromJsonAsync<JsonNode>();
    }

    [Fact]
    public async Task Submission_SecondSubmitReplacesTheWaitingOne()
    {
        string ta = await Register(A);
        var seg = TestSegment("s-cccccccccccc");
        var first = await (await Upload(ta, Bundle(seg), "/api/submissions")).Content.ReadFromJsonAsync<JsonObject>();
        Assert.False(first["replaced"].GetValue<bool>());

        seg.Name = "Test dash, fixed";
        var second = await (await Upload(ta, Bundle(seg), "/api/submissions")).Content.ReadFromJsonAsync<JsonObject>();
        Assert.True(second["replaced"].GetValue<bool>());
        Assert.Equal(first["id"].GetValue<long>(), second["id"].GetValue<long>());

        var subs = (await Admin("/api/admin/submissions")).AsArray();
        Assert.Single(subs);
        Assert.Equal("Test dash, fixed", subs[0]["name"].GetValue<string>());
        Assert.Equal("Runner", subs[0]["runnerName"].GetValue<string>());
        Assert.False(subs[0]["startState"].GetValue<bool>());

        // Once the author has decided, a new submit is a new entry.
        var set = new HttpRequestMessage(HttpMethod.Post, "/api/admin/submissions/" + first["id"] + "/rejected");
        set.Headers.Add("X-Admin-Token", "admin-secret");
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(set)).StatusCode);
        var third = await (await Upload(ta, Bundle(seg), "/api/submissions")).Content.ReadFromJsonAsync<JsonObject>();
        Assert.False(third["replaced"].GetValue<bool>());
        Assert.Equal(2, (await Admin("/api/admin/submissions")).AsArray().Count);
    }

    [Fact]
    public async Task Submission_RefusesOldIdsAndAttempts()
    {
        string ta = await Register(A);
        var old = await Upload(ta, Bundle(TestSegment("spot.my.new-spot-3")), "/api/submissions");
        Assert.Equal(HttpStatusCode.BadRequest, old.StatusCode);
        Assert.Contains("Duplicate", await old.Content.ReadAsStringAsync());

        var seg = TestSegment("s-dddddddddddd");
        var timed = await Upload(ta, Bundle(seg, RunText(seg, A, 10f, 5f)), "/api/submissions");
        Assert.Equal(HttpStatusCode.BadRequest, timed.StatusCode);
    }

    [Fact]
    public async Task Admin_ListsRunnersAndChecksTheToken()
    {
        string ta = await Register(A);
        var seg = TestSegment();
        await Upload(ta, Bundle(seg, RunText(seg, A, 10f, 5f)));

        Assert.True((await Admin("/api/admin/check"))["ok"].GetValue<bool>());
        var runners = (await Admin("/api/admin/runners")).AsArray();
        Assert.Single(runners);
        Assert.Equal(A, runners[0]["id"].GetValue<string>());
        Assert.Equal(1, runners[0]["runs"].GetValue<long>());
        Assert.True(runners[0]["hasToken"].GetValue<bool>());

        var wrong = new HttpRequestMessage(HttpMethod.Get, "/api/admin/check");
        wrong.Headers.Add("X-Admin-Token", "nope");
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.SendAsync(wrong)).StatusCode);
    }
}
