using System;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The input overlay's Comet mouse (T-0385): a head that stays put and a
    // fading tail of where the mouse came from, drawn relative to the head.
    // Pure maths, linked into the tests: the fade, which points are alive,
    // and one uniform scale that fits a long tail into the tile (so a long
    // flick keeps its direction and shape).
    // ------------------------------------------------------------------
    public static class CometPath
    {
        /// 1 at age 0 down to 0 at span seconds (and beyond).
        public static float Fade(float age, float span)
        {
            if (span <= 0f || age >= span) return 0f;
            if (age <= 0f) return 1f;
            return 1f - age / span;
        }

        /// How many points of a ring buffer (newest at head - 1) are younger than span.
        public static int Live(float[] t, int head, int count, int max, float now, float span)
        {
            int n = 0;
            for (int k = 0; k < count; k++)
            {
                int idx = ((head - 1 - k) % max + max) % max;
                if (now - t[idx] >= span) break;   // older entries are older still
                n++;
            }
            return n;
        }

        /// The largest |x| or |y| of the live points relative to the head (hx, hy); 0 with none.
        public static float Extent(float[] x, float[] y, float[] t, int head, int count, int max, float now, float span, float hx, float hy)
        {
            int n = Live(t, head, count, max, now, span);
            float e = 0f;
            for (int k = 0; k < n; k++)
            {
                int idx = ((head - 1 - k) % max + max) % max;
                e = Math.Max(e, Math.Max(Math.Abs(x[idx] - hx), Math.Abs(y[idx] - hy)));
            }
            return e;
        }

        /// The scale that fits a tail of this extent into room: 1 when it already fits, else room / extent (one scale for both axes).
        public static float FitScale(float extent, float room)
        {
            if (extent <= room || extent <= 0f) return 1f;
            return room / extent;
        }
    }
}
