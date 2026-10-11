using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // The HUD values' descriptions as hover tips, one set shared by
    // Settings > HUD and Edit HUD. A description that names a key reads the
    // runner's binding, not the default (T-0262: "The spot F7 restarts"
    // while Restart was on R); its text is rebuilt only when that key
    // changes, so a pass allocates nothing.
    // ------------------------------------------------------------------
    public static class HudLineTips
    {
        private const KeyCode NotShown = (KeyCode)(-1);

        private static GUIContent[] _tips;
        private static KeyCode[] _shown;
        private static HotkeyMap.Binding[] _binds;
        private static HotkeyMap _map;

        /// One tip per HudLines.All entry; `map` may be null (every key then
        /// reads as unbound).
        public static GUIContent[] Get(HotkeyMap map)
        {
            HudLine[] all = HudLines.All;
            if (_tips == null)
            {
                _tips = new GUIContent[all.Length];
                _shown = new KeyCode[all.Length];
                _binds = new HotkeyMap.Binding[all.Length];
                for (int i = 0; i < all.Length; i++)
                {
                    _tips[i] = new GUIContent(all[i].Description);
                    _shown[i] = NotShown;
                }
            }
            if (!ReferenceEquals(map, _map))
            {
                _map = map;
                for (int i = 0; i < all.Length; i++)
                {
                    _binds[i] = map != null && all[i].KeyId != null ? map.Find(all[i].KeyId) : null;
                    _shown[i] = NotShown;
                }
            }
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].KeyId == null) continue;
                KeyCode key = _binds[i] != null ? _binds[i].Key : KeyCode.None;
                if (key == _shown[i]) continue;
                _shown[i] = key;
                _tips[i].text = HudLines.Describe(all[i], key == KeyCode.None ? null : key.ToString());
            }
            return _tips;
        }
    }
}
