using System;
using System.Collections.Generic;
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
    //   Perf (30 s): 57.9 fps, worst 48 ms, 2 over 50 ms, GC x3
    //   (GC frames 45 ms avg, 48 max) |
    //   overlay tick 0.21 ms avg, 1.80 max | GL 0.05 ms/frame,
    //   1.0 passes (3.0 skipped), 2400 verts/frame |
    //   heap +900 KB/s, overlay +2.0 KB/s
    //
    // Hitches with a GC count beside them point at garbage; a high GL
    // figure points at the lines; a high overlay tick at a module (which
    // ModuleHost also names in a "Slow tick" line). Nothing here is
    // allocated per frame - only the log line itself, twice a minute.
    //
    // WHO MAKES THE GARBAGE. A GC every few seconds is a hitch every few
    // seconds, and the question is always "the game, or us?". The managed
    // heap is sampled once a frame (growth = everyone's allocation) and
    // around the overlay's own Tick and OnGUI (growth = ours), and both
    // are reported per second. Boehm's used-size counter moves in heap
    // blocks, not bytes, so treat the figures as rough - the ratio is what
    // matters.
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
        private int _gcLast;
        private float _gcFrameMax;
        private float _gcFrameSum;
        private int _gcFrames;
        private float _gcPendingDt = -1f;

        private double _tickSum;
        private double _tickMax;

        private long _lastHeap = -1;
        private double _heapGrowth;
        private double _overlayGrowth;

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

            long heap = SafeHeap();
            if (_lastHeap >= 0 && heap > _lastHeap) _heapGrowth += heap - _lastHeap;
            _lastHeap = heap;

            float dt = Time.unscaledDeltaTime;
            _frames++;
            if (dt > _maxDt) _maxDt = dt;
            if (dt > HitchSeconds) _hitches++;

            // A frame in which a collection ran: its length is roughly the
            // pause (Boehm stops the world; nothing incremental in 5.6).
            // Seen here, the collection ran since the last check - in the
            // frame just measured (dt) or later in this one (the next dt):
            // the longer of the two.
            if (_gcPendingDt >= 0f)
            {
                float d = Mathf.Max(_gcPendingDt, dt);
                _gcPendingDt = -1f;
                _gcFrames++;
                _gcFrameSum += d;
                if (d > _gcFrameMax) _gcFrameMax = d;
            }
            int gcNow = SafeGcCount();
            if (gcNow != _gcLast)
            {
                _gcLast = gcNow;
                _gcPendingDt = dt;
            }

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
                         (_gcFrames > 0 ? " (GC frames " + (_gcFrameSum * 1000f / _gcFrames).ToString("0") + " ms avg, " +
                                          (_gcFrameMax * 1000f).ToString("0") + " max)" : "") +
                         " | overlay tick " + (_tickSum / _frames).ToString("0.00") + " ms avg, " +
                         _tickMax.ToString("0.00") + " max" +
                         " | GL " + (renderMs / _frames).ToString("0.00") + " ms/frame, " +
                         ((float)PerfCounters.DrawPasses / _frames).ToString("0.0") + " passes (" +
                         ((float)PerfCounters.SkippedPasses / _frames).ToString("0.0") + " skipped), " +
                         (PerfCounters.Vertices / _frames) + " verts/frame" +
                         " | heap +" + (_heapGrowth / 1024.0 / seconds).ToString("0") + " KB/s, overlay +" +
                         (_overlayGrowth / 1024.0 / seconds).ToString("0.0") + " KB/s");

            // Where the frame's time went on the main thread (Game/FrameTimer).
            List<string> frame = ForestOverlay.Game.FrameTimer.Timeline.Report(seconds, System.Diagnostics.Stopwatch.Frequency, 16);
            for (int i = 0; i < frame.Count; i++) _log.LogInfo(i == 0 ? frame[i] : "  " + frame[i]);
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
            _gcLast = _gcAtStart;
            _gcFrames = 0;
            _gcFrameSum = 0f;
            _gcFrameMax = 0f;
            _gcPendingDt = -1f;
            _heapGrowth = 0.0;
            _overlayGrowth = 0.0;
            _lastHeap = -1;

            PerfCounters.DrawPasses = 0;
            PerfCounters.SkippedPasses = 0;
            PerfCounters.Vertices = 0;
            PerfCounters.RenderTicks = 0;
            ForestOverlay.Game.FrameTimer.Timeline.Reset();
        }

        /// Heap size before a stretch of overlay work; pass it to EndAlloc.
        /// While the allocation tracker counts, its exact main-thread
        /// counter instead (the stretches never nest).
        public long BeginAlloc()
        {
            _exactStretch = ForestOverlay.Game.AllocationTracker.Counting;
            return _exactStretch ? ForestOverlay.Game.AllocationTracker.MainBytes : SafeHeap();
        }

        public void EndAlloc(long start)
        {
            if (start < 0) return;
            bool exact = ForestOverlay.Game.AllocationTracker.Counting;
            if (exact != _exactStretch) return;   // switched mid-stretch
            long d = (exact ? ForestOverlay.Game.AllocationTracker.MainBytes : SafeHeap()) - start;
            if (d > 0) _overlayGrowth += d;
        }

        private bool _exactStretch;

        private static long SafeHeap()
        {
            try { return GC.GetTotalMemory(false); }
            catch (Exception) { return -1; }
        }

        private static int SafeGcCount()
        {
            try { return GC.CollectionCount(0); }
            catch (Exception) { return 0; }
        }
    }
}
