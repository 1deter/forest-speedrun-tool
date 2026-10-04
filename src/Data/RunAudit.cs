using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The run audit log (docs/run-audit-and-replays.md part 1; author,
    // 2026-10-03: "a rundown ... a verifier can skim"). Run mode attempts
    // only. Each thing that happened in the run is an `event` line in the
    // attempt log (Data/AttemptChain: real ms, timer, kind, place, plain
    // words), folded into the chain like a move - evidence, never a flag.
    //
    // This file is the pure half, linked into the site and the tests:
    //   - the kinds, their plain-word labels and the groups the attempt
    //     page filters by;
    //   - ItemTally: carried items as merged changes ("+3 Stick, -1 Rock"),
    //     so a run writes a few item lines a minute at most;
    //   - Burst: many of one small thing (trees cut, hits taken) as one line;
    //   - Rundown: the summary first ("2 deaths, 3 caves entered, 41 items
    //     picked up ..."), the timeline behind it.
    //
    // Kinds from the game's own events keep their names (Game/GameEvents,
    // Game/WorldEvents): cave-enter, cave-exit, clothing, passenger,
    // first-input, rope-grab, rope-leave, keycard-door, red-elevator,
    // timmy-pickup, megan-transform, megan-pickup, megan-to-machine,
    // end-crash, end-shutdown, timmy-goodbye, raft-out-of-world,
    // endgame-cutscene. The rest are below.
    // ------------------------------------------------------------------
    public static class RunAudit
    {
        public const string Items = "items";            // "+3 Stick, -1 Rock"
        public const string Death = "death";            // the death's kind and what the game did
        public const string Reload = "reload";          // Reload save on death: the save is back
        public const string RideStart = "ride-start";   // zipline, sled, glider, cliff climb
        public const string RideEnd = "ride-end";
        public const string PauseOpen = "pause-open";
        public const string PauseClose = "pause-close"; // detail: how long it was open
        public const string Built = "built";            // a blueprint finished
        public const string Crafted = "crafted";
        public const string Used = "used";              // eaten / drunk / used from the inventory
        public const string Kill = "kill";              // an enemy killed
        public const string Animal = "animal";          // an animal killed
        public const string Hit = "hit";                // hit by an enemy (a burst)
        public const string Tree = "tree";              // trees cut down (a burst)
        public const string Bomb = "bomb";              // a bomb went off
        public const string Sleep = "sleep";
        public const string Story = "story";            // the game's story progress
        public const string Endgame = "endgame";        // the endgame area entered / left
        public const string Setting = "setting";        // a game setting changed mid-run
        public const string Full = "audit-full";        // the line budget ran out

        /// Lines an attempt writes at most; past it, one `audit-full` line
        /// at the end says how many were left out.
        public const int MaxLines = 3000;

        // --- labels and groups ------------------------------------------------

        public static readonly string[] Groups = { "progress", "caves", "items", "building", "fights", "deaths", "movement", "menu", "world" };

        public static string GroupLabel(string group)
        {
            switch (group)
            {
                case "progress": return "Story and endgame";
                case "caves": return "Caves";
                case "items": return "Items and crafting";
                case "building": return "Building and trees";
                case "fights": return "Fights";
                case "deaths": return "Deaths";
                case "movement": return "Rides, ropes, input";
                case "menu": return "Pause menu";
                default: return "World and settings";
            }
        }

        public static string Group(string kind)
        {
            switch (kind ?? "")
            {
                case "cave-enter": case "cave-exit": return "caves";
                case Items: case Crafted: case Used: case "clothing": return "items";
                case Built: case Tree: return "building";
                case Kill: case Animal: case Hit: case Bomb: return "fights";
                case Death: case Reload: return "deaths";
                case RideStart: case RideEnd: case "rope-grab": case "rope-leave": case "first-input": return "movement";
                case PauseOpen: case PauseClose: return "menu";
                case Story: case Endgame: case "passenger": case "red-elevator": case "endgame-cutscene":
                case "timmy-goodbye": case "raft-out-of-world": return "progress";
            }
            if (kind.StartsWith("keycard-door", StringComparison.Ordinal) || kind.StartsWith("timmy-", StringComparison.Ordinal) ||
                kind.StartsWith("megan-", StringComparison.Ordinal) || kind.StartsWith("end-", StringComparison.Ordinal))
                return "progress";
            return "world";
        }

        public static string Label(string kind)
        {
            switch (kind ?? "")
            {
                case "cave-enter": return "Cave entered";
                case "cave-exit": return "Cave left";
                case "clothing": return "Clothing put on";
                case "passenger": return "Passenger found";
                case "first-input": return "First input";
                case "rope-grab": return "Rope / wall climb started";
                case "rope-leave": return "Rope / wall climb ended";
                case "keycard-door": return "Keycard door";
                case "red-elevator": return "Red elevator";
                case "timmy-pickup": return "Found Timmy";
                case "megan-transform": return "Megan transforms";
                case "megan-pickup": return "Picked Megan up";
                case "megan-to-machine": return "Megan into the artifact";
                case "end-crash": return "Ending: plane crash";
                case "end-shutdown": return "Ending: artifact shut down";
                case "timmy-goodbye": return "Goodbye Timmy";
                case "raft-out-of-world": return "Raft out of the world";
                case "endgame-cutscene": return "Endgame cutscene";
                case Items: return "Items";
                case Death: return "Death";
                case Reload: return "Reload save on death";
                case RideStart: return "Ride started";
                case RideEnd: return "Ride ended";
                case PauseOpen: return "Pause menu opened";
                case PauseClose: return "Pause menu closed";
                case Built: return "Built";
                case Crafted: return "Crafted";
                case Used: return "Used";
                case Kill: return "Enemy killed";
                case Animal: return "Animal killed";
                case Hit: return "Hit by an enemy";
                case Tree: return "Trees cut";
                case Bomb: return "Bomb went off";
                case Sleep: return "Slept";
                case Story: return "Story";
                case Endgame: return "Endgame area";
                case Setting: return "Game setting changed";
                case Full: return "Audit log full";
                default: return kind ?? "";
            }
        }

        // --- the rundown ------------------------------------------------------

        /// The summary a verifier skims first: one line per kind that
        /// happened, the most telling first. Empty for a log without events.
        public static List<string> Rundown(IList<AttemptChain.EventInfo> events)
        {
            List<string> lines = new List<string>();
            if (events == null || events.Count == 0) return lines;

            int deaths = 0, reloads = 0, caves = 0, items = 0, gained = 0, lost = 0, crafted = 0, used = 0, built = 0;
            int trees = 0, kills = 0, animals = 0, hits = 0, bombs = 0, rides = 0, ropes = 0, pauses = 0, sleeps = 0;
            int passengers = 0, clothing = 0, skipped = 0;
            long pausedMs = 0, openAt = -1;
            List<string> settings = new List<string>();
            List<string> progress = new List<string>();
            List<string> caveNames = new List<string>();
            List<string> rideNames = new List<string>();
            for (int i = 0; i < events.Count; i++)
            {
                AttemptChain.EventInfo e = events[i];
                switch (e.Kind)
                {
                    case Death: deaths++; break;
                    case Reload: reloads++; break;
                    case "cave-enter": caves++; AddOnce(caveNames, e.Detail); break;
                    case Items:
                    {
                        int g, l;
                        ItemDeltas(e.Detail, out g, out l);
                        gained += g; lost += l; items++;
                        break;
                    }
                    case Crafted: crafted++; break;
                    case Used: used++; break;
                    case Built: built++; break;
                    case Tree: trees += LeadingCount(e.Detail); break;
                    case Kill: kills++; break;
                    case Animal: animals++; break;
                    case Hit: hits += LeadingCount(e.Detail); break;
                    case Bomb: bombs++; break;
                    case RideStart: rides++; AddOnce(rideNames, e.Detail); break;
                    case "rope-grab": ropes++; break;
                    case PauseOpen: pauses++; openAt = e.RealMs; break;
                    case PauseClose: if (openAt >= 0) { pausedMs += e.RealMs - openAt; openAt = -1; } break;
                    case Sleep: sleeps++; break;
                    case "passenger": passengers++; break;
                    case "clothing": clothing++; break;
                    case Setting: settings.Add(Clock(e.RealMs) + " " + e.Detail); break;
                    case Full: skipped += LeadingCount(e.Detail); break;
                    default:
                        if (Group(e.Kind) == "progress" && e.Kind != "endgame-cutscene" && e.Kind != Endgame)
                            progress.Add((e.Kind == Story && !string.IsNullOrEmpty(e.Detail) ? e.Detail : Label(e.Kind)) + " " + Clock(e.RealMs));
                        break;
                }
            }

            if (deaths > 0)
                lines.Add(Count(deaths, "death", "deaths") + (reloads > 0 ? " (" + Count(reloads, "Reload save on death", "Reloads save on death") + ")" : ""));
            if (settings.Count > 0) lines.Add("Game settings changed during the run: " + string.Join("; ", settings.ToArray()));
            if (progress.Count > 0) lines.Add("Progress: " + string.Join(", ", progress.ToArray()));
            if (passengers > 0) lines.Add(Count(passengers, "passenger found", "passengers found"));
            if (caves > 0)
                lines.Add(Count(caves, "cave entry", "cave entries") + ": " + Join(caveNames, 6));
            if (items > 0)
            {
                string g = gained > 0 ? Count(gained, "item gained", "items gained") : "";
                string l = lost > 0 ? Count(lost, "item used or lost", "items used or lost") : "";
                lines.Add(g + (g.Length > 0 && l.Length > 0 ? ", " : "") + l + " (" + Count(items, "change", "changes") + ")");
            }
            if (crafted > 0) lines.Add(Count(crafted, "thing crafted", "things crafted"));
            if (used > 0) lines.Add(Count(used, "item eaten or used", "items eaten or used"));
            if (clothing > 0) lines.Add(Count(clothing, "piece of clothing put on", "pieces of clothing put on"));
            if (built > 0) lines.Add(Count(built, "structure built", "structures built"));
            if (trees > 0) lines.Add(Count(trees, "tree cut down", "trees cut down"));
            if (kills > 0) lines.Add(Count(kills, "enemy killed", "enemies killed"));
            if (animals > 0) lines.Add(Count(animals, "animal killed", "animals killed"));
            if (hits > 0) lines.Add(Count(hits, "hit taken from enemies", "hits taken from enemies"));
            if (bombs > 0) lines.Add(Count(bombs, "bomb went off", "bombs went off"));
            if (rides > 0) lines.Add(Count(rides, "ride", "rides") + ": " + Join(rideNames, 4));
            if (ropes > 0) lines.Add(Count(ropes, "rope / wall climb", "rope / wall climbs"));
            if (pauses > 0)
                lines.Add("Pause menu opened " + Count(pauses, "time", "times") +
                          (pausedMs > 0 ? ", " + Seconds(pausedMs) + " in all" : ""));
            if (sleeps > 0) lines.Add("Slept " + Count(sleeps, "time", "times"));
            if (skipped > 0) lines.Add("The audit log was full: " + Count(skipped, "later event was", "later events were") + " not written");
            return lines;
        }

        // --- items ------------------------------------------------------------

        /// What an `items` line adds up to: "+3 Stick, -1 Rock" -> 3, 1.
        public static void ItemDeltas(string detail, out int gained, out int lost)
        {
            gained = 0; lost = 0;
            if (string.IsNullOrEmpty(detail)) return;
            string[] parts = detail.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length < 2 || (p[0] != '+' && p[0] != '-')) continue;
                int end = 1;
                while (end < p.Length && char.IsDigit(p[end])) end++;
                int n;
                if (end == 1 || !int.TryParse(p.Substring(1, end - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) continue;
                if (p[0] == '+') gained += n; else lost += n;
            }
        }

        /// Carried items as merged changes. Update with the bag's counts when
        /// they may have moved; Take returns a line's detail once the bag has
        /// been quiet for QuietMs (or changes kept coming for MaxHoldMs), ""
        /// otherwise. A pick-up and a drop inside one window cancel out.
        public sealed class ItemTally
        {
            public const long QuietMs = 2000;
            public const long MaxHoldMs = 10000;

            private readonly Dictionary<string, int> _last = new Dictionary<string, int>();
            private readonly Dictionary<string, int> _pending = new Dictionary<string, int>();
            private readonly List<string> _order = new List<string>();
            private readonly List<string> _gone = new List<string>();
            private bool _hasBaseline;
            private long _firstMs = -1, _lastMs;

            public bool HasBaseline { get { return _hasBaseline; } }

            /// Forget everything: the next Update is the baseline (a load).
            public void Reset()
            {
                _last.Clear(); _pending.Clear(); _order.Clear();
                _hasBaseline = false; _firstMs = -1;
            }

            /// The bag now. The first call after Reset only sets the baseline.
            public void Update(Dictionary<string, int> now, long ms)
            {
                if (!_hasBaseline)
                {
                    _last.Clear();
                    foreach (KeyValuePair<string, int> kv in now) _last[kv.Key] = kv.Value;
                    _hasBaseline = true;
                    return;
                }
                foreach (KeyValuePair<string, int> kv in now)
                {
                    int was;
                    _last.TryGetValue(kv.Key, out was);
                    if (kv.Value != was) Note(kv.Key, kv.Value - was, ms);
                }
                _gone.Clear();
                foreach (KeyValuePair<string, int> kv in _last)
                    if (!now.ContainsKey(kv.Key) && kv.Value != 0) _gone.Add(kv.Key);
                for (int i = 0; i < _gone.Count; i++) Note(_gone[i], -_last[_gone[i]], ms);
                _last.Clear();
                foreach (KeyValuePair<string, int> kv in now) _last[kv.Key] = kv.Value;
            }

            private void Note(string name, int delta, long ms)
            {
                int d;
                _pending.TryGetValue(name, out d);
                _pending[name] = d + delta;
                if (!_order.Contains(name)) _order.Add(name);
                if (_firstMs < 0) _firstMs = ms;
                _lastMs = ms;
            }

            /// The merged line's detail when it is due (or `force`), else "".
            public string Take(long ms, bool force)
            {
                if (_firstMs < 0) return "";
                if (!force && ms - _lastMs < QuietMs && ms - _firstMs < MaxHoldMs) return "";
                StringBuilder sb = new StringBuilder(64);
                for (int i = 0; i < _order.Count; i++)
                {
                    int d = _pending[_order[i]];
                    if (d == 0) continue;
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(d > 0 ? "+" : "-").Append(Math.Abs(d).ToString(CultureInfo.InvariantCulture)).Append(' ')
                      .Append(_order[i].Replace(',', ' '));
                }
                _pending.Clear();
                _order.Clear();
                _firstMs = -1;
                return sb.ToString();
            }

            /// When the pending line started (its time in the log), -1 = none.
            public long PendingSince { get { return _firstMs; } }
        }

        // --- bursts -----------------------------------------------------------

        /// Many of one small thing as one line: "3 trees cut down", "4 hits
        /// (cannibal, creepy)". Kept open while they come within WindowMs of
        /// each other; the line's time and place are the first one's.
        public sealed class Burst
        {
            public const long WindowMs = 5000;

            public readonly string Kind;
            private readonly string _one, _many;
            private readonly List<string> _whats = new List<string>();
            public int Count;
            public long FirstMs = -1, LastMs, TimerMs;
            public bool HasPos;
            public float X, Y, Z;

            public Burst(string kind, string one, string many)
            {
                Kind = kind; _one = one; _many = many;
            }

            /// True when a burst is open and the next one at `ms` would start
            /// a new burst - Take(force) first.
            public bool Closes(long ms) { return Count > 0 && ms - LastMs > WindowMs; }

            public void Add(long ms, long timerMs, bool hasPos, float x, float y, float z, string what)
            {
                if (Count == 0)
                {
                    FirstMs = ms; TimerMs = timerMs; HasPos = hasPos; X = x; Y = y; Z = z;
                    _whats.Clear();
                }
                Count++;
                LastMs = ms;
                if (!string.IsNullOrEmpty(what) && _whats.Count < 4 && !_whats.Contains(what)) _whats.Add(what);
            }

            /// The line's detail once the burst is over (or `force`), else "".
            public string Take(long ms, bool force)
            {
                if (Count == 0 || (!force && ms - LastMs <= WindowMs)) return "";
                string s = Count.ToString(CultureInfo.InvariantCulture) + " " + (Count == 1 ? _one : _many) +
                           (_whats.Count > 0 ? " (" + string.Join(", ", _whats.ToArray()) + ")" : "");
                Count = 0;
                return s;
            }
        }

        // --- helpers ----------------------------------------------------------

        /// The number a detail starts with ("3 trees cut down" -> 3); 1 if none.
        public static int LeadingCount(string detail)
        {
            if (string.IsNullOrEmpty(detail)) return 1;
            int end = 0;
            while (end < detail.Length && char.IsDigit(detail[end])) end++;
            int n;
            return end > 0 && int.TryParse(detail.Substring(0, end), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 1;
        }

        /// ms -> "1:23" / "1:02:03".
        public static string Clock(long ms)
        {
            long s = Math.Max(0, ms) / 1000;
            return s >= 3600
                ? (s / 3600).ToString(CultureInfo.InvariantCulture) + ":" + (s / 60 % 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture)
                : (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        private static string Seconds(long ms)
        {
            return ms >= 60000 ? Clock(ms) : (ms / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " s";
        }

        private static string Count(int n, string one, string many)
        {
            return n.ToString(CultureInfo.InvariantCulture) + " " + (n == 1 ? one : many);
        }

        private static void AddOnce(List<string> list, string s)
        {
            if (!string.IsNullOrEmpty(s) && !list.Contains(s)) list.Add(s);
        }

        private static string Join(List<string> list, int max)
        {
            if (list.Count <= max) return string.Join(", ", list.ToArray());
            return string.Join(", ", list.GetRange(0, max).ToArray()) + " and " + (list.Count - max).ToString(CultureInfo.InvariantCulture) + " more";
        }
    }
}
