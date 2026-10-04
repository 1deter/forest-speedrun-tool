using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using ForestOverlay.Data;
using Microsoft.Data.Sqlite;
using UnityEngine;

namespace ForestSite;

// ------------------------------------------------------------------
// What the API does, apart from HTTP: upload rules, community packs, and
// the JSON the pages read. Parsing and splits maths are the plugin's own
// files (SegmentBundle, AttemptFormat, SplitStats), linked in.
// ------------------------------------------------------------------
public sealed class Runs
{
    /// A time this much faster than the route's best so far is flagged for
    /// the author (docs/website.md: no anti-cheat, a flag is enough).
    public const float FlagRatio = 0.8f;

    /// Spot submissions one runner can have waiting at once.
    public const int MaxOpenSubmissions = 20;
    /// The longest segment id an upload may bring (security audit,
    /// 2026-10-04). The plugin's ids are `s-` + 12 hex (up to 34 chars) and
    /// old ones `spot.<category>.<name>` slugs (~30); 80 leaves room for a
    /// long legacy name, keeps a run folder's name (SafeName, up to 3 bytes a
    /// letter) under the file system's 255 bytes and matches the spot page's
    /// own limit. Ids already stored are still read and deletable.
    public const int MaxSegmentId = 80;

    private readonly Store _store;
    private readonly Func<string, bool> _publishedCategory;

    /// publishedCategory: whether a run category (id or name) is published -
    /// a spot naming one is a run spot, its PBs are announced (PbNews).
    public Runs(Store store, Func<string, bool> publishedCategory = null)
    {
        _store = store;
        _publishedCategory = publishedCategory;
    }

    // --- uploads ----------------------------------------------------------

    public sealed class UploadResult
    {
        public string Error;
        public readonly List<long> Added = new();
        public readonly List<long> Existing = new();
        public readonly List<string> Skipped = new();
        /// A new PB on an announced spot (community / run spot); null when none.
        public PbFound Pb;
    }

    public sealed record PbFound(string Runner, string Spot, string Segment, string Route, long RunId, float Time, float PreviousBest);

    /// A .foseg with the segment and one or more [attempt] sections, from
    /// the runner `runnerId` (their token). The start state is not kept.
    public UploadResult Upload(string runnerId, string text)
    {
        var res = new UploadResult();
        SegmentBundle b = SegmentBundle.Parse(text, out string error, null);
        if (b == null) { res.Error = error; return res; }
        Segment seg = b.Segment;
        if (string.IsNullOrEmpty(seg.Id) || seg.Id.Length > MaxSegmentId)
        {
            res.Error = "the segment id must be 1-" + MaxSegmentId + " characters";
            return res;
        }
        if (!seg.IsTimed) { res.Error = "not a timed segment (no start or end)"; return res; }
        if (b.Attempts.Count == 0) { res.Error = "no [attempt] sections"; return res; }

        string route = seg.RouteFingerprint();
        // Every attempt is checked before anything is written: an upload
        // with none that passes leaves no route behind (an empty spot).
        var good = new List<(Attempt a, string text)>();
        for (int i = 0; i < b.Attempts.Count; i++)
        {
            string runText = b.Attempts[i];
            Attempt a = AttemptFormat.Parse(runText.Split('\n'));
            string why = Check(a, seg, route, runnerId);
            if (why != null) res.Skipped.Add("attempt " + (i + 1) + ": " + why);
            else good.Add((a, runText));
        }
        if (good.Count == 0) return res;

        // A spot already on the site under someone else (another runner's,
        // or a community one): this runner's route is a copy (an import with
        // a moved zone). It keeps its zones but takes the spot's name,
        // category, description and owner - otherwise the newest copy's
        // labels would become the spot's (the spot page shows the route run
        // most recently), and anyone could rename anyone's spot.
        string owner = runnerId;
        bool copy = false;
        var holder = _store.SpotHolder(seg.Id);
        if (holder != null && (holder.Value.community || (holder.Value.owner.Length > 0 && holder.Value.owner != runnerId)))
        {
            owner = holder.Value.community ? "" : holder.Value.owner;
            copy = true;
            seg.Name = holder.Value.name;
            seg.Category = holder.Value.category;
            seg.Notes = ParseBlock(holder.Value.block).Notes;
        }
        _store.SeeRoute(seg.Id, route, Clip(seg.Name, 80), Clip(seg.Category, 40), BlockOf(seg), false, owner, copy);
        string registered = _store.Scalar("SELECT name FROM runners WHERE id = $id", ("$id", runnerId)) as string ?? "";
        float previousBest = RunnerBest(seg.Id, route, runnerId);
        var fresh = new List<(float duration, bool flagged, long id, string name)>();

        foreach (var (a, runText) in good)
        {
            string name = Clip(AttemptFormat.Clean(a.RunnerName), 40);
            if (name.Length == 0) name = registered;
            float best = Best(seg.Id, route, out int count);
            bool flagged = count >= 3 && a.Duration < best * FlagRatio;

            var (id, added) = _store.AddRun(seg.Id, route, runnerId, name, a.Duration, a.Splits,
                                            a.RecordedUtc, runText, flagged);
            (added ? res.Added : res.Existing).Add(id);
            if (added) fresh.Add((a.Duration, flagged, id, name));
        }

        // A new PB on a community spot or a run spot: Program posts it to
        // Discord (PbWebhook). Re-uploads of runs already here never count.
        bool community = Convert.ToInt64(_store.Scalar("SELECT community FROM routes WHERE segment_id = $s AND route = $r",
                                                       ("$s", seg.Id), ("$r", route)) ?? 0L) == 1;
        float? pb = PbNews.NewPb(previousBest, fresh.Select(f => (f.duration, f.flagged)));
        if (pb != null && PbNews.Announces(community, seg.RunCategory, _publishedCategory))
        {
            var run = fresh.First(f => f.duration == pb.Value);
            res.Pb = new PbFound(run.name, seg.Name, seg.Id, route, run.id, pb.Value, previousBest);
        }
        return res;
    }

