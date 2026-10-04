using System.Collections.Generic;
using BepInEx.Configuration;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;
using UnityEngine.SceneManagement;

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
    public sealed partial class PracticeRunModule : OverlayModule
    {
        public enum Reference { Best, Last, Average, BestSegments, Runner, LiveSplit }

        public override string Id { get { return "practicerun"; } }
        public override string DisplayName { get { return "Practice runs"; } }
        public override bool HasTab { get { return true; } }
        public override string TabTitle { get { return "Runs"; } }
        public override int TabOrder { get { return 30; } }

        /// Off until asked for: otherwise every teleport arms a run whether
        /// or not one was wanted.
        public bool Enabled;

        private readonly RunRecorder _recorder = new RunRecorder();

        // Inventory counts, recorded as changes (the website's state panel).
        private ItemCounter _items;
        private readonly List<Attempt> _attempts = new List<Attempt>();
        private AttemptStore _store;
        private PracticeModule _practice;

        // --- the segment being run ----------------------------------------
        private Segment _segment;
        private string _loadedSegmentId;
        private int _armedRevision;

        // The level a run was armed in (build index: an int, no string per
        // tick). Quitting to the title screen kept the run's clock going and
        // its line drawn there (author, v0.24.7); a load restore reloads the
        // same level, so its index does not change.
        private int _armedScene = -1;
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

        // Auto-restart (runner maks, author 2026-09-23: one global setting,
        // off until ticked; it acts for load-mode start states too). The
        // time shows as a notice and the spot restarts this long after the
        // end fired - "a 0.4 s text of your time and then you're ready to go
        // again".
        private const float AutoRestartDelay = 0.4f;
        private ConfigEntry<bool> _autoRestart;
        // Kept across launches (runner maks, twice: practice mode and run
        // lines came back off; v0.24.191).
        private ConfigEntry<bool> _practiceModeCfg, _showLinesCfg;
        private ConfigEntry<string> _compareCfg;
        private float _autoRestartAt;

        // Game events: how many of Ctx.Events have been evaluated, and a
        // line for the tab saying the hooks are alive and what fired last.
        private int _eventsSeen;
        // Runs started on this segment, finished or not (AttemptStore
        // started.txt); never fewer than the finished ones on file.
        private int _started;

        // `event autosplit` (v0.24.186, a LiveSplit import): what in the
        // segment's autosplit list splits, with the ASL's per-run memory
        // (Data/AutoSplitWatch); one split per frame at most, as the ASL.
        private readonly AutoSplitWatch _auto = new AutoSplitWatch();
        private int _autoFrame = -1;
        private int _eventLineBuiltFor = -1;
        private string _eventLine = "";
        private float _tabW;
        private float _tabH;
        private Vector2 _scroll;
        private Vector2 _pageScroll;
        private float _pageH;
        private GUIStyle _rowStyle;

        // Run line rendering.
        private GameObject _lineHost;
        private RunLineBehaviour _lines;
        private bool _showLines = true;
        private Attempt _lineSource;
        // Appended to, not rebuilt - see Data/LineBuffer.
        private readonly LineBuffer _referenceLine = new LineBuffer();
        private readonly LineBuffer _currentLine = new LineBuffer();
        // The last run cut short (a restart, an abort, a death) - kept to
        // see where it went wrong (maks, sxczurass; v0.24.200).
        private readonly LineBuffer _failedLine = new LineBuffer();
        private int _ghostHint;

        // ------------------------------------------------------------------
        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);

            _store = new AttemptStore(ctx.Log, ctx.ConfigDirectory);
            _live = new LiveItemCounts(ctx.Inventory);

            _practice = Host.Find<PracticeModule>();
            if (_practice != null)
            {
                _practice.OnPlacedAtSpot = OnPlacedAtSpot;
                _practice.OnRestartStarting = OnRestartStarting;
            }
            else ctx.Log.LogWarning("PracticeRunModule: no PracticeModule found.");

            _lineHost = new GameObject("ForestOverlay_RunLines");
            _lineHost.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(_lineHost);
            _lines = _lineHost.AddComponent<RunLineBehaviour>();

            // Pulled only when a state sample is due (5 Hz), not read every
            // frame and thrown away.
            _recorder.StateSource = ReadState;
            _items = new ItemCounter(ctx.Log, ctx.Inventory);
            ItemCounter.Install(ctx.Log, OverlayPlugin.PluginGuid);
            _recorder.ItemSource = _items.Fill;

            // Finished runs to the website (Modules/RunUploadModule).
            _upload = Host.Find<RunUploadModule>();
            _runMode = Host.Find<RunModeModule>();
            if (_runMode != null) _runMode.TimerMs = TimerMsNow;
            if (_upload != null)
            {
                _upload.CurrentSegment = SegmentForUpload;
                _upload.SavedRunTexts = SavedRunsForUpload;
                _upload.RunnerIdNow = RunnerIdNow;
                _upload.RunnerNameNow = RunnerNameNow;
            }

            _autoRestart = ctx.Config.Bind("Runs", "AutoRestartAtEnd", false,
                "Restart the spot (as F7 does, start state included) as soon as a timed run finishes. " +
                "The time shows on screen for a moment.");

            _practiceModeCfg = ctx.Config.Bind("Runs", "PracticeMode", false,
                "Practice mode (F9) on: teleporting to a timed segment arms a run. Kept across launches.");
            _showLinesCfg = ctx.Config.Bind("Runs", "RunLines", true, "Draw the run lines (yours and the comparison's).");
            _compareCfg = ctx.Config.Bind("Runs", "CompareTo", "Best",
                "The comparison picked in the Runs tab: Best, Last, Average or BestSegments.");
            Enabled = _practiceModeCfg.Value;
            if (Enabled) _status = "on - go to a timed segment in Practice";
            _showLines = _showLinesCfg.Value;
            try
            {
                Reference saved = (Reference)System.Enum.Parse(typeof(Reference), _compareCfg.Value, true);
                if (saved == Reference.Best || saved == Reference.Last || saved == Reference.Average || saved == Reference.BestSegments)
                    _referenceKind = saved;
            }
            catch (System.Exception) { }

            InitSplits(ctx);
            InitLineOptions(ctx);
        }

        private RunUploadModule _upload;
        private RunModeModule _runMode;

        /// The running timer in ms, -1 when none (run mode's chain).
        private long TimerMsNow()
        {
            return _recorder.State == RunRecorder.RunState.Running ? (long)System.Math.Round(_recorder.Elapsed * 1000.0) : -1;
        }

        /// The spot the runner is on: the armed segment, else the Practice
        /// tab's current spot (practice mode off arms nothing).
        private Segment SegmentForUpload()
        {
            if (_segment != null) return _segment;
            return _practice != null ? _practice.CurrentSegment : null;
        }

        /// The runner's own saved runs only - an imported .foseg's runs are
        /// other runners' and theirs to upload.
        private List<string> SavedRunsForUpload()
        {
            Segment s = SegmentForUpload();
            if (s == null) return null;
            List<string> texts = _store.RunTexts(s.Id);
            if (texts == null) return null;
            string own = RunnerIdNow();
            List<string> mine = new List<string>(texts.Count);
            for (int i = 0; i < texts.Count; i++)
                if (AttemptOwners.IsOwn(AttemptOwners.RunnerIdOf(texts[i]), own)) mine.Add(texts[i]);
            return mine;
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
            map.Add("run.cycleComparison", KeyCode.None, "Splits: next comparison", CycleComparison);
            map.Add("tab.runs", KeyCode.None, "Open Runs tab", OpenMyTab);
        }

        public override void Shutdown()
        {
            if (_lineHost != null) Object.Destroy(_lineHost);
            ItemCounter.Uninstall();
        }

        // ------------------------------------------------------------------
        private void ToggleMode()
        {
            Enabled = !Enabled;
            if (_practiceModeCfg != null && _practiceModeCfg.Value != Enabled) _practiceModeCfg.Value = Enabled;

            if (!Enabled)
            {
                RecordUnfinished("practice mode off");
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

            // Before _segment / _splits move on to the new entry.
            RecordUnfinished("placed at a spot");
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

            RecordUnfinished("re-armed");   // before its splits are cleared
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
            _armedScene = SceneManager.GetActiveScene().buildIndex;
            _armedRoute = _segment.RouteFingerprint();

            _recorder.StateChannels = Ctx.PlayerState.Channels;
            _recorder.Route = _armedRoute;
            KeepFailed();   // a Go / restart without a start state re-arms mid-run
            _recorder.Arm(_segment.HasSpawn ? _segment.SpawnPosition : PlayerPosition(), _segment.Id);

            MaybeFetchBoard();
            _lssDirty = true;   // re-read the linked file if it changed
            SelectReference();
            ArmSplits();
            _status = "armed: " + _segment.Name;
        }

        private void CollectReferencedItemIds(Segment s)
        {
            _referencedItemIds.Clear();

            AddItemId(s.Start);
            AddItemId(s.End);
            for (int i = 0; i < s.Checkpoints.Count; i++) AddItemId(s.Checkpoints[i]);

            // Autosplit items are read every frame like item triggers.
            _auto.Configure(s.AutoSplit);
            for (int i = 0; i < _auto.ItemIds.Count; i++)
                if (!_referencedItemIds.Contains(_auto.ItemIds[i])) _referencedItemIds.Add(_auto.ItemIds[i]);
        }

        private void AddItemId(Trigger t)
        {
            if (t.Kind != TriggerKind.Item) return;
            if (_referencedItemIds.Contains(t.ItemId)) return;
            _referencedItemIds.Add(t.ItemId);
        }

        /// "23 attempts (9 finished)", or "9 attempts" when every one finished.
        private string AttemptsText()
        {
            int started = Mathf.Max(_started, _attempts.Count);
            return started + " attempts" + (started > _attempts.Count ? " (" + _attempts.Count + " finished)" : "");
        }

        private Vector3 PlayerPosition()
        {
            return Ctx.Player.Found ? Ctx.Player.Transform.position : Vector3.zero;
        }

        // ------------------------------------------------------------------
        public override void Tick()
        {
            BuildEventLine();
            FlushLineOptions();
            RefreshTabText();
            RefreshLss();
            RefreshSplits();

            if (_autoRestartAt > 0f && Time.unscaledTime >= _autoRestartAt)
            {
                _autoRestartAt = 0f;
                // Only if nothing changed meanwhile: still on, still this
                // segment, no new run started by hand.
                if (Enabled && _segment != null && _practice != null &&
                    ReferenceEquals(_practice.CurrentSegment, _segment) &&
                    _recorder.State != RunRecorder.RunState.Running)
                {
                    Ctx.Log.LogInfo("Run '" + _segment.Id + "': auto-restart.");
                    _practice.ReturnToSpot();
                }
            }

            if (_segment != null && SceneManager.GetActiveScene().buildIndex != _armedScene) LeaveLevel();

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
            while (_eventsSeen < events)
            {
                string fired = Ctx.Events.NameAt(_eventsSeen++);
                EvaluateTriggers(pos, fired);
                if (_auto.Any && _recorder.State == RunRecorder.RunState.Running && _auto.OnEvent(fired))
                    AutoSplit(pos, fired);
            }
            if (_auto.ItemIds.Count > 0 && _recorder.State == RunRecorder.RunState.Running && _auto.OnItems(_live))
                AutoSplit(pos, "an item");

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
                        RecordSplit(_splits.Count - 1, _recorder.Elapsed);
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

        /// The ASL splits once per update, whatever else also matched.
        private void AutoSplit(Vector3 pos, string why)
        {
            if (Time.frameCount == _autoFrame) return;
            _autoFrame = Time.frameCount;
            Ctx.Log.LogInfo("Run '" + _segment.Id + "': autosplit on " + why + " at " + Format(_recorder.Elapsed) + ".");
            EvaluateTriggers(pos, Segment.AutoSplitEvent);
        }

        private void StartClock(Vector3 pos)
        {
            _auto.Begin(_live);   // what is held now is the ASL's baseline
            if (_segment != null)
            {
                _started = Mathf.Max(_started, _attempts.Count) + 1;
                _store.SetStarted(_segment.Id, _started);
            }
            _recorder.ForceStart(pos);
            _sequence.Begin(_segment.Checkpoints, _segment.End);
            ResetSplits();   // the last run's times stay up until now
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
                RecordSplit(_splits.Count - 1, _recorder.Elapsed);
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

            StampAttempt(done);
            _attempts.Add(done);
            _playedFinished += done.Duration;
            _store.Save(done);
            if (_upload != null && _segment != null)
                _upload.Enqueue(_segment, AttemptFormat.Write(done), done.RunnerId, done.RunnerName);
            FinishSplits(done.Duration);
            if (_runMode != null) _runMode.TimerFinished(_segment, done.Duration);

            Attempt best = RunCompare.Best(_attempts);
            bool isPb = ReferenceEquals(best, done);

            _status = "finished " + Format(done.Duration) + (isPb ? "   NEW BEST" : "");
            SelectReference();
            ClearRunPreview();
            Ctx.Log.LogInfo("Run '" + done.AnchorLabel + "': finished in " + Format(done.Duration) + (isPb ? " (best)" : "") +
                            " - " + done.Splits.Length + " split time(s) saved, runner '" + done.RunnerName + "' (" + done.RunnerId + ").");

            if (_autoRestart.Value && _segment != null)
            {
                Ctx.Notice.Show(Format(done.Duration) + (isPb ? "   NEW BEST" : ""), 1.2f);
                _autoRestartAt = Time.unscaledTime + AutoRestartDelay;
            }
        }

        /// A running attempt about to be dropped: its line stays as the
        /// failed one (Runs -> Line options).
        private void KeepFailed()
        {
            if (_recorder.State != RunRecorder.RunState.Running || _recorder.Current == null) return;
            if (_recorder.Current.Samples.Count < 2) return;
            _failedLine.Clear();
            _failedLine.Sync(_recorder.Current.Samples);
        }

        private void AbortRun()
        {
            _autoRestartAt = 0f;
            KeepFailed();
            RecordUnfinished("aborted");
            _recorder.Abort();
            _hasDelta = false;
            ClearRunPreview();
            _status = "aborted";

            if (_segment != null) ArmRun();
        }

        // Left the level (title screen): the run is not finished and not
        // saved, and nothing of it is drawn. Going back to the spot (F7,
        // Go) arms it again.
        // A restart voids the run at once; OnPlacedAtSpot arms it again
        // once the spot is ready.
        private void OnRestartStarting()
        {
            _autoRestartAt = 0f;
            if (_recorder.State == RunRecorder.RunState.Running && _segment != null)
                Ctx.Log.LogInfo("Run '" + _segment.Id + "': aborted - restarting the spot.");
            KeepFailed();
            RecordUnfinished("restart");
            _recorder.Abort();
            _hasDelta = false;
            ClearRunPreview();
            _splitsDirty = true;
        }

        // Recorded as an abort ([): an unfinished attempt (PB chance,
        // playtime), its line kept as the failed one - and said on screen,
        // since the runner did not ask for it (runner request: "runs
        // continue at the main menu").
        private void LeaveLevel()
        {
            bool running = _recorder.State == RunRecorder.RunState.Running;
            string where = PlayerRef.AtTitleScreen ? "the game went back to the title screen" : "the level unloaded";
            Ctx.Log.LogInfo("Run '" + _segment.Id + "': " + (running ? "aborted" : "disarmed") +
                            " - left the level, " + where + " (not saved).");
            _autoRestartAt = 0f;
            KeepFailed();
            RecordUnfinished("left the level");
            _recorder.Abort();
            _hasDelta = false;
            _segment = null;
            ClearLines();
            ClearRunPreview();
            _status = (running ? "run aborted - " + where : "left the level") + " - go to the spot again to run it";
            if (running) Ctx.Notice.Show("Run aborted: " + where + ". Kept as an unfinished attempt.", 6f);
        }

        private void LoadAttemptsFor(Segment s)
        {
            string route = s.RouteFingerprint();
            if (s.Id == _loadedSegmentId && route == _armedRoute) return;

            _loadedSegmentId = s.Id;
            _attempts.Clear();
            _failedLine.Clear();   // another segment's failure is not this one's
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
            // Other runners' attempts (an imported .foseg) compare, never
            // count (Data/AttemptOwners, v0.24.190).
            string own = RunnerIdNow();
            List<Attempt> others = new List<Attempt>();

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

                if (AttemptOwners.IsOwn(all[i].RunnerId, own)) _attempts.Add(all[i]);
                else others.Add(all[i]);
            }
            SetLocalOthers(others, s.Checkpoints.Count);
            _started = Mathf.Max(_store.Started(s.Id), _attempts.Count);
            LoadPlaytime(s.Id, all, own);

            Ctx.Log.LogInfo("Loaded " + _attempts.Count + " attempt(s) for " + s.Id +
                            (others.Count > 0 ? ", " + others.Count + " by other runners (" + _localOthers.Count + " runner(s), comparisons only)" : "") +
                            (_otherRouteCount > 0 ? " (" + _otherRouteCount + " from another route)" : ""));
        }

        private void SelectReference()
        {
            _rowsDirty = true;
            switch (_referenceKind)
            {
                case Reference.Best:
                case Reference.BestSegments:   // no single attempt: race the PB's line
                    _reference = RunCompare.Best(_attempts);
                    break;
                case Reference.Last:
                    _reference = _attempts.Count > 0 ? _attempts[_attempts.Count - 1] : null;
                    break;
                case Reference.Average:
                    _reference = NearestToAverage();
                    break;
                case Reference.Runner:   // another runner's PB from the website
                    _reference = RunnerReference();
                    break;
                case Reference.LiveSplit:   // times only: race your own PB's line
                    _reference = RunCompare.Best(_attempts);
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
        // While running the Practice module draws the run's zones per its
        // Zones setting - by default only the NEXT objective: every zone at
        // once is a field of overlapping spheres with no indication of
        // where to actually go.
        private void UpdateRunPreview()
        {
            if (_practice == null) return;

            if (_recorder.State != RunRecorder.RunState.Running || _segment == null)
            {
                ClearRunPreview();
                return;
            }

            _practice.SetRunPreview(_segment, _sequence.Next);
        }

        private void ClearRunPreview()
        {
            if (_practice != null) _practice.ClearRunPreview();
        }

        // ------------------------------------------------------------------
        private void UpdateLines()
        {
            if (_lines == null) return;

            // Lines belong to the selected entry only when it is the one
            // being run (author, 2026-09-24): selecting another entry hid
            // its zones but left this segment's line drawn.
            _lines.Show = _showLines && Enabled &&
                          (_practice == null || ReferenceEquals(_practice.SelectedSegment, _segment));
            if (!_lines.Show) return;

            if (!ReferenceEquals(_lineSource, _reference))
            {
                _lineSource = _reference;
                _ghostHint = 0;
                _referenceLine.Clear();
                if (_reference != null) _referenceLine.Sync(_reference.Samples);
            }
            _lines.ReferenceLine = _referenceLine.Points;
            _lines.ReferenceStart = 0;
            _lines.ReferenceCount = _referenceLine.Count;
            _lines.Opacity = LineOpacity;
            if (_lineAheadOn.Value)
            {
                // Only the next few seconds of the comparison (author, QA
                // 2026-09-26): from where its ghost is now, or its start.
                float from = _recorder.State == RunRecorder.RunState.Running ? _recorder.Elapsed : 0f;
                int s, e;
                _referenceLine.Window(from, from + LineAhead, out s, out e);
                _lines.ReferenceStart = s;
                _lines.ReferenceCount = e;
            }

            Attempt current = _recorder.Current;
            if (current == null) _currentLine.Clear();
            else _currentLine.Sync(current.Samples);
            _lines.CurrentLine = _currentLine.Points;
            _lines.CurrentCount = _currentLine.Count;
            _lines.FailedLine = _failedLine.Points;
            _lines.FailedCount = _keepFailedCfg.Value ? _failedLine.Count : 0;

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
            _lines.FailedCount = 0;
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
                // The previous time stays in view while the next one runs
                // (runners: the HUD showed only the current one).
                if (_attempts.Count > 0) hud.Pair("Last", Format(_attempts[_attempts.Count - 1].Duration));
            }
            else
            {
                Attempt best = RunCompare.Best(_attempts);
                hud.Pair("Run", AttemptsText() + (best != null ? "   best " + Format(best.Duration) : ""));
                if (_attempts.Count > 0) hud.Pair("Last", Format(_attempts[_attempts.Count - 1].Duration));
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
            if (GUI.Toggle(new Rect(296, 58, 120, 20), kind == Reference.BestSegments, " best segments")) kind = Reference.BestSegments;
            if (kind != _referenceKind)
            {
                _referenceKind = kind;
                _compareCfg.Value = kind.ToString();   // one write per click
                SelectReference();
                _splitsDirty = true;
            }

            bool lines = GUI.Toggle(new Rect(0, 82, 110, 20), _showLines, " run lines");
            if (lines != _showLines) { _showLines = lines; _showLinesCfg.Value = lines; }

            bool auto = GUI.Toggle(new Rect(114, 82, w - 114 - 130, 20), _autoRestart.Value,
                                   " Auto-restart when a run finishes");
            if (auto != _autoRestart.Value) _autoRestart.Value = auto;
            if (GUI.Button(new Rect(w - 124, 81, 124, 22), _lineOptionsOpen ? "Line options  ^" : "Line options  v"))
                _lineOptionsOpen = !_lineOptionsOpen;

            // Flowing, each line as tall as its text (UiText) - these
            // messages vary in length and clipped at fixed heights. The rest
            // of the tab scrolls: with the splits table and its options open
            // it outgrows the window (v0.24.147).
            const float top = 106f;
            float viewH = _tabH - top - 4f;
            bool scrolls = _pageH > viewH;
            float cw = scrolls ? w - 20f : w;
            _pageScroll = GUI.BeginScrollView(new Rect(0, top, w, viewH), _pageScroll, new Rect(0, 0, cw, Mathf.Max(_pageH, viewH)));

            float y = _runMode != null ? _runMode.DrawSection(0f, cw) + 4f : 0f;
            y = DrawLineOptions(y, cw);
            y = DrawRunnersSection(y, cw);
            y = DrawLiveSplitSection(y, cw);
            y += UiText.Draw(0, y, cw, _statusText);
            y += UiText.Draw(0, y, cw, _diagnoseText);
            y += UiText.Draw(0, y, cw, _eventText);
            if (_upload != null) y = _upload.DrawSection(y + 4f, cw);
            y = DrawSplitsSection(y + 4f, cw);
            y += UiText.Draw(0, y, cw, _whenSetText);

            // The attempts keep their own scrolling list: the room left, or
            // at least 160 px below everything else.
            y += 4f;
            float listH = Mathf.Max(160f, viewH - y - 4f);
            DrawAttemptList(new Rect(0, y, cw, listH));
            _pageH = y + listH + 4f;
            GUI.EndScrollView();
        }

        // Tab text, rebuilt from Tick a few times a second - never in
        // DrawTab, which runs several times a frame (module rules).
        private const float TabTextInterval = 0.25f;
        private float _nextTabText;
        private readonly GUIContent _segmentText = new GUIContent("");
        private readonly GUIContent _statusText = new GUIContent("");
        private readonly GUIContent _diagnoseText = new GUIContent("");
        private readonly GUIContent _eventText = new GUIContent("");
        private readonly GUIContent _emptyListText = new GUIContent("");
        // When the PB and each gold were set (runner request; the .run
        // files have always carried `recorded|`).
        private readonly GUIContent _whenSetText = new GUIContent("");
        private System.DateTime _rowsDay;
        private readonly List<GUIContent> _attemptRows = new List<GUIContent>();
        private bool _rowsDirty = true;

        private void RefreshTabText()
        {
            if (_rowsDirty) RebuildAttemptRows();
            if (_upload != null) _upload.RefreshText();
            // At once: it answers a click.
            if (!ReferenceEquals(_statusText.text, _status)) _statusText.text = _status;
            if (Time.unscaledTime < _nextTabText) return;
            _nextTabText = Time.unscaledTime + TabTextInterval;

            _segmentText.text = _segment != null ? "Segment: " + _segment.Name
                                                 : "Pick a timed segment in the Practice tab";
            _diagnoseText.text = Diagnose();
            _eventText.text = _eventLine;
            // "today" / "yesterday" move on at midnight.
            if (TabShowing && _attempts.Count > 0 && System.DateTime.Now.Date != _rowsDay) _rowsDirty = true;

        }

        private void RebuildAttemptRows()
        {
            _rowsDirty = false;
            Attempt best = RunCompare.Best(_attempts);
            System.DateTime now = System.DateTime.Now;
            _rowsDay = now.Date;

            for (int i = 0; i < _attempts.Count; i++)
            {
                Attempt a = _attempts[i];
                string when = RunDates.WhenUtc(a.RecordedUtc, now);
                string row = "#" + (i + 1) + "   " + Format(a.Duration) +
                             (when.Length > 0 ? "   " + when : "") +
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

            _whenSetText.text = WhenSetText(now);
        }

        /// "Personal best 1:23.45, set today 14:32. Golds: Cave 5 30.10
        /// (3 Oct 14:32); End 12.00 (yesterday 20:01)." - "" with no
        /// finished attempt.
        private string WhenSetText(System.DateTime now)
        {
            if (_segment == null || _segment.Id != _loadedSegmentId || _attempts.Count == 0) return "";
            int cps = _segment.Checkpoints.Count;
            SplitStats st = SplitStats.Build(_attempts, cps);
            if (st.Completed == 0) return "";

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("Personal best ").Append(SplitTable.Time(st.Pb, 3));
            string pbWhen = RunDates.WhenUtc(st.PbSetUtc, now);
            if (pbWhen.Length > 0) sb.Append(", set ").Append(pbWhen);
            sb.Append('.');

            // Golds only mean something with more than one row.
            if (st.Rows > 1)
            {
                bool any = false;
                for (int r = 0; r < st.Rows; r++)
                {
                    if (float.IsNaN(st.BestSegments[r])) continue;
                    sb.Append(any ? "; " : "  Golds: ");
                    any = true;
                    sb.Append(_segment.SplitName(r)).Append(' ').Append(SplitTable.Time(st.BestSegments[r], 3));
                    string w = RunDates.WhenUtc(st.BestSegmentSetUtc[r], now);
                    if (w.Length > 0) sb.Append(" (").Append(w).Append(')');
                }
                if (any) sb.Append('.');
            }
            return sb.ToString();
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
            ResetSplits();
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
