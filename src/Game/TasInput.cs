using System;
using System.Collections.Generic;
using System.Reflection;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // TAS input record / replay (Experimental, practice only) - the game
    // side. Rides on Game/InputInject's postfixes on TheForest.Utils.Input
    // (GetButton / GetButtonDown / GetButtonUp / GetAxis / GetAxisDown),
    // which every script and PlayMaker action reads the game's Rewired
    // actions through (IL: Input.GetAxis is `player.GetAxis(axis)`, no
    // scaling; GetAxisDown is `GetAxisPrev == 0 ? GetAxis : 0`).
    //
    // RECORD: each postfix notes the value the game got this frame (the
    // player's input, plus a bridge press / hold - so a bridge-scripted
    // test records too). At the end of the frame (Modules/TasModule's
    // WaitForEndOfFrame loop) every name the game has asked for is
    // committed: the value read this frame, else Rewired's own state for
    // it (a button only read as GetButtonDown has no held reading on the
    // frames it is not pressed). Changes only (Data/TasRecording).
    //
    // REPLAY: the postfixes REPLACE every result with the recording's
    // value for the frame (frame 0 = the frame after the restart placed
    // the player) - which is also what blocks the player's own input; a
    // name the recording never saw reads as not pressed / 0. Down and Up
    // come from the held state the frame before, as Rewired defines them.
    // Raw keys (Input.GetKey*, the overlay's F-keys) are not routed
    // through here, so the overlay's Stop key still works.
    //
    // LOOK: the mouse axes are per-frame deltas the game multiplies by the
    // sensitivity with no delta time (SimpleMouseRotator.GetInput, IL), so
    // replaying them on the same frames gives the same target angles at
    // any frame rate; only the smoothing (Vector3.SmoothDamp, delta time)
    // and the body's MoveRotation depend on frame time - covered by the
    // frame-rate lock. So look is NOT forced per frame; the 30 Hz samples
    // carry yaw / pitch to measure its drift instead.
    //
    // FRAME LOCK: Time.captureFramerate (Unity 5.6 has no
    // captureDeltaTime) makes every frame's delta time exactly 1/fps,
    // whatever the real frame took; the replay sets it before each frame
    // to the recorded frame's rate, so frame N runs the same game time as
    // it did when recorded (to the rounding of 1/fps), and
    // Application.targetFrameRate keeps the real pace near it (ignored
    // under vsync - the replay then runs slower or faster, never wrong).
    // Both are put back after.
    // ------------------------------------------------------------------
    public static class TasInput
    {
        public enum Mode { Off, Recording, Blocking, Playing }

        public static Mode Current { get; private set; }

        // --- recording ---------------------------------------------------
        private static TasRecording _rec;
        private static int _recStart;
        private static readonly List<int> _btnObsFrame = new List<int>();
        private static readonly List<bool> _btnObs = new List<bool>();
        private static readonly List<int> _axObsFrame = new List<int>();
        private static readonly List<float> _axObs = new List<float>();

        // --- replay --------------------------------------------------------
        private static TasPlayback _play;
        private static int _playStart;

        // --- Rewired, for names not read this frame -----------------------
        private static FieldInfo _playerField;
        private static object _boundPlayer;
        private static Func<string, bool> _rwButton;
        private static Func<string, float> _rwAxis;
        private static bool _rwResolved;
        public static string RewiredStatus = "not bound yet";

        // --- frame lock ------------------------------------------------------
        private static bool _locked;
        private static int _savedCapture;
        private static int _savedTarget;

        public static TasRecording Recording { get { return _rec; } }
        public static TasPlayback Playback { get { return _play; } }

        /// The frame index the current recording / replay is on (-1 before frame 0).
        public static int FrameIndex
        {
            get
            {
                if (Current == Mode.Recording) return Time.frameCount - _recStart;
                if (Current == Mode.Playing) return Time.frameCount - _playStart;
                return -1;
            }
        }

        // ------------------------------------------------------------------
        /// Records from the NEXT frame (frame 0) into `rec`. Names the game
        /// has read before are added now (Rewired gives their state).
        public static void StartRecording(TasRecording rec, List<string> buttons, List<string> axes)
        {
            _rec = rec;
            _recStart = Time.frameCount + 1;
            _btnObsFrame.Clear(); _btnObs.Clear(); _axObsFrame.Clear(); _axObs.Clear();
            for (int i = 0; i < buttons.Count; i++) ButtonCh(buttons[i]);
            for (int i = 0; i < axes.Count; i++) AxisCh(axes[i]);
            _play = null;
            Current = Mode.Recording;
        }

        /// Every read blocked (not pressed / 0) until StartPlaying.
        public static void Block(TasPlayback play)
        {
            _play = play;
            _rec = null;
            Current = Mode.Blocking;
        }

        /// Replays from the NEXT frame (frame 0).
        public static void StartPlaying()
        {
            if (_play == null) return;
            _playStart = Time.frameCount + 1;
            Current = Mode.Playing;
        }

        public static void Stop()
        {
            Current = Mode.Off;
            _rec = null;
            _play = null;
        }

        // ------------------------------------------------------------------
        // From InputInject's postfixes. kind: 0 GetButton, 1 GetButtonDown, 2 GetButtonUp.
        internal static void Button(string name, int kind, ref bool result)
        {
            switch (Current)
            {
                case Mode.Recording:
                {
                    if (name == null) return;
                    int ch = ButtonCh(name);
                    int frame = Time.frameCount;
                    if (kind == 0) { _btnObs[ch] = result; _btnObsFrame[ch] = frame; }
                    else if (result) { _btnObs[ch] = kind == 1; _btnObsFrame[ch] = frame; }
                    return;
                }
                case Mode.Blocking:
                    result = false;
                    return;
                case Mode.Playing:
                {
                    int ch = _play.FindButton(name);
                    int f = Time.frameCount - _playStart;
                    result = kind == 0 ? _play.Held(ch, f) : kind == 1 ? _play.Down(ch, f) : _play.Up(ch, f);
                    return;
                }
            }
        }

        internal static void Axis(string name, bool down, ref float result)
        {
            switch (Current)
            {
                case Mode.Recording:
                {
                    if (name == null) return;
                    int ch = AxisCh(name);
                    if (!down) { _axObs[ch] = result; _axObsFrame[ch] = Time.frameCount; }
                    return;
                }
                case Mode.Blocking:
                    result = 0f;
                    return;
                case Mode.Playing:
                {
                    int ch = _play.FindAxis(name);
                    int f = Time.frameCount - _playStart;
                    result = down ? _play.AxisDown(ch, f) : _play.Axis(ch, f);
                    return;
                }
            }
        }

        private static int ButtonCh(string name)
        {
            int ch = _rec.ButtonChannel(name);
            while (_btnObs.Count <= ch) { _btnObs.Add(false); _btnObsFrame.Add(-1); }
            return ch;
        }

        private static int AxisCh(string name)
        {
            int ch = _rec.AxisChannel(name);
            while (_axObs.Count <= ch) { _axObs.Add(0f); _axObsFrame.Add(-1); }
            return ch;
        }

        // ------------------------------------------------------------------
        /// End of a recorded frame: every channel's value committed. Returns
        /// the frame index committed, -1 before frame 0.
        public static int CommitRecording(InjectedInputs bridge)
        {
            if (Current != Mode.Recording || _rec == null) return -1;
            int frame = Time.frameCount;
            int i = frame - _recStart;
            if (i < 0) return -1;
            BindRewired();
            float now = Time.realtimeSinceStartup;

            for (int ch = 0; ch < _rec.Buttons.Count && ch < _btnObs.Count; ch++)
            {
                bool held;
                if (_btnObsFrame[ch] == frame) held = _btnObs[ch];
                else held = PollButton(_rec.Buttons[ch]) || (bridge != null && bridge.Any && bridge.Held(_rec.Buttons[ch], frame, now));
                _rec.SetButton(i, ch, held);
            }
            for (int ch = 0; ch < _rec.Axes.Count && ch < _axObs.Count; ch++)
            {
                float v;
                if (_axObsFrame[ch] == frame) v = _axObs[ch];
                else
                {
                    v = PollAxis(_rec.Axes[ch]);
                    float inj;
                    if (bridge != null && bridge.Any && bridge.TryAxis(_rec.Axes[ch], frame, now, out inj)) v = inj;
                }
                _rec.SetAxis(i, ch, v);
            }

            int fps = _rec.LockFps > 0 ? _rec.LockFps : FpsOf(Time.unscaledDeltaTime);
            _rec.SetFps(i, fps);
            _rec.EndFrame(i);
            return i;
        }

        public static int FpsOf(float dt)
        {
            if (dt <= 0f) return 0;
            return Mathf.Clamp(Mathf.RoundToInt(1f / dt), 5, 1000);
        }

        private static void BindRewired()
        {
            try
            {
                if (!_rwResolved)
                {
                    _rwResolved = true;
                    Type input = GameBridge.FindGameType("TheForest.Utils.Input");
                    if (input != null) _playerField = input.GetField("player", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (_playerField == null) RewiredStatus = "Input.player not found - names not read on a frame keep their last value";
                }
                if (_playerField == null) return;
                object p = _playerField.GetValue(null);
                if (ReferenceEquals(p, _boundPlayer)) return;
                _boundPlayer = p;
                _rwButton = null;
                _rwAxis = null;
                if (p == null) { RewiredStatus = "no Rewired player yet"; return; }
                MethodInfo b = p.GetType().GetMethod("GetButton", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null);
                MethodInfo a = p.GetType().GetMethod("GetAxis", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null);
                if (b != null) _rwButton = (Func<string, bool>)Delegate.CreateDelegate(typeof(Func<string, bool>), p, b, false);
                if (a != null) _rwAxis = (Func<string, float>)Delegate.CreateDelegate(typeof(Func<string, float>), p, a, false);
                RewiredStatus = _rwButton != null && _rwAxis != null ? "bound" : "Rewired Player.GetButton / GetAxis(string) not found";
            }
            catch (Exception ex)
            {
                _rwButton = null;
                _rwAxis = null;
                RewiredStatus = "could not bind Rewired: " + ex.Message;
            }
        }

        private static bool PollButton(string name)
        {
            if (_rwButton == null) return false;
            try { return _rwButton(name); }
            catch (Exception) { return false; }
        }

        private static float PollAxis(string name)
        {
            if (_rwAxis == null) return 0f;
            try { return _rwAxis(name); }
            catch (Exception) { return 0f; }
        }

        // ------------------------------------------------------------------
        /// Every frame's delta time = 1 / fps from the next frame on.
        public static void LockFrameRate(int fps)
        {
            if (fps <= 0) return;
            if (!_locked)
            {
                _locked = true;
                _savedCapture = Time.captureFramerate;
                _savedTarget = Application.targetFrameRate;
            }
            if (Time.captureFramerate != fps) Time.captureFramerate = fps;
            if (Application.targetFrameRate != fps) Application.targetFrameRate = fps;
        }

        /// Back to what it was; "" when nothing was locked.
        public static string UnlockFrameRate()
        {
            if (!_locked) return "";
            _locked = false;
            Time.captureFramerate = _savedCapture;
            Application.targetFrameRate = _savedTarget;
            return "frame rate unlocked (captureFramerate " + _savedCapture + ", targetFrameRate " + _savedTarget + ")";
        }

        public static bool FrameRateLocked { get { return _locked; } }
    }
}
