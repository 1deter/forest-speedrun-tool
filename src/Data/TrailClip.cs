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

        /// Floats per piece in Split's output: x0, y0, x1, y1, u0, u1.
        public const int PieceStride = 6;
        /// The most pieces Split makes (a corner wrap).
        public const int MaxPieces = 4;

        /// The pieces to draw for the mouse path from the newer point (px,py) back to the older (tx,ty),
        /// in a box centred (cx,cy) with half-size `half`. The mouse wraps at an edge, so a jump over more
        /// than `half` on an axis is the short way across that edge: it becomes a piece out to the edge
        /// and a piece in from the opposite edge (four on a corner wrap), each clipped to the box.
        /// `thick` keeps the quad's corners inside the box where a piece ends on an edge. Each piece is
        /// x0,y0 (newer end), x1,y1 (older end) and u0,u1, how far along the whole segment (0 newer, 1 older)
        /// those ends are - the fade. Returns the pieces written into `o` (MaxPieces * PieceStride floats).
        public static int Split(float px, float py, float tx, float ty, float cx, float cy, float half,
            float thick, float[] o)
        {
            float ox = 0f, oy = 0f;   // the newer point as seen the short way, past the edge
            if (tx - px > half) ox = 2f * half; else if (px - tx > half) ox = -2f * half;
            if (ty - py > half) oy = 2f * half; else if (py - ty > half) oy = -2f * half;
            bool wrap = ox != 0f || oy != 0f;
            int n = 0;
            for (int sa = 0; sa < 2; sa++)
                for (int sb = 0; sb < 2; sb++)
                {
                    if ((sa == 1 && ox == 0f) || (sb == 1 && oy == 0f)) continue;
                    float ax = px + (sa == 1 ? 0f : ox), ay = py + (sb == 1 ? 0f : oy);   // newer end
                    float bx = tx - (sa == 1 ? ox : 0f), by = ty - (sb == 1 ? oy : 0f);   // older end
                    float t0, t1;
                    if (!Clip(ax, ay, bx, by, cx - half, cy - half, cx + half, cy + half, out t0, out t1)) continue;
                    float dx = bx - ax, dy = by - ay;
                    if (wrap && (t0 > 0f || t1 < 1f))
                    {
                        // Cut on an edge: pull the end in so the quad's corners (thick / 2 across the line) stay inside.
                        float len = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (len < 1e-6f) continue;
                        float ix = thick * 0.5f * Math.Abs(dy) / len, iy = thick * 0.5f * Math.Abs(dx) / len;
                        if (!Clip(ax, ay, bx, by, cx - half + ix, cy - half + iy, cx + half - ix, cy + half - iy, out t0, out t1))
                            continue;
                        // keep an end that was not cut where it was
                        float u0, u1;
                        Clip(ax, ay, bx, by, cx - half, cy - half, cx + half, cy + half, out u0, out u1);
                        if (u0 <= 0f) t0 = 0f;
                        if (u1 >= 1f) t1 = 1f;
                        if (t1 <= t0) continue;
                    }
                    int i = n * PieceStride;
                    o[i] = ax + dx * t0; o[i + 1] = ay + dy * t0;
                    o[i + 2] = ax + dx * t1; o[i + 3] = ay + dy * t1;
                    o[i + 4] = t0; o[i + 5] = t1;
                    n++;
                }
            return n;
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
