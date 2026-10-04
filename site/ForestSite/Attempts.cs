using System.Globalization;
using System.Security.Cryptography;
using ForestOverlay.Data;
using Microsoft.Data.Sqlite;

namespace ForestSite;

// ------------------------------------------------------------------
// Run mode attempts (docs/run-mode.md, phase 2: codes and receipts).
//
// The plugin asks for a nonce when an attempt starts, sends the chain's
// head about once a minute (a checkpoint, timed by this server's clock)
// and the whole log when the attempt ends - finished or reset. The log is
// replayed (src/Data/AttemptChain, linked in) and judged against what the
// server saw during the run (Judge). Logs are never replaced: a second
// upload of the same attempt must be the same text.
//
//   <data>/attempts/<id>.log.gz
//
// A runner's starts and logs are capped per day (MaxAttemptsPerDay,
// MaxLogBytesPerDay): past them a 413, which the plugin sets aside and
// never retries (Data/SiteProtocol.Classify).
// ------------------------------------------------------------------
public sealed class Attempts
{
    /// The nonce must be folded this soon after the attempt starts, or the
    /// start is only checked by the video.
    public const long NonceWithinMs = 30_000;
    /// A stretch longer than this with no checkpoint is checked by the video only.
    public const long MaxGapMs = 150_000;
    /// How late a checkpoint (or the log) may reach the server: the request's
    /// own trip, a slow answer to the nonce request.
    public const long LateMs = 45_000;
    /// How early: the PC's clock against the server's, the nonce's trip.
    public const long EarlyMs = 5_000;
    /// The two clocks may drift this much (a fraction of the time between).
    public const double Drift = 0.01;
    public const long MinCheckpointGapMs = 20_000;
    public const int MaxCheckpoints = 600;
    public const long DayMs = 86_400_000;

    /// Per runner, over the last 24 hours (security audit, 2026-10-04: the
    /// address limit alone let one address write tens of GB of logs a day).
    /// Run mode sends a receipt for every attempt, resets included
    /// (docs/run-mode.md): a runner resetting every 10 s for 8 hours makes
    /// ~2,900 attempts, a reset's log is a few KB and an hour's run ~4 MB -
    /// heavy real use stays far under both. Settable for the tests.
    public int MaxAttemptsPerDay { get; set; } = 5_000;
    public long MaxLogBytesPerDay { get; set; } = 2L * 1024 * 1024 * 1024;

    private readonly Store _store;
    private readonly string _dir;
    private readonly Func<long> _now;
    private readonly Categories _categories;

    public Attempts(Store store, string dataDir, Func<long> nowMs = null, Categories categories = null)
    {
        _store = store;
        _categories = categories;
        _dir = Path.Combine(dataDir, "attempts");
        _now = nowMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        using var c = store.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS attempts (
  id TEXT PRIMARY KEY, runner_id TEXT NOT NULL, category TEXT NOT NULL, spot_id TEXT NOT NULL,
  nonce TEXT, issued_ms INTEGER, log_ms INTEGER, end_reason TEXT, end_ms INTEGER, final_timer_ms INTEGER,
  steps INTEGER, verdict TEXT, why TEXT, log_bytes INTEGER);
CREATE INDEX IF NOT EXISTS attempts_runner ON attempts (runner_id);
CREATE TABLE IF NOT EXISTS checkpoints (
  attempt_id TEXT NOT NULL, step INTEGER NOT NULL, head TEXT NOT NULL, received_ms INTEGER NOT NULL,
  PRIMARY KEY (attempt_id, step));
CREATE TABLE IF NOT EXISTS attempt_items (
  attempt_id TEXT NOT NULL, kind TEXT NOT NULL, text TEXT NOT NULL, PRIMARY KEY (attempt_id, kind, text));
CREATE TABLE IF NOT EXISTS allowed_code (
  kind TEXT NOT NULL, text TEXT NOT NULL, by TEXT NOT NULL, at INTEGER NOT NULL, PRIMARY KEY (kind, text));";
        cmd.ExecuteNonQuery();
        // The daily byte cap's count (older databases: the column; their
        // logs count as attempts only).
        if (!Store.HasColumn(c, "attempts", "log_bytes"))
        {
            using var alter = c.CreateCommand();
            alter.CommandText = "ALTER TABLE attempts ADD COLUMN log_bytes INTEGER;";
            alter.ExecuteNonQuery();
        }
    }

    public sealed record Answer(int Status, object Body);

    private static Answer Fail(int status, string error) => new(status, new { error });

    // --- the plugin -------------------------------------------------------

