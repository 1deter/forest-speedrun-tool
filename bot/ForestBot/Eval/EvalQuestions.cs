namespace ForestBot.Eval;

public sealed class EvalQuestion
{
    public string Id;
    public string Question;
    public List<string> Cards = new List<string>();
    public List<string> Must = new List<string>();
    public List<string> Not = new List<string>();
    /// A follow-up asked as a reply to the first answer, with its own facts.
    public string Then;
    public List<string> ThenMust = new List<string>();
    /// Answer length limits in characters (0 = none): "max-length:" / "min-length:"
    /// before "then:" bound the first answer, after it the follow-up's.
    public int MaxLength, MinLength, ThenMaxLength, ThenMinLength;

    /// One check line for the length limits; null when none is set.
    public static (bool ok, string line) CheckLength(string text, int max, int min)
    {
        int len = (text ?? "").Trim().Length;
        if (max > 0) return (len <= max, (len <= max ? "- [x] " : "- [ ] TOO LONG: ") + "answer at most " + max + " characters (was " + len + ")");
        if (min > 0) return (len >= min, (len >= min ? "- [x] " : "- [ ] TOO SHORT: ") + "answer at least " + min + " characters (was " + len + ")");
        return (true, null);
    }
}

// ------------------------------------------------------------------
// knowledge/eval/questions.md: "### <id>", then "question:", "cards:",
// "must:" / "not:" bullets, optional "max-length:" / "min-length:" (characters), optional
// "then:" + its own "must:" (and limits). Pure,
// tested.
// ------------------------------------------------------------------
public static class EvalQuestions
{
    public static List<EvalQuestion> Parse(string markdown)
    {
        List<EvalQuestion> list = new List<EvalQuestion>();
        EvalQuestion cur = null;
        List<string> bullets = null;
        foreach (string raw in (markdown ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.StartsWith("### "))
            {
                cur = new EvalQuestion { Id = line.Substring(4).Trim() };
                list.Add(cur);
                bullets = null;
                continue;
            }
            if (cur == null) continue;
            if (line.StartsWith("- ") && bullets != null) { bullets.Add(line.Substring(2).Trim()); continue; }
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            string key = line.Substring(0, colon).Trim().ToLowerInvariant(), value = line.Substring(colon + 1).Trim();
            switch (key)
            {
                case "question": cur.Question = value; bullets = null; break;
                case "cards":
                    cur.Cards = value.StartsWith("(") ? new List<string>() : Knowledge.Card.SplitList(value);
                    bullets = null;
                    break;
                case "must": bullets = cur.Then != null ? cur.ThenMust : cur.Must; break;
                case "not": bullets = cur.Not; break;
                case "max-length": case "min-length":
                {
                    int.TryParse(value, out int n);
                    bool then = cur.Then != null;
                    if (key == "max-length") { if (then) cur.ThenMaxLength = n; else cur.MaxLength = n; }
                    else { if (then) cur.ThenMinLength = n; else cur.MinLength = n; }
                    bullets = null;
                    break;
                }
                case "then": cur.Then = value; bullets = null; break;
            }
        }
        list.RemoveAll(q => string.IsNullOrEmpty(q.Question));
        return list;
    }

    /// The questions named in `only` (all when empty), in file order. An id
    /// that is not in the file throws: a renamed question must not quietly
    /// shrink CI's subset.
    public static List<EvalQuestion> Select(List<EvalQuestion> all, ICollection<string> only)
    {
        if (only == null || only.Count == 0) return all;
        List<string> unknown = only.Where(id => !all.Any(q => q.Id == id)).ToList();
        if (unknown.Count > 0) throw new ArgumentException("no such eval question: " + string.Join(", ", unknown));
        return all.Where(q => only.Contains(q.Id)).ToList();
    }
}
