using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // One greeble zone on a pooled tree at capture (fix list 3): where the
    // zone stands, the seed its sticks / rocks were drawn from and its
    // per-instance state bytes (255 = taken). Game/GreebleKeeper gives a
    // zone at the same place these values back after a restore.
    //
    //   501.23,76.37,90.30:11525:fdfdfdfd
    //
    // WHY: such a zone's seed and taken flags belong to the pool object,
    // not the tree - they came from whichever tree that object served
    // first - so the same tree shows other sticks each time it respawns
    // (game-notes *Greebles*). The save does not hold them.
    //
    // Pure so the header round trip is tested.
    // ------------------------------------------------------------------
    public sealed class GreebleRecord
    {
        /// How near a zone must stand to count as the recorded one. Trees
        /// do not move; this only absorbs float formatting.
        public const float Tolerance = 0.1f;

        public float X, Y, Z;
        public int Seed;
        public byte[] States = new byte[0];

        public string Format()
        {
            StringBuilder sb = new StringBuilder(32 + States.Length * 2);
            sb.Append(F(X)).Append(',').Append(F(Y)).Append(',').Append(F(Z))
              .Append(':').Append(Seed.ToString(CultureInfo.InvariantCulture)).Append(':');
            for (int i = 0; i < States.Length; i++) sb.Append(States[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public static bool TryParse(string text, out GreebleRecord record)
        {
            record = null;
            if (string.IsNullOrEmpty(text)) return false;
            string[] parts = text.Trim().Split(':');
            if (parts.Length != 3) return false;

            string[] p = parts[0].Split(',');
            if (p.Length != 3) return false;
            GreebleRecord r = new GreebleRecord();
            if (!TryF(p[0], out r.X) || !TryF(p[1], out r.Y) || !TryF(p[2], out r.Z)) return false;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out r.Seed)) return false;

            string hex = parts[2];
            if (hex.Length % 2 != 0) return false;
            r.States = new byte[hex.Length / 2];
            for (int i = 0; i < r.States.Length; i++)
                if (!byte.TryParse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r.States[i])) return false;

            record = r;
            return true;
        }

        /// Parses every entry it can; bad ones are counted, not thrown.
        public static List<GreebleRecord> ParseAll(IList<string> entries, out int bad)
        {
            bad = 0;
            List<GreebleRecord> list = new List<GreebleRecord>();
            if (entries == null) return list;
            for (int i = 0; i < entries.Count; i++)
            {
                GreebleRecord r;
                if (TryParse(entries[i], out r)) list.Add(r);
                else bad++;
            }
            return list;
        }

        /// The index of the record standing at (x, y, z), or -1.
        public static int IndexAt(List<GreebleRecord> list, float x, float y, float z)
        {
            for (int i = 0; i < list.Count; i++)
            {
                GreebleRecord r = list[i];
                if (Math.Abs(r.X - x) <= Tolerance && Math.Abs(r.Y - y) <= Tolerance && Math.Abs(r.Z - z) <= Tolerance) return i;
            }
            return -1;
        }

        /// True when a zone with this seed and these state bytes already
        /// shows what the record does: the same seed and the same taken
        /// (255) instances. Active-state flavours (252 / 253 / 254) are the
        /// game's bookkeeping and differ between spawns.
        public bool Matches(int seed, byte[] states)
        {
            if (seed != Seed || states == null || states.Length != States.Length) return false;
            for (int i = 0; i < States.Length; i++)
                if ((States[i] == Taken) != (states[i] == Taken)) return false;
            return true;
        }

        public const byte Taken = 255;

        private static string F(float v)
        {
            return v.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static bool TryF(string s, out float v)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
    }
}
