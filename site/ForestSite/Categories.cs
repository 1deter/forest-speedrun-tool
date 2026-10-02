using System.Globalization;
using System.Text.Json.Nodes;
using ForestOverlay.Data;

namespace ForestSite;

// ------------------------------------------------------------------
// Run categories (docs/run-mode.md phase 4; src/Data/RunCategory, linked).
//
// The moderators (any admin - author, 2026-10-02: one role at this
// community's size) edit them on /admin; the plugin reads the published
// ones as text. Every save is a new version, and every version is kept:
// an attempt's report names the version it ran under, and its page judges
// against that one.
//
// speedrun.com (author, 2026-10-02: "as long as it follows the
// speedrun.com categories"): once a day the site reads the game's
// categories and rules. A new one (category x subcategory value) becomes
// a draft. A category nobody has edited follows speedrun.com by itself; one
// the moderators edited is never overwritten - it is marked "speedrun.com
// changed this" with both versions, and a moderator accepts or dismisses.
// ------------------------------------------------------------------
public sealed class Categories
{
    public const string SpeedrunGame = "w6j5341j";   // The Forest on speedrun.com
    public const string SyncUser = "speedrun.com";

    private readonly Store _store;
    private readonly Func<long> _now;

    public Categories(Store store, Func<long> nowMs = null)
    {
        _store = store;
        _now = nowMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        using var c = store.Open();
        using var cmd = c.CreateCommand();
        // src_name / src_rules: speedrun.com's text as last applied or
        // dismissed; pending_*: a newer speedrun.com text waiting for a moderator.
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS categories (
  id TEXT PRIMARY KEY, version INTEGER NOT NULL, text TEXT NOT NULL, by TEXT NOT NULL, at INTEGER NOT NULL,
  src TEXT NOT NULL DEFAULT '', src_name TEXT NOT NULL DEFAULT '', src_rules TEXT NOT NULL DEFAULT '',
  pending_name TEXT, pending_rules TEXT);
CREATE TABLE IF NOT EXISTS category_versions (
  id TEXT NOT NULL, version INTEGER NOT NULL, text TEXT NOT NULL, by TEXT NOT NULL, at INTEGER NOT NULL,
  PRIMARY KEY (id, version));
CREATE TABLE IF NOT EXISTS category_sync (k TEXT PRIMARY KEY, v TEXT NOT NULL);";
        cmd.ExecuteNonQuery();
    }

    // --- reads ----------------------------------------------------------------

    private sealed record Row(string Id, int Version, string Text, string By, long At, string Src, string SrcName, string SrcRules,
                              string PendingName, string PendingRules);

