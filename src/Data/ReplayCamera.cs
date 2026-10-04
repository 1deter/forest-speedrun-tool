using System;
using System.Collections.Generic;
using UnityEngine;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The replay camera's and the ghost figure's maths (docs/run-audit-
    // and-replays.md part 2: the "grenade camera", first-person replays, a
    // ghost with a body). Pure: Vector3 arithmetic and System.Math only, so
    // the tests run it with the Vector3 / Mathf shim. The game side
    // (Modules/PracticeRunModule.Camera, Game/DebugDraw) reads a pose here
    // and places the game's own camera through the freecam's handling.
    //
    // Angles are Unity's: yaw 0 = +z, 90 = +x; pitch positive = looking
    // down. A rotation by yaw maps local (x right, y up, z forward) to
    // (x cos + z sin, y, -x sin + z cos).
    // ------------------------------------------------------------------

    /// Where the ghost is at a replay time, and where it looked.
    public struct GhostPose
    {
        public Vector3 P;
        public float Yaw;
        public float Pitch;
        /// The view's height above P (the recorded one, else DefaultEye).
        public float Eye;
        /// False: the run has no look track - Yaw is where it moved, Pitch 0.
        public bool HasLook;
    }

    /// A camera pose: position and look angles (no roll).
    public struct CamPose
    {
        public Vector3 Position;
        public float Pitch;
        public float Yaw;
    }

    public enum ReplayView { Chase, FirstPerson, Trajectory }

    public static class ReplayCamera
    {
        public const float DefaultEye = 1.6f;
        public const float DefaultHeight = 1.8f;

        // Chase: behind and above, looking at the ghost's chest.
        public const float ChaseDistance = 4.5f;
        public const float ChaseHeight = 2.2f;
        public const float ChaseRate = 6f;

        // Heading of a run with no look track: where it moved over this
        // window; a move shorter than MinHeadingMove keeps the last yaw.
        public const float HeadingWindow = 0.25f;
        public const float MinHeadingMove = 0.05f;

        // Trajectory: side-on, framing the path from Back s before to Ahead s after.
        public const float TrajectoryBack = 2f;
        public const float TrajectoryAhead = 4f;
        public const float TrajectoryRate = 2.5f;
        public const float TrajectoryMargin = 1.3f;
        public const float TrajectoryMinDistance = 6f;

        /// A target further than this from the camera is cut to, not
        /// flown to (a seek, a mode change, a teleport in the run).
        public const float SnapDistance = 60f;

        private const float Deg = (float)(180.0 / Math.PI);
        private const float Rad = (float)(Math.PI / 180.0);

        // --- angles ------------------------------------------------------------

        /// An angle in (-180, 180].
        public static float Wrap180(float a)
        {
            a %= 360f;
            if (a > 180f) a -= 360f;
            if (a <= -180f) a += 360f;
            return a;
        }

        /// The shortest turn from `from` to `to`.
        public static float DeltaAngle(float from, float to) { return Wrap180(to - from); }

        public static float LerpAngle(float a, float b, float f) { return a + DeltaAngle(a, b) * f; }

        /// The horizontal forward of a yaw.
        public static Vector3 Forward(float yaw)
        {
            double y = yaw * Rad;
            return new Vector3((float)Math.Sin(y), 0f, (float)Math.Cos(y));
        }

        /// The horizontal right of a yaw.
        public static Vector3 Right(float yaw)
        {
            double y = yaw * Rad;
            return new Vector3((float)Math.Cos(y), 0f, -(float)Math.Sin(y));
        }

        /// The view direction of a pitch / yaw.
        public static Vector3 Direction(float pitch, float yaw)
        {
            double p = pitch * Rad, y = yaw * Rad;
            float c = (float)Math.Cos(p);
            return new Vector3(c * (float)Math.Sin(y), -(float)Math.Sin(p), c * (float)Math.Cos(y));
        }

        /// The view's up of a pitch / yaw (no roll).
        public static Vector3 Up(float pitch, float yaw)
        {
            double p = pitch * Rad, y = yaw * Rad;
            float s = (float)Math.Sin(p);
            return new Vector3(s * (float)Math.Sin(y), (float)Math.Cos(p), s * (float)Math.Cos(y));
        }

        /// The angles that look from `from` at `to` (unchanged when they are the same point).
        public static void LookAt(Vector3 from, Vector3 to, ref float pitch, ref float yaw)
        {
            Vector3 d = to - from;
            float flat = (float)Math.Sqrt(d.x * d.x + d.z * d.z);
            if (flat < 1e-4f && Math.Abs(d.y) < 1e-4f) return;
            if (flat >= 1e-4f) yaw = (float)Math.Atan2(d.x, d.z) * Deg;
            pitch = -(float)Math.Atan2(d.y, flat) * Deg;
        }

        // --- the ghost at a time ---------------------------------------------------

        /// The first sample index with T >= t (Count when none).
        public static int SampleIndex(IList<RunSample> s, float t)
        {
            int lo = 0, hi = s.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (s[mid].T < t) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        private static int LookIndex(IList<LookSample> s, float t)
        {
            int lo = 0, hi = s.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (s[mid].T < t) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        /// The run's last time: its duration, else its last sample's.
        public static float EndOf(Attempt a)
        {
            if (a == null || a.Samples.Count == 0) return 0f;
            float last = a.Samples[a.Samples.Count - 1].T;
            return a.Duration > 0f ? Math.Min(a.Duration, Math.Max(last, 0f)) : last;
        }

        /// Position at t, interpolated; t is clamped to the run.
        public static Vector3 PositionAt(IList<RunSample> s, float t)
        {
            if (s == null || s.Count == 0) return Vector3.zero;
            int i = SampleIndex(s, t);
            if (i <= 0) return s[0].P;
            if (i >= s.Count) return s[s.Count - 1].P;
            float span = s[i].T - s[i - 1].T;
            float f = span <= 0f ? 1f : (t - s[i - 1].T) / span;
            return s[i - 1].P + (s[i].P - s[i - 1].P) * f;
        }

        /// The ghost at t: position, and the recorded look (interpolated, the
        /// short way round) - or, with no look track, the way it moved
        /// (`lastYaw` while it stands still), pitch 0 and the default eye.
        public static bool PoseAt(Attempt a, float t, float lastYaw, out GhostPose pose)
        {
            pose = default(GhostPose);
            if (a == null || a.Samples.Count == 0) return false;
            float end = EndOf(a);
            if (t > end) t = end;
            if (t < 0f) t = 0f;
            pose.P = PositionAt(a.Samples, t);

            if (a.Looks.Count > 0)
            {
                int i = LookIndex(a.Looks, t);
                LookSample l;
                if (i <= 0) l = a.Looks[0];
                else if (i >= a.Looks.Count) l = a.Looks[a.Looks.Count - 1];
                else
                {
                    LookSample l0 = a.Looks[i - 1], l1 = a.Looks[i];
                    float span = l1.T - l0.T;
                    float f = span <= 0f ? 1f : (t - l0.T) / span;
                    l.T = t;
                    l.Yaw = LerpAngle(l0.Yaw, l1.Yaw, f);
                    l.Pitch = l0.Pitch + (l1.Pitch - l0.Pitch) * f;
                    l.Eye = l0.Eye + (l1.Eye - l0.Eye) * f;
                }
                pose.Yaw = Wrap180(l.Yaw);
                pose.Pitch = l.Pitch;
                pose.Eye = l.Eye;
                pose.HasLook = true;
                return true;
            }

            pose.Yaw = HeadingAt(a, t, lastYaw);
            pose.Pitch = 0f;
            pose.Eye = DefaultEye;
            return true;
        }

        /// Where the run moved around t (horizontal), else `lastYaw`.
        public static float HeadingAt(Attempt a, float t, float lastYaw)
        {
            Vector3 p0 = PositionAt(a.Samples, t - HeadingWindow);
            Vector3 p1 = PositionAt(a.Samples, t + HeadingWindow);
            float dx = p1.x - p0.x, dz = p1.z - p0.z;
            if (dx * dx + dz * dz < MinHeadingMove * MinHeadingMove) return lastYaw;
            return (float)Math.Atan2(dx, dz) * Deg;
        }

        /// The figure's height: the recorded eye + the top of the head
        /// (a crouch is shorter), else the default.
        public static float FigureHeight(GhostPose g)
        {
            if (!g.HasLook) return DefaultHeight;
            float h = g.Eye + 0.13f;
            return h < 0.9f ? 0.9f : (h > 2.2f ? 2.2f : h);
        }

        // --- the camera views ------------------------------------------------------

        /// Behind and above the ghost (along its yaw), looking at its chest.
        public static CamPose Chase(GhostPose g)
        {
            float h = FigureHeight(g);
            CamPose c;
            c.Position = g.P - Forward(g.Yaw) * ChaseDistance + new Vector3(0f, ChaseHeight * h / DefaultHeight, 0f);
            c.Pitch = 0f;
            c.Yaw = g.Yaw;
            LookAt(c.Position, g.P + new Vector3(0f, h * 0.7f, 0f), ref c.Pitch, ref c.Yaw);
            return c;
        }

        /// At the ghost's eye, with its recorded look.
        public static CamPose FirstPerson(GhostPose g)
        {
            CamPose c;
            c.Position = g.P + new Vector3(0f, g.HasLook ? g.Eye : DefaultEye, 0f);
            c.Pitch = Math.Max(-89f, Math.Min(89f, g.Pitch));
            c.Yaw = g.Yaw;
            return c;
        }

        /// Side-on to the path from t - back to t + ahead, far enough that
        /// it all fits a view of `vfov` degrees (vertical) and `aspect`.
        /// The path's points (with the ghost's) go into `window` - thinned
        /// to fit - for drawing; `count` says how many.
        public static CamPose Trajectory(Attempt a, float t, float back, float ahead, float vfov, float aspect,
                                         float yaw, Vector3[] window, out int count)
        {
            count = 0;
            CamPose c = default(CamPose);
            if (a == null || a.Samples.Count == 0) return c;

            float end = EndOf(a);
            float from = Math.Max(0f, t - back), to = Math.Min(end, t + ahead);
            int i0 = SampleIndex(a.Samples, from);
            int i1 = SampleIndex(a.Samples, to);
            if (i1 >= a.Samples.Count) i1 = a.Samples.Count - 1;
            int n = Math.Max(0, i1 - i0 + 1);
            int cap = window != null ? window.Length : 0;
            int step = cap > 2 && n > cap - 2 ? (n + cap - 3) / (cap - 2) : 1;

            Vector3 first = PositionAt(a.Samples, from);
            Vector3 last = PositionAt(a.Samples, to);
            Vector3 lo = first, hi = first;
            Grow(ref lo, ref hi, last);
            if (cap > 0) window[count++] = first;
            for (int i = i0; i <= i1 && i < a.Samples.Count; i += step)
            {
                Vector3 p = a.Samples[i].P;
                Grow(ref lo, ref hi, p);
                if (count < cap - 1) window[count++] = p;
            }
            if (count < cap) window[count++] = last;

            // The run's main direction over the window; a path that goes
            // nowhere (standing, straight up) uses the ghost's facing.
            Vector3 dir = new Vector3(last.x - first.x, 0f, last.z - first.z);
            float len = dir.magnitude;
            dir = len >= 1f ? dir * (1f / len) : Forward(yaw);
            Vector3 side = new Vector3(dir.z, 0f, -dir.x);   // the right of the travel

            // The body stands on the path: frame its height too.
            hi.y += DefaultHeight;

            Vector3 centre = (lo + hi) * 0.5f;
            float alongMin = float.MaxValue, alongMax = float.MinValue, sideMax = 0f;
            for (int k = 0; k < 8; k++)
            {
                Vector3 corner = new Vector3((k & 1) == 0 ? lo.x : hi.x, (k & 2) == 0 ? lo.y : hi.y, (k & 4) == 0 ? lo.z : hi.z) - centre;
                float along = corner.x * dir.x + corner.z * dir.z;
                float across = Math.Abs(corner.x * side.x + corner.z * side.z);
                if (along < alongMin) alongMin = along;
                if (along > alongMax) alongMax = along;
                if (across > sideMax) sideMax = across;
            }
            float halfLength = (alongMax - alongMin) * 0.5f;
            float halfHeight = (hi.y - lo.y) * 0.5f;

            double tanV = Math.Tan(Math.Max(10f, Math.Min(150f, vfov)) * 0.5 * Rad);
            double tanH = tanV * Math.Max(0.3f, aspect);
            float dist = (float)Math.Max(halfLength / tanH, halfHeight / tanV) * TrajectoryMargin + sideMax;
            if (dist < TrajectoryMinDistance) dist = TrajectoryMinDistance;

            c.Position = centre + side * dist + new Vector3(0f, dist * 0.1f, 0f);
            c.Pitch = 0f;
            c.Yaw = yaw;
            LookAt(c.Position, centre, ref c.Pitch, ref c.Yaw);
            return c;
        }

        private static void Grow(ref Vector3 lo, ref Vector3 hi, Vector3 p)
        {
            if (p.x < lo.x) lo.x = p.x; if (p.y < lo.y) lo.y = p.y; if (p.z < lo.z) lo.z = p.z;
            if (p.x > hi.x) hi.x = p.x; if (p.y > hi.y) hi.y = p.y; if (p.z > hi.z) hi.z = p.z;
        }

        // --- smoothing -----------------------------------------------------------

        /// The share of the way to a target covered in dt at `rate` per
        /// second - frame-rate independent (1 - e^(-rate dt)).
        public static float Alpha(float rate, float dt)
        {
            if (rate <= 0f) return 1f;
            if (dt <= 0f) return 0f;
            return 1f - (float)Math.Exp(-rate * dt);
        }

        /// Moves `current` toward `target`; a target further than
        /// SnapDistance (or a rate of 0) is taken at once.
        public static CamPose Smooth(CamPose current, CamPose target, float rate, float dt)
        {
            if (rate <= 0f || (target.Position - current.Position).sqrMagnitude > SnapDistance * SnapDistance) return target;
            float f = Alpha(rate, dt);
            CamPose c;
            c.Position = current.Position + (target.Position - current.Position) * f;
            c.Pitch = current.Pitch + (target.Pitch - current.Pitch) * f;
            c.Yaw = Wrap180(LerpAngle(current.Yaw, target.Yaw, f));
            return c;
        }

        public static string Label(ReplayView v)
        {
            switch (v)
            {
                case ReplayView.FirstPerson: return "first person";
                case ReplayView.Trajectory: return "trajectory";
                default: return "chase";
            }
        }
    }

    // ------------------------------------------------------------------
    // The replay's own clock: the comparison run played back from any
    // time, paused, stepped and at a slower or faster speed. Real seconds
    // in, run seconds out; stops (paused) at the end.
    // ------------------------------------------------------------------
    public sealed class ReplayClock
    {
        public static readonly float[] Speeds = { 0.1f, 0.25f, 0.5f, 1f, 2f };
        public const int NormalSpeed = 3;

        public float T;
        public float Duration;
        public int SpeedIndex = NormalSpeed;
        public bool Paused;

        public float Speed { get { return Speeds[SpeedIndex]; } }
        public bool AtEnd { get { return T >= Duration; } }

        public void Start(float duration, float at)
        {
            Duration = Math.Max(0f, duration);
            T = Math.Max(0f, Math.Min(Duration, at));
            Paused = false;
        }

        public void Advance(float realDt)
        {
            if (Paused || realDt <= 0f) return;
            T += realDt * Speed;
            if (T >= Duration) { T = Duration; Paused = true; }
        }

        public void Seek(float delta)
        {
            T = Math.Max(0f, Math.Min(Duration, T + delta));
        }

        /// Play / pause; play at the end starts over.
        public void TogglePause()
        {
            if (Paused && AtEnd) T = 0f;
            Paused = !Paused;
        }

        public void Faster() { if (SpeedIndex < Speeds.Length - 1) SpeedIndex++; }
        public void Slower() { if (SpeedIndex > 0) SpeedIndex--; }
    }

    // ------------------------------------------------------------------
    // The ghost's body as GL lines: a capsule (rings, sides, rounded ends),
    // a head, an arrow out of the chest along its facing, and a short gaze
    // line from the head along its look. Fixed vertex count (MaxVertices),
    // written into a caller's array - no allocation per frame.
    // ------------------------------------------------------------------
    public static class GhostFigure
    {
        private const int Ring = 10;      // segments per body ring
        private const int Arc = 6;        // segments per half circle (even: one point at the pole)
        private const int HeadRing = 8;

        /// Vertices Build writes (line pairs).
        public const int Vertices = 3 * Ring * 2 + 8 + 4 * Arc * 2 + 3 * HeadRing * 2 + 6 + 2;
        public const float ArrowLength = 0.6f;
        public const float GazeLength = 0.5f;

        /// Body radius and head radius for a figure `height` tall.
        public static float BodyRadius(float height) { return 0.28f * height / ReplayCamera.DefaultHeight; }
        public static float HeadRadius(float height) { return 0.13f * height / ReplayCamera.DefaultHeight; }

        /// The figure standing at `feet`, facing `yaw`, looking at `pitch`.
        /// Returns the vertices written (Vertices, or 0 if `v` is too short).
        public static int Build(Vector3 feet, float yaw, float pitch, float height, Vector3[] v)
        {
            if (v == null || v.Length < Vertices) return 0;
            int n = 0;
            double ry = yaw * Math.PI / 180.0;
            float cy = (float)Math.Cos(ry), sy = (float)Math.Sin(ry);

            float r = BodyRadius(height);
            float hr = HeadRadius(height);
            float shoulder = height - 2f * hr - 0.04f * height / ReplayCamera.DefaultHeight;
            float c0 = r, c1 = Math.Max(r, shoulder - r);

            // Rings at the bottom, middle and top of the body's straight part.
            n = Circle(v, n, feet, cy, sy, c0, r, Ring);
            n = Circle(v, n, feet, cy, sy, (c0 + c1) * 0.5f, r, Ring);
            n = Circle(v, n, feet, cy, sy, c1, r, Ring);
            // Four sides.
            for (int k = 0; k < 4; k++)
            {
                float x = k == 0 ? r : k == 2 ? -r : 0f;
                float z = k == 1 ? r : k == 3 ? -r : 0f;
                v[n++] = W(feet, cy, sy, x, c0, z);
                v[n++] = W(feet, cy, sy, x, c1, z);
            }
            // Rounded ends: half circles in the forward and the side plane.
            for (int end = 0; end < 2; end++)
            {
                float centre = end == 0 ? c0 : c1;
                float dirY = end == 0 ? -1f : 1f;
                for (int plane = 0; plane < 2; plane++)
                    for (int s = 0; s < Arc; s++)
                    {
                        double a0 = Math.PI * s / Arc, a1 = Math.PI * (s + 1) / Arc;
                        v[n++] = ArcPoint(feet, cy, sy, centre, r, dirY, plane, a0);
                        v[n++] = ArcPoint(feet, cy, sy, centre, r, dirY, plane, a1);
                    }
            }

            // The head: three circles.
            float hc = height - hr;
            n = Circle(v, n, feet, cy, sy, hc, hr, HeadRing);
            for (int plane = 0; plane < 2; plane++)
                for (int s = 0; s < HeadRing; s++)
                {
                    double a0 = 2.0 * Math.PI * s / HeadRing, a1 = 2.0 * Math.PI * (s + 1) / HeadRing;
                    v[n++] = VerticalPoint(feet, cy, sy, hc, hr, plane, a0);
                    v[n++] = VerticalPoint(feet, cy, sy, hc, hr, plane, a1);
                }

            // The facing arrow, out of the chest.
            float chest = shoulder * 0.75f;
            Vector3 from = W(feet, cy, sy, 0f, chest, r);
            Vector3 tip = W(feet, cy, sy, 0f, chest, r + ArrowLength);
            float barb = ArrowLength * 0.35f;
            v[n++] = from; v[n++] = tip;
            v[n++] = tip; v[n++] = W(feet, cy, sy, barb * 0.6f, chest, r + ArrowLength - barb);
            v[n++] = tip; v[n++] = W(feet, cy, sy, -barb * 0.6f, chest, r + ArrowLength - barb);

            // The gaze: from the head along the look.
            Vector3 head = feet + new Vector3(0f, hc, 0f);
            v[n++] = head;
            v[n++] = head + ReplayCamera.Direction(pitch, yaw) * (hr + GazeLength);
            return n;
        }

        private static Vector3 W(Vector3 feet, float cy, float sy, float x, float y, float z)
        {
            return new Vector3(feet.x + x * cy + z * sy, feet.y + y, feet.z - x * sy + z * cy);
        }

        private static int Circle(Vector3[] v, int n, Vector3 feet, float cy, float sy, float y, float r, int segs)
        {
            for (int s = 0; s < segs; s++)
            {
                double a0 = 2.0 * Math.PI * s / segs, a1 = 2.0 * Math.PI * (s + 1) / segs;
                v[n++] = W(feet, cy, sy, r * (float)Math.Cos(a0), y, r * (float)Math.Sin(a0));
                v[n++] = W(feet, cy, sy, r * (float)Math.Cos(a1), y, r * (float)Math.Sin(a1));
            }
            return n;
        }

        // A point of a half circle below (dirY -1) or above (+1) a ring at
        // height `centre`; plane 0 = the forward plane, 1 = the side plane.
        private static Vector3 ArcPoint(Vector3 feet, float cy, float sy, float centre, float r, float dirY, int plane, double a)
        {
            float h = r * (float)Math.Cos(a);
            float up = dirY * r * (float)Math.Sin(a);
            return plane == 0 ? W(feet, cy, sy, 0f, centre + up, h) : W(feet, cy, sy, h, centre + up, 0f);
        }

        private static Vector3 VerticalPoint(Vector3 feet, float cy, float sy, float centre, float r, int plane, double a)
        {
            float h = r * (float)Math.Cos(a);
            float up = r * (float)Math.Sin(a);
            return plane == 0 ? W(feet, cy, sy, 0f, centre + up, h) : W(feet, cy, sy, h, centre + up, 0f);
        }
    }
}
