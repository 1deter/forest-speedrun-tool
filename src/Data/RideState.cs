using System;
using System.Globalization;
using UnityEngine;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // A ride or climb at capture (Game/RideModes), as a savestate's `ride`
    // header: "<kind> x,y,z x,y,z". The two vectors by kind:
    //   zipline      the line's enter trigger (its box centre) | the body's velocity
    //   sled         the sled's position | its rotation (Euler angles)
    //   glider       where the player was | the body's velocity (flying)
    //   glider-held  where the player was | the body's velocity (holding it)
    //   cliff        where the player was | the cliff's normal at the entry
    // ------------------------------------------------------------------
    public struct RideState
    {
        public const string Zipline = "zipline";
        public const string Sled = "sled";
        public const string Glider = "glider";
        public const string GliderHeld = "glider-held";
        public const string Cliff = "cliff";

        public string Kind;
        public Vector3 A;
        public Vector3 B;

        public RideState(string kind, Vector3 a, Vector3 b)
        {
            Kind = kind;
            A = a;
            B = b;
        }

        public bool IsNone { get { return string.IsNullOrEmpty(Kind); } }

        public string Write()
        {
            if (IsNone) return "";
            return Kind + " " + V(A) + " " + V(B);
        }

        /// False for "" and for anything malformed (a later version's kind
        /// parses; RideModes says it does not know it).
        public static bool TryParse(string text, out RideState s)
        {
            s = new RideState();
            if (string.IsNullOrEmpty(text)) return false;
            string[] parts = text.Trim().Split(' ');
            if (parts.Length != 3 || parts[0].Length == 0) return false;
            Vector3 a, b;
            if (!TryV(parts[1], out a) || !TryV(parts[2], out b)) return false;
            s = new RideState(parts[0], a, b);
            return true;
        }

        private static string V(Vector3 v)
        {
            return F(v.x) + "," + F(v.y) + "," + F(v.z);
        }

        private static string F(float f)
        {
            return f.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static bool TryV(string text, out Vector3 v)
        {
            v = Vector3.zero;
            string[] c = text.Split(',');
            if (c.Length != 3) return false;
            float x, y, z;
            if (!float.TryParse(c[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                !float.TryParse(c[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
                !float.TryParse(c[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z)) return false;
            v = new Vector3(x, y, z);
            return true;
        }
    }
}
