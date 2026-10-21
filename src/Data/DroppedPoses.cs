using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    /// The savestate's `dropped` line (T-0416): where each loose world item
    /// (a root object with a pickup, no identifier) lay at the capture, keyed
    /// by the root's GameObject instance id, so a restore can put a copy that
    /// rolled since back. Format `id:item@x,y,z@qx,qy,qz,qw;...`, with an
    /// optional 4th segment of comma tags (T-0418): `p` (a copy of the item's
    /// own drop prefab, re-made when it is gone), `t<age>` (its despawn timer
    /// ran, `age` game seconds old) or `s` (it has one, not running), or `u` (not readable at the capture). Unknown
    /// tags are ignored. Pure.
    public static class DroppedPoses
    {
        /// What the capture knew of a copy's despawn timer.
        public enum TimerKind
        {
            Unknown,        // no tag: a file from before T-0418
            Running,        // `t<age>`
            Stopped,        // `s`
            Unread          // `u`: a new file whose capture could not read it (T-0423)
        }

        public struct Pose
        {
            public int Item;
            public float X, Y, Z, Qx, Qy, Qz, Qw;
            public bool Remake;
            public TimerKind Timer;
            public float Age;
        }

        private static string F(float v) { return v.ToString("R", CultureInfo.InvariantCulture); }

        public static string Format(List<KeyValuePair<int, Pose>> poses)
        {
            if (poses == null || poses.Count == 0) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < poses.Count; i++)
            {
                Pose p = poses[i].Value;
                if (i > 0) sb.Append(';');
                sb.Append(poses[i].Key.ToString(CultureInfo.InvariantCulture)).Append(':')
                  .Append(p.Item.ToString(CultureInfo.InvariantCulture)).Append('@')
                  .Append(F(p.X)).Append(',').Append(F(p.Y)).Append(',').Append(F(p.Z)).Append('@')
                  .Append(F(p.Qx)).Append(',').Append(F(p.Qy)).Append(',').Append(F(p.Qz)).Append(',').Append(F(p.Qw));
                string tags = Tags(p);
                if (tags.Length > 0) sb.Append('@').Append(tags);
            }
            return sb.ToString();
        }

        private static string Tags(Pose p)
        {
            string s = p.Remake ? "p" : "";
            if (p.Timer == TimerKind.Running) s += (s.Length > 0 ? "," : "") + "t" + F(p.Age);
            else if (p.Timer == TimerKind.Stopped) s += (s.Length > 0 ? "," : "") + "s";
            else if (p.Timer == TimerKind.Unread) s += (s.Length > 0 ? "," : "") + "u";
            return s;
        }

        private static void ReadTags(string seg, ref Pose p)
        {
            string[] tags = seg.Split(',');
            for (int i = 0; i < tags.Length; i++)
            {
                string t = tags[i].Trim();
                if (t == "p") p.Remake = true;
                else if (t == "u") { p.Timer = TimerKind.Unread; p.Age = 0f; }
                else if (t == "s") { p.Timer = TimerKind.Stopped; p.Age = 0f; }
                else if (t.Length > 1 && t[0] == 't')
                {
                    float age;
                    if (float.TryParse(t.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out age) && age >= 0f)
                    {
                        p.Timer = TimerKind.Running;
                        p.Age = age;
                    }
                }
            }
        }

        /// The captured ids to make again: flagged `Remake`, made at runtime
        /// (negative id), of a real item, and not among `present` (the
        /// captured ids still in the world, hidden ones too).
        public static List<int> ToRemake(Dictionary<int, Pose> poses, ICollection<int> present)
        {
            List<int> ids = new List<int>();
            if (poses == null) return ids;
            foreach (KeyValuePair<int, Pose> kv in poses)
            {
                if (!kv.Value.Remake || kv.Key >= 0 || kv.Value.Item <= 0) continue;
                if (present != null && present.Contains(kv.Key)) continue;
                ids.Add(kv.Key);
            }
            return ids;
        }

        /// Instance id -> pose. A part that does not parse is skipped; never null.
        public static Dictionary<int, Pose> Parse(string line)
        {
            Dictionary<int, Pose> map = new Dictionary<int, Pose>();
            if (string.IsNullOrEmpty(line)) return map;
            string[] parts = line.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                int colon = part.IndexOf(':');
                if (colon <= 0) continue;
                string[] seg = part.Substring(colon + 1).Split('@');
                if (seg.Length != 3 && seg.Length != 4) continue;
                int id, item;
                if (!int.TryParse(part.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) continue;
                if (!int.TryParse(seg[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out item)) continue;
                float[] v = new float[7];
                if (!Floats(seg[1], v, 0, 3) || !Floats(seg[2], v, 3, 4)) continue;
                Pose p = new Pose();
                p.Item = item;
                p.X = v[0]; p.Y = v[1]; p.Z = v[2];
                p.Qx = v[3]; p.Qy = v[4]; p.Qz = v[5]; p.Qw = v[6];
                if (seg.Length == 4) ReadTags(seg[3], ref p);
                map[id] = p;
            }
            return map;
        }

        private static bool Floats(string s, float[] into, int at, int count)
        {
            string[] f = s.Split(',');
            if (f.Length != count) return false;
            for (int i = 0; i < count; i++)
            {
                float x;
                if (!float.TryParse(f[i], NumberStyles.Float, CultureInfo.InvariantCulture, out x)) return false;
                into[at + i] = x;
            }
            return true;
        }

        /// Off by more than 0.05 m or 2 degrees (the rotation as quaternions:
        /// |dot| under cos(1 degree)).
        public static bool Differs(Pose p, float x, float y, float z, float qx, float qy, float qz, float qw)
        {
            float dx = x - p.X, dy = y - p.Y, dz = z - p.Z;
            if (dx * dx + dy * dy + dz * dz > 0.05f * 0.05f) return true;
            float dot = Math.Abs(p.Qx * qx + p.Qy * qy + p.Qz * qz + p.Qw * qw);
            return dot < 0.99985f;
        }

        /// The label a thrown thing counts under, from which marker
        /// components its root carries; "" when none marks it as one.
        public static string ThrownKind(bool bomb, bool explode, bool molotov, bool flareWeapon, bool flareSpawn, bool other)
        {
            if (bomb) return "bomb";
            if (explode) return "explosion";
            if (molotov) return "molotov";
            if (flareWeapon || flareSpawn) return "flare";
            return other ? "effect" : "";
        }
    }
}
