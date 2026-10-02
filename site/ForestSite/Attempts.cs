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

    private readonly Store _store;
    private readonly string _dir;
    private readonly Func<long> _now;

    public Attempts(Store store, string dataDir, Func<long> nowMs = null)
    {
        _store = store;
        _dir = Path.Combine(dataDir, "attempts");
        _now = nowMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        using var c = store.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS attempts (
  id TEXT PRIMARY KEY, runner_id TEXT NOT NULL, category TEXT NOT NULL, spot_id TEXT NOT NULL,
  nonce TEXT, issued_ms INTEGER, log_ms INTEGER, end_reason TEXT, end_ms INTEGER, final_timer_ms INTEGER,
  steps INTEGER, verdict TEXT, why TEXT);
CREATE INDEX IF NOT EXISTS attempts_runner ON attempts (runner_id);
CREATE TABLE IF NOT EXISTS checkpoints (
  attempt_id TEXT NOT NULL, step INTEGER NOT NULL, head TEXT NOT NULL, received_ms INTEGER NOT NULL,
  PRIMARY KEY (attempt_id, step));
CREATE TABLE IF NOT EXISTS attempt_items (
  attempt_id TEXT NOT NULL, kind TEXT NOT NULL, text TEXT NOT NULL, PRIMARY KEY (attempt_id, kind, text));
CREATE TABLE IF NOT EXISTS allowed_code (
  kind TEXT NOT NULL, text TEXT NOT NULL, by TEXT NOT NULL, at INTEGER NOT NULL, PRIMARY KEY (kind, text));";
        cmd.ExecuteNonQuery();
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
                        verdict = $v, why = $w WHERE id = $id",
            ("$id", id), ("$t", now), ("$e", r.Ended ? Short(r.EndReason, 80) : "no end"), ("$em", r.Ended ? r.EndMs : r.LastMs),
            ("$ft", r.FinalTimerMs), ("$n", r.Steps.Count), ("$v", verdict), ("$w", string.Join("\n", why)));
        var report = RunReport.Parse(r.Report);
        foreach (var (kind, item) in Items(report))
            _store.Update("INSERT INTO attempt_items (attempt_id, kind, text) VALUES ($id, $k, $t) ON CONFLICT DO NOTHING",
                ("$id", id), ("$k", kind), ("$t", Short(item, 200)));
        var (overall, all) = Overall(verdict, why, r.Report, Allowed());
        return new(200, new { verdict = overall, why = all });
    }

    /// The verdict over both halves: the receipt (Judge) and what ran (JudgeReport),
    /// with the report's problems after the receipt's lines.
    private static (string verdict, List<string> why) Overall(string receipt, IEnumerable<string> receiptWhy, string reportText,
                                                             ISet<(string, string)> allowed)
    {
        var (level, findings) = JudgeReport(reportText, allowed);
        var why = receiptWhy.ToList();
        why.AddRange(findings.Where(f => f.Level == "bad" || f.Level == "pending").Select(f => f.Text));
        return (Worst(receipt, level), why);
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
        string report = null, started = null, plugin = null, startedAt = null, mode = null;
        int flags = 0;
        if (row.LogMs != null)
        {
            var r = AttemptChain.Read(LogText(id));
            report = r.Report; started = r.Started; plugin = r.Plugin; flags = r.Flags.Count;
            var parsed = RunReport.Parse(report);
            startedAt = parsed.StartedAt; mode = parsed.Started;
        }
        string verdict = "running";
        string[] why = Lines(row.Why);
        object findings = null, recording = null;
        if (row.LogMs != null)
        {
            var allowed = Allowed();
            var (v, all) = Overall(row.Verdict, why, report, allowed);
            var (_, list) = JudgeReport(report, allowed);
            verdict = v;
            findings = list.Select(f => new { level = f.Level, text = f.Text }).ToList();
            recording = new { verdict = row.Verdict, why };
            why = all.ToArray();
        }
        return new
        {
            id, runner = row.Runner, runnerName = name, category = row.Category, spot = row.Spot, plugin, started, startedAt, mode,
            online = row.Nonce != null, issued = Iso(row.IssuedMs), received = Iso(row.LogMs), checkpoints = cps.Count,
            ended = row.LogMs != null, endReason = row.EndReason, durationMs = row.EndMs, finalTimerMs = row.FinalTimerMs,
            steps = row.Steps, flags, verdict, why, recording, findings,
            report,
        };
    }

    // --- what ran: the report's findings (pure, tested) ----------------------------

    public sealed record Finding(string Level, string Text);   // ok, bad, pending, allowed, note

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
    public static (string verdict, List<Finding> findings) JudgeReport(string text, ISet<(string, string)> allowed)
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
        else f.Add(new("bad", "The game's code is not the Steam game's - it was changed or is another version."));

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

        if (r.PracticeBefore.Length > 0)
            f.Add(new("note", "Practice was used before this attempt (" + r.PracticeBefore + "); the attempt itself started clean."));

        string verdict = f.Any(x => x.Level == "bad") ? "red" : f.Any(x => x.Level == "pending") ? "amber" : "green";
        return (verdict, f);
    }

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
        return true;
    }

    public bool Disallow(string kind, string text) =>
        _store.Update("DELETE FROM allowed_code WHERE kind = $k AND text = $t", ("$k", kind ?? ""), ("$t", text ?? "")) == 1;

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
