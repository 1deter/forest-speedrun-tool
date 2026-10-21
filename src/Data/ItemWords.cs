using System;

namespace ForestOverlay.Data
{
    /// Runner words for the few items the game names with a code name
    /// (T-0407, author 2026-10-21; 'Axe Plane' kept). Display only:
    /// saved label settings and run data keep the game's name.
    public static class ItemWords
    {
        public static string Say(string gameName)
        {
            if (gameName == null) return "";
            if (Eq(gameName, "SurvivalBook")) return "Survival book";
            if (Eq(gameName, "WalkyTalky")) return "Walkie talkie";
            return gameName;
        }

        private static bool Eq(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }
}