    /// A runner's best on a route so far (runs under review included, hidden
    /// ones not); NaN when they have none.
    private float RunnerBest(string segmentId, string route, string runnerId)
    {
        object best = _store.Scalar("SELECT MIN(duration) FROM runs WHERE segment_id = $s AND route = $r AND runner_id = $rid AND hidden = 0",
                                    ("$s", segmentId), ("$r", route), ("$rid", runnerId));
        return best is double d ? (float)d : float.NaN;
    }

    // --- a runner deleting their own spot ------------------------------------------

    /// DELETE /api/spots/<id> with the runner's upload token: the spot goes
    /// (every route, every run) when it is theirs - they first uploaded on it
    /// - and nobody else has runs on it (those are other runners' times: an
    /// admin decides). A community spot is the author's. The answer's status
    /// and a sentence for the runner; runs = how many went.
    public (int status, string message, int runs) DeleteOwnSpot(string runnerId, string segmentId)
    {
        var holder = _store.SpotHolder(segmentId);
        if (holder == null) return (404, "no such spot on the site", 0);
        if (holder.Value.community) return (403, "a community spot - only the author removes those", 0);
        long others = Convert.ToInt64(_store.Scalar("SELECT COUNT(*) FROM routes WHERE segment_id = $s AND owner <> $r",
                                                    ("$s", segmentId), ("$r", runnerId)));
        if (others > 0) return (403, "not your spot - only the runner who first uploaded a run on it can delete it", 0);
        long theirs = Convert.ToInt64(_store.Scalar("SELECT COUNT(*) FROM runs WHERE segment_id = $s AND runner_id <> $r",
                                                    ("$s", segmentId), ("$r", runnerId)));
        if (theirs > 0)
            return (409, theirs + (theirs == 1 ? " run" : " runs") + " by other runners are on it - ask an admin to remove it", 0);
        var (n, error) = _store.DeleteSpot(segmentId);
        return error != null ? (404, error, 0) : (200, "deleted with " + n + (n == 1 ? " run" : " runs"), n);
    }

