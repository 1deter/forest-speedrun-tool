using System;

namespace ForestOverlay.Data
{
    // The installed-font choice (T-0339): "" is the default look, else the
    // name of a font installed on the PC. Pure, so it is tested.
    public static class FontNames
    {
        /// The installed name that matches `text` ignoring case and spaces at
        /// the ends, else null. "" matches nothing (that is the default).
        public static string Find(string[] names, string text)
        {
            if (names == null || text == null) return null;
            string t = text.Trim();
            if (t.Length == 0) return null;
            for (int i = 0; i < names.Length; i++)
                if (string.Equals(names[i], t, StringComparison.OrdinalIgnoreCase)) return names[i];
            return null;
        }

        /// What a saved config value means: the installed font it names (the
        /// installed spelling), else "" = the default. A font that is gone, a
        /// blank or null all fall back to the default.
        public static string Resolve(string[] names, string saved)
        {
            return Find(names, saved) ?? "";
        }

        /// The next choice in the ring: default, names[0] ... names[n-1], default.
        /// A current name not in the list counts as the default.
        public static string Step(string[] names, string current, int direction)
        {
            int n = names == null ? 0 : names.Length;
            if (n == 0) return "";
            int at = -1;   // -1 = the default
            string found = Find(names, current);
            if (found != null)
                for (int i = 0; i < n; i++) if (names[i] == found) { at = i; break; }
            int next = at + (direction < 0 ? -1 : 1);
            if (next < -1) next = n - 1;
            if (next >= n) next = -1;
            return next < 0 ? "" : names[next];
        }
    }
}
