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

    /// The card as the model reads it: title, metadata, body.
    public string Render()
    {
        StringBuilder b = new StringBuilder();
        b.Append("card: ").Append(Id).Append('\n');
        if (Aliases.Count > 0) b.Append("aliases: ").Append(string.Join(", ", Aliases)).Append('\n');
        if (!string.IsNullOrEmpty(Confidence)) b.Append("confidence: ").Append(Confidence).Append('\n');
        if (!string.IsNullOrEmpty(Checked)) b.Append("checked: ").Append(Checked).Append('\n');
        if (!string.IsNullOrEmpty(Sources)) b.Append("sources: ").Append(Sources).Append('\n');
        if (Related.Count > 0) b.Append("related cards: ").Append(string.Join(", ", Related)).Append('\n');
        if (Code.Count > 0) b.Append("code to open: ").Append(string.Join(", ", Code)).Append('\n');
        b.Append('\n').Append(Body);
        return b.ToString();
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