    /// Why an attempt is refused; null when it is fine.
    public static string Check(Attempt a, Segment seg, string route, string runnerId)
    {
        if (a == null) return "no position samples";
        if (a.Route != route) return "recorded on another version of the route (" + (a.Route.Length > 0 ? a.Route : "none") + ", the segment is " + route + ")";
        if (!(a.Duration > 0f) || a.Duration > 24 * 3600) return "no duration";
        if (a.RecordedUtc == default) return "no recorded time";
        if (a.RunnerId.Length > 0 && a.RunnerId != runnerId) return "another runner's attempt";
        if (a.Splits.Length != 0 && a.Splits.Length != seg.Checkpoints.Count) return "split count does not match the checkpoints";
        return null;
    }

    /// A spot for the author to approve (runners do not open pull requests).
    public (long id, bool replaced, string error) Submit(string runnerId, string text)
    {
        SegmentBundle b = SegmentBundle.Parse(text, out string error, null);
        if (b == null) return (0, false, error);
        string why = SiteProtocol.SubmitRefusal(b.Segment.Id, b.Attempts.Count > 0);
        if (why != null) return (0, false, why);
        // Each waiting submission keeps its file: a cap per runner, so one
        // token cannot fill the disk. A re-submit of a waiting spot replaces.
        if (_store.OpenSubmissions(runnerId, b.Segment.Id) >= MaxOpenSubmissions)
            return (0, false, MaxOpenSubmissions + " of your spots are already waiting for the author - wait until they are looked at.");
        var (id, replaced) = _store.AddSubmission(runnerId, b.Segment.Id, Clip(b.Segment.Name, 80), text, b.StartState != null);
        return (id, replaced, null);
    }

    private float Best(string segmentId, string route, out int count)
    {
        using var c = _store.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT MIN(duration), COUNT(*) FROM runs WHERE segment_id = $s AND route = $r AND hidden = 0";
        cmd.Parameters.AddWithValue("$s", segmentId);
        cmd.Parameters.AddWithValue("$r", route);
        using var r = cmd.ExecuteReader();
        r.Read();
        count = r.GetInt32(1);
        return r.IsDBNull(0) ? float.NaN : (float)r.GetDouble(0);
    }

    // --- community packs --------------------------------------------------

    /// Reads the packs shipped with the site; returns how many were read.
    public int LoadCommunity(string folder, Action<string> log)
    {
        var keep = new HashSet<string>();
        if (Directory.Exists(folder))
            foreach (string path in Directory.GetFiles(folder, "*.foseg"))
            {
                SegmentBundle b = SegmentBundle.Parse(File.ReadAllText(path), out string error, null);
                if (b == null) { log("Community pack " + Path.GetFileName(path) + " skipped: " + error); continue; }
                Segment s = b.Segment;
                string route = s.RouteFingerprint();
                _store.SeeRoute(s.Id, route, s.Name, s.Category, BlockOf(s), true);
                keep.Add(s.Id + "|" + route);
            }
        _store.ClearCommunityExcept(keep);
        return keep.Count;
    }

    // --- reads --------------------------------------------------------------

    /// Every community spot, and every other spot with a run: its current
    /// route's numbers.
    public JsonArray Spots()
    {
        var bySpot = new Dictionary<string, RouteRow>();
        foreach (RouteRow r in Routes(null))
            if (!bySpot.TryGetValue(r.Segment, out RouteRow cur) || Newer(r, cur)) bySpot[r.Segment] = r;

        var list = new JsonArray();
        foreach (RouteRow r in bySpot.Values.OrderByDescending(x => x.Community).ThenBy(x => x.Category).ThenBy(x => x.Name))
        {
            if (!r.Community && r.Runs == 0) continue;
            list.Add(new JsonObject
            {
                ["id"] = r.Segment, ["name"] = r.Name, ["category"] = r.Category, ["community"] = r.Community,
                ["by"] = r.Community ? "" : r.By, ["runs"] = r.Runs, ["runners"] = r.Runners, ["best"] = Num(r.Best), ["lastRun"] = r.LastRun,
                // A community spot can be a plain teleport (no start / end).
                ["timed"] = !r.Community || ParseBlock(r.Block).IsTimed,
            });
        }
        return list;
    }

