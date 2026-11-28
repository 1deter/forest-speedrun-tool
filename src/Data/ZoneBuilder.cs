using System;
using UnityEngine;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The zone editor's geometry (T-0492; decisions.md *Zone editor mode*):
    // a box is three clicks - two ground corners set its width and length,
    // the third its height - on a grid that can be turned and snapped.
    //
    // The grid's frame is the box's: a box drawn on a grid turned by Yaw
    // gets that Yaw, the same turn TriggerEvaluator tests containment in
    // (local = inverse of Euler(0, yaw, 0)), so a box fires where it is
    // drawn. Pure, tested (ZoneBuilderTests).
    // ------------------------------------------------------------------
    public static class ZoneBuilder
    {
        /// The grid sizes the cycle key steps through; 0 = no snapping.
        public static readonly float[] GridSizes = { 0f, 0.5f, 1f, 2f, 4f };

        /// The smallest a box gets on each axis (half-size): a stray double
        /// click never makes a zone nobody can enter.
        public const float MinHalf = 0.25f;

        /// The height a box gets before the third click (half-size).
        public const float DefaultHalfHeight = 1.5f;

        public static int NextGrid(int index)
        {
            return (index + 1) % GridSizes.Length;
        }

        /// Into the grid's frame (turned by yaw degrees about +Y), x / z only.
        public static Vector2 ToGrid(Vector3 world, float yaw)
        {
            double a = yaw * Math.PI / 180.0;
            float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
            return new Vector2(world.x * c - world.z * s, world.x * s + world.z * c);
        }

        /// Back out of the grid's frame: Euler(0, yaw, 0) applied to (x, 0, z).
        public static Vector3 FromGrid(Vector2 local, float y, float yaw)
        {
            double a = yaw * Math.PI / 180.0;
            float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
            return new Vector3(local.x * c + local.y * s, y, -local.x * s + local.y * c);
        }

        /// A point snapped to the grid (x / z in the grid's frame; y as it
        /// is - the ground decides the height). Grid 0 leaves it alone.
        public static Vector3 Snap(Vector3 p, float grid, float yaw)
        {
            if (grid <= 0f) return p;
            Vector2 g = ToGrid(p, yaw);
            g = new Vector2(Rnd(g.x / grid) * grid, Rnd(g.y / grid) * grid);
            return FromGrid(g, p.y, yaw);
        }

        /// The box after two corners: their footprint in the grid's frame,
        /// standing on the lower corner, DefaultHalfHeight up from there.
        public static Trigger Footprint(Vector3 a, Vector3 b, float yaw)
        {
            float floor = Mathf.Min(a.y, b.y);
            return Make(a, b, floor, floor + 2f * DefaultHalfHeight, yaw);
        }

        /// The finished box: the footprint of `a` / `b`, from the lower
        /// corner's height to `top` (below the floor works too - a zone
        /// hung under a ledge).
        public static Trigger Finish(Vector3 a, Vector3 b, float top, float yaw)
        {
            return Make(a, b, Mathf.Min(a.y, b.y), top, yaw);
        }

        /// The third click's height: where the view ray meets the upright
        /// plane through the footprint's middle that faces the camera.
        /// False when the ray runs along that plane (looking edge-on).
        public static bool HeightFromRay(Vector3 origin, Vector3 dir, Vector3 middle, out float top)
        {
            top = middle.y;
            // The plane's normal: level, from the middle toward the camera.
            Vector3 n = new Vector3(origin.x - middle.x, 0f, origin.z - middle.z);
            float len = (float)Math.Sqrt(n.x * n.x + n.z * n.z);
            if (len < 1e-4f) return false;   // standing over the middle: no plane faces you
            n = new Vector3(n.x / len, 0f, n.z / len);
            float along = dir.x * n.x + dir.z * n.z;
            if (Mathf.Abs(along) < 1e-4f) return false;
            float t = ((middle.x - origin.x) * n.x + (middle.z - origin.z) * n.z) / along;
            if (t <= 0f) return false;       // the plane is behind the camera
            top = origin.y + dir.y * t;
            return true;
        }

        /// A height snapped to the grid's size (0: as it is).
        public static float SnapHeight(float y, float grid)
        {
            return grid <= 0f ? y : Rnd(y / grid) * grid;
        }

        private static Trigger Make(Vector3 a, Vector3 b, float floor, float top, float yaw)
        {
            Vector2 ga = ToGrid(a, yaw), gb = ToGrid(b, yaw);
            float hx = Mathf.Max(Mathf.Abs(ga.x - gb.x) * 0.5f, MinHalf);
            float hz = Mathf.Max(Mathf.Abs(ga.y - gb.y) * 0.5f, MinHalf);
            float lo = Mathf.Min(floor, top), hi = Mathf.Max(floor, top);
            float hy = Mathf.Max((hi - lo) * 0.5f, MinHalf);
            Vector2 mid = new Vector2((ga.x + gb.x) * 0.5f, (ga.y + gb.y) * 0.5f);

            Trigger t = new Trigger();
            t.Kind = TriggerKind.Zone;
            t.Shape = ZoneShape.Box;
            t.Position = FromGrid(mid, (lo + hi) * 0.5f, yaw);
            t.Extents = new Vector3(hx, hy, hz);
            t.Yaw = NormaliseYaw(yaw);
            t.Radius = 3f;   // kept for a switch to a sphere
            return t;
        }

        /// Round half away from zero (Mathf.Round is banker's; a grid line
        /// halfway between should not flip by parity).
        public static float Rnd(float v)
        {
            return (float)Math.Round(v, MidpointRounding.AwayFromZero);
        }

        private static float NormaliseYaw(float yaw)
        {
            float y = yaw % 360f;
            return y < 0f ? y + 360f : y;
        }
    }
}
