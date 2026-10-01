using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace ForestSite;

// ------------------------------------------------------------------
// Everything the site keeps: a SQLite file for the index (runners, the
// routes seen, runs, spot submissions) and the uploaded texts as gzip
// files beside it. The .run text is kept verbatim - the site's parsed
// view is rebuilt from it, so a format change never needs a migration.
//
//   <data>/forest.db
//   <data>/runs/<segment id>/<run id>.run.gz
//   <data>/submissions/<id>.foseg.gz
// ------------------------------------------------------------------
public sealed class Store
{
    private readonly string _dir;
    private readonly string _connection;

    public Store(string dataDir)
    {
        _dir = dataDir;
        Directory.CreateDirectory(_dir);
        _connection = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_dir, "forest.db"),
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        using var c = Open();
        Exec(c, "PRAGMA journal_mode=WAL;");
        Exec(c, @"
CREATE TABLE IF NOT EXISTS runners (
  id TEXT PRIMARY KEY, name TEXT NOT NULL, token_hash TEXT,
  created TEXT NOT NULL, banned INTEGER NOT NULL DEFAULT 0);
CREATE TABLE IF NOT EXISTS routes (
  segment_id TEXT NOT NULL, route TEXT NOT NULL, name TEXT NOT NULL,
  category TEXT NOT NULL, block TEXT NOT NULL, community INTEGER NOT NULL,
  first_seen TEXT NOT NULL, PRIMARY KEY (segment_id, route));
CREATE TABLE IF NOT EXISTS runs (
  id INTEGER PRIMARY KEY AUTOINCREMENT, segment_id TEXT NOT NULL, route TEXT NOT NULL,
  runner_id TEXT NOT NULL, runner_name TEXT NOT NULL, duration REAL NOT NULL,
  splits TEXT NOT NULL, recorded TEXT NOT NULL, uploaded TEXT NOT NULL,
  flagged INTEGER NOT NULL DEFAULT 0, hidden INTEGER NOT NULL DEFAULT 0);
CREATE INDEX IF NOT EXISTS runs_route ON runs (segment_id, route);
CREATE UNIQUE INDEX IF NOT EXISTS runs_once ON runs (runner_id, segment_id, recorded, duration);
CREATE TABLE IF NOT EXISTS submissions (
  id INTEGER PRIMARY KEY AUTOINCREMENT, runner_id TEXT NOT NULL, segment_id TEXT NOT NULL,
  name TEXT NOT NULL, uploaded TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'open');");

        // A runner's spot belongs to the runner who first uploaded a run on
        // it (2026-09-27): their later uploads carry its renames, and the
        // site shows who made it. Older databases get the column and the
        // first uploader of each route.
        if (!HasColumn(c, "routes", "owner"))
        {
            Exec(c, "ALTER TABLE routes ADD COLUMN owner TEXT NOT NULL DEFAULT '';");
            Exec(c, @"UPDATE routes SET owner = COALESCE((SELECT x.runner_id FROM runs x
                      WHERE x.segment_id = routes.segment_id AND x.route = routes.route ORDER BY x.id LIMIT 1), '')
                      WHERE community = 0;");
        }
        // Admins besides the owner (FOREST_ADMIN_TOKEN): a named token each,
        // made and revoked by the owner on /admin (author, 2026-09-27: not
        // one shared key). Every change an admin makes is logged.
        Exec(c, @"
CREATE TABLE IF NOT EXISTS admins (
  id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, token_hash TEXT NOT NULL UNIQUE,
  created TEXT NOT NULL, revoked INTEGER NOT NULL DEFAULT 0);
CREATE TABLE IF NOT EXISTS admin_log (
  id INTEGER PRIMARY KEY AUTOINCREMENT, admin TEXT NOT NULL, action TEXT NOT NULL,
  status INTEGER NOT NULL, at TEXT NOT NULL);");
        if (!HasColumn(c, "submissions", "start_state"))
            Exec(c, "ALTER TABLE submissions ADD COLUMN start_state INTEGER NOT NULL DEFAULT 0;");
    }

