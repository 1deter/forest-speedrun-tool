using System.Security.Cryptography;
using System.Text;

namespace ForestBot.Knowledge;

/// One searchable piece: a card section, a docs section, an FSM state.
public sealed class Chunk
{
    /// Stable id the model passes back to read it: "card:bomb-boost#numbers",
    /// "doc:game-notes#deaths", "fsm:player-damageFSM#gotHitFall".
    public string Id;
    /// card | glossary | doc | fsm
    public string Kind;
    /// What the citation shows ("Bomb boost - Numbers", "game-notes: Deaths").
    public string Title;
    /// Extra words to match (a card's aliases), weighted like the title.
    public string Keywords = "";
    public string Text;
}

// ------------------------------------------------------------------
// Everything the bot can search, loaded from a folder laid out like the
// repo (`knowledge/`, `docs/`): cards, the glossary, docs sections, FSM
// states. Built once at startup; read-only after.
// ------------------------------------------------------------------
public sealed class Corpus
{
    /// Longest chunk before a section is split into parts (chars).
    public const int MaxChunk = 6000;

    public readonly Dictionary<string, Card> Cards = new Dictionary<string, Card>(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, FsmText> Fsms = new Dictionary<string, FsmText>(StringComparer.OrdinalIgnoreCase);
    public readonly List<Chunk> Chunks = new List<Chunk>();
    public readonly Dictionary<string, Chunk> ById = new Dictionary<string, Chunk>(StringComparer.OrdinalIgnoreCase);
    public readonly List<string> Problems = new List<string>();
    public string Glossary = "";
    /// A hash of every file read - the answer cache's key part.
    public string Version = "";

    /// The docs searched besides the cards, by file name (without .md).
    public static readonly string[] DocNames = { "game-notes", "savestates", "run-mode" };

    public static Corpus Load(string root)
    {
        Corpus c = new Corpus();
        IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        string cards = Path.Combine(root, "knowledge", "cards");
        if (Directory.Exists(cards))
            foreach (string path in Sorted(Directory.GetFiles(cards, "*.md")))
            {
                string text = Read(path, hash);
                c.AddCard(Card.Parse(text, Path.GetFileNameWithoutExtension(path), c.Problems));
            }
        else c.Problems.Add("no knowledge/cards folder under " + root);

        string glossary = Path.Combine(root, "knowledge", "glossary.md");
        if (File.Exists(glossary))
        {
            c.Glossary = Read(glossary, hash).Replace("\r\n", "\n").Trim();
            c.AddParts("glossary", "glossary", "Glossary", "", c.Glossary);   // in parts once it outgrows a chunk
        }

        foreach (string name in DocNames)
        {
            string path = Path.Combine(root, "docs", name + ".md");
            if (File.Exists(path)) c.AddDoc(name, Read(path, hash));
        }

        string fsm = Path.Combine(root, "docs", "fsm");
        if (Directory.Exists(fsm))
            foreach (string path in Sorted(Directory.GetFiles(fsm, "*.txt")))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                c.AddFsm(FsmText.Parse(name, Read(path, hash)));
            }

        c.Version = Convert.ToHexString(hash.GetHashAndReset()).Substring(0, 16).ToLowerInvariant();
        foreach (Card card in c.Cards.Values)
            foreach (string r in card.Related)
                if (!c.Cards.ContainsKey(r)) c.Problems.Add(card.Id + ": related card '" + r + "' does not exist");
        return c;
    }

    public void AddCard(Card card)
    {
        if (Cards.ContainsKey(card.Id)) { Problems.Add("duplicate card id " + card.Id); return; }
        Cards[card.Id] = card;
        string keywords = card.Title + ", " + string.Join(", ", card.Aliases);
        Add(new Chunk
        {
            Id = "card:" + card.Id, Kind = "card", Title = card.Title, Keywords = keywords,
            Text = card.Summary,
        });
        foreach ((string heading, string text) in card.Sections)
            AddParts("card:" + card.Id + "#" + MarkdownSections.Slug(heading), "card",
                card.Title + " - " + heading, keywords, text);
    }

    public void AddDoc(string name, string markdown)
    {
        foreach (MarkdownSections.Section s in MarkdownSections.Split(markdown, 3))
        {
            if (s.Level == 0 || s.Text.Trim().Length == 0) continue;
            string title = name + ": " + (s.Parents.Count > 1 ? s.Parents[^1] + " / " : "") + s.Heading;
            AddParts("doc:" + name + "#" + MarkdownSections.Slug(s.Heading), "doc", title, "", s.Text);
        }
    }

    public void AddFsm(FsmText f)
    {
        Fsms[f.Name] = f;
        AddParts("fsm:" + f.Name, "fsm", "FSM " + f.Name + " (overview)", "", f.Overview());
        foreach ((string state, string text) in f.States)
            AddParts("fsm:" + f.Name + "#" + state, "fsm", "FSM " + f.Name + " / " + state, state, text);
    }

    private void AddParts(string id, string kind, string title, string keywords, string text)
    {
        text = text.Trim('\n');
        if (text.Length <= MaxChunk)
        {
            Add(new Chunk { Id = id, Kind = kind, Title = title, Keywords = keywords, Text = text });
            return;
        }
        List<string> parts = SplitParagraphs(text, MaxChunk);
        for (int i = 0; i < parts.Count; i++)
            Add(new Chunk
            {
                Id = id + "/" + (i + 1), Kind = kind, Title = title + " (part " + (i + 1) + "/" + parts.Count + ")",
                Keywords = keywords, Text = parts[i],
            });
    }

    private void Add(Chunk chunk)
    {
        string id = chunk.Id;
        for (int n = 2; ById.ContainsKey(chunk.Id); n++) chunk.Id = id + "~" + n;
        Chunks.Add(chunk);
        ById[chunk.Id] = chunk;
    }

    /// Splits at blank lines into pieces of at most `max` chars (a single
    /// paragraph longer than that is cut at a line break).
    public static List<string> SplitParagraphs(string text, int max)
    {
        List<string> parts = new List<string>();
        StringBuilder cur = new StringBuilder();
        foreach (string para in text.Split(new[] { "\n\n" }, StringSplitOptions.None))
        {
            if (cur.Length > 0 && cur.Length + 2 + para.Length > max)
            {
                parts.Add(cur.ToString());
                cur.Clear();
            }
            if (para.Length > max)
            {
                foreach (string line in para.Split('\n'))
                {
                    if (cur.Length > 0 && cur.Length + 1 + line.Length > max) { parts.Add(cur.ToString()); cur.Clear(); }
                    if (cur.Length > 0) cur.Append('\n');
                    cur.Append(line);
                }
                continue;
            }
            if (cur.Length > 0) cur.Append("\n\n");
            cur.Append(para);
        }
        if (cur.Length > 0) parts.Add(cur.ToString());
        return parts;
    }

    /// The card index for the prompt: one line per card.
    public string CardIndex()
    {
        StringBuilder b = new StringBuilder();
        foreach (Card card in Cards.Values.OrderBy(x => x.Id, StringComparer.Ordinal))
            b.Append(card.IndexLine()).Append('\n');
        return b.ToString();
    }

    private static string Read(string path, IncrementalHash hash)
    {
        byte[] bytes = File.ReadAllBytes(path);
        hash.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(path)));
        hash.AppendData(bytes);
        return Encoding.UTF8.GetString(bytes).TrimStart('﻿');
    }

    private static string[] Sorted(string[] paths)
    {
        Array.Sort(paths, StringComparer.Ordinal);
        return paths;
    }
}
