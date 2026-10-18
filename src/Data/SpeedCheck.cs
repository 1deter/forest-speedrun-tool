using System;
using System.Globalization;

namespace ForestOverlay.Data
{
    /// Run mode's game-speed watch: is Time.timeScale something the game
    /// itself writes? Vanilla writes only 0 (menus) and 1 in play
    /// (docs/game-notes.md *timeScale*), and 0.1 while a save loads, which
    /// the caller excuses.
    public static class SpeedCheck
    {
        /// null for exactly 0 or 1, else the flag text.
        public static string TimeScaleFlag(float scale)
        {
            if (scale == 0f || scale == 1f) return null;
            return "the game's speed was changed (time scale " + Num(scale) + ")";
        }

        /// The log line for an episode that has ended.
        public static string EpisodeText(float peak, float seconds)
        {
            return "time scale " + Num(peak) + " lasted " + seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s (back to normal)";
        }

        private static string Num(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
    }

    /// One flag per episode of an irregular time scale: the first frame
    /// raises the flag, the frame it returns to 0 / 1 (or the attempt's end)
    /// gives one summary line with the peak and the unscaled seconds. A later
    /// episode with the same value as the last flagged one is not flagged
    /// again. Frames the plugin itself explains (a savestate action, a
    /// cutscene fast-forward, a save loading) are excused: they start no
    /// episode and leave a running one as it was.
    public sealed class TimeScaleWatch
    {
        private bool _active;
        private bool _reported;
        private float _peak;
        private float _seconds;
        private bool _hasLast;
        private float _lastFlagged;

        /// A new attempt: nothing is remembered.
        public void Reset()
        {
            _active = false; _reported = false; _peak = 0f; _seconds = 0f; _hasLast = false; _lastFlagged = 0f;
        }

        /// One frame. Returns the flag text on an episode's first irregular
        /// frame (else null); `ended` is the summary when an episode that
        /// was flagged has just ended (else null).
        public string Frame(float scale, bool excused, float dt, out string ended)
        {
            ended = null;
            if (excused) return null;
            string irregular = SpeedCheck.TimeScaleFlag(scale);
            if (irregular == null)
            {
                ended = Close();
                return null;
            }
            string flag = null;
            if (!_active)
            {
                _active = true;
                _peak = scale;
                _seconds = 0f;
                _reported = !_hasLast || _lastFlagged != scale;
                if (_reported) { flag = irregular; _hasLast = true; _lastFlagged = scale; }
            }
            else if (Math.Abs(scale - 1f) > Math.Abs(_peak - 1f)) _peak = scale;
            _seconds += dt;
            return flag;
        }

        /// The attempt ends: the summary of an episode still running, else null.
        public string End() { return Close(); }

        private string Close()
        {
            if (!_active) return null;
            string text = _reported ? SpeedCheck.EpisodeText(_peak, _seconds) : null;
            _active = false; _reported = false;
            return text;
        }
    }
}
