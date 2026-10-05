using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Runners' spots on the website, fetchable in game (author, 2026-10-05:
    // "less manual downloading"). Text, like Data/SiteBoard - the plugin has
    // no JSON parser and the site links this file.
    //
    //   GET /api/spots.txt               the list (runners' spots, not community)
    //   GET /api/spots/{id}/foseg        one spot: its current route as a .foseg
    //                                    (the segment; no start state - the site
    //                                    does not keep them - and no attempts)
    //
    //   forest-spots 1
    //   spot|<id>|<runs>|<best s or ->|<by>|<name>
    //
    // The name is last so a '|' in it survives; an id with a '|' is skipped.
    // ------------------------------------------------------------------
    public sealed class SiteSpot
    {
        public string Id = "";
        public string Name = "";
        public string By = "";
        public int Runs;
        public float Best = float.NaN;
    }

    public static class SiteSpots
    {
        public const string Header = "forest-spots 1";
        public const string Category = "Website";
        public const string SegmentFile = "website.txt";

        public static string ListUrl(string baseUrl)
        {
            return SiteProtocol.TrimUrl(baseUrl) + "/api/spots.txt";
        }

        public static string FileUrl(string baseUrl, string segmentId)
        {
            return SiteProtocol.TrimUrl(baseUrl) + "/api/spots/" + Uri.EscapeDataString(segmentId ?? "") + "/foseg";
        }

        public static string Write(IList<SiteSpot> spots)
        {
            StringBuilder sb = new StringBuilder(Header).Append('\n');
            for (int i = 0; i < spots.Count; i++)
            {
                SiteSpot s = spots[i];
                if (string.IsNullOrEmpty(s.Id) || s.Id.IndexOf('|') >= 0) continue;
                sb.Append("spot|").Append(Clean(s.Id)).Append('|').Append(s.Runs.ToString(CultureInfo.InvariantCulture))
                  .Append('|').Append(float.IsNaN(s.Best) || float.IsInfinity(s.Best) ? "-" : s.Best.ToString("0.###", CultureInfo.InvariantCulture))
                  .Append('|').Append(Clean(s.By).Replace('|', ' ')).Append('|').Append(Clean(s.Name)).Append('\n');
            }
            return sb.ToString();
        }

        /// The spots; null when the text is not a list (an error page,
        /// another format version).
        public static List<SiteSpot> Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines[0].Trim() != Header) return null;

            List<SiteSpot> list = new List<SiteSpot>();
            for (int i = 1; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith("spot|")) continue;
                string[] p = lines[i].Split('|');
                if (p.Length < 6 || p[1].Length == 0) continue;
                SiteSpot s = new SiteSpot();
                s.Id = p[1];
                int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out s.Runs);
                float best;
                s.Best = float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out best) ? best : float.NaN;
                s.By = p[4];
                s.Name = string.Join("|", p, 5, p.Length - 5);
                list.Add(s);
            }
            return list;
        }

        /// The website segment file's text: every segment under the Website
        /// category (read-only in the editor, like the community's).
        public static string SegmentFileText(IList<Segment> segments)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("# ForestOverlay website spots - written by the plugin from the spots\n");
            sb.Append("# you added from the website; any edit here is replaced on the next update.\n");
            sb.Append("# Duplicate an entry in the Practice tab to make your own copy.\n");
            for (int i = 0; i < segments.Count; i++)
            {
                Segment s = segments[i];
                string own = s.Category;
                s.Category = Category;
                SegmentFormat.WriteSegment(sb, s, "\n");
                s.Category = own;
            }
            return sb.ToString();
        }

        private static string Clean(string s)
        {
            return (s ?? "").Replace('\n', ' ').Replace('\r', ' ');
        }
    }
}
