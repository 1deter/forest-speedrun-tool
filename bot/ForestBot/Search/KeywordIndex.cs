using System.Text;
using ForestBot.Knowledge;
using Microsoft.Data.Sqlite;

namespace ForestBot.Search;

// ------------------------------------------------------------------
// BM25 keyword search over the corpus: an in-memory SQLite FTS5 table.
// Finds exact names (`HandleLanded`, `waitForInput`, "keycard 210") that
// embeddings blur. Titles and keywords (card aliases) weigh more than
// the text.
// ------------------------------------------------------------------
public sealed class KeywordIndex : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly List<Chunk> _chunks;

    /// Words too common to search on.
    private static readonly HashSet<string> Stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "a","an","the","and","or","but","is","are","was","were","be","been","do","does","did","doing",
        "how","why","what","when","where","which","who","whom","can","could","would","should","will",
        "i","me","my","you","your","it","its","this","that","these","those","to","of","in","on","at",
        "for","with","by","from","as","if","so","than","then","there","here","about","into","out",
        "up","down","work","works","get","gets","got","make","there's","whats","what's","im","i'm",
        "game","forest","please","explain","tell","much","many","any","some","just","also","not",
    };

    public KeywordIndex(IReadOnlyList<Chunk> chunks)
    {
        _chunks = new List<Chunk>(chunks);
        _db = new SqliteConnection("Data Source=:memory:");
        _db.Open();
        Exec("CREATE VIRTUAL TABLE fts USING fts5(title, keywords, body, tokenize = 'porter unicode61')");
        using SqliteTransaction tx = _db.BeginTransaction();
        using SqliteCommand ins = _db.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = "INSERT INTO fts(rowid, title, keywords, body) VALUES ($r, $t, $k, $b)";
        SqliteParameter r = ins.Parameters.Add("$r", SqliteType.Integer);
        SqliteParameter t = ins.Parameters.Add("$t", SqliteType.Text);
        SqliteParameter k = ins.Parameters.Add("$k", SqliteType.Text);
        SqliteParameter b = ins.Parameters.Add("$b", SqliteType.Text);
        for (int i = 0; i < _chunks.Count; i++)
        {
            r.Value = i;
            t.Value = _chunks[i].Title ?? "";
            k.Value = _chunks[i].Keywords ?? "";
            b.Value = _chunks[i].Text ?? "";
            ins.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// Best matches first. `kinds` (card, doc, fsm, glossary) filters.
    public List<(Chunk chunk, double score)> Search(string query, int limit, ICollection<string> kinds = null)
    {
        List<(Chunk, double)> hits = new List<(Chunk, double)>();
        string match = ToMatch(query);
        if (match.Length == 0) return hits;
        using SqliteCommand cmd = _db.CreateCommand();
        // bm25 weights: title, keywords, body. Lower = better in SQLite.
        cmd.CommandText = "SELECT rowid, bm25(fts, 6.0, 8.0, 1.0) AS s FROM fts WHERE fts MATCH $m ORDER BY s LIMIT $l";
        cmd.Parameters.AddWithValue("$m", match);
        cmd.Parameters.AddWithValue("$l", kinds == null ? limit : limit * 6);
        using SqliteDataReader rd = cmd.ExecuteReader();
        while (rd.Read() && hits.Count < limit)
        {
            Chunk c = _chunks[rd.GetInt32(0)];
            if (kinds != null && kinds.Count > 0 && !kinds.Contains(c.Kind)) continue;
            hits.Add((c, -rd.GetDouble(1)));
        }
        return hits;
    }

    /// The query as an FTS5 expression: its words OR-ed, each quoted (so
    /// nothing in a question is read as FTS syntax), stop words dropped.
    /// Identifiers keep their dots split ("Type.Method" -> both words).
    public static string ToMatch(string query)
    {
        List<string> words = new List<string>();
        StringBuilder w = new StringBuilder();
        foreach (char ch in (query ?? "") + " ")
        {
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '%') { w.Append(ch); continue; }
            if (w.Length > 0)
            {
                string word = w.ToString().Replace("%", "");
                w.Clear();
                if (word.Length == 0 || Stop.Contains(word)) continue;
                if (word.Length == 1 && !char.IsDigit(word[0])) continue;
                if (!words.Contains(word, StringComparer.OrdinalIgnoreCase)) words.Add(word);
            }
        }
        StringBuilder m = new StringBuilder();
        foreach (string word in words)
        {
            if (m.Length > 0) m.Append(" OR ");
            m.Append('"').Append(word.Replace("\"", "")).Append('"');
        }
        return m.ToString();
    }

    private void Exec(string sql)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _db.Dispose();
}
