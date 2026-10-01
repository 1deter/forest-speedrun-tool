using System;
using System.Collections.Generic;
using System.Text;

namespace ForestOverlay.BridgeMcp
{
    // ------------------------------------------------------------------
    // The text side of the QA Discord bot (Discord.cs): splitting a long
    // post into messages Discord accepts. Pure, so it is tested.
    // ------------------------------------------------------------------
    public static class DiscordText
    {
        /// Discord's limit per message.
        public const int MaxChars = 2000;

        /// Splits a list into messages at blank lines (its sections), as
        /// few messages as fit; a section longer than a message falls back
        /// to Split. The to-do list, when it outgrows one message (author,
        /// 2026-09-26: "send it as two messages").
        public static List<string> SplitAtSections(string text, int max = MaxChars)
        {
            List<string> parts = new List<string>();
            if (string.IsNullOrEmpty(text)) return parts;
            text = text.Replace("\r\n", "\n");
            if (text.Length <= max) { parts.Add(text); return parts; }

            string[] sections = text.Split(new[] { "\n\n" }, StringSplitOptions.None);
            StringBuilder cur = new StringBuilder();
            for (int i = 0; i < sections.Length; i++)
            {
                string s = sections[i];
                if (s.Trim().Length == 0) continue;
                if (cur.Length > 0 && cur.Length + 2 + s.Length > max)
                {
                    parts.Add(cur.ToString());
                    cur.Length = 0;
                }
                if (s.Length > max) { parts.AddRange(Split(s, max)); continue; }
                if (cur.Length > 0) cur.Append("\n\n");
                cur.Append(s);
            }
            if (cur.Length > 0) parts.Add(cur.ToString());
            return parts;
        }

        /// Splits text into messages of at most `max` chars, at line breaks
        /// where it can. A ``` code block cut in two is closed at the end of
        /// one message and reopened (same language tag) at the start of the
        /// next, so a QA list stays one copyable block per message.
        public static List<string> Split(string text, int max = MaxChars)
        {
            List<string> parts = new List<string>();
            if (string.IsNullOrEmpty(text)) return parts;
            text = text.Replace("\r\n", "\n");
            if (text.Length <= max) { parts.Add(text); return parts; }

            string[] lines = text.Split('\n');
            StringBuilder cur = new StringBuilder();
            string fence = null;   // the open fence line ("```" or "```txt"), null outside a block

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                // Room kept for a closing fence when inside a block.
                int reserve = fence != null ? 4 : 0;
                string sep = cur.Length > 0 ? "\n" : "";

                if (cur.Length + sep.Length + line.Length + reserve > max && cur.Length > 0)
                {
                    Flush(parts, cur, fence);
                    if (fence != null) cur.Append(fence);
                    sep = cur.Length > 0 ? "\n" : "";
                }

                // A single line longer than a message: hard cut.
                while (cur.Length + sep.Length + line.Length + (fence != null ? 4 : 0) > max)
                {
                    int room = max - cur.Length - sep.Length - (fence != null ? 4 : 0);
                    if (room <= 0) { Flush(parts, cur, fence); if (fence != null) cur.Append(fence); sep = cur.Length > 0 ? "\n" : ""; continue; }
                    cur.Append(sep).Append(line, 0, room);
                    line = line.Substring(room);
                    Flush(parts, cur, fence);
                    if (fence != null) cur.Append(fence);
                    sep = cur.Length > 0 ? "\n" : "";
                }

                cur.Append(sep).Append(line);
                if (line.TrimStart().StartsWith("```"))
                    fence = fence == null ? line.Trim() : null;
            }
            if (cur.Length > 0) parts.Add(cur.ToString());
            return parts;
        }

        /// Turns "@name" into a real mention ("<@id>") for every name in
        /// `ids` (username or display name, any case), outside ``` blocks
        /// and `inline code` - a plain "@name" never pings (author,
        /// 2026-10-01: "ping the members you are mentioning"). Names it
        /// does not know are listed in `unknown`, the text left as it was.
        public static string LinkMentions(string text, IDictionary<string, string> ids, List<string> unknown)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            Dictionary<string, string> byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> kv in ids) byName[kv.Key] = kv.Value;
            StringBuilder sb = new StringBuilder(text.Length);
            bool fence = false, code = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '`')
                {
                    if (string.CompareOrdinal(text, i, "```", 0, 3) == 0) { fence = !fence; sb.Append("```"); i += 2; continue; }
                    if (!fence) code = !code;
                    sb.Append(c);
                    continue;
                }
                bool start = i == 0 || !(char.IsLetterOrDigit(text[i - 1]) || text[i - 1] == '_' || text[i - 1] == '<');
                if (c != '@' || fence || code || !start) { sb.Append(c); continue; }
                int j = i + 1;
                while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '_' || text[j] == '.')) j++;
                // A sentence's full stop is not part of the name.
                while (j > i + 1 && text[j - 1] == '.') j--;
                string name = text.Substring(i + 1, j - i - 1);
                string id;
                if (name.Length > 0 && byName.TryGetValue(name, out id)) { sb.Append("<@").Append(id).Append('>'); i = j - 1; continue; }
                if (name.Length > 0 && unknown != null && !name.Equals("everyone", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("here", StringComparison.OrdinalIgnoreCase) && !unknown.Contains(name)) unknown.Add(name);
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static void Flush(List<string> parts, StringBuilder cur, string fence)
        {
            if (fence != null) cur.Append("\n```");
            parts.Add(cur.ToString());
            cur.Length = 0;
        }
    }
}
