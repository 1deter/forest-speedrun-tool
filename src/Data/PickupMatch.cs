using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ForestOverlay.Data
{
    /// Which live pickups a capture's pickup list accounts for, after a Full
    /// load. Pure (tested).
    ///
    /// Positions are not exact across loads: a bottle settled 0.27 m away
    /// and the modern axe 1.2 m (bridge, v0.24.51), and an item may come
    /// back at another of its spawn points. So each captured pickup accounts
    /// for one live pickup of the same item: the nearest within `near`
    /// first (a pile, a small settle), then any left over at any distance
    /// (a random spawn point). A live pickup left unmatched is one more of
    /// its item than the capture had - taken before the capture.
    public static class PickupMatch
    {
        public struct Entry
        {
            public int Id;
            public Vector3 Position;
        }

        /// Parses SavestateFile.PickupKey's "id@x,y,z".
        public static bool TryParse(string key, out Entry e)
        {
            e = new Entry();
            if (string.IsNullOrEmpty(key)) return false;
            int at = key.IndexOf('@');
            if (at <= 0) return false;
            string[] p = key.Substring(at + 1).Split(',');
            float x, y, z;
            if (p.Length != 3 ||
                !int.TryParse(key.Substring(0, at), NumberStyles.Integer, CultureInfo.InvariantCulture, out e.Id) ||
                !float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                !float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
                !float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z)) return false;
            e.Position = new Vector3(x, y, z);
            return true;
        }

        /// For each live pickup, whether a captured one accounts for it.
        public static bool[] Match(IList<Entry> captured, IList<Entry> live, float near)
        {
            bool[] liveUsed = new bool[live.Count];
            bool[] capUsed = new bool[captured.Count];

            Dictionary<int, List<int>> capById = new Dictionary<int, List<int>>();
            for (int i = 0; i < captured.Count; i++)
            {
                List<int> l;
                if (!capById.TryGetValue(captured[i].Id, out l)) capById[captured[i].Id] = l = new List<int>();
                l.Add(i);
            }

            List<Pair> pairs = new List<Pair>();
            for (int li = 0; li < live.Count; li++)
            {
                List<int> caps;
                if (!capById.TryGetValue(live[li].Id, out caps)) continue;
                for (int k = 0; k < caps.Count; k++)
                {
                    Pair p;
                    p.Cap = caps[k];
                    p.Live = li;
                    p.Dist = Vector3.Distance(captured[caps[k]].Position, live[li].Position);
                    pairs.Add(p);
                }
            }
            pairs.Sort(delegate(Pair a, Pair b) { return a.Dist.CompareTo(b.Dist); });

            // Near first, then whatever is left, both nearest first.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < pairs.Count; i++)
                {
                    Pair p = pairs[i];
                    if (pass == 0 && p.Dist > near) break;
                    if (capUsed[p.Cap] || liveUsed[p.Live]) continue;
                    capUsed[p.Cap] = true;
                    liveUsed[p.Live] = true;
                }
            }
            return liveUsed;
        }

        /// Whether any captured pickup, of any item, sits within `within`
        /// of `position` (a greeble slot that re-rolled its item on a load).
        public static bool SpotTaken(IList<Entry> captured, Vector3 position, float within)
        {
            for (int i = 0; i < captured.Count; i++)
                if (Vector3.Distance(captured[i].Position, position) <= within) return true;
            return false;
        }

        private struct Pair
        {
            public int Cap;
            public int Live;
            public float Dist;
        }
    }
}
