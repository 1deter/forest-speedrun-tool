using BepInEx.Configuration;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // The HUD's settings, under [HUD] in the config: a switch per value
    // (Data/HudLines) and the HUD-wide compact, text size and position (the
    // column of values, T-0018) and the trend colours (T-0023). Defaults are
    // the pre-overhaul info box's values and place.
    //
    // Every write here is one click (a tick box, a size step) or a drag's
    // release - never per frame or per mouse event: a config write saves
    // the whole file (gotcha 60).
    // ------------------------------------------------------------------
    public sealed class HudSettings
    {
        public const float DefaultX = 10f, DefaultY = 10f;

        private readonly ConfigEntry<bool>[] _show = new ConfigEntry<bool>[HudLines.All.Length];
        private readonly ConfigEntry<bool> _compact;
        private readonly ConfigEntry<int> _textSize;
        private readonly ConfigEntry<float> _x, _y;
        private readonly ConfigEntry<string>[] _trend = new ConfigEntry<string>[3];   // by TrendState
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
                _show[i] = config.Bind("HUD", l.ConfigKey, l.DefaultOn, "HUD value: " + l.Name + " - " + l.Description);
            }
            _compact = config.Bind("HUD", "Compact", false,
                "The whole HUD: fewer words (shorter values, a short title).");
            _textSize = config.Bind("HUD", "TextSize", 0,
                "Text size in px of the column of values and the practice / ON NOW warnings (0 = the game's default; offered: 10, 11, 12, 14, 16, 18, 20, 24). A value placed on its own has its own size.");
            _x = config.Bind("HUD", "X", DefaultX, "The column of values: position from the left, in pixels (drag it while the window is open).");
            _y = config.Bind("HUD", "Y", DefaultY, "The column of values: position from the top, in pixels.");
            _trend[(int)TrendState.Gaining] = config.Bind("HUD", "TrendGaining", HudTrend.Format(HudTrend.DefaultGain),
                "Trend colour (#RRGGBB) of a value with Trend on (Edit HUD layout) while it rises.");
            _trend[(int)TrendState.Losing] = config.Bind("HUD", "TrendLosing", HudTrend.Format(HudTrend.DefaultLoss),
                "Trend colour (#RRGGBB) of a value with Trend on while it falls.");
        }

        // --- trend colours (T-0023) -------------------------------------------

        public static readonly uint[] TrendDefaults = { 0, HudTrend.DefaultGain, HudTrend.DefaultLoss };   // Steady: no setting
        private readonly string[] _trendSeen = new string[3];
        private readonly Color[] _trendColour = new Color[3];

        /// The colour a value with Trend on draws in. Read in OnGUI: the
        /// text is parsed again only when the setting changed (a reference
        /// compare), so nothing allocates.
        public Color TrendColour(TrendState state)
        {
            // Steady is the theme's text colour (author, 2026-10-11): no setting of its own.
            int i = (int)state;
            if (i < 0 || i >= _trend.Length || _trend[i] == null) return UiKit.TextColour;
            string text = _trend[i].Value;
            if (!ReferenceEquals(text, _trendSeen[i]))
            {
                _trendSeen[i] = text;
                uint c = HudTrend.ParseOr(text, TrendDefaults[i]);
                _trendColour[i] = new Color(UiPalette.R(c), UiPalette.G(c), UiPalette.B(c), UiPalette.A(c));
            }
            return _trendColour[i];
        }

        /// The stored text ("#33DD55"), for the Settings field.
        public string TrendText(TrendState state)
        {
            ConfigEntry<string> e = _trend[(int)state];
            return e == null ? null : e.Value;
        }

        /// Sets a trend colour from typed text; false (nothing written) when
        /// it is not a colour or is the one already set.
        public bool SetTrendText(TrendState state, string text)
        {
            uint c;
            if (!HudTrend.TryParse(text, out c)) return false;
            string formatted = HudTrend.Format(c);
            ConfigEntry<string> e = _trend[(int)state];
            if (e == null) return false;
            if (string.Equals(e.Value, formatted, System.StringComparison.OrdinalIgnoreCase)) return false;
            e.Value = formatted;
            Version++;
            return true;
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

        /// The one value switched by its own module (100% totals, HudLines
        /// External): the module hands its setting over here, so Settings
        /// and Edit HUD switch it the same way.
        public System.Func<bool> ExternalGet;
        public System.Action<bool> ExternalSet;

        public bool ExternalShows { get { return ExternalGet != null && ExternalGet(); } }

        public void SetExternalShows(bool on)
        {
            if (ExternalSet == null || ExternalShows == on) return;
            ExternalSet(on);
            Version++;
        }

        /// The HUD profile holds these (HudWidgets.Profiles, T-0019).
        public void RegisterProfileEntries(HudWidgets widgets)
        {
            for (int i = 0; i < _show.Length; i++)
                if (_show[i] != null) widgets.RegisterProfileEntry(_show[i], null);
            widgets.RegisterProfileEntry(_compact, null);
            widgets.RegisterProfileEntry(_textSize, null);
            widgets.RegisterProfileEntry(_x, null);
            widgets.RegisterProfileEntry(_y, null);
            for (int i = 0; i < _trend.Length; i++) if (_trend[i] != null) widgets.RegisterProfileEntry(_trend[i], null);
        }

        /// A profile was switched in: the cached look rebuilds.
        public void Touch()
        {
            Version++;
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