    /// An attempt starts: a nonce for it. A retry (the answer was lost)
    /// gets the same nonce; someone else's id is refused.
    public Answer Start(string runner, string id, string category, string spot)
    {
        if (!AttemptChain.IsAttemptId(id)) return Fail(400, "attempt must be a- and 16 lowercase hex digits");
        var row = Row(id);
        if (row != null)
        {
            if (row.Runner != runner) return Fail(409, "this attempt id is someone else's");
            if (row.Nonce == null || row.LogMs != null) return Fail(409, "this attempt has ended");
            return new(200, new { nonce = row.Nonce, again = true });
        }
        long started = (long)_store.Scalar("SELECT COUNT(*) FROM attempts WHERE runner_id = $r AND issued_ms >= $since",
                                           ("$r", runner), ("$since", _now() - DayMs));
        if (started >= MaxAttemptsPerDay) return Fail(413, DailyLimit());
        string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        _store.Update(@"INSERT INTO attempts (id, runner_id, category, spot_id, nonce, issued_ms)
                        VALUES ($id, $r, $c, $s, $n, $t)",
            ("$id", id), ("$r", runner), ("$c", Short(category, 60)), ("$s", Short(spot, 40)), ("$n", nonce), ("$t", _now()));
        return new(200, new { nonce });
    }

    /// The head after `step`, sent during the attempt.
    public Answer Checkpoint(string runner, string id, int step, string head)
    {
        var row = Row(id);
        if (row == null || row.Runner != runner) return Fail(404, "no such attempt of yours");
        if (row.LogMs != null) return Fail(409, "this attempt has ended");
        if (step < 1 || head == null || head.Length != 64 || !head.All(Uri.IsHexDigit)) return Fail(400, "expected { step, head }");
        long now = _now();
        long count = (long)_store.Scalar("SELECT COUNT(*) FROM checkpoints WHERE attempt_id = $id", ("$id", id));
        if (count >= MaxCheckpoints) return Fail(413, "too many checkpoints");
        object last = _store.Scalar("SELECT MAX(received_ms) FROM checkpoints WHERE attempt_id = $id", ("$id", id));
        if (last is long l && now - l < MinCheckpointGapMs) return Fail(429, "checkpoints come once a minute");
        int added = _store.Update(@"INSERT INTO checkpoints (attempt_id, step, head, received_ms) VALUES ($id, $s, $h, $t)
                                    ON CONFLICT DO NOTHING",
            ("$id", id), ("$s", step), ("$h", head.ToLowerInvariant()), ("$t", now));
        return added == 1 ? new(200, new { ok = true }) : Fail(409, "a checkpoint for this step is in already");
    }

    /// The attempt's log, at its end. An offline attempt (no Start) is
    /// taken too - judged amber.
    public Answer Log(string runner, string id, string text)
    {
        if (!AttemptChain.IsAttemptId(id)) return Fail(400, "not an attempt id");
        var row = Row(id);
        if (row != null && row.Runner != runner) return Fail(409, "this attempt id is someone else's");
        if (row?.LogMs != null)
        {
            string kept = LogText(id);
            return kept == text ? new(200, new { verdict = row.Verdict, why = Lines(row.Why), existing = true })
                                : Fail(409, "this attempt's log is in already; logs are never replaced");
        }
        long bytes = System.Text.Encoding.UTF8.GetByteCount(text ?? "");
        var (logs, sent) = LogsToday(runner);
        if (logs >= MaxAttemptsPerDay || sent + bytes > MaxLogBytesPerDay) return Fail(413, DailyLimit());

        var r = AttemptChain.Read(text);
        if (r.Error != null) return Fail(400, "the log does not read: " + r.Error);
        if (r.AttemptId != id) return Fail(400, "the log is another attempt's");
        if (r.RunnerId != runner) return Fail(400, "the log names another runner");

        long now = _now();
        var cps = Checkpoints(id);
        var (verdict, why) = Judge(r, row?.Nonce, row?.IssuedMs ?? 0, cps, now);

        Store.WriteGz(Path.Combine(_dir, id + ".log.gz"), text);
        if (row == null)
            _store.Update(@"INSERT INTO attempts (id, runner_id, category, spot_id) VALUES ($id, $r, $c, $s)",
                ("$id", id), ("$r", runner), ("$c", Short(r.Category, 60)), ("$s", Short(r.SpotId, 40)));
        _store.Update(@"UPDATE attempts SET log_ms = $t, end_reason = $e, end_ms = $em, final_timer_ms = $ft, steps = $n,
                        verdict = $v, why = $w, log_bytes = $b WHERE id = $id",
            ("$id", id), ("$t", now), ("$e", r.Ended ? Short(r.EndReason, 80) : "no end"), ("$em", r.Ended ? r.EndMs : r.LastMs),
            ("$ft", r.FinalTimerMs), ("$n", r.Steps.Count), ("$v", verdict), ("$w", string.Join("\n", why)), ("$b", bytes));
        var report = RunReport.Parse(r.Report);
        foreach (var (kind, item) in Items(report))
            _store.Update("INSERT INTO attempt_items (attempt_id, kind, text) VALUES ($id, $k, $t) ON CONFLICT DO NOTHING",
                ("$id", id), ("$k", kind), ("$t", Short(item, 200)));
        var (overall, all) = Overall(verdict, why, r.Report, Allowed(), CategoryOf(report));
        return new(200, new { verdict = overall, why = all });
    }

