using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    /// Fire across a Quick load (T-0419, Game/FireKeeper). Pure.
    /// - The savestate's `fires` line: the built fires and cave fires (Fire2)
    ///   lit at the capture, each "scene path@x,y,z" (cave fires repeat their
    ///   names, so the place tells them apart), joined by ';'.
    /// - Which burning bush / tree goes out: lit after the capture's
    ///   `bushes` mark (Game/NatureKeeper's numbers), or every one when the
    ///   file is from another world.
    /// - The restore's note.
    public static class LitFires
    {
        /// The kinds the restore counts, in the order its note names them.
        public const int Tree = 0, Bush = 1, Fire = 2, Effigy = 3, Cannibal = 4, Animal = 5, Kinds = 6;

        private static readonly string[] KindNames = { "tree", "bush", "fire", "effigy", "cannibal", "animal" };

        public static string Format(IEnumerable<string> keys)
        {
            if (keys == null) return "";
            StringBuilder sb = new StringBuilder();
            foreach (string k in keys)
            {
                if (!IsKey(k)) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(k.Trim());
            }
            return sb.ToString();
        }

        /// The keys of a `fires` line; entries that are not "path@x,y,z"
        /// are dropped.
        public static HashSet<string> Parse(string line)
        {
            HashSet<string> set = new HashSet<string>();
            if (string.IsNullOrEmpty(line)) return set;
            string[] parts = line.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                string k = parts[i].Trim();
                if (IsKey(k)) set.Add(k);
            }
            return set;
        }

        /// "path@x,y,z" with three numbers (invariant culture).
        public static bool IsKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            key = key.Trim();
            int at = key.LastIndexOf('@');
            if (at <= 0 || key.IndexOf(';') >= 0) return false;
            string[] p = key.Substring(at + 1).Split(',');
            if (p.Length != 3) return false;
            float v;
            for (int i = 0; i < 3; i++)
                if (!float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return false;
            return true;
        }

        /// The capture's number from its `bushes` mark ("world:n") when the
        /// mark is for `world`; -1 otherwise (another world, no mark).
        public static int SinceOf(string mark, string world)
        {
            if (string.IsNullOrEmpty(mark) || string.IsNullOrEmpty(world)) return -1;
            int colon = mark.LastIndexOf(':');
            if (colon <= 0 || mark.Substring(0, colon) != world) return -1;
            int n;
            if (!int.TryParse(mark.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < 0) return -1;
            return n;
        }

        /// True when the `bushes` mark is from `world` (this launch and this
        /// game); "" / no mark / another launch or world = false (T-0066).
        public static bool SameWorld(string mark, string world)
        {
            return SinceOf(mark, world) >= 0;
        }

        /// A fire lit as number `seq` goes out: lit after the capture's
        /// number, or `since` is -1 (another world: every fire, as a load).
        public static bool PutOut(int seq, int since)
        {
            return since < 0 || seq > since;
        }

        /// "fire: 3 put out (tree x1, bush x2), 1 orphan bush view removed,
        /// 1 cave fire lit again (burning at the capture)"; "" when nothing
        /// changed. `counts` is indexed by the kind constants.
        public static string Describe(int[] counts, int orphans, int relit)
        {
            int total = 0;
            StringBuilder kinds = new StringBuilder();
            if (counts != null)
            {
                for (int i = 0; i < counts.Length && i < Kinds; i++)
                {
                    if (counts[i] <= 0) continue;
                    total += counts[i];
                    if (kinds.Length > 0) kinds.Append(", ");
                    kinds.Append(KindNames[i]).Append(" x").Append(counts[i].ToString(CultureInfo.InvariantCulture));
                }
            }
            if (total == 0 && orphans <= 0 && relit <= 0) return "";
            StringBuilder sb = new StringBuilder("fire: ");
            sb.Append(total.ToString(CultureInfo.InvariantCulture)).Append(" put out");
            if (kinds.Length > 0) sb.Append(" (").Append(kinds).Append(')');
            if (orphans > 0)
                sb.Append(", ").Append(orphans.ToString(CultureInfo.InvariantCulture))
                  .Append(orphans == 1 ? " orphan bush view removed" : " orphan bush views removed");
            if (relit > 0)
                sb.Append(", ").Append(relit.ToString(CultureInfo.InvariantCulture))
                  .Append(relit == 1 ? " fire lit again (burning at the capture)" : " fires lit again (burning at the capture)");
            return sb.ToString();
        }
    }
}
