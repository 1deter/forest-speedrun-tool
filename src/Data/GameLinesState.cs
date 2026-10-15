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
        private bool _warn;
        private bool _warned;
        private int _failures;
        private float _lastFailure;

        /// Failures in a row (each rebinds) before the overlay draws the
        /// lines for good; a quiet spell this long starts the count again,
        /// so a long session's scene changes never add up (checker, T-0325).
        public const int MaxFailures = 3;
        public const float QuietSeconds = 60f;

        /// Top to bottom: each ON NOW flag, then the marker, lowest - the
        /// line the eye lands on.
        public List<string> Lines { get { return _lines; } }

        /// Lines from here on are the marker's (the rest are flags).
        public int MarkerStart { get; private set; }

        /// Bumped whenever Lines changes.
        public int Version { get; private set; }

        /// The flags ('\n' between them) and the marker; "" or null = none.
        /// `warn`: the marker in its warning colour. Returns whether anything
        /// changed (nothing is rebuilt otherwise).
        public bool SetText(string on, string marker, bool warn)
        {
            if (warn == _warn && ReferenceEquals(on, _on) && ReferenceEquals(marker, _marker)) return false;
            if (warn == _warn && on == _on && marker == _marker) { _on = on; _marker = marker; return false; }
            _on = on;
            _marker = marker;
            _warn = warn;
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

        /// Line i in the warning colour: a marker line while warn is set
        /// (run mode's not-valid line), as the overlay fallback draws it.
        public bool LineWarns(int i)
        {
            return _warn && i >= MarkerStart;
        }

        /// The members the lines need that were not found (`names[i]` when
        /// `found[i]` is false), for the log: "" = all there, anything else
        /// the overlay text. The names come from Game/ (rule 1).
        public static string MissingMembers(string[] names, bool[] found)
        {
            string s = "";
            for (int i = 0; i < names.Length; i++)
                if (!found[i]) s = s.Length == 0 ? names[i] : s + ", " + names[i];
            return s;
        }

        /// True the first time the game's members are missing (a game
        /// update): one warning, then the overlay text for good.
        public bool WarnMissing()
        {
            if (_warned) return false;
            _warned = true;
            return true;
        }

        /// A failure drawing in the grid at `now` (seconds). True: give up,
        /// the overlay draws the lines for good; false: rebind and retry.
        public bool GiveUpAfterFailure(float now)
        {
            if (_failures > 0 && now - _lastFailure >= QuietSeconds) _failures = 0;
            _lastFailure = now;
            return ++_failures >= MaxFailures;
        }

        /// Failures in the current count (for the log line).
        public int Failures { get { return _failures; } }
    }
}
