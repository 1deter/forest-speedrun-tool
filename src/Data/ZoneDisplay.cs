using System;
using System.Collections.Generic;

namespace ForestOverlay.Data
{
    /// The Practice tab's "Zones" setting (runner request, backlog *Runs*).
    public enum ZoneMode { All, NextOnly, Off }

    // ------------------------------------------------------------------
    // Which of a segment's zones are drawn, as slots: StartSlot, a
    // checkpoint index, or `checkpoints` for the end.
    //
    //   Off       nothing, ever.
    //   no run    (editing, armed) the whole route.
    //   running   All = the whole route; NextOnly = the trigger that fires
    //             next (SplitSequence.Next: a checkpoint, or the end once
    //             only the end is left).
    //
    // A checkpoint marked hidden in the editor is left out everywhere
    // except where it is edited (`showHidden`: the Practice tab showing
    // with no run going). Hiding is display only - it is not in the route
    // fingerprint, so it retires no times.
    //
    // Only slots are chosen here; the zones' kinds and shapes stay with
    // the caller (a trigger that is not a zone simply draws nothing).
    //
    // Pure: linked into the tests.
    // ------------------------------------------------------------------
    public static class ZoneDisplay
    {
        public const int StartSlot = -1;

        /// Fills `into` (at least checkpoints + 2 long) and returns how many
        /// slots to draw. `next` is the running sequence's next trigger
        /// (0..checkpoints), or -1 when no run is going.
        public static int Pick(ZoneMode mode, int checkpoints, int next, IList<bool> hidden, bool showHidden, int[] into)
        {
            if (mode == ZoneMode.Off || into == null) return 0;
            checkpoints = Math.Max(0, checkpoints);
            bool running = next >= 0;
            if (running) showHidden = false;

            int n = 0;
            if (running && mode == ZoneMode.NextOnly)
            {
                int slot = Math.Min(next, checkpoints);
                if (slot < checkpoints && IsHidden(hidden, slot)) return 0;
                into[n++] = slot;
                return n;
            }

            into[n++] = StartSlot;
            for (int i = 0; i < checkpoints; i++)
                if (showHidden || !IsHidden(hidden, i)) into[n++] = i;
            into[n++] = checkpoints;
            return n;
        }

        /// 0 start, 1 checkpoint, 2 end - ZonePreview's colours.
        public static int KindOf(int slot, int checkpoints)
        {
            if (slot == StartSlot) return 0;
            return slot >= checkpoints ? 2 : 1;
        }

        public static bool IsHidden(IList<bool> hidden, int i)
        {
            return hidden != null && i >= 0 && i < hidden.Count && hidden[i];
        }

        /// The config text <-> mode; anything unknown is null.
        public static ZoneMode? Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            switch (text.Trim().ToLowerInvariant())
            {
                case "all": return ZoneMode.All;
                case "next": case "nextonly": case "next only": return ZoneMode.NextOnly;
                case "off": case "none": return ZoneMode.Off;
                default: return null;
            }
        }
    }
}