    /// The runner's logs taken in the last 24 hours: how many, how many bytes.
    private (long count, long bytes) LogsToday(string runner)
    {
        using var c = _store.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*), COALESCE(SUM(log_bytes), 0) FROM attempts WHERE runner_id = $r AND log_ms >= $since";
        cmd.Parameters.AddWithValue("$r", runner);
        cmd.Parameters.AddWithValue("$since", _now() - DayMs);
        using var r = cmd.ExecuteReader();
        r.Read();
        return (r.GetInt64(0), r.GetInt64(1));
    }

    /// The refusal past the daily caps (413: the plugin keeps the file in
    /// uploads/attempts/refused with this reason and does not retry it).
    private string DailyLimit() =>
        "this runner reached the site's daily limit for run mode attempts (" + MaxAttemptsPerDay.ToString("N0", CultureInfo.InvariantCulture) +
        " attempts or " + (MaxLogBytesPerDay / (1024.0 * 1024 * 1024)).ToString("0.#", CultureInfo.InvariantCulture) +
        " GB of logs in 24 hours) - not stored";

    /// The category version an attempt's report names (null: none, or the
    /// site does not have it).
    private RunCategory CategoryOf(RunReport r) =>
        _categories == null || r.Category.Length == 0 ? null : _categories.Version(r.Category, r.CategoryVersion);

    /// The verdict over both halves: the receipt (Judge) and what ran
    /// (JudgeReport + the category), with the report's problems after the
    /// receipt's lines. A category without the anti-splice codes (author,
    /// 2026-10-02: optional per category) does not judge the recording's
    /// timing - only a log that contradicts the site, or run mode's flags;
    /// one that does not accept amber turns amber red.
    public static (string verdict, List<string> why) Overall(string receipt, IEnumerable<string> receiptWhy, string reportText,
                                                            ISet<(string, string)> allowed, RunCategory category)
    {
        var (level, findings) = JudgeReport(reportText, allowed, null, category);
        var why = new List<string>();
        if (category != null && !category.AntiSplice)
        {
            if (receipt == "red") why.AddRange(receiptWhy);
            else receipt = "green";
            why.Add(category.Name + " does not use the anti-splice codes: the recording's timing is not checked.");
        }
        else why.AddRange(receiptWhy);
        why.AddRange(findings.Where(f => f.Level == "bad" || f.Level == "pending" || f.Level == "warn").Select(f => f.Text));
        string verdict = Worst(receipt, level);
        if (verdict == "amber" && category != null && !category.AmberAccepted)
        {
            verdict = "red";
            why.Add(category.Name + " does not accept attempts checked by the video's codes only.");
        }
        return (verdict, why);
    }

    public static string Worst(string a, string b) =>
        a == "red" || b == "red" ? "red" : a == "amber" || b == "amber" ? "amber" : a ?? b;

    // --- reads ------------------------------------------------------------

    /// The public view of an attempt (reports are public - docs/run-mode.md).
    public object View(string id)
    {
        var row = Row(id);
        if (row == null) return null;
        string name = _store.Scalar("SELECT name FROM runners WHERE id = $id", ("$id", row.Runner)) as string ?? "";
        var cps = Checkpoints(id);
        Judged j = row.LogMs != null ? JudgedLog(id, row) : null;
        return new
        {
            id, runner = row.Runner, runnerName = name, category = row.Category, spot = row.Spot,
            plugin = j?.Plugin, started = j?.Started, startedAt = j?.StartedAt, mode = j?.Mode,
            online = row.Nonce != null, issued = Iso(row.IssuedMs), received = Iso(row.LogMs), checkpoints = cps.Count,
            ended = row.LogMs != null, endReason = row.EndReason, durationMs = row.EndMs, finalTimerMs = row.FinalTimerMs,
            steps = row.Steps, flags = j?.Flags ?? 0, verdict = j?.Verdict ?? "running", why = j?.Why ?? Lines(row.Why),
            recording = j?.Recording, findings = j?.Findings, rules = j?.Category, moves = j?.Moves, rundown = j?.Rundown,
            events = j?.Events, eventGroups = j?.EventGroups, report = j?.Report, loads = j?.Loads,
        };
    }

    // --- the judged log, cached (security audit, 2026-10-04) --------------------------

    /// What a view works out from a stored log: the replay of up to 4 MB, the
    /// report's findings, the verdict. Logs are never replaced, so it holds
    /// until the allow-list changes, the category version the report names
    /// reaches the site, or the attempt is deleted (Delete) - each page view
    /// no longer replays the whole log.
    private sealed record Judged(long LogMs, int AllowedVersion, string CategoryId, int CategoryVersion, bool CategoryFound, long Weight,
                                 string Plugin, string Started, string StartedAt, string Mode, int Flags, object Category, object Moves,
                                 List<string> Rundown, object Events, object EventGroups, string Verdict, string[] Why, object Findings,
                                 object Recording, string Report, object Loads);

    /// The logs' size the cache may hold (characters of log text); past it,
    /// it starts again.
    public const long MaxCachedLog = 64L * 1024 * 1024;

    private readonly Dictionary<string, Judged> _judged = new();
    private long _judgedWeight;
    private int _allowedVersion;

    /// How many times a view replayed and judged a log (tests).
    public int LogsJudged;

    private Judged JudgedLog(string id, AttemptRow row)
    {
        lock (_judged)
        {
            if (_judged.TryGetValue(id, out var hit) && hit.LogMs == row.LogMs && hit.AllowedVersion == _allowedVersion &&
                (hit.CategoryFound || hit.CategoryId.Length == 0 || _categories?.Version(hit.CategoryId, hit.CategoryVersion) == null))
                return hit;
        }
        int allowedVersion = Volatile.Read(ref _allowedVersion);
        string text = LogText(id) ?? "";
        Interlocked.Increment(ref LogsJudged);
        var replay = AttemptChain.Read(text);
        string report = replay.Report;
        var parsed = RunReport.Parse(report);
        var allowed = Allowed();
        var cat = CategoryOf(parsed);
        string[] why = Lines(row.Why);
        var moves = MoveNotes(replay.Moves, cat).Select(m => new
        {
            kind = m.Kind, label = m.Label, realMs = m.RealMs, detail = m.Detail,
            pos = m.HasPos ? new[] { m.X, m.Y, m.Z } : null, maybeBanned = m.MaybeBanned,
        }).ToList();
        // The audit log (run mode attempts since the audit log; none in
        // older logs): the rundown first, the timeline behind it.
        var notes = EventNotes(replay.Events);
        var events = notes.Select(e => new
        {
            kind = e.Kind, label = e.Label, group = e.Group, realMs = e.RealMs, timerMs = e.TimerMs, detail = e.Detail,
            pos = e.HasPos ? new[] { e.X, e.Y, e.Z } : null,
        }).ToList();
        var eventGroups = EventGroups(notes).Select(g => new { id = g.Id, label = g.Label, count = g.Count }).ToList();
        var (verdict, all) = Overall(row.Verdict, why, report, allowed, cat);
        var (_, list) = JudgeReport(report, allowed, null, cat);
        var j = new Judged(row.LogMs.Value, allowedVersion, parsed.Category ?? "", parsed.CategoryVersion, cat != null, text.Length,
            replay.Plugin, replay.Started, parsed.StartedAt, parsed.Started, replay.Flags.Count, Categories.View(cat), moves,
            RunAudit.Rundown(replay.Events), events, eventGroups, verdict, all.ToArray(),
            list.Select(f => new { level = f.Level, text = f.Text, details = f.Details }).ToList(),
            new { verdict = row.Verdict, why, judged = cat == null || cat.AntiSplice }, ShownReport(report), LoadsView(replay));
        lock (_judged)
        {
            if (_judged.Remove(id, out var old)) _judgedWeight -= old.Weight;
            if (j.Weight <= MaxCachedLog)
            {
                if (_judgedWeight + j.Weight > MaxCachedLog) { _judged.Clear(); _judgedWeight = 0; }
                _judged[id] = j;
                _judgedWeight += j.Weight;
            }
        }
        return j;
    }

    private void Forget(string id)
    {
        lock (_judged)
            if (_judged.Remove(id, out var old)) _judgedWeight -= old.Weight;
    }

    // --- moves the game saw (pure, tested) -----------------------------------------

    /// One `move` line for the attempt page (Data/MoveDetector).
    public sealed record MoveNote(string Kind, string Label, long RealMs, string Detail, bool HasPos, double X, double Y, double Z,
                                  string MaybeBanned);

    // Words that name each kind in a category's banned moves (speedrun.com's
    // rule text: "No bomb boosting", "The explosives glitch").
    private static readonly Dictionary<string, (string Label, string[] Words)> MoveKinds = new()
    {
        ["bomb-boost"] = ("Bomb boost", new[] { "bomb", "explosi", "knockback" }),
        ["huge-speed"] = ("Huge speed", Array.Empty<string>()),
        ["cave-force-load"] = ("Cave state force load", new[] { "cave" }),
        ["fall-damage-cancel"] = ("Fall damage cancel", new[] { "fall damage", "fall-damage", "fall cancel", "slide cancel" }),
        ["lift"] = ("Lift out of a structure", new[] { "log boost", "logboost", "log-boost", "wall boost", "depenetrat" }),
        ["clip"] = ("Clip through a solid", new[] { "clip" }),
    };

    /// The moves the game saw, in plain words, each with the category's
    /// banned move it may be (by the words the rule uses) - a lead for the
    /// verifier to check on the video. Never part of the verdict
    /// (docs/run-mode.md: a move is never an automatic reject).
    public static List<MoveNote> MoveNotes(IEnumerable<AttemptChain.MoveInfo> moves, RunCategory category)
    {
        var list = new List<MoveNote>();
        if (moves == null) return list;
        foreach (var m in moves)
        {
            string label = m.Kind;
            string[] words = Array.Empty<string>();
            if (MoveKinds.TryGetValue(m.Kind, out var k)) { label = k.Label; words = k.Words; }
            string banned = null;
            if (category != null)
                banned = category.Banned.FirstOrDefault(b => words.Any(w => b.Contains(w, StringComparison.OrdinalIgnoreCase)));
            list.Add(new MoveNote(m.Kind, label, m.RealMs, m.Detail, m.HasPos, m.X, m.Y, m.Z, banned));
        }
        return list;
    }

    // --- the audit log (pure, tested) -----------------------------------------------

    /// One `event` line for the attempt page's timeline (src/Data/RunAudit).
    public sealed record EventNote(string Kind, string Label, string Group, long RealMs, long TimerMs, string Detail,
                                   bool HasPos, double X, double Y, double Z);

    /// A filter on the timeline: a group of kinds and how many lines it has.
    public sealed record EventGroup(string Id, string Label, int Count);

    /// The game's loads in the log (`load` lines, plugin load-removed time):
    /// how many, their real time, their time on the timer and the timer
    /// without them. Null for a log from before them (it has no line to
    /// tell "no loads" from "not tracked").
    public static object LoadsView(AttemptChain.Replay r)
    {
        if (r == null || r.Loads.Count == 0) return null;
        long real = 0;
        foreach (var l in r.Loads) real += l.LengthMs;
        return new
        {
            count = r.Loads.Count, realMs = real, timedMs = r.LoadTimedMs, lrtMs = r.LrtMs,
            list = r.Loads.Select(l => new { realMs = l.RealMs, lengthMs = l.LengthMs, timedMs = l.TimedMs }).ToList(),
        };
    }

    /// The events in plain words with their group, in log order. Never part
    /// of the verdict: like a move, an event is what the game saw.
    public static List<EventNote> EventNotes(IEnumerable<AttemptChain.EventInfo> events)
    {
        var list = new List<EventNote>();
        if (events == null) return list;
        foreach (var e in events)
            list.Add(new EventNote(e.Kind, RunAudit.Label(e.Kind), RunAudit.Group(e.Kind), e.RealMs, e.TimerMs, e.Detail,
                                   e.HasPos, e.X, e.Y, e.Z));
        return list;
    }

    /// The groups present, in RunAudit's order (the page's filter chips).
    public static List<EventGroup> EventGroups(IEnumerable<EventNote> notes)
    {
        var counts = notes.GroupBy(n => n.Group).ToDictionary(g => g.Key, g => g.Count());
        return RunAudit.Groups.Where(counts.ContainsKey).Select(g => new EventGroup(g, RunAudit.GroupLabel(g), counts[g])).ToList();
    }

    // --- what ran: the report's findings (pure, tested) ----------------------------

    /// ok, bad (red), pending / warn (amber), allowed, note; Details = lines
    /// behind a fold (internal names - author: plain words first).
    public sealed record Finding(string Level, string Text, List<string> Details = null);

    /// The mods and code a report names, as (kind, text) - what the
    /// admins can allow. "could not list" lines are never allowable.
    public static IEnumerable<(string kind, string text)> Items(RunReport r)
    {
        foreach (string s in r.OtherPlugins) if (!Unread(s)) yield return ("mod", s);
        foreach (string s in r.OtherPatchers) if (!Unread(s)) yield return ("patcher", s);
        foreach (string s in r.OtherCode) yield return ("code", s);
        var owners = new HashSet<string>();
        foreach (string s in r.ForeignPatches)
            if (!Unread(s) && PatchOwner(s) is { } o && owners.Add(o)) yield return ("patches", o);
    }

    private static bool Unread(string s) => s.StartsWith("could not list", StringComparison.Ordinal);

    /// "PlayerStats.Update (by some.mod)" -> "some.mod".
    public static string PatchOwner(string entry)
    {
        int at = entry.LastIndexOf(" (by ", StringComparison.Ordinal);
        return at < 0 || !entry.EndsWith(")") ? null : entry.Substring(at + 5, entry.Length - at - 6);
    }

    /// The report's findings in plain words, judged: anything a report
    /// says is wrong is red, unless the admins allowed that exact mod /
    /// version; a report still checking (or none) is amber. Run mode's flags
    /// are judged by the receipt (they are in the chain), not again here.
    public static (string verdict, List<Finding> findings) JudgeReport(string text, ISet<(string, string)> allowed, GameCode code = null,
                                                                       RunCategory category = null)
    {
        var f = new List<Finding>();
        if (string.IsNullOrWhiteSpace(text))
        {
            f.Add(new("pending", "The game sent no report of what ran during the attempt."));
            return ("amber", f);
        }
        var r = RunReport.Parse(text);
        bool Ok(string kind, string item) => allowed.Contains((kind, item));

        if (r.GameHash.Length == 0) f.Add(new("pending", "The game's files were still being checked when the attempt ended."));
        else if (r.GameHash.StartsWith("error")) f.Add(new("bad", "The game's code could not be checked (" + r.GameHash + ")."));
        else if (RunReport.IsKnownGame(r.GameHash)) f.Add(new("ok", "The game's code is the unmodified Steam game."));
        else f.Add(ChangedGame(r, code ?? GameCode.Steam));

        if (r.OtherPlugins.Count + r.OtherPatchers.Count + r.OtherCode.Count == 0) f.Add(new("ok", "No other mods were loaded."));
        foreach (string s in r.OtherPlugins)
            f.Add(Ok("mod", s) ? new("allowed", "Another mod was loaded, allowed by the moderators: " + s + ".")
                               : new("bad", "Another mod was loaded: " + s + "."));
        foreach (string s in r.OtherPatchers)
            f.Add(Ok("patcher", s) ? new("allowed", "Another BepInEx patcher was installed, allowed by the moderators: " + s + ".")
                                   : new("bad", "Another BepInEx patcher was installed: " + s + "."));
        foreach (string s in r.OtherCode)
            f.Add(Ok("code", s) ? new("allowed", "Code from outside the game was loaded, allowed by the moderators: " + s + ".")
                                : new("bad", "Code from outside the game was loaded: " + s + "."));

        if (r.ForeignPatches.Count == 0) f.Add(new("ok", "Nothing else changed the game's code while it ran."));
        var byOwner = new Dictionary<string, List<string>>();
        foreach (string s in r.ForeignPatches)
        {
            string owner = PatchOwner(s);
            if (owner == null || Unread(s)) { f.Add(new("bad", "Another mod changed the game's code: " + s + ".")); continue; }
            if (!byOwner.TryGetValue(owner, out var list)) byOwner[owner] = list = new List<string>();
            list.Add(s.Substring(0, s.Length - owner.Length - 6));
        }
        foreach (var (owner, methods) in byOwner)
        {
            string what = methods.Count + (methods.Count == 1 ? " method" : " methods") + ": " +
                          string.Join(", ", methods.Take(8)) + (methods.Count > 8 ? " and " + (methods.Count - 8) + " more" : "");
            f.Add(Ok("patches", owner) ? new("allowed", "Changes to the game's code by " + owner + ", allowed by the moderators (" + what + ").")
                                       : new("bad", "Another mod (" + owner + ") changed the game's code while it ran (" + what + ")."));
        }

        if (r.Cheats.Count == 0) f.Add(new("ok", "The game's own cheats were off."));
        foreach (string s in r.Cheats) f.Add(new("bad", "A game cheat was on: " + s + "."));

        f.AddRange(Categories.Judge(r, category));

        if (r.PracticeBefore.Length > 0)
            f.Add(new("note", "Practice was used before this attempt (" + r.PracticeBefore + "); the attempt itself started clean."));

        string verdict = f.Any(x => x.Level == "bad") ? "red" : f.Any(x => x.Level == "pending" || x.Level == "warn") ? "amber" : "green";
        return (verdict, f);
    }

    /// A game file that is not the Steam build's: what changed, by area
    /// (the report's per-type hashes against GameCode's table).
    private static Finding ChangedGame(RunReport r, GameCode code)
    {
        const string NotSteam = "The game's code is not the Steam game's - it was changed or is another version";
        if (r.TypeHashes.Count == 0) return new("bad", NotSteam + ".");
        if (code.Count == 0) return new("bad", NotSteam + " (the site has no table of the Steam game's code to say where).");
        var d = code.Compare(r.TypeHashes);
        if (d.Changed.Count + d.Added.Count + d.Missing.Count == 0)
            return new("warn", "The game's file is not the Steam game's, but its code is the same, part by part (" + code.Count +
                               " parts compared) - only other parts of the file differ.");

        var details = new List<string>();
        void List(string what, List<string> types)
        {
            foreach (var (area, list) in code.ByArea(types))
                details.Add(what + " in " + area + ": " + string.Join(", ", list.Take(40)) + (list.Count > 40 ? " and " + (list.Count - 40) + " more" : "") + ".");
        }
        List("Changed", d.Changed);
        List("Added", d.Added);
        List("Missing", d.Missing);
        var touched = d.Changed.Concat(d.Added).Concat(d.Missing).ToList();
        var areas = code.ByArea(touched);
        string where = string.Join(", ", areas.Take(6).Select(a => a.area + " (" + a.types.Count + ")")) +
                       (areas.Count > 6 ? " and " + (areas.Count - 6) + " more areas" : "");
        return new("bad", "The game's code was changed: " + where + ". " + touched.Count + " of " + code.Count +
                          " parts differ from the Steam game.", details);
    }

    /// The report as shown: the per-type hashes left out (thousands of lines;
    /// the findings say what they showed).
    public static string ShownReport(string report) =>
        report == null ? null : string.Join("\n", report.Split('\n').Where(l => !l.StartsWith("typehash = ", StringComparison.Ordinal)));

    // --- the allow-list (admins) ---------------------------------------------------

    private static readonly string[] Kinds = { "mod", "patcher", "code", "patches" };

    public ISet<(string, string)> Allowed()
    {
        using var c = _store.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT kind, text FROM allowed_code";
        var set = new HashSet<(string, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add((r.GetString(0), r.GetString(1)));
        return set;
    }

    /// Every mod / patcher / code / patch owner reports have named, with
    /// how many attempts and whether it is allowed - and allowed ones no
    /// report names any more.
    public List<object> AllowList()
    {
        using var c = _store.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT kind, text, SUM(n), MAX(by), MAX(at), MAX(allowed) FROM (
  SELECT i.kind, i.text, 1 AS n, NULL AS by, NULL AS at, 0 AS allowed FROM attempt_items i
  UNION ALL SELECT kind, text, 0, by, at, 1 FROM allowed_code)
GROUP BY kind, text ORDER BY MAX(allowed) DESC, SUM(n) DESC, kind, text";
        var list = new List<object>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new
            {
                kind = r.GetString(0), text = r.GetString(1), attempts = r.GetInt64(2), allowed = r.GetInt64(5) == 1,
                by = r.IsDBNull(3) ? null : r.GetString(3), at = r.IsDBNull(4) ? null : Iso(r.GetInt64(4)),
            });
        return list;
    }

    public bool Allow(string kind, string text, string by)
    {
        if (!Kinds.Contains(kind) || string.IsNullOrWhiteSpace(text) || text.Length > 200) return false;
        _store.Update("INSERT INTO allowed_code (kind, text, by, at) VALUES ($k, $t, $b, $a) ON CONFLICT DO NOTHING",
            ("$k", kind), ("$t", text), ("$b", by ?? "?"), ("$a", _now()));
        Interlocked.Increment(ref _allowedVersion);   // every cached judgement is stale
        return true;
    }

    public bool Disallow(string kind, string text)
    {
        bool had = _store.Update("DELETE FROM allowed_code WHERE kind = $k AND text = $t", ("$k", kind ?? ""), ("$t", text ?? "")) == 1;
        Interlocked.Increment(ref _allowedVersion);
        return had;
    }

    /// Where a code shows in an attempt's log: the "check a code" box
    /// (phase 3's page). Empty when the log is not in yet.
    public object FindCode(string id, string typed)
    {
        string code = AttemptChain.NormaliseCode(typed);
        if (code == null) return null;
        var row = Row(id);
        if (row?.LogMs == null) return new { code, matches = Array.Empty<object>() };
        var r = AttemptChain.Read(LogText(id));
        var hits = r.Steps.Where(s => s.Code == code)
                          .Select(s => (object)new { step = s.N, realMs = s.RealMs, timerMs = s.TimerMs }).Take(50).ToList();
        return new { code, matches = hits };
    }

    /// The owner's delete: the attempt, its checkpoints and its log.
    public bool Delete(string id)
    {
        if (!AttemptChain.IsAttemptId(id)) return false;
        _store.Update("DELETE FROM checkpoints WHERE attempt_id = $id", ("$id", id));
        _store.Update("DELETE FROM attempt_items WHERE attempt_id = $id", ("$id", id));
        bool had = _store.Update("DELETE FROM attempts WHERE id = $id", ("$id", id)) == 1;
        try { File.Delete(Path.Combine(_dir, id + ".log.gz")); } catch (IOException) { }
        Forget(id);
        return had;
    }

    public string LogText(string id)
    {
        string path = Path.Combine(_dir, id + ".log.gz");
        return File.Exists(path) ? Store.ReadGz(path) : null;
    }

    // --- the judgement (pure, tested) ----------------------------------------

    public sealed record Cp(int Step, string Head, long ReceivedMs);

    /// green: started online, the log matches every checkpoint, and the
    /// checkpoints cover the attempt in real time. amber: parts are covered
    /// by the video's codes only (offline, a late nonce, a gap, no end).
    /// red: the log contradicts what the server saw, or run mode flagged it.
    public static (string verdict, List<string> why) Judge(AttemptChain.Replay r, string nonce, long issuedMs,
                                                          IList<Cp> checkpoints, long logReceivedMs)
    {
        var red = new List<string>();
        var amber = new List<string>();
        foreach (string f in r.Flags) red.Add("Run mode flagged the attempt: " + f + ".");

        bool anchored = false;
        if (nonce == null)
        {
            if (r.Nonce != null) red.Add("The log holds a start code the site never gave.");
            else amber.Add("Started offline: the site did not see this attempt while it ran - it is checked by the video's codes only.");
        }
        else if (r.Nonce == null)
            amber.Add("The site's start code never reached the game - the attempt is checked by the video's codes only.");
        else if (r.Nonce != nonce)
            red.Add("The log's start code is not the one the site gave this attempt.");
        else
        {
            anchored = true;
            if (r.NonceMs > NonceWithinMs)
                amber.Add("The site's start code arrived " + Clock(r.NonceMs) + " into the attempt - the part before it is checked by the video's codes only.");
        }

        // The times the server can vouch for, in the log's real time.
        var covered = new List<long>();
        if (anchored) covered.Add(r.NonceMs <= NonceWithinMs ? 0 : r.NonceMs);
        foreach (var cp in checkpoints.OrderBy(c => c.Step))
        {
            var s = r.Step(cp.Step);
            if (s == null) { red.Add("The site received a checkpoint for step " + cp.Step + ", which the log does not have."); continue; }
            if (!string.Equals(s.Head, cp.Head, StringComparison.OrdinalIgnoreCase))
            {
                red.Add("The log does not match what the game sent the site at " + Clock(s.RealMs) + ".");
                continue;
            }
            if (!anchored) continue;
            long since = s.RealMs - r.NonceMs;
            long expected = issuedMs + since;
            long slack = (long)(Math.Abs(since) * Drift);
            if (cp.ReceivedMs < expected - EarlyMs - slack)
                red.Add("The log's clock runs ahead of real time at " + Clock(s.RealMs) + " (the site received it " +
                        Secs(expected - cp.ReceivedMs) + " s too early).");
            else if (cp.ReceivedMs > expected + LateMs + slack)
                amber.Add("The checkpoint at " + Clock(s.RealMs) + " reached the site " + Secs(cp.ReceivedMs - expected) + " s late.");
            else covered.Add(s.RealMs);
        }

        // The log itself can come any time later (queued offline), but
        // never before its last record could have happened.
        long end = r.Ended ? r.EndMs : r.LastMs;
        if (anchored && logReceivedMs < issuedMs + (end - r.NonceMs) - EarlyMs - (long)(Math.Abs(end - r.NonceMs) * Drift))
            red.Add("The log reached the site before the attempt it describes could have ended (" +
                    Secs(issuedMs + (end - r.NonceMs) - logReceivedMs) + " s early).");
        if (!r.Ended) amber.Add("The attempt has no end in its log (the game closed during it?).");

        if (anchored)
        {
            covered.Sort();
            var gaps = new List<string>();
            long prev = covered.Count > 0 ? covered[0] : 0;
            foreach (long t in covered)
            {
                if (t - prev > MaxGapMs) gaps.Add(Clock(prev) + "-" + Clock(t));
                prev = t;
            }
            if (end - prev > MaxGapMs) gaps.Add(Clock(prev) + "-" + Clock(end));
            if (gaps.Count > 0)
                amber.Add("No checkpoint reached the site during " + string.Join(", ", gaps.Take(5)) + (gaps.Count > 5 ? " and " + (gaps.Count - 5) + " more" : "") +
                          " - checked by the video's codes only.");
        }

        if (red.Count > 0) { red.AddRange(amber); return ("red", red); }
        if (amber.Count > 0) return ("amber", amber);
        int matched = checkpoints.Count;
        return ("green", new List<string>
        {
            matched == 0 ? "Started online, and short enough that the site's start code covers it: it ended before a checkpoint was due."
                         : "Started online; " + matched + " checkpoint(s) received during the run match the log, and they cover it in real time.",
        });
    }

    public static string Clock(long ms)
    {
        long s = ms / 1000;
        return s >= 3600 ? (s / 3600) + ":" + (s / 60 % 60).ToString("00") + ":" + (s % 60).ToString("00")
                         : (s / 60) + ":" + (s % 60).ToString("00");
    }

    private static string Secs(long ms) => (ms / 1000.0).ToString("0", CultureInfo.InvariantCulture);

    // --- rows -----------------------------------------------------------------

    private sealed record AttemptRow(string Runner, string Category, string Spot, string Nonce, long? IssuedMs, long? LogMs,
                                     string EndReason, long? EndMs, long? FinalTimerMs, long? Steps, string Verdict, string Why);

    private AttemptRow Row(string id)
    {
        using var c = _store.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT runner_id, category, spot_id, nonce, issued_ms, log_ms, end_reason, end_ms, final_timer_ms,
                                   steps, verdict, why FROM attempts WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        string S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        long? L(int i) => r.IsDBNull(i) ? null : r.GetInt64(i);
        return new AttemptRow(r.GetString(0), r.GetString(1), r.GetString(2), S(3), L(4), L(5), S(6), L(7), L(8), L(9), S(10), S(11));
    }

    private List<Cp> Checkpoints(string id)
    {
        using var c = _store.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT step, head, received_ms FROM checkpoints WHERE attempt_id = $id ORDER BY step";
        cmd.Parameters.AddWithValue("$id", id);
        var list = new List<Cp>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new Cp((int)r.GetInt64(0), r.GetString(1), r.GetInt64(2)));
        return list;
    }

    private static string[] Lines(string why) => string.IsNullOrEmpty(why) ? Array.Empty<string>() : why.Split('\n');

    private static string Iso(long? ms) =>
        ms == null ? null : DateTimeOffset.FromUnixTimeMilliseconds(ms.Value).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string Short(string s, int max)
    {
        s = AttemptChain.Clean(s ?? "");
        return s.Length > max ? s.Substring(0, max) : s;
    }
}
