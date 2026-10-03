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

    [Fact]
    public void Moves_NeverChangeTheVerdict()
    {
        var c = new AttemptChain();
        c.Header(Id, Runner, "Runner", "test", "Any%", "-", "-", "seed", DateTime.UtcNow);
        c.Nonce(100, "n1");
        c.Step(1000, -1, true, 0, 0, 0);
        c.Move(1500, "bomb-boost", true, 1, 2, 3, "game time stopped 2.00 s");
        c.Move(1600, "huge-speed", true, 1, 2, 3, "1,500 m/s");
        c.End(2000, "reset", -1);
        var r = AttemptChain.Read(c.Text);
        Assert.Null(r.Error);
        var (v, why) = Attempts.Judge(r, "n1", Issued, new List<Attempts.Cp>(), Issued + 2000);
        Assert.Equal("green", v);
        Assert.DoesNotContain(why, w => w.Contains("boost") || w.Contains("m/s"));
        var cat = RunCategory.Parse("[category]\nid = any\nname = Any%\nbanned = The explosives glitch\n").Single();
        var (ov, _) = Attempts.Overall(v, why, Report(), new HashSet<(string, string)>(), cat);
        Assert.Equal("green", ov);
    }

    [Fact]
    public void MoveNotes_NameTheBannedMoveTheyMayBe()
    {
        var cat = RunCategory.Parse("[category]\nid = any\nname = Any%\nbanned = No log boosting\nbanned = The Explosives glitch\n").Single();
        var moves = new List<AttemptChain.MoveInfo>
        {
            new() { RealMs = 1, Kind = "bomb-boost", Detail = "d", HasPos = true, X = 1 },
            new() { RealMs = 2, Kind = "huge-speed", Detail = "d" },
            new() { RealMs = 3, Kind = "something-new", Detail = "d" },
        };
        var notes = Attempts.MoveNotes(moves, cat);
        Assert.Equal("The Explosives glitch", notes[0].MaybeBanned);   // not "log boosting": a bomb boost is not a log boost
        Assert.Equal("Bomb boost", notes[0].Label);
        Assert.Null(notes[1].MaybeBanned);
        Assert.Equal("something-new", notes[2].Label);                 // a kind from a newer plugin still shows
        Assert.Null(Attempts.MoveNotes(moves, null)[0].MaybeBanned);
        var noBomb = RunCategory.Parse("[category]\nid = g\nname = Glitched\nbanned = No OOB\n").Single();
        Assert.Null(Attempts.MoveNotes(moves, noBomb)[0].MaybeBanned);

        var caves = RunCategory.Parse("[category]\nid = gl\nname = Glitchless\nbanned = No bomb boosting\nbanned = Cave force loads\n").Single();
        var cave = Attempts.MoveNotes(new[] { new AttemptChain.MoveInfo { RealMs = 4, Kind = "cave-force-load", Detail = "d" } }, caves)[0];
        Assert.Equal("Cave state force load", cave.Label);
        Assert.Equal("Cave force loads", cave.MaybeBanned);

        var falls = RunCategory.Parse("[category]\nid = gl\nname = Glitchless\nbanned = No waterfall clips\nbanned = Fall damage cancels\n").Single();
        var fall = Attempts.MoveNotes(new[] { new AttemptChain.MoveInfo { RealMs = 5, Kind = "fall-damage-cancel", Detail = "d" } }, falls)[0];
        Assert.Equal("Fall damage cancel", fall.Label);
        Assert.Equal("Fall damage cancels", fall.MaybeBanned);   // not "waterfall"

        var lift = Attempts.MoveNotes(new[] { new AttemptChain.MoveInfo { RealMs = 6, Kind = "lift", Detail = "d" } }, cat)[0];
        Assert.Equal("Lift out of a solid", lift.Label);
        Assert.Equal("No log boosting", lift.MaybeBanned);
        var clip = Attempts.MoveNotes(new[] { new AttemptChain.MoveInfo { RealMs = 7, Kind = "clip", Detail = "d" } }, falls)[0];
        Assert.Equal("Clip through a solid", clip.Label);
        Assert.Equal("No waterfall clips", clip.MaybeBanned);
        Assert.Null(Attempts.MoveNotes(new[] { new AttemptChain.MoveInfo { RealMs = 8, Kind = "clip", Detail = "d" } }, cat)[0].MaybeBanned);
    }

    // --- what ran: the report ------------------------------------------------

    private static string Report(Action<RunReport> change = null)
    {
        var r = new RunReport { Attempt = 1, Started = "Normal", PluginVersion = "test", GameHash = RunReport.KnownGameHashes[0] };
        change?.Invoke(r);
        return r.Format();
    }

    private static readonly ISet<(string, string)> None = new HashSet<(string, string)>();

    [Fact]
    public void Report_Clean_Green_NoReport_Amber()
    {
        var (v, f) = Attempts.JudgeReport(Report(), None);
        Assert.Equal("green", v);
        Assert.All(f, x => Assert.Equal("ok", x.Level));
        Assert.Equal("amber", Attempts.JudgeReport("", None).verdict);
        Assert.Equal("amber", Attempts.JudgeReport(Report(r => r.GameHash = ""), None).verdict);
        Assert.Equal("red", Attempts.JudgeReport(Report(r => r.GameHash = "0123"), None).verdict);
    }

    [Fact]
    public void Report_AnotherMod_Red_UnlessAllowed()
    {
        string text = Report(r =>
        {
            r.OtherPlugins.Add("Other 1.0 (other.dll)");
            r.ForeignPatches.Add("PlayerStats.Update (by other.mod)");
            r.ForeignPatches.Add("PlayerStats.Fell (by other.mod)");
        });
        var (v, f) = Attempts.JudgeReport(text, None);
        Assert.Equal("red", v);
        Assert.Contains(f, x => x.Level == "bad" && x.Text == "Another mod was loaded: Other 1.0 (other.dll).");
        // Patches are said once per owner, with their methods.
        Assert.Single(f, x => x.Text.Contains("other.mod"));
        Assert.Contains(f, x => x.Text.Contains("2 methods: PlayerStats.Update, PlayerStats.Fell"));

        var allowed = new HashSet<(string, string)> { ("mod", "Other 1.0 (other.dll)"), ("patches", "other.mod") };
        var (v2, f2) = Attempts.JudgeReport(text, allowed);
        Assert.Equal("green", v2);
        Assert.Equal(2, f2.Count(x => x.Level == "allowed"));
        // Another version is another entry.
        Assert.Equal("red", Attempts.JudgeReport(text.Replace("Other 1.0", "Other 1.1"), allowed).verdict);
    }

    [Fact]
    public void Report_CheatsRed_PracticeBeforeANote_UnreadNotAllowable()
    {
        Assert.Equal("red", Attempts.JudgeReport(Report(r => r.Cheats.Add("GodMode")), None).verdict);
        var (v, f) = Attempts.JudgeReport(Report(r => r.PracticeBefore = "Go"), None);
        Assert.Equal("green", v);
        Assert.Contains(f, x => x.Level == "note");
        var unread = RunReport.Parse(Report(r => r.OtherPlugins.Add("could not list the plugins: boom")));
        Assert.Empty(Attempts.Items(unread));
        Assert.Equal("some.mod", Attempts.PatchOwner("A.B (by some.mod)"));
        Assert.Null(Attempts.PatchOwner("A.B"));
    }

    // --- a changed game, part by part -----------------------------------------

    private static readonly GameCode Table = new(
        new[] { "# Assembly-CSharp test", "girlMutantAiManager 1111111111111111", "PlayerStats 2222222222222222", "TheForest.Utils.LocalPlayer 3333333333333333" },
        new[] { "# areas", "^(girl|.*Megan) = the Megan boss fight", "^(PlayerStats|.*Health) = player health" });

    private static string Changed(params string[] hashes) => Report(r =>
    {
        r.GameHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        r.TypeHashes.AddRange(hashes);
    });

    [Fact]
    public void GameCode_ComparesPartByPart()
    {
        var d = Table.Compare(new[] { "girlMutantAiManager 1111111111111111", "PlayerStats 9999999999999999", "Cheat 4444444444444444" });
        Assert.Equal(new[] { "PlayerStats" }, d.Changed);
        Assert.Equal(new[] { "Cheat" }, d.Added);
        Assert.Equal(new[] { "TheForest.Utils.LocalPlayer" }, d.Missing);
        Assert.Equal("the Megan boss fight", Table.Area("girlMutantAiManager"));
        Assert.Equal(GameCode.Other, Table.Area("Cheat"));
    }

    [Fact]
    public void AChangedGame_IsNamedByArea_TheNamesBehindAFold()
    {
        var (v, f) = Attempts.JudgeReport(Changed("girlMutantAiManager 1111111111111112", "PlayerStats 2222222222222222",
                                                  "TheForest.Utils.LocalPlayer 3333333333333333"), None, Table);
        Assert.Equal("red", v);
        var game = f[0];
        Assert.Equal("bad", game.Level);
        Assert.Equal("The game's code was changed: the Megan boss fight (1). 1 of 3 parts differ from the Steam game.", game.Text);
        Assert.Equal(new[] { "Changed in the Megan boss fight: girlMutantAiManager." }, game.Details);

        // The same code in another file: amber, said plainly.
        var same = Attempts.JudgeReport(Changed("girlMutantAiManager 1111111111111111", "PlayerStats 2222222222222222",
                                                "TheForest.Utils.LocalPlayer 3333333333333333"), None, Table);
        Assert.Equal("amber", same.verdict);
        Assert.Equal("warn", same.findings[0].Level);
        // No per-type hashes (an older plugin), or no table: red, unnamed.
        Assert.Equal("The game's code is not the Steam game's - it was changed or is another version.",
                     Attempts.JudgeReport(Changed(), None, Table).findings[0].Text);
        Assert.Contains("no table", Attempts.JudgeReport(Changed("PlayerStats 1"), None, new GameCode(new string[0], new string[0])).findings[0].Text);
    }

    [Fact]
    public void TheShippedAreas_NameTheGamesParts()
    {
        var steam = GameCode.Steam;
        Assert.Equal("the Megan boss fight", steam.Area("girlMutantAiManager"));
        Assert.Equal("cannibals and mutants", steam.Area("mutantAI"));
        Assert.Equal("player movement", steam.Area("FirstPersonCharacter"));
        Assert.Equal("the game's scripted actions (PlayMaker)", steam.Area("HutongGames.PlayMaker.Actions.FloatCompare"));
        Assert.Equal("cheats and the debug console", steam.Area("Cheats"));
        Assert.Equal(GameCode.Other, steam.Area("zzzz"));
        // The Steam build's table, written in game (RunIntegrity.WriteTypeHashes).
        Assert.True(steam.Count > 3000, "steam-types.txt has " + steam.Count + " types");
        var none = steam.Compare(new[] { "PlayerStats a6ed9e529f0ef306" });
        Assert.Empty(none.Changed);
        Assert.Equal(new[] { "PlayerStats" }, steam.Compare(new[] { "PlayerStats 0000000000000000" }).Changed);
        Assert.Equal("game = x\nflag = y", Attempts.ShownReport("game = x\ntypehash = A 1\nflag = y"));
    }

    // --- the API ------------------------------------------------------------

    private readonly string _data;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _http;

    public AttemptTests()
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
        c.Move(3050, "bomb-boost", true, 772.5f, 40f, 0f, "game time stopped 1.00 s");
        c.End(3100, "finished", 1150);
        string log = c.Text + AttemptChain.ReportMarker + "\n" + Report();
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
        Assert.Equal(Report().TrimEnd('\n'), view["report"].GetValue<string>());
        Assert.Equal("green", view["recording"]["verdict"].GetValue<string>());
        // A move is shown, never judged: the attempt is still green.
        var mv = Assert.Single(view["moves"].AsArray());
        Assert.Equal("Bomb boost", mv["label"].GetValue<string>());
        Assert.Equal(3050, mv["realMs"].GetValue<long>());
        Assert.Equal(772.5, mv["pos"][0].GetValue<double>(), 3);
        Assert.All(view["findings"].AsArray(), f => Assert.Equal("ok", f["level"].GetValue<string>()));
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

    private async Task<HttpStatusCode> AdminCall(HttpMethod method, string path)
    {
        var msg = new HttpRequestMessage(method, path);
        msg.Headers.Add("X-Admin-Token", "admin-secret");
        return (await _http.SendAsync(msg)).StatusCode;
    }

    [Fact]
    public async Task AnotherMod_Red_AllowedByAnAdmin_TheVerdictFollows()
    {
        string token = await Register(Runner);
        string log = Chain(5, nonce: null).Text + AttemptChain.ReportMarker + "\n" + Report(r => r.OtherPlugins.Add("Other 1.0 (other.dll)"));
        var up = await Post(token, "/api/attempts/" + Id + "/log", new StringContent(log, Encoding.UTF8, "text/plain"));
        var answer = await up.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("red", answer["verdict"].GetValue<string>());
        Assert.Contains(answer["why"].AsArray(), w => w.GetValue<string>().Contains("Other 1.0"));

        // Listed for the admins, allowed: the receipt's amber (offline) is what is left.
        var list = await AdminGet("/api/admin/allowed");
        Assert.Contains(list.AsArray(), x => x["kind"].GetValue<string>() == "mod" && x["text"].GetValue<string>() == "Other 1.0 (other.dll)" && !x["allowed"].GetValue<bool>());
        string q = "/api/admin/allowed?kind=mod&text=" + Uri.EscapeDataString("Other 1.0 (other.dll)");
        Assert.Equal(HttpStatusCode.OK, await AdminCall(HttpMethod.Post, q));
        var view = await _http.GetFromJsonAsync<JsonObject>("/api/attempts/" + Id);
        Assert.Equal("amber", view["verdict"].GetValue<string>());
        Assert.Contains(view["findings"].AsArray(), f => f["level"].GetValue<string>() == "allowed");
        Assert.Equal(HttpStatusCode.BadRequest, await AdminCall(HttpMethod.Post, "/api/admin/allowed?kind=nope&text=x"));

        // Taken off the list: red again.
        Assert.Equal(HttpStatusCode.OK, await AdminCall(HttpMethod.Delete, q));
        Assert.Equal("red", (await _http.GetFromJsonAsync<JsonObject>("/api/attempts/" + Id))["verdict"].GetValue<string>());
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.GetAsync("/api/admin/allowed")).StatusCode);

        // The page: the app, with the attempt in its link preview.
        string page = await _http.GetStringAsync("/attempt/" + Id);
        Assert.Contains("<meta property=\"og:title\" content=\"Any% attempt by Runner - Forest Practice Runs\">", page);
        Assert.Contains("Problems found", page);
        Assert.Contains("/attempt.js?v=", page);
        Assert.Contains("<title>Forest Practice Runs</title>", await _http.GetStringAsync("/attempt/a-00000000000000fe"));
    }

    private async Task<JsonNode> AdminGet(string path)
    {
        var msg = new HttpRequestMessage(HttpMethod.Get, path);
        msg.Headers.Add("X-Admin-Token", "admin-secret");
        var r = await _http.SendAsync(msg);
        return await r.Content.ReadFromJsonAsync<JsonNode>();
    }
}
