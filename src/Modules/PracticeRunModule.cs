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
        private int _armedRevision;
        private string _armedRoute = "";
        private int _otherRouteCount;

        private TriggerState _startState;
        // Checkpoints in order, then the end - see Data/SplitSequence.
        private readonly SplitSequence _sequence = new SplitSequence();

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

        // Game events: how many of Ctx.Events have been evaluated, and a
        // line for the tab saying the hooks are alive and what fired last.
        private int _eventsSeen;
        private int _eventLineBuiltFor = -1;
        private string _eventLine = "";
        private float _tabW;
        private float _tabH;
        private Vector2 _scroll;
        private GUIStyle _rowStyle;

        // Run line rendering.
        private GameObject _lineHost;
        private RunLineBehaviour _lines;
        private bool _showLines = true;
        private Attempt _lineSource;
        // Appended to, not rebuilt - see Data/LineBuffer.
        private readonly LineBuffer _referenceLine = new LineBuffer();
        private readonly LineBuffer _currentLine = new LineBuffer();
        private int _ghostHint;

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

            // Pulled only when a state sample is due (5 Hz), not read every
            // frame and thrown away.
            _recorder.StateSource = ReadState;
        }

        private float[] ReadState()
        {
            return Ctx.PlayerState.Read();
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

            _splits.Clear();
            _deltaHint = 0;
            _eventsSeen = Ctx.Events.Count;
            _hasDelta = false;

            CollectReferencedItemIds(_segment);
            if (_referencedItemIds.Count > 0)
            {
                Ctx.Inventory.Resolve();
                Ctx.Inventory.Refresh();
            }
            _baseline.Capture(_live, _referencedItemIds);

            _armedRevision = _segment.Revision;
            _armedRoute = _segment.RouteFingerprint();

            _recorder.StateChannels = Ctx.PlayerState.Channels;
            _recorder.Route = _armedRoute;
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
            BuildEventLine();
            RefreshTabText();

            // Events that arrive while nothing is armed are not ours to
            // act on later.
            if (!Enabled) { ClearLines(); _eventsSeen = Ctx.Events.Count; return; }

            // No segment = a plain spot: the last segment's lines went with
            // it (runner report, v0.22.6: they stayed until practice mode was
            // toggled).
            if (_segment == null) ClearLines();
            if (!Ctx.Player.Found || _segment == null) { _eventsSeen = Ctx.Events.Count; return; }

            // The segment can be edited while armed. Re-arm against the
            // new geometry rather than keeping a run pointed at a zone
            // that has moved.
            if (_segment.Revision != _armedRevision &&
                _recorder.State != RunRecorder.RunState.Running)
            {
                LoadAttemptsFor(_segment);
                ArmRun();
                _status = "segment edited - re-armed";
            }

            Vector3 pos = Ctx.Player.Transform.position;

            // Item triggers read the live inventory, so it has to be fresh.
            if (_referencedItemIds.Count > 0)
            {
                Ctx.Inventory.Resolve();
                Ctx.Inventory.Refresh();
            }

            Ctx.PlayerState.Resolve();

            // Once with no event, or once per event that fired since the
            // last frame - so two cutscenes starting in one frame both
            // count. Events are instants: satisfied only in the pass that
            // carries them, which gives the rising edge by itself.
            int events = Ctx.Events.Count;
            if (_eventsSeen > events) _eventsSeen = events;

            if (_eventsSeen == events) EvaluateTriggers(pos, null);
            while (_eventsSeen < events) EvaluateTriggers(pos, Ctx.Events.NameAt(_eventsSeen++));

            _recorder.Tick(pos, Ctx.Player.HorizontalSpeed, Time.unscaledDeltaTime, null);

            if (_recorder.State == RunRecorder.RunState.Running && _reference != null)
            {
                _hasDelta = RunCompare.Delta(_reference.Samples, pos, _recorder.Elapsed,
                                             ref _deltaHint, out _delta);
            }
            else _hasDelta = false;

            UpdateLines();
            UpdateRunPreview();
        }

        private void EvaluateTriggers(Vector3 pos, string firedEvent)
        {
            if (_recorder.State == RunRecorder.RunState.Armed)
            {
                // Crossing, not entering: the spawn usually sits inside
                // the start zone, so the run begins when you leave it.
                if (TriggerEvaluator.Crossed(_segment.Start, ref _startState, pos, _live, firedEvent, _baseline))
                {
                    StartClock(pos);
                    _status = "running";
                }
            }
            else if (_recorder.State == RunRecorder.RunState.Running)
            {
                switch (_sequence.Evaluate(pos, _live, firedEvent, _baseline))
                {
                    case SplitEvent.Split:
                        _splits.Add(_recorder.Elapsed);
                        _status = "split " + _splits.Count + "/" + _segment.Checkpoints.Count +
                                  "  " + Format(_recorder.Elapsed);
                        Ctx.Log.LogInfo("Run '" + _segment.Id + "': checkpoint " + _splits.Count + "/" +
                                        _segment.Checkpoints.Count + " at " + Format(_recorder.Elapsed) + ".");
                        break;

                    case SplitEvent.EndBlocked:
                        // Said, not silently ignored: a run that will not
                        // finish looks exactly like a broken end trigger.
                        _status = "end reached, but checkpoint " + (_sequence.Next + 1) + "/" + _segment.Checkpoints.Count +
                                  " (" + _sequence.Current.Describe() + ") was not - the run goes on. F12 skips it.";
                        Ctx.Log.LogInfo("Run '" + _segment.Id + "': end reached with checkpoint " + (_sequence.Next + 1) +
                                        " (" + _sequence.Current.Describe() + ") outstanding" + ItemReading(_sequence.Current) + ".");
                        break;

                    case SplitEvent.Finished:
                        FinishRun();
                        break;
                }
            }
        }

        private void StartClock(Vector3 pos)
        {
            _recorder.ForceStart(pos);
            _sequence.Begin(_segment.Checkpoints, _segment.End);
        }

        // What an item trigger reads now - the log line a "checkpoint never
        // fired" report needs.
        private string ItemReading(Trigger t)
        {
            if (t.Kind != TriggerKind.Item) return "";
            return "; holding " + _live.AmountOf(t.ItemId) + ", " + _baseline.AmountOf(t.ItemId) + " at the start";
        }

        private void BuildEventLine()
        {
            int n = Ctx.Events.Count;
            if (n == _eventLineBuiltFor) return;
            _eventLineBuiltFor = n;

            string last = n == 0 ? null : Ctx.Events.NameAt(n - 1);
            _eventLine = "Game events: " + Ctx.Events.Status +
                         (n == 0 ? ", none fired yet"
                                 : ", last '" + last + "' at " + Ctx.Events.StampAt(n - 1)) +
                         // Which keypad: the vault, automatic door and red
                         // elevator all share one action.
                         (last != null && (last.StartsWith(GameEvents.KeycardDoor) || last == GameEvents.RedElevator) && GameEvents.LastDoor != null
                             ? " - " + GameEvents.LastDoor : "");
        }

        /// Manual split, or finish when all checkpoints are done. Lets a
        /// segment be driven by hand while its triggers are still being
        /// worked out.
        private void ManualAdvance()
        {
            if (_recorder.State == RunRecorder.RunState.Armed)
            {
                if (Ctx.Player.Found && _segment != null)
                {
                    StartClock(Ctx.Player.Transform.position);
                    _status = "running (manual start)";
                }
                return;
            }

            if (_recorder.State != RunRecorder.RunState.Running) { _status = "no run armed"; return; }

            if (_segment != null && !_sequence.OnlyEndLeft)
            {
                _splits.Add(_recorder.Elapsed);
                _sequence.SkipCheckpoint();
                _status = "split " + _splits.Count + " (manual)";
                Ctx.Log.LogInfo("Run '" + _segment.Id + "': checkpoint " + _splits.Count + " split by hand at " +
                                Format(_recorder.Elapsed) + ".");
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
            string route = s.RouteFingerprint();
            if (s.Id == _loadedSegmentId && route == _armedRoute) return;

            _loadedSegmentId = s.Id;
            _attempts.Clear();
            _rowsDirty = true;
            // Cleared here, not left to UpdateLines: a segment with no
            // attempts has no reference either, and null == null never
            // told UpdateLines the old segment's line had to go.
            _lineSource = null;
            _referenceLine.Clear();
            _otherRouteCount = 0;

            // Attempts are keyed on the segment id so they can be
            // compared between players - but moving a start zone changes
            // what the times mean while leaving the id alone. Times from
            // a different route are kept on disk and left out of the
            // comparison rather than silently racing the new one.
            List<Attempt> all = _store.LoadAll(s.Id);

            for (int i = 0; i < all.Count; i++)
            {
                // An empty route means the attempt predates route
                // tracking; treat it as belonging to the current one
                // rather than throwing away someone’s history.
                if (all[i].Route.Length > 0 && all[i].Route != route)
                {
                    _otherRouteCount++;
                    continue;
                }

                _attempts.Add(all[i]);
            }

            Ctx.Log.LogInfo("Loaded " + _attempts.Count + " attempt(s) for " + s.Id +
                            (_otherRouteCount > 0 ? " (" + _otherRouteCount + " from another route)" : ""));
        }

        private void SelectReference()
        {
            _rowsDirty = true;
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

            _practice.SetRunPreview(_sequence.Current, _sequence.OnlyEndLeft ? 2 : 1);
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
                _ghostHint = 0;
                _referenceLine.Clear();
                if (_reference != null) _referenceLine.Sync(_reference.Samples);
            }
            _lines.ReferenceLine = _referenceLine.Points;
            _lines.ReferenceCount = _referenceLine.Count;

            Attempt current = _recorder.Current;
            if (current == null) _currentLine.Clear();
            else _currentLine.Sync(current.Samples);
            _lines.CurrentLine = _currentLine.Points;
            _lines.CurrentCount = _currentLine.Count;

            _lines.HasGhost = false;
            if (_reference != null && _recorder.State == RunRecorder.RunState.Running)
            {
                Vector3 ghost;
                if (RunCompare.PositionAt(_reference.Samples, _reference.Duration, _recorder.Elapsed,
                                          ref _ghostHint, out ghost))
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
            _referenceLine.Clear();
            _currentLine.Clear();
            _ghostHint = 0;
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

                hud.Pair("Next", !_sequence.OnlyEndLeft
                    ? "checkpoint " + (_sequence.Next + 1) + "/" + _segment.Checkpoints.Count
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

            GUI.Label(new Rect(150, 2, w - 160, 20), _segmentText);

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

            // Flowing, each line as tall as its text (UiText) - these
            // messages vary in length and clipped at fixed heights.
            float y = 82f;
            y += UiText.Draw(0, y, w, _statusText);
            y += UiText.Draw(0, y, w, _diagnoseText);
            y += UiText.Draw(0, y, w, _splitsText);
            y += UiText.Draw(0, y, w, _eventText);

            y = Mathf.Max(y + 4f, 166f);
            DrawAttemptList(new Rect(0, y, w, _tabH - y - 4f));
        }

        // Tab text, rebuilt from Tick a few times a second - never in
        // DrawTab, which runs several times a frame (module rules).
        private const float TabTextInterval = 0.25f;
        private float _nextTabText;
        private readonly GUIContent _segmentText = new GUIContent("");
        private readonly GUIContent _statusText = new GUIContent("");
        private readonly GUIContent _diagnoseText = new GUIContent("");
        private readonly GUIContent _splitsText = new GUIContent("");
        private readonly GUIContent _eventText = new GUIContent("");
        private readonly GUIContent _emptyListText = new GUIContent("");
        private readonly List<GUIContent> _attemptRows = new List<GUIContent>();
        private bool _rowsDirty = true;
        private int _splitsShown = -1;

        private void RefreshTabText()
        {
            if (_rowsDirty) RebuildAttemptRows();
            // At once: it answers a click.
            if (!ReferenceEquals(_statusText.text, _status)) _statusText.text = _status;
            if (Time.unscaledTime < _nextTabText) return;
            _nextTabText = Time.unscaledTime + TabTextInterval;

            _segmentText.text = _segment != null ? "Segment: " + _segment.Name
                                                 : "Pick a timed segment in the Practice tab";
            _diagnoseText.text = Diagnose();
            _eventText.text = _eventLine;

            if (_splitsShown != _splits.Count)
            {
                _splitsShown = _splits.Count;
                if (_splits.Count == 0) _splitsText.text = "";
                else
                {
                    System.Text.StringBuilder sb = new System.Text.StringBuilder("splits:");
                    for (int i = 0; i < _splits.Count; i++) sb.Append("  ").Append(Format(_splits[i]));
                    _splitsText.text = sb.ToString();
                }
            }
        }

        private void RebuildAttemptRows()
        {
            _rowsDirty = false;
            Attempt best = RunCompare.Best(_attempts);

            for (int i = 0; i < _attempts.Count; i++)
            {
                Attempt a = _attempts[i];
                string row = "#" + (i + 1) + "   " + Format(a.Duration) +
                             "   max " + a.TopSpeed.ToString("F1") + " u/s" +
                             (ReferenceEquals(a, best) ? "   BEST" : "") +
                             (ReferenceEquals(a, _reference) ? "   [ref]" : "");
                if (i < _attemptRows.Count) _attemptRows[i].text = row;
                else _attemptRows.Add(new GUIContent(row));
            }

            if (_attempts.Count == 0)
                _emptyListText.text = _otherRouteCount > 0
                    ? "No attempts on this route yet. " + _otherRouteCount +
                      " saved time(s) belong to an earlier version of it."
                    : "No attempts yet for this segment.";
            else
                _emptyListText.text = _otherRouteCount > 0
                    ? _otherRouteCount + " older time(s) hidden - recorded before this route changed"
                    : "";
        }

        /// Says WHY a run is not progressing. A silent "nothing
        /// happens" is the hardest thing to report and the hardest to
        /// debug, so the state is on screen.
        private string Diagnose()
        {
            if (!Enabled) return "practice mode is off";
            if (!Ctx.Player.Found) return "player not found";
            if (_segment == null) return "no timed segment - Go to one in the Practice tab";

            if (_recorder.State == RunRecorder.RunState.Armed)
            {
                if (_segment.Start.Kind == TriggerKind.Event)
                    return "armed - waiting for game event '" + _segment.Start.EventName + "'";

                Vector3 p = Ctx.Player.Transform.position;
                bool inside = TriggerEvaluator.IsSatisfied(_segment.Start, p, _live, null, _baseline);

                return "armed - start is " + _segment.Start.Describe() +
                       (inside ? ", you are INSIDE it (leave to start)"
                               : ", you are outside it (enter to start)");
            }

            if (_recorder.State == RunRecorder.RunState.Running)
                return _sequence.OnlyEndLeft
                    ? "running - end is " + _segment.End.Describe()
                    : "running - next is checkpoint " + (_sequence.Next + 1) + "/" + _segment.Checkpoints.Count +
                      ", " + _sequence.Current.Describe() + " (the end waits for it)";

            return "idle - Restart to arm";
        }

        private void Restart()
        {
            if (_practice == null) { _status = "no practice module"; return; }
            _practice.ReturnToSpot();
        }

        private void ClearTimes()
        {
            _attempts.Clear();
            _rowsDirty = true;
            SelectReference();
            ClearLines();
            _status = "times cleared from view (files kept)";
        }

        private void DrawAttemptList(Rect listRect)
        {
            const float rowH = 20f;

            Rect content = new Rect(0, 0, listRect.width - 20f, _attempts.Count * rowH + 4f);
            _scroll = GUI.BeginScrollView(listRect, _scroll, content);

            int rows = Mathf.Min(_attempts.Count, _attemptRows.Count);
            for (int i = 0; i < rows; i++)
            {
                float y = i * rowH;
                if (y + rowH < _scroll.y || y > _scroll.y + listRect.height) continue;
                GUI.Label(new Rect(4, y, content.width - 8, rowH), _attemptRows[i], _rowStyle);
            }

            GUI.EndScrollView();

            if (_attempts.Count == 0)
                UiText.Draw(listRect.x + 4, listRect.y + 4, listRect.width - 8, _emptyListText);
            else if (_emptyListText.text.Length > 0)
                UiText.Draw(listRect.x + 4, listRect.yMax - 20f, listRect.width - 8, _emptyListText);
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