    /// One spot: every route (current first), each with its zones, split
    /// names, per-runner best and the golds.
    public JsonObject Spot(string segmentId)
    {
        List<RouteRow> routes = Routes(segmentId);
        if (routes.Count == 0) return null;
        routes.Sort((a, b) => Newer(a, b) ? -1 : Newer(b, a) ? 1 : 0);

        var outRoutes = new JsonArray();
        for (int i = 0; i < routes.Count; i++)
        {
            RouteRow r = routes[i];
            Segment seg = ParseBlock(r.Block);
            int rows = seg.Checkpoints.Count + 1;

            List<RunRow> runs = RunsOn(segmentId, r.Route);
            var attempts = new List<Attempt>();
            foreach (RunRow run in runs) attempts.Add(run.AsAttempt());
            SplitStats stats = SplitStats.Build(attempts, seg.Checkpoints.Count);

            // Per runner: their best run, and how many they uploaded.
            var board = new JsonArray();
            foreach (var g in runs.GroupBy(x => x.Runner).Select(g => (best: g.OrderBy(x => x.Duration).First(), n: g.Count(), name: g.OrderByDescending(x => x.Recorded).First().Name))
                                  .OrderBy(x => x.best.Duration))
                board.Add(RunJson(g.best, rows, g.name, g.n));

            var names = new JsonArray();
            for (int row = 0; row < rows; row++) names.Add(seg.SplitName(row));

            var checks = new JsonArray();
            foreach (Trigger t in seg.Checkpoints) checks.Add(TriggerJson(t));

            outRoutes.Add(new JsonObject
            {
                ["route"] = r.Route, ["current"] = i == 0, ["community"] = r.Community, ["firstSeen"] = r.FirstSeen,
                ["start"] = TriggerJson(seg.Start), ["checks"] = checks, ["end"] = TriggerJson(seg.End),
                ["spawn"] = seg.HasSpawn ? Vec(seg.SpawnPosition) : null,
                ["splitNames"] = names, ["runs"] = runs.Count,
                ["bestSegments"] = Floats(stats.BestSegments), ["sumOfBest"] = Num(stats.SumOfBest),
                ["board"] = board,
            });
        }

        RouteRow top = routes[0];
        return new JsonObject
        {
            ["id"] = segmentId, ["name"] = top.Name, ["category"] = top.Category, ["community"] = top.Community,
            ["by"] = top.Community ? "" : top.By, ["notes"] = ParseBlock(top.Block).Notes, ["routes"] = outRoutes,
        };
    }

    /// Each runner's best on one route, fastest first, for the plugin's
    /// comparisons (Data/SiteBoard). Runs under review are left out.
    public string BoardText(string segmentId, string route)
    {
        Segment seg = ParseBlock(Routes(segmentId).FirstOrDefault(x => x.Route == route)?.Block ?? "");
        int rows = seg.Checkpoints.Count + 1;
        var entries = new List<BoardEntry>();
        foreach (var g in RunsOn(segmentId, route).Where(x => !x.Flagged).GroupBy(x => x.Runner))
        {
            RunRow best = g.OrderBy(x => x.Duration).First();
            entries.Add(new BoardEntry
            {
                RunId = best.Id, RunnerId = best.Runner, Duration = best.Duration,
                Name = g.OrderByDescending(x => x.Recorded).First().Name,
                Splits = SplitStats.SplitsOf(best.AsAttempt(), rows),
            });
        }
        entries.Sort((a, b) => a.Duration.CompareTo(b.Duration));
        return SiteBoard.Write(entries);
    }

    /// One runner's runs on one route, fastest first.
    public JsonArray RunnerRuns(string segmentId, string route, string runnerId)
    {
        List<RunRow> runs = RunsOn(segmentId, route).Where(x => x.Runner == runnerId).OrderBy(x => x.Duration).ToList();
        Segment seg = ParseBlock(Routes(segmentId).FirstOrDefault(x => x.Route == route)?.Block ?? "");
        var list = new JsonArray();
        foreach (RunRow r in runs) list.Add(RunJson(r, seg.Checkpoints.Count + 1, r.Name, 0));
        return list;
    }

