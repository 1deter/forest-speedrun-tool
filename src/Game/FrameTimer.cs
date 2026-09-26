using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Feeds Data/FrameTimeline from the frame's marks: our FixedUpdate,
    // Update and LateUpdate, every camera's OnPreCull / OnPostRender
    // (Camera.onPreCull / onPostRender, static callbacks - no patch), and
    // WaitForEndOfFrame. The frame's start comes from Time.unscaledTime,
    // which Unity sets at the start of the frame on the same clock as
    // realtimeSinceStartup: at our first callback, `realtimeSinceStartup -
    // unscaledTime` is how long ago the frame began (float seconds, so
    // ~30 us of rounding per frame at 500 s of uptime - it averages out).
    //
    // Always on: a few clock reads a frame and one per camera, nothing
    // allocated. PerfMonitor writes its `Frame (30 s):` line beside the
    // Perf line; Snapshot() reads a short window for the bridge.
    // ------------------------------------------------------------------
    public sealed class FrameTimer : MonoBehaviour
    {
        public static readonly Data.FrameTimeline Timeline = new Data.FrameTimeline();
        public static BepInEx.Logging.ManualLogSource Log;
        /// Called at every end of frame (RenderProbe's tests); += / -=.
        public static Action EndOfFrameHook;

        /// The load test (Debug views): this many ms of busy work on the
        /// main thread every frame. Main thread the limit = the frame grows
        /// by it; the render thread the limit = it grows less (the main
        /// thread had been waiting for it, inside the first cameras).
        public static double TestLoadMs;
        private static FrameTimer _instance;

        private Camera.CameraCallback _pre, _post;
        private float _snapshotStart;

        public static void Install(GameObject host)
        {
            if (_instance == null && host != null) _instance = host.AddComponent<FrameTimer>();
        }

        private void OnEnable()
        {
            try
            {
                _pre = OnCameraPreCull;
                _post = OnCameraPostRender;
                Camera.onPreCull += _pre;
                Camera.onPostRender += _post;
                StartCoroutine(Ends());
            }
            catch (Exception) { }
        }

        private void OnDisable()
        {
            try
            {
                if (_pre != null) Camera.onPreCull -= _pre;
                if (_post != null) Camera.onPostRender -= _post;
            }
            catch (Exception) { }
        }

        private IEnumerator Ends()
        {
            WaitForEndOfFrame end = new WaitForEndOfFrame();
            while (true)
            {
                yield return end;
                Timeline.EndOfFrame(Stopwatch.GetTimestamp());
                Action hook = EndOfFrameHook;
                if (hook != null)
                {
                    try { hook(); }
                    catch (Exception) { }
                }
            }
        }

        private void FixedUpdate()
        {
            long now = Stopwatch.GetTimestamp();
            MarkStart(now);
            Timeline.FixedStep(now);
        }

        private void Update()
        {
            long now = Stopwatch.GetTimestamp();
            MarkStart(now);
            Timeline.Update(now);
            // The load test: a fixed cost on the main thread, inside the
            // "Update to LateUpdate" phase (Diagnostics, off by default).
            if (TestLoadMs > 0.0)
            {
                long until = now + (long)(TestLoadMs * Stopwatch.Frequency / 1000.0);
                while (Stopwatch.GetTimestamp() < until) { }
            }
        }

        private void LateUpdate()
        {
            Timeline.LateUpdate(Stopwatch.GetTimestamp());
        }

        // The frame's start, before our first callback of the frame.
        private static void MarkStart(long now)
        {
            double ago = (double)Time.realtimeSinceStartup - Time.unscaledTime;
            if (ago < 0.0) ago = 0.0;
            Timeline.FrameStart(now - (long)(ago * Stopwatch.Frequency), now);
        }

        private static void OnCameraPreCull(Camera cam)
        {
            long now = Stopwatch.GetTimestamp();
            Timeline.PreCull(Slot(cam), now);
        }

        private static void OnCameraPostRender(Camera cam)
        {
            long now = Stopwatch.GetTimestamp();
            Timeline.PostRender(Slot(cam), now);
        }

        private static int Slot(Camera cam)
        {
            if (cam == null) return -1;
            int id = cam.GetInstanceID();
            int slot = Timeline.CameraSlot(id);
            if (slot >= 0) return slot;
            string name;
            try { name = cam.name; }
            catch (Exception) { name = "?"; }
            return Timeline.AddCamera(id, name);
        }

        /// Dev (bridge): the window since the last call, as text (and in
        /// the log - the bridge shortens long replies), then a new window. `call ... FrameSnapshot`, `wait 5`, again. Resets
        /// the Perf line's window too (it shares the counters).
        public static string Snapshot()
        {
            float now = Time.unscaledTime;
            float seconds = _instance != null ? now - _instance._snapshotStart : 0f;
            List<string> lines = Timeline.Report(seconds, Stopwatch.Frequency, 16);
            Timeline.Reset();
            if (Log != null)
                for (int i = 0; i < lines.Count; i++) Log.LogInfo((i == 0 ? "Snapshot " : "  ") + lines[i]);
            if (_instance != null) _instance._snapshotStart = now;
            return lines.Count == 0 ? "no frames" : string.Join("\n", lines.ToArray());
        }
    }
}
