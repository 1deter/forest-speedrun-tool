using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ForestOverlay.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using UnityEngine;
using Xunit;

namespace ForestSite.Tests;

[Collection("site")]   // one site at a time: FOREST_DATA is process-wide
public sealed class ApiTests : IDisposable
{
    private readonly string _data;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _http;

    public ApiTests()
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
        // State channels as the game writes them: shown ones among internal ones.
        a.Channels = new[] { "HeartRate", "Stamina", "Health", "explodeHash", "item:Soda" };
        for (int i = 0; i <= 4; i++)
            a.States.Add(new StateSample { T = duration * i / 4, Values = new float[] { 70, 100 - 10 * i, 100, 12345, i / 2 } });
        a.Items.Add(new ItemChange { T = 0, Name = "Soda", Count = 3 });
        a.Items.Add(new ItemChange { T = duration / 2, Name = "Soda", Count = 2 });
        a.Events.Add(new RunEvent { T = 1, Kind = RunAudit.Crafted, Detail = "Bomb", P = new Vector3(0, 0, 4) });
        a.Buildings.Add(new RunBuilding { T = 2, State = RunBuilding.Placed, Kind = "LogCabin", P = new Vector3(1, 2, 3),
                                          Euler = new Vector3(0, 90, 0), Size = new Vector3(7, 5, 7) });
        a.Buildings.Add(new RunBuilding { T = 6, State = RunBuilding.Built, Kind = "LogCabin", P = new Vector3(1.5f, 2, 3),
                                          Euler = new Vector3(2, 90, 0), Center = new Vector3(0, 2.5f, 0.5f), Size = new Vector3(7, 5, 7) });
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
    public async Task WebsiteSpots_ListAndFoseg_ForThePlugin()
    {
        Segment seg = TestSegment();
        string ta = await Register(A);
        await Upload(ta, Bundle(seg, RunText(seg, A, 10f, 4f, 1)));

        List<SiteSpot> list = SiteSpots.Parse(await _http.GetStringAsync(SiteSpots.ListUrl("")));
        SiteSpot spot = Assert.Single(list);
        Assert.Equal(seg.Id, spot.Id);
        Assert.Equal(1, spot.Runs);

        // The spot as a .foseg: the same route, so the board matches in game.
        string text = await _http.GetStringAsync(SiteSpots.FileUrl("", seg.Id));
        SegmentBundle b = SegmentBundle.Parse(text, out string error, null);
        Assert.NotNull(b);
        Assert.Equal(seg.RouteFingerprint(), b.Segment.RouteFingerprint());
        Assert.Empty(b.Attempts);

        var none = await _http.GetAsync(SiteSpots.FileUrl("", "s-none"));
        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
    }

