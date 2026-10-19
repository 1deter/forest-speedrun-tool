using System;
using System.Collections.Generic;
using UnityEngine;

namespace ForestOverlay.Core
{
    // Installed PC fonts by name (T-0339): the list for Settings and one
    // dynamic font per name and size, made once. Unity 5.6 loads OS fonts by
    // name (Font.CreateDynamicFontFromOSFont).
    public static class FontPick
    {
        private static string[] _names;
        private static readonly Dictionary<string, Font> _made = new Dictionary<string, Font>();

        /// Every installed font name, sorted; empty if the OS gives none.
        public static string[] Installed()
        {
            if (_names != null) return _names;
            try
            {
                string[] all = Font.GetOSInstalledFontNames();
                _names = all == null ? new string[0] : (string[])all.Clone();
                Array.Sort(_names, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception) { _names = new string[0]; }
            return _names;
        }

        /// The font `name` at `size`, or null for "" / a name that will not load.
        public static Font Get(string name, int size)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string key = name + "|" + size;
            Font f;
            if (_made.TryGetValue(key, out f)) return f;
            try
            {
                f = Font.CreateDynamicFontFromOSFont(name, size);
                if (f != null) f.hideFlags = HideFlags.HideAndDontSave;
            }
            catch (Exception) { f = null; }
            _made[key] = f;
            return f;
        }
    }
}
