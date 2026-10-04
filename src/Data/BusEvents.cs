using System;
using System.Collections.Generic;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Segment event names for what the game tells its own event bus
    // (EventRegistry.Publish, caught once in Game/AuditWatch - the same
    // postfix the run audit log reads) and for the rides
    // (Game/WorldEvents polls RideModes after an enter / exit).
    //
    // Every event that carries a readable id fires twice in one frame:
    // the specific name first (`crafted-bomb-timed`, `built-log-cabin`,
    // `kill-deer`, `zipline-start`), then the general one (`crafted`,
    // `built`, `kill-animal`, `ride-start`) as its companion - one
    // occurrence, so it moves a run on at most once
    // (Data/SplitSequence.OccurrenceGate).
    //
    // The general names that the run audit log also writes are the same
    // strings as its kinds (Data/RunAudit: built, crafted, used, bomb,
    // story). The specific part is the game's own name made readable:
    // the BuildingTypes / StoryElements enum value or the ItemDatabase
    // name ("BombTimed" -> "bomb-timed"); a number when the name is
    // not known.
    //
    // Names are ids in segment files - renaming one breaks every segment
    // using it.
    //
    // Pure: tested in BusEventsTests.
    // ------------------------------------------------------------------
    public static class BusEvents
    {
        public const string Built = "built";                  // + built-<structure>
        public const string Crafted = "crafted";              // + crafted-<item>
        public const string Used = "used";                    // + used-<item>: eaten / drunk
        public const string KillEnemy = "kill-enemy";
        public const string KillAnimal = "kill-animal";       // + kill-<animal>
        public const string HitByEnemy = "hit-by-enemy";
        public const string TreeCut = "tree-cut";
        public const string Bomb = "bomb";
        public const string Slept = "slept";
        public const string Story = "story";                  // + story-<element>
        public const string EndgameEnter = "endgame-area-enter";
        public const string EndgameLeave = "endgame-area-leave";
        public const string RideStart = "ride-start";         // + <ride>-start
        public const string RideEnd = "ride-end";             // + <ride>-end

        private const string Kill = "kill";

        /// The animals the bus names (TfEvent.Killed<Animal>), in its order.
        public static readonly string[] Animals = { "rabbit", "lizard", "raccoon", "deer", "turtle", "bird", "shark" };

        /// RideModes.Current()'s values.
        public static readonly string[] Rides = { "zipline", "sled", "glider", "cliff climb" };

        // --- names ------------------------------------------------------------

        /// "LogCabinMed" -> "log-cabin-med", "RoofOLD" -> "roof-old",
        /// "cliff climb" -> "cliff-climb", "Keycard2" -> "keycard2".
        /// Letters and digits only, words split at case changes, lower
        /// case; "" for nothing usable.
        public static string Slug(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            StringBuilder sb = new StringBuilder(raw.Length + 8);
            bool dash = false;
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (!char.IsLetterOrDigit(c) || c > 127) { dash = sb.Length > 0; continue; }
                if (char.IsUpper(c) && sb.Length > 0 && i > 0)
                {
                    char prev = raw[i - 1];
                    bool nextLower = i + 1 < raw.Length && char.IsLower(raw[i + 1]);
                    // aB -> a-b; ABc -> a-bc (an acronym ends); 2B -> 2-b
                    if (char.IsLower(prev) || char.IsDigit(prev) || (char.IsUpper(prev) && nextLower)) dash = true;
                }
                if (dash) { sb.Append('-'); dash = false; }
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        /// "crafted" + "BombTimed" -> "crafted-bomb-timed"; null when the
        /// raw part has nothing usable.
        public static string Specific(string general, string raw)
        {
            string s = Slug(raw);
            return s.Length == 0 ? null : general + "-" + s;
        }

        /// An item's event: its database name, else its id.
        public static string ItemEvent(string general, int id, string name)
        {
            return Specific(general, name) ?? general + "-" + id;
        }

        /// "deer" -> "kill-deer".
        public static string KillOf(string animal)
        {
            return Specific(Kill, animal);
        }

        /// "cliff climb", true -> "cliff-climb-start".
        public static string RideEvent(string ride, bool start)
        {
            string s = Slug(ride);
            return s.Length == 0 ? null : s + (start ? "-start" : "-end");
        }

        // --- the editor's picker ------------------------------------------------

        /// The general name, then each raw name's event (sorted, no
        /// repeats, nothing unusable), then the extras.
        public static string[] Group(string general, IEnumerable<string> raws, params string[] extras)
        {
            List<string> specific = new List<string>();
            if (raws != null)
                foreach (string raw in raws)
                {
                    string e = Specific(general, raw);
                    if (e != null && !specific.Contains(e)) specific.Add(e);
                }
            specific.Sort(StringComparer.Ordinal);
            List<string> all = new List<string>(specific.Count + 1 + (extras != null ? extras.Length : 0));
            all.Add(general);
            all.AddRange(specific);
            if (extras != null) all.AddRange(extras);
            return all.ToArray();
        }

        /// kill-enemy, kill-animal, kill-<animal>..., hit-by-enemy, bomb.
        public static string[] FightEvents()
        {
            List<string> all = new List<string>();
            all.Add(KillEnemy);
            all.Add(KillAnimal);
            for (int i = 0; i < Animals.Length; i++) all.Add(KillOf(Animals[i]));
            all.Add(HitByEnemy);
            all.Add(Bomb);
            return all.ToArray();
        }

        /// ride-start, ride-end, then each ride's start and end.
        public static string[] RideEvents()
        {
            List<string> all = new List<string>();
            all.Add(RideStart);
            all.Add(RideEnd);
            for (int i = 0; i < Rides.Length; i++)
            {
                all.Add(RideEvent(Rides[i], true));
                all.Add(RideEvent(Rides[i], false));
            }
            return all.ToArray();
        }

        public const string GroupFights = "fights";
        public const string GroupRides = "rides";

        /// Which picker group a typed name belongs to: Built (with
        /// tree-cut), Crafted, Used, Story (with slept), GroupFights,
        /// GroupRides, or null. Case ignored; allocates nothing (the editor
        /// asks while drawing).
        public static string PrefixGroup(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (Is(name, TreeCut) || IsOrUnder(name, Built)) return Built;
            if (IsOrUnder(name, Crafted)) return Crafted;
            if (IsOrUnder(name, Used)) return Used;
            if (Is(name, Slept) || IsOrUnder(name, Story)) return Story;
            if (Is(name, HitByEnemy) || Is(name, Bomb) || Under(name, Kill)) return GroupFights;
            if (Is(name, RideStart) || Is(name, RideEnd)) return GroupRides;
            for (int i = 0; i < RideNames.Length; i++)
                if (Is(name, RideNames[i])) return GroupRides;
            return null;
        }

        private static bool Is(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static bool Under(string a, string general)
        {
            return a.Length > general.Length + 1 && a[general.Length] == '-' &&
                   string.Compare(a, 0, general, 0, general.Length, StringComparison.OrdinalIgnoreCase) == 0;
        }

        private static bool IsOrUnder(string a, string general)
        {
            return Is(a, general) || Under(a, general);
        }

        // zipline-start, zipline-end, sled-start, ... (after Rides).
        private static readonly string[] RideNames = BuildRideNames();

        private static string[] BuildRideNames()
        {
            string[] all = new string[Rides.Length * 2];
            for (int i = 0; i < Rides.Length; i++)
            {
                all[i * 2] = RideEvent(Rides[i], true);
                all[i * 2 + 1] = RideEvent(Rides[i], false);
            }
            return all;
        }

        // "cliff climb" for "cliff-climb-start" / "-end"; null otherwise.
        private static string RideOf(string e)
        {
            for (int i = 0; i < RideNames.Length; i++)
                if (Is(e, RideNames[i])) return Rides[i / 2];
            return null;
        }

        // --- labels -------------------------------------------------------------

        /// What the editor shows under a name; null when it is not one of these.
        public static string LabelFor(string evt)
        {
            if (string.IsNullOrEmpty(evt)) return null;
            string e = evt.ToLowerInvariant();
            switch (e)
            {
                case Built: return "Built anything (a blueprint finished)";
                case Crafted: return "Crafted anything (the crafting cog)";
                case Used: return "Ate or drank anything (from the inventory or the world)";
                case KillEnemy: return "Killed an enemy (cannibal or mutant)";
                case KillAnimal: return "Killed any animal";
                case HitByEnemy: return "Hit by an enemy";
                case TreeCut: return "A tree came down (chopped or blown up)";
                case Bomb: return "A bomb went off";
                case Slept: return "Slept";
                case Story: return "Story progress (any: the hanging, the yacht, the climbing wall, Timmy, Megan)";
                case EndgameEnter: return "Entered the endgame area (the game's own event)";
                case EndgameLeave: return "Left the endgame area (the game's own event)";
                case RideStart: return "Got on any ride (zipline, sled, glider, cliff climb)";
                case RideEnd: return "Got off any ride (zipline, sled, glider, cliff climb)";
            }

            string rest;
            if ((rest = After(e, Built)) != null) return "Built: " + Words(rest);
            if ((rest = After(e, Crafted)) != null) return "Crafted: " + Words(rest);
            if ((rest = After(e, Used)) != null) return "Ate or drank: " + Words(rest);
            if ((rest = After(e, Story)) != null) return "Story: " + Words(rest);
            for (int i = 0; i < Animals.Length; i++)
                if (e == KillOf(Animals[i])) return "Killed a " + Animals[i];

            string ride = RideOf(e);
            if (ride != null)
                return (e.EndsWith("-start", StringComparison.Ordinal) ? "Got on: " : "Got off: ") + ride;
            return null;
        }

        private static string After(string e, string general)
        {
            return e.Length > general.Length + 1 && e.StartsWith(general + "-", StringComparison.Ordinal)
                ? e.Substring(general.Length + 1) : null;
        }

        private static string Words(string slug)
        {
            return slug.Replace('-', ' ');
        }
    }
}
