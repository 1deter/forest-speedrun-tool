using System.Text;

namespace ForestBot.Knowledge;

// ------------------------------------------------------------------
// One knowledge card (knowledge/cards/<id>.md, format in
// knowledge/README.md): front matter between "---" lines, then markdown
// with "# Title" and "## Section" headings. Pure, tested.
// ------------------------------------------------------------------
public sealed class Card
{
    public string Id;
    public string Title;
    public List<string> Aliases = new List<string>();
    public List<string> Tags = new List<string>();
    public string Confidence;
    public string Checked;
    public string Sources;
    public List<string> Related = new List<string>();
    public List<string> Code = new List<string>();

    /// The markdown after the front matter, as written.
    public string Body = "";

    /// The paragraph under the title - the card's summary.
    public string Summary = "";

    /// "## " sections in order (heading without the hashes, text under it).
    public List<(string heading, string text)> Sections = new List<(string, string)>();

    /// Parses a card file. `fallbackId` (the file name) is used when the
    /// front matter has no id. Never throws on bad input: missing pieces
    /// stay empty, `Problems` says what was wrong.
    public static Card Parse(string text, string fallbackId, List<string> problems = null)
    {
        Card c = new Card { Id = fallbackId };
        text = (text ?? "").Replace("\r\n", "\n");
        if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);

        string body = text;
        if (text.StartsWith("---\n"))
        {
            int end = text.IndexOf("\n---", 4, StringComparison.Ordinal);
            if (end < 0) problems?.Add(fallbackId + ": front matter is not closed");
            else
            {
                foreach (string raw in text.Substring(4, end - 4).Split('\n'))
                {
                    int colon = raw.IndexOf(':');
                    if (colon <= 0) continue;
                    string key = raw.Substring(0, colon).Trim().ToLowerInvariant();
                    string value = raw.Substring(colon + 1).Trim();
                    switch (key)
                    {
                        case "id": if (value.Length > 0) c.Id = value; break;
                        case "title": c.Title = value; break;
                        case "aliases": c.Aliases = SplitList(value); break;
                        case "tags": c.Tags = SplitList(value); break;
                        case "confidence": c.Confidence = value; break;
                        case "checked": c.Checked = value; break;
                        case "sources": c.Sources = value; break;
                        case "related": c.Related = SplitList(value); break;
                        case "code": c.Code = SplitList(value); break;
                    }
                }
                int bodyStart = text.IndexOf('\n', end + 1);
                body = bodyStart < 0 ? "" : text.Substring(bodyStart + 1);
            }
        }
        else problems?.Add(fallbackId + ": no front matter");

