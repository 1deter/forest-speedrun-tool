using BepInEx.Configuration;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // The ghost with a body and the replay camera (docs/run-audit-and-
    // replays.md part 2: the "grenade camera" idea, first-person replays;
    // author, 2026-10-03: in game first, every trajectory variant, runners
    // say which helps).
    //
    // GHOST LOOK (Runs tab, `[Runs] GhostLook`): the comparison run's ghost
    // as the old marker or as a figure (Data/GhostFigure: capsule body,
    // head, an arrow out of the chest along where it faced, a gaze line
    // along where it looked). The facing is the run's recorded look (the
    // `l|` track, recorded with every position sample since this version)
    // or, for older runs, the way it moved.
    //
    // REPLAY CAMERA (Experimental, practice; locked in run mode as
    // "replaycam"): plays the comparison run back on its own clock (pause,
    // seek, 0.1x - 2x) and flies the game's own camera after the ghost -
    // through a second FreeCamBehaviour, the freecam's tested handling
    // (gotchas 56 / 58: no camera copied, none switched off; the gameplay
    // children parked on a stand-in, the look scripts paused). Views:
    //   chase         behind and above, smoothed
    //   first person  at the recorded eye with the recorded look (a run
    //                 with no look track: where it moved, level)
    //   trajectory    side-on, framing the next 4 / last 2 s of the path,
    //                 the framed stretch drawn white - for boosts / ziplines
    // The player is held while it is on (HoldsPlayer: LockView + the game's
    // input map, gotcha 1). It ends on Esc, its key, the tab's button, F9
    // off, run mode, a new run starting, or the camera torn down by a load.
    // No timed run arms or starts while it is on (triggers are not read).
    // ------------------------------------------------------------------
    public sealed partial class PracticeRunModule
    {
        public const string GhostMarker = "Marker";
        public const string GhostFigureLook = "Figure";
        private const float SeekStep = 1f, SeekStepShift = 5f, FrameStep = 1f / 30f;
        private const float CameraTextEvery = 0.1f;

        private ConfigEntry<string> _ghostLookCfg;
        private FreeCamBehaviour _replayCam;
        private bool _camOn;
        private ReplayView _camView = ReplayView.Chase;
        private readonly ReplayClock _clock = new ReplayClock();
        private Attempt _camSource;
        private CamPose _camPose;
        private bool _camSnap;
        private float _ghostYaw;
        private float _nextCameraText;

        private readonly GUIContent _camStatusText = new GUIContent("");
        private readonly GUIContent _camTimeText = new GUIContent("");
        private string _camHud = "";
        // What follows the replay time in the HUD: " / duration  x1 paused" -
        // rebuilt when the duration, speed or play state moves, not every refresh.
        private string _camTail = "";
        private float _camTailDuration = -1f;
        private int _camTailSpeed = -1;
        private bool _camTailPaused, _camTailAtEnd;

        private static readonly GUIContent CameraHeading = new GUIContent(
            "Replay camera - Experimental, a practice view (it holds you still while it plays):");
        private static readonly GUIContent CameraHelp = new GUIContent(
            "Plays the comparison run back and follows its ghost. Close this window to use the keys: Space play / pause, " +
            "Left / Right 1 s back / on (Shift 5 s), , and . one frame while paused, Up / Down faster / slower, " +
            "1 chase, 2 first person, 3 trajectory (V cycles), R from the start, Esc stops. " +
            "First person shows where the runner looked for runs recorded from this version on; older runs face where they moved.");

        public override bool HoldsPlayer { get { return _camOn; } }

        private bool FigureLook { get { return _ghostLookCfg == null || _ghostLookCfg.Value != GhostMarker; } }

        /// The replay camera is on (the bridge reads this).
        public bool ReplayCameraOn { get { return _camOn; } }

        private void InitCamera(ModuleContext ctx)
        {
            _ghostLookCfg = ctx.Config.Bind("Runs", "GhostLook", GhostFigureLook,
                "The comparison run's ghost: Figure (a body facing where the run looked) or Marker (the old post).");
            _replayCam = _lineHost.AddComponent<FreeCamBehaviour>();
            _replayCam.Owner = "the replay camera";
            _replayCam.InputEnabled = false;
            _recorder.LookSource = ReadLook;
        }

        /// The look sampled with each position (the run's `l|` track): the
        /// view camera's angles and height above the player. None while the
        /// view is flown away (freecam, the replay camera).
        private bool ReadLook(out float yaw, out float pitch, out float eye)
        {
            yaw = 0f; pitch = 0f; eye = 0f;
            if (DrawTarget.FreeCam != null || !Ctx.Player.Found) return false;
            Camera cam = DrawTarget.View();
            if (cam == null) return false;
            Transform t = cam.transform;
            Vector3 e = t.eulerAngles;
            yaw = ReplayCamera.Wrap180(e.y);
            pitch = ReplayCamera.Wrap180(e.x);
            eye = t.position.y - Ctx.Player.Transform.position.y;
            return true;
        }

        // --- on / off ----------------------------------------------------------------

        /// The replay camera on / off (its hotkey, the Runs tab, the bridge).
        public void ToggleReplayCamera()
        {
            if (_camOn) { EndReplayCamera("turned off"); return; }
            StartReplayCamera();
        }

        private void StartReplayCamera()
        {
            if (Ctx.Run.Refuse("replaycam", "the replay camera")) { SetCamStatus(Ctx.Run.RefusedText("The replay camera")); return; }
            if (!Enabled) { SetCamStatus("Practice mode is off - turn it on (F9) and Go to a timed segment with a finished run."); return; }
            if (_segment == null) { SetCamStatus("No timed segment - Go to one in the Practice tab first."); return; }
            if (_reference == null || _reference.Samples.Count < 2) { SetCamStatus("No comparison run to watch - finish a run of this segment first (or pick another Compare to)."); return; }
            if (_recorder.State == RunRecorder.RunState.Running) { SetCamStatus("A run is going - finish or abort it first."); return; }
            if (!Ctx.Player.Found) { SetCamStatus("No player yet."); return; }
            Camera cam = Camera.main;
            if (cam == null) { SetCamStatus("No main camera."); return; }
            if (!_replayCam.Begin(cam)) { SetCamStatus("Replay camera: " + _replayCam.LastReport + " - switch it off first."); return; }
            _replayCam.InputEnabled = false;

            _camOn = true;
            _camSource = _reference;
            _clock.Start(ReplayCamera.EndOf(_reference), 0f);
            _ghostYaw = ReplayCamera.HeadingAt(_reference, 0f, 0f);
            _camSnap = true;
            _nextCameraText = 0f;
            Ctx.Practice.Mark("replay camera");
            SetCamStatus("");
            Ctx.Log.LogInfo("Replay camera: on - '" + _segment.Id + "' comparison run of " + _clock.Duration.ToString("0.00") + " s, " +
                            _reference.Looks.Count + " look sample(s), " + _reference.Samples.Count + " position(s), view " +
                            ReplayCamera.Label(_camView) + ". " + _replayCam.LastReport + ".");
            if (Host == null || !Host.AnyPanelOpen())
                Ctx.Notice.Show("Replay camera: Space play / pause, Left / Right seek, Up / Down speed, 1 2 3 views, Esc stops.", 7f);
            TickReplayCamera();
        }

        private void EndReplayCamera(string why)
        {
            if (!_camOn) return;
            _camOn = false;
            _replayCam.End();
            _camSource = null;
            if (_lines != null) _lines.PathCount = 0;
            _camHud = "";
            SetCamStatus("Replay camera off (" + why + ").");
            Ctx.Log.LogInfo("Replay camera: off - " + why + ", at " + _clock.T.ToString("0.00") + " s.");
        }

        private void SetCamStatus(string text)
        {
            if (text != _camStatusText.text) _camStatusText.text = text;
        }

        private void SetView(ReplayView v)
        {
            if (v == _camView) return;
            _camView = v;
            _camSnap = v == ReplayView.FirstPerson;   // the others fly there
            Ctx.Log.LogInfo("Replay camera: view " + ReplayCamera.Label(v) + ".");
        }

        // --- every frame while on ------------------------------------------------------

        /// From Tick while the camera is on (instead of the run's triggers).
        private void TickReplayCamera()
        {
            if (!_camOn) return;
            if (!_replayCam.Active) { EndReplayCamera("the camera changed (a load)"); return; }
            if (!Enabled) { EndReplayCamera("practice mode off"); return; }
            if (Ctx.Run.Active && Ctx.Run.Locks("replaycam")) { EndReplayCamera("run mode"); return; }
            if (_recorder.State == RunRecorder.RunState.Running) { EndReplayCamera("a run started"); return; }
            if (!Ctx.Player.Found || _segment == null) { EndReplayCamera("no player / segment"); return; }
            if (_reference == null || _reference.Samples.Count < 2) { EndReplayCamera("no comparison run"); return; }
            if (!ReferenceEquals(_camSource, _reference))
            {
                // Compare to changed: the new run, from the same time.
                _camSource = _reference;
                _clock.Start(ReplayCamera.EndOf(_reference), _clock.T);
                _camSnap = true;
            }

            if (Host == null || !Host.AnyPanelOpen())
            {
                if (Input.GetKeyDown(KeyCode.Escape)) { EndReplayCamera("Esc"); return; }
                ReadCameraKeys();
            }

            float dt = Time.unscaledDeltaTime;
            _clock.Advance(dt);

            GhostPose g;
            if (!ReplayCamera.PoseAt(_reference, _clock.T, _ghostYaw, out g)) return;
            _ghostYaw = g.Yaw;

            Camera cam = _replayCam.Camera;
            CamPose target;
            float rate;
            int window = 0;
            switch (_camView)
            {
                case ReplayView.FirstPerson:
                    target = ReplayCamera.FirstPerson(g);
                    rate = g.HasLook ? 0f : 8f;   // a recorded look as it was; a heading turns smoothly
                    break;
                case ReplayView.Trajectory:
                    target = ReplayCamera.Trajectory(_reference, _clock.T, ReplayCamera.TrajectoryBack, ReplayCamera.TrajectoryAhead,
                                                     cam != null ? cam.fieldOfView : 60f, cam != null ? cam.aspect : 16f / 9f,
                                                     g.Yaw, _lines.PathWindow, out window);
                    rate = ReplayCamera.TrajectoryRate;
                    break;
                default:
                    target = ReplayCamera.Chase(g);
                    rate = ReplayCamera.ChaseRate;
                    break;
            }
            _lines.PathCount = window;

            _camPose = _camSnap ? target : ReplayCamera.Smooth(_camPose, target, rate, dt);
            _camSnap = false;
            _replayCam.Place(_camPose.Position, _camPose.Pitch, _camPose.Yaw);

            UpdateLines();
            UpdateReplay();
            RefreshCameraText();
        }

        private void ReadCameraKeys()
        {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (Input.GetKeyDown(KeyCode.Space)) _clock.TogglePause();
            if (Input.GetKeyDown(KeyCode.LeftArrow)) { _clock.Seek(-(shift ? SeekStepShift : SeekStep)); _camSnap = true; }
            if (Input.GetKeyDown(KeyCode.RightArrow)) { _clock.Seek(shift ? SeekStepShift : SeekStep); _camSnap = true; }
            if (_clock.Paused && Input.GetKeyDown(KeyCode.Comma)) _clock.Seek(-FrameStep);
            if (_clock.Paused && Input.GetKeyDown(KeyCode.Period)) _clock.Seek(FrameStep);
            if (Input.GetKeyDown(KeyCode.UpArrow)) _clock.Faster();
            if (Input.GetKeyDown(KeyCode.DownArrow)) _clock.Slower();
            if (Input.GetKeyDown(KeyCode.Alpha1)) SetView(ReplayView.Chase);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetView(ReplayView.FirstPerson);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetView(ReplayView.Trajectory);
            if (Input.GetKeyDown(KeyCode.V)) SetView((ReplayView)(((int)_camView + 1) % 3));
            if (Input.GetKeyDown(KeyCode.R)) { _clock.Seek(-_clock.Duration); _clock.Paused = false; _camSnap = true; }
        }

        // Throttled text for the HUD and the tab (never built in OnGUI).
        private void RefreshCameraText()
        {
            if (Time.unscaledTime < _nextCameraText) return;
            _nextCameraText = Time.unscaledTime + CameraTextEvery;
            if (_clock.Duration != _camTailDuration || _clock.SpeedIndex != _camTailSpeed ||
                _clock.Paused != _camTailPaused || _clock.AtEnd != _camTailAtEnd)
            {
                _camTailDuration = _clock.Duration;
                _camTailSpeed = _clock.SpeedIndex;
                _camTailPaused = _clock.Paused;
                _camTailAtEnd = _clock.AtEnd;
                _camTail = " / " + Format(_clock.Duration) + "  x" + _clock.Speed.ToString("0.##") +
                           (_clock.Paused ? (_clock.AtEnd ? "  (end - Space plays again)" : "  paused") : "");
            }
            _camHud = ReplayCamera.Label(_camView) + "  " + ClockText.Clock(_clock.T) + _camTail;
            // The tab's line: only built while the tab shows it.
            if (!TabShowing) return;
            _camTimeText.text = "Watching: " + _camHud +
                                (_reference != null && _reference.Looks.Count == 0 && _camView == ReplayView.FirstPerson
                                    ? " - this run has no recorded look: the view faces where it moved." : "");
        }

        // --- the ghost's figure (UpdateLines) --------------------------------------------

        /// The ghost at `t` into RunLineBehaviour: position always, the
        /// figure's lines when the look is Figure. Hidden in first person
        /// (the camera is inside it).
        /// The Map tab (Modules/MapModule): the comparison run, the segment
        /// being timed, and where the ghost is - reads only.
        public Attempt ComparisonRun { get { return _reference; } }
        public Segment TimedSegment { get { return _segment; } }

        public bool GhostOnMap(out Vector3 position)
        {
            position = Vector3.zero;
            if (_lines == null || !_lines.Show || !_lines.HasGhost) return false;
            position = _lines.GhostPosition;
            return true;
        }

        private void SetGhost(float t, Vector3 position, bool exact)
        {
            GhostPose g;
            bool pose = ReplayCamera.PoseAt(_reference, t, _ghostYaw, out g);
            if (pose) _ghostYaw = g.Yaw;
            _lines.GhostPosition = exact && pose ? g.P : position;
            _lines.HasGhost = !(_camOn && _camView == ReplayView.FirstPerson);
            _lines.DrawFigure = FigureLook && pose;
            _lines.FigureCount = _lines.DrawFigure
                ? GhostFigure.Build(_lines.GhostPosition, g.Yaw, g.Pitch, ReplayCamera.FigureHeight(g), _lines.FigureVerts)
                : 0;
        }

        // --- the Runs tab --------------------------------------------------------------------

        private float DrawGhostLook(float y, float w)
        {
            GUI.Label(new Rect(0, y, 90, 20), "Ghost look:");
            bool figure = FigureLook;
            if (GUI.Toggle(new Rect(94, y, 80, 20), !figure, " marker") && figure) _ghostLookCfg.Value = GhostMarker;
            if (GUI.Toggle(new Rect(178, y, 160, 20), figure, " figure (facing)") && !figure) _ghostLookCfg.Value = GhostFigureLook;
            return y + 24f;
        }

        private float DrawCameraSection(float y, float w)
        {
            y += UiText.Draw(0, y, w, CameraHeading);
            if (GUI.Button(new Rect(0, y, 200, 22), _camOn ? "Stop watching" : "Watch the comparison run")) ToggleReplayCamera();
            y += 26f;

            GUI.Label(new Rect(0, y, 50, 20), "View:");
            bool oneRow = w >= 420f;
            float x = 54f;
            ReplayView v = _camView;
            if (GUI.Toggle(new Rect(x, y, 70, 20), v == ReplayView.Chase, " chase")) v = ReplayView.Chase;
            x += 74f;
            if (!oneRow) { y += 20f; x = 54f; }
            if (GUI.Toggle(new Rect(x, y, 110, 20), v == ReplayView.FirstPerson, " first person")) v = ReplayView.FirstPerson;
            x += 114f;
            if (!oneRow) { y += 20f; x = 54f; }
            if (GUI.Toggle(new Rect(x, y, 100, 20), v == ReplayView.Trajectory, " trajectory")) v = ReplayView.Trajectory;
            if (v != _camView) SetView(v);
            y += 24f;

            if (_camOn)
            {
                y += UiText.Draw(0, y, w, _camTimeText);
                float t = GUI.HorizontalSlider(new Rect(0, y + 4, w - 4, 16), _clock.T, 0f, Mathf.Max(0.01f, _clock.Duration));
                if (Mathf.Abs(t - _clock.T) > 0.0001f) { _clock.T = t; _camSnap = true; }
                y += 24f;
                // One row when it fits, else two (narrow windows).
                bool wide = w >= 350f;
                if (GUI.Button(new Rect(0, y, 90, 22), _clock.Paused ? "Play" : "Pause")) _clock.TogglePause();
                if (GUI.Button(new Rect(94, y, 60, 22), "-1 s")) { _clock.Seek(-1f); _camSnap = true; }
                if (GUI.Button(new Rect(158, y, 60, 22), "+1 s")) { _clock.Seek(1f); _camSnap = true; }
                float bx = wide ? 222f : 0f;
                if (!wide) y += 26f;
                if (GUI.Button(new Rect(bx, y, 60, 22), "slower")) _clock.Slower();
                if (GUI.Button(new Rect(bx + 64f, y, 60, 22), "faster")) _clock.Faster();
                y += 26f;
            }
            if (_camStatusText.text.Length > 0) y += UiText.Draw(0, y, w, _camStatusText);
            y += UiText.Draw(0, y, w, CameraHelp);
            return y + 6f;
        }
    }
}
