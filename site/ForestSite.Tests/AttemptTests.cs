using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using ForestOverlay.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ForestSite.Tests;

// ------------------------------------------------------------------
// Run mode attempts (phase 2): the judgement against what the server saw,
// and the plugin's calls end to end.
// ------------------------------------------------------------------
[Collection("site")]
public sealed class AttemptTests : IDisposable
{
    private const string Id = "a-0123456789abcdef";
    private const string Runner = "r-00000000000000aa";
    private const long Issued = 1_000_000_000;

    /// An attempt of `minutes`, a step a second, the nonce folded at 300 ms.
    private static AttemptChain Chain(int seconds, string nonce = "n1", bool end = true, long nonceMs = 300)
    {
        var c = new AttemptChain();
        c.Header(Id, Runner, "Runner", "test", "Any%", "s-0123456789ab", "h", "seed", DateTime.UtcNow);
        for (int s = 1; s <= seconds; s++)
        {
            if (nonce != null && nonceMs < s * 1000L && c.Steps == (int)(nonceMs / 1000)) c.Nonce(nonceMs, nonce);
            c.Step(s * 1000L, -1, true, s, 0, 0);
        }
        if (end) c.End(seconds * 1000L + 100, "reset", -1);
        return c;
    }

    /// Checkpoints every 60 steps, received when real time says they were sent.
    private static List<Attempts.Cp> Cps(AttemptChain.Replay r, long nonceMs = 300, long late = 200, int every = 60)
    {
        var list = new List<Attempts.Cp>();
        for (int n = every; n <= r.Steps.Count; n += every)
        {
            var s = r.Step(n);
            list.Add(new Attempts.Cp(n, s.Head, Issued + (s.RealMs - nonceMs) + late));
        }
        return list;
    }

    private static long LogAt(AttemptChain.Replay r) => Issued + (r.EndMs - r.NonceMs) + 500;

    [Fact]
    public void Online_CheckpointsMatch_Green()
    {
        var r = AttemptChain.Read(Chain(300).Text);
        var (v, why) = Attempts.Judge(r, "n1", Issued, Cps(r), LogAt(r));
        Assert.Equal("green", v);
        Assert.Contains("5 checkpoint(s)", why[0]);
    }

    [Fact]
    public void Offline_Amber()
    {
        var r = AttemptChain.Read(Chain(300, nonce: null).Text);
        var (v, why) = Attempts.Judge(r, null, 0, new List<Attempts.Cp>(), Issued);
        Assert.Equal("amber", v);
        Assert.Contains("offline", why[0]);
    }

    [Fact]
    public void ASplicedLog_Red()
    {
        // The checkpoints were sent by the real attempt; the uploaded log
        // swaps its second half for another attempt's positions.
        var real = AttemptChain.Read(Chain(300).Text);
        string spliced = Chain(300).Text.Replace("step|200|200000|-|20000|", "step|200|200000|-|20001|");
        var r = AttemptChain.Read(spliced);
        Assert.Null(r.Error);
        var (v, why) = Attempts.Judge(r, "n1", Issued, Cps(real), LogAt(r));
        Assert.Equal("red", v);
        Assert.Contains(why, w => w.Contains("does not match") && w.Contains("4:00"));
    }

    [Fact]
    public void AnotherNonce_Red_NoNonce_Amber()
    {
        var other = AttemptChain.Read(Chain(100, nonce: "n2").Text);
        Assert.Equal("red", Attempts.Judge(other, "n1", Issued, new List<Attempts.Cp>(), LogAt(other)).verdict);

        var none = AttemptChain.Read(Chain(100, nonce: null).Text);
        Assert.Equal("amber", Attempts.Judge(none, "n1", Issued, new List<Attempts.Cp>(), Issued + 100_000).verdict);
        // A nonce the server never gave (an offline attempt with one made up).
        Assert.Equal("red", Attempts.Judge(other, null, 0, new List<Attempts.Cp>(), Issued).verdict);
    }

    [Fact]
    public void ALogMadeFasterThanRealTime_Red()
    {
        // The log claims 5 minutes, but the server received the 5-minute
        // checkpoint 2 minutes after the nonce.
        var r = AttemptChain.Read(Chain(300).Text);
        var cps = Cps(r).Select(c => c with { ReceivedMs = c.ReceivedMs - 180_000 }).ToList();
        var (v, why) = Attempts.Judge(r, "n1", Issued, cps, LogAt(r));
        Assert.Equal("red", v);
        Assert.Contains(why, w => w.Contains("ahead of real time"));
    }

