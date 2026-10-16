using System;
using UnityEngine;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Run lines as ribbons of constant pixel width (T-0368): the pure half.
    //
    // WHY
    // GL lines are always 1 px on D3D11 (no width), aliased and flat: a
    // thin thread lost against bright foliage (tasks/notes/T-0345.md, after
    // the ftse surf tool's wr_render.cpp EmitPath). A ribbon is a strip of
    // quads turned to face the camera, as many pixels wide at every depth.
    //
    // Per frame, for the camera given in SetView, Add turns a polyline into
    // strips of points ready for the mesh (Game/DebugDraw RunLineBehaviour
    // lays the quads across them):
    //   - near-plane clip IN WORLD SPACE: a segment crossing the camera's
    //     near plane is cut at it (a point behind the camera projects to
    //     the wrong side - the line swept across the screen while turning);
    //   - frustum cull: a segment with both ends past the same screen edge
    //     is dropped (a straight segment in front of the camera stays on
    //     its side of an edge);
    //   - distance: a point's opacity (Fade) falls to 0 over the last
    //     FadeShare of the draw distance; a segment with both ends beyond
    //     it is dropped;
    //   - decimation: a point closer than MinPixels on screen to the last
    //     one kept is skipped (a strip's last point is always kept), so a
    //     far line costs what it shows;
    //   - Sides: per point, the world vector of ONE screen pixel across the
    //     line (perpendicular to the line and to the view ray, scaled by the
    //     point's depth), mitred at a bend so the ribbon keeps its width.
    // A dropped segment ends the strip; the next one kept starts another.
    //
    // Runs every frame over every point of every line, so the maths is
    // written out on floats into arrays: with Vector3 operators and lists
    // (each a call and a copy on the game's old Mono) it cost 0.56 ms a
    // frame for a 1,491-point line (bridge 2026-10-15). Each input point is
    // projected once. Allocation free once the arrays have grown; linked
    // into the tests.
    // ------------------------------------------------------------------
    public sealed class RibbonPath
    {
        /// The kept points, strip after strip: [0, Count).
        public Vector3[] Points = new Vector3[256];
        /// Per point: one screen pixel across the line, in world units.
        public Vector3[] Sides = new Vector3[256];
        /// Per point: 0..1, the distance fade.
        public float[] Fade = new float[256];
        /// Per point: the value Add was given for it (a speed; 0 without), for colouring along the line (T-0372).
        public float[] Values = new float[256];
        public int Count;
        /// Per strip: the index of its first point (a strip ends where the
        /// next starts, the last at Count). Every strip has >= 2 points.
        public int[] Starts = new int[16];
        public int StripCount;

        private float _ex, _ey, _ez, _fx, _fy, _fz, _rx, _ry, _rz, _ux, _uy, _uz;
        private float _near, _k, _edgeX, _edgeY, _farSqr, _far, _fadeFrom, _fadeFromSqr, _minPxSqr;

        // The strip being built.
        private bool _open;
        private float _lastX, _lastY;          // screen position of the last kept point
        private bool _pending;                 // a skipped point that must end the strip
        private float _pendX, _pendY, _pendZ, _pendV;
        private float _va, _vb;                // the values of the segment's ends

        /// A bend sharper than this keeps this much of the width at least
        /// (a mitre grows as 1 / cos of half the turn; capped at 2x).
        private const float MinMitreCos = 0.5f;

        /// The view for this frame. `pixelsPerUnit`: screen pixels per world
        /// unit at depth 1 (screen height / (2 tan(vertical fov / 2)));
        /// `halfWidth` / `halfHeight`: half the screen in pixels plus a margin;
        /// `drawDistance` 0 = no limit.
        public void SetView(Vector3 eye, Vector3 forward, Vector3 right, Vector3 up, float near,
                            float pixelsPerUnit, float halfWidth, float halfHeight,
                            float drawDistance, float fadeShare, float minPixels)
        {
            _ex = eye.x; _ey = eye.y; _ez = eye.z;
            _fx = forward.x; _fy = forward.y; _fz = forward.z;
            _rx = right.x; _ry = right.y; _rz = right.z;
            _ux = up.x; _uy = up.y; _uz = up.z;
            _near = near > 1e-4f ? near : 1e-4f;
            _k = pixelsPerUnit;
            _edgeX = halfWidth;
            _edgeY = halfHeight;
            _far = drawDistance > 0f ? drawDistance : float.MaxValue;
            _farSqr = drawDistance > 0f ? drawDistance * drawDistance : float.MaxValue;
            _fadeFrom = drawDistance > 0f ? drawDistance * (1f - Mathf.Clamp(fadeShare, 0f, 1f)) : float.MaxValue;
            _fadeFromSqr = drawDistance > 0f ? _fadeFrom * _fadeFrom : float.MaxValue;
            _minPxSqr = minPixels * minPixels;
        }

        public void Clear()
        {
            Count = 0;
            StripCount = 0;
            _open = false;
            _pending = false;
        }

        /// Appends points[start..end) (end clamped to the array) as strips.
        public void Add(Vector3[] points, int start, int end) { Add(points, null, start, end); }

        /// Same, with a value per point (index-aligned with `points`; null =
        /// all 0) carried to the kept points in Values.
        public void Add(Vector3[] points, float[] values, int start, int end)
        {
            if (points == null) return;
            if (start < 0) start = 0;
            if (end > points.Length) end = points.Length;
            if (end - start < 2) return;

            // The point before (a): world, depth, distance squared, screen.
            float ax = points[start].x, ay = points[start].y, az = points[start].z;
            float adx = ax - _ex, ady = ay - _ey, adz = az - _ez;
            float za = adx * _fx + ady * _fy + adz * _fz;
            float da = adx * adx + ady * ady + adz * adz;
            float asx = 0f, asy = 0f;
            if (za >= _near) { asx = (adx * _rx + ady * _ry + adz * _rz) * _k / za; asy = (adx * _ux + ady * _uy + adz * _uz) * _k / za; }

            float va = values != null ? values[start] : 0f;
            for (int i = start + 1; i < end; i++)
            {
                float vb = values != null ? values[i] : 0f;
                _va = va; _vb = vb;
                float bx = points[i].x, by = points[i].y, bz = points[i].z;
                float bdx = bx - _ex, bdy = by - _ey, bdz = bz - _ez;
                float zb = bdx * _fx + bdy * _fy + bdz * _fz;
                float db = bdx * bdx + bdy * bdy + bdz * bdz;
                float bsx = 0f, bsy = 0f;
                if (zb >= _near) { bsx = (bdx * _rx + bdy * _ry + bdz * _rz) * _k / zb; bsy = (bdx * _ux + bdy * _uy + bdz * _uz) * _k / zb; }

                Segment(ax, ay, az, za, da, asx, asy, bx, by, bz, zb, db, bsx, bsy);

                ax = bx; ay = by; az = bz; za = zb; da = db; asx = bsx; asy = bsy; va = vb;
            }
            Close();
        }

        private void Segment(float ax, float ay, float az, float za, float da, float asx, float asy,
                             float bx, float by, float bz, float zb, float db, float bsx, float bsy)
        {
            if (za < _near && zb < _near) { Close(); return; }
            if (da > _farSqr && db > _farSqr) { Close(); return; }

            bool cutA = false, cutB = false;
            if (za < _near || zb < _near)
            {
                // The point on the near plane, and where it lands on screen.
                float t = (_near - za) / (zb - za);
                float cx = ax + (bx - ax) * t, cy = ay + (by - ay) * t, cz = az + (bz - az) * t;
                float cdx = cx - _ex, cdy = cy - _ey, cdz = cz - _ez;
                float csx = (cdx * _rx + cdy * _ry + cdz * _rz) * _k / _near;
                float csy = (cdx * _ux + cdy * _uy + cdz * _uz) * _k / _near;
                if (za < _near) { ax = cx; ay = cy; az = cz; asx = csx; asy = csy; cutA = true; }
                else { bx = cx; by = cy; bz = cz; bsx = csx; bsy = csy; cutB = true; }
            }

            if ((asx < -_edgeX && bsx < -_edgeX) || (asx > _edgeX && bsx > _edgeX) ||
                (asy < -_edgeY && bsy < -_edgeY) || (asy > _edgeY && bsy > _edgeY))
            {
                Close();
                return;
            }

            // A cut start is a new point on the near plane: the strip
            // before ended at another one (or behind the camera).
            if (cutA) Close();
            if (!_open)
            {
                _open = true;
                if (StripCount == Starts.Length) Array.Resize(ref Starts, Starts.Length * 2);
                Starts[StripCount++] = Count;
                Keep(ax, ay, az, asx, asy, _va);
            }

            float dx = bsx - _lastX, dy = bsy - _lastY;
            if (cutB || dx * dx + dy * dy >= _minPxSqr) Keep(bx, by, bz, bsx, bsy, _vb);
            else { _pending = true; _pendX = bx; _pendY = by; _pendZ = bz; _pendV = _vb; }

            if (cutB) Close();
        }

        private void Keep(float x, float y, float z, float sx, float sy, float v)
        {
            if (Count == Points.Length)
            {
                int n = Points.Length * 2;
                Array.Resize(ref Points, n);
                Array.Resize(ref Sides, n);
                Array.Resize(ref Fade, n);
                Array.Resize(ref Values, n);
            }
            Points[Count].x = x; Points[Count].y = y; Points[Count].z = z;
            Values[Count] = v;
            Count++;
            _lastX = sx;
            _lastY = sy;
            _pending = false;
        }

        /// Ends the open strip: its last skipped point kept, its sides and
        /// fades worked out; a strip of one point is dropped.
        private void Close()
        {
            if (!_open) return;
            _open = false;
            if (_pending) Keep(_pendX, _pendY, _pendZ, 0f, 0f, _pendV);

            int first = Starts[StripCount - 1];
            if (Count - first < 2)
            {
                Count = first;
                StripCount--;
                return;
            }

            int last = Count - 1;
            for (int j = first; j <= last; j++)
            {
                float px = Points[j].x, py = Points[j].y, pz = Points[j].z;
                float vx = px - _ex, vy = py - _ey, vz = pz - _ez;

                // Across the segment before and the one after: cross(along, view), unit.
                float sax = 0f, say = 0f, saz = 0f, sbx = 0f, sby = 0f, sbz = 0f;
                bool hasA = false, hasB = false;
                if (j > first)
                {
                    float tx = px - Points[j - 1].x, ty = py - Points[j - 1].y, tz = pz - Points[j - 1].z;
                    sax = ty * vz - tz * vy; say = tz * vx - tx * vz; saz = tx * vy - ty * vx;
                    float m = sax * sax + say * say + saz * saz;
                    if (m > 1e-12f) { m = 1f / (float)Math.Sqrt(m); sax *= m; say *= m; saz *= m; hasA = true; }
                }
                if (j < last)
                {
                    float tx = Points[j + 1].x - px, ty = Points[j + 1].y - py, tz = Points[j + 1].z - pz;
                    sbx = ty * vz - tz * vy; sby = tz * vx - tx * vz; sbz = tx * vy - ty * vx;
                    float m = sbx * sbx + sby * sby + sbz * sbz;
                    if (m > 1e-12f) { m = 1f / (float)Math.Sqrt(m); sbx *= m; sby *= m; sbz *= m; hasB = true; }
                }

                float sx, sy, sz;
                if (hasA && hasB)
                {
                    sx = sax + sbx; sy = say + sby; sz = saz + sbz;
                    float m = sx * sx + sy * sy + sz * sz;
                    if (m < 1e-12f) { sx = sax; sy = say; sz = saz; }   // turned right round: the one before
                    else
                    {
                        m = 1f / (float)Math.Sqrt(m);
                        sx *= m; sy *= m; sz *= m;
                        float c = sx * sax + sy * say + sz * saz;
                        float mitre = 1f / (c > MinMitreCos ? c : MinMitreCos);
                        sx *= mitre; sy *= mitre; sz *= mitre;
                    }
                }
                else if (hasA) { sx = sax; sy = say; sz = saz; }
                else if (hasB) { sx = sbx; sy = sby; sz = sbz; }
                else { sx = _rx; sy = _ry; sz = _rz; }     // the line points straight at the camera

                float px1 = (vx * _fx + vy * _fy + vz * _fz) / _k;   // one pixel at this depth
                Sides[j].x = sx * px1; Sides[j].y = sy * px1; Sides[j].z = sz * px1;

                float d2 = vx * vx + vy * vy + vz * vz;
                if (d2 <= _fadeFromSqr) Fade[j] = 1f;
                else
                {
                    float d = (float)Math.Sqrt(d2);
                    Fade[j] = Mathf.Clamp((_far - d) / (_far - _fadeFrom), 0f, 1f);
                }
            }
        }
    }
}