    /// One run with its path: [t, x, y, z, speed] per sample, and its
    /// state: the shown channels, or every channel with allChannels.
    public JsonObject Run(long id, bool allChannels = false)
    {
        RunRow r = RunById(id);
        if (r == null) return null;
        string text = _store.RunText(r.SegmentId, id);
        Attempt a = text != null ? AttemptFormat.Parse(text.Split('\n')) : null;
        var path = new JsonArray();
        if (a != null)
            foreach (RunSample s in a.Samples)
                path.Add(new JsonArray(R(s.T), R(s.P.x), R(s.P.y), R(s.P.z), R(s.Speed)));
        JsonObject o = RunJson(r, r.Splits.Length + 1, r.Name, 0);
        o["segment"] = r.SegmentId;
        o["route"] = r.Route;
        o["path"] = path;
        o["state"] = StateJson(a, allChannels);
        // Item count changes (plugin v0.24.161+): [t, name, count], time order.
        var items = new JsonArray();
        if (a != null)
            foreach (ItemChange c in a.Items)
                items.Add(new JsonArray(R(c.T), c.Name, c.Count));
        o["items"] = items;
        // What the runner did (plugin replays): [t, kind, label, x, y, z,
        // group], time order; the label in the run audit's words and the
        // group its colour on the maps (Data/RunAudit).
        var events = new JsonArray();
        if (a != null)
            foreach (RunEvent e in a.Events)
                events.Add(new JsonArray(R(e.T), e.Kind, Clip(RunAudit.Label(e.Kind) + (string.IsNullOrEmpty(e.Detail) ? "" : ": " + e.Detail), 80),
                                         R(e.P.x), R(e.P.y), R(e.P.z), RunAudit.Group(e.Kind)));
        o["events"] = events;
        o["buildings"] = a != null ? BuildingsJson(a.Buildings) : new JsonArray();
        // The save's plane (plugin v0.24.163+): [x, z, yaw], or null.
        o["plane"] = a != null && a.HasPlane ? new JsonArray(R(a.Plane.x), R(a.Plane.z), R(a.PlaneYaw)) : null;
        return o;
    }

    /// The player's state channels the page shows, in its order: the
    /// `.run` carries every PlayerStats number (~80, most of them internal);
    /// the full set stays in the file download.
    public static readonly string[] ShownChannels =
    {
        "Health", "Stamina", "Energy", "Fullness", "Thirst", "Armor", "ColdArmor",
        "BatteryCharge", "BodyTemp", "Stealth", "Cold", "IsLit",
        // v0.24.160's fixed item channels (runs from that version only; since
        // v0.24.161 items are the run's `items` track).
        "item:Soda", "item:Booze", "item:EnergyMix", "item:Meds", "item:Aloe",
        "item:Stick", "item:Rock", "item:Log", "item:Rope", "item:Cloth",
        "item:Molotov", "item:BombTimed", "item:Dynamite", "item:Flare", "item:Battery",
    };

    /// { channels: [names], samples: [[t, v...]] } - the 5 Hz `v|` lines:
    /// the shown channels the run has (the preset runners read), or all of
    /// them (the page's "Show all", ~80 - fetched only when asked for).
    /// Null when there are none.
    private static JsonObject StateJson(Attempt a, bool all)
    {
        if (a == null || a.States.Count == 0) return null;
        var names = new JsonArray();
        var index = new List<int>();
        foreach (string name in all ? a.Channels : ShownChannels)
        {
            int i = a.ChannelIndex(name);
            if (i < 0) continue;
            names.Add(name);
            index.Add(i);
        }
        if (index.Count == 0) return null;
        var samples = new JsonArray();
        foreach (StateSample s in a.States)
        {
            var row = new JsonArray(R(s.T));
            foreach (int i in index) row.Add(s.Values != null && i < s.Values.Length ? Num(s.Values[i]) : null);
            samples.Add(row);
        }
        return new JsonObject { ["channels"] = names, ["samples"] = samples };
    }

    public (string segment, string text) RunFile(long id)
    {
        RunRow r = RunById(id);
        return r == null ? (null, null) : (r.SegmentId, _store.RunText(r.SegmentId, id));
    }

    // --- rows -----------------------------------------------------------------

    private sealed class RouteRow
    {
        public string Segment, Route, Name, Category, Block, FirstSeen, LastRun, By;
        public bool Community;
        public int Runs, Runners;
        public float Best = float.NaN;
    }

    private sealed class RunRow
    {
        public long Id;
        public string SegmentId, Route, Runner, Name, Recorded;
        public float Duration;
        public float[] Splits;
        public bool Flagged;

