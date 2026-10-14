using System;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The drawn surface under the crosshair, read off the camera's depth
    // texture (T-0296). Paint aimed by a raycast lands on the collider, and
    // a rock's collider is a sphere (Pool_Rocks/*/collision, radius ~2.6 m
    // on Rock05): paint floated off the rock where the sphere stuck out and
    // sank into it where the rock did (bridge, 2026-10-15). The depth
    // texture holds what the player actually sees.
    //
    // Asked for only while painting (Request each frame): one full-screen
    // copy of the depth into a float target and a 9x9 read back, during
    // the late pass (Game/LatePass, where the camera's depth texture is
    // still bound). The result is a frame old - the crosshair as last
    // drawn, which is what the player aimed at.
    // ------------------------------------------------------------------
    public static class SurfaceProbe
    {
        private const int Patch = 9;           // pixels read around the centre
        private const int Step = 3;            // neighbour offset for the normal

        private static int _askedFrame = -100;
        private static int _streakFrom = -100;     // first frame of the current run of requests
        private static int _sampledFrame = -100;
        private static bool _ok;
        private static Vector3 _point, _normal, _eye;
        private static string _failure;

        private static RenderTexture _copy;
        private static Texture2D _read;

        /// What went wrong last (null while it works), for the log.
        public static string Failure { get { return _failure; } }

        /// Ask for a reading this frame (the paint key is held).
        public static void Request()
        {
            int f = Time.frameCount;
            if (f - _askedFrame > 2) _streakFrom = f;
            _askedFrame = f;
        }

        /// The first reading of a run of requests is still on its way (a
        /// frame or two): the caller waits rather than falling back to the
        /// collider. Never longer than 3 frames, so a probe that cannot run
        /// (no late pass) costs nothing but that.
        public static bool Pending
        {
            get { return _sampledFrame < _streakFrom && Time.frameCount - _streakFrom < 3; }
        }

        /// A reading was asked for in the last few frames: the late pass runs.
        public static bool Wanted { get { return Time.frameCount - _askedFrame <= 2; } }

        /// The last reading, if it is at most two frames old and saw a surface.
        public static bool TryGet(out Vector3 eye, out Vector3 point, out Vector3 normal)
        {
            eye = _eye; point = _point; normal = _normal;
            return _ok && Time.frameCount - _sampledFrame <= 2;
        }

        /// Called from the late pass of `camera`. Leaves the render target
        /// changed: the caller binds its own again.
        public static void Sample(Camera camera)
        {
            if (!Wanted || _sampledFrame == Time.frameCount) return;
            _sampledFrame = Time.frameCount;
            _ok = false;

            Texture depth = Shader.GetGlobalTexture("_CameraDepthTexture");
            if (depth == null) { Fail("no _CameraDepthTexture"); return; }
            int w = depth.width, h = depth.height;
            if (w < Patch * 2 || h < Patch * 2) { Fail("depth texture " + w + "x" + h); return; }

            if (_copy == null || _copy.width != w || _copy.height != h)
            {
                if (_copy != null) { _copy.Release(); UnityEngine.Object.Destroy(_copy); }
                _copy = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                _copy.hideFlags = HideFlags.HideAndDontSave;
                _copy.filterMode = FilterMode.Point;
                _copy.Create();
            }
            if (_read == null)
            {
                _read = new Texture2D(Patch, Patch, TextureFormat.RGBAFloat, false, true);
                _read.hideFlags = HideFlags.HideAndDontSave;
            }

            Graphics.Blit(depth, _copy);
            RenderTexture before = RenderTexture.active;
            RenderTexture.active = _copy;
            int x0 = w / 2 - Patch / 2, y0 = h / 2 - Patch / 2;
            _read.ReadPixels(new Rect(x0, y0, Patch, Patch), 0, 0, false);
            RenderTexture.active = before;
            Color[] px = _read.GetPixels();
            if (px == null || px.Length < Patch * Patch) { Fail("read back nothing"); return; }

            int c = Patch / 2;
            float near = camera.nearClipPlane, far = camera.farClipPlane;
            bool reversed = SystemInfo.usesReversedZBuffer;
            Transform t = camera.transform;
            Vector3 eye = t.position, fwd = t.forward;
            float sx = camera.pixelWidth / (float)w, sy = camera.pixelHeight / (float)h;

            Vector3 centre, l, r, d, u;
            if (!At(px, c, c, x0, y0, sx, sy, camera, near, far, reversed, eye, fwd, out centre)) { _failure = null; return; }   // sky
            bool all = At(px, c - Step, c, x0, y0, sx, sy, camera, near, far, reversed, eye, fwd, out l)
                     & At(px, c + Step, c, x0, y0, sx, sy, camera, near, far, reversed, eye, fwd, out r)
                     & At(px, c, c - Step, x0, y0, sx, sy, camera, near, far, reversed, eye, fwd, out d)
                     & At(px, c, c + Step, x0, y0, sx, sy, camera, near, far, reversed, eye, fwd, out u);
            Vector3 n;
            if (!all || !DepthMath.Normal(eye, centre, l, r, d, u, out n)) n = -fwd;

            _eye = eye; _point = centre; _normal = n;
            _ok = true;
            _failure = null;
        }

        /// The world point at patch pixel (i, j); false for the sky.
        private static bool At(Color[] px, int i, int j, int x0, int y0, float sx, float sy, Camera camera,
            float near, float far, bool reversed, Vector3 eye, Vector3 fwd, out Vector3 point)
        {
            point = Vector3.zero;
            float z = DepthMath.EyeDepth(px[j * Patch + i].r, near, far, reversed);
            if (z >= far * 0.99f || z <= 0f) return false;
            Ray ray = camera.ScreenPointToRay(new Vector3((x0 + i + 0.5f) * sx, (y0 + j + 0.5f) * sy, 0f));
            float along = Vector3.Dot(ray.direction, fwd);
            if (along <= 1e-4f) return false;
            point = eye + ray.direction * (z / along);
            return true;
        }

        private static void Fail(string why) { _failure = why; }

        public static void Release()
        {
            if (_copy != null) { _copy.Release(); UnityEngine.Object.Destroy(_copy); _copy = null; }
            if (_read != null) { UnityEngine.Object.Destroy(_read); _read = null; }
        }
    }
}
