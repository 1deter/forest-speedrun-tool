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
        public float Radius;

        // Item
        public int ItemId;
        public Comparison Compare;
        public int Amount;

        // Event
        public string EventName;

        public bool IsSet { get { return Kind != TriggerKind.None; } }

        public string Describe()
        {
            switch (Kind)
            {
                case TriggerKind.Zone:
                    return "zone (" + Position.x.ToString("F0") + ", " +
                                      Position.y.ToString("F0") + ", " +
                                      Position.z.ToString("F0") + ") r" + Radius.ToString("F1");
                case TriggerKind.Item:
                    return "item " + ItemId + " " + OpText(Compare) + " " + Amount;
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
    }

    // ------------------------------------------------------------------
    // Edge detection. Pure and Unity-light on purpose - this is unit
    // tested, because an off-by-one here means a split fires on the wrong
    // side of a zone and that is miserable to debug in game.
    // ------------------------------------------------------------------
    public static class TriggerEvaluator
    {
        /// True while the condition holds (level, not edge).
        public static bool IsSatisfied(Trigger t, Vector3 position, IItemCounts items,
                                       string firedEvent)
        {
            switch (t.Kind)
            {
                case TriggerKind.Zone:
                    return (position - t.Position).sqrMagnitude <= t.Radius * t.Radius;

                case TriggerKind.Item:
                    if (items == null) return false;
                    int have = items.AmountOf(t.ItemId);
                    if (t.Compare == Comparison.AtLeast) return have >= t.Amount;
                    if (t.Compare == Comparison.AtMost) return have <= t.Amount;
                    return have == t.Amount;

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
                                 IItemCounts items, string firedEvent)
        {
            bool now = IsSatisfied(t, position, items, firedEvent);

            if (!state.Primed)
            {
                state.Primed = true;
                state.Satisfied = now;
                return false;
            }

            bool rising = now && !state.Satisfied;
            state.Satisfied = now;
            return rising;
        }

        public static void Reset(ref TriggerState state)
        {
            state.Satisfied = false;
            state.Primed = false;
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

        // No cached GUIContent here on purpose. Segment is pure data and
        // is linked into the test project, which has no Unity - the label
        // cache is a GUI concern and lives with the panel that draws it.
        public bool IsValid { get { return Id.Length > 0 && Start.IsSet && End.IsSet; } }
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
                t.Position = new Vector3(x, y, z);
                t.Radius = r;
                return true;
            }

            if (kind == "item")
            {
                if (p.Length < 4) return false;

                int id, amount;
                if (!I(p[1], out id)) return false;
                if (!I(p[3], out amount)) return false;

                Comparison c;
                if (p[2] == ">=") c = Comparison.AtLeast;
                else if (p[2] == "<=") c = Comparison.AtMost;
                else if (p[2] == "==" || p[2] == "=") c = Comparison.Exactly;
                else return false;

                t.Kind = TriggerKind.Item;
                t.ItemId = id;
                t.Compare = c;
                t.Amount = amount;
                return true;
            }

            return false;
        }


        public static string Write(Trigger t)
        {
            switch (t.Kind)
            {
                case TriggerKind.Zone:
                    return "zone " + Num(t.Position.x) + " " + Num(t.Position.y) + " " +
                           Num(t.Position.z) + " " + Num(t.Radius);
                case TriggerKind.Item:
                    return "item " + t.ItemId + " " + Trigger.OpText(t.Compare) + " " + t.Amount;
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
