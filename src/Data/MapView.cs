using System;
using System.Collections.Generic;
using UnityEngine;

namespace ForestOverlay.Data
{
    /// An axis-aligned box: view pixels (Y down) or texture UVs (Y up).
    public struct MapBox
    {
        public float X0, Y0, X1, Y1;
        public float Width { get { return X1 - X0; } }
        public float Height { get { return Y1 - Y0; } }
    }

    // ------------------------------------------------------------------
    // The Map tab's camera: a top-down view of the island, north up.
    //
    // View pixels have their origin at the map area's top-left corner,
    // +x east and +y SOUTH (GUI space, Y down); the world is (x, z).
    // `Scale` is pixels per metre. Zoomed all the way out the whole
    // island fits the view (FitScale); a centre is clamped so the view
    // never shows more than the island's edge on an axis it does not
    // already show whole.
    //
    // Pure: linked into the tests.
    // ------------------------------------------------------------------
    public sealed class MapView
    {
        /// The closest zoom: 4 px a metre (a 1024 px relief of 3.5 km is
        /// 0.3 px a metre - past this the relief is mush and spots that
        /// close are already far apart).
        public const float MaxScale = 4f;

        public float MinX, MinZ, SizeX = 1f, SizeZ = 1f;
        public float ViewW = 1f, ViewH = 1f;
        public float Scale = 1f;
        public float CentreX, CentreZ;

        public float MaxX { get { return MinX + SizeX; } }
        public float MaxZ { get { return MinZ + SizeZ; } }

        public float FitScale
        {
            get { return Math.Min(ViewW / SizeX, ViewH / SizeZ); }
        }

        /// True when zoomed all the way out.
        public bool AtFit { get { return Scale <= FitScale * 1.0001f; } }

        public void SetBounds(float minX, float minZ, float sizeX, float sizeZ)
        {
            MinX = minX; MinZ = minZ;
            SizeX = Math.Max(1f, sizeX); SizeZ = Math.Max(1f, sizeZ);
            Clamp();
        }

        /// The map area's size; the centre stays where it was.
        public void SetViewport(float w, float h)
        {
            ViewW = Math.Max(1f, w); ViewH = Math.Max(1f, h);
            Clamp();
        }

        public void Fit()
        {
            Scale = FitScale;
            CentreX = MinX + SizeX * 0.5f;
            CentreZ = MinZ + SizeZ * 0.5f;
            Clamp();
        }

        public Vector2 WorldToView(float x, float z)
        {
            return new Vector2((x - CentreX) * Scale + ViewW * 0.5f,
                               (CentreZ - z) * Scale + ViewH * 0.5f);
        }

        /// The world (x, z) under a view pixel, as a Vector2 (y = z).
        public Vector2 ViewToWorld(float px, float py)
        {
            return new Vector2((px - ViewW * 0.5f) / Scale + CentreX,
                               CentreZ - (py - ViewH * 0.5f) / Scale);
        }

        /// Zoom by `factor` keeping the world point under (px, py) there.
        public void ZoomAt(float factor, float px, float py)
        {
            if (factor <= 0f) return;
            Vector2 w = ViewToWorld(px, py);
            Scale = ClampScale(Scale * factor);
            CentreX = w.x - (px - ViewW * 0.5f) / Scale;
            CentreZ = w.y + (py - ViewH * 0.5f) / Scale;
            Clamp();
        }

        /// A drag by (dx, dy) view pixels: the map follows the pointer.
        public void Pan(float dx, float dy)
        {
            CentreX -= dx / Scale;
            CentreZ += dy / Scale;
            Clamp();
        }

        public void CentreOn(float x, float z)
        {
            CentreX = x; CentreZ = z;
            Clamp();
        }

        public float ClampScale(float s)
        {
            float lo = FitScale;
            float hi = Math.Max(lo, MaxScale);
            if (float.IsNaN(s) || s < lo) return lo;
            return s > hi ? hi : s;
        }

        public void Clamp()
        {
            Scale = ClampScale(Scale);
            CentreX = ClampAxis(CentreX, MinX, SizeX, ViewW * 0.5f / Scale);
            CentreZ = ClampAxis(CentreZ, MinZ, SizeZ, ViewH * 0.5f / Scale);
        }

        private static float ClampAxis(float c, float min, float size, float half)
        {
            if (float.IsNaN(c)) c = min + size * 0.5f;
            if (half * 2f >= size) return min + size * 0.5f;
            if (c < min + half) return min + half;
            if (c > min + size - half) return min + size - half;
            return c;
        }

        /// Where the island's picture goes in the view and which part of
        /// the texture fills it (UVs, V up = north). False when none of it
        /// is in view.
        public bool TextureWindow(out MapBox view, out MapBox uv)
        {
            Vector2 tl = WorldToView(MinX, MaxZ);
            Vector2 br = WorldToView(MaxX, MinZ);
            view = new MapBox
            {
                X0 = Math.Max(0f, tl.x), Y0 = Math.Max(0f, tl.y),
                X1 = Math.Min(ViewW, br.x), Y1 = Math.Min(ViewH, br.y)
            };
            uv = new MapBox();
            if (view.X1 <= view.X0 || view.Y1 <= view.Y0) return false;

            Vector2 a = ViewToWorld(view.X0, view.Y1);   // south-west
            Vector2 b = ViewToWorld(view.X1, view.Y0);   // north-east
            uv.X0 = (a.x - MinX) / SizeX; uv.Y0 = (a.y - MinZ) / SizeZ;
            uv.X1 = (b.x - MinX) / SizeX; uv.Y1 = (b.y - MinZ) / SizeZ;
            return true;
        }
    }