    private List<Row> Rows(string where = "", params (string, object)[] args)
    {
        using var c = _store.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, version, text, by, at, src, src_name, src_rules, pending_name, pending_rules FROM categories " + where + " ORDER BY id";
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v);
        var list = new List<Row>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Row(r.GetString(0), (int)r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetInt64(4), r.GetString(5),
                             r.GetString(6), r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9)));
        return list;
    }

    private static RunCategory Read(string text) => RunCategory.Parse(text).FirstOrDefault();

    public List<RunCategory> All() => Rows().Select(r => Read(r.Text)).Where(c => c != null).ToList();

    /// What the plugin fetches: the published categories, in name order.
    public string PublishedText() =>
        RunCategory.Format(All().Where(c => c.Status == "published").OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList());

    /// The published list's ETag: a hash of its text (quoted, as HTTP has it).
    public static string ETag(string text) =>
        "\"" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text ?? ""))).Substring(0, 32).ToLowerInvariant() + "\"";

    /// True when the request's If-None-Match names `etag` (or is `*`).
    public static bool Unchanged(string ifNoneMatch, string etag)
    {
        if (string.IsNullOrWhiteSpace(ifNoneMatch)) return false;
        foreach (string part in ifNoneMatch.Split(','))
        {
            string p = part.Trim();
            if (p.StartsWith("W/")) p = p.Substring(2);
            if (p == "*" || p == etag) return true;
        }
        return false;
    }

    /// A category as it was at `version` (an attempt's report names it).
    public RunCategory Version(string id, int version)
    {
        object text = _store.Scalar("SELECT text FROM category_versions WHERE id = $id AND version = $v", ("$id", id ?? ""), ("$v", version));
        return text is string t ? Read(t) : null;
    }

    public RunCategory Current(string id) => Rows("WHERE id = $id", ("$id", id ?? "")).Select(r => Read(r.Text)).FirstOrDefault();

    /// The admin page's list: each category's text, who saved it last and
    /// what speedrun.com changed since.
    public List<object> AdminList() => Rows().Select(r =>
    {
        var c = Read(r.Text);
        return (object)new
        {
            id = r.Id, version = r.Version, name = c?.Name ?? r.Id, status = c?.Status ?? "draft", category = View(c), by = r.By, at = Iso(r.At),
            src = r.Src, srcName = r.SrcName, srcRules = r.SrcRules,
            changed = r.PendingName != null ? new { name = r.PendingName, rules = r.PendingRules } : null,
        };
    }).ToList();

    /// The public view of one category's version (the attempt page).
    public static object View(RunCategory c) => c == null ? null : new
    {
        id = c.Id, name = c.Name, version = c.Version, status = c.Status, difficulty = c.Difficulty, creative = c.Creative,
        multiplayer = c.Multiplayer, spot = c.Spot, antisplice = c.AntiSplice, amber = c.AmberAccepted,
        banned = c.Banned, rules = c.Rules, src = c.Source, logcap = c.LogCap,
        caps = c.ItemCaps.Select(k => new { name = k.Key, cap = k.Value }).ToList(),
        features = RunCategory.Features.Select(f => new { key = f.Key, label = f.Label, policy = c.Policy(f.Key) }).ToList(),
    };

    // --- writes -----------------------------------------------------------------

    /// A moderator's save: one [category] block. The version is the site's
    /// (the next one); the id must be the URL's. Returns the saved category.
    public (RunCategory saved, string error) Save(string id, string text, string by)
    {
        if (!RunCategory.ValidId(id)) return (null, "an id is lowercase letters, digits and dashes");
        var list = RunCategory.Parse(text ?? "");
        if (list.Count != 1) return (null, "expected one [category] block with a valid id");
        var c = list[0];
        if (c.Id != id) return (null, "the block's id is not this category's");
        if (c.Name.Trim().Length == 0 || c.Name.Length > 80) return (null, "a name, 1-80 characters");
        if (c.Rules.Count > 200 || c.Banned.Count > 100) return (null, "too many rules");
        return (Put(c, by, null, null), null);
    }

    /// Writes a new version of `c` (src fields kept unless given).
    private RunCategory Put(RunCategory c, string by, string srcName, string srcRules, bool clearPending = false)
    {
        var row = Rows("WHERE id = $id", ("$id", c.Id)).FirstOrDefault();
        c.Version = (row?.Version ?? 0) + 1;
        string text = c.Format();
        long now = _now();
        if (row == null)
            _store.Update(@"INSERT INTO categories (id, version, text, by, at, src, src_name, src_rules)
                            VALUES ($id, $v, $t, $b, $a, $s, $sn, $sr)",
                ("$id", c.Id), ("$v", c.Version), ("$t", text), ("$b", by), ("$a", now), ("$s", c.Source), ("$sn", srcName ?? ""), ("$sr", srcRules ?? ""));
        else
            _store.Update(@"UPDATE categories SET version = $v, text = $t, by = $b, at = $a, src = $s,
                              src_name = COALESCE($sn, src_name), src_rules = COALESCE($sr, src_rules),
                              pending_name = CASE WHEN $clear = 1 THEN NULL ELSE pending_name END,
                              pending_rules = CASE WHEN $clear = 1 THEN NULL ELSE pending_rules END
                            WHERE id = $id",
                ("$id", c.Id), ("$v", c.Version), ("$t", text), ("$b", by), ("$a", now), ("$s", c.Source),
                ("$sn", (object)srcName ?? DBNull.Value), ("$sr", (object)srcRules ?? DBNull.Value), ("$clear", clearPending ? 1 : 0));
        _store.Update("INSERT INTO category_versions (id, version, text, by, at) VALUES ($id, $v, $t, $b, $a)",
            ("$id", c.Id), ("$v", c.Version), ("$t", text), ("$b", by), ("$a", now));
        return c;
    }

    /// speedrun.com's change taken: its name and rules into the category
    /// (the moderators' other settings kept).
    public RunCategory AcceptSource(string id, string by)
    {
        var row = Rows("WHERE id = $id", ("$id", id ?? "")).FirstOrDefault();
        if (row?.PendingName == null) return null;
        var c = Read(row.Text);
        if (row.PendingName == Removed) { c.Status = "hidden"; return Put(c, by, row.SrcName, row.SrcRules, true); }
        c.Name = row.PendingName;
        c.Rules.Clear();
        c.Rules.AddRange(SplitRules(row.PendingRules));
        return Put(c, by, row.PendingName, row.PendingRules, true);
    }

    /// speedrun.com's change set aside: the category stays as it is.
    public bool DismissSource(string id) =>
        _store.Update(@"UPDATE categories SET src_name = COALESCE(pending_name, src_name), src_rules = COALESCE(pending_rules, src_rules),
                          pending_name = NULL, pending_rules = NULL WHERE id = $id AND pending_name IS NOT NULL", ("$id", id ?? "")) == 1;

    // --- speedrun.com ------------------------------------------------------------

    public const string Removed = "(removed from speedrun.com)";

    /// One category as speedrun.com has it (pure: from the API's JSON).
    public sealed record Seed(string Src, string Name, string Rules, string Difficulty, string Creative, string Multiplayer);

    /// `/games/<id>/categories?embed=variables` -> one seed per full-game
    /// category and subcategory value (Any% - Normal, Any% - Hardmode, ...).
    public static List<Seed> Seeds(JsonNode json)
    {
        var list = new List<Seed>();
        if (json?["data"] is not JsonArray cats) return list;
        foreach (var cat in cats)
        {
            if ((string)cat?["type"] != "per-game") continue;
            string id = (string)cat["id"] ?? "", name = ((string)cat["name"] ?? "").Trim();
            string rules = ((string)cat["rules"] ?? "").Replace("\r\n", "\n").Trim();
            bool coop = name.Contains("co-op", StringComparison.OrdinalIgnoreCase) || name.Contains("coop", StringComparison.OrdinalIgnoreCase);
            string mp = coop ? "yes" : "no";
            JsonObject values = null;
            if (cat["variables"]?["data"] is JsonArray vars)
                foreach (var v in vars)
                    if (v?["is-subcategory"]?.GetValue<bool>() == true) { values = v["values"]?["values"] as JsonObject; break; }
            if (values == null || values.Count == 0)
            {
                var (d0, c0) = GameOf(name, rules);
                list.Add(new Seed(id, name, rules, d0, c0, mp));
                continue;
            }
            foreach (var (valueId, value) in values)
            {
                string label = ((string)value?["label"] ?? valueId).Trim();
                var (d, c) = GameOf(label, "");
                // A subcategory that is not a game setting (Any% Bombs) leaves the game open.
                list.Add(new Seed(id + "/" + valueId, name + " - " + label, rules, d, c, mp));
            }
        }
        return list;
    }

    /// The difficulty and Creative a label names: Peaceful / Normal /
    /// Hardmode / Creative; anything else is any.
    public static (string difficulty, string creative) GameOf(string label, string rules)
    {
        string l = label.ToLowerInvariant();
        if (l.Contains("creative")) return ("any", "yes");
        if (l.Contains("peaceful")) return ("peaceful", "no");
        if (l.Contains("hard")) return ("hard", "no");
        if (l.Contains("normal")) return ("normal", "no");
        return ("any", "any");
    }

    /// Rule lines as the category keeps them: speedrun.com's lines, blank
    /// lines between them dropped.
    public static List<string> SplitRules(string rules) =>
        (rules ?? "").Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).Where(l => l.Trim().Length > 0).ToList();

    /// The banned moves a rule names: "-No OOB", "No clipping through
    /// walls (...)", "-No explosives glitch" - lines saying "No" that are not
    /// about co-op, software or the developer mode (checked by the report).
    public static List<string> BannedOf(IEnumerable<string> rules)
    {
        var list = new List<string>();
        foreach (string raw in rules)
        {
            string l = raw.Trim().TrimStart('-', '*', ' ').Trim();
            if (!l.StartsWith("No ", StringComparison.OrdinalIgnoreCase)) continue;
            string what = l.Substring(3).Trim();
            string low = what.ToLowerInvariant();
            if (low.StartsWith("co") || low.Contains("software") || low.Contains("developer") || low.Contains("cheat")) continue;
            if (what.Length > 0) list.Add(char.ToUpperInvariant(what[0]) + what.Substring(1));
        }
        return list;
    }

    /// A new draft from speedrun.com.
    public static RunCategory Draft(Seed s)
    {
        var c = new RunCategory
        {
            Id = RunCategory.Slug(s.Name), Name = s.Name, Status = "draft", Difficulty = s.Difficulty, Creative = s.Creative,
            Multiplayer = s.Multiplayer, Source = s.Src,
        };
        c.Rules.AddRange(SplitRules(s.Rules));
        c.Banned.AddRange(BannedOf(c.Rules));
        return c;
    }

    /// Applies speedrun.com's list: new categories as drafts, changes to
    /// untouched ones applied, changes to edited ones waiting for a moderator,
    /// removed ones marked. Returns what it did, for the admin log.
    public List<string> Apply(IList<Seed> seeds)
    {
        var done = new List<string>();
        var rows = Rows();
        var bySrc = rows.Where(r => r.Src.Length > 0).GroupBy(r => r.Src).ToDictionary(g => g.Key, g => g.First());
        var ids = new HashSet<string>(rows.Select(r => r.Id));
        foreach (var s in seeds)
        {
            if (!bySrc.TryGetValue(s.Src, out var row))
            {
                var c = Draft(s);
                if (!RunCategory.ValidId(c.Id)) continue;
                string baseId = c.Id;
                for (int n = 2; ids.Contains(c.Id); n++) c.Id = baseId + "-" + n;
                ids.Add(c.Id);
                Put(c, SyncUser, s.Name, s.Rules);
                done.Add("new draft: " + c.Name);
                continue;
            }
            if (row.SrcName == s.Name && row.SrcRules == s.Rules) continue;
            if (row.PendingName == s.Name && row.PendingRules == s.Rules) continue;
            bool untouched = !_store.Exists("SELECT 1 FROM category_versions WHERE id = $id AND by != $u", ("$id", row.Id), ("$u", SyncUser));
            if (untouched)
            {
                var c = Read(row.Text);
                c.Name = s.Name;
                c.Rules.Clear();
                c.Rules.AddRange(SplitRules(s.Rules));
                c.Banned.Clear();
                c.Banned.AddRange(BannedOf(c.Rules));
                Put(c, SyncUser, s.Name, s.Rules, true);
                done.Add("followed speedrun.com: " + s.Name);
            }
            else
            {
                _store.Update("UPDATE categories SET pending_name = $n, pending_rules = $r WHERE id = $id",
                    ("$id", row.Id), ("$n", s.Name), ("$r", s.Rules));
                done.Add("speedrun.com changed: " + row.Id);
            }
        }
        var live = new HashSet<string>(seeds.Select(s => s.Src));
        foreach (var row in rows)
            if (row.Src.Length > 0 && !live.Contains(row.Src) && row.PendingName != Removed && row.SrcName != Removed)
            {
                _store.Update("UPDATE categories SET pending_name = $n, pending_rules = '' WHERE id = $id", ("$id", row.Id), ("$n", Removed));
                done.Add("removed from speedrun.com: " + row.Id);
            }
        _store.Update("INSERT INTO category_sync (k, v) VALUES ('last', $v) ON CONFLICT (k) DO UPDATE SET v = $v",
            ("$v", Iso(_now()) + (done.Count == 0 ? " (no changes)" : " (" + done.Count + " change(s))")));
        return done;
    }

    public string LastSync() => _store.Scalar("SELECT v FROM category_sync WHERE k = 'last'") as string ?? "";

    /// Reads speedrun.com and applies it. Throws on a network error.
    public async Task<List<string>> Sync(HttpClient http)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get,
            "https://www.speedrun.com/api/v1/games/" + SpeedrunGame + "/categories?embed=variables");
        req.Headers.UserAgent.ParseAdd("ForestSite/1.0 (+https://forest.deter.cloud)");
        using var res = await http.SendAsync(req);
        res.EnsureSuccessStatusCode();
        var seeds = Seeds(JsonNode.Parse(await res.Content.ReadAsStringAsync()));
        if (seeds.Count == 0) throw new InvalidDataException("speedrun.com listed no categories");
        return Apply(seeds);
    }

    /// The categories that are not speedrun.com's, made once (author,
    /// 2026-10-02: a manhunt preset, "just do your best" - the admins look
    /// it over before it is used). sxczurass's manhunt (QA 2026-10-02): two
    /// runners finish the game while four hunt them, with logs in the
    /// inventory and the inventory mods, locked so nobody changes them.
    public void SeedPresets()
    {
        if (_store.Exists("SELECT 1 FROM category_sync WHERE k = 'presets'")) return;
        if (Current("manhunt") == null)
        {
            var c = new RunCategory { Id = "manhunt", Name = "Manhunt", Status = "draft", Multiplayer = "yes", AntiSplice = false };
            c.SetPolicy("logs", RunCategory.Forced);
            c.SetPolicy("itemcaps", RunCategory.Forced);
            c.SetPolicy("fastbuild", RunCategory.Forced);
            c.Rules.Add("Two runners finish the game while the other players hunt them.");
            c.Rules.Add("Logs in the inventory, item caps and fast building are on for everyone and cannot be changed during the game.");
            c.Rules.Add("Draft - the admins set the details before it is used.");
            Put(c, "preset", null, null);
        }
        _store.Update("INSERT INTO category_sync (k, v) VALUES ('presets', '1') ON CONFLICT DO NOTHING");
    }

    // --- an attempt against its category (pure, tested) ---------------------------

    /// What the category says about a report: the game it was played in, and
    /// the features used that it allowed. `category` null with a named id =
    /// the site does not have that category (version).
    public static List<Attempts.Finding> Judge(RunReport r, RunCategory category)
    {
        var f = new List<Attempts.Finding>();
        // None chosen (by hand, or a plugin older than categories): nothing to say.
        if (r.Category.Length == 0) return f;
        if (category == null)
        {
            f.Add(new("warn", "The attempt names the category '" + r.Category + "' (version " + r.CategoryVersion + "), which the site does not have."));
            return f;
        }
        if (r.Difficulty.Length > 0)
        {
            var bad = category.GameMismatch(r.Difficulty, r.Creative, r.Multiplayer);
            if (bad.Count == 0) f.Add(new("ok", "The game was set up as " + category.Name + " asks."));
            foreach (string b in bad) f.Add(new("bad", Capital(b) + "."));
        }
        foreach (string used in r.Used)
        {
            var feature = RunCategory.FindFeature(used);
            string label = feature?.Label ?? used;
            f.Add(category.IsLocked(used) ? new Attempts.Finding("bad", label + " was used, which " + category.Name + " does not allow.")
                  : category.IsForced(used) ? new Attempts.Finding("allowed", label + " was on, as " + category.Name + " asks of everyone.")
                                            : new Attempts.Finding("allowed", label + " was used, allowed by " + category.Name + "."));
        }
        return f;
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    private static string Iso(long ms) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
