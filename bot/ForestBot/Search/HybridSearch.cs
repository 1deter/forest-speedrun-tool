using System.Security.Cryptography;
using System.Text;
using ForestBot.Knowledge;
using Microsoft.Data.Sqlite;

namespace ForestBot.Search;

/// A search result: the chunk and why it ranked (for logs and tests).
public sealed class Hit
{
    public Chunk Chunk;
    public double Score;
    public int KeywordRank = -1;
    public int VectorRank = -1;
}

// ------------------------------------------------------------------
// Keyword (FTS5 / BM25) + meaning (local embeddings) merged by
// reciprocal rank fusion: each list contributes 1 / (k + rank). Keyword
// finds exact names, embeddings find paraphrases ("why do I fly when I
// pause"); a chunk both agree on wins. With no embedder it is plain
// keyword search.
// ------------------------------------------------------------------
public sealed class HybridSearch : IDisposable
{
    public const int RrfK = 60;

    private readonly Corpus _corpus;
    private readonly KeywordIndex _keyword;
    private readonly IEmbedder _embedder;
    private readonly float[][] _vectors;

    public bool HasVectors => _embedder != null;

    /// `cachePath`: a SQLite file keeping chunk vectors by text hash, so a
    /// restart only embeds what changed. Null = no cache.
    public HybridSearch(Corpus corpus, IEmbedder embedder, string cachePath, Action<string> log)
    {
        _corpus = corpus;
        _keyword = new KeywordIndex(corpus.Chunks);
        _embedder = embedder;
        if (embedder != null) _vectors = EmbedAll(corpus.Chunks, embedder, cachePath, log ?? (_ => { }));
    }

    public List<Hit> Search(string query, int limit, ICollection<string> kinds = null)
    {
        Dictionary<Chunk, Hit> merged = new Dictionary<Chunk, Hit>();
        int depth = Math.Max(limit * 4, 20);

        List<(Chunk chunk, double score)> kw = _keyword.Search(query, depth, kinds);
        for (int i = 0; i < kw.Count; i++)
        {
            Hit h = Get(merged, kw[i].chunk);
            h.KeywordRank = i;
            h.Score += 1.0 / (RrfK + i + 1);
        }

        if (_embedder != null && !string.IsNullOrWhiteSpace(query))
        {
            float[] q = _embedder.EmbedQuery(query);
            List<(int index, float sim)> best = new List<(int, float)>();
            for (int i = 0; i < _vectors.Length; i++)
            {
                Chunk c = _corpus.Chunks[i];
                if (kinds != null && kinds.Count > 0 && !kinds.Contains(c.Kind)) continue;
                best.Add((i, Vectors.Dot(q, _vectors[i])));
            }
            best.Sort((a, b) => b.sim.CompareTo(a.sim));
            for (int r = 0; r < best.Count && r < depth; r++)
            {
                Hit h = Get(merged, _corpus.Chunks[best[r].index]);
                h.VectorRank = r;
                h.Score += 1.0 / (RrfK + r + 1);
            }
        }

        List<Hit> hits = merged.Values.ToList();
        hits.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (hits.Count > limit) hits.RemoveRange(limit, hits.Count - limit);
        return hits;
    }

    /// Cosine similarity of two texts by the embedder (the answer cache's
    /// "same question" test); -1 without an embedder.
    public float Similarity(float[] a, float[] b) => a == null || b == null ? -1 : Vectors.Dot(a, b);

    public float[] EmbedQuery(string text) => _embedder?.EmbedQuery(text);

    private static Hit Get(Dictionary<Chunk, Hit> merged, Chunk c)
    {
        if (!merged.TryGetValue(c, out Hit h)) merged[c] = h = new Hit { Chunk = c };
        return h;
    }

    /// The text a chunk is embedded as: its title and keywords lead, so a
    /// short section still says what it is about.
    public static string EmbedText(Chunk c)
    {
        string head = c.Title + (string.IsNullOrEmpty(c.Keywords) ? "" : " (" + c.Keywords + ")");
        return head + "\n" + c.Text;
    }

    private static float[][] EmbedAll(List<Chunk> chunks, IEmbedder embedder, string cachePath, Action<string> log)
    {
        float[][] vectors = new float[chunks.Count][];
        SqliteConnection db = null;
        if (cachePath != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(cachePath)));
            db = new SqliteConnection("Data Source=" + cachePath);
            db.Open();
            using SqliteCommand create = db.CreateCommand();
            create.CommandText = "CREATE TABLE IF NOT EXISTS vectors (key TEXT PRIMARY KEY, v BLOB NOT NULL)";
            create.ExecuteNonQuery();
        }
        int made = 0;
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            for (int i = 0; i < chunks.Count; i++)
            {
                string text = EmbedText(chunks[i]);
                string key = embedder.ModelId + ":" + Hash(text);
                float[] v = db == null ? null : Load(db, key);
                if (v == null)
                {
                    v = embedder.EmbedDocument(text);
                    made++;
                    if (db != null) Store(db, key, v);
                }
                vectors[i] = v;
            }
        }
        finally { db?.Dispose(); }
        log("Embeddings: " + chunks.Count + " chunks, " + made + " embedded now, " + (chunks.Count - made) + " from the cache (" + sw.ElapsedMilliseconds + " ms)");
        return vectors;
    }

    private static float[] Load(SqliteConnection db, string key)
    {
        using SqliteCommand cmd = db.CreateCommand();
        cmd.CommandText = "SELECT v FROM vectors WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() is byte[] b ? Vectors.FromBytes(b) : null;
    }

    private static void Store(SqliteConnection db, string key, float[] v)
    {
        using SqliteCommand cmd = db.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO vectors (key, v) VALUES ($k, $v)";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", Vectors.ToBytes(v));
        cmd.ExecuteNonQuery();
    }

    public static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).Substring(0, 24);

    public void Dispose()
    {
        _keyword.Dispose();
        (_embedder as IDisposable)?.Dispose();
    }
}
