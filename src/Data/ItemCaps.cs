using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Runner-set carry caps per item (sxczurass, QA 2026-09-27: "50 rocks
    // instead of 5"; v0.24.192), as config text: "53:50;57:30" (item id :
    // cap), in the order they were added. Pure: linked into the tests.
    // ------------------------------------------------------------------
    public static class ItemCaps
    {
        public const int MinCap = 1, MaxCap = 9999;

        public static List<KeyValuePair<int, int>> Parse(string text)
        {
            List<KeyValuePair<int, int>> list = new List<KeyValuePair<int, int>>();
            if (string.IsNullOrEmpty(text)) return list;
            string[] parts = text.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string[] p = parts[i].Split(':');
                int id, cap;
                if (p.Length != 2 ||
                    !int.TryParse(p[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id) ||
                    !int.TryParse(p[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out cap)) continue;
                if (id <= 0) continue;
                cap = Math.Max(MinCap, Math.Min(MaxCap, cap));
                int at = IndexOf(list, id);
                if (at >= 0) list[at] = new KeyValuePair<int, int>(id, cap);
                else list.Add(new KeyValuePair<int, int>(id, cap));
            }
            return list;
        }

        public static string Format(List<KeyValuePair<int, int>> list)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(list[i].Key.ToString(CultureInfo.InvariantCulture)).Append(':')
                  .Append(list[i].Value.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public static int IndexOf(List<KeyValuePair<int, int>> list, int id)
        {
            for (int i = 0; i < list.Count; i++) if (list[i].Key == id) return i;
            return -1;
        }
    }
}