    [Fact]
    public async Task StartState_KeptPerRoute_WhenItsHashMatches()
    {
        var state = new SavestateFile { Name = "boost", Level = "TheForest", Data = "level data 1" };
        string stateText = state.Write();
        Segment seg = TestSegment();
        seg.StartState = Segment.HashText(state.Data);
        string ta = await Register(A), tb = await Register(B);

        // Uploaded without its state: the site asks its owner for it.
        var first = await (await Upload(ta, Bundle(seg, RunText(seg, A, 10f, 4f, 1)))).Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("wanted", first["startstate"]?.GetValue<string>());
        Assert.Null(SegmentBundle.Parse(await _http.GetStringAsync(SiteSpots.FileUrl("", seg.Id)), out _, null).StartState);

        // Not the owner: never asked, and a state sent is not kept.
        var bb = new SegmentBundle { Segment = seg, Exported = "now", PluginVersion = "test", StartState = stateText };
        bb.Attempts.Add(RunText(seg, B, 11f, 4f, 4));
        var notOwner = await (await Upload(tb, bb.Write())).Content.ReadFromJsonAsync<JsonObject>();
        Assert.Null(notOwner["startstate"]?.GetValue<string>());
        Assert.Null(SegmentBundle.Parse(await _http.GetStringAsync(SiteSpots.FileUrl("", seg.Id)), out _, null).StartState);

        // Another state than the route was timed from is refused.
        var wrong = new SavestateFile { Name = "boost", Level = "TheForest", Data = "level data 2" };
        var b = new SegmentBundle { Segment = seg, Exported = "now", PluginVersion = "test", StartState = wrong.Write() };
        b.Attempts.Add(RunText(seg, A, 10f, 4f, 1));
        var refused = await (await Upload(ta, b.Write())).Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("wanted", refused["startstate"]?.GetValue<string>());
        Assert.Contains(refused["skipped"].AsArray(), x => x.GetValue<string>().Contains("start state"));

        // The matching one is kept and comes with the spot's .foseg.
        b.StartState = stateText;
        var stored = await (await Upload(ta, b.Write())).Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("stored", stored["startstate"]?.GetValue<string>());
        SegmentBundle got = SegmentBundle.Parse(await _http.GetStringAsync(SiteSpots.FileUrl("", seg.Id)), out _, null);
        Assert.Equal(stateText, got.StartState);
        Assert.Equal(seg.RouteFingerprint(), got.Segment.RouteFingerprint());

        // Kept: later uploads are not asked again.
        var later = await (await Upload(ta, Bundle(seg, RunText(seg, A, 9f, 4f, 2)))).Content.ReadFromJsonAsync<JsonObject>();
        Assert.Null(later["startstate"]?.GetValue<string>());

        // A teleport-only spot is never asked for one.
        Segment plain = TestSegment("s-0123456789cd");
        var p = await (await Upload(ta, Bundle(plain, RunText(plain, A, 10f, 4f, 3)))).Content.ReadFromJsonAsync<JsonObject>();
        Assert.Null(p["startstate"]?.GetValue<string>());

        // Deleting the spot (an admin: B has a run on it) takes its state with it.
        var del = new HttpRequestMessage(HttpMethod.Delete, "/api/admin/spots/" + seg.Id);
        del.Headers.Add("X-Admin-Token", "admin-secret");
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(del)).StatusCode);
        Assert.Empty(Directory.GetFiles(Path.Combine(_data, "startstates")));
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
    public async Task SpotsTextNamesTheOwner_WhoseEditedRouteBecomesTheSpot()
    {
        // T-0265: the plugin adds a runner's own spot back as theirs, by the
        // owner the list names; their upload of an edited copy (same id, their
        // token) is the spot again, not a copy.
        Segment seg = TestSegment();
        string ta = await Register(A), tb = await Register(B);
        await Upload(ta, Bundle(seg, RunText(seg, A, 10f, 4f, 1)));
        await Upload(tb, Bundle(seg, RunText(seg, B, 9f, 4f, 2)));

        SiteSpot listed = SiteSpots.Parse(await _http.GetStringAsync("/api/spots.txt")).Single(s => s.Id == seg.Id);
        Assert.Equal(A, listed.Owner);

        seg.End = new Trigger { Kind = TriggerKind.Zone, Position = new Vector3(0, 0, 45), Radius = 3 };
        seg.Name = "Edited dash";
        Assert.Equal(HttpStatusCode.OK, (await Upload(ta, Bundle(seg, RunText(seg, A, 11f, 4f, 3)))).StatusCode);

        SegmentBundle got = SegmentBundle.Parse(await _http.GetStringAsync(SiteSpots.FileUrl("", seg.Id)), out _, null);
        Assert.Equal(seg.RouteFingerprint(), got.Segment.RouteFingerprint());
        Assert.Equal("Edited dash", got.Segment.Name);
        Assert.True(SiteSpots.SameAsOwn(got.Segment, seg));
        listed = SiteSpots.Parse(await _http.GetStringAsync("/api/spots.txt")).Single(s => s.Id == seg.Id);
        Assert.Equal(A, listed.Owner);
        Assert.Equal("Edited dash", listed.Name);
    }

    [Fact]
    public async Task APolygonZoneReachesTheMapAsItsOutline()
    {
        Segment seg = TestSegment();
        Assert.True(TriggerParser.Parse("poly 5 2 -3 18 3 18 3 22 -3 22", out Trigger poly));
        seg.Checkpoints[0] = poly;
        string ta = await Register(A);
        Assert.Equal(HttpStatusCode.OK, (await Upload(ta, Bundle(seg, RunText(seg, A, 10f, 4f, 1)))).StatusCode);

        var detail = await _http.GetFromJsonAsync<JsonObject>("/api/spots/" + seg.Id);
        JsonNode route = detail["routes"][0];
        Assert.Equal(seg.RouteFingerprint(), route["route"].GetValue<string>());
        JsonNode check = route["checks"][0];
        Assert.Equal("poly", check["kind"].GetValue<string>());
        Assert.Equal(4, check["points"].AsArray().Count);
        Assert.Equal(3.0, check["points"][1][0].GetValue<double>());
        Assert.Equal(18.0, check["points"][1][1].GetValue<double>());
        Assert.Equal(2.0, check["half"].GetValue<double>());
        Assert.Equal(5.0, check["at"][1].GetValue<double>());
        Assert.Equal(20.0, check["at"][2].GetValue<double>());
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
        // Only the shown channels, in the page's order (Health before Stamina).
        var state = run["state"].AsObject();
        Assert.Equal(new[] { "Health", "Stamina", "item:Soda" }, state["channels"].AsArray().Select(n => n.GetValue<string>()));
        Assert.Equal(5, state["samples"].AsArray().Count);
        Assert.Equal(new[] { 9.0, 100, 60, 2 }, state["samples"][4].AsArray().Select(n => n.GetValue<double>()));
        Assert.Equal(2, run["items"].AsArray().Count);
        Assert.Equal("Soda", run["items"][1][1].GetValue<string>());
        Assert.Equal(2, run["items"][1][2].GetValue<int>());
        Assert.Equal("Crafted: Bomb", run["events"][0][2].GetValue<string>());
        Assert.Equal(4.0, run["events"][0][5].GetValue<double>());
        Assert.Equal("LogCabin", run["buildings"][0][2].GetValue<string>());
        Assert.Equal(90.0, run["buildings"][0][6].GetValue<double>());
        // The maps' extras: the event's group (its colour), each box's centre,
        // tilt, and a blueprint's end - when it was finished at that place.
        Assert.Equal("items", run["events"][0][6].GetValue<string>());
        Assert.Equal(2, run["buildings"].AsArray().Count);
        Assert.Equal(6.0, run["buildings"][0][15].GetValue<double>());
        Assert.Null(run["buildings"][1][15]);
        Assert.Equal(2.5, run["buildings"][1][11].GetValue<double>());
        Assert.Equal(0.5, run["buildings"][1][12].GetValue<double>());
        Assert.Equal(2.0, run["buildings"][1][13].GetValue<double>());
        // A run from before load-removed time: no LRT to show.
        Assert.Null(run["lrt"]);
        Assert.Null(run["loads"]);
        var every = await _http.GetFromJsonAsync<JsonObject>("/api/runs/" + id + "?all=1");
        Assert.Equal(5, every["state"]["channels"].AsArray().Count);

        string file = await _http.GetStringAsync("/api/runs/" + id + "/file");
        Assert.Contains("duration|9.000", file);
    }

    [Fact]
    public async Task SpotPage_CarriesItsPreviewTags()
    {
        Segment seg = TestSegment();
        seg.Name = "Dash <to> the rock";
        string ta = await Register(A);
        Assert.Equal(HttpStatusCode.OK, (await Upload(ta, Bundle(seg, RunText(seg, A, 75.5f, 4f)))).StatusCode);

        string html = await _http.GetStringAsync("/spot/" + seg.Id);
        Assert.Contains("<title>Dash &lt;to&gt; the rock - Forest Practice Runs</title>", html);
        Assert.Contains("<meta property=\"og:title\" content=\"Dash &lt;to&gt; the rock - Forest Practice Runs\">", html);
        Assert.Contains("1 run by 1 runner, best 1:15.500 by Runner 0000", html);
        Assert.Contains("og:url\" content=\"https://forest.deter.cloud/spot/" + seg.Id, html);

        string plain = await _http.GetStringAsync("/spot/s-nothere");
        Assert.DoesNotContain("og:title", plain);
        Assert.Contains("<title>Forest Practice Runs</title>", plain);
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

        // ... and no route either: an upload with no good attempt leaves no empty spot.
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/api/spots/" + seg.Id)).StatusCode);
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
        // The 3D view's module, loaded on demand, is versioned the same way.
        Assert.Matches(@"data-map3d-src=""/map3d\.js\?v=[0-9a-f]{10}""", html);
        // ... and the world it imports (a plain import would carry no version).
        Assert.Matches(@"data-world3d-src=""/world3d\.js\?v=[0-9a-f]{10}""", html);

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

    private async Task<HttpResponseMessage> AdminSend(HttpMethod method, string path, string token = "admin-secret", string body = null)
    {
        var msg = new HttpRequestMessage(method, path);
        msg.Headers.Add("X-Admin-Token", token);
        if (body != null) msg.Content = new StringContent(body, Encoding.UTF8, "text/plain");
        return await _http.SendAsync(msg);
    }

    [Fact]
    public async Task Admins_OwnerMakesAndRevokesTokens_ChangesAreLogged()
    {
        var made = await (await AdminSend(HttpMethod.Post, "/api/admin/admins", body: "maks")).Content.ReadFromJsonAsync<JsonObject>();
        string token = made["token"].GetValue<string>();
        Assert.StartsWith("fa_", token);

        var me = await (await AdminSend(HttpMethod.Get, "/api/admin/check", token)).Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("maks", me["name"].GetValue<string>());
        Assert.False(me["owner"].GetValue<bool>());

        // An admin does the work, but cannot manage admins.
        string ta = await Register(A);
        Assert.Equal(HttpStatusCode.OK, (await AdminSend(HttpMethod.Post, "/api/admin/runners/" + A + "/ban", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AdminSend(HttpMethod.Get, "/api/admin/admins", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AdminSend(HttpMethod.Post, "/api/admin/admins", token, "x")).StatusCode);

        var log = (await Admin("/api/admin/log")).AsArray();
        Assert.Contains(log, l => l["admin"].GetValue<string>() == "maks" && l["action"].GetValue<string>().EndsWith("/ban")
                                  && l["status"].GetValue<long>() == 200);
        Assert.Contains(log, l => l["admin"].GetValue<string>() == "owner" && l["action"].GetValue<string>() == "POST /api/admin/admins");

        Assert.Equal(HttpStatusCode.OK, (await AdminSend(HttpMethod.Delete, "/api/admin/admins/" + made["id"])).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AdminSend(HttpMethod.Get, "/api/admin/check", token)).StatusCode);
        Assert.True((await Admin("/api/admin/admins")).AsArray()[0]["revoked"].GetValue<bool>());
    }

    [Fact]
    public async Task Admin_DeletesARunnersSpot_NotACommunityOne()
    {
        string ta = await Register(A);
        var seg = TestSegment("s-eeeeeeeeeeee");
        await Upload(ta, Bundle(seg, RunText(seg, A, 10f, 5f), RunText(seg, A, 11f, 5f, 1)));
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync("/api/spots/s-eeeeeeeeeeee")).StatusCode);

        var del = await AdminSend(HttpMethod.Delete, "/api/admin/spots/s-eeeeeeeeeeee");
        Assert.Equal(2, (await del.Content.ReadFromJsonAsync<JsonObject>())["runs"].GetValue<int>());
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/api/spots/s-eeeeeeeeeeee")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AdminSend(HttpMethod.Delete, "/api/admin/spots/s-eeeeeeeeeeee")).StatusCode);

        var spots = await _http.GetFromJsonAsync<JsonArray>("/api/spots");
        var community = spots.First(s => s["community"].GetValue<bool>())["id"].GetValue<string>();
        Assert.Equal(HttpStatusCode.BadRequest, (await AdminSend(HttpMethod.Delete, "/api/admin/spots/" + community)).StatusCode);
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

    [Fact]
    public async Task AerialTilesUploadByTheOwnerOnly()
    {
        byte[] Zip(params string[] names)
        {
            using var ms = new MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
                foreach (string n in names)
                {
                    using var w = new StreamWriter(zip.CreateEntry(n).Open());
                    w.Write("jpg bytes");
                }
            return ms.ToArray();
        }
        async Task<HttpResponseMessage> Post(byte[] body, string token)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/aerial?clear=1") { Content = new ByteArrayContent(body) };
            req.Headers.Add("X-Admin-Token", token);
            return await _http.SendAsync(req);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await _http.GetAsync("/aerial/aerial.json")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(Zip("canopy/6/1_2.jpg"), "wrong")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(Zip("../evil.jpg"), "admin-secret")).StatusCode);
        var ok = await Post(Zip("canopy/6/1_2.jpg", "aerial.json"), "admin-secret");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("jpg bytes", await _http.GetStringAsync("/aerial/canopy/6/1_2.jpg"));
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync("/aerial/aerial.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/aerial/canopy/6/9_9.jpg")).StatusCode);

        // The 3D world: its own folder and names; .bin served as bytes.
        Assert.Equal(HttpStatusCode.NoContent, (await _http.GetAsync("/world/world.json")).StatusCode);
        async Task<HttpResponseMessage> PostWorld(byte[] body)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/world?clear=1") { Content = new ByteArrayContent(body) };
            req.Headers.Add("X-Admin-Token", "admin-secret");
            return await _http.SendAsync(req);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await PostWorld(Zip("canopy/6/1_2.jpg"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostWorld(Zip("world.json", "m/3.bin", "t/0.jpg", "t/0.png", "c/caves_-2_5.bin", "c/caves_0_0_L.bin"))).StatusCode);
        var bin = await _http.GetAsync("/world/c/caves_-2_5.bin");
        Assert.Equal(HttpStatusCode.OK, bin.StatusCode);
        Assert.Equal("application/octet-stream", bin.Content.Headers.ContentType?.MediaType);
        Assert.Equal("jpg bytes", await _http.GetStringAsync("/aerial/canopy/6/1_2.jpg"));   // untouched
    }

    [Fact]
    public async Task WorldFilesOfAnotherBuildAreRefused()
    {
        // Files are named by index: a page holding the previous upload's
        // world.json must not get this upload's m/3.bin in its place.
        async Task Upload(string query, params (string Name, string Text)[] files)
        {
            using var ms = new MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
                foreach (var (name, text) in files)
                {
                    using var w = new StreamWriter(zip.CreateEntry(name).Open());
                    w.Write(text);
                }
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/world" + query) { Content = new ByteArrayContent(ms.ToArray()) };
            req.Headers.Add("X-Admin-Token", "admin-secret");
            Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(req)).StatusCode);
        }

        await Upload("?clear=1", ("m/3.bin", "mesh A"), ("world.json", "{\"version\":1,\"build\":1790815322}"));
        Assert.Equal("mesh A", await _http.GetStringAsync("/world/m/3.bin?v=1790815322"));
        Assert.Equal("mesh A", await _http.GetStringAsync("/world/m/3.bin"));   // no version: served
        var old = await _http.GetAsync("/world/m/3.bin?v=1790000000");
        Assert.Equal(HttpStatusCode.NotFound, old.StatusCode);
        Assert.Contains("no-store", old.Headers.CacheControl?.ToString());

        // Mid-upload (?clear=1 removed the json, which goes last): no version is served.
        await Upload("?clear=1", ("m/3.bin", "mesh B"));
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/world/m/3.bin?v=1790815322")).StatusCode);
        await Upload("", ("world.json", "{\"version\":1,\"build\":1790900000}"));
        Assert.Equal("mesh B", await _http.GetStringAsync("/world/m/3.bin?v=1790900000"));
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/world/m/3.bin?v=1790815322")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync("/world/world.json")).StatusCode);

        // A json without a stamp (an older export) refuses nothing.
        await Upload("", ("world.json", "{\"version\":1}"));
        Assert.Equal("mesh B", await _http.GetStringAsync("/world/m/3.bin?v=123"));
    }

    [Fact]
    public async Task WorldFilesAreServedGzipped()
    {
        // The upload gzips each .bin / .json once (Precompressed); a client
        // taking gzip gets that copy, labelled as the original.
        async Task Upload(string query, params (string Name, string Text)[] files)
        {
            using var ms = new MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
                foreach (var (name, text) in files)
                {
                    using var w = new StreamWriter(zip.CreateEntry(name).Open());
                    w.Write(text);
                }
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/world" + query) { Content = new ByteArrayContent(ms.ToArray()) };
            req.Headers.Add("X-Admin-Token", "admin-secret");
            Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(req)).StatusCode);
        }
        async Task<HttpResponseMessage> Get(string url, string accept)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (accept != null) req.Headers.TryAddWithoutValidation("Accept-Encoding", accept);
            return await _http.SendAsync(req);
        }
        static string Gunzip(byte[] b)
        {
            using var z = new System.IO.Compression.GZipStream(new MemoryStream(b), System.IO.Compression.CompressionMode.Decompress);
            using var r = new StreamReader(z);
            return r.ReadToEnd();
        }
        static string Unbrotli(byte[] b)
        {
            using var z = new System.IO.Compression.BrotliStream(new MemoryStream(b), System.IO.Compression.CompressionMode.Decompress);
            using var r = new StreamReader(z);
            return r.ReadToEnd();
        }

        string pack = string.Concat(Enumerable.Repeat("mesh bytes ", 400));
        string json = "{\"version\":2,\"build\":1790815322,\"packs\":[\"p/0.bin\"],\"pad\":\"" + new string('x', 2000) + "\"}";
        await Upload("?clear=1", ("p/0.bin", pack), ("t/0.jpg", "jpg bytes"), ("world.json", json));

        // Brotli first when the client takes it (every browser over HTTPS).
        var br = await Get("/world/p/0.bin?v=1790815322", "gzip, deflate, br, zstd");
        Assert.Equal(HttpStatusCode.OK, br.StatusCode);
        Assert.Equal("br", string.Join(",", br.Content.Headers.ContentEncoding));
        Assert.Equal("application/octet-stream", br.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Accept-Encoding", br.Headers.Vary);
        Assert.Equal(pack, Unbrotli(await br.Content.ReadAsByteArrayAsync()));
        Assert.Equal("gzip", string.Join(",", (await Get("/world/p/0.bin", "gzip, br;q=0")).Content.Headers.ContentEncoding));

        var gz = await Get("/world/p/0.bin?v=1790815322", "gzip, deflate");
        Assert.Equal(HttpStatusCode.OK, gz.StatusCode);
        Assert.Equal("gzip", string.Join(",", gz.Content.Headers.ContentEncoding));
        Assert.Equal("application/octet-stream", gz.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Accept-Encoding", gz.Headers.Vary);
        Assert.Contains("max-age=86400", gz.Headers.CacheControl?.ToString());
        byte[] body = await gz.Content.ReadAsByteArrayAsync();
        Assert.True(body.Length < pack.Length / 10);
        Assert.Equal(pack, Gunzip(body));

        var plain = await Get("/world/p/0.bin?v=1790815322", null);
        Assert.Empty(plain.Content.Headers.ContentEncoding);
        Assert.Contains("Accept-Encoding", plain.Headers.Vary);
        Assert.Equal(pack, await plain.Content.ReadAsStringAsync());
        Assert.Empty((await Get("/world/p/0.bin", "gzip;q=0, identity")).Content.Headers.ContentEncoding);

        var meta = await Get("/world/world.json", "gzip");
        Assert.Equal("gzip", string.Join(",", meta.Content.Headers.ContentEncoding));
        Assert.Equal("application/json", meta.Content.Headers.ContentType?.MediaType);
        Assert.Equal(json, Gunzip(await meta.Content.ReadAsByteArrayAsync()));
        // A revalidation (world.json is fetched no-cache) still answers 304.
        var again = new HttpRequestMessage(HttpMethod.Get, "/world/world.json");
        again.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip");
        again.Headers.IfNoneMatch.Add(meta.Headers.ETag!);
        Assert.Equal(HttpStatusCode.NotModified, (await _http.SendAsync(again)).StatusCode);

        // Textures are never compressed; another build's ?v= is still refused.
        Assert.Empty((await Get("/world/t/0.jpg", "gzip, br")).Content.Headers.ContentEncoding);
        var old = await Get("/world/p/0.bin?v=1790000000", "gzip");
        Assert.Equal(HttpStatusCode.NotFound, old.StatusCode);
        Assert.Contains("no-store", old.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await Get("/world/p/9.bin", "gzip")).StatusCode);

        // An upload over a file replaces its .gz; one too small to shrink leaves none.
        await Upload("", ("p/0.bin", "tiny"));
        Assert.Equal("tiny", await (await Get("/world/p/0.bin", null)).Content.ReadAsStringAsync());
        var tiny = await Get("/world/p/0.bin", "gzip, br");
        Assert.Empty(tiny.Content.Headers.ContentEncoding);
        Assert.Equal("tiny", await tiny.Content.ReadAsStringAsync());
    }

    [Fact]
    public void Precompressed_BackfillWritesMissingBrotliOnly()
    {
        // A world uploaded before Brotli: .gz copies only; the startup pass
        // adds the .br ones, leaves textures and tiny files alone, and a
        // second pass writes nothing.
        string dir = Path.Combine(Path.GetTempPath(), "fo-br-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "b"));
            string big = Path.Combine(dir, "b", "0.bin"), small = Path.Combine(dir, "b", "1.bin"), tex = Path.Combine(dir, "t.jpg");
            File.WriteAllText(big, string.Concat(Enumerable.Repeat("mesh bytes ", 400)));
            File.WriteAllText(small, "x");
            File.WriteAllText(tex, string.Concat(Enumerable.Repeat("jpg ", 400)));
            Assert.Equal(1, Precompressed.Backfill(dir));   // big only
            Assert.True(File.Exists(big + ".br"));
            Assert.False(File.Exists(small + ".br"));
            Assert.False(File.Exists(tex + ".br"));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp", SearchOption.AllDirectories));
            Assert.Equal(0, Precompressed.Backfill(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("gzip, deflate, br, zstd", true)]
    [InlineData("br;q=1.0, GZIP;q=0.5", true)]
    [InlineData("gzip;q=0", false)]
    [InlineData("gzip; q=0.0, *", false)]
    [InlineData("*", true)]
    [InlineData("identity", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AcceptsGzip(string header, bool taken) => Assert.Equal(taken, Precompressed.AcceptsGzip(header));

    [Theory]
    [InlineData("gzip, deflate, br, zstd", true)]
    [InlineData("BR;q=0.9", true)]
    [InlineData("gzip, deflate", false)]
    [InlineData("br;q=0, *", false)]
    [InlineData("*", true)]
    public void AcceptsBrotli(string header, bool taken) => Assert.Equal(taken, Precompressed.Accepts(header, "br"));

    // --- security review (2026-10-01) ------------------------------------------

    [Fact]
    public async Task SecurityHeaders_OnPagesApiAndFiles()
    {
        foreach (string path in new[] { "/", "/api/spots", "/app.js", "/vendor/three-0.170.0.module.min.js?v=0.170.0" })
        {
            var r = await _http.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            string csp = string.Join(";", r.Headers.GetValues("Content-Security-Policy"));
            Assert.Contains("script-src 'self';", csp);
            Assert.Contains("frame-ancestors 'none'", csp);
            Assert.DoesNotContain("unsafe", csp);
            Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
        }
        // No script from anywhere but the site: the CSP would block it.
        string root = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(root, "ForestSite", "wwwroot"))) root = Path.GetDirectoryName(root);
        root = Path.Combine(root, "ForestSite", "wwwroot");
        foreach (string js in Directory.GetFiles(root, "*.js"))
            Assert.DoesNotMatch(@"import[^;]*""https?://", File.ReadAllText(js));
    }

    // --- /compare: two YouTube runs side by side (maks, QA 1554074251831672943) ---

    [Fact]
    public async Task Compare_PageWithPreviewAndYouTubeFrameOnly()
    {
        var r = await _http.GetAsync("/compare?a=dQw4w9WgXcQ~60~1000~5000&n=Cave");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        string html = await r.Content.ReadAsStringAsync();
        Assert.Contains("<title>Compare runs", html);
        Assert.Contains("og:title", html);
        Assert.Matches(@"src=""/compare\.js\?v=[0-9a-f]{10}""", html);
        Assert.Contains(@"href=""/compare""", html);   // in the nav

        // The one frame allowed is YouTube's no-cookie player; its script
        // is not (the player is driven by postMessage).
        string csp = string.Join(";", r.Headers.GetValues("Content-Security-Policy"));
        var frame = Regex.Match(csp, @"frame-src ([^;]*);");
        Assert.True(frame.Success, csp);
        Assert.Equal("https://www.youtube-nocookie.com", frame.Groups[1].Value.Trim());
        Assert.Contains("script-src 'self';", csp);
        Assert.DoesNotContain("youtube.com/iframe_api", csp);

        string root = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(root, "ForestSite", "wwwroot"))) root = Path.GetDirectoryName(root);
        string js = File.ReadAllText(Path.Combine(root, "ForestSite", "wwwroot", "compare.js"));
        // Every YouTube address the script names is the no-cookie origin,
        // and it never adds a script element.
        foreach (Match m in Regex.Matches(js, @"https://[a-z0-9.-]*youtube[a-z0-9.-]*"))
            Assert.Equal("https://www.youtube-nocookie.com", m.Value);
        Assert.DoesNotContain("\"script\"", js);
        Assert.DoesNotContain("innerHTML", js);
    }

    [Fact]
    public async Task OriginLock_RefusesRequestsWithoutCloudflaresHeader()
    {
        Environment.SetEnvironmentVariable("FOREST_ORIGIN_SECRET", "edge-secret");
        try
        {
            using var factory = new WebApplicationFactory<Program>();
            using var http = factory.CreateClient();
            Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/api/spots")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/")).StatusCode);
            var wrong = new HttpRequestMessage(HttpMethod.Get, "/api/spots");
            wrong.Headers.Add("X-Forest-Origin", "edge-secreT");
            Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(wrong)).StatusCode);
            var right = new HttpRequestMessage(HttpMethod.Get, "/api/spots");
            right.Headers.Add("X-Forest-Origin", "edge-secret");
            Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(right)).StatusCode);
        }
        finally { Environment.SetEnvironmentVariable("FOREST_ORIGIN_SECRET", null); }
    }

    [Fact]
    public async Task AnotherRunnersRoute_CannotRenameTheSpot()
    {
        Segment seg = TestSegment();
        seg.Notes = "A's description";
        string ta = await Register(A), tb = await Register(B);
        await Upload(ta, Bundle(seg, RunText(seg, A, 10f, 4f, 1)));

        // B takes A's id, moves a zone (a new route) and labels it as theirs.
        // B's run is the newest, so B's route is the spot's current one.
        Segment copy = TestSegment();
        copy.End = new Trigger { Kind = TriggerKind.Zone, Position = new Vector3(0, 0, 50), Radius = 3 };
        copy.Name = "Defaced"; copy.Category = "Spam"; copy.Notes = "visit my site";
        Assert.NotEqual(seg.RouteFingerprint(), copy.RouteFingerprint());
        Assert.Equal(HttpStatusCode.OK, (await Upload(tb, Bundle(copy, RunText(copy, B, 9f, 4f, 2)))).StatusCode);

        var detail = await _http.GetFromJsonAsync<JsonObject>("/api/spots/" + seg.Id);
        Assert.Equal(copy.RouteFingerprint(), detail["routes"][0]["route"].GetValue<string>());   // B's zones, shown
        Assert.Equal("Test dash", detail["name"].GetValue<string>());
        Assert.Equal("Test", detail["category"].GetValue<string>());
        Assert.Equal("A's description", detail["notes"].GetValue<string>());
        Assert.Equal("Runner 0000", detail["by"].GetValue<string>());

        // A's later rename still reaches the whole spot, B's copy included.
        seg.Name = "Renamed dash";
        await Upload(ta, Bundle(seg, RunText(seg, A, 9.5f, 4f, 3)));
        await Upload(tb, Bundle(copy, RunText(copy, B, 8.5f, 4f, 4)));
        detail = await _http.GetFromJsonAsync<JsonObject>("/api/spots/" + seg.Id);
        Assert.Equal("Renamed dash", detail["name"].GetValue<string>());
    }

    [Fact]
    public async Task Submissions_CappedPerRunner()
    {
        string ta = await Register(A);
        for (int i = 0; i < Runs.MaxOpenSubmissions; i++)
            Assert.Equal(HttpStatusCode.OK, (await Upload(ta, Bundle(TestSegment("s-" + i.ToString("x12"))), "/api/submissions")).StatusCode);
        var over = await Upload(ta, Bundle(TestSegment("s-ffffffffffff")), "/api/submissions");
        Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);
        Assert.Contains("already waiting", await over.Content.ReadAsStringAsync());
        // A waiting one can still be fixed (it replaces itself).
        Assert.Equal(HttpStatusCode.OK, (await Upload(ta, Bundle(TestSegment("s-" + 3.ToString("x12"))), "/api/submissions")).StatusCode);
    }

    [Theory]
    [InlineData("m/1.bin", true)]
    [InlineData("c/caves_-3_12_L.bin", true)]
    [InlineData("p/12.bin", true)]
    [InlineData("p/12.bin.gz", false)]
    [InlineData("p/12.bin.br", false)]
    [InlineData("q/3.bin", true)]
    [InlineData("q/3.png", false)]
    [InlineData("b/230.bin", true)]
    [InlineData("b/1.jpg", false)]
    [InlineData("world.json.gz", false)]
    [InlineData("m/1.bin\n", false)]
    [InlineData("../m/1.bin", false)]
    [InlineData("m/../../x.bin", false)]
    [InlineData("m/\u0661.bin", false)]
    [InlineData("/etc/m/1.bin", false)]
    [InlineData("m\\1.bin", false)]
    public void WorldUploadPaths(string path, bool allowed) => Assert.Equal(allowed, UploadPath.IsWorld(path));

    // --- a runner deleting their own spot --------------------------------------------

    private async Task<HttpResponseMessage> DeleteSpot(string token, string id)
    {
        var msg = new HttpRequestMessage(HttpMethod.Delete, "/api/spots/" + id);
        if (token != null) msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _http.SendAsync(msg);
    }

    [Fact]
    public async Task Owner_DeletesTheirSpot_OthersCannot()
    {
        string ta = await Register(A), tb = await Register(B);
        var seg = TestSegment("s-dddddddddddd");
        await Upload(ta, Bundle(seg, RunText(seg, A, 10f, 5f), RunText(seg, A, 11f, 5f, 1)));

        // No token, an unknown one, another runner: refused, the spot stays.
        Assert.Equal(HttpStatusCode.Unauthorized, (await DeleteSpot(null, seg.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await DeleteSpot("ft_made-up", seg.Id)).StatusCode);
        var notYours = await DeleteSpot(tb, seg.Id);
        Assert.Equal(HttpStatusCode.Forbidden, notYours.StatusCode);
        Assert.Contains("not your spot", await notYours.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync("/api/spots/" + seg.Id)).StatusCode);

        // The owner: gone, with its runs; the admins see who did it.
        var del = await DeleteSpot(ta, seg.Id);
        Assert.Equal(HttpStatusCode.OK, del.StatusCode);
        Assert.Equal(2, (await del.Content.ReadFromJsonAsync<JsonObject>())["runs"].GetValue<int>());
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/api/spots/" + seg.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await DeleteSpot(ta, seg.Id)).StatusCode);
        var log = (await Admin("/api/admin/log")).AsArray();
        Assert.Contains(log, l => l["admin"].GetValue<string>() == "runner " + A && l["status"].GetValue<long>() == 200
                                  && l["action"].GetValue<string>().StartsWith("DELETE /api/spots/" + seg.Id));

        // A banned owner's token no longer works.
        await Upload(ta, Bundle(seg, RunText(seg, A, 10f, 5f)));
        await AdminSend(HttpMethod.Post, "/api/admin/runners/" + A + "/ban");
        Assert.Equal(HttpStatusCode.Unauthorized, (await DeleteSpot(ta, seg.Id)).StatusCode);
    }

    [Fact]
    public async Task Owner_CannotDeleteOtherRunnersTimes_OrACommunitySpot()
    {
        string ta = await Register(A), tb = await Register(B);
        var seg = TestSegment("s-dddddddddddd");
        await Upload(ta, Bundle(seg, RunText(seg, A, 10f, 5f)));
        await Upload(tb, Bundle(seg, RunText(seg, B, 9f, 5f)));

        var busy = await DeleteSpot(ta, seg.Id);
        Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
        Assert.Contains("1 run by other runners", await busy.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync("/api/spots/" + seg.Id)).StatusCode);

        var spots = await _http.GetFromJsonAsync<JsonArray>("/api/spots");
        string community = spots.First(s => s["community"].GetValue<bool>())["id"].GetValue<string>();
        Assert.Equal(HttpStatusCode.Forbidden, (await DeleteSpot(ta, community)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await DeleteSpot(ta, "s-nothinghere0")).StatusCode);
    }

    // --- PB posts to Discord ---------------------------------------------------------------

    private const string OfficialHook = "https://discord.invalid/api/webhooks/test";
    private const string OwnHook = "https://discord.com/api/webhooks/1234567890/run-spot_TOKEN";

    /// A fake Discord behind the site's HttpClient: every POST it got (URL + JSON body); it answers
    /// with Answer (204 by default). No real Discord call is ever made.
    private sealed class FakeDiscord : HttpMessageHandler
    {
        public readonly List<(string url, JsonObject body)> Posts = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Answer = _ => new HttpResponseMessage(HttpStatusCode.NoContent);

        /// The embeds each post carried (what PbWebhook.Recent holds), oldest first.
        public List<JsonObject> Contents(string url = null)
        {
            lock (Posts) return Posts.Where(p => url == null || p.url == url)
                .Select(p => new JsonObject { ["content"] = (string)p.body["content"], ["embeds"] = p.body["embeds"].DeepClone() }).ToList();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = JsonNode.Parse(await r.Content.ReadAsStringAsync(ct)) as JsonObject;
            lock (Posts) Posts.Add((r.RequestUri.ToString(), body));
            return Answer(r);
        }
    }

    /// The official webhook switched on, both senders posting to a fake Discord.
    private FakeDiscord FakeWebhook()
    {
        var posts = _factory.Services.GetRequiredService<PbPosts>();
        var fake = new FakeDiscord();
        posts.Official.Url = OfficialHook;
        foreach (var hook in new[] { posts.Official, posts.RunnerSpots })
        {
            hook.SiteUrl = "https://forest.deter.cloud";
            hook.Http = new HttpClient(fake);
            hook.Gap = TimeSpan.FromMilliseconds(10);
        }
        return fake;
    }

    private static async Task Until(Func<bool> done)
    {
        for (int i = 0; i < 100 && !done(); i++) await Task.Delay(50);
    }

    private static JsonObject E(JsonObject post) => post["embeds"][0].AsObject();
    private static string Author(JsonObject post) => (string)E(post)["author"]["name"];
    private static string Name(JsonObject post) => Author(post).Substring(0, Author(post).IndexOf(" \u00B7 "));
    private static string Desc(JsonObject post) => (string)E(post)["description"];
    private static JsonObject Msg(string text) => new() { ["embeds"] = new JsonArray { new JsonObject { ["description"] = text } } };
    private static string Foot(JsonObject post) => (string)E(post)["footer"]["text"];
    private static string Field(JsonObject post, string name) =>
        E(post)["fields"]?.AsArray().Where(f => (string)f["name"] == name).Select(f => (string)f["value"]).FirstOrDefault();
    private static void SamePosts(List<JsonObject> a, List<JsonObject> b)
    {
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++) Assert.True(JsonNode.DeepEquals(a[i], b[i]), a[i].ToJsonString() + " != " + b[i].ToJsonString());
    }

    private void MakeCommunity(Segment seg)
    {
        var sb = new StringBuilder();
        SegmentFormat.WriteSegment(sb, seg, "\n");
        _factory.Services.GetRequiredService<Store>().SeeRoute(seg.Id, seg.RouteFingerprint(), seg.Name, seg.Category, sb.ToString(), true);
    }

    [Fact]
    public async Task Webhook_PostsANewPbOnACommunitySpot()
    {
        FakeDiscord discord = FakeWebhook();
        var hook = _factory.Services.GetRequiredService<PbWebhook>();
        string ta = await Register(A);
        var seg = TestSegment("s-cccccccccccc");
        seg.Name = "Plane to *cave*";
        MakeCommunity(seg);

        // The first run: "finished", with the run's link.
        await Upload(ta, Bundle(seg, RunText(seg, A, 10f, 5f, 1)));
        Assert.Single(hook.Recent);
        Assert.Contains("first run", Author(hook.Recent[0]));
        Assert.DoesNotContain("runner's spot", Author(hook.Recent[0]));
        Assert.Equal("Plane to \\*cave\\*", (string)E(hook.Recent[0])["title"]);
        Assert.Equal("**10.000**\nTheir first run here", Desc(hook.Recent[0]));
        Assert.Equal("Community spot", Foot(hook.Recent[0]));
        Assert.Equal(PbNews.CommunityColour, (int)E(hook.Recent[0])["color"]);
        Assert.Equal("Plane to \\*cave\\* first run: " + Name(hook.Recent[0]) + " 10.000", (string)hook.Recent[0]["content"]);
        Assert.Matches(@"^https://forest\.deter\.cloud/spot/s-cccccccccccc/[^/?]+\?run=\d+$", (string)E(hook.Recent[0])["url"]);
        Assert.Null(Field(hook.Recent[0], "Rank"));   // alone on the spot: no rank, no gap
        Assert.Null(Field(hook.Recent[0], "Next best"));
        Assert.Null(Field(hook.Recent[0], "Spot record"));

        // Slower: nothing. The same attempt again: nothing.
        await Upload(ta, Bundle(seg, RunText(seg, A, 11f, 5f, 2)));
        await Upload(ta, Bundle(seg, RunText(seg, A, 10f, 5f, 1)));
        Assert.Single(hook.Recent);

        // Two new ones, one faster: the fastest, against the old PB.
        await Upload(ta, Bundle(seg, RunText(seg, A, 9.5f, 5f, 3), RunText(seg, A, 8.75f, 4f, 4)));
        Assert.Equal(2, hook.Recent.Count);
        Assert.Contains("new PB", Author(hook.Recent[1]));
        Assert.Equal("**8.750**\n**1.250** faster than 10.000", Desc(hook.Recent[1]));
        Assert.Equal("Plane to \\*cave\\* PB: " + Name(hook.Recent[1]) + " 8.750", (string)hook.Recent[1]["content"]);

        // The queue delivers them to the webhook, mentions off.
        await Until(() => discord.Contents().Count >= 2);
        SamePosts(hook.Recent, discord.Contents(OfficialHook));
        Assert.All(discord.Posts, p => Assert.Empty(p.body["allowed_mentions"]["parse"].AsArray()));
        Assert.All(discord.Posts, p => Assert.NotEmpty((string)p.body["content"]));
    }

    [Fact]
    public async Task Webhook_RankAndGapAgainstOtherRunners()
    {
        FakeWebhook();
        var hook = _factory.Services.GetRequiredService<PbWebhook>();
        string ta = await Register(A), tb = await Register(B);
        var seg = TestSegment("s-gggggggggggg");
        seg.Category = "Cave 5";
        MakeCommunity(seg);

        await Upload(ta, Bundle(seg, RunText(seg, A, 10f, 5f, 1)));
        Assert.Equal("Cave 5", Field(hook.Recent[0], "Category"));
        // B is slower than A: rank 2 of 2, behind A's best.
        await Upload(tb, Bundle(seg, RunText(seg, B, 12f, 5f, 2)));
        Assert.Equal("#2 of 2 runners", Field(hook.Recent[1], "Rank"));
        Assert.Equal("10.000 (you: +2.000)", Field(hook.Recent[1], "Spot record"));
        Assert.Null(Field(hook.Recent[1], "Next best"));
        Assert.EndsWith(" first run: " + Name(hook.Recent[1]) + " 12.000", (string)hook.Recent[1]["content"]);
        // B beats A: first, ahead of A's best.
        await Upload(tb, Bundle(seg, RunText(seg, B, 9f, 5f, 3)));
        Assert.Equal("#1 of 2 runners", Field(hook.Recent[2], "Rank"));
        Assert.Equal("10.000 (+1.000)", Field(hook.Recent[2], "Next best"));
        Assert.Null(Field(hook.Recent[2], "Spot record"));
        Assert.EndsWith(" WR: " + Name(hook.Recent[2]) + " 9.000", (string)hook.Recent[2]["content"]);
    }

    [Fact]
    public async Task Webhook_QuietForOwnSpotsFlaggedRunsAndWhenOff()
    {
        var hook = _factory.Services.GetRequiredService<PbWebhook>();
        string ta = await Register(A), tb = await Register(B);

        // Off (no URL): nothing queued, the upload is fine.
        var com = TestSegment("s-bbbbbbbbbbbb");
        MakeCommunity(com);
        Assert.Equal(HttpStatusCode.OK, (await Upload(ta, Bundle(com, RunText(com, A, 10f, 5f)))).StatusCode);
        Assert.Empty(hook.Recent);

        FakeDiscord discord = FakeWebhook();
        // A runner's own practice spot is not posted (the default; /admin's PB posts tab).
        var own = TestSegment("s-aaaaaaaaaaaa");
        await Upload(ta, Bundle(own, RunText(own, A, 10f, 5f)));
        Assert.Empty(hook.Recent);

        // A run under review (far under the route's best of 3+) posts nothing.
        await Upload(tb, Bundle(com, RunText(com, B, 10f, 5f, 1), RunText(com, B, 10.5f, 5f, 2)));
        Assert.Single(hook.Recent);   // B's first run
        await Upload(tb, Bundle(com, RunText(com, B, 3f, 1f, 3)));
        Assert.Single(hook.Recent);

        // A failing webhook never fails an upload.
        discord.Answer = _ => throw new HttpRequestException("down");
        Assert.Equal(HttpStatusCode.OK, (await Upload(ta, Bundle(com, RunText(com, A, 9f, 5f, 4)))).StatusCode);
    }

    [Fact]
    public void PbNews_Decisions()
    {
        Assert.Equal(9f, PbNews.NewPb(float.NaN, new[] { (9f, false), (10f, false) }));
        Assert.Equal(9f, PbNews.NewPb(9.5f, new[] { (9f, false) }));
        Assert.Null(PbNews.NewPb(9f, new[] { (9f, false) }));          // a tie is not a PB
        Assert.Null(PbNews.NewPb(8f, new[] { (9f, false) }));
        Assert.Null(PbNews.NewPb(10f, new[] { (5f, true), (9f, false) }));   // the fastest is under review
        Assert.Null(PbNews.NewPb(10f, Array.Empty<(float, bool)>()));

        Assert.True(PbNews.Announces(true, "", null));
        Assert.False(PbNews.Announces(false, "", _ => true));
        Assert.False(PbNews.Announces(false, "Any%", _ => false));
        Assert.True(PbNews.Announces(false, " Any% ", c => c == "Any%"));

        Assert.Equal("1:02.345", PbNews.Time(62.345));
        Assert.Equal("1:00:00.000", PbNews.Time(3600));
        Assert.Equal("0.250", PbNews.Time(0.25));
        Assert.Equal("@everyone \\[x\\]\\(http\\://e\\) \\_a\\_ b", PbNews.Escape("@everyone [x](http://e) _a_\nb"));
        string url = PbNews.RunLink("https://x/", "s-1", "r", 7);
        var post = PbNews.Embed(new Runs.PbFound("deter", "Cave 5", "s-1", "r", 7, 59f, 60f, true, "Any%", 1, 3, 61.5f, true), url,
                                new DateTime(2026, 10, 10, 12, 30, 0, DateTimeKind.Utc));
        const string expected = @"{""content"":""Cave 5 WR: deter 59.000"",""embeds"":[{""author"":{""name"":""deter \u00B7 new PB""},""title"":""Cave 5"",""url"":""https://x/spot/s-1/r?run=7"",""description"":""**59.000**\n**1.000** faster than 1:00.000"",""color"":3055195,""footer"":{""text"":""Run category""},""fields"":[{""name"":""Category"",""value"":""Any%"",""inline"":true},{""name"":""Rank"",""value"":""#1 of 3 runners"",""inline"":true},{""name"":""Next best"",""value"":""1:01.500 (+2.500)"",""inline"":true}],""timestamp"":""2026-10-10T12:30:00Z""}]}";
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), post), post.ToJsonString());
        // A runner's spot, first run, not first on the spot: marked, no category, no PB line.
        var other = PbNews.Embed(new Runs.PbFound("deter", "Mine", "s-2", "r", 8, 20f, float.NaN, false, "", 2, 2, 19f), url);
        Assert.Equal("deter \u00B7 first run on a runner's spot", Author(other));
        Assert.Equal("Runner's spot", Foot(other));
        Assert.Null(E(other)["timestamp"]);
        Assert.Null(Field(other, "Category"));
        Assert.Equal("19.000 (you: +1.000)", Field(other, "Spot record"));
        Assert.Equal("Mine first run: deter 20.000", (string)other["content"]);
        // The plugin's default labels and "Community" are not categories.
        foreach (string dull in new[] { "", "My spots", "my spots", "Segments", "Spots", "Community", "  " })
            Assert.False(PbNews.RealCategory(dull), dull);
        Assert.True(PbNews.RealCategory("Cave 5"));
    }

    // --- PB posts from runners' own spots (T-0232) -----------------------------------------

    private Task<HttpResponseMessage> PutPbPosts(string json, string token = "admin-secret") =>
        AdminSend(HttpMethod.Put, "/api/admin/pbposts", token, json);

    private static string PbPostsJson(bool runnerSpots, string channel, string webhook) =>
        new JsonObject { ["runnerSpots"] = runnerSpots, ["channel"] = channel, ["webhook"] = webhook }.ToJsonString();

    [Fact]
    public async Task Webhook_RunnerSpots_OffThenSameChannelThenOwnChannel()
    {
        FakeDiscord discord = FakeWebhook();
        var posts = _factory.Services.GetRequiredService<PbPosts>();
        string ta = await Register(A);
        var own = TestSegment("s-eeeeeeeeeeee");
        own.Name = "My *dash*";
        var com = TestSegment("s-ffffffffffff");
        MakeCommunity(com);

        // Off until the owner saves it on.
        await Upload(ta, Bundle(own, RunText(own, A, 10f, 5f, 1)));
        Assert.Empty(posts.RunnerSpots.Recent);

        // The same channel as community / run spot PBs - through their own sender (own caps).
        Assert.Equal(HttpStatusCode.OK, (await PutPbPosts(PbPostsJson(true, "same", ""))).StatusCode);
        await Upload(ta, Bundle(own, RunText(own, A, 9f, 5f, 2)));
        Assert.Single(posts.RunnerSpots.Recent);
        Assert.Empty(posts.Official.Recent);
        var marked = posts.RunnerSpots.Recent[0];
        Assert.Equal("My \\*dash\\*", (string)E(marked)["title"]);
        Assert.Equal("**9.000**\n**1.000** faster than 10.000", Desc(marked));
        // Marked apart from a main-category PB: the author line, the footer and the colour.
        Assert.Contains("new PB on a runner's spot", Author(marked));
        Assert.Equal("Runner's spot", Foot(marked));
        Assert.Equal(PbNews.RunnerSpotColour, (int)E(marked)["color"]);
        await Until(() => discord.Contents().Count >= 1);
        SamePosts(posts.RunnerSpots.Recent, discord.Contents(OfficialHook));

        // Their own channel: runners' spots there, community spots still in the official one.
        Assert.Equal(HttpStatusCode.OK, (await PutPbPosts(PbPostsJson(true, "own", OwnHook))).StatusCode);
        await Upload(ta, Bundle(own, RunText(own, A, 8f, 4f, 3)));
        await Upload(ta, Bundle(com, RunText(com, A, 10f, 5f, 4)));
        await Until(() => discord.Contents().Count >= 3);
        var ownPost = Assert.Single(discord.Contents(OwnHook));
        Assert.StartsWith("**8.000**", Desc(ownPost));
        Assert.Equal("Runner's spot", Foot(ownPost));   // marked apart also in their own channel
        Assert.Equal(2, discord.Contents(OfficialHook).Count);
        Assert.Contains("first run", Author(discord.Contents(OfficialHook)[1]));
        Assert.Equal("Community spot", Foot(discord.Contents(OfficialHook)[1]));
        Assert.Equal(PbNews.CommunityColour, (int)E(discord.Contents(OfficialHook)[1])["color"]);
        Assert.Contains("first run: ", (string)discord.Contents(OfficialHook)[1]["content"]);
        Assert.All(discord.Posts, p => Assert.Empty(p.body["allowed_mentions"]["parse"].AsArray()));

        // Off again: nothing more; the webhook is kept for next time.
        Assert.Equal(HttpStatusCode.OK, (await PutPbPosts(PbPostsJson(false, "own", OwnHook))).StatusCode);
        await Upload(ta, Bundle(own, RunText(own, A, 7f, 4f, 5)));
        Assert.Equal(2, posts.RunnerSpots.Recent.Count);
        var view = (await Admin("/api/admin/pbposts")).AsObject();
        Assert.False((bool)view["settings"]["runnerSpots"]);
        Assert.Equal(OwnHook, (string)view["settings"]["webhook"]);
        Assert.True((bool)view["official"]);
    }

    [Fact]
    public async Task PbPostsSettings_OwnerOnly_Validated_SecretKeptOutOfTheLog()
    {
        var posts = _factory.Services.GetRequiredService<PbPosts>();
        var made = await (await AdminSend(HttpMethod.Post, "/api/admin/admins", body: "maks")).Content.ReadFromJsonAsync<JsonObject>();
        string named = (string)made["token"];

        // The owner only: a named admin neither reads the secret nor changes it.
        Assert.Equal(HttpStatusCode.Forbidden, (await AdminSend(HttpMethod.Get, "/api/admin/pbposts", named)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutPbPosts(PbPostsJson(true, "same", ""), named)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AdminSend(HttpMethod.Get, "/api/admin/pbposts", "")).StatusCode);

        // Before a save: off, the same channel, no webhook; the official one is off in tests.
        var view = (await Admin("/api/admin/pbposts")).AsObject();
        Assert.False((bool)view["settings"]["runnerSpots"]);
        Assert.Equal("same", (string)view["settings"]["channel"]);
        Assert.Equal(0, (int)view["rev"]);
        Assert.False((bool)view["official"]);

        // Refused: nothing saved.
        foreach (string bad in new[]
        {
            "not json", "[]", "{\"runnerSpots\":\"yes\"}", "{\"channel\":\"same\"}", PbPostsJson(true, "elsewhere", ""),
            PbPostsJson(true, "own", ""),                                                  // their own channel needs one
            PbPostsJson(true, "own", "https://example.com/api/webhooks/1/x"),              // only Discord (no SSRF)
            PbPostsJson(true, "own", "http://discord.com/api/webhooks/1/x"),
            PbPostsJson(true, "own", "https://discord.com.evil.io/api/webhooks/1/x"),
            PbPostsJson(true, "own", "https://discord.com/api/webhooks/1/x?wait=true"),
            PbPostsJson(false, "same", "https://discord.com/api/webhooks/1/x/../../../x"),
            "{\"runnerSpots\":true,\"url\":\"x\"}",
        })
            Assert.Equal(HttpStatusCode.BadRequest, (await PutPbPosts(bad)).StatusCode);
        Assert.Equal(0, (int)(await Admin("/api/admin/pbposts"))["rev"]);
        Assert.False(posts.Settings.Current.RunnerSpots);

        // Discord's other hosts and a versioned path are webhooks too; spaces around are trimmed.
        foreach (string good in new[] { "https://canary.discord.com/api/v10/webhooks/1/x", "https://discordapp.com/api/webhooks/1/x", " " + OwnHook + " " })
            Assert.Equal(HttpStatusCode.OK, (await PutPbPosts(PbPostsJson(true, "own", good))).StatusCode);
        Assert.Equal(3, (int)(await Admin("/api/admin/pbposts"))["rev"]);
        Assert.Equal(OwnHook, posts.Settings.Current.RunnerSpotsWebhook);

        // Kept across a restart (read back from the database).
        var again = new PbSettings(_factory.Services.GetRequiredService<Store>());
        Assert.Equal(new PbSettings.Values(true, true, OwnHook), again.Current);

        // The activity log names the save, never the URL; the public API has no trace of it.
        string log = (await Admin("/api/admin/log")).ToJsonString();
        Assert.Contains("PUT /api/admin/pbposts", log);
        Assert.DoesNotContain("run-spot_TOKEN", log);
        Assert.DoesNotContain("run-spot_TOKEN", await _http.GetStringAsync("/api/spots"));
    }

    [Fact]
    public async Task Webhook_Sender_RetriesA429_AndLogsNoUrl()
    {
        var fake = new FakeDiscord();
        int calls = 0;
        fake.Answer = _ =>
        {
            if (Interlocked.Increment(ref calls) > 1) return new HttpResponseMessage(HttpStatusCode.NoContent);
            var limited = new HttpResponseMessage((HttpStatusCode)429);
            limited.Headers.Add("Retry-After", "1");
            return limited;
        };
        var logged = new List<string>();
        var hook = new PbWebhook(OwnHook, null, "Discord webhook (runners' spots)")
        {
            Http = new HttpClient(fake), Gap = TimeSpan.FromMilliseconds(10), Log = m => { lock (logged) logged.Add(m); },
        };

        // A 429: one retry after Discord's retry-after, the same post again.
        Assert.True(hook.Enqueue(Msg("first")));
        await Until(() => fake.Posts.Count >= 2);
        Assert.Equal(new[] { "first", "first" }, fake.Contents(OwnHook).Select(Desc));
        Assert.Empty(logged);

        // A refused post is dropped and logged - by the sender's name, never its URL.
        fake.Answer = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        Assert.True(hook.Enqueue(Msg("second")));
        await Until(() => logged.Count >= 1);
        Assert.Equal("Discord webhook (runners' spots): post failed (HTTP 404), dropped.", Assert.Single(logged));
        fake.Answer = _ => throw new HttpRequestException("Connection refused (discord.com:443)");
        Assert.True(hook.Enqueue(Msg("third")));
        await Until(() => logged.Count >= 2);
        Assert.Equal(2, logged.Count);
        Assert.All(logged, l => Assert.DoesNotContain("TOKEN", l));

        // No URL: off, nothing queued.
        Assert.False(new PbWebhook("", null).Enqueue(Msg("nothing")));
    }

    [Fact]
    public void PbNews_Target()
    {
        var off = PbSettings.Values.Off;
        var same = new PbSettings.Values(true, false, OwnHook);
        var own = new PbSettings.Values(true, true, OwnHook);
        // Community / run spots: the official webhook, whatever runners' spots do.
        Assert.Equal(OfficialHook, PbNews.Target(true, off, OfficialHook));
        Assert.Equal(OfficialHook, PbNews.Target(true, own, OfficialHook));
        Assert.Null(PbNews.Target(true, own, ""));
        // Runners' spots: off, the same channel, their own.
        Assert.Null(PbNews.Target(false, off, OfficialHook));
        Assert.Null(PbNews.Target(false, new PbSettings.Values(false, true, OwnHook), OfficialHook));
        Assert.Equal(OfficialHook, PbNews.Target(false, same, OfficialHook));
        Assert.Null(PbNews.Target(false, same, ""));                 // the same channel, and there is none
        Assert.Equal(OwnHook, PbNews.Target(false, own, ""));        // their own works without the official one
        Assert.Null(PbNews.Target(false, new PbSettings.Values(true, true, ""), OfficialHook));
    }

    // --- security audit (2026-10-04) -------------------------------------------------

    [Fact]
    public async Task OwnerDelete_OfALookAlikeId_LeavesTheOtherSpotsRunFiles()
    {
        string ta = await Register(A), tb = await Register(B);
        var seg = TestSegment("s-0123456789ab");
        await Upload(ta, Bundle(seg, RunText(seg, A, 10f, 5f)));
        long runId = (await _http.GetFromJsonAsync<JsonObject>("/api/spots/" + seg.Id))["routes"][0]["board"][0]["id"].GetValue<long>();

        // B's own spot whose id makes the same folder name (the dot is trimmed).
        var twin = TestSegment(".s-0123456789ab");
        Assert.Equal(Store.SafeName(seg.Id), Store.SafeName(twin.Id));
        Assert.Equal(HttpStatusCode.OK, (await Upload(tb, Bundle(twin, RunText(twin, B, 12f, 5f)))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await DeleteSpot(tb, twin.Id)).StatusCode);

        // A's run is still downloadable: only B's file went.
        var file = await _http.GetAsync("/api/runs/" + runId + "/file");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Contains("duration", (await file.Content.ReadAsStringAsync()).ToLowerInvariant());
        var detail = await _http.GetFromJsonAsync<JsonObject>("/api/runs/" + runId);
        Assert.True(detail["path"].AsArray().Count > 0);
    }

    [Fact]
    public async Task LinkPreview_DollarSignsInRunnerText_AreText()
    {
        string ta = await Register(A);
        var seg = TestSegment("s-0123456789ac");
        seg.Name = "$_ $` $' $$ $0";
        await Upload(ta, Bundle(seg, RunText(seg, A, 10f, 5f)));
        string html = await _http.GetStringAsync("/spot/" + seg.Id);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "<script src=\"/app\\.js"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "<title>"));
        Assert.Contains("<title>$_ $` $&#39; $$ $0 - Forest Practice Runs</title>", html);
    }

    [Fact]
    public void PbPost_ClipsLongNames()
    {
        var post = PbNews.Embed(new Runs.PbFound(new string('r', 500) + "\n@everyone", new string('s', 100_000), "s-1", "r", 7, 59f, 60f,
                                                 true, new string('c', 5000), 1, 2, 70f), "https://x/spot/s-1/r?run=7");
        string m = post.ToJsonString();
        Assert.True(m.Length < 900, "post is " + m.Length + " characters");
        Assert.Equal(new string('s', 80), (string)E(post)["title"]);
        Assert.Equal(PbNews.MaxRunner, Author(post).IndexOf(" \u00B7 "));
        Assert.Equal(new string('c', 40), Field(post, "Category"));
    }

    [Fact]
    public async Task Webhook_CapsEachRunnersPostsAnHour()
    {
        FakeWebhook();
        var hook = _factory.Services.GetRequiredService<PbWebhook>();
        DateTime now = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
        hook.Now = () => now;
        string ta = await Register(A), tb = await Register(B);
        var seg = TestSegment("s-dddddddddddd");
        MakeCommunity(seg);

        // A sets a PB after PB: five posts, then nothing for the hour.
        for (int i = 0; i < 7; i++)
            Assert.Equal(HttpStatusCode.OK, (await Upload(ta, Bundle(seg, RunText(seg, A, 20f - i, 5f, i)))).StatusCode);
        Assert.Equal(PbWebhook.PerRunnerPerHour, hook.Recent.Count);
        // Another runner still posts.
        await Upload(tb, Bundle(seg, RunText(seg, B, 30f, 5f, 1)));
        Assert.Equal(PbWebhook.PerRunnerPerHour + 1, hook.Recent.Count);
        Assert.Contains("Runner 0000", Author(hook.Recent[^1]));
        // An hour on, A posts again.
        now = now.AddMinutes(61);
        await Upload(ta, Bundle(seg, RunText(seg, A, 13f, 2f, 20)));
        Assert.Equal(PbWebhook.PerRunnerPerHour + 2, hook.Recent.Count);
    }

    [Fact]
    public async Task LinkPreview_NamesTheSitesAddress_NotTheHostHeader()
    {
        string ta = await Register(A);
        var seg = TestSegment("s-0123456789ad");
        await Upload(ta, Bundle(seg, RunText(seg, A, 10f, 5f)));
        foreach (string path in new[] { "/spot/" + seg.Id, "/compare" })
        {
            var msg = new HttpRequestMessage(HttpMethod.Get, path);
            msg.Headers.Host = "evil.example";
            string html = await (await _http.SendAsync(msg)).Content.ReadAsStringAsync();
            Assert.Contains("og:url\" content=\"https://forest.deter.cloud/", html);
            Assert.DoesNotContain("evil.example", html);
        }
    }

    [Fact]
    public async Task Upload_SegmentIdsAreCapped_OldAndRandomOnesFit()
    {
        string ta = await Register(A);
        // The plugin's ids - random (12 to 32 hex) and legacy slugs - go in.
        foreach (string id in new[] { "s-0123456789ae", "s-" + new string('f', 32), "spot.my.new-spot-3",
                                      "spot." + new string('c', 30) + "." + new string('n', 40) })
        {
            var seg = TestSegment(id);
            Assert.True(id.Length <= Runs.MaxSegmentId, id);
            Assert.Equal(HttpStatusCode.OK, (await Upload(ta, Bundle(seg, RunText(seg, A, 10f, 5f)))).StatusCode);
        }
        // Past the cap: refused (400 - the plugin sets the file aside, no retry),
        // and nothing is stored.
        var tooLong = TestSegment("s-" + new string('a', Runs.MaxSegmentId));
        var r = await Upload(ta, Bundle(tooLong, RunText(tooLong, A, 10f, 5f)));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("segment id", await r.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/api/spots/" + tooLong.Id)).StatusCode);
        Assert.Equal(UploadOutcome.Refused, SiteProtocol.Classify(400));
    }

    [Theory]
    [InlineData("ground-dry/3/12_7.jpg", true)]
    [InlineData("ground/3/12_7.jpg\n", false)]
    [InlineData("ground/3/../../12_7.jpg", false)]
    public void AerialUploadPaths(string path, bool allowed) => Assert.Equal(allowed, UploadPath.IsTile(path));

    /// scripts/site-smoke.py seeds the throwaway site with smoke-run.foseg;
    /// this keeps it uploadable as the formats move. Rewrite it with
    /// FOREST_WRITE_SMOKE_FIXTURE=1 dotnet test --filter SmokeFixture.
    [Fact]
    public async Task SmokeFixture_Uploads()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "site", "ForestSite.Tests"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        string path = Path.Combine(dir, "site", "ForestSite.Tests", "smoke-run.foseg");
        if (Environment.GetEnvironmentVariable("FOREST_WRITE_SMOKE_FIXTURE") == "1")
        {
            Segment seg = TestSegment("s-5a0ce0000001");
            File.WriteAllText(path, Bundle(seg, RunText(seg, A, 12.5f, 6.25f)));
        }
        var r = await Upload(await Register(A), File.ReadAllText(path));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Single((await r.Content.ReadFromJsonAsync<JsonObject>())["added"].AsArray());
    }
}
