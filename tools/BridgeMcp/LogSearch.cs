using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ForestOverlay.BridgeMcp
{
    // ------------------------------------------------------------------
    // Searching a LogOutput.log. Pure (lines in, text out), so it is
    // tested; the file side is in Tools.
    // ------------------------------------------------------------------
    public static class LogSearch
    {
        public const int MaxLineChars = 1500;

        /// A case-insensitive regex; text that is not a valid regex is
        /// searched for literally.
        public static Regex Pattern(string pattern)
        {
            try { return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
            catch (ArgumentException) { return new Regex(Regex.Escape(pattern), RegexOptions.IgnoreCase); }
        }

        /// The last `max` matching lines (newest last), each with `context`
        /// lines around it, numbered from 1; runs that touch are merged and
        /// separated by "--" otherwise.
        public static string Grep(IList<string> lines, string pattern, int context, int max, out int matches)
        {
            Regex re = Pattern(pattern);
            List<int> hits = new List<int>();
            for (int i = 0; i < lines.Count; i++)
                if (re.IsMatch(lines[i])) hits.Add(i);
            matches = hits.Count;

            int first = Math.Max(0, hits.Count - Math.Max(1, max));
            StringBuilder sb = new StringBuilder();
            int printedTo = -1;
            for (int h = first; h < hits.Count; h++)
            {
                int from = Math.Max(0, hits[h] - context);
                int to = Math.Min(lines.Count - 1, hits[h] + context);
                if (from <= printedTo) from = printedTo + 1;
                else if (printedTo >= 0 && context > 0) sb.Append("--\n");
                for (int i = from; i <= to; i++) Append(sb, lines, i, i == hits[h] || re.IsMatch(lines[i]));
                printedTo = Math.Max(printedTo, to);
            }
            return sb.ToString();
        }

        /// The last `n` lines, numbered.
        public static string Tail(IList<string> lines, int n)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = Math.Max(0, lines.Count - n); i < lines.Count; i++) Append(sb, lines, i, false);
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, IList<string> lines, int i, bool hit)
        {
            string l = lines[i];
            if (l.Length > MaxLineChars) l = l.Substring(0, MaxLineChars) + " ...(" + (lines[i].Length - MaxLineChars) + " more chars)";
            sb.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(hit ? ": " : "- ").Append(l).Append('\n');
        }
    }
}
