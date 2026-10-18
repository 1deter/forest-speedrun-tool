using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    /// A kept animal's health, hurt skin and poison across a Quick load
    /// (T-0425, Game/AnimalKeeper). Pure.
    /// The savestate's `animals` line: one entry per live animal at the
    /// capture, "id:health@d1,d2,d3,d4[@p]" joined by ';' (id = the root's
    /// instance id, d1-d4 the skin's _Damage1-4, p = poisoned).
    public static class AnimalStates
    {
        public sealed class Entry
        {
            public int Id;
            public int Health;
            public float[] Damage = new float[4];
            public bool Poisoned;
        }

        public static string Format(IEnumerable<Entry> entries)
        {
            if (entries == null) return "";
            StringBuilder sb = new StringBuilder();
            foreach (Entry e in entries)
            {
                if (e == null || e.Damage == null || e.Damage.Length != 4) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(e.Id.ToString(CultureInfo.InvariantCulture)).Append(':')
                  .Append(e.Health.ToString(CultureInfo.InvariantCulture)).Append('@');
                for (int i = 0; i < 4; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(e.Damage[i].ToString("R", CultureInfo.InvariantCulture));
                }
                if (e.Poisoned) sb.Append("@p");
            }
            return sb.ToString();
        }

        /// The entries of an `animals` line; bad entries are dropped.
        public static List<Entry> Parse(string line)
        {
            List<Entry> list = new List<Entry>();
            if (string.IsNullOrEmpty(line)) return list;
            string[] parts = line.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                Entry e = ParseOne(parts[i].Trim());
                if (e != null) list.Add(e);
            }
            return list;
        }

        private static Entry ParseOne(string s)
        {
            int colon = s.IndexOf(':');
            if (colon <= 0) return null;
            string[] rest = s.Substring(colon + 1).Split('@');
            if (rest.Length < 2 || rest.Length > 3) return null;
            Entry e = new Entry();
            if (!int.TryParse(s.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out e.Id)) return null;
            if (!int.TryParse(rest[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out e.Health)) return null;
            string[] d = rest[1].Split(',');
            if (d.Length != 4) return null;
            for (int i = 0; i < 4; i++)
                if (!float.TryParse(d[i], NumberStyles.Float, CultureInfo.InvariantCulture, out e.Damage[i])) return null;
            if (rest.Length == 3)
            {
                if (rest[2] != "p") return null;
                e.Poisoned = true;
            }
            return e;
        }

        /// "animals: 2 put back (health x2, skin x1, poison stopped x1)";
        /// "" when nothing changed. `animals` counts an animal once however
        /// many of its parts changed.
        public static string Describe(int animals, int health, int skin, int poison)
        {
            if (animals <= 0) return "";
            StringBuilder sb = new StringBuilder("animals: ");
            sb.Append(animals.ToString(CultureInfo.InvariantCulture)).Append(" put back (");
            bool first = true;
            Part(sb, ref first, "health", health);
            Part(sb, ref first, "skin", skin);
            Part(sb, ref first, "poison stopped", poison);
            return sb.Append(')').ToString();
        }

        private static void Part(StringBuilder sb, ref bool first, string name, int n)
        {
            if (n <= 0) return;
            if (!first) sb.Append(", ");
            first = false;
            sb.Append(name).Append(" x").Append(n.ToString(CultureInfo.InvariantCulture));
        }
    }
}