    private static bool HasColumn(SqliteConnection c, string table, string column)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(" + table + ")";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (r.GetString(1) == column) return true;
        return false;
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connection);
        c.Open();
        return c;
    }

    // --- runners --------------------------------------------------------

    /// A new token for a runner id nobody holds yet; null when it is taken.
    public string Register(string runnerId, string name)
    {
        string token = "ft_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        using var c = Open();
        using var cmd = c.CreateCommand();
        // A runner whose token the author reset (token_hash NULL) can
        // register again; a held one cannot.
        cmd.CommandText = @"
INSERT INTO runners (id, name, token_hash, created) VALUES ($id, $name, $hash, $now)
ON CONFLICT(id) DO UPDATE SET token_hash = $hash, name = $name WHERE runners.token_hash IS NULL";
        cmd.Parameters.AddWithValue("$id", runnerId);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$hash", Hash(token));
        cmd.Parameters.AddWithValue("$now", Now());
        return cmd.ExecuteNonQuery() == 1 ? token : null;
    }

    /// The runner id a token belongs to; null for an unknown token or a
    /// banned runner.
    public string RunnerOf(string token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id FROM runners WHERE token_hash = $h AND banned = 0";
        cmd.Parameters.AddWithValue("$h", Hash(token));
        return cmd.ExecuteScalar() as string;
    }

    public bool ResetToken(string runnerId) =>
        Update("UPDATE runners SET token_hash = NULL WHERE id = $id", ("$id", runnerId)) == 1;

    public bool Ban(string runnerId, bool banned) =>
        Update("UPDATE runners SET banned = $b WHERE id = $id", ("$id", runnerId), ("$b", banned ? 1 : 0)) == 1;

    /// Every runner, newest first, for the admin page.
    public List<object> Runners()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT r.id, r.name, r.created, r.banned, r.token_hash IS NOT NULL,
                                   (SELECT COUNT(*) FROM runs x WHERE x.runner_id = r.id),
                                   (SELECT MAX(x.uploaded) FROM runs x WHERE x.runner_id = r.id)
                            FROM runners r ORDER BY r.created DESC LIMIT 500";
        var list = new List<object>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new
            {
                id = r.GetString(0), name = r.GetString(1), created = r.GetString(2), banned = r.GetInt64(3) == 1,
                hasToken = r.GetInt64(4) == 1, runs = r.GetInt64(5), lastUpload = r.IsDBNull(6) ? null : r.GetString(6),
            });
        return list;
    }

    // --- routes ---------------------------------------------------------

    /// Records a route (a segment at one fingerprint) the first time it is
    /// seen. A community pack always wins the name and marks it community.
    /// A runner's upload makes them the owner of a new route; the owner's
    /// later uploads update its description (block) and the spot's name and
    /// category on all their routes (maks, 2026-09-27: a rename in game did
    /// not reach the site). Anyone else's copy changes nothing: with `copy`
    /// (Runs.Upload: the spot is someone else's) a new route is recorded
    /// under the spot's owner and labels, and an existing one is left alone.
    public void SeeRoute(string segmentId, string route, string name, string category, string block, bool community, string owner = "", bool copy = false)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = copy && !community
            ? @"INSERT INTO routes (segment_id, route, name, category, block, community, first_seen, owner)
                VALUES ($s, $r, $n, $c, $b, 0, $now, $o) ON CONFLICT DO NOTHING"
            : community
            ? @"INSERT INTO routes (segment_id, route, name, category, block, community, first_seen, owner)
                VALUES ($s, $r, $n, $c, $b, 1, $now, '')
                ON CONFLICT DO UPDATE SET name = $n, category = $c, block = $b, community = 1"
            : @"INSERT INTO routes (segment_id, route, name, category, block, community, first_seen, owner)
                VALUES ($s, $r, $n, $c, $b, 0, $now, $o)
                ON CONFLICT DO UPDATE SET block = $b WHERE community = 0 AND owner = $o;
                UPDATE routes SET name = $n, category = $c WHERE segment_id = $s AND community = 0 AND owner = $o;";
        cmd.Parameters.AddWithValue("$s", segmentId);
        cmd.Parameters.AddWithValue("$r", route);
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$c", category);
        cmd.Parameters.AddWithValue("$b", block);
        cmd.Parameters.AddWithValue("$o", owner ?? "");
        cmd.Parameters.AddWithValue("$now", Now());
        cmd.ExecuteNonQuery();
    }

    /// Who a spot belongs to: its community route if it has one, else its
    /// first-seen route (owner, labels, block). Null for a new spot.
    public (string owner, bool community, string name, string category, string block)? SpotHolder(string segmentId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT owner, community, name, category, block FROM routes WHERE segment_id = $s
                            ORDER BY community DESC, first_seen, route LIMIT 1";
        cmd.Parameters.AddWithValue("$s", segmentId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return (r.GetString(0), r.GetInt64(1) == 1, r.GetString(2), r.GetString(3), r.GetString(4));
    }

    /// Community marks that no longer have a pack (the author removed it).
    public void ClearCommunityExcept(ICollection<string> keep)
    {
        using var c = Open();
        using var read = c.CreateCommand();
        read.CommandText = "SELECT segment_id, route FROM routes WHERE community = 1";
        var drop = new List<(string, string)>();
        using (var r = read.ExecuteReader())
            while (r.Read())
                if (!keep.Contains(r.GetString(0) + "|" + r.GetString(1))) drop.Add((r.GetString(0), r.GetString(1)));
        foreach (var (s, route) in drop)
            Update("UPDATE routes SET community = 0 WHERE segment_id = $s AND route = $r", ("$s", s), ("$r", route));
    }

    // --- runs -----------------------------------------------------------

    /// Stores one run; the id of the stored run (the existing one for a
    /// re-upload of the same attempt), and whether it was new.
    public (long id, bool added) AddRun(string segmentId, string route, string runnerId, string runnerName,
                                        float duration, float[] splits, DateTime recorded, string text, bool flagged)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO runs (segment_id, route, runner_id, runner_name, duration, splits, recorded, uploaded, flagged)
VALUES ($s, $r, $rid, $rn, $d, $sp, $rec, $now, $f)
ON CONFLICT DO NOTHING RETURNING id";
        cmd.Parameters.AddWithValue("$s", segmentId);
        cmd.Parameters.AddWithValue("$r", route);
        cmd.Parameters.AddWithValue("$rid", runnerId);
        cmd.Parameters.AddWithValue("$rn", runnerName);
        cmd.Parameters.AddWithValue("$d", duration);
        cmd.Parameters.AddWithValue("$sp", Json.Floats(splits));
        cmd.Parameters.AddWithValue("$rec", recorded.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$now", Now());
        cmd.Parameters.AddWithValue("$f", flagged ? 1 : 0);
        object id = cmd.ExecuteScalar();

        if (id == null)
        {
            using var find = c.CreateCommand();
            find.Transaction = tx;
            find.CommandText = "SELECT id FROM runs WHERE runner_id = $rid AND segment_id = $s AND recorded = $rec AND duration = $d";
            foreach (SqliteParameter p in cmd.Parameters)
                if (p.ParameterName is "$rid" or "$s" or "$rec" or "$d") find.Parameters.AddWithValue(p.ParameterName, p.Value);
            long existing = (long)find.ExecuteScalar();
            tx.Commit();
            return (existing, false);
        }

        long newId = (long)id;
        WriteGz(RunPath(segmentId, newId), text);
        tx.Commit();

        // The newest name is the one shown.
        Update("UPDATE runners SET name = $n WHERE id = $id", ("$id", runnerId), ("$n", runnerName));
        return (newId, true);
    }

    public string RunText(string segmentId, long id)
    {
        string path = RunPath(segmentId, id);
        return File.Exists(path) ? ReadGz(path) : null;
    }

    public bool HideRun(long id, bool hidden) =>
        Update("UPDATE runs SET hidden = $h WHERE id = $id", ("$id", id), ("$h", hidden ? 1 : 0)) == 1;

    public bool DeleteRun(long id)
    {
        string seg = Scalar("SELECT segment_id FROM runs WHERE id = $id", ("$id", id)) as string;
        if (seg == null) return false;
        Update("DELETE FROM runs WHERE id = $id", ("$id", id));
        string path = RunPath(seg, id);
        if (File.Exists(path)) File.Delete(path);
        return true;
    }

    /// Removes a runner's spot from the site: every route and every run
    /// (a runner's accidental upload). A community spot is refused - it
    /// comes back from community/ on the next start. The spot reappears if
    /// its owner uploads a run on it again.
    public (int runs, string error) DeleteSpot(string segmentId)
    {
        if (Convert.ToInt64(Scalar("SELECT COUNT(*) FROM routes WHERE segment_id = $s AND community = 1", ("$s", segmentId))) > 0)
            return (0, "a community spot - remove its file from community/ instead");
        int routes = Update("DELETE FROM routes WHERE segment_id = $s", ("$s", segmentId));
        int runs = Update("DELETE FROM runs WHERE segment_id = $s", ("$s", segmentId));
        if (routes == 0 && runs == 0) return (0, "no such spot");
        string dir = Path.Combine(_dir, "runs", SafeName(segmentId));
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        return (runs, null);
    }

    // --- admins -------------------------------------------------------------

    /// A new admin: the token is returned once, only its hash is kept.
    public (long id, string token) AddAdmin(string name)
    {
        string token = "fa_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        long id = (long)Scalar("INSERT INTO admins (name, token_hash, created) VALUES ($n, $h, $now) RETURNING id",
                               ("$n", name), ("$h", Hash(token)), ("$now", Now()));
        return (id, token);
    }

    /// The admin a token belongs to; null for unknown or revoked.
    public string AdminOf(string token) =>
        string.IsNullOrEmpty(token) ? null : Scalar("SELECT name FROM admins WHERE token_hash = $h AND revoked = 0", ("$h", Hash(token))) as string;

    public bool RevokeAdmin(long id) => Update("UPDATE admins SET revoked = 1 WHERE id = $id AND revoked = 0", ("$id", id)) == 1;

    public List<object> Admins()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, name, created, revoked FROM admins ORDER BY id";
        var list = new List<object>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new { id = r.GetInt64(0), name = r.GetString(1), created = r.GetString(2), revoked = r.GetInt64(3) == 1 });
        return list;
    }

    public void LogAdmin(string admin, string action, int status) =>
        Update("INSERT INTO admin_log (admin, action, status, at) VALUES ($a, $x, $s, $now)",
               ("$a", admin), ("$x", action), ("$s", status), ("$now", Now()));

    public List<object> AdminLog()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT admin, action, status, at FROM admin_log ORDER BY id DESC LIMIT 200";
        var list = new List<object>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new { admin = r.GetString(0), action = r.GetString(1), status = r.GetInt64(2), at = r.GetString(3) });
        return list;
    }

    // --- spot submissions -------------------------------------------------

    /// A spot for the author. The same runner's second submit of a spot
    /// still waiting replaces it (a fix before the author has looked).
    public (long id, bool replaced) AddSubmission(string runnerId, string segmentId, string name, string text, bool startState)
    {
        object open = Scalar("SELECT id FROM submissions WHERE runner_id = $r AND segment_id = $s AND status = 'open' ORDER BY id DESC LIMIT 1",
                             ("$r", runnerId), ("$s", segmentId));
        long id;
        if (open is long existing)
        {
            id = existing;
            Update("UPDATE submissions SET name = $n, uploaded = $now, start_state = $st WHERE id = $id",
                   ("$id", id), ("$n", name), ("$now", Now()), ("$st", startState ? 1 : 0));
        }
        else
            id = (long)Scalar(@"INSERT INTO submissions (runner_id, segment_id, name, uploaded, start_state)
                                VALUES ($r, $s, $n, $now, $st) RETURNING id",
                              ("$r", runnerId), ("$s", segmentId), ("$n", name), ("$now", Now()), ("$st", startState ? 1 : 0));
        WriteGz(Path.Combine(_dir, "submissions", id + ".foseg.gz"), text);
        return (id, open is long);
    }

    /// A runner's submissions still waiting, other than one for `exceptSegment`
    /// (a re-submit replaces that one).
    public int OpenSubmissions(string runnerId, string exceptSegment) =>
        Convert.ToInt32(Scalar("SELECT COUNT(*) FROM submissions WHERE runner_id = $r AND status = 'open' AND segment_id <> $s",
                               ("$r", runnerId), ("$s", exceptSegment)));

    /// Submissions, newest first, with the runner's name and whether the
    /// spot is already a community spot (an update, not a new one).
    public List<object> Submissions()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT s.id, s.runner_id, COALESCE(r.name, ''), s.segment_id, s.name, s.uploaded, s.status, s.start_state,
                                   EXISTS (SELECT 1 FROM routes x WHERE x.segment_id = s.segment_id AND x.community = 1)
                            FROM submissions s LEFT JOIN runners r ON r.id = s.runner_id
                            ORDER BY s.id DESC LIMIT 200";
        var list = new List<object>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new
            {
                id = r.GetInt64(0), runner = r.GetString(1), runnerName = r.GetString(2), segment = r.GetString(3),
                name = r.GetString(4), uploaded = r.GetString(5), status = r.GetString(6),
                startState = r.GetInt64(7) == 1, community = r.GetInt64(8) == 1,
            });
        return list;
    }

    public string SubmissionText(long id)
    {
        string path = Path.Combine(_dir, "submissions", id + ".foseg.gz");
        return File.Exists(path) ? ReadGz(path) : null;
    }

    public bool SetSubmissionStatus(long id, string status) =>
        Update("UPDATE submissions SET status = $st WHERE id = $id", ("$id", id), ("$st", status)) == 1;

    // --- helpers ----------------------------------------------------------

    private string RunPath(string segmentId, long id) =>
        Path.Combine(_dir, "runs", SafeName(segmentId), id + ".run.gz");

    /// A segment id as a folder name (ids are `s-` + hex, older ones have
    /// dots and slashes).
    public static string SafeName(string id)
    {
        var sb = new StringBuilder(id.Length);
        foreach (char ch in id) sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '.' ? ch : '_');
        string s = sb.ToString().Trim('.');
        return s.Length == 0 ? "_" : s;
    }

    private static void WriteGz(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using var f = File.Create(path);
        using var gz = new GZipStream(f, CompressionLevel.Optimal);
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        gz.Write(bytes, 0, bytes.Length);
    }

    private static string ReadGz(string path)
    {
        using var f = File.OpenRead(path);
        using var gz = new GZipStream(f, CompressionMode.Decompress);
        using var r = new StreamReader(gz, Encoding.UTF8);
        return r.ReadToEnd();
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string Now() => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public int Update(string sql, params (string, object)[] args)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v);
        return cmd.ExecuteNonQuery();
    }

    public object Scalar(string sql, params (string, object)[] args)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v);
        return cmd.ExecuteScalar();
    }
}
