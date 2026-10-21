using System;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The checkpoint markers on the comparison run line (T-0371): one per
    // split the comparison run reached, and one for its end, each labelled
    // with that split's time (and, once your run is past it, your delta).
    // This is the pure part: which times, which show, the label's text.
    // The module places them (Modules/PracticeRunModule.LineMarks), the
    // behaviour draws the dots (Game/DebugDraw RunLineBehaviour).
    // ------------------------------------------------------------------
    public static class CheckpointMarks
    {
        public const int Max = 64;

        /// The comparison's split times, then its duration (the end), into
        /// `into`; returns how many. A run that kept no splits has only its
        /// end. An unknown (NaN) split keeps its place and is not shown.
        public static int Times(float[] splits, float duration, float[] into)
        {
            int n = 0;
            int count = splits == null ? 0 : splits.Length;
            for (int i = 0; i < count && n < into.Length - 1; i++) into[n++] = splits[i];
            if (duration > 0f && !float.IsNaN(duration) && !float.IsInfinity(duration)) into[n++] = duration;
            return n;
        }

        /// A marker is shown for a real time, and - with "only the next few
        /// seconds" on - only inside that stretch of the comparison.
        public static bool Shown(float t, bool windowed, float from, float to)
        {
            if (float.IsNaN(t) || float.IsInfinity(t) || t < 0f) return false;
            return !windowed || (t >= from && t <= to);
        }

        /// The last marker your run has passed (it has a time in `times`), or
        /// -1. In the ahead-only mode that one stays up until you pass the
        /// next, so its delta can be read (T-0384).
        public static int LastPassed(int count, float[] times)
        {
            int rows = times == null ? 0 : times.Length;
            for (int i = count - 1; i >= 0; i--)
            {
                int row = Row(i, count, rows);
                if (row >= 0 && !float.IsNaN(times[row])) return i;
            }
            return -1;
        }

        /// Which row of your run's times marker `i` of `count` belongs to, or
        /// -1: the end is always your last row; a checkpoint only when both
        /// runs have the same rows.
        public static int Row(int i, int count, int rows)
        {
            if (rows <= 0) return -1;
            if (i == count - 1) return rows - 1;
            return count == rows && i < rows ? i : -1;
        }

        /// "2.333" or, past it in your run, "2.333  +0.120".
        public static string Text(float time, float delta, int timeDecimals, int deltaDecimals)
        {
            string t = SplitTable.Time(time, timeDecimals);
            if (float.IsNaN(delta) || float.IsInfinity(delta)) return t;
            return t + "  " + SplitTable.Delta(delta, deltaDecimals);
        }

        /// The fade of a label (and the dot's) at `distance` metres: 1 up to
        /// the last `share` of `range`, then down to 0 at `range` - the
        /// line's own fade (Data/RibbonPath).
        public static float Fade(float distance, float range, float share)
        {
            if (distance >= range) return 0f;
            float start = range * (1f - share);
            if (distance <= start) return 1f;
            return (range - distance) / (range - start);
        }
    }
}
