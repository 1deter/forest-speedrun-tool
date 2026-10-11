using System;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The hotkey list in groups (T-0263, author 2026-10-09: "group each
    // list under short headings, and move the developer-only keys (dumps,
    // type explorer, TAS, QA) to the Developer tab"). Settings > Keys shows
    // the runner's groups in Order under their headings; the Developer
    // group is drawn in the Developer tab's Keys fold instead.
    //
    // A key is grouped by its hotkey id (HotkeyMap.Add's first argument),
    // so no module changes to be grouped. An id no rule knows lands in
    // Other, shown last - a new key is never hidden.
    //
    // Pure: linked into the tests.
    // ------------------------------------------------------------------
    public static class KeyGroups
    {
        public const int Practice = 0;
        public const int Runs = 1;
        public const int Window = 2;
        public const int Other = 3;
        public const int Developer = 4;

        /// Headings by group (proposed words, tasks/notes/T-0263.md).
        public static readonly string[] Headings =
        {
            "Practice",
            "Runs and timer",
            "Window and tabs",
            "Other",
            "Developer",
        };

        /// The groups Settings > Keys shows, top to bottom: what a runner
        /// presses first, the window's openers (mostly unbound) after.
        public static readonly int[] Order = { Practice, Runs, Window, Other };

        // Exact ids before prefixes: the first match wins.
        private static readonly string[] ExactIds =
        {
            "dump.write", "panel.explorer", "tab.qa",
            "run.toggleMode", "run.restartCheckpoint",
            "panel.main",
            "debug.trajectory",
        };
        private static readonly int[] ExactGroups =
        {
            Developer, Developer, Developer,
            Practice, Practice,
            Window,
            Developer,   // the trajectory preview is Experimental (author, 2026-10-11)
        };
        private static readonly string[] Prefixes = { "tas.", "qa.", "practice.", "debug.", "run.", "timer.", "tab.", "ui." };
        private static readonly int[] PrefixGroups = { Developer, Developer, Practice, Practice, Runs, Runs, Window, Window };

        public static int Of(string id)
        {
            if (string.IsNullOrEmpty(id)) return Other;
            for (int i = 0; i < ExactIds.Length; i++)
                if (string.Equals(id, ExactIds[i], StringComparison.Ordinal)) return ExactGroups[i];
            for (int i = 0; i < Prefixes.Length; i++)
                if (id.StartsWith(Prefixes[i], StringComparison.Ordinal)) return PrefixGroups[i];
            return Other;
        }

        public static bool IsDeveloper(string id)
        {
            return Of(id) == Developer;
        }
    }
}
