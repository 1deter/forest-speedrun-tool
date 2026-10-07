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
    public void Card_lists_unconfirmed_claims_on_top()
    {
        string text = "---\nid: clip\ntitle: Clip\nconfidence: code\n---\n\n# Clip\n\nA clip.\n\n## Why it works\n\n" +
            "The capsule stays small [live]. Depenetration pushes it out [code]. Runners need uncapped fps [runner].\n" +
            "- the head sphere moves 1.63 m [live]\n- it is all one window [inferred]\n\n" +
            "## How the pieces combine [inferred]\n\nThey combine.\n\n```csharp\nx = 1; // not reproduced\n```\n\n" +
            "## Open questions\n\n- Why fps matters (a guess).\n";
        Card c = Card.Parse(text, "clip");
        Assert.Equal(new[]
        {
            "Runners need uncapped fps [runner].",
            "it is all one window [inferred]",
            "The section 'How the pieces combine [inferred]' as a whole.",
            "Open question (not known): Why fps matters (a guess).",
        }, c.Unconfirmed());
        string r = c.Render();
        Assert.True(r.IndexOf("NOT CONFIRMED", StringComparison.Ordinal) < r.IndexOf("# Clip", StringComparison.Ordinal));

        Card live = Card.Parse(Sample, "x");
        Assert.Empty(live.Unconfirmed());
        Assert.DoesNotContain("NOT CONFIRMED", live.Render());
        Assert.DoesNotContain("DEV NOTES", live.Render());
    }

    [Fact]
    public void Card_lists_dev_notes_on_top()
    {
        string text = "---\nid: clip\ntitle: Clip\nconfidence: code\n---\n\n# Clip\n\nA clip.\n\n## Why\n\n" +
            "It works [code]. A script pressed crouch at 0.25 s [dev]. Done.\n\n" +
            "## What our tests tried [dev]\n\nScripted inputs.\n";
        Card c = Card.Parse(text, "clip");
        Assert.Equal(new[]
        {
            "A script pressed crouch at 0.25 s [dev].",
            "The section 'What our tests tried [dev]' as a whole.",
        }, c.DevNotes());
        Assert.Empty(c.Unconfirmed());
        string r = c.Render();
        Assert.True(r.IndexOf("DEV NOTES", StringComparison.Ordinal) < r.IndexOf("# Clip", StringComparison.Ordinal));
    }

    private static readonly string[] KnownTags = { "live", "code", "runner", "inferred", "dev", "arithmetic" };

    [Fact]
    public void Card_tags_are_known()
    {
        // [word ...] not followed by "(" (a link): the first word is a confidence tag
        var tag = new System.Text.RegularExpressions.Regex(@"\[([a-z]+)[^\]\n]*\](?!\()");
        foreach (string file in Directory.GetFiles(Path.Combine(RepoRoot(), "knowledge", "cards"), "*.md"))
            foreach (System.Text.RegularExpressions.Match m in tag.Matches(File.ReadAllText(file)))
                Assert.True(KnownTags.Contains(m.Groups[1].Value), Path.GetFileName(file) + ": unknown tag " + m.Value);
    }

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
        // read_card's answer is cut at the tool output cap: a longer card
        // would lose its last sections (records, open questions) without
        // the model knowing - split a card before it gets there.
        foreach (Card card in c.Cards.Values)
            Assert.True(card.Render().Length <= ForestBot.Agent.Tools.MaxOutput,
                card.Id + " renders " + card.Render().Length + " chars, over read_card's " + ForestBot.Agent.Tools.MaxOutput + " - split it");
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

    [Fact]
    public void Eval_parser_reads_length_limits_and_a_too_long_answer_fails()
    {
        string md = "### a\nquestion: q1?\nmax-length: 600\nmust:\n- f\nthen: more\nmin-length: 1200\nmust:\n- g\n";
        EvalQuestion q = EvalQuestions.Parse(md).Single();
        Assert.Equal(600, q.MaxLength);
        Assert.Equal(0, q.MinLength);
        Assert.Equal(1200, q.ThenMinLength);
        Assert.Equal(0, q.ThenMaxLength);
        Assert.Equal(new[] { "f" }, q.Must);
        Assert.Equal(new[] { "g" }, q.ThenMust);
        Assert.True(EvalQuestion.CheckLength(new string('x', 600), 600, 0).ok);
        (bool ok, string line) = EvalQuestion.CheckLength(new string('x', 601), 600, 0);
        Assert.False(ok);
        Assert.Contains("TOO LONG", line);
        Assert.False(EvalQuestion.CheckLength("short", 0, 1200).ok);
        Assert.Null(EvalQuestion.CheckLength("anything", 0, 0).line);
    }

    [Fact]
    public void Eval_select_keeps_file_order_and_refuses_unknown_ids()
    {
        List<EvalQuestion> qs = EvalQuestions.Parse("### a\nquestion: q?\nmust:\n- f\n### b\nquestion: q?\nmust:\n- f\n### c\nquestion: q?\nmust:\n- f\n");
        Assert.Equal(3, EvalQuestions.Select(qs, new HashSet<string>()).Count);
        Assert.Equal(new[] { "a", "c" }, EvalQuestions.Select(qs, new HashSet<string> { "c", "a" }).Select(q => q.Id));
        ArgumentException ex = Assert.Throws<ArgumentException>(() => EvalQuestions.Select(qs, new HashSet<string> { "a", "gone" }));
        Assert.Contains("gone", ex.Message);
    }

    [Fact]
    public void Eval_score_counts_only_answered_questions()
    {
        EvalScore s = new EvalScore { Passed = 7, Total = 9, Answered = 3, Busy = 2 };
        Assert.Equal("Score: 7/9 = 77.8%, 3 answered, 2 busy skipped", s.Line());
        Assert.Equal(0, new EvalScore { Busy = 5 }.Percent);
    }

    /// CI's eval subset (bot.yml EVAL_SUBSET) names real questions - a
    /// rename would otherwise fail only after a deploy.
    [Fact]
    public void Ci_eval_subset_names_real_questions()
    {
        string root = RepoRoot();
        string line = File.ReadAllLines(Path.Combine(root, ".github", "workflows", "bot.yml")).Single(l => l.Trim().StartsWith("EVAL_SUBSET:"));
        string[] ids = line.Substring(line.IndexOf(':') + 1).Trim().Trim('\'', '"').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.InRange(ids.Length, 3, 8);
        List<EvalQuestion> qs = EvalQuestions.Parse(File.ReadAllText(Path.Combine(root, "knowledge", "eval", "questions.md")));
        Assert.Equal(ids.Length, EvalQuestions.Select(qs, ids.ToHashSet()).Count);
    }
}
