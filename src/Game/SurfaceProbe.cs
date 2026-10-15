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
    // Asked for only while painting (Request each frame): the 7x7 pixels
    // around the centre are copied out of the depth (a quad through
    // Hidden/BlitCopy with the patch as its texture rect - a full-screen
    // float copy cost ~1 ms a frame) and read back, during the late pass
    // (Game/LatePass, where the camera's depth texture is still bound).
    // The read back waits for the GPU; reading only 5 pixels of it keeps
    // it free of allocation. The result is a frame old - the crosshair as
    // last drawn, which is what the player aimed at.
    // ------------------------------------------------------------------
    public static class SurfaceProbe
    {
        private const int Step = 3;                // neighbour offset for the normal
        private const int Patch = Step * 2 + 1;    // pixels read around the centre

        private static int _askedFrame = -100;
        private static int _streakFrom = -100;     // first frame of the current run of requests
        private static int _sampledFrame = -100;
        private static bool _ok;
        private static Vector3 _point, _normal, _eye;
        private static string _failure;

        private static RenderTexture _patch;       // the patch alone
        private static RenderTexture _copy;        // the whole depth (no BlitCopy, or FullCopy)
        private static Material _blit;
        private static bool _blitTried;
        private static Texture2D _read;

        /// Copy the whole depth texture instead of the patch (the old way;
        /// for comparing the two over the bridge).
        public static bool FullCopy;

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
            int x0 = w / 2 - Patch / 2, y0 = h / 2 - Patch / 2;

            if (_read == null)
            {
                _read = new Texture2D(Patch, Patch, TextureFormat.RGBAFloat, false, true);
                _read.hideFlags = HideFlags.HideAndDontSave;
            }

            RenderTexture before = RenderTexture.active;
            Material blit = BlitMaterial();
            if (blit != null && !FullCopy)
            {
                if (_patch == null) _patch = Target(Patch, Patch);
                // A quad of our own: Graphics.Blit ignores the material's
                // texture scale / offset (the neighbours came from 3/7 of the
                // screen away, bridge 2026-10-15).
                RenderTexture.active = _patch;
                blit.mainTexture = depth;
                GL.PushMatrix();
                GL.LoadOrtho();
                blit.SetPass(0);
                float u0 = x0 / (float)w, v0 = y0 / (float)h;
                float u1 = (x0 + Patch) / (float)w, v1 = (y0 + Patch) / (float)h;
                // On D3D both the depth texture and the patch count rows from the
                // top: unflipped, the patch came a row off; v flipped alone, its
                // rows mirrored about the centre (bridge 2026-10-15).
                if (SystemInfo.graphicsUVStartsAtTop) { float b = 1f - v1; v1 = 1f - v0; v0 = b; }
                GL.Begin(GL.QUADS);
                GL.TexCoord2(u0, v0); GL.Vertex3(0f, 0f, 0f);
                GL.TexCoord2(u0, v1); GL.Vertex3(0f, 1f, 0f);
                GL.TexCoord2(u1, v1); GL.Vertex3(1f, 1f, 0f);
                GL.TexCoord2(u1, v0); GL.Vertex3(1f, 0f, 0f);
                GL.End();
                GL.PopMatrix();
                _read.ReadPixels(new Rect(0, 0, Patch, Patch), 0, 0, false);
            }
            else
            {
                if (_copy != null && (_copy.width != w || _copy.height != h)) { Destroy(ref _copy); }
                if (_copy == null) _copy = Target(w, h);
                Graphics.Blit(depth, _copy);
                RenderTexture.active = _copy;
                _read.ReadPixels(new Rect(x0, y0, Patch, Patch), 0, 0, false);
            }
            RenderTexture.active = before;

            int c = Patch / 2;
            float near = camera.nearClipPlane, far = camera.farClipPlane;
            bool reversed = SystemInfo.usesReversedZBuffer;
            Transform t = camera.transform;
            Vector3 eye = t.position, fwd = t.forward;
            float sx = camera.pixelWidth / (float)w, sy = camera.pixelHeight / (float)h;

            Vector3 centre, l, r, d, u;
            if (!At(c, c, x0, y0, sx, sy, camera, near, far, reversed, eye, fwd, out centre)) { _failure = null; return; }   // sky
            bool all = At(c - Step, c, x0, y0, sx, sy, camera, near, far, reversed, eye, fwd, out l)
                     & At(c + Step, c, x0, y0, sx, sy, camera, near, far, reversed, eye, fwd, out r)
                     & At(c, c - Step, x0, y0, sx, sy, camera, near, far, reversed, eye, fwd, out d)
                     & At(c, c + Step, x0, y0, sx, sy, camera, near, far, reversed, eye, fwd, out u);
            Vector3 n;
            if (!all || !DepthMath.Normal(eye, centre, l, r, d, u, out n)) n = -fwd;

            _eye = eye; _point = centre; _normal = n;
            _ok = true;
            _failure = null;
        }

        /// The world point at patch pixel (i, j); false for the sky.
        private static bool At(int i, int j, int x0, int y0, float sx, float sy, Camera camera,
            float near, float far, bool reversed, Vector3 eye, Vector3 fwd, out Vector3 point)
        {
            point = Vector3.zero;
            float z = DepthMath.EyeDepth(_read.GetPixel(i, j).r, near, far, reversed);
            if (z >= far * 0.99f || z <= 0f) return false;
            Ray ray = camera.ScreenPointToRay(new Vector3((x0 + i + 0.5f) * sx, (y0 + j + 0.5f) * sy, 0f));
            float along = Vector3.Dot(ray.direction, fwd);
            if (along <= 1e-4f) return false;
            point = eye + ray.direction * (z / along);
            return true;
        }

        /// Hidden/BlitCopy, drawn as a quad whose texture coordinates are
        /// the patch; null when the game has no such shader (then the
        /// whole depth is copied).
        private static Material BlitMaterial()
        {
            if (_blit != null || _blitTried) return _blit;
            _blitTried = true;
            Shader shader = Shader.Find("Hidden/BlitCopy");
            if (shader == null) return null;
            _blit = new Material(shader);
            _blit.hideFlags = HideFlags.HideAndDontSave;
            return _blit;
        }

        private static RenderTexture Target(int w, int h)
        {
            RenderTexture rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            rt.hideFlags = HideFlags.HideAndDontSave;
            rt.filterMode = FilterMode.Point;
            rt.Create();
            return rt;
        }

        private static void Destroy(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            UnityEngine.Object.Destroy(rt);
            rt = null;
        }

        private static void Fail(string why) { _failure = why; }

        public static void Release()
        {
            Destroy(ref _patch);
            Destroy(ref _copy);
            if (_read != null) { UnityEngine.Object.Destroy(_read); _read = null; }
            if (_blit != null) { UnityEngine.Object.Destroy(_blit); _blit = null; }
            _blitTried = false;
        }
    }
}
