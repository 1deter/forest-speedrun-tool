using System;
using System.Collections.Generic;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Snapping while a HUD piece is dragged in Edit HUD mode (T-0020,
    // author 2026-10-11): within Threshold px, the dragged box's left edge,
    // centre or right edge (top, middle, bottom) jumps to
    //
    //   - 25 / 50 / 75 % of the screen (50 % = the screen's centre lines);
    //   - another piece's edges and centre (the placed values, the column,
    //     the splits and results panels).
    //
    // The two axes snap on their own: x to the nearest x line, y to the
    // nearest y line. The nearest line wins; on a tie the screen's lines
    // come first, then the other pieces in the order given, and the box's
    // start edge before its centre before its end edge. A line that would
    // push the box off the screen is not a candidate. Holding Alt drags
    // freely (the caller skips this). Pure; linked into the tests.
    // ------------------------------------------------------------------
    public struct SnapBox
    {
        public float X, Y, W, H;

        public SnapBox(float x, float y, float w, float h)
        {
            X = x; Y = y; W = w; H = h;
        }
    }

    public struct SnapResult
    {
        /// Where the box goes (its top-left corner).
        public float X, Y;
        /// Which axes snapped; the guide line is drawn at GuideX / GuideY.
        public bool SnappedX, SnappedY;
        public float GuideX, GuideY;
    }

    public static class HudSnap
    {
        /// How close (px) an edge or centre has to come to a line to snap (author: ~8 px).
        public const float Threshold = 8f;

        /// The screen's lines, as fractions of its width / height.
        public static readonly float[] ScreenStops = { 0.25f, 0.5f, 0.75f };

        /// `box` moved to where the mouse put it; `others` are the other
        /// pieces on screen (the box itself not among them).
        public static SnapResult Snap(SnapBox box, float screenW, float screenH, IList<SnapBox> others, float threshold)
        {
            SnapResult r = new SnapResult();
            r.X = box.X;
            r.Y = box.Y;
            float pos, guide;
            if (Axis(box.X, box.W, screenW, others, true, threshold, out pos, out guide))
            {
                r.X = pos;
                r.SnappedX = true;
                r.GuideX = guide;
            }
            if (Axis(box.Y, box.H, screenH, others, false, threshold, out pos, out guide))
            {
                r.Y = pos;
                r.SnappedY = true;
                r.GuideY = guide;
            }
            return r;
        }

        // One axis: the nearest (line, anchor) pair within the threshold.
        private static bool Axis(float start, float size, float screen, IList<SnapBox> others, bool x,
                                 float threshold, out float pos, out float guide)
        {
            pos = start;
            guide = 0f;
            if (float.IsNaN(start) || threshold <= 0f) return false;
            float best = float.MaxValue;
            bool found = false;

            for (int i = 0; i < ScreenStops.Length; i++)
                Try(screen * ScreenStops[i], start, size, screen, threshold, ref best, ref found, ref pos, ref guide);

            if (others != null)
            {
                for (int i = 0; i < others.Count; i++)
                {
                    SnapBox o = others[i];
                    float os = x ? o.X : o.Y;
                    float ol = x ? o.W : o.H;
                    if (ol <= 0f) continue;
                    Try(os, start, size, screen, threshold, ref best, ref found, ref pos, ref guide);
                    Try(os + ol * 0.5f, start, size, screen, threshold, ref best, ref found, ref pos, ref guide);
                    Try(os + ol, start, size, screen, threshold, ref best, ref found, ref pos, ref guide);
                }
            }
            return found;
        }

        // The box's start, centre and end against `line`.
        private static void Try(float line, float start, float size, float screen, float threshold,
                                ref float best, ref bool found, ref float pos, ref float guide)
        {
            for (int a = 0; a < 3; a++)
            {
                float offset = a == 0 ? 0f : a == 1 ? size * 0.5f : size;
                float d = Math.Abs(line - (start + offset));
                if (d > threshold || d >= best) continue;
                float at = line - offset;
                if (at < 0f || at + size > Math.Max(size, screen)) continue;   // off the screen
                best = d;
                found = true;
                pos = at;
                guide = line;
            }
        }
    }
}