        c.Body = body.Trim('\n');
        List<MarkdownSections.Section> parts = MarkdownSections.Split(c.Body, 2);
        foreach (MarkdownSections.Section s in parts)
        {
            if (s.Level == 1 || s.Level == 0)
            {
                if (s.Level == 1 && string.IsNullOrEmpty(c.Title)) c.Title = s.Heading;
                if (c.Summary.Length == 0) c.Summary = FirstParagraph(s.Text);
            }
            else c.Sections.Add((s.Heading, s.Text.Trim('\n')));
        }
        if (string.IsNullOrEmpty(c.Title)) { c.Title = c.Id; problems?.Add(c.Id + ": no title"); }
        return c;
    }

    /// One line for the prompt's index: id, title, the summary's first
    /// sentence.
    public string IndexLine()
    {
        string s = Summary.Replace('\n', ' ');
        int dot = s.IndexOf(". ", StringComparison.Ordinal);
        if (dot > 0 && dot < 220) s = s.Substring(0, dot + 1);
        else if (s.Length > 220) s = s.Substring(0, 217) + "...";
        return "- " + Id + " - " + Title + ": " + s;
    }

    /// The card as the model reads it: title, metadata, the claims that
    /// are not confirmed (on top - a model skims past an inline "[inferred]";
    /// the 2026-10-03 eval called a never-reproduced clip "based on live
    /// tests"), body.
    public string Render()
    {
        StringBuilder b = new StringBuilder();
        b.Append("card: ").Append(Id).Append('\n');
        List<string> unconfirmed = Unconfirmed();
        if (unconfirmed.Count > 0)
        {
            b.Append("NOT CONFIRMED - if your answer uses any of these, say so in it (\"runners report\", \"not tested in game\", \"a guess\"):\n");
            foreach (string u in unconfirmed) b.Append("- ").Append(u).Append('\n');
        }
        List<string> dev = DevNotes();
        if (dev.Count > 0)
        {
            b.Append("DEV NOTES - our own test notes, background only: never tell the runner how we tested (scripts, the bridge, automated tests) and never give these as advice; at most say it was not reproduced in our tests:\n");
            foreach (string d in dev) b.Append("- ").Append(d).Append('\n');
        }
        if (Aliases.Count > 0) b.Append("aliases: ").Append(string.Join(", ", Aliases)).Append('\n');
        if (!string.IsNullOrEmpty(Confidence)) b.Append("confidence: ").Append(Confidence).Append('\n');
        if (!string.IsNullOrEmpty(Checked)) b.Append("checked: ").Append(Checked).Append('\n');
        if (!string.IsNullOrEmpty(Sources)) b.Append("sources: ").Append(Sources).Append('\n');
        if (Related.Count > 0) b.Append("related cards: ").Append(string.Join(", ", Related)).Append('\n');
        if (Code.Count > 0) b.Append("code to open: ").Append(string.Join(", ", Code)).Append('\n');
        b.Append('\n').Append(Body);
        return b.ToString();
    }

    private static readonly string[] UnconfirmedMarks =
    {
        "[runner", "[inferred", "not reproduced", "a guess", "not confirmed", "not tested", "untested",
    };

    /// The card's claims that are not confirmed: the card-wide confidence
    /// when it is runner / inferred, every sentence or bullet tagged
    /// [runner] / [inferred] or saying "not reproduced" / "a guess" (a
    /// heading tagged so covers its section), and the "Open questions".
    public List<string> Unconfirmed()
    {
        List<string> list = new List<string>();
        if (Confidence == "runner" || Confidence == "inferred")
            list.Add("The whole card's core claims are " + (Confidence == "runner" ? "runners' reports" : "inferred") + ", not reproduced in game.");
        foreach ((string heading, string text) in Sections)
        {
            if (heading.Equals("Open questions", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string unit in Units(text)) Add(list, "Open question (not known): " + unit);
                continue;
            }
            if (HasMark(heading)) { Add(list, "The section '" + heading + "' as a whole."); continue; }
            foreach (string unit in Units(text))
                if (HasMark(unit)) Add(list, unit);
        }
        return list;
    }

    /// The card's dev-only test notes: every sentence or bullet tagged
    /// [dev] (a heading tagged so covers its section) - how our own tests
    /// were set up, which a runner answer must not pass on (T-0163).
    public List<string> DevNotes()
    {
        List<string> list = new List<string>();
        foreach ((string heading, string text) in Sections)
        {
            if (heading.Contains(DevMark)) { Add(list, "The section '" + heading + "' as a whole."); continue; }
            foreach (string unit in Units(text))
                if (unit.Contains(DevMark)) Add(list, unit);
        }
        return list;
    }

    public const string DevMark = "[dev]";

    private static bool HasMark(string s)
    {
        s = s.Replace("[inferred: calculated]", "");   // arithmetic on known numbers
        foreach (string m in UnconfirmedMarks)
            if (s.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private static void Add(List<string> list, string s)
    {
        if (list.Count >= 15) return;
        if (s.Length > 260) s = s.Substring(0, 257) + "...";
        if (!list.Contains(s)) list.Add(s);
    }

    /// Bullets and sentences of a section's text, code blocks skipped. A
    /// sentence ends at ". " or at a "[tag] " closing it.
    private static IEnumerable<string> Units(string text)
    {
        List<string> paras = new List<string>();
        StringBuilder para = new StringBuilder();
        List<string> tableRows = new List<string>();
        bool code = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("```")) { code = !code; continue; }
            if (code) continue;
            if (line.StartsWith("|"))
            {
                if (para.Length > 0) paras.Add(para.ToString());
                para.Clear();
                if (line.Trim('|', '-', ' ', ':').Length > 0) tableRows.Add(line);
                continue;
            }
            bool bullet = line.StartsWith("- ") || line.StartsWith("* ");
            int num = line.IndexOf(". ", StringComparison.Ordinal);
            bool numbered = num > 0 && num < 4 && char.IsDigit(line[0]);
            if (line.Length == 0 || bullet || numbered)
            {
                if (para.Length > 0) paras.Add(para.ToString());
                para.Clear();
                if (line.Length == 0) continue;
                line = bullet ? line.Substring(2) : line.Substring(num + 2);
            }
            para.Append(para.Length > 0 ? " " : "").Append(line);
        }
        if (para.Length > 0) paras.Add(para.ToString());
        foreach (string row in tableRows) yield return row;   // a row is one claim
        foreach (string p in paras)
        {
            int start = 0;
            for (int i = 0; i < p.Length; i++)
            {
                bool atEnd = i + 1 == p.Length || p[i + 1] == ' ';
                bool end = (p[i] == '.' || p[i] == '?' || p[i] == '!') && atEnd;
                if (!end) continue;
                string s = p.Substring(start, i + 1 - start).Trim();
                if (s.Length > 0) yield return s;
                start = i + 1;
            }
            string rest = p.Substring(start).Trim();
            if (rest.Length > 0) yield return rest;
        }
    }

    internal static List<string> SplitList(string value)
    {
        List<string> list = new List<string>();
        foreach (string p in value.Split(','))
        {
            string t = p.Trim();
            if (t.Length > 0) list.Add(t);
        }
        return list;
    }

    private static string FirstParagraph(string text)
    {
        string t = text.Trim('\n');
        int blank = t.IndexOf("\n\n", StringComparison.Ordinal);
        if (blank >= 0) t = t.Substring(0, blank);
        return t.Replace('\n', ' ').Trim();
    }
}
