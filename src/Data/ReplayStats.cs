using System.Collections.Generic;

namespace ForestOverlay.Data
{
    /// Lookups into a run's tracks at a replay time (T-0386): the last
    /// position sample / state snapshot at or before t (step values, as
    /// Attempt.TryStateAt). Pure; the replay bar's values (Data/ReplayValues)
    /// and the checkpoint marks' labels (Data/MarkLabels) read through them.
    public static class ReplayStats
    {
        /// The horizontal speed at t: the last position sample at or before t.
        public static bool SpeedAt(IList<RunSample> samples, float t, out float speed)
        {
            speed = 0f;
            if (samples == null || samples.Count == 0) return false;
            if (samples[0].T > t) { speed = samples[0].Speed; return true; }
            int lo = 0, hi = samples.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (samples[mid].T <= t) lo = mid; else hi = mid - 1;
            }
            speed = samples[lo].Speed;
            return true;
        }

        /// Index of the last element at or before t (-1 before the first).
        public static int IndexAt(IList<RunSample> samples, float t)
        {
            int lo = -1, hi = samples.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (samples[mid].T <= t) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        public static int IndexAt(IList<StateSample> states, float t)
        {
            int lo = -1, hi = states.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (states[mid].T <= t) lo = mid; else hi = mid - 1;
            }
            return lo;
        }
    }
}