    // ------------------------------------------------------------------
    // The Map tab's geometry: clipping, picking, zone outlines, line
    // thinning. Pure: linked into the tests.
    // ------------------------------------------------------------------
    public static class MapGeometry
    {
        /// Liang-Barsky: clips a-b to the box; false when nothing is left.
        public static bool ClipSegment(ref float ax, ref float ay, ref float bx, ref float by,
                                       float x0, float y0, float x1, float y1)
        {
            float dx = bx - ax, dy = by - ay;
            float t0 = 0f, t1 = 1f;
            if (!ClipEdge(-dx, ax - x0, ref t0, ref t1)) return false;
            if (!ClipEdge(dx, x1 - ax, ref t0, ref t1)) return false;
            if (!ClipEdge(-dy, ay - y0, ref t0, ref t1)) return false;
            if (!ClipEdge(dy, y1 - ay, ref t0, ref t1)) return false;

            float sx = ax, sy = ay;
            if (t1 < 1f) { bx = sx + t1 * dx; by = sy + t1 * dy; }
            if (t0 > 0f) { ax = sx + t0 * dx; ay = sy + t0 * dy; }
            return true;
        }

        private static bool ClipEdge(float p, float q, ref float t0, ref float t1)
        {
            if (p == 0f) return q >= 0f;
            float r = q / p;
            if (p < 0f)
            {
                if (r > t1) return false;
                if (r > t0) t0 = r;
            }
            else
            {
                if (r < t0) return false;
                if (r < t1) t1 = r;
            }
            return true;
        }

        /// The index of the point nearest (px, py) within maxDist, or -1.
        /// Ties go to the later point (drawn on top).
        public static int Nearest(float[] xs, float[] ys, int count, float px, float py, float maxDist)
        {
            if (xs == null || ys == null) return -1;
            count = Math.Min(count, Math.Min(xs.Length, ys.Length));
            int best = -1;
            float bestD = maxDist * maxDist;
            for (int i = 0; i < count; i++)
            {
                float dx = xs[i] - px, dy = ys[i] - py;
                float d = dx * dx + dy * dy;
                if (d <= bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// A box zone's four corners in the world (x, z), in order around
        /// it, as TriggerEvaluator turns it (Yaw degrees about +Y).
        public static void BoxCorners(float cx, float cz, float ex, float ez, float yaw,
                                      float[] xs, float[] zs)
        {
            double a = yaw * Math.PI / 180.0;
            float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
            for (int i = 0; i < 4; i++)
            {
                float lx = (i == 0 || i == 3) ? -ex : ex;
                float lz = (i < 2) ? -ez : ez;
                xs[i] = cx + lx * c + lz * s;
                zs[i] = cz - lx * s + lz * c;
            }
        }

        /// Keeps a run's positions at least `minStep` metres apart (the last
        /// always kept), into xs / zs; returns how many. Past the arrays'
        /// length the rest is dropped but the final point still ends it.
        public static int Thin(IList<RunSample> samples, float minStep, float[] xs, float[] zs)
        {
            if (samples == null || xs == null || zs == null) return 0;
            int cap = Math.Min(xs.Length, zs.Length);
            int n = samples.Count;
            if (n == 0 || cap == 0) return 0;

            float step2 = minStep * minStep;
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = samples[i].P;
                bool last = i == n - 1;
                if (count > 0 && !last)
                {
                    float dx = p.x - xs[count - 1], dz = p.z - zs[count - 1];
                    if (dx * dx + dz * dz < step2) continue;
                }
                if (count == cap) count = cap - 1;   // full: the end replaces the tail
                xs[count] = p.x; zs[count] = p.z;
                count++;
            }
            return count;
        }

        /// A category's colour slot, stable across launches (FNV-1a of the
        /// category key, case and outer spaces ignored).
        public static int PaletteIndex(string category, int paletteSize)
        {
            if (paletteSize <= 0) return 0;
            string key = (category ?? "").Trim().ToLowerInvariant();
            uint h = 2166136261;
            for (int i = 0; i < key.Length; i++) { h ^= key[i]; h *= 16777619; }
            return (int)(h % (uint)paletteSize);
        }

        /// A spot is drawn as underground when it names a cave or stands
        /// well under the terrain at its x / z (the endgame lab, a cave the
        /// spot was not told about). `terrainY` NaN = no terrain known.
        public const float UndergroundDepth = 4f;

        public static bool IsUnderground(float spawnY, float terrainY, string cave)
        {
            if (!string.IsNullOrEmpty(cave)) return true;
            if (float.IsNaN(terrainY)) return false;
            return spawnY < terrainY - UndergroundDepth;
        }
    }
}
