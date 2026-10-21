using System;
using System.Collections.Generic;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The Runs tab's speed graph (T-0373): speed against run time for a few
    // curves. Each pixel column keeps its min and max, so no spike falls
    // between samples; a median-of-5 removes two-sample spikes from the
    // picture only (the stored run is never touched). Pure, tested.
    // ------------------------------------------------------------------
    public static class SpeedGraph
    {
        private static readonly float[] Window = new float[5];   // main thread only

        /// Median of the five samples centred on each one (clamped at the
        /// ends) into `o` (caller-owned, length >= s.Count); a spike of one or
        /// two samples disappears, a real change that lasts three or more
        /// stays. Fewer than 5 samples: copied. No allocation.
        public static void Median5(IList<RunSample> s, float[] o)
        {
            int n = s.Count;
            float[] w = Window;
            for (int i = 0; i < n; i++)
            {
                if (n < 5) { o[i] = s[i].Speed; continue; }
                int a = i - 2; if (a < 0) a = 0;
                if (a > n - 5) a = n - 5;
                for (int k = 0; k < 5; k++)
                {
                    float x = s[a + k].Speed;
                    int j = k;
                    while (j > 0 && w[j - 1] > x) { w[j] = w[j - 1]; j--; }
                    w[j] = x;
                }
                o[i] = w[2];
            }
        }

        /// Fills lo / hi (length >= columns) with the smoothed speed range of
        /// each column over 0..tMax; a column no sample lands in takes the
        /// line between its neighbours. `smooth` (length >= s.Count) is the
        /// caller's scratch for the smoothed speeds. Returns false when there
        /// is nothing to draw (fewer than 2 samples, no time span, no
        /// columns). No allocation.
        public static bool Columns(IList<RunSample> s, float tMax, int columns, float[] smooth, float[] lo, float[] hi)
        {
            if (s == null || s.Count < 2 || tMax <= 0f || columns < 2 || lo.Length < columns || hi.Length < columns || smooth.Length < s.Count) return false;
            Median5(s, smooth);
            float[] v = smooth;
            // An empty column has lo > hi.
            for (int c = 0; c < columns; c++) { lo[c] = float.MaxValue; hi[c] = float.MinValue; }
            for (int i = 0; i < s.Count; i++)
            {
                int c = ColumnOf(s[i].T, tMax, columns);
                if (c < 0) continue;
                if (v[i] < lo[c]) lo[c] = v[i];
                if (v[i] > hi[c]) hi[c] = v[i];
            }
            // Between samples: the line between them (a sparse run on a wide graph).
            int j = 0;
            for (int c = 0; c < columns; c++)
            {
                if (lo[c] <= hi[c]) continue;
                float t = c * tMax / (columns - 1);
                if (t < s[0].T || t > s[s.Count - 1].T) { lo[c] = hi[c] = 0f; continue; }
                while (j < s.Count - 2 && s[j + 1].T < t) j++;
                float t0 = s[j].T, t1 = s[j + 1].T;
                float f = t1 > t0 ? (t - t0) / (t1 - t0) : 0f;
                lo[c] = hi[c] = v[j] + (v[j + 1] - v[j]) * f;
            }
            return true;
        }

        /// -1 outside 0..tMax.
        public static int ColumnOf(float t, float tMax, int columns)
        {
            if (t < 0f || t > tMax) return -1;
            int c = (int)(t / tMax * (columns - 1) + 0.5f);
            return c < 0 ? 0 : (c >= columns ? columns - 1 : c);
        }

        /// The stored speed at time t (interpolated between samples, the
        /// unsmoothed value); false when t is outside the run.
        public static bool SpeedAt(IList<RunSample> s, float t, out float speed)
        {
            speed = 0f;
            if (s == null || s.Count == 0 || t < s[0].T || t > s[s.Count - 1].T) return false;
            if (s.Count == 1) { speed = s[0].Speed; return true; }
            int a = 0, b = s.Count - 1;
            while (b - a > 1)
            {
                int m = (a + b) / 2;
                if (s[m].T <= t) a = m; else b = m;
            }
            float t0 = s[a].T, t1 = s[b].T;
            float f = t1 > t0 ? (t - t0) / (t1 - t0) : 0f;
            speed = s[a].Speed + (s[b].Speed - s[a].Speed) * f;
            return true;
        }

        /// A top for the speed axis: the next multiple of 5 at or above top (at least 5).
        public static float AxisTop(float top)
        {
            float t = (float)Math.Ceiling(top / 5f) * 5f;
            return t < 5f ? 5f : t;
        }
    }
}
