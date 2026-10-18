using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    /// The savestate's `dropped` line (T-0416): where each loose world item
    /// (a root object with a pickup, no identifier) lay at the capture, keyed
    /// by the root's GameObject instance id, so a restore can put a copy that
    /// rolled since back. Format `id:item@x,y,z@qx,qy,qz,qw;...`. Pure.
    public static class DroppedPoses
    {
        public struct Pose
        {
            public int Item;
            public float X, Y, Z, Qx, Qy, Qz, Qw;
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
            }
            return sb.ToString();
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
                if (seg.Length != 3) continue;
                int id, item;
                if (!int.TryParse(part.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) continue;
                if (!int.TryParse(seg[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out item)) continue;
                float[] v = new float[7];
                if (!Floats(seg[1], v, 0, 3) || !Floats(seg[2], v, 3, 4)) continue;
                Pose p;
                p.Item = item;
                p.X = v[0]; p.Y = v[1]; p.Z = v[2];
                p.Qx = v[3]; p.Qy = v[4]; p.Qz = v[5]; p.Qw = v[6];
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
        public static string ThrownKind(bool bomb, bool explode, bool molotov, bool flareWeapon, string rootName, bool other)
        {
            if (bomb) return "bomb";
            if (explode) return "explosion";
            if (molotov) return "molotov";
            if (flareWeapon || (rootName != null && rootName.StartsWith("FlareSpawn", StringComparison.Ordinal))) return "flare";
            return other ? "effect" : "";
        }
    }
}
