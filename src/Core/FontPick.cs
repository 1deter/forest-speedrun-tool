using System;
using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Core
{
    // Installed PC fonts by name (T-0339): the list for Settings and one
    // dynamic font per owner, name and size, made once. Unity 5.6 loads OS
    // fonts by name (Font.CreateDynamicFontFromOSFont), and gives a fallback
    // font for a name it does not know - so a saved name is checked against
    // the installed list first (Resolved).
    public static class FontPick
    {
        private static string[] _names;
        private static readonly Dictionary<string, Font> _made = new Dictionary<string, Font>();
        private static readonly HashSet<string> _warned = new HashSet<string>();

        /// Set by the host; the one warning for a saved font that is gone.
        public static BepInEx.Logging.ManualLogSource Log;

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

        /// A saved name as the installed font it names, else "" (the default),
        /// with one log warning per missing name.
        public static string Resolved(string saved)
        {
            string r = FontNames.Resolve(Installed(), saved);
            if (r.Length == 0 && !string.IsNullOrEmpty(saved) && saved.Trim().Length > 0 && _warned.Add(saved))
            {
                if (Log != null) Log.LogWarning("Font: " + saved.Trim() + " is not installed - using the default font.");
            }
            return r;
        }

        /// The font `name` at `size` for one consumer (`owner`: each keeps its
        /// own instance, so one's glyph rebuild never rebuilds another's
        /// texture, T-0301), or null for "" / a name that will not load.
        public static Font Get(string owner, string name, int size)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string key = owner + "|" + name + "|" + size;
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