        public Attempt AsAttempt() => new Attempt { Duration = Duration, Splits = Splits, Completed = true };
    }

    /// The current route: a community one, else the one run most recently.
    private static bool Newer(RouteRow a, RouteRow b)
    {
        if (a.Community != b.Community) return a.Community;
        return string.CompareOrdinal(a.LastRun ?? a.FirstSeen, b.LastRun ?? b.FirstSeen) > 0;
    }

    private List<RouteRow> Routes(string segmentId)
    {
        using var c = _store.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT r.segment_id, r.route, r.name, r.category, r.block, r.community, r.first_seen,
       COUNT(x.id), COUNT(DISTINCT x.runner_id), MIN(x.duration), MAX(x.uploaded),
       COALESCE((SELECT o.runner_name FROM runs o WHERE o.runner_id = r.owner ORDER BY o.id DESC LIMIT 1),
                (SELECT n.name FROM runners n WHERE n.id = r.owner), '')
FROM routes r LEFT JOIN runs x ON x.segment_id = r.segment_id AND x.route = r.route AND x.hidden = 0
" + (segmentId != null ? "WHERE r.segment_id = $s " : "") + "GROUP BY r.segment_id, r.route";
        if (segmentId != null) cmd.Parameters.AddWithValue("$s", segmentId);
        var list = new List<RouteRow>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add(new RouteRow
            {
                Segment = rd.GetString(0), Route = rd.GetString(1), Name = rd.GetString(2), Category = rd.GetString(3),
                Block = rd.GetString(4), Community = rd.GetInt64(5) == 1, FirstSeen = rd.GetString(6),
                Runs = rd.GetInt32(7), Runners = rd.GetInt32(8),
                Best = rd.IsDBNull(9) ? float.NaN : (float)rd.GetDouble(9),
                LastRun = rd.IsDBNull(10) ? null : rd.GetString(10),
                By = rd.GetString(11),
            });
        return list;
    }

    private const string RunColumns = "id, segment_id, route, runner_id, runner_name, duration, splits, recorded, flagged";

    private List<RunRow> RunsOn(string segmentId, string route)
    {
        using var c = _store.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT " + RunColumns + " FROM runs WHERE segment_id = $s AND route = $r AND hidden = 0 ORDER BY duration";
        cmd.Parameters.AddWithValue("$s", segmentId);
        cmd.Parameters.AddWithValue("$r", route);
        return ReadRuns(cmd);
    }

    private RunRow RunById(long id)
    {
        using var c = _store.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT " + RunColumns + " FROM runs WHERE id = $id AND hidden = 0";
        cmd.Parameters.AddWithValue("$id", id);
        return ReadRuns(cmd).FirstOrDefault();
    }

    private static List<RunRow> ReadRuns(SqliteCommand cmd)
    {
        var list = new List<RunRow>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add(new RunRow
            {
                Id = rd.GetInt64(0), SegmentId = rd.GetString(1), Route = rd.GetString(2), Runner = rd.GetString(3),
                Name = rd.GetString(4), Duration = (float)rd.GetDouble(5), Splits = Json.ParseFloats(rd.GetString(6)),
                Recorded = rd.GetString(7), Flagged = rd.GetInt64(8) == 1,
            });
        return list;
    }

    // --- JSON -------------------------------------------------------------------

    private static JsonObject RunJson(RunRow r, int rows, string name, int attempts)
    {
        var o = new JsonObject
        {
            ["id"] = r.Id, ["runner"] = r.Runner, ["name"] = name, ["duration"] = Num(r.Duration),
            ["splits"] = Floats(SplitStats.SplitsOf(r.AsAttempt(), rows)), ["recorded"] = r.Recorded,
        };
        if (attempts > 0) o["attempts"] = attempts;
        if (r.Flagged) o["flagged"] = true;
        return o;
    }

