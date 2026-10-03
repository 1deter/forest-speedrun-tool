using Microsoft.Data.Sqlite;

namespace ForestBot.Store;

/// A stored answer (one question and its reply).
public sealed class StoredAnswer
{
    public long Id;
    public long ConversationId;
    public string UserId;
    public string ChannelId;
    public string Question;
    public string Answer;
    public string Sources;
    public string Status;
    public string Model;
    public DateTime Created;
}

public sealed class QueueItem
{
    public long Id;
    public long AnswerId;
    public string Reason;
    public string Question;
    public string Detail;
    public DateTime Created;
}

// ------------------------------------------------------------------
// The bot's SQLite file (FOREST_BOT_DATA/bot.db):
//   answers    every question + answer, grouped by conversation
//   messages   Discord message id -> answer (a reply to any part of an
//              answer continues its conversation)
//   feedback   👍 / 👎 (+ what was wrong), one vote per user per answer
//   queue      questions for research sessions: 👎, partial,
//              not_documented, failures
//   usage      per-user question times (rate limit)
//   cache      first-turn answers by normalised question + knowledge version
// ------------------------------------------------------------------
public sealed class BotStore : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly object _lock = new object();

    public BotStore(string path)
    {
        if (path != ":memory:") Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        _db = new SqliteConnection("Data Source=" + path);
        _db.Open();
        Exec(@"
PRAGMA journal_mode = WAL;
CREATE TABLE IF NOT EXISTS answers (
  id INTEGER PRIMARY KEY AUTOINCREMENT, conversation INTEGER NOT NULL, user_id TEXT, channel_id TEXT,
  question TEXT NOT NULL, answer TEXT NOT NULL, sources TEXT, status TEXT, model TEXT, created TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS answers_conv ON answers(conversation);
CREATE TABLE IF NOT EXISTS messages (message_id TEXT PRIMARY KEY, answer_id INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS feedback (
  answer_id INTEGER NOT NULL, user_id TEXT NOT NULL, vote INTEGER NOT NULL, comment TEXT, created TEXT NOT NULL,
  PRIMARY KEY (answer_id, user_id));
CREATE TABLE IF NOT EXISTS queue (
  id INTEGER PRIMARY KEY AUTOINCREMENT, answer_id INTEGER, reason TEXT NOT NULL, question TEXT, detail TEXT,
  created TEXT NOT NULL, resolved TEXT);
CREATE TABLE IF NOT EXISTS usage (user_id TEXT NOT NULL, at TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS usage_user ON usage(user_id, at);
CREATE TABLE IF NOT EXISTS cache (
  key TEXT PRIMARY KEY, kb_version TEXT NOT NULL, answer_id INTEGER NOT NULL, created TEXT NOT NULL);
");
    }

    /// Saves an answer; `conversation` 0 = a new conversation (its id is
    /// the answer's own).
    public long AddAnswer(long conversation, string userId, string channelId, string question, string answer,
        string sources, string status, string model)
    {
        lock (_lock)
        {
            using SqliteCommand cmd = _db.CreateCommand();
            cmd.CommandText = @"INSERT INTO answers (conversation, user_id, channel_id, question, answer, sources, status, model, created)
                                VALUES ($c, $u, $ch, $q, $a, $s, $st, $m, $t); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$c", conversation);
            cmd.Parameters.AddWithValue("$u", (object)userId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ch", (object)channelId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$q", question ?? "");
            cmd.Parameters.AddWithValue("$a", answer ?? "");
            cmd.Parameters.AddWithValue("$s", (object)sources ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$st", (object)status ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$m", (object)model ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$t", Now());
            long id = (long)cmd.ExecuteScalar();
            if (conversation == 0) Exec("UPDATE answers SET conversation = " + id + " WHERE id = " + id);
            return id;
        }
    }

    public void MapMessage(string messageId, long answerId)
    {
        lock (_lock)
        {
            using SqliteCommand cmd = _db.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO messages (message_id, answer_id) VALUES ($m, $a)";
            cmd.Parameters.AddWithValue("$m", messageId);
            cmd.Parameters.AddWithValue("$a", answerId);
            cmd.ExecuteNonQuery();
        }
    }

    /// The answer a Discord message belongs to, or null.
    public StoredAnswer AnswerForMessage(string messageId)
    {
        lock (_lock)
        {
            using SqliteCommand cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT answer_id FROM messages WHERE message_id = $m";
            cmd.Parameters.AddWithValue("$m", messageId);
            object id = cmd.ExecuteScalar();
            return id == null ? null : GetAnswerLocked((long)id);
        }
    }

    public StoredAnswer GetAnswer(long id)
    {
        lock (_lock) return GetAnswerLocked(id);
    }

    /// A conversation's answers, oldest first, up to and including `upTo`.
    public List<StoredAnswer> Conversation(long conversation, long upTo = long.MaxValue)
    {
        lock (_lock)
        {
            List<StoredAnswer> list = new List<StoredAnswer>();
            using SqliteCommand cmd = _db.CreateCommand();
            cmd.CommandText = Select + " WHERE conversation = $c AND id <= $u ORDER BY id";
            cmd.Parameters.AddWithValue("$c", conversation);
            cmd.Parameters.AddWithValue("$u", upTo);
            using SqliteDataReader r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadAnswer(r));
            return list;
        }
    }

    /// Records a vote (a second vote by the same user replaces the first).
    public void Vote(long answerId, string userId, int vote, string comment = null)
    {
        lock (_lock)
        {
            using SqliteCommand cmd = _db.CreateCommand();
            cmd.CommandText = @"INSERT INTO feedback (answer_id, user_id, vote, comment, created) VALUES ($a, $u, $v, $c, $t)
                                ON CONFLICT(answer_id, user_id) DO UPDATE SET vote = $v, comment = COALESCE($c, comment), created = $t";
            cmd.Parameters.AddWithValue("$a", answerId);
            cmd.Parameters.AddWithValue("$u", userId);
            cmd.Parameters.AddWithValue("$v", vote);
            cmd.Parameters.AddWithValue("$c", (object)comment ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$t", Now());
            cmd.ExecuteNonQuery();
        }
    }

    public (int up, int down) Votes(long answerId)
    {
        lock (_lock)
        {
            using SqliteCommand cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(vote > 0), 0), COALESCE(SUM(vote < 0), 0) FROM feedback WHERE answer_id = $a";
            cmd.Parameters.AddWithValue("$a", answerId);
            using SqliteDataReader r = cmd.ExecuteReader();
            r.Read();
            return (r.GetInt32(0), r.GetInt32(1));
        }
    }

    public long Enqueue(long answerId, string reason, string question, string detail)
    {
        lock (_lock)
        {
            using SqliteCommand cmd = _db.CreateCommand();
            cmd.CommandText = @"INSERT INTO queue (answer_id, reason, question, detail, created) VALUES ($a, $r, $q, $d, $t);
                                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$a", answerId);
            cmd.Parameters.AddWithValue("$r", reason);
            cmd.Parameters.AddWithValue("$q", (object)question ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$d", (object)detail ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$t", Now());
            return (long)cmd.ExecuteScalar();
        }
    }

    public List<QueueItem> OpenQueue(int limit = 100)
    {
        lock (_lock)
        {
            List<QueueItem> list = new List<QueueItem>();
            using SqliteCommand cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT id, answer_id, reason, question, detail, created FROM queue WHERE resolved IS NULL ORDER BY id LIMIT $l";
            cmd.Parameters.AddWithValue("$l", limit);
            using SqliteDataReader r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new QueueItem
                {
                    Id = r.GetInt64(0), AnswerId = r.IsDBNull(1) ? 0 : r.GetInt64(1), Reason = r.GetString(2),
                    Question = r.IsDBNull(3) ? null : r.GetString(3), Detail = r.IsDBNull(4) ? null : r.GetString(4),
                    Created = DateTime.Parse(r.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
                });
            return list;
        }
    }

    public void Resolve(long queueId)
    {
        lock (_lock) Exec("UPDATE queue SET resolved = '" + Now() + "' WHERE id = " + queueId);
    }

    /// Records a question now and says whether the user is within both
    /// limits (per hour, per day). A refused question is not recorded.
    public bool TryUse(string userId, int perHour, int perDay)
    {
        lock (_lock)
        {
            DateTime now = DateTime.UtcNow;
            int hour = CountSince(userId, now.AddHours(-1)), day = CountSince(userId, now.AddDays(-1));
            if ((perHour > 0 && hour >= perHour) || (perDay > 0 && day >= perDay)) return false;
            using SqliteCommand cmd = _db.CreateCommand();
            cmd.CommandText = "INSERT INTO usage (user_id, at) VALUES ($u, $t); DELETE FROM usage WHERE at < $old";
            cmd.Parameters.AddWithValue("$u", userId);
            cmd.Parameters.AddWithValue("$t", Now());
            cmd.Parameters.AddWithValue("$old", now.AddDays(-2).ToString("o"));
            cmd.ExecuteNonQuery();
            return true;
        }
    }

    public StoredAnswer Cached(string key, string kbVersion)
    {
        lock (_lock)
        {
            using SqliteCommand cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT answer_id FROM cache WHERE key = $k AND kb_version = $v";
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", kbVersion);
            object id = cmd.ExecuteScalar();
            return id == null ? null : GetAnswerLocked((long)id);
        }
    }

    public void Cache(string key, string kbVersion, long answerId)
    {
        lock (_lock)
        {
            using SqliteCommand cmd = _db.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO cache (key, kb_version, answer_id, created) VALUES ($k, $v, $a, $t)";
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", kbVersion);
            cmd.Parameters.AddWithValue("$a", answerId);
            cmd.Parameters.AddWithValue("$t", Now());
            cmd.ExecuteNonQuery();
        }
    }

    /// Drops a cached answer (a 👎 on it).
    public void Uncache(long answerId)
    {
        lock (_lock) Exec("DELETE FROM cache WHERE answer_id = " + answerId);
    }

    /// The cache key: the question lowercased, punctuation and extra spaces
    /// gone.
    public static string CacheKey(string question)
    {
        System.Text.StringBuilder b = new System.Text.StringBuilder();
        bool space = false;
        foreach (char ch in (question ?? "").ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) { b.Append(ch); space = false; }
            else if (!space && b.Length > 0) { b.Append(' '); space = true; }
        }
        return b.ToString().Trim();
    }

    private int CountSince(string userId, DateTime since)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM usage WHERE user_id = $u AND at >= $s";
        cmd.Parameters.AddWithValue("$u", userId);
        cmd.Parameters.AddWithValue("$s", since.ToString("o"));
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private const string Select = "SELECT id, conversation, user_id, channel_id, question, answer, sources, status, model, created FROM answers";

    private StoredAnswer GetAnswerLocked(long id)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = Select + " WHERE id = $i";
        cmd.Parameters.AddWithValue("$i", id);
        using SqliteDataReader r = cmd.ExecuteReader();
        return r.Read() ? ReadAnswer(r) : null;
    }

    private static StoredAnswer ReadAnswer(SqliteDataReader r) => new StoredAnswer
    {
        Id = r.GetInt64(0), ConversationId = r.GetInt64(1),
        UserId = r.IsDBNull(2) ? null : r.GetString(2), ChannelId = r.IsDBNull(3) ? null : r.GetString(3),
        Question = r.GetString(4), Answer = r.GetString(5),
        Sources = r.IsDBNull(6) ? null : r.GetString(6), Status = r.IsDBNull(7) ? null : r.GetString(7),
        Model = r.IsDBNull(8) ? null : r.GetString(8),
        Created = DateTime.Parse(r.GetString(9), null, System.Globalization.DateTimeStyles.RoundtripKind),
    };

    private void Exec(string sql)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string Now() => DateTime.UtcNow.ToString("o");

    public void Dispose() => _db.Dispose();
}
