using System.Collections.Generic;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Practice runs: timed attempts between an anchor and a finish, with
    // a live delta against a reference attempt.
    //
    // Shaped after Momentum / KSF surf practice timers:
    //   * being placed at the anchor ARMS the run
    //   * the clock starts when you actually move, so lining up is free
    //   * finishing records the attempt and compares it
    //   * the delta is "at the point you are standing, the reference had
    //     taken N seconds", which is what a ghost tells you
    //
    // This is INFO-ONLY in itself - it reads position and time. The
    // teleporting that sets the anchor is what writes state, and that is
    // PracticeModule's business, not this one's.
    //
    // Run-line and ghost RENDERING are not here yet; this records and
    // stores the paths they need. See docs for the plan.
    // ------------------------------------------------------------------
    public sealed class PracticeRunModule : OverlayModule
    {
        public enum Reference { Best, Last, Average }

        public override string Id { get { return "practicerun"; } }
        public override string DisplayName { get { return "Practice runs"; } }
        public override bool HasPanel { get { return true; } }

        /// Practice mode is off until asked for. It was previously always
        /// live, which made the HUD line appear unbidden and meant every
        /// teleport armed a run whether or not you wanted one.
        public bool Enabled;

        private readonly RunRecorder _recorder = new RunRecorder();
        private readonly List<Attempt> _attempts = new List<Attempt>();
        private AttemptStore _store;

        // Run line rendering. Point buffers are reused and only rebuilt
        // when the underlying attempt changes, because OnRenderObject
        // walks them every frame.
        private GameObject _lineHost;
        private RunLineBehaviour _lines;
        private bool _showLines = true;
        private Attempt _lineSource;
        private int _currentLineCount;

        private Attempt _reference;
        private Reference _referenceKind = Reference.Best;
        private int _deltaHint;
        private float _delta;
        private bool _hasDelta;

        private string _status = "";
        private Rect _windowRect;
        private bool _windowPlaced;
        private Vector2 _scroll;
        private GUIStyle _rowStyle;

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);
            _store = new AttemptStore(ctx.Log, ctx.ConfigDirectory);

            _lineHost = new GameObject("ForestOverlay_RunLines");
            _lineHost.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(_lineHost);
            _lines = _lineHost.AddComponent<RunLineBehaviour>();

            // Hook the anchor so placing the player arms a run. Done by
            // event rather than by reaching into PracticeModule, so either
            // module can be removed without breaking the other.
            PracticeModule practice = Host.Find<PracticeModule>();
            if (practice != null)
            {
                practice.OnPlacedAtAnchor = OnPlacedAtAnchor;
                _practice = practice;
            }
            else
            {
                ctx.Log.LogWarning("PracticeRunModule: no PracticeModule found, runs must be armed manually.");
            }
        }

        private PracticeModule _practice;
        private string _loadedAnchor;

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("run.toggleMode", KeyCode.F9, "Practice mode on / off", ToggleMode);
            map.Add("run.finish", KeyCode.F12, "Finish practice run", FinishRun);
            // No separate restart key: PracticeModule's "return to anchor"
            // (F7) already raises OnPlacedAtAnchor, which re-arms a run.
            map.Add("run.abort", KeyCode.LeftBracket, "Abort practice run", AbortRun);
            map.Add("panel.runs", KeyCode.F8, "Practice runs panel", TogglePanel);
        }

        // ------------------------------------------------------------------
        private void ToggleMode()
        {
            Enabled = !Enabled;

            if (!Enabled)
            {
                _recorder.Abort();
                _hasDelta = false;
                ClearLines();
                _status = "practice mode off";
            }
            else
            {
                _status = "practice mode on - teleport or set an anchor";
            }
        }

        private void OnPlacedAtAnchor()
        {
            if (!Enabled) return;
            if (_practice == null || !_practice.HasAnchor) return;

            // Switching anchor switches "track": load that anchor's saved
            // attempts so a best time survives a restart and so someone
            // else's shared run folder can be raced straight away.
            if (_practice.AnchorLabel != _loadedAnchor)
            {
                _loadedAnchor = _practice.AnchorLabel;
                _attempts.Clear();
                _attempts.AddRange(_store.LoadAll(_loadedAnchor));
                Ctx.Log.LogInfo("Loaded " + _attempts.Count + " saved attempt(s) for '" + _loadedAnchor + "'");
            }

            _recorder.Arm(_practice.AnchorPosition, _practice.AnchorLabel);
            _deltaHint = 0;
            _hasDelta = false;
            SelectReference();
            _status = "armed - move to start";
        }

        /// Return to the anchor and arm a fresh attempt. This is the
        /// "another go" key.
        private void RestartRun()
        {
            if (_practice == null) { _status = "No practice module."; return; }
            _practice.ReturnToAnchor();   // raises OnPlacedAtAnchor
        }

        private void FinishRun()
        {
            Attempt done = _recorder.Finish();
            if (done == null) { _status = "No run in progress."; return; }

            _attempts.Add(done);
            _store.Save(done);

            Attempt best = RunCompare.Best(_attempts);
            bool isPb = ReferenceEquals(best, done);

            _status = "finished " + Format(done.Duration) + (isPb ? "   NEW BEST" : "");
            SelectReference();
        }

        private void AbortRun()
        {
            _recorder.Abort();
            _hasDelta = false;
            _status = "aborted";
        }

        private void SelectReference()
        {
            switch (_referenceKind)
            {
                case Reference.Best:
                    _reference = RunCompare.Best(_attempts);
                    break;

                case Reference.Last:
                    _reference = _attempts.Count > 0 ? _attempts[_attempts.Count - 1] : null;
                    break;

                case Reference.Average:
                    // There is no "average path", so the closest honest
                    // thing is the attempt nearest the mean duration.
                    _reference = NearestToAverage();
                    break;
            }
        }

        private Attempt NearestToAverage()
        {
            float avg = RunCompare.AverageDuration(_attempts);
            if (avg <= 0f) return null;

            Attempt best = null;
            float bestGap = float.MaxValue;

            for (int i = 0; i < _attempts.Count; i++)
            {
                if (!_attempts[i].Completed) continue;
                float gap = Mathf.Abs(_attempts[i].Duration - avg);
                if (gap >= bestGap) continue;
                bestGap = gap;
                best = _attempts[i];
            }
            return best;
        }

        // ------------------------------------------------------------------
        public override void Tick()
        {
            // Clear the renderer BEFORE the early return. Previously Tick
            // bailed out when practice mode was off, so UpdateLines never
            // ran and RunLineBehaviour kept drawing its last buffers -
            // the line stayed on screen until something re-toggled it.
            if (!Enabled) { ClearLines(); return; }
            if (!Ctx.Player.Found) return;

            Vector3 pos = Ctx.Player.Transform.position;
            _recorder.Tick(pos, Ctx.Player.HorizontalSpeed, Time.unscaledDeltaTime);

            if (_recorder.State == RunRecorder.RunState.Running && _reference != null)
            {
                _hasDelta = RunCompare.Delta(_reference.Samples, pos, _recorder.Elapsed,
                                             ref _deltaHint, out _delta);
            }
            else _hasDelta = false;

            UpdateLines();
        }

        private void ClearLines()
        {
            if (_lines == null) return;

            _lines.Show = false;
            _lines.ReferenceCount = 0;
            _lines.CurrentCount = 0;
            _lines.HasGhost = false;

            // Drop the cached source so re-enabling rebuilds rather than
            // reusing buffers that may belong to a cleared attempt list.
            _lineSource = null;
            _currentLineCount = 0;
        }

        // ------------------------------------------------------------------
        private void UpdateLines()
        {
            if (_lines == null) return;

            _lines.Show = _showLines && Enabled;
            if (!_lines.Show) return;

            // Reference path: rebuild only when the chosen attempt changes.
            if (!ReferenceEquals(_lineSource, _reference))
            {
                _lineSource = _reference;

                if (_reference == null)
                {
                    _lines.ReferenceCount = 0;
                }
                else
                {
                    _lines.ReferenceLine = ToPoints(_reference);
                    _lines.ReferenceCount = _lines.ReferenceLine.Length;
                }
            }

            // Live path: grown in place as samples arrive.
            Attempt current = _recorder.Current;
            if (current == null)
            {
                _lines.CurrentCount = 0;
                _currentLineCount = 0;
            }
            else if (current.Samples.Count != _currentLineCount)
            {
                _currentLineCount = current.Samples.Count;
                _lines.CurrentLine = ToPoints(current);
                _lines.CurrentCount = _lines.CurrentLine.Length;
            }

            // Ghost: where the reference was at this elapsed time.
            _lines.HasGhost = false;
            if (_reference != null && _recorder.State == RunRecorder.RunState.Running)
            {
                Vector3 ghost;
                if (SampleAtTime(_reference, _recorder.Elapsed, out ghost))
                {
                    _lines.GhostPosition = ghost;
                    _lines.HasGhost = true;
                }
            }
        }

        private static Vector3[] ToPoints(Attempt a)
        {
            Vector3[] pts = new Vector3[a.Samples.Count];
            for (int i = 0; i < pts.Length; i++) pts[i] = a.Samples[i].P;
            return pts;
        }

        /// Position of the reference at time t, linearly interpolated.
        /// Returns false once the reference has finished.
        private static bool SampleAtTime(Attempt a, float t, out Vector3 position)
        {
            position = Vector3.zero;
            if (a == null || a.Samples.Count == 0) return false;
            if (t > a.Duration) return false;

            for (int i = 1; i < a.Samples.Count; i++)
            {
                if (a.Samples[i].T < t) continue;

                float span = a.Samples[i].T - a.Samples[i - 1].T;
                float f = span <= 0f ? 0f : (t - a.Samples[i - 1].T) / span;
                position = Vector3.Lerp(a.Samples[i - 1].P, a.Samples[i].P, f);
                return true;
            }

            position = a.Samples[a.Samples.Count - 1].P;
            return true;
        }

        public override void ContributeHud(HudBuilder hud)
        {
            if (!Enabled) return;

            switch (_recorder.State)
            {
                case RunRecorder.RunState.Armed:
                    hud.Pair("Run", "armed - move to start");
                    break;

                case RunRecorder.RunState.Running:
                    hud.Pair("Run", Format(_recorder.Elapsed) +
                                    (_hasDelta ? "   " + SignedDelta(_delta) : ""));
                    break;

                default:
                    if (_attempts.Count > 0)
                    {
                        Attempt best = RunCompare.Best(_attempts);
                        hud.Pair("Run", _attempts.Count + " attempts" +
                                        (best != null ? "   best " + Format(best.Duration) : ""));
                    }
                    break;
            }
        }

        // ------------------------------------------------------------------
        public override void DrawPanel(int windowId)
        {
            if (!_windowPlaced)
            {
                _windowRect = new Rect(30f, 60f, 420f, 340f);
                _windowPlaced = true;
            }

            _windowRect = GUI.Window(windowId, _windowRect, DrawContents, _title);
        }

        private readonly GUIContent _title = new GUIContent("Practice runs");

        private void DrawContents(int id)
        {
            if (_rowStyle == null)
            {
                _rowStyle = new GUIStyle(GUI.skin.label);
                _rowStyle.alignment = TextAnchor.MiddleLeft;
            }

            float w = _windowRect.width;

            bool on = GUI.Toggle(new Rect(12, 26, 150, 20), Enabled, " Practice mode");
            if (on != Enabled) ToggleMode();

            GUI.Label(new Rect(168, 26, w - 180, 20),
                      "Anchor: " + (_practice != null && _practice.HasAnchor
                                        ? _practice.AnchorLabel : "none set (F3 panel)"));

            if (GUI.Button(new Rect(12, 50, 120, 24), "Restart run")) RestartRun();
            if (GUI.Button(new Rect(138, 50, 110, 24), "Finish")) FinishRun();
            if (GUI.Button(new Rect(254, 50, 90, 24), "Abort")) AbortRun();
            if (GUI.Button(new Rect(350, 50, w - 362, 24), "Clear"))
            {
                _attempts.Clear();
                SelectReference();
                ClearLines();
            }

            GUI.Label(new Rect(12, 80, 80, 20), "Compare to");
            Reference kind = _referenceKind;
            if (GUI.Toggle(new Rect(96, 80, 60, 20), kind == Reference.Best, " best")) kind = Reference.Best;
            if (GUI.Toggle(new Rect(160, 80, 60, 20), kind == Reference.Last, " last")) kind = Reference.Last;
            if (GUI.Toggle(new Rect(224, 80, 80, 20), kind == Reference.Average, " average")) kind = Reference.Average;
            if (kind != _referenceKind) { _referenceKind = kind; SelectReference(); }

            bool lines = GUI.Toggle(new Rect(320, 80, 110, 20), _showLines, " run lines");
            if (lines != _showLines) _showLines = lines;

            GUI.Label(new Rect(12, 104, w - 24, 20), _status);

            GUI.Label(new Rect(12, 126, w - 24, 18),
                      "attempt   time   max = top horizontal speed reached", _rowStyle);

            DrawAttemptList(new Rect(8, 146, w - 16, _windowRect.height - 156));

            GUI.DragWindow(new Rect(0, 0, w, 22));
        }

        private void DrawAttemptList(Rect listRect)
        {
            const float rowH = 20f;

            Rect content = new Rect(0, 0, listRect.width - 20f, _attempts.Count * rowH);
            _scroll = GUI.BeginScrollView(listRect, _scroll, content);

            Attempt best = RunCompare.Best(_attempts);

            for (int i = 0; i < _attempts.Count; i++)
            {
                Attempt a = _attempts[i];
                float y = i * rowH;
                if (y + rowH < _scroll.y || y > _scroll.y + listRect.height) continue;

                string row = "#" + (i + 1) + "   " + Format(a.Duration) +
                             "   max " + a.TopSpeed.ToString("F1") + " u/s" +
                             (ReferenceEquals(a, best) ? "   BEST" : "") +
                             (ReferenceEquals(a, _reference) ? "   [ref]" : "");

                GUI.Label(new Rect(4, y, content.width - 8, rowH), row, _rowStyle);
            }

            GUI.EndScrollView();
        }

        public override void Shutdown()
        {
            if (_lineHost != null) Object.Destroy(_lineHost);
        }

        // ------------------------------------------------------------------
        public static string Format(float seconds)
        {
            int m = (int)(seconds / 60f);
            float s = seconds - m * 60f;
            return m.ToString("00") + ":" + s.ToString("00.000");
        }

        private static string SignedDelta(float d)
        {
            return (d >= 0f ? "+" : "-") + Mathf.Abs(d).ToString("0.00");
        }
    }
}
