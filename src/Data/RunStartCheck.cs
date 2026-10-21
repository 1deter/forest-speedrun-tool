namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // How an attempt started, in run mode's words (T-0414): the start state
    // a run spot really loaded against the one it names (F4), and a run
    // started by hand - not from a category spot, flagged when practice
    // came before it (author, 2026-10-21, D3).
    // ------------------------------------------------------------------
    public static class RunStartCheck
    {
        /// The flag text when the loaded start state is not the one the
        /// spot names; null when they match or the spot names none.
        /// `loaded` is the SHA-256, `loadedLegacy` the old FNV-1a (T-0414
        /// D4), read only for a spot that names one that way.
        public static string StartStateFlag(string loaded, string loadedLegacy, string named)
        {
            if (string.IsNullOrEmpty(named) || loaded == named) return null;
            bool legacy = Segment.IsLegacyStateHash(named);
            if (legacy && loadedLegacy == named) return null;
            string shown = legacy ? loadedLegacy : loaded;
            return "the run spot's start state is not the one the spot names (loaded " +
                   (string.IsNullOrEmpty(shown) ? "none" : shown) + ", the spot names " + named + ")";
        }

        /// What the HUD's run line says after the label for a run started
        /// by hand: it did not start from a category spot.
        public const string NotFromSpot = "not from a category spot";

        /// The HUD label of a run started by hand: "Any%, not from a category spot".
        public static string ByHandLabel(string label)
        {
            return string.IsNullOrEmpty(label) ? NotFromSpot : label + ", " + NotFromSpot;
        }

        /// The flag text for Start run mode by hand when practice was used
        /// before it (`practice` = the last practice action); null when not.
        public static string ByHandAfterPracticeFlag(string practice)
        {
            if (string.IsNullOrEmpty(practice)) return null;
            return "run mode was started by hand after practice (" + practice + "), " + NotFromSpot;
        }
    }
}
