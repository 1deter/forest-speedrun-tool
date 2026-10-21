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

        /// The player's own run / swim speed (FpCharacter.runSpeed / swimmingSpeed, docs/game-notes.md
        /// *timeScale*): vanilla only ever lowers the run speed, so anything above is a change.
        public const float VanillaRunSpeed = 13.5f;
        public const float VanillaSwimSpeed = 3.75f;

        /// The flag text for a raised run or swim speed; null when both are at or below the game's.
        public static string RunSpeedFlag(float run, float swim)
        {
            if (run <= VanillaRunSpeed + 0.01f && swim <= VanillaSwimSpeed + 0.01f) return null;
            return "the player's run speed was changed (run " + Num(run) + ", swim " + Num(swim) +
                   "; the game's " + Num(VanillaRunSpeed) + " / " + Num(VanillaSwimSpeed) + ")";
        }

        // ------------------------------------------------------------------
        // The game clock against real time (T-0414 F1, F2; docs/game-notes.md
        // *The other clocks and the movement constants*). In vanilla
        // deltaTime never exceeds unscaledDeltaTime; a time scale written
        // late in a frame and put back early in the next, or a capture
        // frame rate, makes it.

        /// How far deltaTime may run ahead of unscaledDeltaTime before it counts (float noise).
        public const float ClockMargin = 1.01f;

        /// True when the frame's game time ran ahead of its real time.
        public static bool ClockAhead(float dt, float unscaledDt)
        {
            return unscaledDt > 0f && dt > unscaledDt * ClockMargin;
        }

        /// The flag text for a clock running ahead (ratio = dt / unscaled dt; scale = Time.timeScale read).
        public static string ClockFlagText(float ratio, float scale)
        {
            return "the game's clock ran faster than real time (x" + Ratio(ratio) + ", time scale read " + Num(scale) + ")";
        }

        /// The log line for a clock episode that has ended.
        public static string ClockEpisodeText(float peak, float seconds)
        {
            return "game clock x" + Ratio(peak) + " lasted " + seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s (back to real time)";
        }

        /// The flag text for Time.captureFramerate (no game writer); null for 0.
        public static string CaptureRateFlag(int rate)
        {
            if (rate == 0) return null;
            return "the game's clock was locked to " + rate.ToString(CultureInfo.InvariantCulture) +
                   " frames a second (capture frame rate)";
        }

        private static string Ratio(float r) { return r.ToString("0.##", CultureInfo.InvariantCulture); }

        // ------------------------------------------------------------------
        // The player's movement constants and the physics settings (T-0414
        // F3, author D1 2026-10-21: any physics rate but the default is
        // flagged, the game's own 30 Hz Low quality physics included).
        public const float VanillaWalkSpeed = 6.5f;
        public const float VanillaStrafeSpeed = 6f;
        public const float VanillaCrouchSpeed = 4.5f;
        public const float VanillaJumpHeight = 8f;
        public const float VanillaGravity = 10f;
        public const float VanillaPhysicsGravityY = -16f;
        public const float VanillaFixedDeltaTime = 1f / 60f;

        /// The flag text naming every movement / physics value that is not the
        /// game's, or null when all are. Walk and strafe only ever go down in
        /// vanilla (hit reactions: 1.2, 0, 1), so only a raise counts; the
        /// rest have no writer but the constructor, so any change counts.
        /// The physics rate is skipped in multiplayer (Bolt sets 1 / its fps).
        public static string MovementFlag(float walk, float strafe, float crouch, float jump, float gravity,
                                          float physX, float physY, float physZ, float fixedDt, bool multiplayer)
        {
            const float tol = 0.01f;
            string parts = null;
            if (walk > VanillaWalkSpeed + tol) parts = Add(parts, "walk speed " + Num(walk) + " (the game's " + Num(VanillaWalkSpeed) + ")");
            if (strafe > VanillaStrafeSpeed + tol) parts = Add(parts, "strafe speed " + Num(strafe) + " (the game's " + Num(VanillaStrafeSpeed) + ")");
            if (Off(crouch, VanillaCrouchSpeed, tol)) parts = Add(parts, "crouch speed " + Num(crouch) + " (the game's " + Num(VanillaCrouchSpeed) + ")");
            if (Off(jump, VanillaJumpHeight, tol)) parts = Add(parts, "jump height " + Num(jump) + " (the game's " + Num(VanillaJumpHeight) + ")");
            if (Off(gravity, VanillaGravity, tol)) parts = Add(parts, "player gravity " + Num(gravity) + " (the game's " + Num(VanillaGravity) + ")");
            if (Off(physX, 0f, tol) || Off(physY, VanillaPhysicsGravityY, tol) || Off(physZ, 0f, tol))
                parts = Add(parts, "physics gravity (" + Num(physX) + ", " + Num(physY) + ", " + Num(physZ) + ") (the game's (0, " + Num(VanillaPhysicsGravityY) + ", 0))");
            if (!multiplayer && Math.Abs(fixedDt - VanillaFixedDeltaTime) > 1e-5f)
                parts = Add(parts, "physics rate " + Hz(fixedDt) + " Hz (the game's 60 Hz" +
                                   (Math.Abs(fixedDt - 1f / 30f) <= 1e-5f ? "; 30 is its hidden Low quality physics setting" : "") + ")");
            return parts == null ? null : "the game's movement values were changed: " + parts;
        }

        private static bool Off(float v, float want, float tol) { return Math.Abs(v - want) > tol; }
        private static string Add(string list, string item) { return list == null ? item : list + ", " + item; }
        private static string Hz(float fixedDt)
        {
            return fixedDt > 0f ? (1f / fixedDt).ToString("0.#", CultureInfo.InvariantCulture) : "0";
        }

        private static string Num(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
    }

    /// One flag and one end line per episode of the game clock running
    /// ahead of real time (SpeedCheck.ClockAhead), shaped like
    /// TimeScaleWatch: the first frame ahead raises the flag, the first
    /// frame back (or the attempt's end) gives a line with the peak ratio
    /// and the unscaled seconds. Excused frames (not loaded, a savestate
    /// action, a cutscene fast-forward, a time scale above 1 that the
    /// time-scale watch flags) start nothing and leave a running episode
    /// as it was.
    public sealed class ClockWatch
    {
        private bool _active;
        private float _peak;
        private float _seconds;

        /// A new attempt: nothing is remembered.
        public void Reset() { _active = false; _peak = 0f; _seconds = 0f; }

        /// One frame. Returns the flag text on an episode's first frame
        /// (else null); `ended` is the summary when one has just ended.
        public string Frame(float dt, float unscaledDt, float scale, bool excused, out string ended)
        {
            ended = null;
            if (excused) return null;
            if (!SpeedCheck.ClockAhead(dt, unscaledDt))
            {
                ended = Close();
                return null;
            }
            float ratio = dt / unscaledDt;
            if (!_active)
            {
                _active = true;
                _peak = ratio;
                _seconds = unscaledDt;
                return SpeedCheck.ClockFlagText(ratio, scale);
            }
            if (ratio > _peak) _peak = ratio;
            _seconds += unscaledDt;
            return null;
        }

        /// The attempt ends: the summary of an episode still running, else null.
        public string End() { return Close(); }

        private string Close()
        {
            if (!_active) return null;
            _active = false;
            return SpeedCheck.ClockEpisodeText(_peak, _seconds);
        }
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
