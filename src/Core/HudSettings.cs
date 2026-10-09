using BepInEx.Configuration;
using ForestOverlay.Data;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // The HUD's settings, under [HUD] in the config: a switch per value
    // (Data/HudLines; the box's lines and the widgets), the box itself, and
    // the HUD-wide compact, text size and position (the box, or the
    // practice / ON NOW warnings without it). Defaults are the box as it
    // always looked.
    //
    // Every write here is one click (a tick box, a size step) or a drag's
    // release - never per frame or per mouse event: a config write saves
    // the whole file (gotcha 60).
    // ------------------------------------------------------------------
    public sealed class HudSettings
    {
        public const float DefaultX = 10f, DefaultY = 10f;

        private readonly ConfigEntry<bool>[] _show = new ConfigEntry<bool>[HudLines.All.Length];
        private readonly ConfigEntry<bool> _compact, _infoBox;
        private readonly ConfigEntry<int> _textSize;
        private readonly ConfigEntry<float> _x, _y;
        private readonly ConfigFile _config;

        /// Bumped on every change, so a cached look (styles) knows to rebuild.
        public int Version { get; private set; }

        public HudSettings(ConfigFile config)
        {
            _config = config;
            for (int i = 0; i < HudLines.All.Length; i++)
            {
                HudLine l = HudLines.All[i];
                if (!l.Switchable) continue;
                _show[i] = config.Bind("HUD", l.ConfigKey, l.DefaultOn, "Info box: " + l.Name + " - " + l.Description);
            }
            _infoBox = config.Bind("HUD", "InfoBox", false,
                "The info box (top left). Off: only HUD widgets and the practice / ON NOW markers show (Settings -> Edit HUD layout).");
            _compact = config.Bind("HUD", "Compact", false,
                "The whole HUD: fewer words (shorter values in the box and the widgets, no column padding, a short title).");
            _textSize = config.Bind("HUD", "TextSize", 0,
                "Text size in px of the info box and the practice / ON NOW warnings (0 = the game's default; offered: 10, 11, 12, 14, 16, 18, 20, 24). Widgets have their own size.");
            _x = config.Bind("HUD", "X", DefaultX, "Info box position from the left, in pixels (drag it while the window is open).");
            _y = config.Bind("HUD", "Y", DefaultY, "Info box position from the top, in pixels.");
        }

        /// Whether line i of HudLines.All is shown; locked and externally
        /// switched lines always answer yes here.
        public bool Shows(int index)
        {
            if (index < 0 || index >= _show.Length) return true;
            ConfigEntry<bool> e = _show[index];
            return e == null || e.Value;
        }

        public void SetShows(int index, bool on)
        {
            if (index < 0 || index >= _show.Length || _show[index] == null || _show[index].Value == on) return;
            _show[index].Value = on;
            Version++;
        }

        public bool InfoBox
        {
            get { return _infoBox.Value; }
            set { if (_infoBox.Value != value) { _infoBox.Value = value; Version++; } }
        }

        public bool Compact
        {
            get { return _compact.Value; }
            set { if (_compact.Value != value) { _compact.Value = value; Version++; } }
        }

        /// 0 = default; an odd value typed into the file snaps to an offered one.
        public int TextSize
        {
            get { return HudLines.TextSizes[HudLines.NearestSize(_textSize.Value)]; }
            set { if (_textSize.Value != value) { _textSize.Value = value; Version++; } }
        }

        public float X { get { return _x.Value; } }
        public float Y { get { return _y.Value; } }

        public void SetPosition(float x, float y)
        {
            if (_x.Value == x && _y.Value == y) return;
            bool saveEach = _config.SaveOnConfigSet;
            _config.SaveOnConfigSet = false;
            _x.Value = x;
            _y.Value = y;
            _config.SaveOnConfigSet = saveEach;
            _config.Save();
            Version++;
        }

        /// The values' switches back to their defaults, nothing else
        /// (T-0253: compact, size and position are HUD-wide options with
        /// their own default buttons). One file write for all of it (each
        /// entry would save the file).
        public void ResetValues()
        {
            bool saveEach = _config.SaveOnConfigSet;
            _config.SaveOnConfigSet = false;
            for (int i = 0; i < _show.Length; i++)
                if (_show[i] != null) _show[i].Value = HudLines.All[i].DefaultOn;
            _config.SaveOnConfigSet = saveEach;
            _config.Save();
            Version++;
        }
    }
}
