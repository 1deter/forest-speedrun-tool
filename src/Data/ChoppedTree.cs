using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    /// A tree half-chopped at capture, for the file's `chopped` line
    /// (T-0067): its CoopTreeId, the cut model's Health and each chunk still
    /// standing (the cut model's children 1..4) with its TreeCutChunk
    /// damage. Record: `2543:3:1=2,3=0,4=0`. Pure (tested).
    public sealed class ChoppedTree
    {
        public int Id;
        public int Health;
        public List<int> Slots = new List<int>();
        public List<float> Damage = new List<float>();

        public void Add(int slot, float damage)
        {
            Slots.Add(slot);
            Damage.Add(damage);
        }

        public string Format()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Id.ToString(CultureInfo.InvariantCulture)).Append(':')
              .Append(Health.ToString(CultureInfo.InvariantCulture)).Append(':');
            for (int i = 0; i < Slots.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Slots[i].ToString(CultureInfo.InvariantCulture)).Append('=')
                  .Append(Damage[i].ToString("0.##", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// False for a malformed record (the caller skips it).
        public static bool TryParse(string record, out ChoppedTree tree)
        {
            tree = null;
            if (string.IsNullOrEmpty(record)) return false;
            string[] parts = record.Trim().Split(':');
            if (parts.Length != 3) return false;
            ChoppedTree t = new ChoppedTree();
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out t.Id) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out t.Health)) return false;
            if (parts[2].Length > 0)
            {
                string[] chunks = parts[2].Split(',');
                for (int i = 0; i < chunks.Length; i++)
                {
                    string[] kv = chunks[i].Split('=');
                    int slot;
                    float damage;
                    if (kv.Length != 2 ||
                        !int.TryParse(kv[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out slot) ||
                        !float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out damage) ||
                        slot < 1 || slot > 4 || float.IsNaN(damage) || damage < 0f) return false;
                    t.Add(slot, damage);
                }
            }
            tree = t;
            return true;
        }

        /// The Fake (1..4) a chunk shows at `damage`, as TreeCutChunk.Hit
        /// leaves it: [0,1) Fake1, [1,2) Fake2, [2,3) Fake3, from 3 Fake4.
        public static int ActiveFake(float damage)
        {
            if (damage >= 3f) return 4;
            if (damage >= 2f) return 3;
            if (damage >= 1f) return 2;
            return 1;
        }

        /// Whether a live cut model (Health, slots, damage) is the record.
        public bool Matches(int health, IList<int> slots, IList<float> damage)
        {
            if (health != Health || slots.Count != Slots.Count) return false;
            for (int i = 0; i < Slots.Count; i++)
            {
                int at = -1;
                for (int k = 0; k < slots.Count; k++) if (slots[k] == Slots[i]) { at = k; break; }
                if (at < 0 || Math.Abs(damage[at] - Damage[i]) > 0.01f) return false;
            }
            return true;
        }
    }
}
