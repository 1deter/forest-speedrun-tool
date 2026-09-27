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

    // --- routes ---------------------------------------------------------

    /// Records a route (a segment at one fingerprint) the first time it is
    /// seen. A community pack always wins the name and marks it community.
    public void SeeRoute(string segmentId, string route, string name, string category, string block, bool community)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = community
            ? @"INSERT INTO routes VALUES ($s, $r, $n, $c, $b, 1, $now)
                ON CONFLICT DO UPDATE SET name = $n, category = $c, block = $b, community = 1"
            : @"INSERT INTO routes VALUES ($s, $r, $n, $c, $b, 0, $now) ON CONFLICT DO NOTHING";
        cmd.Parameters.AddWithValue("$s", segmentId);
        cmd.Parameters.AddWithValue("$r", route);
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$c", category);
        cmd.Parameters.AddWithValue("$b", block);
        cmd.Parameters.AddWithValue("$now", Now());
        cmd.ExecuteNonQuery();
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

    // --- spot submissions -------------------------------------------------

    public long AddSubmission(string runnerId, string segmentId, string name, string text)
    {
        long id = (long)Scalar(@"INSERT INTO submissions (runner_id, segment_id, name, uploaded)
                                 VALUES ($r, $s, $n, $now) RETURNING id",
                               ("$r", runnerId), ("$s", segmentId), ("$n", name), ("$now", Now()));
        WriteGz(Path.Combine(_dir, "submissions", id + ".foseg.gz"), text);
        return id;
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
