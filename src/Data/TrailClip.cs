using System;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Clips a line to a box (Liang-Barsky) - the input overlay's mouse
    // path, where a segment that wraps at an edge is drawn as pieces that
    // each stay inside the box. Pure: linked into the tests.
    // ------------------------------------------------------------------
    public static class TrailClip
    {
        /// The part of (x0,y0)-(x1,y1) inside [minX,maxX] x [minY,maxY], as
        /// fractions t0..t1 of the segment. False when none of it is inside.
        public static bool Clip(float x0, float y0, float x1, float y1,
            float minX, float minY, float maxX, float maxY, out float t0, out float t1)
        {
            t0 = 0f; t1 = 1f;
            float dx = x1 - x0, dy = y1 - y0;
            return Edge(-dx, x0 - minX, ref t0, ref t1)
                && Edge(dx, maxX - x0, ref t0, ref t1)
                && Edge(-dy, y0 - minY, ref t0, ref t1)
                && Edge(dy, maxY - y0, ref t0, ref t1)
                && t1 > t0;
        }

        private static bool Edge(float p, float q, ref float t0, ref float t1)
        {
            if (Math.Abs(p) < 1e-9f) return q >= 0f;
            float r = q / p;
            if (p < 0f) { if (r > t1) return false; if (r > t0) t0 = r; }
            else { if (r < t0) return false; if (r < t1) t1 = r; }
            return true;
        }
    }
}
