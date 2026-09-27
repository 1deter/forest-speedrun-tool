using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Each runner's best run on one route, as the website hands it to the
    // plugin (v0.24.155): other runners' PBs as split comparisons in game.
    // Text, not JSON - the plugin has no JSON parser and the site links
    // this file, so both ends share one writer and one reader.
    //
    //   GET /api/spots/{id}/{route}/board.txt
    //
    //   forest-board 1
    //   pb|<run id>|<runner id>|<duration s>|<cum split s>;...;|<name>
    //
    // Splits are cumulative seconds, one per row (checkpoints, then the
    // end); "-" = unknown (a run saved before split times were). The name
    // is last, so a '|' in it survives. The full run (path for the ghost)
    // is GET /api/runs/{run id}/file, an ordinary .run text.
    // ------------------------------------------------------------------
    public sealed class BoardEntry
    {
        public long RunId;
        public string RunnerId = "";
        public string Name = "";
        public float Duration;
        public float[] Splits = new float[0];
    }

    public static class SiteBoard
    {
        public const string Header = "forest-board 1";

        public static string Url(string baseUrl, string segmentId, string route)
        {
            return SiteProtocol.TrimUrl(baseUrl) + "/api/spots/" + Uri.EscapeDataString(segmentId ?? "") +
                   "/" + Uri.EscapeDataString(route ?? "") + "/board.txt";
        }

        public static string RunFileUrl(string baseUrl, long runId)
        {
            return SiteProtocol.TrimUrl(baseUrl) + "/api/runs/" + runId.ToString(CultureInfo.InvariantCulture) + "/file";
        }

        public static string Write(IList<BoardEntry> entries)
        {
            StringBuilder sb = new StringBuilder(Header).Append('\n');
            for (int i = 0; i < entries.Count; i++)
            {
                BoardEntry e = entries[i];
                sb.Append("pb|").Append(e.RunId.ToString(CultureInfo.InvariantCulture))
                  .Append('|').Append(Clean(e.RunnerId))
                  .Append('|').Append(Num(e.Duration)).Append('|');
                for (int s = 0; s < e.Splits.Length; s++)
                {
                    if (s > 0) sb.Append(';');
                    sb.Append(Num(e.Splits[s]));
                }
                sb.Append('|').Append(Clean(e.Name)).Append('\n');
            }
            return sb.ToString();
        }

        /// The entries, in the site's order (fastest first); null when the
        /// text is not a board (an error page, another format version).
        public static List<BoardEntry> Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines[0].Trim() != Header) return null;

            List<BoardEntry> list = new List<BoardEntry>();
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (!line.StartsWith("pb|")) continue;
                string[] p = line.Split('|');
                if (p.Length < 6) continue;

                BoardEntry e = new BoardEntry();
                if (!long.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out e.RunId)) continue;
                e.RunnerId = p[2];
                e.Duration = Read(p[3]);
                if (float.IsNaN(e.Duration)) continue;

                string[] s = p[4].Length > 0 ? p[4].Split(';') : new string[0];
                e.Splits = new float[s.Length];
                for (int k = 0; k < s.Length; k++) e.Splits[k] = Read(s[k]);

                e.Name = string.Join("|", p, 5, p.Length - 5);
                list.Add(e);
            }
            return list;
        }

        /// The entries a runner compares against: the same number of rows,
        /// never their own (their own runs are their PB, from disk).
        public static List<BoardEntry> Others(List<BoardEntry> board, string ownRunnerId, int rows)
        {
            List<BoardEntry> list = new List<BoardEntry>();
            if (board == null) return list;
            for (int i = 0; i < board.Count; i++)
            {
                BoardEntry e = board[i];
                if (e.RunnerId == ownRunnerId) continue;
                if (e.Splits.Length != rows) continue;
                list.Add(e);
            }
            return list;
        }

        private static string Num(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return "-";
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static float Read(string s)
        {
            float v;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : float.NaN;
        }

        private static string Clean(string s)
        {
            return (s ?? "").Replace('\n', ' ').Replace('\r', ' ');
        }
    }
}
