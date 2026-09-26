using System;
using System.Collections.Generic;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Where a frame's time goes on the main thread (raw FPS work, Next up
    // 6): the counters behind Game/FrameTimer and its `Frame (30 s):` line.
    //
    // WHY: the game profiler times the game's scripts - 1.5 ms of a 5.3 ms
    // frame here - and says nothing about the rest: rendering, physics, and
    // time the main thread spends waiting for the GPU or the render thread.
    // Whether a runner's machine is CPU- or GPU-bound decides what could
    // help it, and it cannot be measured on the author's hardware.
    //
    // The frame is cut at marks the plugin can see, each in Stopwatch
    // ticks; the time since the last mark goes to the phase the frame was
    // in, then the phase changes:
    //
    //   end of frame -> frame start        Wait (present; the main thread
    //                                       blocks here when the GPU or
    //                                       the render thread is behind)
    //   frame start -> our first callback   ToUpdate (time, input, physics
    //                                       and FixedUpdate, scripts that
    //                                       run before ours)
    //   our Update -> our LateUpdate         Update (Update, coroutines,
    //                                       animation, LateUpdate)
    //   our LateUpdate -> first camera       ToRender
    //   a camera's OnPreCull -> OnPostRender  that camera (culling, drawing
    //                                       - the draw calls are queued for
    //                                       the render thread here)
    //   OnPostRender -> the next mark        after that camera (its image
    //                                       effects; after the last one
    //                                       also every OnGUI)
    //
    // A camera rendered inside another (Camera.Render from a script's
    // callback) is charged to itself, not to the outer one. Frames with a
    // physics step are counted apart so the step's cost shows.
    //
    // Pure and allocation-free on the hot path (the marks); a camera's
    // name is asked for once per window. Tested.
    // ------------------------------------------------------------------
    public sealed class FrameTimeline
    {
        public const int MaxCameras = 16;
        private const int MaxDepth = 8;

        private enum Phase { None, Wait, ToUpdate, Update, ToRender, Camera, AfterCamera }

        // Frame phases (ticks, summed over the window).
        private long _wait, _toUpdate, _toUpdateFixed, _update, _toRender;
        private long _frameTicks;
        private int _frames, _fixedFrames;

        // Cameras.
        private readonly int[] _camIds = new int[MaxCameras];
        private readonly string[] _camNames = new string[MaxCameras];
        private readonly long[] _camRender = new long[MaxCameras];
        private readonly long[] _camAfter = new long[MaxCameras];
        private readonly int[] _camCount = new int[MaxCameras];
        private int _cams;
        private long _otherCams;

        // Where the frame is.
        private Phase _phase = Phase.None;
        private int _phaseCam = -1;
        private long _last;
        private long _frameStartTicks;
        private bool _fixedThisFrame;
        private readonly int[] _stack = new int[MaxDepth];
        private int _depth;

        public int Frames { get { return _frames; } }

        /// The slot of a camera already seen this window, or -1: the
        /// caller then asks for its name once and calls AddCamera.
        public int CameraSlot(int id)
        {
            for (int i = 0; i < _cams; i++) if (_camIds[i] == id) return i;
            return -1;
        }

        /// A new camera; -1 once the table is full (its time goes to
        /// "other cameras").
        public int AddCamera(int id, string name)
        {
            int slot = CameraSlot(id);
            if (slot >= 0) return slot;
            if (_cams == MaxCameras) return -1;
            _camIds[_cams] = id;
            _camNames[_cams] = name ?? "?";
            return _cams++;
        }

        // ------------------------------------------------------------------
        // The marks.

        /// WaitForEndOfFrame resumed: the frame's work is done.
        public void EndOfFrame(long now)
        {
            if (_phase != Phase.None && _phase != Phase.Wait)
            {
                Charge(now);
                _frames++;
                if (_fixedThisFrame) _fixedFrames++;
                _frameTicks += now - _frameStartTicks;
            }
            _depth = 0;
            _phase = Phase.Wait;
            _last = now;
            _frameStartTicks = now;
            _fixedThisFrame = false;
        }

        /// The frame began at `frameStart` (Time.unscaledTime on the
        /// Stopwatch clock), known at our first callback `now`. Clamped
        /// between the last end of frame and `now`.
        public void FrameStart(long frameStart, long now)
        {
            if (_phase != Phase.Wait) return;
            if (frameStart < _last) frameStart = _last;
            if (frameStart > now) frameStart = now;
            _wait += frameStart - _last;
            _last = frameStart;
            _phase = Phase.ToUpdate;
        }

        /// Our FixedUpdate: this frame has a physics step.
        public void FixedStep(long now)
        {
            _fixedThisFrame = true;
            if (_phase == Phase.Wait) FrameStart(now, now);
        }

        public void Update(long now)
        {
            if (_phase == Phase.None) return;
            if (_phase == Phase.Wait) FrameStart(now, now);
            Charge(now);
            _phase = Phase.Update;
        }

        public void LateUpdate(long now)
        {
            if (_phase == Phase.None || _phase == Phase.Wait) return;
            Charge(now);
            _phase = Phase.ToRender;
        }

        /// Camera.onPreCull. `slot` from CameraSlot / AddCamera (-1 = other).
        public void PreCull(int slot, long now)
        {
            if (_phase == Phase.None || _phase == Phase.Wait) return;
            Charge(now);
            if (_phase != Phase.Camera) _depth = 0;
            if (_depth < MaxDepth) _stack[_depth++] = slot;
            _phase = Phase.Camera;
            _phaseCam = slot;
            if (slot >= 0) _camCount[slot]++;
        }

        /// Camera.onPostRender.
        public void PostRender(int slot, long now)
        {
            if (_phase != Phase.Camera) return;
            Charge(now);
            // Pop to this camera (a missing post-render must not wedge the stack).
            int i = _depth - 1;
            while (i >= 0 && _stack[i] != slot) i--;
            if (i >= 0) _depth = i;
            else if (_depth > 0) _depth--;
            if (_depth > 0)
            {
                _phaseCam = _stack[_depth - 1];
                return;
            }
            _phase = Phase.AfterCamera;
            _phaseCam = slot;
        }

        private void Charge(long now)
        {
            long d = now - _last;
            _last = now;
            if (d <= 0) return;
            switch (_phase)
            {
                case Phase.ToUpdate:
                    if (_fixedThisFrame) _toUpdateFixed += d;
                    else _toUpdate += d;
                    break;
                case Phase.Update: _update += d; break;
                case Phase.ToRender: _toRender += d; break;
                case Phase.Camera:
                    if (_phaseCam >= 0) _camRender[_phaseCam] += d;
                    else _otherCams += d;
                    break;
                case Phase.AfterCamera:
                    if (_phaseCam >= 0) _camAfter[_phaseCam] += d;
                    else _otherCams += d;
                    break;
            }
        }

        /// A new window: counters to zero, the camera table cleared (a load
        /// replaces cameras). The frame in progress carries on.
        public void Reset()
        {
            _wait = _toUpdate = _toUpdateFixed = _update = _toRender = 0;
            _frameTicks = 0;
            _frames = _fixedFrames = 0;
            _otherCams = 0;
            for (int i = 0; i < _cams; i++)
            {
                _camRender[i] = _camAfter[i] = 0;
                _camCount[i] = 0;
                _camNames[i] = null;
            }
            _cams = 0;
            // The open camera's slot is gone: the rest of it goes to "other".
            for (int i = 0; i < _depth; i++) _stack[i] = -1;
            _phaseCam = -1;
        }

        // ------------------------------------------------------------------
        // The report: milliseconds per frame.

        public double WaitMs(long freq) { return PerFrame(_wait, freq); }
        public double FrameMs(long freq) { return PerFrame(_frameTicks, freq); }

        public double CamerasMs(long freq)
        {
            long sum = _otherCams;
            for (int i = 0; i < _cams; i++) sum += _camRender[i] + _camAfter[i];
            return PerFrame(sum, freq);
        }

        private double PerFrame(long ticks, long freq)
        {
            return _frames == 0 || freq <= 0 ? 0.0 : ticks * 1000.0 / freq / _frames;
        }

        /// "Frame (30 s): 5.28 ms/frame on the main thread = ..." plus a
        /// line of cameras, most expensive first. Empty with no frames.
        public List<string> Report(float seconds, long freq, int topCameras)
        {
            List<string> lines = new List<string>();
            if (_frames == 0 || freq <= 0) return lines;
            int plain = _frames - _fixedFrames;
            // "To Update" split: frames with a physics step and without.
            double toUpdatePlain = plain > 0 ? _toUpdate * 1000.0 / freq / plain : 0.0;
            double toUpdateFixed = _fixedFrames > 0 ? _toUpdateFixed * 1000.0 / freq / _fixedFrames : 0.0;

            StringBuilder sb = new StringBuilder();
            sb.Append("Frame (").Append(seconds.ToString("0")).Append(" s, ").Append(_frames).Append(" frames): ")
              .Append(Ms(FrameMs(freq))).Append(" ms/frame = waiting ").Append(Ms(WaitMs(freq)))
              .Append(" (GPU / render thread / present) + start to Update ").Append(Ms(PerFrame(_toUpdate + _toUpdateFixed, freq)))
              .Append(" (").Append(Ms(toUpdatePlain)).Append(" without a physics step, ").Append(Ms(toUpdateFixed))
              .Append(" with, ").Append(((double)_fixedFrames / _frames * 100.0).ToString("0")).Append("% of frames)")
              .Append(" + Update to LateUpdate ").Append(Ms(PerFrame(_update, freq)))
              .Append(" + to rendering ").Append(Ms(PerFrame(_toRender, freq)))
              .Append(" + cameras and OnGUI ").Append(Ms(CamerasMs(freq)));
            lines.Add(sb.ToString());

            // Cameras by total time.
            int n = _cams;
            int[] order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort(order, delegate(int a, int b)
            {
                long ta = _camRender[a] + _camAfter[a], tb = _camRender[b] + _camAfter[b];
                return tb.CompareTo(ta);
            });
            sb.Length = 0;
            sb.Append("cameras: ");
            int shown = 0;
            for (int k = 0; k < n && shown < topCameras; k++)
            {
                int i = order[k];
                if (_camCount[i] == 0) continue;
                if (shown > 0) sb.Append(", ");
                sb.Append(_camNames[i]).Append(' ').Append(Ms(PerFrame(_camRender[i], freq)));
                double after = PerFrame(_camAfter[i], freq);
                if (after >= 0.005) sb.Append(" +").Append(Ms(after)).Append(" after");
                double per = (double)_camCount[i] / _frames;
                if (per < 0.995 || per > 1.005) sb.Append(" (x").Append(per.ToString("0.##")).Append("/f)");
                shown++;
            }
            if (_otherCams > 0) sb.Append(shown > 0 ? ", " : "").Append("other cameras ").Append(Ms(PerFrame(_otherCams, freq)));
            if (shown == 0 && _otherCams == 0) sb.Append("none");
            lines.Add(sb.ToString());
            return lines;
        }

        private static string Ms(double ms) { return ms.ToString("0.00"); }
    }
}
