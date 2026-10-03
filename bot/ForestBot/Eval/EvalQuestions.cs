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
}

// ------------------------------------------------------------------
// knowledge/eval/questions.md: "### <id>", then "question:", "cards:",
// "must:" / "not:" bullets, optional "then:" + its own "must:". Pure,
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
                case "then": cur.Then = value; bullets = null; break;
            }
        }
        list.RemoveAll(q => string.IsNullOrEmpty(q.Question));
        return list;
    }
}
