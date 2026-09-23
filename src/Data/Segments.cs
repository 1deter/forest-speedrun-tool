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
    public enum ZoneShape { Sphere, Box }

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
                    return (Shape == ZoneShape.Box ? "box (" : "zone (") +
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
                    if (t.Shape == ZoneShape.Box)
                    {
                        Vector3 d = position - t.Position;
                        return Mathf.Abs(d.x) <= t.Extents.x &&
                               Mathf.Abs(d.y) <= t.Extents.y &&
                               Mathf.Abs(d.z) <= t.Extents.z;
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
                    return firedEvent != null &&
                           string.Equals(firedEvent, t.EventName, StringComparison.OrdinalIgnoreCase);

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

        /// Where to place the player to attempt this segment. Optional -
        /// without it the segment can still be timed, just not practised
        /// from a teleport.
        public bool HasSpawn;
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

        /// Which start state the segment expects: a hash of the savestate's
        /// data, written on capture as `startstate = <hash>`. Part of the
        /// route fingerprint, so a new start state retires old times just
        /// as moving a zone does (author's call, 2026-09-23). Empty when
        /// there is none - and then the fingerprint is what it always was.
        public string StartState = "";

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
                t.EventName = p[1];
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

                t.Kind = TriggerKind.Zone;
                t.Shape = ZoneShape.Box;
                t.Position = new Vector3(x, y, z);
                t.Extents = new Vector3(ex, ey, ez);
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


        public static string Write(Trigger t)
        {
            switch (t.Kind)
            {
                case TriggerKind.Zone:
                    if (t.Shape == ZoneShape.Box)
                        return "box " + Num(t.Position.x) + " " + Num(t.Position.y) + " " +
                               Num(t.Position.z) + " " + Num(t.Extents.x) + " " +
                               Num(t.Extents.y) + " " + Num(t.Extents.z);

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
    }
}
