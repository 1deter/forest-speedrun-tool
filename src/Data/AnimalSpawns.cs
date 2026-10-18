using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    /// Where the zone animals stood, which zone each belonged to, and the
    /// zones' spawn caps at a capture, so a Quick load can bring back one
    /// killed since, remove one spawned since and move the rest back
    /// (T-0428, Game/AnimalKeeper). Pure.
    /// Lines: `animalposes` "id:prefab@zone@x,y,z@qx,qy,qz,qw" joined by ';';
    /// `animalzones` "index:max@id,id@back,back" joined by ';' (back = seconds
    /// left at the capture); `animalcounts` "name=value" joined by ';'.
    public static class AnimalSpawns
    {
        public sealed class Pose
        {
            public int Id;
            public string Prefab = "";
            public int Zone = -1;
            public float[] Pos = new float[3];
            public float[] Rot = new float[4];
        }

        public sealed class ZoneState
        {
            public int Index;
            public int Max;
            public List<int> Ids = new List<int>();
            public List<float> Backs = new List<float>();
        }

        public sealed class Plan
        {
            /// Live animals (their own ids) that were not there at the capture.
            public List<int> Despawn = new List<int>();
            /// Captured ids with no live animal now.
            public List<int> Respawn = new List<int>();
            /// Captured ids with a live animal (its id may differ: stand-in).
            public List<int> Move = new List<int>();
        }

        private static string F(float v) { return v.ToString("R", CultureInfo.InvariantCulture); }
        private static string I(int v) { return v.ToString(CultureInfo.InvariantCulture); }

        public static bool NameOk(string name)
        {
            return !string.IsNullOrEmpty(name) && name.IndexOfAny(new[] { ':', '@', ';', ',' }) < 0;
        }

        // ---- poses ----

        public static string FormatPoses(IEnumerable<Pose> poses)
        {
            if (poses == null) return "";
            StringBuilder sb = new StringBuilder();
            foreach (Pose p in poses)
            {
                if (p == null || !NameOk(p.Prefab) || p.Pos == null || p.Pos.Length != 3 || p.Rot == null || p.Rot.Length != 4) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(I(p.Id)).Append(':').Append(p.Prefab).Append('@').Append(I(p.Zone)).Append('@')
                  .Append(F(p.Pos[0])).Append(',').Append(F(p.Pos[1])).Append(',').Append(F(p.Pos[2])).Append('@')
                  .Append(F(p.Rot[0])).Append(',').Append(F(p.Rot[1])).Append(',').Append(F(p.Rot[2])).Append(',').Append(F(p.Rot[3]));
            }
            return sb.ToString();
        }

        public static List<Pose> ParsePoses(string line)
        {
            List<Pose> list = new List<Pose>();
            if (string.IsNullOrEmpty(line)) return list;
            string[] parts = line.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                Pose p = ParsePose(parts[i].Trim());
                if (p != null) list.Add(p);
            }
            return list;
        }

        private static Pose ParsePose(string s)
        {
            int colon = s.IndexOf(':');
            if (colon <= 0) return null;
            string[] rest = s.Substring(colon + 1).Split('@');
            if (rest.Length != 4 || !NameOk(rest[0])) return null;
            Pose p = new Pose();
            if (!int.TryParse(s.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out p.Id)) return null;
            p.Prefab = rest[0];
            if (!int.TryParse(rest[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out p.Zone)) return null;
            if (!Floats(rest[2], p.Pos) || !Floats(rest[3], p.Rot)) return null;
            return p;
        }

        private static bool Floats(string s, float[] into)
        {
            string[] d = s.Split(',');
            if (d.Length != into.Length) return false;
            for (int i = 0; i < d.Length; i++)
                if (!float.TryParse(d[i], NumberStyles.Float, CultureInfo.InvariantCulture, out into[i])) return false;
            return true;
        }

        // ---- zones ----

        public static string FormatZones(IEnumerable<ZoneState> zones)
        {
            if (zones == null) return "";
            StringBuilder sb = new StringBuilder();
            foreach (ZoneState z in zones)
            {
                if (z == null) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(I(z.Index)).Append(':').Append(I(z.Max)).Append('@');
                for (int i = 0; i < z.Ids.Count; i++) { if (i > 0) sb.Append(','); sb.Append(I(z.Ids[i])); }
                sb.Append('@');
                for (int i = 0; i < z.Backs.Count; i++) { if (i > 0) sb.Append(','); sb.Append(F(z.Backs[i])); }
            }
            return sb.ToString();
        }

        public static List<ZoneState> ParseZones(string line)
        {
            List<ZoneState> list = new List<ZoneState>();
            if (string.IsNullOrEmpty(line)) return list;
            string[] parts = line.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                string s = parts[i].Trim();
                int colon = s.IndexOf(':');
                if (colon <= 0) continue;
                string[] rest = s.Substring(colon + 1).Split('@');
                if (rest.Length != 3) continue;
                ZoneState z = new ZoneState();
                if (!int.TryParse(s.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out z.Index)) continue;
                if (!int.TryParse(rest[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out z.Max)) continue;
                bool ok = true;
                if (rest[1].Length > 0)
                {
                    string[] ids = rest[1].Split(',');
                    for (int k = 0; k < ids.Length && ok; k++)
                    {
                        int id;
                        if (int.TryParse(ids[k], NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) z.Ids.Add(id); else ok = false;
                    }
                }
                if (ok && rest[2].Length > 0)
                {
                    string[] bs = rest[2].Split(',');
                    for (int k = 0; k < bs.Length && ok; k++)
                    {
                        float b;
                        if (float.TryParse(bs[k], NumberStyles.Float, CultureInfo.InvariantCulture, out b)) z.Backs.Add(b); else ok = false;
                    }
                }
                if (ok) list.Add(z);
            }
            return list;
        }

        // ---- counts ----

        public static string FormatCounts(IEnumerable<KeyValuePair<string, int>> counts)
        {
            if (counts == null) return "";
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, int> c in counts)
            {
                if (!NameOk(c.Key) || c.Key.IndexOf('=') >= 0) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(c.Key).Append('=').Append(I(c.Value));
            }
            return sb.ToString();
        }

        public static List<KeyValuePair<string, int>> ParseCounts(string line)
        {
            List<KeyValuePair<string, int>> list = new List<KeyValuePair<string, int>>();
            if (string.IsNullOrEmpty(line)) return list;
            string[] parts = line.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq <= 0) continue;
                int v;
                if (!int.TryParse(parts[i].Substring(eq + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) continue;
                list.Add(new KeyValuePair<string, int>(parts[i].Substring(0, eq).Trim(), v));
            }
            return list;
        }

        // ---- the planner ----

        /// The id a live root counts as: its own when the capture holds it,
        /// else the captured id it stands in for (if any).
        public static int Resolve(int id, ICollection<int> captured, IDictionary<int, int> standIn)
        {
            int mapped;
            if (captured != null && captured.Contains(id)) return id;
            return standIn != null && standIn.TryGetValue(id, out mapped) ? mapped : id;
        }

        /// `liveIds`: the instance ids of the live zone animals now. `standIn`
        /// maps an id made since (a new root the pool gave) to the captured id
        /// it stands for; may be null.
        public static Plan MakePlan(IEnumerable<int> captured, IEnumerable<int> liveIds, IDictionary<int, int> standIn)
        {
            Plan plan = new Plan();
            HashSet<int> cap = new HashSet<int>();
            if (captured != null) foreach (int c in captured) cap.Add(c);
            HashSet<int> seen = new HashSet<int>();
            if (liveIds != null)
            {
                foreach (int live in liveIds)
                {
                    int id = Resolve(live, cap, standIn);
                    if (cap.Contains(id) && seen.Add(id)) plan.Move.Add(id);
                    else plan.Despawn.Add(live);
                }
            }
            foreach (int c in cap)
                if (!seen.Contains(c)) plan.Respawn.Add(c);
            plan.Respawn.Sort();
            plan.Move.Sort();
            return plan;
        }

        /// "animals: 2 put back (health x1), 1 brought back, 1 removed
        /// (spawned since), 3 moved back, 2 zone(s) reset"; the parts that
        /// are 0 are left out. `putBack` is the health step's own text.
        public static string Describe(string putBack, int broughtBack, int removed, int moved, int zones)
        {
            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrEmpty(putBack))
                sb.Append(putBack.StartsWith("animals: ") ? putBack.Substring(9) : putBack);
            Add(sb, broughtBack > 0, I(broughtBack) + " brought back");
            Add(sb, removed > 0, I(removed) + " removed (spawned since)");
            Add(sb, moved > 0, I(moved) + " moved back");
            Add(sb, zones > 0, I(zones) + " zone(s) reset");
            return sb.Length == 0 ? "" : "animals: " + sb;
        }

        private static void Add(StringBuilder sb, bool on, string text)
        {
            if (!on) return;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(text);
        }
    }
}
