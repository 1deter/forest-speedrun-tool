using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The blueprints at capture (Game/BlueprintKeeper), as a savestate's
    // `blueprints` header: "<id>:<a>,<b>,<c>;..." - each placed blueprint's
    // UniqueIdentifier id and how many of each ingredient it held, in the
    // order of its recipe. "" = none were placed; no line (before
    // v0.24.203) = not known.
    //
    // Also the build HUD's tally ("GATHER LOGS 0/6"): the game keeps one
    // number per item, the sum over every placed blueprint of what it
    // still needs (Craft_Structure.Initialize adds required - present).
    // ------------------------------------------------------------------
    public static class BlueprintState
    {
        public static string Write(IList<string> ids, IList<int[]> present)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < ids.Count && i < present.Count; i++)
            {
                if (string.IsNullOrEmpty(ids[i]) || ids[i].IndexOf(':') >= 0 || ids[i].IndexOf(';') >= 0) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(ids[i]).Append(':');
                int[] p = present[i] ?? new int[0];
                for (int k = 0; k < p.Length; k++)
                {
                    if (k > 0) sb.Append(',');
                    sb.Append(p[k].ToString(CultureInfo.InvariantCulture));
                }
            }
            return sb.ToString();
        }

        /// Null for null (no header line); malformed entries are skipped.
        public static Dictionary<string, int[]> Parse(string text)
        {
            if (text == null) return null;
            Dictionary<string, int[]> map = new Dictionary<string, int[]>();
            string[] entries = text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < entries.Length; i++)
            {
                string e = entries[i].Trim();
                int colon = e.LastIndexOf(':');
                if (colon <= 0) continue;
                string id = e.Substring(0, colon).Trim();
                string rest = e.Substring(colon + 1).Trim();
                string[] parts = rest.Length > 0 ? rest.Split(',') : new string[0];
                int[] amounts = new int[parts.Length];
                bool ok = true;
                for (int k = 0; k < parts.Length && ok; k++)
                    ok = int.TryParse(parts[k].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out amounts[k]);
                if (ok && id.Length > 0) map[id] = amounts;
            }
            return map;
        }

        public static bool Same(int[] a, int[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        /// Adds one blueprint's remaining needs to `tally` (item id ->
        /// amount), as the game's Initialize does; nothing for what is
        /// complete or over-filled.
        public static void AddNeeded(Dictionary<int, int> tally, int[] itemIds, int[] required, int[] present)
        {
            for (int i = 0; i < itemIds.Length && i < required.Length; i++)
            {
                int have = present != null && i < present.Length ? present[i] : 0;
                int need = required[i] - have;
                if (need <= 0) continue;
                int sum;
                tally.TryGetValue(itemIds[i], out sum);
                tally[itemIds[i]] = sum + need;
            }
        }
    }
}
