using System.Text;

namespace ForestBot.Knowledge;

// ------------------------------------------------------------------
// Splits markdown at headings, ignoring "#" lines inside ``` code
// blocks. Pure, tested.
// ------------------------------------------------------------------
public static class MarkdownSections
{
    public sealed class Section
    {
        /// 0 = text before the first heading; else the number of hashes.
        public int Level;
        public string Heading = "";
        /// The headings above this one, outermost first ("Speedrun tech").
        public List<string> Parents = new List<string>();
        public string Text = "";
    }

    /// Splits at headings of level `maxLevel` or less (1..maxLevel); deeper
    /// headings stay inside their section's text.
    public static List<Section> Split(string markdown, int maxLevel)
    {
        List<Section> list = new List<Section>();
        string[] lines = (markdown ?? "").Replace("\r\n", "\n").Split('\n');
        string[] stack = new string[7];
        Section cur = new Section();
        StringBuilder text = new StringBuilder();
        bool inCode = false;

        foreach (string line in lines)
        {
            if (line.TrimStart().StartsWith("```")) inCode = !inCode;
            int level = inCode ? 0 : HeadingLevel(line);
            if (level > 0 && level <= maxLevel)
            {
                cur.Text = text.ToString();
                if (cur.Level > 0 || cur.Text.Trim().Length > 0) list.Add(cur);
                text.Clear();
                string heading = line.Substring(level).Trim();
                stack[level] = heading;
                for (int i = level + 1; i < stack.Length; i++) stack[i] = null;
                cur = new Section { Level = level, Heading = heading };
                for (int i = 1; i < level; i++)
                    if (stack[i] != null) cur.Parents.Add(stack[i]);
                continue;
            }
            text.Append(line).Append('\n');
        }
        cur.Text = text.ToString();
        if (cur.Level > 0 || cur.Text.Trim().Length > 0) list.Add(cur);
        return list;
    }

    /// "## Foo" -> 2; not a heading -> 0.
    public static int HeadingLevel(string line)
    {
        int n = 0;
        while (n < line.Length && line[n] == '#') n++;
        if (n == 0 || n > 6 || n >= line.Length || line[n] != ' ') return 0;
        return n;
    }

    /// A heading as a short stable slug ("Bomb boost, refined" ->
    /// "bomb-boost-refined").
    public static string Slug(string heading)
    {
        StringBuilder b = new StringBuilder();
        bool dash = false;
        foreach (char ch in heading.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) { b.Append(ch); dash = false; }
            else if (!dash && b.Length > 0) { b.Append('-'); dash = true; }
            if (b.Length >= 60) break;
        }
        return b.ToString().TrimEnd('-');
    }
}
