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
            return "the game's speed was changed (time scale " + scale.ToString("0.###", CultureInfo.InvariantCulture) + ")";
        }
    }
}
