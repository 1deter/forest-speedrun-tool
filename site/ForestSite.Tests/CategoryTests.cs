using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using ForestOverlay.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ForestSite.Tests;

// Run categories (phase 4): speedrun.com's list, the moderators' versions,
// and an attempt judged against its category.
[Collection("site")]
public sealed class CategoryTests : IDisposable
{
    private readonly string _data;
    private readonly Store _store;
    private long _now = 1_000_000;

    public CategoryTests()
    {
        _data = Path.Combine(Path.GetTempPath(), "forest-site-test-" + Guid.NewGuid().ToString("N"));
        _store = new Store(_data);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_data, true); } catch { }
    }

    private Categories New() => new(_store, () => _now++);

    // A trimmed real answer (2026-10-02) of /games/w6j5341j/categories?embed=variables.
    private static List<Categories.Seed> RealSeeds() =>
        Categories.Seeds(JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "speedrun-categories.json"))));

    [Fact]
    public void Seeds_OnePerSubcategory_TheGameFromTheLabel()
    {
        var seeds = RealSeeds();
        var normal = seeds.Single(s => s.Name == "Any% (No Explosive Glitch) - Normal");
        Assert.Equal(("normal", "no", "no"), (normal.Difficulty, normal.Creative, normal.Multiplayer));
        var creative = seeds.Single(s => s.Name == "Any% (No Explosive Glitch) - Creative");
        Assert.Equal(("any", "yes"), (creative.Difficulty, creative.Creative));
        Assert.Equal("hard", seeds.Single(s => s.Name == "Any% (No Explosive Glitch) - Hardmode").Difficulty);
        // Not a game setting: the game is left open.
        Assert.Equal(("any", "any"), (seeds.Single(s => s.Name.EndsWith("Any% Bombs")).Difficulty, seeds.Single(s => s.Name.EndsWith("Any% Bombs")).Creative));
        Assert.All(seeds.Where(s => s.Name.StartsWith("Co-op")), s => Assert.Equal("yes", s.Multiplayer));
        // No subcategory: one, the runner picks the difficulty.
        var robe = seeds.Single(s => s.Name == "Bathrobe%");
        Assert.Equal(("any", "any"), (robe.Difficulty, robe.Creative));
        Assert.DoesNotContain(seeds, s => s.Src.Length == 0);
    }

    [Fact]
    public void Drafts_TakeTheRules_BannedMovesFromTheNoLines()
    {
        var glitchless = Categories.Draft(RealSeeds().First(s => s.Name.StartsWith("Any% Glitchless")));
        Assert.Equal("draft", glitchless.Status);
        Assert.Contains("OOB", glitchless.Banned);
        Assert.Contains(glitchless.Banned, b => b.StartsWith("Clipping through walls"));
        // Co-op, software and the developer mode are checked otherwise, not moves.
        Assert.DoesNotContain(glitchless.Banned, b => b.Contains("Coop") || b.Contains("software") || b.Contains("developer"));
        Assert.Contains("-No developer mode", glitchless.Rules);
        Assert.DoesNotContain("", glitchless.Rules);
        Assert.Equal("any-glitchless-normal", RunCategory.Slug("Any% Glitchless - Normal"));
    }

    [Fact]
    public void Sync_NewAreDrafts_UntouchedFollow_EditedWait_RemovedMarked()
    {
        var cats = New();
        var seeds = RealSeeds();
        var done = cats.Apply(seeds);
        Assert.Equal(seeds.Count, done.Count);
        Assert.Empty(cats.Apply(seeds));   // the same list again: nothing
        Assert.Equal("", cats.PublishedText());   // drafts are not served

        // Speedrun.com changes two: one untouched, one a moderator edited.
        string normalId = RunCategory.Slug("Any% (No Explosive Glitch) - Normal");
        string robeId = RunCategory.Slug("Bathrobe%");
        var edited = cats.Current(robeId);
        edited.Status = "published";
        edited.SetPolicy("godmode", RunCategory.Allowed);
        Assert.Null(cats.Save(robeId, edited.Format(), "maks").error);

        var changed = seeds.Select(s => s.Name.StartsWith("Any% (No Explosive Glitch)") || s.Name == "Bathrobe%"
            ? s with { Rules = s.Rules + "\n-New rule" } : s).Where(s => s.Name != "Co-op Any% - Peaceful").ToList();
        done = cats.Apply(changed);
        Assert.Contains(cats.Current(normalId).Rules, r => r == "-New rule");   // followed
        var robe = cats.Current(robeId);
        Assert.DoesNotContain("-New rule", robe.Rules);   // kept as the moderators left it
        Assert.Equal(RunCategory.Allowed, robe.Policy("godmode"));
        Assert.Contains(done, d => d == "speedrun.com changed: " + robeId);
        Assert.Contains(done, d => d.StartsWith("removed from speedrun.com"));

        // Accept: the rules taken, the moderators' settings kept, a new version.
        int before = robe.Version;
        var accepted = cats.AcceptSource(robeId, "maks");
        Assert.Contains("-New rule", accepted.Rules);
        Assert.Equal(RunCategory.Allowed, accepted.Policy("godmode"));
        Assert.Equal(before + 1, accepted.Version);
        Assert.Null(cats.AcceptSource(robeId, "maks"));   // nothing waiting now
        Assert.Contains("[category]\nid = bathrobe", cats.PublishedText());

        // The old version stays readable (an attempt names it).
        Assert.DoesNotContain("-New rule", cats.Version(robeId, before).Rules);
    }

    [Fact]
    public void Save_BumpsTheVersion_RefusesAnotherId()
    {
        var cats = New();
        var c = new RunCategory { Id = "test-cat", Name = "Test" };
        Assert.Equal(1, cats.Save("test-cat", c.Format(), "a").saved.Version);
        Assert.Equal(2, cats.Save("test-cat", c.Format(), "a").saved.Version);
        Assert.NotNull(cats.Save("other", c.Format(), "a").error);
        Assert.NotNull(cats.Save("test-cat", "nonsense", "a").error);
    }

    [Fact]
    public void Presets_Manhunt_OnceAsADraft()
    {
        var cats = New();
        cats.SeedPresets();
        cats.SeedPresets();
        var m = cats.Current("manhunt");
        Assert.Equal(1, m.Version);
        Assert.True(m.IsForced("logs"));
        Assert.False(m.AntiSplice);
        Assert.Equal("yes", m.Multiplayer);
    }

    // --- an attempt against its category -------------------------------------

    private static string Report(string category = "any-normal v1", string difficulty = "Normal", params string[] used)
    {
        var r = new RunReport { GameHash = RunReport.KnownGameHashes[0], Difficulty = difficulty };
        int v = category.LastIndexOf(" v");
        if (v > 0) { r.Category = category.Substring(0, v); r.CategoryVersion = int.Parse(category.Substring(v + 2)); }
        r.Used.AddRange(used);
        return r.Format();
    }

    private static RunCategory Normal(bool antiSplice = true, bool amber = true) =>
        new() { Id = "any-normal", Name = "Any% - Normal", Version = 1, Difficulty = "normal", Creative = "no", Multiplayer = "no",
                AntiSplice = antiSplice, AmberAccepted = amber };

    [Fact]
    public void Judge_TheGameMustMatch_UsedFeaturesNamed()
    {
        var allowed = new HashSet<(string, string)>();
        Assert.Equal("green", Attempts.JudgeReport(Report(), allowed, null, Normal()).verdict);
        var (v, f) = Attempts.JudgeReport(Report(difficulty: "Hard"), allowed, null, Normal());
        Assert.Equal("red", v);
        Assert.Contains(f, x => x.Text.Contains("played on Normal, the game was Hard"));

        var cat = Normal();
        cat.SetPolicy("godmode", RunCategory.Allowed);
        (v, f) = Attempts.JudgeReport(Report(used: "godmode"), allowed, null, cat);
        Assert.Equal("green", v);
        Assert.Contains(f, x => x.Level == "allowed" && x.Text.StartsWith("God mode was used"));
        Assert.Equal("red", Attempts.JudgeReport(Report(used: "godmode"), allowed, null, Normal()).verdict);
        cat.SetPolicy("logs", RunCategory.Forced);
        (v, f) = Attempts.JudgeReport(Report(used: "logs"), allowed, null, cat);
        Assert.Contains(f, x => x.Level == "allowed" && x.Text == "Logs in the inventory was on, as Any% - Normal asks of everyone.");

        // A category the site does not have: amber, said.
        (v, f) = Attempts.JudgeReport(Report("gone v4"), allowed, null, null);
        Assert.Equal("amber", v);
        // None chosen: a note only.
        Assert.Equal("green", Attempts.JudgeReport(Report(""), allowed, null, null).verdict);
    }

    [Fact]
    public void Overall_NoAntiSplice_TimingNotJudged_RedStays_AmberRefused()
    {
        var allowed = new HashSet<(string, string)>();
        string report = Report();
        var offline = new[] { "Started offline: ..." };
        Assert.Equal("amber", Attempts.Overall("amber", offline, report, allowed, Normal()).verdict);
        var (v, why) = Attempts.Overall("amber", offline, report, allowed, Normal(antiSplice: false));
        Assert.Equal("green", v);
        Assert.Contains(why, w => w.Contains("does not use the anti-splice codes"));
        Assert.DoesNotContain(offline[0], why);
        Assert.Equal("red", Attempts.Overall("red", new[] { "spliced" }, report, allowed, Normal(antiSplice: false)).verdict);

        (v, why) = Attempts.Overall("amber", offline, report, allowed, Normal(amber: false));
        Assert.Equal("red", v);
        Assert.Contains(why, w => w.Contains("does not accept"));
    }

    // --- the API ------------------------------------------------------------

    [Fact]
    public async Task Api_AdminSaves_PluginReadsPublished()
    {
        Environment.SetEnvironmentVariable("FOREST_DATA", Path.Combine(_data, "api"));
        Environment.SetEnvironmentVariable("FOREST_SRC_SYNC", "off");
        Environment.SetEnvironmentVariable("FOREST_ADMIN_TOKEN", "admin-secret");
        using var factory = new WebApplicationFactory<Program>();
        using var http = factory.CreateClient();

        var c = new RunCategory { Id = "any-normal", Name = "Any% - Normal", Status = "published", Difficulty = "normal" };
        var put = new HttpRequestMessage(HttpMethod.Put, "/api/admin/categories/any-normal") { Content = new StringContent(c.Format(), Encoding.UTF8) };
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(put)).StatusCode);
        put = new HttpRequestMessage(HttpMethod.Put, "/api/admin/categories/any-normal") { Content = new StringContent(c.Format(), Encoding.UTF8) };
        put.Headers.Add("X-Admin-Token", "admin-secret");
        var res = await http.SendAsync(put);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var list = RunCategory.Parse(await http.GetStringAsync("/api/categories.txt"));
        Assert.Equal(new[] { "any-normal" }, list.Select(x => x.Id));
        Assert.Equal(1, list[0].Version);
        var v1 = await http.GetFromJsonAsync<JsonObject>("/api/categories/any-normal/1");
        Assert.Equal("normal", (string)v1["difficulty"]);

        var get = new HttpRequestMessage(HttpMethod.Get, "/api/admin/categories");
        get.Headers.Add("X-Admin-Token", "admin-secret");
        var admin = await (await http.SendAsync(get)).Content.ReadFromJsonAsync<JsonObject>();
        Assert.Contains(admin["list"].AsArray(), x => (string)x["id"] == "manhunt");   // the preset, a draft
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }
}
