using System.Collections.Generic;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Timed practice runs, driven by a segment's triggers.
    //
    // Teleporting to a timed segment ARMS a run. The clock starts when the
    // segment's start trigger fires - usually walking out of the start
    // zone - so lining up costs nothing. Checkpoints split, the end
    // trigger finishes, and the attempt is compared against the best for
    // that segment.
    //
    // Attempts are keyed on the SEGMENT ID, not on a position or a name,
    // because that is what makes two people's runs of the same route
    // comparable.
    //
    // A segment with no triggers is just a teleport; this module ignores
    // it rather than inventing a run around it.
    // ------------------------------------------------------------------
    public sealed class PracticeRunModule : OverlayModule
    {
        public enum Reference { Best, Last, Average }

        public override string Id { get { return "practicerun"; } }
        public override string DisplayName { get { return "Practice runs"; } }
        public override bool HasTab { get { return true; } }
        public override string TabTitle { get { return "Runs"; } }
        public override int TabOrder { get { return 30; } }

        /// Off until asked for: otherwise every teleport arms a run whether
        /// or not one was wanted.
        public bool Enabled;

        private readonly RunRecorder _recorder = new RunRecorder();
        private readonly List<Attempt> _attempts = new List<Attempt>();
        private AttemptStore _store;
        private PracticeModule _practice;

        // --- the segment being run ----------------------------------------
        private Segment _segment;
        private string _loadedSegmentId;

        private TriggerState _startState;
        private TriggerState _endState;
        private TriggerState[] _checkStates = new TriggerState[0];
        private int _nextCheckpoint;

        private LiveItemCounts _live;
        private readonly ItemSnapshot _baseline = new ItemSnapshot();
        private readonly List<int> _referencedItemIds = new List<int>();
        private readonly List<float> _splits = new List<float>();

        // --- comparison ----------------------------------------------------
        private Attempt _reference;
        private Reference _referenceKind = Reference.Best;
        private int _deltaHint;
        private float _delta;
        private bool _hasDelta;

        private string _status = "";
        private float _tabW;
        private float _tabH;
        private Vector2 _scroll;
        private GUIStyle _rowStyle;

        // Run line rendering.
        private GameObject _lineHost;
        private RunLineBehaviour _lines;
        private bool _showLines = true;
        private Attempt _lineSource;
        private int _currentLineCount;

        // ------------------------------------------------------------------
        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);

            _store = new AttemptStore(ctx.Log, ctx.ConfigDirectory);
            _live = new LiveItemCounts(ctx.Inventory);

            _practice = Host.Find<PracticeModule>();
            if (_practice != null) _practice.OnPlacedAtSpot = OnPlacedAtSpot;
            else ctx.Log.LogWarning("PracticeRunModule: no PracticeModule found.");

            _lineHost = new GameObject("ForestOverlay_RunLines");
            _lineHost.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(_lineHost);
            _lines = _lineHost.AddComponent<RunLineBehaviour>();
        }

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("run.toggleMode", KeyCode.F9, "Practice mode on / off", ToggleMode);
            map.Add("run.manualSplit", KeyCode.F12, "Manual split / finish", ManualAdvance);
            map.Add("run.abort", KeyCode.LeftBracket, "Abort practice run", AbortRun);
            map.Add("tab.runs", KeyCode.None, "Open Runs tab", OpenMyTab);
        }

        public override void Shutdown()
        {
            if (_lineHost != null) Object.Destroy(_lineHost);
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
                ClearRunPreview();
                _status = "practice mode off";
                return;
            }

            _status = "on - go to a timed segment in Practice";
            OnPlacedAtSpot();
        }

        /// Called when the player is placed at the current entry.
        private void OnPlacedAtSpot()
        {
            if (!Enabled || _practice == null) return;

            Segment s = _practice.CurrentSegment;

            if (s == null || !s.IsTimed)
            {
                // A plain spot is a teleport, not a run.
                _segment = null;
                _recorder.Abort();
                ClearRunPreview();
                _status = s == null
                    ? "no entry selected"
                    : "'" + s.Name + "' is a spot, not a timed segment";
                return;
            }

            _segment = s;
            LoadAttemptsFor(s);
            ArmRun();
        }

        private void ArmRun()
        {
            // Priming (rather than firing) on the first evaluation is what
            // stops the start trigger going off while you are still standing
            // in the start zone after teleporting in.
            TriggerEvaluator.Reset(ref _startState);
            TriggerEvaluator.Reset(ref _endState);

            if (_checkStates.Length != _segment.Checkpoints.Count)
                _checkStates = new TriggerState[_segment.Checkpoints.Count];

            for (int i = 0; i < _checkStates.Length; i++)
                TriggerEvaluator.Reset(ref _checkStates[i]);

            _nextCheckpoint = 0;
            _splits.Clear();
            _deltaHint = 0;
            _hasDelta = false;

            CollectReferencedItemIds(_segment);
            if (_referencedItemIds.Count > 0)
            {
                Ctx.Inventory.Resolve();
                Ctx.Inventory.Refresh();
            }
            _baseline.Capture(_live, _referencedItemIds);

            _recorder.StateChannels = Ctx.PlayerState.Channels;
            _recorder.Arm(_segment.HasSpawn ? _segment.SpawnPosition : PlayerPosition(), _segment.Id);

            SelectReference();
            _status = "armed: " + _segment.Name;
        }

        private void CollectReferencedItemIds(Segment s)
        {
            _referencedItemIds.Clear();

            AddItemId(s.Start);
            AddItemId(s.End);
            for (int i = 0; i < s.Checkpoints.Count; i++) AddItemId(s.Checkpoints[i]);
        }

        private void AddItemId(Trigger t)
        {
            if (t.Kind != TriggerKind.Item) return;
            if (_referencedItemIds.Contains(t.ItemId)) return;
            _referencedItemIds.Add(t.ItemId);
        }

        private Vector3 PlayerPosition()
        {
            return Ctx.Player.Found ? Ctx.Player.Transform.position : Vector3.zero;
        }

        // ------------------------------------------------------------------
        public override void Tick()
        {
            if (!Enabled) { ClearLines(); return; }
            if (!Ctx.Player.Found || _segment == null) return;

            Vector3 pos = Ctx.Player.Transform.position;

            // Item triggers read the live inventory, so it has to be fresh.
            if (_referencedItemIds.Count > 0)
            {
                Ctx.Inventory.Resolve();
                Ctx.Inventory.Refresh();
            }

            Ctx.PlayerState.Resolve();

            if (_recorder.State == RunRecorder.RunState.Armed)
            {
                if (TriggerEvaluator.Fired(_segment.Start, ref _startState, pos, _live, null, _baseline))
                {
                    _recorder.ForceStart(pos);
                    _status = "running";
                }
            }
            else if (_recorder.State == RunRecorder.RunState.Running)
            {
                EvaluateCheckpoints(pos);

                if (TriggerEvaluator.Fired(_segment.End, ref _endState, pos, _live, null, _baseline))
                    FinishRun();
            }

            _recorder.Tick(pos, Ctx.Player.HorizontalSpeed, Time.unscaledDeltaTime,
                           Ctx.PlayerState.Read());

            if (_recorder.State == RunRecorder.RunState.Running && _reference != null)
            {
                _hasDelta = RunCompare.Delta(_reference.Samples, pos, _recorder.Elapsed,
                                             ref _deltaHint, out _delta);
            }
            else _hasDelta = false;

            UpdateLines();
            UpdateRunPreview();
        }

        private void EvaluateCheckpoints(Vector3 pos)
        {
            // Checkpoints fire IN ORDER. Letting a later one fire early
            // would let a route that happens to pass near it skip a split
            // and silently produce an incomparable run.
            if (_nextCheckpoint >= _segment.Checkpoints.Count) return;

            if (!TriggerEvaluator.Fired(_segment.Checkpoints[_nextCheckpoint],
                                        ref _checkStates[_nextCheckpoint], pos, _live, null, _baseline))
                return;

            _splits.Add(_recorder.Elapsed);
            _nextCheckpoint++;

            _status = "split " + _splits.Count + "/" + _segment.Checkpoints.Count +
                      "  " + Format(_recorder.Elapsed);
        }

        /// Manual split, or finish when all checkpoints are done. Lets a
        /// segment be driven by hand while its triggers are still being
        /// worked out.
        private void ManualAdvance()
        {
            if (_recorder.State == RunRecorder.RunState.Armed)
            {
                if (Ctx.Player.Found)
                {
                    _recorder.ForceStart(Ctx.Player.Transform.position);
                    _status = "running (manual start)";
                }
                return;
            }

            if (_recorder.State != RunRecorder.RunState.Running) { _status = "no run armed"; return; }

            if (_segment != null && _nextCheckpoint < _segment.Checkpoints.Count)
            {
                _splits.Add(_recorder.Elapsed);
                _nextCheckpoint++;
                _status = "split " + _splits.Count + " (manual)";
                return;
            }

            FinishRun();
        }

        private void FinishRun()
        {
            Attempt done = _recorder.Finish();
            if (done == null) { _status = "no run in progress"; return; }

            _attempts.Add(done);
            _store.Save(done);

            Attempt best = RunCompare.Best(_attempts);
            bool isPb = ReferenceEquals(best, done);

            _status = "finished " + Format(done.Duration) + (isPb ? "   NEW BEST" : "");
            SelectReference();
            ClearRunPreview();
        }

        private void AbortRun()
        {
            _recorder.Abort();
            _hasDelta = false;
            ClearRunPreview();
            _status = "aborted";

            if (_segment != null) ArmRun();
        }

        private void LoadAttemptsFor(Segment s)
        {
            if (s.Id == _loadedSegmentId) return;

            _loadedSegmentId = s.Id;
            _attempts.Clear();
            _attempts.AddRange(_store.LoadAll(s.Id));
            _lineSource = null;

            Ctx.Log.LogInfo("Loaded " + _attempts.Count + " attempt(s) for " + s.Id);
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
        // Only the NEXT objective is shown while running. Drawing every
        // zone at once turns a route into a field of overlapping spheres
        // with no indication of where to actually go.
        private void UpdateRunPreview()
        {
            if (_practice == null) return;

            if (_recorder.State != RunRecorder.RunState.Running || _segment == null)
            {
                ClearRunPreview();
                return;
            }

            bool isEnd = _nextCheckpoint >= _segment.Checkpoints.Count;
            Trigger next = isEnd ? _segment.End : _segment.Checkpoints[_nextCheckpoint];

            _practice.SetRunPreview(next, isEnd ? 2 : 1);
        }

        private void ClearRunPreview()
        {
            if (_practice != null) _practice.ClearRunPreview();
        }

        // ------------------------------------------------------------------
        private void UpdateLines()
        {
            if (_lines == null) return;

            _lines.Show = _showLines && Enabled;
            if (!_lines.Show) return;

            if (!ReferenceEquals(_lineSource, _reference))
            {
                _lineSource = _reference;

                if (_reference == null) _lines.ReferenceCount = 0;
                else
                {
                    _lines.ReferenceLine = ToPoints(_reference);
                    _lines.ReferenceCount = _lines.ReferenceLine.Length;
                }
            }

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

        private void ClearLines()
        {
            if (_lines == null) return;

            _lines.Show = false;
            _lines.ReferenceCount = 0;
            _lines.CurrentCount = 0;
            _lines.HasGhost = false;
            _lineSource = null;
            _currentLineCount = 0;
        }

        private static Vector3[] ToPoints(Attempt a)
        {
            Vector3[] pts = new Vector3[a.Samples.Count];
            for (int i = 0; i < pts.Length; i++) pts[i] = a.Samples[i].P;
            return pts;
        }

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

        // ------------------------------------------------------------------
        public override void ContributeHud(HudBuilder hud)
        {
            if (!Enabled) return;

            if (_segment == null)
            {
                hud.Pair("Run", "no timed segment selected");
                return;
            }

            if (_recorder.State == RunRecorder.RunState.Armed)
            {
                hud.Pair("Run", "armed - " + _segment.Name);
            }
            else if (_recorder.State == RunRecorder.RunState.Running)
            {
                hud.Pair("Run", Format(_recorder.Elapsed) +
                                (_hasDelta ? "   " + SignedDelta(_delta) : ""));

                hud.Pair("Next", _nextCheckpoint < _segment.Checkpoints.Count
                    ? "checkpoint " + (_nextCheckpoint + 1) + "/" + _segment.Checkpoints.Count
                    : "finish");
            }
            else
            {
                Attempt best = RunCompare.Best(_attempts);
                hud.Pair("Run", _attempts.Count + " attempts" +
                                (best != null ? "   best " + Format(best.Duration) : ""));
            }
        }

        // ------------------------------------------------------------------
        public override void DrawTab(Rect area)
        {
            _tabW = area.width;
            _tabH = area.height;

            if (_rowStyle == null)
            {
                _rowStyle = new GUIStyle(GUI.skin.label);
                _rowStyle.alignment = TextAnchor.MiddleLeft;
            }

            float w = _tabW;

            bool on = GUI.Toggle(new Rect(0, 2, 140, 20), Enabled, " Practice mode");
            if (on != Enabled) ToggleMode();

            GUI.Label(new Rect(150, 2, w - 160, 20),
                      _segment != null ? "Segment: " + _segment.Name
                                       : "Pick a timed segment in the Practice tab");

            if (GUI.Button(new Rect(0, 28, 120, 24), "Restart")) Restart();
            if (GUI.Button(new Rect(126, 28, 120, 24), "Split / finish")) ManualAdvance();
            if (GUI.Button(new Rect(252, 28, 90, 24), "Abort")) AbortRun();
            if (GUI.Button(new Rect(w - 100, 28, 100, 24), "Clear times")) ClearTimes();

            GUI.Label(new Rect(0, 58, 80, 20), "Compare to");
            Reference kind = _referenceKind;
            if (GUI.Toggle(new Rect(84, 58, 60, 20), kind == Reference.Best, " best")) kind = Reference.Best;
            if (GUI.Toggle(new Rect(148, 58, 60, 20), kind == Reference.Last, " last")) kind = Reference.Last;
            if (GUI.Toggle(new Rect(212, 58, 80, 20), kind == Reference.Average, " average")) kind = Reference.Average;
            if (kind != _referenceKind) { _referenceKind = kind; SelectReference(); }

            bool lines = GUI.Toggle(new Rect(300, 58, 110, 20), _showLines, " run lines");
            if (lines != _showLines) _showLines = lines;

            GUI.Label(new Rect(0, 82, w, 20), _status);

            if (_splits.Count > 0)
            {
                string line = "splits:";
                for (int i = 0; i < _splits.Count; i++) line += "  " + Format(_splits[i]);
                GUI.Label(new Rect(0, 102, w, 20), line);
            }

            DrawAttemptList(new Rect(0, 126, w, _tabH - 130));
        }

        private void Restart()
        {
            if (_practice == null) { _status = "no practice module"; return; }
            _practice.ReturnToSpot();
        }

        private void ClearTimes()
        {
            _attempts.Clear();
            SelectReference();
            ClearLines();
            _status = "times cleared from view (files kept)";
        }

        private void DrawAttemptList(Rect listRect)
        {
            const float rowH = 20f;

            Rect content = new Rect(0, 0, listRect.width - 20f, _attempts.Count * rowH + 4f);
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

            if (_attempts.Count == 0)
            {
                GUI.Label(new Rect(listRect.x + 4, listRect.y + 4, listRect.width - 8, 40),
                          "No attempts yet for this segment.", _rowStyle);
            }
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
