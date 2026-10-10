using System;
using System.Globalization;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Every value the HUD can show (the column at the top left, or placed
    // on its own - Core/HudWidgets), for the Settings tab's
    // HUD section (runner request, docs/backlog.md *HUD*: "more control
    // over the top-left HUD, less clutter").
    //
    // A line is known by the module that writes it and its label (the
    // first argument of HudBuilder.Pair), so no module has to change for
    // its lines to become switchable. A line not listed here is always
    // shown. Locked lines are the honest labelling (CLAUDE.md *Project
    // intent*): the practice marker, ON NOW and run mode's code - they
    // are listed with why, never with a tick box.
    //
    // Pure: linked into the tests.
    // ------------------------------------------------------------------
    public sealed class HudLine
    {
        /// Module id that writes the line ("" for the plugin itself).
        public readonly string Module;
        /// HudBuilder.Pair label; null = every line of that module.
        public readonly string Label;
        /// Config key under [HUD]; null for locked / externally switched lines.
        public readonly string ConfigKey;
        public readonly string Name;
        public readonly string Description;
        public readonly bool DefaultOn;
        /// Honest labelling: always shown, no tick box.
        public readonly bool Locked;
        /// Switched by its own module's setting (the 100% tab's), not by [HUD].
        public readonly bool External;

        public HudLine(string module, string label, string configKey, string name, string description,
                       bool defaultOn, bool locked, bool external)
        {
            Module = module;
            Label = label;
            ConfigKey = configKey;
            Name = name;
            Description = description;
            DefaultOn = defaultOn;
            Locked = locked;
            External = external;
        }

        /// Has a [HUD] config switch of its own.
        public bool Switchable { get { return !Locked && !External; } }
    }

    public static class HudLines
    {
        public const string PluginModule = "";
        public const string TitleLabel = "title";
        public const string CollectiblesModule = "collectibles";

        private static HudLine L(string module, string label, string key, string name, string description)
        {
            return new HudLine(module, label, key, name, description, true, false, false);
        }

        private static HudLine Always(string name, string description)
        {
            return new HudLine(PluginModule, null, null, name, description, true, true, false);
        }

        /// Settings order (roughly top to bottom on the HUD).
        public static readonly HudLine[] All =
        {
            L(PluginModule, TitleLabel, "ShowTitle", "Title",
              "The box's title: Forest Overlay and its version."),
            L("update", "Update", "ShowUpdate", "Update check",
              "Whether a newer version is out (or that the check worked)."),
            L("runinfo", "Speed", "ShowSpeed", "Speed",
              "Horizontal speed (u/s)."),
            // One value per line (author, 2026-10-05: no "(tot x)" inside the speed).
            new HudLine("runinfo", "Total", "ShowTotalSpeed", "Total speed",
              "Speed including falling and jumping (u/s).", false, false, false),
            L("runinfo", "Vel", "ShowVelocity", "Velocity",
              "Velocity along x, y and z."),
            L("runinfo", "Pos", "ShowPosition", "Position",
              "Where you stand (x, y, z)."),
            L("runinfo", "Lock", "ShowLockWarning", "Player hold warning",
              "Only when the window cannot hold the player - says why."),
            L("inventory", "Items", "ShowItems", "Items carried",
              "How many items you carry, and in how many stacks."),
            L("inventory", "Logs", "ShowLogs", "Logs",
              "Logs carried / cap, while logs in the inventory is on."),
            L("inventory", "", "ShowPinnedItems", "Pinned items",
              "The items pinned in the Inventory tab, one line each."),
            new HudLine(CollectiblesModule, null, null, "100% totals",
              "Items, nature, tasks and passengers found - the same switch as the 100% tab's.",
              false, false, true),
            L("debugview", "Cam", "ShowFreecam", "Freecam",
              "FREECAM while the free camera is on."),
            L("debugview", "Flight", "ShowTrajectory", "Trajectory preview",
              "Where you come down (distance, time, speed) while the trajectory preview (Developer tab, Experimental) is on."),
            L("debugview", "Boost", "ShowBoostPreview", "Bomb boost preview",
              "The piled-up speed and distance in the pause menu during a knockback, while the trajectory preview is on."),
            L("dumps", "Dump", "ShowDumps", "Dump status",
              "What the F11 dumps are doing."),
            L("practice", "Spot", "ShowSpot", "Current spot",
              "The spot F7 restarts."),
            L("practice", "Prac", "ShowPracticeStatus", "Practice messages",
              "The last practice action's result (teleport, restart, save)."),
            L("practicerun", "Run", "ShowRunTimer", "Run timer",
              "The timed segment: timer and delta, or armed / attempts and best."),
            // Off until ticked (added after the HUD settings: the old look stays).
            new HudLine("practicerun", "LRT", "ShowLoadRemoved", "Load-removed time",
              "The timer with the game's loads taken out (LRT), while a run is on; the last run's after it.",
              false, false, false),
            L("practicerun", "Next", "ShowNextSplit", "Next split",
              "The next checkpoint while a run is on."),
            L("practicerun", "Last", "ShowLastTime", "Last time",
              "Your previous time on the segment."),
            Always("ON NOW",
              "Always shown while on: what changes the game right now (god mode, item caps...), one orange name each. A recording must show it."),
            Always("Practice marker",
              "Always shown once a practice tool was used: its name, orange (nothing while clean); or the run line. A recording must show it."),
            Always("Run code",
              "Always shown during a run, even with every overlay hidden (run mode's anti-splice code)."),
        };

        /// Index of the line a module's Pair writes, or -1 (not listed:
        /// always shown).
        public static int Find(string module, string label)
        {
            if (module == null) return -1;
            for (int i = 0; i < All.Length; i++)
            {
                HudLine l = All[i];
                if (l.Locked || !string.Equals(l.Module, module, StringComparison.Ordinal)) continue;
                if (l.Label == null || string.Equals(l.Label, label ?? "", StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        public static int IndexOfKey(string configKey)
        {
            for (int i = 0; i < All.Length; i++)
                if (string.Equals(All[i].ConfigKey, configKey, StringComparison.Ordinal)) return i;
            return -1;
        }

        // --- formatting -------------------------------------------------

        /// One HUD line. Normal: the label padded to a column (today's
        /// look). Compact: the label and the value with one space, and
        /// a label-less line (pinned items) without its indent.
        public static string Pair(string label, string value, bool compact)
        {
            if (label == null) label = "";
            if (value == null) value = "";
            if (!compact) return label.PadRight(7) + value;
            if (label.Length == 0) return value.Trim();
            return label + " " + value.Trim();
        }

        public static string Title(string version, bool compact)
        {
            return compact ? "FO v" + version : "Forest Overlay v" + version;
        }

        public static string Speed(float speed, bool compact)
        {
            return F(speed, 2);   // the number only (author, 2026-10-05: no units on a run screen)
        }

        public static string Vector(float x, float y, float z, int decimals)
        {
            return F(x, decimals) + ", " + F(y, decimals) + ", " + F(z, decimals);
        }

        public static string Items(int total, int stacks, bool compact)
        {
            string t = total.ToString(CultureInfo.InvariantCulture);
            return compact ? t : t + "   (" + stacks.ToString(CultureInfo.InvariantCulture) + " stacks)";
        }

        /// The HUD's practice flag: a Mark reason cut to the tool's short
        /// name (author, 2026-10-10: "test bridge: screenshot" -> "test
        /// bridge", "savestate restore (load)" -> "savestate restore").
        public static string ShortReason(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return "";
            int cut = reason.Length;
            int colon = reason.IndexOf(':');
            if (colon > 0) cut = colon;
            int paren = reason.IndexOf(" (", StringComparison.Ordinal);
            if (paren > 0 && paren < cut) cut = paren;
            return reason.Substring(0, cut).Trim();
        }

        /// The update check's message; compact drops the version an
        /// up-to-date install already shows in the title.
        public static string Update(string message, bool compact)
        {
            if (message == null) return "";
            if (compact && message.StartsWith("up to date", StringComparison.Ordinal)) return "up to date";
            return message;
        }

        // The format strings themselves, made once ("F" + n was two
        // strings per number, ten times a second per HUD value).
        private static readonly string[] Formats = { "F0", "F1", "F2", "F3", "F4", "F5", "F6" };

        private static string F(float v, int decimals)
        {
            string format = decimals >= 0 && decimals < Formats.Length ? Formats[decimals]
                          : "F" + decimals.ToString(CultureInfo.InvariantCulture);
            return v.ToString(format, CultureInfo.InvariantCulture);
        }

        // --- size and place ---------------------------------------------

        /// Text sizes offered, in px; 0 = the game skin's own (today's look).
        public static readonly int[] TextSizes = { 0, 10, 11, 12, 14, 16, 18, 20, 24 };

        /// The size the skin's default renders at, for scaling the box.
        public const int DefaultTextPx = 13;
        public const float DefaultWidth = 330f;
        public const float DefaultLineHeight = 18f;

        /// The next offered size up (+1) or down (-1); an unknown saved
        /// value snaps to the nearest offered one first.
        public static int StepTextSize(int current, int direction)
        {
            int at = NearestSize(current);
            int next = at + Math.Sign(direction);
            if (next < 0) next = 0;
            if (next >= TextSizes.Length) next = TextSizes.Length - 1;
            return TextSizes[next];
        }

        public static int NearestSize(int px)
        {
            if (px <= 0) return 0;
            int best = 1;
            for (int i = 1; i < TextSizes.Length; i++)
                if (Math.Abs(TextSizes[i] - px) < Math.Abs(TextSizes[best] - px)) best = i;
            return best;
        }

        public static float LineHeight(int textPx)
        {
            return textPx <= 0 ? DefaultLineHeight : (float)Math.Round(DefaultLineHeight * textPx / DefaultTextPx);
        }

        /// The box widens / narrows with the text, never past the screen.
        public static float Width(int textPx, float screenWidth)
        {
            float w = textPx <= 0 ? DefaultWidth : (float)Math.Round(DefaultWidth * textPx / DefaultTextPx);
            float max = Math.Max(160f, screenWidth - 20f);
            return Math.Min(w, max);
        }

        /// Keeps the box on screen. Applied to live drag input as well as
        /// to the saved value, so no position can read as something else
        /// (gotcha 61).
        public static float Clamp(float v, float size, float screen)
        {
            float max = Math.Max(0f, screen - size);
            if (float.IsNaN(v) || v < 0f) return 0f;
            return v > max ? max : v;
        }

        public static string SizeText(int px)
        {
            return px <= 0 ? "default" : px.ToString(CultureInfo.InvariantCulture) + " px";
        }
    }
}
