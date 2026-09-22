using System;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // Counters fed by the GL renderers (run lines, zones, debug volumes).
    // Static because renderers are MonoBehaviours with no context to hand;
    // plain int/long increments, nothing allocated.
    // ------------------------------------------------------------------
    public static class PerfCounters
    {
        /// OnRenderObject passes that drew something.
        public static int DrawPasses;
        /// Passes skipped because they were for another camera
        /// (reflections, UI) - see Game/DrawTarget.
        public static int SkippedPasses;
        public static int Vertices;
        /// Stopwatch ticks spent inside our OnRenderObject hooks.
        public static long RenderTicks;
    }

    // ------------------------------------------------------------------
    // One log line every 30 s while in game, so a runner reporting
    // slowdown can send their LogOutput.log and it says where the time
    // went - instead of "it feels slow" on hardware nobody here has.
    //
    //   Perf (30 s): 57.9 fps, worst 48 ms, 2 over 50 ms, GC x3 |
    //   overlay tick 0.21 ms avg, 1.80 max | GL 0.05 ms/frame,
    //   1.0 passes (3.0 skipped), 2400 verts/frame
    //
    // Hitches with a GC count beside them point at garbage; a high GL
    // figure points at the lines; a high overlay tick at a module (which
    // ModuleHost also names in a "Slow tick" line). Nothing here is
    // allocated per frame - only the log line itself, twice a minute.
    // ------------------------------------------------------------------
    public sealed class PerfMonitor
    {
        private const float Interval = 30f;
        private const float HitchSeconds = 0.05f;

        private readonly ManualLogSource _log;

        private float _windowStart = -1f;
        private int _frames;
        private float _maxDt;
        private int _hitches;
        private int _gcAtStart;

        private double _tickSum;
        private double _tickMax;

        public PerfMonitor(ManualLogSource log)
        {
            _log = log;
        }

        /// Once per frame from ModuleHost.Tick. `overlayMs` is the time
        /// every module's Tick took together this frame.
        public void Frame(double overlayMs, bool inGame)
        {
            float now = Time.unscaledTime;
            if (_windowStart < 0f || !inGame) { Restart(now); return; }

            float dt = Time.unscaledDeltaTime;
            _frames++;
            if (dt > _maxDt) _maxDt = dt;
            if (dt > HitchSeconds) _hitches++;

            _tickSum += overlayMs;
            if (overlayMs > _tickMax) _tickMax = overlayMs;

            if (now - _windowStart < Interval) return;

            Report(now - _windowStart);
            Restart(now);
        }

        private void Report(float seconds)
        {
            if (_frames == 0) return;

            double renderMs = PerfCounters.RenderTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            int gc = SafeGcCount() - _gcAtStart;

            _log.LogInfo("Perf (" + seconds.ToString("0") + " s): " +
                         (_frames / seconds).ToString("0.0") + " fps, worst " +
                         (_maxDt * 1000f).ToString("0") + " ms, " +
                         _hitches + " over 50 ms, GC x" + gc +
                         " | overlay tick " + (_tickSum / _frames).ToString("0.00") + " ms avg, " +
                         _tickMax.ToString("0.00") + " max" +
                         " | GL " + (renderMs / _frames).ToString("0.00") + " ms/frame, " +
                         ((float)PerfCounters.DrawPasses / _frames).ToString("0.0") + " passes (" +
                         ((float)PerfCounters.SkippedPasses / _frames).ToString("0.0") + " skipped), " +
                         (PerfCounters.Vertices / _frames) + " verts/frame");
        }

        private void Restart(float now)
        {
            _windowStart = now;
            _frames = 0;
            _maxDt = 0f;
            _hitches = 0;
            _tickSum = 0.0;
            _tickMax = 0.0;
            _gcAtStart = SafeGcCount();

            PerfCounters.DrawPasses = 0;
            PerfCounters.SkippedPasses = 0;
            PerfCounters.Vertices = 0;
            PerfCounters.RenderTicks = 0;
        }

        private static int SafeGcCount()
        {
            try { return GC.CollectionCount(0); }
            catch (Exception) { return 0; }
        }
    }
}
