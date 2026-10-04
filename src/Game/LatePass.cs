using System;
using System.Collections.Generic;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // GL overlays drawn AFTER the game's image effects.
    //
    // The game's camera (MainCamNew) is HDR and ends in Unity's
    // post-processing stack (PostProcessingBehaviour: eye adaptation,
    // bloom, Neutral tonemapping, TAA). Lines drawn in OnRenderObject go
    // into the HDR picture before it, so eye adaptation multiplies them
    // like the world - up to x32 at night (keyValue 0.25 / 2^-7) - and a
    // colour at 1.0 comes out 255,255,255 with its hue only in the bloom
    // around it (bridge, v0.24.242: the beacon (1, 0.2, 1) read 255,255,255;
    // with the post-processing off 254,124,254; game-notes *Overlay colours*).
    //
    // This component sits last on the camera, so its OnRenderImage runs
    // after the post-processing's (component order). It draws the
    // registered overlays into the finished LDR picture there, depth-tested
    // against the scene: the camera's own target (with its depth buffer)
    // is taken in OnRenderObject, while it is still bound, and its depth is
    // bound with the LDR colour. Every overlay keeps its OnRenderObject path
    // as the fallback (Covers false): no pass on the camera, nothing to draw,
    // or a frame the pass could not use (no depth, a size mismatch) - then
    // it retries after a few seconds and logs why once.
    //
    // The component is enabled only while an overlay wants to draw, so an
    // idle overlay costs the camera nothing (an enabled OnRenderImage is a
    // full-screen copy). It only ever changes in Sync, from LateUpdate -
    // never mid-frame (game-notes: a screen camera switched mid-frame froze
    // the picture).
    // ------------------------------------------------------------------
    public interface ILateDrawer
    {
        /// Something to draw this frame (decides whether the pass runs).
        bool WantsLateDraw { get; }

        /// Emits GL lines in world space: SetPass + GL.Begin / End. The
        /// camera's view and projection are loaded.
        void DrawLate(Camera camera);
    }

    public sealed class LatePass : MonoBehaviour
    {
        private static readonly List<ILateDrawer> Drawers = new List<ILateDrawer>();
        private static LatePass _pass;
        private static int _syncFrame = -1;

        /// One line per thing worth knowing (installed, first draw, a
        /// fallback and why); set by the plugin.
        public static Action<string> Log;

        /// What the pass did last, for the bridge / logs.
        public static string Status = "not installed";

        /// Frames drawn late since startup (a "did it run" count).
        public static int Frames;

        private Camera _camera;
        private RenderTexture _scene;
        private int _sceneFrame = -1;
        private float _retryAt;
        private string _lastFailure;
        private bool _loggedFirst;

        public static void Register(ILateDrawer d)
        {
            if (d != null && !Drawers.Contains(d)) Drawers.Add(d);
        }

        public static void Unregister(ILateDrawer d)
        {
            Drawers.Remove(d);
        }

        /// Once a frame, from LateUpdate (any caller; the rest of the frame
        /// is a no-op): puts the pass on `target` (the view the player looks
        /// through) and enables it only while an overlay wants to draw.
        public static void Sync(Camera target)
        {
            if (Time.frameCount == _syncFrame) return;
            _syncFrame = Time.frameCount;

            bool wants = false;
            for (int i = 0; i < Drawers.Count; i++)
                if (Drawers[i] != null && Drawers[i].WantsLateDraw) { wants = true; break; }

            if (target == null) return;
            if (_pass == null || _pass._camera != target)
            {
                if (!wants) return;
                if (_pass != null) _pass.enabled = false;
                _pass = target.GetComponent<LatePass>();
                if (_pass == null)
                {
                    _pass = target.gameObject.AddComponent<LatePass>();
                    Say("Late pass: on '" + target.name + "' (after its image effects; " + Drawers.Count + " overlay(s) registered)");
                }
                _pass._camera = target;
            }

            bool on = wants && Time.realtimeSinceStartup >= _pass._retryAt;
            if (_pass.enabled != on) _pass.enabled = on;
        }

        /// True when `camera`'s overlays are drawn by the pass this frame:
        /// OnRenderObject then skips them.
        public static bool Covers(Camera camera)
        {
            return _pass != null && _pass.enabled && camera != null && _pass._camera == camera;
        }

        private static void Say(string line)
        {
            Status = line;
            if (Log != null) Log(line);
        }

        private void OnRenderObject()
        {
            // The camera's target is bound while it renders the scene: keep
            // it (and its depth buffer) for the late draw.
            if (Camera.current != _camera) return;
            _scene = RenderTexture.active;
            _sceneFrame = Time.frameCount;
        }

        private void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            string why = Usable(src);
            if (why != null)
            {
                Graphics.Blit(src, dst);
                _retryAt = Time.realtimeSinceStartup + 5f;
                enabled = false;
                if (why != _lastFailure)
                {
                    _lastFailure = why;
                    Say("Late pass: not used (" + why + ") - overlays drawn before the image effects, retry in 5 s");
                }
                return;
            }

            Graphics.SetRenderTarget(src.colorBuffer, _scene.depthBuffer);
            GL.PushMatrix();
            GL.LoadProjectionMatrix(_camera.projectionMatrix);
            GL.modelview = _camera.worldToCameraMatrix;
            for (int i = 0; i < Drawers.Count; i++)
            {
                ILateDrawer d = Drawers[i];
                if (d == null) continue;
                try { d.DrawLate(_camera); }
                catch (Exception e)
                {
                    // Once per message: this runs every frame.
                    if (_lastFailure != e.Message)
                    {
                        _lastFailure = e.Message;
                        Say("Late pass: " + d.GetType().Name + " threw: " + e.Message);
                    }
                }
            }
            GL.PopMatrix();
            Graphics.Blit(src, dst);
            Frames++;

            if (!_loggedFirst)
            {
                _loggedFirst = true;
                Say("Late pass: drawing on '" + _camera.name + "' after its image effects (picture " + src.width + "x" + src.height +
                    " " + src.format + (src.sRGB ? " sRGB" : " linear") + ", scene depth from '" + _scene.name + "' " +
                    _scene.width + "x" + _scene.height + ", " + _scene.depth + "-bit)");
            }
        }

        /// Null when the late draw can run on this frame, else why not.
        private string Usable(RenderTexture src)
        {
            if (_camera == null) return "no camera";
            if (src == null) return "no picture";
            if (_scene == null || _sceneFrame != Time.frameCount) return "the camera's scene target was not seen this frame";
            if (_scene.depth == 0) return "the scene target '" + _scene.name + "' has no depth buffer";
            if (_scene.width != src.width || _scene.height != src.height)
                return "picture " + src.width + "x" + src.height + " vs scene " + _scene.width + "x" + _scene.height;
            if (_scene.antiAliasing != src.antiAliasing) return "anti-aliasing differs (" + src.antiAliasing + " vs " + _scene.antiAliasing + ")";
            return null;
        }

        private void OnDestroy()
        {
            if (_pass == this) _pass = null;
        }
    }
}