    [Fact]
    public void AGap_Amber()
    {
        // Checkpoints at 1, 2, and then none until 6 minutes (the network went).
        var r = AttemptChain.Read(Chain(400).Text);
        var cps = Cps(r).Where(c => c.Step <= 120 || c.Step >= 360).ToList();
        var (v, why) = Attempts.Judge(r, "n1", Issued, cps, LogAt(r));
        Assert.Equal("amber", v);
        Assert.Contains(why, w => w.Contains("2:00-6:00"));
    }

    [Fact]
    public void TheLogsArrival_LateIsFine_EarlyIsRed()
    {
        var r = AttemptChain.Read(Chain(300).Text);
        // Uploaded a day later (queued offline): still green.
        Assert.Equal("green", Attempts.Judge(r, "n1", Issued, Cps(r), Issued + 86_400_000).verdict);
        // The last checkpoint 4:00, the end 7:00: the tail is the video's.
        var longer = AttemptChain.Read(Chain(420).Text);
        var (v, why) = Attempts.Judge(longer, "n1", Issued, Cps(longer).Where(c => c.Step <= 240).ToList(), LogAt(longer));
        Assert.Equal("amber", v);
        Assert.Contains(why, w => w.Contains("4:00-7:00"));
        // A 5-minute log that arrives a minute after the start was made up.
        Assert.Equal("red", Attempts.Judge(r, "n1", Issued, new List<Attempts.Cp>(), Issued + 60_000).verdict);
    }

    [Fact]
    public void ALateNonce_AndNoEnd_Amber()
    {
        var r = AttemptChain.Read(Chain(100, nonceMs: 40_300).Text);
        var (v, why) = Attempts.Judge(r, "n1", Issued, new List<Attempts.Cp>(), LogAt(r));
        Assert.Equal("amber", v);
        Assert.Contains(why, w => w.Contains("arrived 0:40"));

        var open = AttemptChain.Read(Chain(100, end: false).Text);
        Assert.Contains(Attempts.Judge(open, "n1", Issued, new List<Attempts.Cp>(), Issued + 100_000).why, w => w.Contains("no end"));
    }

    [Fact]
    public void RunModeFlags_Red()
    {
        var c = new AttemptChain();
        c.Header(Id, Runner, "Runner", "test", "Any%", "-", "-", "seed", DateTime.UtcNow);
        c.Nonce(100, "n1");
        c.Step(1000, -1, false, 0, 0, 0);
        c.Flag(1500, "the test bridge is on");
        c.End(2000, "reset", -1);
        var r = AttemptChain.Read(c.Text);
        var (v, why) = Attempts.Judge(r, "n1", Issued, new List<Attempts.Cp>(), Issued + 2000);
        Assert.Equal("red", v);
        Assert.Contains("the test bridge is on", why[0]);
    }

    // --- the API ------------------------------------------------------------

    private readonly string _data;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _http;

    public AttemptTests()
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

    private async Task<string> Register(string runner)
    {
        var r = await _http.PostAsJsonAsync("/api/register", new { runner, name = "Runner" });
        return (await r.Content.ReadFromJsonAsync<JsonObject>())["token"].GetValue<string>();
    }

