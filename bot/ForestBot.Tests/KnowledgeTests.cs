using ForestBot.Eval;
using ForestBot.Knowledge;
using Xunit;

namespace ForestBot.Tests;

public class KnowledgeTests
{
    /// The repo root (the folder holding knowledge/), found from the test's bin.
    internal static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "knowledge", "cards"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir;
    }

    private const string Sample = "---\nid: bomb-boost\ntitle: Bomb boost\naliases: bb, pause boost , \ntags: tech\n" +
        "confidence: live\nrelated: knockback-sources, player-physics\ncode: A.B, C.D\n---\n\n# Bomb boost\n\n" +
        "Stand near an explosion. It flies you far.\n\nSecond paragraph.\n\n## How\n\nPause.\n\n```csharp\n## not a heading\n```\n\n## Numbers\n\n8 m/s.\n";

    [Fact]
    public void Card_front_matter_and_sections()
    {
        List<string> problems = new List<string>();
        Card c = Card.Parse(Sample, "file-name", problems);
        Assert.Empty(problems);
        Assert.Equal("bomb-boost", c.Id);
        Assert.Equal("Bomb boost", c.Title);
        Assert.Equal(new[] { "bb", "pause boost" }, c.Aliases);
        Assert.Equal(new[] { "knockback-sources", "player-physics" }, c.Related);
        Assert.Equal(new[] { "A.B", "C.D" }, c.Code);
        Assert.Equal("Stand near an explosion. It flies you far.", c.Summary);
        Assert.Equal(2, c.Sections.Count);
        Assert.Equal("How", c.Sections[0].heading);
        Assert.Contains("## not a heading", c.Sections[0].text);   // inside a code block
        Assert.Equal("- bomb-boost - Bomb boost: Stand near an explosion.", c.IndexLine());
    }

    [Fact]
    public void Card_without_front_matter_is_reported_not_thrown()
    {
        List<string> problems = new List<string>();
        Card c = Card.Parse("# Only a title\n\ntext", "x", problems);
        Assert.Equal("x", c.Id);
        Assert.Equal("Only a title", c.Title);
        Assert.Contains(problems, p => p.Contains("no front matter"));
    }

    [Fact]
    public void Markdown_sections_keep_parents_and_ignore_code()
    {
        string md = "intro\n# A\na\n## B\nb\n```\n# fake\n```\n### C\nc\n## D\nd";
        List<MarkdownSections.Section> s = MarkdownSections.Split(md, 3);
        Assert.Equal(new[] { 0, 1, 2, 3, 2 }, s.Select(x => x.Level));
        Assert.Contains("# fake", s[2].Text);
        Assert.Equal(new[] { "A", "B" }, s[3].Parents);
        Assert.Equal(new[] { "A" }, s[4].Parents);
        Assert.Equal("bomb-boost-refined", MarkdownSections.Slug("**Bomb boost, refined** (live)").Replace("-live", ""));
    }

    [Fact]
    public void Fsm_states_and_overview()
    {
        string text = "fsm damageFSM  on player\nstart state: init\n\n[variables]\n  float a = 0\n\n" +
                      "[state startState]\n  on toHit -> gotHit\n  action SetTag\n\n[state gotHit]\n  on FINISHED -> resetHit\n";
        FsmText f = FsmText.Parse("player-damageFSM", text);
        Assert.Equal(2, f.States.Count);
        Assert.Contains("[variables]", f.Header);
        Assert.Contains("startState: toHit -> gotHit", f.Overview());
        Assert.Contains("action SetTag", f.State("STARTSTATE"));
        Assert.Null(f.State("nope"));
    }

    [Fact]
    public void Long_sections_split_into_parts()
    {
        string para = new string('x', 2500);
        List<string> parts = Corpus.SplitParagraphs(para + "\n\n" + para + "\n\n" + para, 6000);
        Assert.Equal(2, parts.Count);
        Assert.All(parts, p => Assert.True(p.Length <= 6000));
    }

    // ---- lint over the real knowledge base ----

    [Fact]
    public void The_knowledge_base_loads_without_problems()
    {
        Corpus c = Corpus.Load(RepoRoot());
        Assert.Empty(c.Problems);
        Assert.True(c.Cards.Count >= 20);
        foreach (Card card in c.Cards.Values)
        {
            Assert.False(string.IsNullOrWhiteSpace(card.Summary), card.Id + " has no summary");
            Assert.True(card.Aliases.Count >= 3, card.Id + " needs aliases (search lives on them)");
            Assert.Contains(card.Confidence, new[] { "live", "code", "runner", "inferred" });
            Assert.Equal(card.Id, card.Id.ToLowerInvariant());
        }
        Assert.All(c.Chunks, ch => Assert.True(ch.Text.Length <= Corpus.MaxChunk + 10, ch.Id));
    }

    [Fact]
    public void Glossary_and_eval_name_real_cards()
    {
        string root = RepoRoot();
        Corpus c = Corpus.Load(root);
        foreach (string line in c.Glossary.Split('\n'))
        {
            string[] cells = line.Split('|');
            if (cells.Length < 5 || line.Contains("---") || cells[3].Trim() == "Card") continue;
            string card = cells[3].Trim();
            Assert.True(c.Cards.ContainsKey(card), "glossary names missing card '" + card + "'");
        }
        List<EvalQuestion> qs = EvalQuestions.Parse(File.ReadAllText(Path.Combine(root, "knowledge", "eval", "questions.md")));
        Assert.True(qs.Count >= 30);
        Assert.Equal(qs.Count, qs.Select(q => q.Id).Distinct().Count());
        foreach (EvalQuestion q in qs)
        {
            Assert.NotEmpty(q.Must);
            foreach (string card in q.Cards) Assert.True(c.Cards.ContainsKey(card), q.Id + " names missing card " + card);
            if (q.Then != null) Assert.NotEmpty(q.ThenMust);
        }
    }

    [Fact]
    public void Eval_parser_reads_follow_ups_and_forbidden_items()
    {
        string md = "intro\n### a\nquestion: q1?\ncards: x, y\nmust:\n- f1\n- f2\nnot:\n- bad\nthen: q2?\nmust:\n- g1\n\n### b\nquestion: q3\ncards: (none)\nmust:\n- h\n";
        List<EvalQuestion> qs = EvalQuestions.Parse(md);
        Assert.Equal(2, qs.Count);
        Assert.Equal(new[] { "f1", "f2" }, qs[0].Must);
        Assert.Equal(new[] { "bad" }, qs[0].Not);
        Assert.Equal("q2?", qs[0].Then);
        Assert.Equal(new[] { "g1" }, qs[0].ThenMust);
        Assert.Empty(qs[1].Cards);
    }
}