    private static JsonObject TriggerJson(Trigger t)
    {
        var o = new JsonObject { ["text"] = TriggerParser.Write(t) };
        switch (t.Kind)
        {
            case TriggerKind.Zone:
                o["kind"] = t.Shape == ZoneShape.Box ? "box" : t.Shape == ZoneShape.Polygon ? "poly" : "zone";
                o["at"] = Vec(t.Position);
                if (t.Shape == ZoneShape.Box) { o["size"] = Vec(t.Extents); o["yaw"] = R(t.Yaw); }
                else if (t.Shape == ZoneShape.Polygon)
                {
                    // [[x, z], ...] around the outline; "half" the half height
                    // around at[1], as a box's size[1].
                    var pts = new JsonArray();
                    foreach (Vector2 p in t.Points ?? new Vector2[0]) pts.Add(new JsonArray(R(p.x), R(p.y)));
                    o["points"] = pts;
                    o["half"] = R(t.Extents.y);
                }
                else o["radius"] = R(t.Radius);
                break;
            case TriggerKind.Item: o["kind"] = "item"; break;
            case TriggerKind.Event: o["kind"] = "event"; break;
            case TriggerKind.Manual: o["kind"] = "manual"; break;
            default: o["kind"] = "none"; break;
        }
        return o;
    }

    private static JsonArray Vec(Vector3 v) => new(R(v.x), R(v.y), R(v.z));

    /// A placed blueprint and its finished structure at most this far apart
    /// (m) are one building (the plugin's ReplayMarks.SamePlace).
    private const float SamePlace = 1.5f;

    /// Structures placed / finished, for the spot page's maps:
    /// [t, state, kind, x, y, z, yaw, sx, sy, sz, cx, cy, cz, rx, rz, until].
    /// yaw / rx / rz: Unity's Euler angles (degrees); s: the box's size and
    /// c its centre in the structure's own frame; until: when a placed
    /// blueprint stops showing - the time the same kind is finished at the
    /// same place after it (as the in-game replay, ReplayMarks.Until) - or
    /// null (finished ones, blueprints never finished).
    public static JsonArray BuildingsJson(IList<RunBuilding> list)
    {
        var buildings = new JsonArray();
        for (int i = 0; i < list.Count; i++)
        {
            RunBuilding b = list[i];
            JsonNode until = null;
            if (b.State == RunBuilding.Placed)
                for (int j = i + 1; j < list.Count; j++)
                {
                    RunBuilding f = list[j];
                    if (f.State != RunBuilding.Built || f.T < b.T || f.Kind != b.Kind) continue;
                    if ((f.P - b.P).sqrMagnitude > SamePlace * SamePlace) continue;
                    until = R(f.T);
                    break;
                }
            buildings.Add(new JsonArray(R(b.T), b.State, b.Kind, R(b.P.x), R(b.P.y), R(b.P.z), R(b.Euler.y),
                                        R(b.Size.x), R(b.Size.y), R(b.Size.z), R(b.Center.x), R(b.Center.y), R(b.Center.z),
                                        R(b.Euler.x), R(b.Euler.z), until));
        }
        return buildings;
    }

    private static JsonArray Floats(float[] f)
    {
        var a = new JsonArray();
        foreach (float x in f) a.Add(Num(x));
        return a;
    }

    /// NaN (unknown) as null.
    private static JsonNode Num(float f) => float.IsNaN(f) || float.IsInfinity(f) ? null : JsonValue.Create(R(f));

    private static double R(float f) => Math.Round((double)f, 3);

    public static Segment ParseBlock(string block)
    {
        List<Segment> found = SegmentFormat.ParseAll(block.Replace("\r\n", "\n").Split('\n'), null);
        return found.Count > 0 ? found[0] : new Segment();
    }

    private static string BlockOf(Segment s)
    {
        var sb = new StringBuilder();
        SegmentFormat.WriteSegment(sb, s, "\n");
        return sb.ToString();
    }

    private static string Clip(string s, int n)
    {
        s = (s ?? "").Trim();
        return s.Length > n ? s.Substring(0, n) : s;
    }
}

public static class Json
{
    public static string Floats(float[] f)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < (f?.Length ?? 0); i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(f[i].ToString("R", CultureInfo.InvariantCulture));
        }
        return sb.Append(']').ToString();
    }

    public static float[] ParseFloats(string s)
    {
        s = s.Trim().TrimStart('[').TrimEnd(']');
        if (s.Length == 0) return new float[0];
        string[] p = s.Split(',');
        var f = new float[p.Length];
        for (int i = 0; i < p.Length; i++) f[i] = float.Parse(p[i], CultureInfo.InvariantCulture);
        return f;
    }
}
