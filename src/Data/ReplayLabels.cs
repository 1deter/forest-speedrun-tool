using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The replay's marker labels without overlap (bridge, v0.24.242:
    // several interactions at one spot - crafting, eating, a pause -
    // stacked their labels into unreadable text).
    //
    //   - Group: the picked markers (nearest first) standing at one spot
    //     (within SameSpot m of a group's first member) become one label;
    //   - Text: that label's lines - each distinct label once, "x2" for
    //     repeats, at most MaxLines and "+n more";
    //   - Stack: on screen, a label box that overlaps one placed before it
    //     (nearer the viewer) moves up above it.
    //
    // Group / Text run on the label pick throttle (0.2 s); Stack runs per
    // repaint and does not allocate. Tested in ReplayLabelsTests.
    // ------------------------------------------------------------------
    public static class ReplayLabels
    {
        /// Markers this close (m) share one label.
        public const float SameSpot = 1.5f;
        /// Lines in one label; the rest are counted ("+3 more").
        public const int MaxLines = 4;
        /// Screen sizes for the stacking (13 px text).
        public const float LineHeight = 16f;
        public const float CharWidth = 7f;
        public const float Padding = 8f;
        public const float Gap = 2f;

        /// groupOf[k] = the group of picked marker k (idx[k] into events);
        /// first[g] = the k that opened group g (the nearest of it). Returns
        /// how many groups.
        public static int Group(IList<RunEvent> events, int[] idx, int n, float sameSpot, int[] groupOf, int[] first)
        {
            if (events == null || idx == null || groupOf == null || first == null) return 0;
            n = Mathf.Min(n, Mathf.Min(idx.Length, groupOf.Length));
            float r2 = sameSpot * sameSpot;
            int groups = 0;
            for (int k = 0; k < n; k++)
            {
                Vector3 p = events[idx[k]].P;
                int g = -1;
                for (int j = 0; j < groups; j++)
                {
                    if ((events[idx[first[j]]].P - p).sqrMagnitude <= r2) { g = j; break; }
                }
                if (g < 0 && groups < first.Length)
                {
                    g = groups++;
                    first[g] = k;
                }
                groupOf[k] = g < 0 ? groups - 1 : g;
            }
            return groups;
        }

        /// Group g's text: its members' labels (labels[event index]) in
        /// time order, each distinct one once with "x2" for repeats, one per
        /// line, at most maxLines (the last saying "+n more"). `lines` and
        /// `longest` (characters) size the box.
        public static string Text(IList<string> labels, int[] idx, int[] groupOf, int n, int g, int maxLines, out int lines, out int longest)
        {
            lines = 0;
            longest = 0;
            // The members' event indices, in time order (= index order).
            List<int> members = new List<int>();
            for (int k = 0; k < n; k++)
                if (groupOf[k] == g) members.Add(idx[k]);
            members.Sort();

            List<string> distinct = new List<string>();
            List<int> counts = new List<int>();
            for (int i = 0; i < members.Count; i++)
            {
                string s = labels[members[i]] ?? "";
                int at = distinct.IndexOf(s);
                if (at >= 0) counts[at]++;
                else { distinct.Add(s); counts.Add(1); }
            }

            int shown = distinct.Count;
            int hidden = 0;
            if (maxLines > 0 && shown > maxLines)
            {
                shown = maxLines - 1;
                for (int i = shown; i < distinct.Count; i++) hidden += counts[i];
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < shown; i++)
            {
                string line = counts[i] > 1 ? distinct[i] + " x" + counts[i] : distinct[i];
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(line);
                lines++;
                if (line.Length > longest) longest = line.Length;
            }
            if (hidden > 0)
            {
                string more = "+" + hidden + " more";
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(more);
                lines++;
                if (more.Length > longest) longest = more.Length;
            }
            return sb.ToString();
        }

        /// Box size for a label of `lines` lines, the longest `longest`
        /// characters.
        public static float Width(int longest) { return longest * CharWidth + Padding; }
        public static float Height(int lines) { return Mathf.Max(1, lines) * LineHeight; }

        /// Screen boxes (y down): centre x cx[k], top[k], w[k] x h[k], in
        /// priority order (nearest first). A shown box that overlaps one
        /// placed before it moves up to just above it, until it is clear;
        /// the first keeps its place. Only top[] changes.
        public static void Stack(float[] cx, float[] top, float[] w, float[] h, bool[] shown, int n, float gap)
        {
            for (int k = 1; k < n; k++)
            {
                if (!shown[k]) continue;
                // Each move is upwards past one earlier box, so k passes settle it.
                for (int pass = 0; pass <= k; pass++)
                {
                    bool moved = false;
                    for (int j = 0; j < k; j++)
                    {
                        if (!shown[j]) continue;
                        if (!Overlap(cx[k], top[k], w[k], h[k], cx[j], top[j], w[j], h[j], gap)) continue;
                        top[k] = top[j] - h[k] - gap;
                        moved = true;
                    }
                    if (!moved) break;
                }
            }
        }

        private static bool Overlap(float ax, float at, float aw, float ah, float bx, float bt, float bw, float bh, float gap)
        {
            if (Mathf.Abs(ax - bx) * 2f >= aw + bw) return false;
            return at < bt + bh + gap && bt < at + ah + gap;
        }
    }
}
