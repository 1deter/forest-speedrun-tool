using BepInEx.Configuration;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Replays that show what happened (docs/run-audit-and-replays.md part
    // 2; author, 2026-10-03: in game first).
    //
    // Recording - every timed run, run mode or not: what the runner did
    // goes on the run's event track (`e|` lines, Data/RunEvent) at its
    // time and place, and each structure placed / finished on its
    // building track (`b|`, Data/RunBuilding). Sources, all already
    // watched:
    //   Ctx.Events     caves, clothing, passengers, ropes, keycard doors,
    //                  the endgame, and the game's event bus (built,
    //                  crafted, used, kills, hits, trees, bombs, sleep,
    //                  story, endgame area) and rides - the general names
    //                  only (Data/ReplayMarks.KindFor)
    //   AuditWatch     the pause menu (PauseOpen / PauseToggles)
    //   DeathHooks     deaths (a counter)
    //   Game/BuildWatch  blueprints placed and structures finished, with
    //                  their place, rotation and box
    //
    // Playback - the comparison run (the ghost's): its buildings as
    // wireframe boxes from their time on, a marker at each interaction
    // along its line (behind the ghost in full colour, ahead faded), and
    // a label over the markers nearest the camera (Game/ReplayDraw; the
    // labels in DrawScreen from cached text). Idle (no run going) it shows
    // the whole run's end state. Runs tab: "Replay shows: buildings /
    // interaction markers" (persisted).
    // ------------------------------------------------------------------
    public sealed partial class PracticeRunModule
    {
        private const float LabelPickEvery = 0.2f;

        private ConfigEntry<bool> _replayBuildingsCfg, _replayMarkersCfg;
        private ReplayBehaviour _replay;
        private Attempt _replaySource;
        private string[] _markerLabels = new string[0];

        private readonly int[] _labelIdx = new int[ReplayMarks.MaxLabels];
        private readonly float[] _labelDist = new float[ReplayMarks.MaxLabels];
        private readonly GUIContent[] _labelText = NewContents(ReplayMarks.MaxLabels);
        private readonly Vector3[] _labelPos = new Vector3[ReplayMarks.MaxLabels];
        private int _labelCount;
        private float _nextLabelPick;
        private GUIStyle _labelStyle, _labelShadow;

        // Recording state.
        private int _replayDeaths, _replayPauses;
        private bool _replayPaused;
        private float _replayPausedAt;

        private readonly GUIContent _replayText = new GUIContent("");
        private int _replayKeyEvents = -2, _replayKeyBuildings = -2, _replayKeyWatch = -1;

        private static GUIContent[] NewContents(int n)
        {
            GUIContent[] c = new GUIContent[n];
            for (int i = 0; i < n; i++) c[i] = new GUIContent("");
            return c;
        }

        private void InitReplay(ModuleContext ctx)
        {
            _replayBuildingsCfg = ctx.Config.Bind("Runs", "ReplayBuildings", true,
                "Replays: draw the comparison run's buildings (placed blueprints and finished structures) as wireframe boxes.");
            _replayMarkersCfg = ctx.Config.Bind("Runs", "ReplayMarkers", true,
                "Replays: a marker at each thing the comparison run did (crafted, ate, kills, rides, pause ...), labelled when near.");
            _replay = _lineHost.AddComponent<ReplayBehaviour>();
            BuildWatch.Install(ctx.Log, OverlayPlugin.PluginGuid);
        }

        private void ShutdownReplay()
        {
            BuildWatch.Uninstall();
        }

        // --- recording -----------------------------------------------------------

        /// The clock just started (or resumed): the tracks start empty.
        private void ReplayRunStarted()
        {
            BuildWatch.Reset();
            _replayDeaths = DeathHooks.Deaths;
            _replayPauses = AuditWatch.PauseToggles;
            _replayPaused = AuditWatch.PauseOpen;
            _replayPausedAt = Time.unscaledTime;
        }

        /// One of Ctx.Events as it is evaluated (before the occurrence
        /// gate: a companion name is the one that carries the detail).
        private void RecordReplayEvent(int at, Vector3 pos)
        {
            if (_recorder.State != RunRecorder.RunState.Running) return;
            string text;
            string detail = Ctx.Events.DetailAt(at);
            string kind = ReplayMarks.KindFor(Ctx.Events.NameAt(at), detail, out text);
            if (kind == null) return;
            if ((kind == WorldEvents.CaveEnter || kind == WorldEvents.CaveExit) && !string.IsNullOrEmpty(detail))
                text = WorldEvents.CaveLabel(detail) ?? detail;
            _recorder.RecordEvent(kind, text, pos);
        }

        /// Every frame, before Tick's early returns: the sources that are
        /// not Ctx.Events.
        private void TickReplayRecording()
        {
            bool running = _recorder.State == RunRecorder.RunState.Running && _segment != null;
            BuildWatch.Recording = running;
            if (!running)
            {
                BuildWatch.Pending.Clear();
                _replayDeaths = DeathHooks.Deaths;
                _replayPauses = AuditWatch.PauseToggles;
                _replayPaused = AuditWatch.PauseOpen;
                return;
            }

            Vector3 pos = PlayerPosition();
            for (int i = 0; i < BuildWatch.Pending.Count; i++) _recorder.RecordBuilding(BuildWatch.Pending[i]);
            BuildWatch.Pending.Clear();

            if (DeathHooks.Deaths != _replayDeaths)
            {
                _replayDeaths = DeathHooks.Deaths;
                _recorder.RecordEvent(RunAudit.Death, null, pos);
            }

            if (AuditWatch.PauseToggles != _replayPauses)
            {
                _replayPauses = AuditWatch.PauseToggles;
                bool open = AuditWatch.PauseOpen;
                if (open != _replayPaused)
                {
                    _replayPaused = open;
                    if (open)
                    {
                        _replayPausedAt = Time.unscaledTime;
                        _recorder.RecordEvent(RunAudit.PauseOpen, null, pos);
                    }
                    else
                        _recorder.RecordEvent(RunAudit.PauseClose, "open " + (Time.unscaledTime - _replayPausedAt).ToString("0.0") + " s", pos);
                }
            }
        }

        // --- playback --------------------------------------------------------------

        private void UpdateReplay()
        {
            if (_replay == null) return;
            bool wanted = _replayBuildingsCfg.Value || _replayMarkersCfg.Value;
            bool visible = wanted && Enabled && _reference != null &&
                           (_practice == null || ReferenceEquals(_practice.SelectedSegment, _segment));
            if (!visible) { ClearReplay(); return; }

            if (!ReferenceEquals(_replaySource, _reference))
            {
                _replaySource = _reference;
                _replay.SetSource(_reference);
                int n = _reference.Events.Count;
                if (_markerLabels.Length < n) _markerLabels = new string[n];
                for (int i = 0; i < n; i++) _markerLabels[i] = ReplayMarks.Label(_reference.Events[i]);
                _labelCount = 0;
                _nextLabelPick = 0f;
            }

            bool running = _recorder.State == RunRecorder.RunState.Running;
            float upTo = ReplayMarks.ShownUpTo(running, _recorder.Elapsed);
            _replay.UpTo = upTo;
            _replay.MarkersPassed = running ? ReplayMarks.UpTo(_reference.Events, upTo) : int.MaxValue;
            _replay.ShowBuildings = _replayBuildingsCfg.Value;
            _replay.ShowMarkers = _replayMarkersCfg.Value;
            _replay.Opacity = LineOpacity;

            if (!_replayMarkersCfg.Value) { _labelCount = 0; return; }
            if (Time.unscaledTime < _nextLabelPick) return;
            _nextLabelPick = Time.unscaledTime + LabelPickEvery;

            Camera cam = DrawTarget.View();
            Vector3 viewer = cam != null ? cam.transform.position : PlayerPosition();
            int count = ReplayMarks.Nearest(_reference.Events, _reference.Events.Count, viewer, ReplayMarks.LabelRadius, _labelIdx, _labelDist);
            for (int k = 0; k < count; k++)
            {
                int i = _labelIdx[k];
                _labelText[k].text = _markerLabels[i];
                Vector3 p = _reference.Events[i].P;
                _labelPos[k] = new Vector3(p.x, p.y + ReplayBehaviour.MarkerHeight + 0.5f, p.z);
            }
            _labelCount = count;
        }

        private void ClearReplay()
        {
            _labelCount = 0;
            if (_replaySource == null || _replay == null) return;
            _replaySource = null;
            _replay.SetSource(null);
        }

        /// From DrawScreen: the labels over the nearest markers. Repaint
        /// only; text cached in UpdateReplay.
        private void DrawReplayLabels()
        {
            if (_labelCount == 0) return;
            Event e = Event.current;
            if (e == null || e.type != EventType.Repaint) return;
            Camera cam = DrawTarget.View();
            if (cam == null) return;
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label);
                _labelStyle.alignment = TextAnchor.MiddleCenter;
                _labelStyle.fontSize = 13;
                _labelStyle.normal.textColor = Color.white;
                _labelShadow = new GUIStyle(_labelStyle);
                _labelShadow.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            }
            for (int k = 0; k < _labelCount; k++)
            {
                Vector3 sp = cam.WorldToScreenPoint(_labelPos[k]);
                if (sp.z <= 0f) continue;
                Rect r = new Rect(sp.x - 160f, Screen.height - sp.y - 11f, 320f, 22f);
                GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), _labelText[k], _labelShadow);
                GUI.Label(r, _labelText[k], _labelStyle);
            }
        }

        // --- the Runs tab ------------------------------------------------------------

        // From RefreshTabText's throttled part; rebuilt when it changes.
        private void RefreshReplayText()
        {
            Attempt a = _reference;
            int ev = a != null ? a.Events.Count : -1;
            int bu = a != null ? a.Buildings.Count : -1;
            bool watchOk = BuildWatch.Status.StartsWith("watching") && !BuildWatch.Status.Contains("not found");
            if (ev == _replayKeyEvents && bu == _replayKeyBuildings && (watchOk ? 1 : 0) == _replayKeyWatch) return;
            _replayKeyEvents = ev;
            _replayKeyBuildings = bu;
            _replayKeyWatch = watchOk ? 1 : 0;

            string text;
            if (a == null) text = "";
            else if (ev == 0 && bu == 0)
                text = "The comparison run has no interactions or buildings recorded (runs before this version keep none, or nothing happened).";
            else
                text = "Comparison run: " + ev + (ev == 1 ? " interaction" : " interactions") + ", " +
                       bu + (bu == 1 ? " building" : " buildings") + " - labels show within " + ReplayMarks.LabelRadius.ToString("0") + " m.";
            if (!watchOk) text += (text.Length > 0 ? " " : "") + "Buildings: " + BuildWatch.Status + ".";
            _replayText.text = text;
        }

        private float DrawReplayOptions(float y, float w)
        {
            GUI.Label(new Rect(0, y, 104, 20), "Replay shows:");
            bool oneRow = w >= 400f;
            float x = oneRow ? 108f : 12f;
            if (!oneRow) y += 20f;
            bool b = GUI.Toggle(new Rect(x, y, 100, 20), _replayBuildingsCfg.Value, " buildings");
            if (b != _replayBuildingsCfg.Value) _replayBuildingsCfg.Value = b;
            if (!oneRow) y += 20f; else x += 104f;
            bool m = GUI.Toggle(new Rect(x, y, 180, 20), _replayMarkersCfg.Value, " interaction markers");
            if (m != _replayMarkersCfg.Value) _replayMarkersCfg.Value = m;
            y += 22f;
            if (_replayText.text.Length > 0) y += UiText.Draw(0, y, w, _replayText);
            return y + 4f;
        }
    }
}
