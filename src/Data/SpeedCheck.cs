using System;
using System.Globalization;

namespace ForestOverlay.Data
{
    /// Run mode's game-speed watch: is Time.timeScale something the game
    /// itself writes? Vanilla writes 1 in play, 0 only while the pause menu
    /// or the inventory is open, and 0.1 while a save loads, which the
    /// caller excuses (docs/game-notes.md *timeScale*).
    public static class SpeedCheck
    {
        /// How long a 0 outside a menu is tolerated (the toggle frames), unscaled seconds.
        public const float ZeroGrace = 0.5f;

        /// True for any value but exactly 0 or 1 (0 is judged by the watch, with its grace).
        public static bool Irregular(float scale)
        {
            return scale != 0f && scale != 1f;
        }

        /// The flag text.
        public static string FlagText(float scale)
        {
            return "the game's speed was changed (time scale " + Num(scale) + ")";
        }

        /// The log line for an episode that has ended.
        public static string EpisodeText(float peak, float seconds)
        {
            return "time scale " + Num(peak) + " lasted " + seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s (back to normal)";
        }

        private static string Num(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
    }

    /// One flag and one end line per episode of an irregular time scale:
    /// the first irregular frame raises the flag, the frame it returns to
    /// normal (or the attempt's end) gives a line with the peak and the
    /// unscaled seconds. Every episode gets both, repeats of a value too
    /// (the run's flag list dedups by text; the end lines are the record).
    /// A 0 is irregular only outside the pause menu and the inventory, and
    /// only after SpeedCheck.ZeroGrace. Frames the plugin itself explains
    /// (a savestate action, a cutscene fast-forward, a save loading) are
    /// excused: they start no episode and leave a running one as it was.
    public sealed class TimeScaleWatch
    {
        private bool _active;
        private float _peak;
        private float _seconds;
        private float _zeroFor;     // unscaled seconds of a 0 outside a menu

        /// A new attempt: nothing is remembered.
        public void Reset()
        {
            _active = false; _peak = 0f; _seconds = 0f; _zeroFor = 0f;
        }

        /// One frame (dt = unscaled seconds). Returns the flag text on an
        /// episode's first irregular frame (else null); `ended` is the
        /// summary when an episode has just ended (else null).
        public string Frame(float scale, bool excused, bool zeroAllowed, float dt, out string ended)
        {
            ended = null;
            if (excused) return null;

            bool irregular;
            if (scale == 0f)
            {
                if (zeroAllowed) _zeroFor = 0f; else _zeroFor += dt;
                irregular = !zeroAllowed && _zeroFor > SpeedCheck.ZeroGrace;
            }
            else
            {
                _zeroFor = 0f;
                irregular = SpeedCheck.Irregular(scale);
            }

            if (!irregular)
            {
                ended = Close();
                return null;
            }
            string flag = null;
            if (!_active)
            {
                _active = true;
                _peak = scale;
                _seconds = scale == 0f ? _zeroFor : dt;
                flag = SpeedCheck.FlagText(scale);
            }
            else
            {
                if (Math.Abs(scale - 1f) > Math.Abs(_peak - 1f)) _peak = scale;
                _seconds += dt;
            }
            return flag;
        }

        /// The attempt ends: the summary of an episode still running, else null.
        public string End() { return Close(); }

        private string Close()
        {
            if (!_active) return null;
            _active = false;
            return SpeedCheck.EpisodeText(_peak, _seconds);
        }
    }
}