    private async Task<HttpResponseMessage> Post(string token, string path, HttpContent body)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, path) { Content = body };
        if (token != null) msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _http.SendAsync(msg);
    }

    [Fact]
    public async Task StartCheckpointLog_Green_ThenImmutable()
    {
        string token = await Register(Runner);
        var start = await Post(token, "/api/attempts", JsonContent.Create(new { attempt = Id, category = "Any%", spot = "s-0123456789ab" }));
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        string nonce = (await start.Content.ReadFromJsonAsync<JsonObject>())["nonce"].GetValue<string>();
        Assert.Equal(32, nonce.Length);

        // A retry gets the same nonce; another runner cannot take the id.
        var again = await Post(token, "/api/attempts", JsonContent.Create(new { attempt = Id, category = "Any%", spot = "" }));
        Assert.Equal(nonce, (await again.Content.ReadFromJsonAsync<JsonObject>())["nonce"].GetValue<string>());
        string other = await Register("r-00000000000000bb");
        Assert.Equal(HttpStatusCode.Conflict, (await Post(other, "/api/attempts", JsonContent.Create(new { attempt = Id }))).StatusCode);

        var c = new AttemptChain();
        c.Header(Id, Runner, "Runner", "test", "Any%", "s-0123456789ab", "h", "seed", DateTime.UtcNow);
        c.Nonce(50, nonce);
        c.Step(1000, -1, true, 1, 2, 3);
        c.Step(2000, 100, true, 1, 2, 4);
        var cp = await Post(token, "/api/attempts/" + Id + "/checkpoints", JsonContent.Create(new { step = 2, head = c.Head }));
        Assert.Equal(HttpStatusCode.OK, cp.StatusCode);
        // Not yours / too soon.
        Assert.Equal(HttpStatusCode.NotFound, (await Post(other, "/api/attempts/" + Id + "/checkpoints", JsonContent.Create(new { step = 2, head = c.Head }))).StatusCode);
        Assert.Equal((HttpStatusCode)429, (await Post(token, "/api/attempts/" + Id + "/checkpoints", JsonContent.Create(new { step = 3, head = c.Head }))).StatusCode);

        c.Step(3000, 1100, true, 1, 2, 5);
        c.End(3100, "finished", 1150);
        string log = c.Text + AttemptChain.ReportMarker + "\nclean\n";
        var up = await Post(token, "/api/attempts/" + Id + "/log", new StringContent(log, Encoding.UTF8, "text/plain"));
        Assert.Equal(HttpStatusCode.OK, up.StatusCode);
        Assert.Equal("green", (await up.Content.ReadFromJsonAsync<JsonObject>())["verdict"].GetValue<string>());

        // The same log again is fine; a different one never replaces it.
        Assert.Equal(HttpStatusCode.OK, (await Post(token, "/api/attempts/" + Id + "/log", new StringContent(log, Encoding.UTF8, "text/plain"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(token, "/api/attempts/" + Id + "/log", new StringContent(log + "x\n", Encoding.UTF8, "text/plain"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(token, "/api/attempts/" + Id + "/checkpoints", JsonContent.Create(new { step = 3, head = c.Head }))).StatusCode);

        // Public: the view, the log, a code from the video.
        var view = await _http.GetFromJsonAsync<JsonObject>("/api/attempts/" + Id);
        Assert.Equal("green", view["verdict"].GetValue<string>());
        Assert.Equal("finished", view["endReason"].GetValue<string>());
        Assert.Equal(1150, view["finalTimerMs"].GetValue<long>());
        Assert.Equal("clean", view["report"].GetValue<string>());
        Assert.Equal(log, await _http.GetStringAsync("/api/attempts/" + Id + "/log"));
        var found = await _http.GetFromJsonAsync<JsonObject>("/api/attempts/" + Id + "/code/" + c.Code.ToLowerInvariant());
        Assert.Contains(found["matches"].AsArray(), m => m["step"].GetValue<int>() == 3);

        // Only the owner deletes one.
        var del = new HttpRequestMessage(HttpMethod.Delete, "/api/admin/attempts/" + Id);
        del.Headers.Add("X-Admin-Token", "admin-secret");
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(del)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/api/attempts/" + Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/api/attempts/" + Id + "/log")).StatusCode);
    }

    [Fact]
    public async Task AnOfflineLog_IsTaken_Amber()
    {
        string token = await Register(Runner);
        var c = Chain(5, nonce: null);
        var up = await Post(token, "/api/attempts/" + Id + "/log", new StringContent(c.Text, Encoding.UTF8, "text/plain"));
        Assert.Equal(HttpStatusCode.OK, up.StatusCode);
        Assert.Equal("amber", (await up.Content.ReadFromJsonAsync<JsonObject>())["verdict"].GetValue<string>());

        // A log naming someone else, a damaged log, a bad id.
        string other = await Register("r-00000000000000bb");
        var stolen = await Post(other, "/api/attempts/a-00000000000000ff/log", new StringContent(c.Text.Replace(Id, "a-00000000000000ff"), Encoding.UTF8, "text/plain"));
        Assert.Equal(HttpStatusCode.BadRequest, stolen.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(token, "/api/attempts/a-00000000000000fe/log", new StringContent("forest-attempt 1\nnope\n", Encoding.UTF8, "text/plain"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(token, "/api/attempts/a-..%2F..%2Fx/log", new StringContent(c.Text, Encoding.UTF8, "text/plain"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(null, "/api/attempts/" + Id + "/log", new StringContent(c.Text, Encoding.UTF8, "text/plain"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/api/attempts/a-00000000000000fe")).StatusCode);
    }
}
