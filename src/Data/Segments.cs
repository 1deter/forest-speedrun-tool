using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ForestOverlay.Data
{
    public enum TriggerKind
    {
        None,
        Zone,     // inside a sphere
        Item,     // inventory count comparison
        Event,    // a named game event (hooked in-process)
        Manual    // fired by hotkey
    }

    public enum Comparison { AtLeast, AtMost, Exactly }

    /// A zone is a sphere by default. Boxes exist because doorways,
    /// ledges and corridors are not round, and forcing a sphere onto
    /// one either over-covers the approach or misses the edges.
    /// Polygons (prisms over an outline of x / z points) cover what
    /// neither does: a bend in a path, a ledge, a cave's irregular
    /// mouth. Appended last so the existing values keep their numbers.
    public enum ZoneShape { Sphere, Box, Polygon }

    // ------------------------------------------------------------------
    // A named condition that can fire.
    //
    // This is the concept everything else was missing. Splits, segment
    // start/end, checkpoints and eventually leaderboard keys are all "a
    // trigger fired" - so it is defined once, here, rather than being
    // reinvented per feature.
    //
    // Triggers are EDGE-based: a zone trigger fires when you enter it, not
    // every frame you stand in it. TriggerState holds that edge.
    // ------------------------------------------------------------------
    public struct Trigger
    {
        public TriggerKind Kind;

        // Zone
        public Vector3 Position;
        public float Radius;          // sphere
        public ZoneShape Shape;
        public Vector3 Extents;       // box half-size
        /// Box turn about the vertical, degrees (0 = axis-aligned): its
        /// depth runs along this heading, as a player facing it looks
        /// (runners: checkpoint boxes across diagonal paths, v0.24.75).
        public float Yaw;
        /// Polygon outline, (x, z) world points in order around the area
        /// (3 or more; Vector2.y is the world z). A prism: the outline,
        /// between Position.y - Extents.y and Position.y + Extents.y, as a
        /// box's height. Position.x / z is the points' mean - kept for
        /// labels, the preview's post and the website's map, never written.
        /// Shared between copies of the struct: replace the array, never
        /// edit it in place (ZonePolygon's helpers return new ones).
        public Vector2[] Points;

        // Item
        public int ItemId;
        public Comparison Compare;
        public int Amount;

        /// When true, Amount is measured FROM the count held when the
        /// run started rather than as an absolute total - "pick up 3
        /// more rope" rather than "hold 5 rope". Absolute triggers are
        /// wrong for practice: starting a segment with some already in
        /// the bag would fire the split immediately.
        public bool Relative;

        // Event
        public string EventName;

        public bool IsSet { get { return Kind != TriggerKind.None; } }

        public string Describe()
        {
            switch (Kind)
            {
                case TriggerKind.Zone:
                    return (Shape == ZoneShape.Box ? "box (" : Shape == ZoneShape.Polygon ? "poly (" : "zone (") +
                           Position.x.ToString("F0") + ", " +
                           Position.y.ToString("F0") + ", " +
                           Position.z.ToString("F0") + ")";
                case TriggerKind.Item:
                    return "item " + ItemId + " " + OpText(Compare) +
                           (Relative ? " +" : " ") + Amount;
                case TriggerKind.Event:
                    return "event " + EventName;
                case TriggerKind.Manual:
                    return "manual";
                default:
                    return "none";
            }
        }

        public static string OpText(Comparison c)
        {
            if (c == Comparison.AtLeast) return ">=";
            if (c == Comparison.AtMost) return "<=";
            return "==";
        }
    }

    /// Supplies the inventory side of trigger evaluation. An interface
    /// rather than a delegate so evaluation allocates nothing, and so the
    /// tests can provide a trivial fake.
    public interface IItemCounts
    {
        int AmountOf(int itemId);
    }

    /// Per-trigger edge state. Kept beside the trigger rather than inside
    /// it so a Segment definition stays immutable data that can be shared
    /// between attempts.
    public struct TriggerState
    {
        public bool Satisfied;
        public bool Primed;      // false until the first evaluation
        public bool InitialValue; // what it read when primed
    }

    // ------------------------------------------------------------------
    // Edge detection. Pure and Unity-light on purpose - this is unit
    // tested, because an off-by-one here means a split fires on the wrong
    // side of a zone and that is miserable to debug in game.
    // ------------------------------------------------------------------
    public static class TriggerEvaluator
    {
        /// True while the condition holds (level, not edge).
        ///
        /// `baseline` supplies the counts held when the run started, for
        /// relative item triggers. Null means no baseline is known, in
        /// which case a relative trigger treats the baseline as zero.
        public static bool IsSatisfied(Trigger t, Vector3 position, IItemCounts items,
                                       string firedEvent, IItemCounts baseline)
        {
            switch (t.Kind)
            {
                case TriggerKind.Zone:
                    if (t.Shape == ZoneShape.Polygon)
                        return Mathf.Abs(position.y - t.Position.y) <= t.Extents.y &&
                               ZonePolygon.Contains(t.Points, position.x, position.z);
                    if (t.Shape == ZoneShape.Box)
                    {
                        Vector3 d = position - t.Position;
                        float lx = d.x, lz = d.z;
                        if (t.Yaw != 0f)
                        {
                            // Into the box's frame: the inverse of a turn
                            // by Yaw about +Y (Unity's Euler(0, yaw, 0)).
                            double a = t.Yaw * Math.PI / 180.0;
                            float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
                            lx = d.x * c - d.z * s;
                            lz = d.x * s + d.z * c;
                        }
                        return Mathf.Abs(lx) <= t.Extents.x &&
                               Mathf.Abs(d.y) <= t.Extents.y &&
                               Mathf.Abs(lz) <= t.Extents.z;
                    }
                    return (position - t.Position).sqrMagnitude <= t.Radius * t.Radius;

                case TriggerKind.Item:
                    {
                        if (items == null) return false;

                        int have = items.AmountOf(t.ItemId);
                        int target = t.Amount;

                        if (t.Relative)
                            target += (baseline != null ? baseline.AmountOf(t.ItemId) : 0);

                        if (t.Compare == Comparison.AtLeast) return have >= target;
                        if (t.Compare == Comparison.AtMost) return have <= target;
                        return have == target;
                    }

                case TriggerKind.Event:
                    return EventMatches(t.EventName, firedEvent);

                case TriggerKind.Manual:
                    return false;   // only ever fired explicitly

                default:
                    return false;
            }
        }

        /// True on the rising edge only.
        ///
        /// The first evaluation primes the state instead of firing. That
        /// matters: teleporting INTO a start zone would otherwise fire the
        /// start trigger immediately, before you have moved.
        public static bool Fired(Trigger t, ref TriggerState state, Vector3 position,
                                 IItemCounts items, string firedEvent, IItemCounts baseline)
        {
            bool now = IsSatisfied(t, position, items, firedEvent, baseline);

            if (!state.Primed)
            {
                state.Primed = true;
                state.Satisfied = now;
                state.InitialValue = now;
                return false;
            }

            bool rising = now && !state.Satisfied;
            state.Satisfied = now;
            return rising;
        }

        /// True the first time the condition CHANGES from however it
        /// read when primed - in either direction.
        ///
        /// This is what a start trigger needs, and getting it wrong is
        /// why the clock never started: you teleport to a spawn that
        /// sits inside the start zone, so the trigger primes as
        /// "satisfied", and walking out is a FALLING edge that a
        /// rising-edge check ignores forever.
        ///
        /// Crossing handles both authoring styles without a setting:
        /// spawn inside the zone and it fires when you leave; approach
        /// from outside and it fires when you enter.
        public static bool Crossed(Trigger t, ref TriggerState state, Vector3 position,
                                   IItemCounts items, string firedEvent, IItemCounts baseline)
        {
            bool now = IsSatisfied(t, position, items, firedEvent, baseline);

            if (!state.Primed)
            {
                state.Primed = true;
                state.Satisfied = now;
                state.InitialValue = now;
                return false;
            }

            bool changed = now != state.InitialValue;
            state.Satisfied = now;
            return changed;
        }

        /// An event trigger's name is one event or several separated by '|'
        /// ("hold-interact|first-input": either starts it - the autosplitter's two
        /// start settings at once, v0.24.186). Compared without allocating:
        /// this runs once per event per trigger.
        public static bool EventMatches(string name, string fired)
        {
            if (fired == null || string.IsNullOrEmpty(name)) return false;
            int start = 0;
            while (start <= name.Length)
            {
                int bar = name.IndexOf('|', start);
                int end = bar < 0 ? name.Length : bar;
                int a = start, b = end;
                while (a < b && name[a] == ' ') a++;
                while (b > a && name[b - 1] == ' ') b--;
                if (b - a == fired.Length && b > a &&
                    string.Compare(name, a, fired, 0, fired.Length, StringComparison.OrdinalIgnoreCase) == 0)
                    return true;
                if (bar < 0) break;
                start = bar + 1;
            }
            return false;
        }

        /// Convenience overloads for callers with no relative triggers.
        public static bool IsSatisfied(Trigger t, Vector3 position, IItemCounts items,
                                       string firedEvent)
        {
            return IsSatisfied(t, position, items, firedEvent, null);
        }

        public static bool Fired(Trigger t, ref TriggerState state, Vector3 position,
                                 IItemCounts items, string firedEvent)
        {
            return Fired(t, ref state, position, items, firedEvent, null);
        }

        public static void Reset(ref TriggerState state)
        {
            state.Satisfied = false;
            state.Primed = false;
            state.InitialValue = false;
        }

        /// Primed as "not satisfied", so the next Fired() fires if the
        /// condition already holds. For a trigger that becomes the one to
        /// watch because an earlier one fired (SplitSequence).
        public static void ArmAsNext(ref TriggerState state)
        {
            state.Satisfied = false;
            state.Primed = true;
            state.InitialValue = false;
        }
    }

    // ------------------------------------------------------------------
    // A named piece of run to practise: plane crash -> cave 5, a sinkhole
    // drop, whatever. Start and end are triggers; checkpoints split.
    //
    // Segment ids are what leaderboard comparison keys off, so they must
    // be stable and human-authored rather than generated.
    // ------------------------------------------------------------------
    public sealed class Segment
    {
        public string Id = "";
        public string Name = "";
        public string Category = "Segments";
        public string Notes = "";
        public string SourceFile = "";

        public Trigger Start;
        public Trigger End;
        public readonly List<Trigger> Checkpoints = new List<Trigger>();

        /// Split names for the splits table (v0.24.146), index-aligned with
        /// Checkpoints; missing or empty = "Checkpoint n". EndName names the
        /// last row (empty = "End"). Display only: not in the route
        /// fingerprint, so renaming a split retires nothing.
        public readonly List<string> CheckpointNames = new List<string>();
        public string EndName = "";

        /// Checkpoints not drawn during a run (runner request: hide zones
        /// individually), index-aligned with Checkpoints; missing = shown.
        /// Written as `hide = yes` under the checkpoint. Display only: not in
        /// the route fingerprint, so hiding one retires nothing.
        public readonly List<bool> CheckpointHidden = new List<bool>();

        public bool IsCheckpointHidden(int index)
        {
            return index >= 0 && index < CheckpointHidden.Count && CheckpointHidden[index];
        }

        public void SetCheckpointHidden(int index, bool hidden)
        {
            if (index < 0) return;
            while (CheckpointHidden.Count <= index) CheckpointHidden.Add(false);
            CheckpointHidden[index] = hidden;
        }

        /// Which trigger a `split =` line names while parsing: -1 none,
        /// a checkpoint index, or EndNameTarget.
        public int ParseNameTarget = -1;
        public const int EndNameTarget = int.MaxValue;

        /// The name of splits-table row `row` (0..Checkpoints.Count; the
        /// last is the end).
        public string SplitName(int row)
        {
            if (row >= Checkpoints.Count) return EndName.Length > 0 ? EndName : "End";
            string n = row < CheckpointNames.Count ? CheckpointNames[row] : null;
            return string.IsNullOrEmpty(n) ? DefaultName(row) : n;
        }

        // "Checkpoint n", made once per row number: the splits table asks
        // for every row's name ten times a second while a run is on. A race
        // between threads (the site links this file) only makes an equal
        // string twice.
        private static readonly string[] DefaultNames = new string[64];

        private static string DefaultName(int row)
        {
            if (row < 0 || row >= DefaultNames.Length) return "Checkpoint " + (row + 1);
            string s = DefaultNames[row];
            if (s == null) { s = "Checkpoint " + (row + 1); DefaultNames[row] = s; }
            return s;
        }

        public void SetCheckpointName(int index, string name)
        {
            while (CheckpointNames.Count <= index) CheckpointNames.Add("");
            CheckpointNames[index] = name ?? "";
        }

        /// Removes a checkpoint and its name (and hide flag) together.
        public void RemoveCheckpoint(int index)
        {
            Checkpoints.RemoveAt(index);
            if (index < CheckpointNames.Count) CheckpointNames.RemoveAt(index);
            if (index < CheckpointHidden.Count) CheckpointHidden.RemoveAt(index);
        }

        /// Where to place the player to attempt this segment. Optional -
        /// without it the segment can still be timed, just not practised
        /// from a teleport.
        public bool HasSpawn;
        /// Which cave the spawn is in ("cave06", the game's CaveNames in
        /// lower case; "" = the surface or not known), recorded where the
        /// spawn is set (v0.24.193). A teleport sets the game's current
        /// cave from it - only the cave mouths do otherwise. Not part of the
        /// route fingerprint.
        public string Cave = "";
        public Vector3 SpawnPosition;
        public float SpawnYaw;
        public float SpawnPitch;

        /// How the segment's start state (a savestate kept beside it, see
        /// Modules/SavestateModule) is restored on a restart: in place by
        /// default - instant - or with a scene load, slower but the full
        /// reset the game itself does. Written as `restore = load` only
        /// when set (author's call, 2026-09-23: fastest by default, the
        /// validated method as the alternative).
        public bool StartRestoreWithLoad;

        /// Keep loaded (author, 2026-10-10, T-0212): the start state is
        /// restored once, then restarts teleport and reset the player and the
        /// endgame movers while the world is still the one that restore made
        /// (Data/KeepLoaded decides). Written as `keep = loaded` only when
        /// set; not part of the route.
        public bool KeepLoaded;

        /// A run category ("Any%"): a Restart here with a start state starts
        /// a run attempt - always a Full load, run mode on, practice locked;
        /// a Restart during it is a reset (author, 2026-10-02: runs start
        /// from preset category saves, only marked spots). Written as
        /// `run = <category>` when set; not part of the route.
        public string RunCategory = "";

        /// Which start state the segment expects: a hash of the savestate's
        /// data, written on capture as `startstate = <hash>`. Part of the
        /// route fingerprint, so a new start state retires old times just
        /// as moving a zone does (author's call, 2026-09-23). Empty when
        /// there is none - and then the fingerprint is what it always was.
        public string StartState = "";

        /// What `event autosplit` triggers split on (v0.24.186): the LiveSplit
        /// autosplitter's enabled settings as event names - cave-enter-cave06,
        /// item-143 (first pickup), item-change-143 (every change),
        /// clothing-<id>, passenger-<n>, endgame-cutscene. A LiveSplit split
        /// is "the next thing the autosplitter splits on", whatever it is, so
        /// each checkpoint is that one trigger and the list says what counts
        /// (Data/LssAutoSplit, Modules/PracticeRunModule). Written as
        /// `autosplit = a b c`; part of the route fingerprint when set.
        public readonly List<string> AutoSplit = new List<string>();
        public const string AutoSplitEvent = "autosplit";

        // No cached GUIContent here on purpose. Segment is pure data and
        // is linked into the test project, which has no Unity - the label
        // cache is a GUI concern and lives with the panel that draws it.

        /// Bumped by the editor on every change, so anything holding a
        /// reference can notice it went stale. A run armed against the
        /// old start zone must not keep using it after the zone moves.
        public int Revision;

        /// True when this can be TIMED. Without both ends it is still a
        /// perfectly good place to teleport to, just not a run.
        public bool IsTimed { get { return Start.IsSet && End.IsSet; } }

        /// Identifies the ROUTE, as opposed to the entry.
        ///
        /// Attempts are keyed on the segment id so they can be compared
        /// between players, but moving a start zone changes what the
        /// times mean while leaving the id alone. Recording this
        /// alongside each attempt lets old times be recognised as
        /// belonging to a different route rather than silently competing
        /// with new ones.
        ///
        /// FNV-1a over the trigger text: deterministic across runs and
        /// machines, unlike string.GetHashCode.
        public string RouteFingerprint()
        {
            uint hash = 2166136261;

            hash = Fold(hash, TriggerParser.Write(Start));
            for (int i = 0; i < Checkpoints.Count; i++)
                hash = Fold(hash, TriggerParser.Write(Checkpoints[i]));
            hash = Fold(hash, TriggerParser.Write(End));

            // Only when set, so segments without a start state keep the
            // fingerprint their recorded attempts carry.
            if (!string.IsNullOrEmpty(StartState)) hash = Fold(hash, "startstate " + StartState);
            if (AutoSplit.Count > 0) hash = Fold(hash, "autosplit " + string.Join(" ", AutoSplit.ToArray()));

            return hash.ToString("x8");
        }

        /// FNV-1a of any text as eight hex digits - how a start state's
        /// data is named in the segment block.
        public static string HashText(string text)
        {
            return Fold(2166136261, text).ToString("x8");
        }

        private static uint Fold(uint hash, string text)
        {
            if (text == null) return hash;

            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= 16777619;
            }

            // Separator, so "ab" + "c" cannot collide with "a" + "bc".
            hash ^= 31;
            hash *= 16777619;
            return hash;
        }

        /// A spot and a segment are the same thing at different levels of
        /// configuration: somewhere to stand, optionally with a start and
        /// an end attached. Keeping them as separate types produced two
        /// lists, two files and two editors for one idea, so an entry is
        /// valid if it can do EITHER job.
        public bool IsValid { get { return Id.Length > 0 && (HasSpawn || IsTimed); } }
    }

    // ------------------------------------------------------------------
    // Trigger text <-> struct.
    //
    // Kept apart from SegmentLibrary because that class touches BepInEx
    // logging and the filesystem and so cannot be linked into the test
    // project. Parsing is pure, and it is the part worth testing: capture
    // writes triggers back out as text, so Parse and Write have to agree
    // or in-game capture silently corrupts a route file.
    // ------------------------------------------------------------------
    public static class TriggerParser
    {
        public static bool Parse(string value, out Trigger t)
        {
            t = new Trigger();
            if (string.IsNullOrEmpty(value)) return false;

            string[] p = Split(value);
            if (p.Length == 0) return false;

            string kind = p[0].ToLowerInvariant();

            if (kind == "manual")
            {
                t.Kind = TriggerKind.Manual;
                return true;
            }

            if (kind == "event")
            {
                if (p.Length < 2) return false;
                t.Kind = TriggerKind.Event;
                t.EventName = MigrateEventName(p[1]);
                return true;
            }

            if (kind == "zone")
            {
                float x, y, z, r;
                if (p.Length < 5) return false;
                if (!F(p[1], out x) || !F(p[2], out y) || !F(p[3], out z) || !F(p[4], out r)) return false;
                if (r <= 0f) return false;

                t.Kind = TriggerKind.Zone;
                t.Shape = ZoneShape.Sphere;
                t.Position = new Vector3(x, y, z);
                t.Radius = r;
                return true;
            }

            if (kind == "box")
            {
                float x, y, z, ex, ey, ez;
                if (p.Length < 7) return false;
                if (!F(p[1], out x) || !F(p[2], out y) || !F(p[3], out z)) return false;
                if (!F(p[4], out ex) || !F(p[5], out ey) || !F(p[6], out ez)) return false;
                if (ex <= 0f || ey <= 0f || ez <= 0f) return false;

                // Optional 8th value (v0.24.75); older files have none.
                float yaw = 0f;
                if (p.Length > 7 && !F(p[7], out yaw)) return false;

                t.Kind = TriggerKind.Zone;
                t.Shape = ZoneShape.Box;
                t.Position = new Vector3(x, y, z);
                t.Extents = new Vector3(ex, ey, ez);
                t.Yaw = NormalizeYaw(yaw);
                return true;
            }

            if (kind == "poly")
            {
                // poly <middle y> <half height> x1 z1 x2 z2 x3 z3 ... - the
                // height as a box writes it (centre and half-size).
                float cy, ey;
                if (p.Length < 9 || (p.Length - 3) % 2 != 0) return false;
                if (!F(p[1], out cy) || !F(p[2], out ey) || ey <= 0f) return false;

                Vector2[] pts = new Vector2[(p.Length - 3) / 2];
                for (int i = 0; i < pts.Length; i++)
                {
                    float px, pz;
                    if (!F(p[3 + i * 2], out px) || !F(p[4 + i * 2], out pz)) return false;
                    pts[i] = new Vector2(px, pz);
                }

                t.Kind = TriggerKind.Zone;
                t.Shape = ZoneShape.Polygon;
                t.Points = pts;
                t.Extents = new Vector3(0f, ey, 0f);
                Vector2 c = ZonePolygon.Centre(pts);
                t.Position = new Vector3(c.x, cy, c.y);
                return true;
            }

            if (kind == "item")
            {
                if (p.Length < 4) return false;

                int id, amount;
                if (!I(p[1], out id)) return false;

                // A leading + marks a relative amount: "3 more than at
                // the start" rather than "a total of 3".
                string amountText = p[3];
                bool relative = amountText.Length > 1 && amountText[0] == (char)43;
                if (relative) amountText = amountText.Substring(1);

                if (!I(amountText, out amount)) return false;

                Comparison c;
                if (p[2] == ">=") c = Comparison.AtLeast;
                else if (p[2] == "<=") c = Comparison.AtMost;
                else if (p[2] == "==" || p[2] == "=") c = Comparison.Exactly;
                else return false;

                t.Kind = TriggerKind.Item;
                t.ItemId = id;
                t.Compare = c;
                t.Amount = amount;
                t.Relative = relative;
                return true;
            }

            return false;
        }

        /// The velocity start is gone (T-0282, author 2026-10-10): a stored
        /// `moving` reads as `first-input`, alone or in a '|' list ("hold-
        /// interact|moving"), never twice. The trigger's text changes, so
        /// the route fingerprint does and old times retire - the start moved
        /// (author's call).
        public const string LegacyMoving = "moving";
        public const string FirstInput = "first-input";

        public static string MigrateEventName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.IndexOf(LegacyMoving, StringComparison.OrdinalIgnoreCase) < 0) return name;
            string[] parts = name.Split('|');
            List<string> kept = new List<string>();
            bool changed = false;
            for (int i = 0; i < parts.Length; i++)
            {
                string e = parts[i].Trim();
                if (string.Equals(e, LegacyMoving, StringComparison.OrdinalIgnoreCase)) { e = FirstInput; changed = true; }
                bool dup = false;
                for (int k = 0; k < kept.Count; k++)
                    if (string.Equals(kept[k], e, StringComparison.OrdinalIgnoreCase)) dup = true;
                if (!dup) kept.Add(e);
            }
            return changed ? string.Join("|", kept.ToArray()) : name;
        }


        /// Degrees into [0, 360), rounded as written (0.01) so a value
        /// read back compares equal to the one written.
        public static float NormalizeYaw(float yaw)
        {
            float y = yaw % 360f;
            if (y < 0f) y += 360f;
            y = (float)Math.Round(y, 2);
            return y >= 360f ? 0f : y;
        }

        public static string Write(Trigger t)
        {
            switch (t.Kind)
            {
                case TriggerKind.Zone:
                    if (t.Shape == ZoneShape.Polygon)
                    {
                        System.Text.StringBuilder sb = new System.Text.StringBuilder("poly ");
                        sb.Append(Num(t.Position.y)).Append(' ').Append(Num(t.Extents.y));
                        if (t.Points != null)
                            for (int i = 0; i < t.Points.Length; i++)
                                sb.Append(' ').Append(Num(t.Points[i].x)).Append(' ').Append(Num(t.Points[i].y));
                        return sb.ToString();
                    }
                    if (t.Shape == ZoneShape.Box)
                        // Yaw only when turned: an unturned box writes (and
                        // fingerprints) exactly as before, so no times retire.
                        return "box " + Num(t.Position.x) + " " + Num(t.Position.y) + " " +
                               Num(t.Position.z) + " " + Num(t.Extents.x) + " " +
                               Num(t.Extents.y) + " " + Num(t.Extents.z) +
                               (t.Yaw != 0f ? " " + Num(t.Yaw) : "");

                    return "zone " + Num(t.Position.x) + " " + Num(t.Position.y) + " " +
                           Num(t.Position.z) + " " + Num(t.Radius);
                case TriggerKind.Item:
                    return "item " + t.ItemId + " " + Trigger.OpText(t.Compare) + " " +
                           (t.Relative ? "+" : "") + t.Amount;
                case TriggerKind.Event:
                    return "event " + t.EventName;
                default:
                    return "manual";
            }
        }


        public static string[] Split(string s)
        {
            return s.Split(new char[] { ' ', '	' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public static bool F(string s, out float v)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        public static bool I(string s, out int v)
        {
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        }

        public static string Num(float f)
        {
            return f.ToString("F2", CultureInfo.InvariantCulture);
        }

        /// What a coordinates box keeps of typed or pasted text (author,
        /// 2026-09-26: reject anything that is not a number): digits,
        /// '-', '.', spaces and commas. A ';' or a tab becomes a space so
        /// a pasted "1;2;3" still separates; everything else is dropped.
        /// Returns `text` itself when nothing changes.
        public static string FilterCoords(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            bool clean = true;
            for (int i = 0; i < text.Length && clean; i++) clean = CoordsChar(text[i]);
            if (clean) return text;

            var sb = new System.Text.StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (CoordsChar(c)) sb.Append(c);
                else if (c == ';' || c == '\t') sb.Append(' ');
            }
            return sb.ToString();
        }

        private static bool CoordsChar(char c)
        {
            return (c >= '0' && c <= '9') || c == '-' || c == '.' || c == ' ' || c == ',';
        }

        /// Spaces and commas around a complete value (three numbers) are
        /// dropped; an incomplete one keeps them, so "1 " can become
        /// "1 2". Returns `text` itself when nothing changes.
        public static string TidyCoords(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            string t = text.Trim(' ', ',');
            Vector3 v;
            return t.Length != text.Length && ParseCoords(t, out v) ? t : text;
        }

        /// Three numbers separated by spaces, commas or semicolons, with
        /// optional brackets: "1 2 3", "1, 2, 3", "(1, 2, 3)".
        public static bool ParseCoords(string text, out Vector3 v)
        {
            v = Vector3.zero;
            if (text == null) return false;
            string[] p = text.Trim().Trim('(', ')', '[', ']').Split(new[] { ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            float x, y, z;
            if (p.Length != 3 || !F(p[0], out x) || !F(p[1], out y) || !F(p[2], out z)) return false;
            v = new Vector3(x, y, z);
            return true;
        }

        /// A polygon point: two numbers (x z), separated as ParseCoords
        /// separates three.
        public static bool ParsePoint(string text, out Vector2 v)
        {
            v = new Vector2(0f, 0f);
            if (text == null) return false;
            string[] p = text.Trim().Trim('(', ')', '[', ']').Split(new[] { ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            float x, z;
            if (p.Length != 2 || !F(p[0], out x) || !F(p[1], out z)) return false;
            v = new Vector2(x, z);
            return true;
        }

        /// TidyCoords for a point (two numbers).
        public static string TidyPoint(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            string t = text.Trim(' ', ',');
            Vector2 v;
            return t.Length != text.Length && ParsePoint(t, out v) ? t : text;
        }
    }

    // ------------------------------------------------------------------
    // Polygon zones' geometry. Pure: the containment test and the edits
    // the editor makes, each returning a NEW array (a Trigger is a struct
    // and copies share the array - an in-place edit would move the zone
    // in every copy, a duplicated entry's too).
    // ------------------------------------------------------------------
    public static class ZonePolygon
    {
        /// Even-odd ray cast in the x / z plane: inside when a ray from the
        /// point crosses the outline an odd number of times. Fewer than 3
        /// points is never inside. A self-crossing outline counts its
        /// overlaps as outside, as any fill rule must pick one.
        public static bool Contains(Vector2[] pts, float x, float z)
        {
            if (pts == null || pts.Length < 3) return false;
            bool inside = false;
            for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
            {
                float zi = pts[i].y, zj = pts[j].y;
                if ((zi > z) != (zj > z))
                {
                    float cross = pts[i].x + (z - zi) * (pts[j].x - pts[i].x) / (zj - zi);
                    if (x < cross) inside = !inside;
                }
            }
            return inside;
        }

        /// The points' mean: where the label, the preview's post and a
        /// Here move are measured from.
        public static Vector2 Centre(Vector2[] pts)
        {
            if (pts == null || pts.Length == 0) return new Vector2(0f, 0f);
            double x = 0, z = 0;
            for (int i = 0; i < pts.Length; i++) { x += pts[i].x; z += pts[i].y; }
            return new Vector2((float)(x / pts.Length), (float)(z / pts.Length));
        }

        /// A square of side 2 * half around (x, z), turned by yaw degrees
        /// as a box is - a new polygon starts as the box it replaces.
        public static Vector2[] Square(float x, float z, float half, float yaw)
        {
            double a = yaw * Math.PI / 180.0;
            float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
            float[] lx = { -half, half, half, -half };
            float[] lz = { -half, -half, half, half };
            Vector2[] pts = new Vector2[4];
            for (int i = 0; i < 4; i++)
                // Box frame to world: the inverse of TriggerEvaluator's turn.
                pts[i] = new Vector2(x + lx[i] * c + lz[i] * s, z - lx[i] * s + lz[i] * c);
            return pts;
        }

        public static Vector2[] Translated(Vector2[] pts, float dx, float dz)
        {
            if (pts == null) return null;
            Vector2[] o = new Vector2[pts.Length];
            for (int i = 0; i < pts.Length; i++) o[i] = new Vector2(pts[i].x + dx, pts[i].y + dz);
            return o;
        }

        public static Vector2[] WithPoint(Vector2[] pts, int index, Vector2 p)
        {
            Vector2[] o = (Vector2[])pts.Clone();
            o[index] = p;
            return o;
        }

        public static Vector2[] Added(Vector2[] pts, Vector2 p)
        {
            int n = pts != null ? pts.Length : 0;
            Vector2[] o = new Vector2[n + 1];
            for (int i = 0; i < n; i++) o[i] = pts[i];
            o[n] = p;
            return o;
        }

        public static Vector2[] Removed(Vector2[] pts, int index)
        {
            Vector2[] o = new Vector2[pts.Length - 1];
            for (int i = 0, k = 0; i < pts.Length; i++) if (i != index) o[k++] = pts[i];
            return o;
        }

        /// Sets the trigger's Position.x / z to its points' mean, keeping y.
        public static void Recentre(ref Trigger t)
        {
            Vector2 c = Centre(t.Points);
            t.Position = new Vector3(c.x, t.Position.y, c.y);
        }
    }
}
