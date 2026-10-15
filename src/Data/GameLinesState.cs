using System.Collections.Generic;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The always-on lines (ON NOW's flags, the practice marker with run
    // mode's line) as the game's own bottom-left messages (author,
    // 2026-10-11 / 2026-10-15, T-0325; decisions.md *Plugin*). Game/
    // GameMessageLines draws them in the game's grid; this decides what the
    // lines are and who draws them.
    //
    // Pure: linked into the tests.
    // ------------------------------------------------------------------
    public enum GameLinesMode
    {
        /// The plugin draws them (the grid hidden, not loaded, or missing).
        Overlay,
        /// The game's grid shows them.
        Game,
    }

    public sealed class GameLinesState
    {
        private readonly List<string> _lines = new List<string>();
        private string _on;
        private string _marker;
        private bool _warned;

        /// Top to bottom: each ON NOW flag, then the marker, lowest - the
        /// line the eye lands on.
        public List<string> Lines { get { return _lines; } }

        /// Lines from here on are the marker's (the rest are flags).
        public int MarkerStart { get; private set; }

        /// Bumped whenever Lines changes.
        public int Version { get; private set; }

        /// The flags ('\n' between them) and the marker; "" or null = none.
        /// Returns whether the lines changed (nothing is rebuilt otherwise).
        public bool SetText(string on, string marker)
        {
            if (ReferenceEquals(on, _on) && ReferenceEquals(marker, _marker)) return false;
            if (on == _on && marker == _marker) { _on = on; _marker = marker; return false; }
            _on = on;
            _marker = marker;
            _lines.Clear();
            AddLines(on);
            MarkerStart = _lines.Count;
            // The marker may hold two lines (a ModAPI flag above it); one
            // grid cell holds one.
            AddLines(marker);
            Version++;
            return true;
        }

        private void AddLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            string[] parts = text.Split('\n');
            for (int i = 0; i < parts.Length; i++)
                if (parts[i].Trim().Length > 0) _lines.Add(parts[i]);
        }

        /// The game's grid shows them only when its members resolved and it
        /// is on screen; anywhere else the plugin draws them in the same
        /// spot, so a recording always shows them (author, 2026-10-15).
        public static GameLinesMode Decide(bool membersFound, bool gridOnScreen)
        {
            return membersFound && gridOnScreen ? GameLinesMode.Game : GameLinesMode.Overlay;
        }

        /// True the first time the game's members are missing (a game
        /// update): one warning, then the overlay text for good.
        public bool WarnMissing()
        {
            if (_warned) return false;
            _warned = true;
            return true;
        }
    }
}
