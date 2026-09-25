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

        private static void Flush(List<string> parts, StringBuilder cur, string fence)
        {
            if (fence != null) cur.Append("\n```");
            parts.Add(cur.ToString());
            cur.Length = 0;
        }
    }
}
