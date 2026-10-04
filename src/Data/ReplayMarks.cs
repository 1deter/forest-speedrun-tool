using System;
using System.Collections.Generic;
using UnityEngine;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Replays that show what happened (docs/run-audit-and-replays.md part
    // 2; author, 2026-10-03: in game first). The pure half:
    //   - which of the game's named events (Game/GameEvents, WorldEvents,
    //     the bus events from Game/AuditWatch) a run records as a marker,
    //     under the run audit's kind (Data/RunAudit), so labels and groups
    //     are shared with the attempt log;
    //   - which buildings / markers show at a replay time t (a blueprint
    //     placed shows until it is finished at the same place);
    //   - the markers nearest the viewer, for their labels;
    //   - a default box per structure kind when its meshes were not read.
    //
    // Allocation-free where the game calls it every frame (UpTo, Nearest,
    // Shows). Tested in ReplayMarksTests.
    // ------------------------------------------------------------------
    public static class ReplayMarks
    {
        /// Labels show for markers this close to the player (m), at most
        /// MaxLabels of them, the nearest first (markers at one spot share
        /// a label: Data/ReplayLabels).
        public const float LabelRadius = 25f;
        public const int MaxLabels = 12;

        /// A placed blueprint and the finished structure at most this far
        /// apart (m) are one building.
        public const float SamePlace = 1.5f;

        /// A label is cut to this many characters.
        public const int LabelChars = 48;

        // The endgame's named events (GameEvents.Hooks) and the doors.
        private static readonly string[] Endgame =
        {
            "keycard-door", "red-elevator", "timmy-pickup", "megan-transform", "megan-pickup",
            "megan-to-machine", "end-crash", "end-shutdown", "timmy-goodbye", "raft-out-of-world",
        };

        /// The marker kind for a game event name, or null when it is not
        /// one (a run start trigger, the specific copy of a general event -
        /// crafted-bomb-timed beside crafted - or something unknown).
        /// `text` is the marker's detail: the event's own, or one made
        /// from the name (clothing-<id>, the endgame area).
        public static string KindFor(string name, string detail, out string text)
        {
            text = detail ?? "";
            if (string.IsNullOrEmpty(name)) return null;
            switch (name)
            {
                case "cave-enter": case "cave-exit": case "passenger": case "rope-grab": case "rope-leave":
                    return name;
                case BusEvents.Built: return RunAudit.Built;
                case BusEvents.Crafted: return RunAudit.Crafted;
                case BusEvents.Used: return RunAudit.Used;
                case BusEvents.KillEnemy: return RunAudit.Kill;
                case BusEvents.KillAnimal: return RunAudit.Animal;
                case BusEvents.HitByEnemy: return RunAudit.Hit;
                case BusEvents.TreeCut: return RunAudit.Tree;
                case BusEvents.Bomb: return RunAudit.Bomb;
                case BusEvents.Slept: return RunAudit.Sleep;
                case BusEvents.Story: return RunAudit.Story;
                case BusEvents.RideStart: return RunAudit.RideStart;
                case BusEvents.RideEnd: return RunAudit.RideEnd;
                case BusEvents.EndgameEnter:
                    if (text.Length == 0) text = "entered the endgame area";
                    return RunAudit.Endgame;
                case BusEvents.EndgameLeave:
                    if (text.Length == 0) text = "left the endgame area";
                    return RunAudit.Endgame;
            }
            if (name.StartsWith("clothing-", StringComparison.Ordinal) && name.Length > 9)
            {
                if (text.Length == 0) text = name.Substring(9);
                return "clothing";
            }
            for (int i = 0; i < Endgame.Length; i++)
                if (name == Endgame[i]) return name;
            return null;
        }

        /// "Crafted: Bomb", cut to LabelChars.
        public static string Label(RunEvent e)
        {
            string label = RunAudit.Label(e.Kind);
            string s = string.IsNullOrEmpty(e.Detail) ? label : label + ": " + e.Detail;
            return s.Length > LabelChars ? s.Substring(0, LabelChars - 3) + "..." : s;
        }

        // --- what shows at time t -------------------------------------------------

        /// How many events happened at or before t (time order).
        public static int UpTo(IList<RunEvent> events, float t)
        {
            if (events == null) return 0;
            int lo = 0, hi = events.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (events[mid].T <= t) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        /// The replay time things show up to: the ghost's time while a run
        /// runs, everything otherwise (the whole route, like its line).
        public static float ShownUpTo(bool running, float ghostTime)
        {
            return running ? ghostTime : float.PositiveInfinity;
        }

        /// For each building, the time it stops showing: a placed blueprint
        /// until the same kind is finished at the same place after it, a
        /// finished one never (+inf).
        public static float[] Until(IList<RunBuilding> b)
        {
            float[] until = new float[b == null ? 0 : b.Count];
            for (int i = 0; i < until.Length; i++)
            {
                until[i] = float.PositiveInfinity;
                if (b[i].State != RunBuilding.Placed) continue;
                for (int j = i + 1; j < b.Count; j++)
                {
                    if (b[j].State != RunBuilding.Built || b[j].T < b[i].T) continue;
                    if (b[j].Kind != b[i].Kind) continue;
                    if ((b[j].P - b[i].P).sqrMagnitude > SamePlace * SamePlace) continue;
                    until[i] = b[j].T;
                    break;
                }
            }
            return until;
        }

        /// Building i shows at t.
        public static bool Shows(RunBuilding b, float until, float t)
        {
            return b.T <= t && (t < until || float.IsPositiveInfinity(until));
        }

        // --- labels near the viewer -----------------------------------------------

        /// The first `count` events' indices within `radius` of `viewer`,
        /// the nearest first, into `idx` (its length is the most); `dist`
        /// is scratch of the same length. Returns how many.
        public static int Nearest(IList<RunEvent> events, int count, Vector3 viewer, float radius, int[] idx, float[] dist)
        {
            if (events == null || idx == null || dist == null) return 0;
            int max = Mathf.Min(idx.Length, dist.Length);
            if (max == 0) return 0;
            count = Mathf.Min(count, events.Count);
            float r2 = radius * radius;
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                float d = (events[i].P - viewer).sqrMagnitude;
                if (d > r2) continue;
                if (n == max && d >= dist[n - 1]) continue;
                int at = n < max ? n++ : n - 1;
                while (at > 0 && dist[at - 1] > d)
                {
                    dist[at] = dist[at - 1];
                    idx[at] = idx[at - 1];
                    at--;
                }
                dist[at] = d;
                idx[at] = i;
            }
            return n;
        }

        // --- a structure's box when its meshes were not read ------------------------

        private static readonly string[] SizeKeys =
        {
            "cabin", "house", "platform", "shelter", "tent", "wall", "gate", "fire", "trap",
            "raft", "sled", "glider", "zipline", "bed", "rack", "stairs", "bridge", "rope",
        };

        private static readonly Vector3[] Sizes =
        {
            new Vector3(7f, 5f, 7f), new Vector3(8f, 6f, 8f), new Vector3(6f, 1f, 6f), new Vector3(4f, 3f, 4f),
            new Vector3(3f, 2f, 3f), new Vector3(4f, 3f, 0.6f), new Vector3(4f, 3f, 0.6f), new Vector3(1.5f, 1f, 1.5f),
            new Vector3(2f, 1.5f, 2f), new Vector3(4f, 1.5f, 4f), new Vector3(1.2f, 0.8f, 2.5f), new Vector3(4f, 1f, 3f),
            new Vector3(1f, 3f, 1f), new Vector3(2.2f, 0.8f, 1.2f), new Vector3(2f, 2f, 1f), new Vector3(2f, 3f, 4f),
            new Vector3(2f, 1f, 6f), new Vector3(1f, 3f, 1f),
        };

        /// A box for a structure kind (BuildingTypes name, any case) whose
        /// meshes were not read: by the first word it contains, else 2 m.
        public static Vector3 DefaultSize(string kind)
        {
            if (!string.IsNullOrEmpty(kind))
            {
                string k = kind.ToLowerInvariant();
                for (int i = 0; i < SizeKeys.Length; i++)
                    if (k.Contains(SizeKeys[i])) return Sizes[i];
            }
            return new Vector3(2f, 2f, 2f);
        }
    }
}
