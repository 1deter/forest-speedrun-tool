using UnityEngine;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Reading a point off the camera's depth texture (T-0296): a stored
    // depth value -> the distance in front of the camera, and the surface's
    // normal from the points around it. Pure maths, tested.
    // ------------------------------------------------------------------
    public static class DepthMath
    {
        /// The view-space depth (metres in front of the camera) of a value
        /// read from the depth texture: Unity's LinearEyeDepth with the
        /// _ZBufferParams for `near` / `far`. Reversed (D3D11 and newer):
        /// 1 at the near plane, 0 at the far one. A value at the far plane
        /// (the sky) gives `far`.
        public static float EyeDepth(float stored, float near, float far, bool reversed)
        {
            float x, y;
            if (reversed) { x = -1f + far / near; y = 1f; }
            else { x = 1f - far / near; y = far / near; }
            float div = (x / far) * stored + y / far;
            if (div <= 1e-9f) return far;
            return 1f / div;
        }

        /// The surface normal at `c` from its neighbours left / right
        /// (`l`, `r`) and below / above (`d`, `u`), turned towards `eye`.
        /// Each axis uses the side nearer to `c` in depth, so a point next to
        /// an edge takes the face it is on, not the drop beside it. False when
        /// the points are degenerate.
        public static bool Normal(Vector3 eye, Vector3 c, Vector3 l, Vector3 r, Vector3 d, Vector3 u, out Vector3 normal)
        {
            Vector3 ax = Nearer(c, l, r, eye);
            Vector3 ay = Nearer(c, d, u, eye);
            normal = Vector3.Cross(ax, ay);
            if (normal.sqrMagnitude < 1e-12f) return false;
            normal.Normalize();
            if (Vector3.Dot(normal, eye - c) < 0f) normal = -normal;
            return true;
        }

        /// The step from c along one axis: towards whichever neighbour lies
        /// nearer to c's distance from the eye (always pointing the same way
        /// along the axis: from the `lo` side to the `hi` side).
        private static Vector3 Nearer(Vector3 c, Vector3 lo, Vector3 hi, Vector3 eye)
        {
            float dc = (c - eye).magnitude;
            float dl = Mathf.Abs((lo - eye).magnitude - dc);
            float dh = Mathf.Abs((hi - eye).magnitude - dc);
            return dl < dh ? c - lo : hi - c;
        }
    }
}
