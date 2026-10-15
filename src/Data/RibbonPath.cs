using System;
using System.Collections.Generic;
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
    // Pure and allocation free once the lists have grown; linked into the
    // tests.
    // ------------------------------------------------------------------
    public sealed class RibbonPath
    {
        /// The kept points, strip after strip.
        public readonly List<Vector3> Points = new List<Vector3>();
        /// Per point: one screen pixel across the line, in world units.
        public readonly List<Vector3> Sides = new List<Vector3>();
        /// Per point: 0..1, the distance fade.
        public readonly List<float> Fade = new List<float>();
        /// Per strip: the index of its first point (a strip ends where the
        /// next starts, the last at Points.Count). Every strip has >= 2.
        public readonly List<int> Starts = new List<int>();

        private Vector3 _eye, _fwd, _right, _up;
        private float _near, _k, _edgeX, _edgeY, _far, _fadeFrom, _minPxSqr;

        // The strip being built.
        private bool _open;
        private float _lastX, _lastY;          // screen position of the last kept point
        private bool _pending;                 // a skipped point that must end the strip
        private Vector3 _pendingP;

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
            _eye = eye;
            _fwd = forward;
            _right = right;
            _up = up;
            _near = near > 1e-4f ? near : 1e-4f;
            _k = pixelsPerUnit;
            _edgeX = halfWidth;
            _edgeY = halfHeight;
            _far = drawDistance > 0f ? drawDistance : float.MaxValue;
            _fadeFrom = drawDistance > 0f ? drawDistance * (1f - Mathf.Clamp(fadeShare, 0f, 1f)) : float.MaxValue;
            _minPxSqr = minPixels * minPixels;
        }

        public void Clear()
        {
            Points.Clear();
            Sides.Clear();
            Fade.Clear();
            Starts.Clear();
            _open = false;
            _pending = false;
        }

        /// Appends points[start..end) (end clamped to the array) as strips.
        public void Add(Vector3[] points, int start, int end)
        {
            if (points == null) return;
            if (start < 0) start = 0;
            if (end > points.Length) end = points.Length;
            if (end - start < 2) return;

            float farSqr = _far >= float.MaxValue ? float.MaxValue : _far * _far;
            for (int i = start + 1; i < end; i++)
            {
                Vector3 a = points[i - 1], b = points[i];
                float za = Depth(a), zb = Depth(b);
                if (za < _near && zb < _near) { Close(); continue; }
                if ((a - _eye).sqrMagnitude > farSqr && (b - _eye).sqrMagnitude > farSqr) { Close(); continue; }

                bool cutA = false, cutB = false;
                if (za < _near) { a = a + (b - a) * ((_near - za) / (zb - za)); za = _near; cutA = true; }
                else if (zb < _near) { b = a + (b - a) * ((_near - za) / (zb - za)); zb = _near; cutB = true; }

                float ax = Dot(a - _eye, _right) * _k / za, ay = Dot(a - _eye, _up) * _k / za;
                float bx = Dot(b - _eye, _right) * _k / zb, by = Dot(b - _eye, _up) * _k / zb;
                if ((ax < -_edgeX && bx < -_edgeX) || (ax > _edgeX && bx > _edgeX) ||
                    (ay < -_edgeY && by < -_edgeY) || (ay > _edgeY && by > _edgeY))
                {
                    Close();
                    continue;
                }

                // A cut start is a new point on the near plane: the strip
                // before ended at another one (or behind the camera).
                if (cutA) Close();
                if (!_open)
                {
                    _open = true;
                    Starts.Add(Points.Count);
                    Keep(a, ax, ay);
                }

                float dx = bx - _lastX, dy = by - _lastY;
                if (cutB || dx * dx + dy * dy >= _minPxSqr) Keep(b, bx, by);
                else { _pending = true; _pendingP = b; }

                if (cutB) Close();
            }
            Close();
        }

        private void Keep(Vector3 p, float x, float y)
        {
            Points.Add(p);
            _lastX = x;
            _lastY = y;
            _pending = false;
        }

        /// Ends the open strip: its last skipped point kept, its sides and
        /// fades worked out; a strip of one point is dropped.
        private void Close()
        {
            if (!_open) return;
            _open = false;
            if (_pending) { Points.Add(_pendingP); _pending = false; }

            int first = Starts[Starts.Count - 1];
            int count = Points.Count - first;
            if (count < 2)
            {
                Points.RemoveRange(first, count);
                Starts.RemoveAt(Starts.Count - 1);
                return;
            }

            for (int j = first; j < Points.Count; j++)
            {
                Vector3 p = Points[j];
                Vector3 v = p - _eye;
                Vector3 sa = j > first ? Across(Points[j] - Points[j - 1], v) : Vector3.zero;
                Vector3 sb = j < Points.Count - 1 ? Across(Points[j + 1] - Points[j], v) : Vector3.zero;

                Vector3 s;
                bool hasA = sa.sqrMagnitude > 0f, hasB = sb.sqrMagnitude > 0f;
                if (hasA && hasB)
                {
                    s = Unit(sa + sb);
                    // Turned right round: the two cancel - take the one before.
                    if (s.sqrMagnitude == 0f) s = sa;
                    else
                    {
                        float c = Dot(s, sa);
                        s = s * (1f / (c > MinMitreCos ? c : MinMitreCos));
                    }
                }
                else if (hasA) s = sa;
                else if (hasB) s = sb;
                else s = _right;     // the line points straight at the camera

                float z = Depth(p);
                Sides.Add(s * (z / _k));

                float d = (float)Math.Sqrt(v.sqrMagnitude);
                Fade.Add(d <= _fadeFrom ? 1f : Mathf.Clamp((_far - d) / (_far - _fadeFrom), 0f, 1f));
            }
        }

        /// The unit vector across a segment as seen from the eye, zero when
        /// the segment lies along the view ray.
        private static Vector3 Across(Vector3 along, Vector3 view)
        {
            return Unit(Vector3.Cross(along, view));
        }

        private static Vector3 Unit(Vector3 v)
        {
            float m = v.sqrMagnitude;
            if (m < 1e-12f) return Vector3.zero;
            return v * (1f / (float)Math.Sqrt(m));
        }

        private float Depth(Vector3 p)
        {
            return Dot(p - _eye, _fwd);
        }

        private static float Dot(Vector3 a, Vector3 b)
        {
            return a.x * b.x + a.y * b.y + a.z * b.z;
        }
    }
}
