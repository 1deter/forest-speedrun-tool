using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    /// A split was taken: row (0-based), seconds on the timer, by hand (F12) or not.
    public delegate void SplitTakenHandler(int row, float seconds, bool byHand);

    // ------------------------------------------------------------------
    // The run timer (T-0467, REVIEW.md Top 10 item 7): the timer of record
    // once the tool replaces LiveSplit, in a Core module so a run file
    // without the practice tools still has it.
    //
    // It owns the timer's state machine (Data/RunTimer: arming, the clock,
    // the start crossing, splits in order, the rows the splits panel shows,
    // load-removed time), who armed it (Source: F9 or a run spot's run
    // start, Data/RunTiming), the arming (Arm), one frame of it (Step: the
    // inventory its item triggers read, the game events in order, the
    // autosplits, the clock), F12 and the abort key, and the timer's log
    // lines. It provides IRunTimer to run mode (ModuleContext.RunTimer).
    //
    // Who drives it: today Modules/PracticeRunModule - it decides when a
    // spot is armed, calls Step at the point of its frame where a level
    // left, run mode ended or the replay camera have been dealt with (so
    // RunTimerModule never ticks on its own: a level left would be
    // evaluated for a frame), and acts on the events below (status lines,
    // the attempt store, the results). The splits panel moves here next
    // (tasks/notes/T-0467.md step 5).
    // ------------------------------------------------------------------
    public sealed class RunTimerModule : OverlayModule, IRunTimer, ITimerEvents, ITimerSink
    {
        public override string Id { get { return "runtimer"; } }
        public override string DisplayName { get { return "Run timer"; } }
        public override ModuleKind Kind { get { return ModuleKind.Core; } }

        private readonly RunTimer _timer = new RunTimer();
        private LiveItemCounts _live;
        private System.Func<string[]> _channels;

        // How many of Ctx.Events have been read; Arm calls, so an arming
        // inside a step keeps its own count.
        private int _eventsSeen;
        private int _arms;
        private Vector3 _stepAt;

        /// The state machine (the driver reads its rows, splits and clock).
        public RunTimer Machine { get { return _timer; } }

        /// The live item counts the triggers read.
        public IItemCounts Live { get { return _live; } }

        /// Who armed the run: F9, or a run spot's run start in run mode,
        /// which times it whatever F9 says (Data/RunTiming).
        public ArmSource Source { get; set; }

        /// Practice mode (F9) is on: set by its owner, the practice runs.
        public bool PracticeOn { get; set; }

        /// The timer works: F9 on, or a run mode run while run mode is on.
        /// Gates the triggers, the HUD's Run line, splits and results.
        public bool Timing { get { return RunTiming.TimingOn(PracticeOn, Source, Ctx.Run.Active); } }

        // --- what the driver hears, in the order it happens ------------------------

        /// A game event is read (index into Ctx.Events, where the player
        /// is) - before the occurrence gate.
        public event System.Action<int, Vector3> EventRead;
        /// The clock started (true: by hand, F12).
        public event System.Action<bool> ClockStarted;
        /// A checkpoint split (after IRunTimer.Split and the log line).
        public event SplitTakenHandler SplitTaken;
        /// The end was reached with a checkpoint outstanding (logged).
        public event System.Action EndBlocked;
        /// The end was reached (or F12 with only the end left): the driver
        /// finishes the run (Machine.Finish) - before the next event is read.
        public event System.Action FinishDue;
        /// F12 with nothing armed.
        public event System.Action NothingArmed;
        /// The abort key: the running attempt is about to be dropped (what
        /// to keep of it: the driver's).
        public event System.Action<string> Dropping;
        /// The abort key: the run was dropped (the driver re-arms it).
        public event System.Action Aborted;

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);
            _live = new LiveItemCounts(ctx.Inventory);
            _channels = delegate { return Ctx.PlayerState.Channels; };
            // Load-removed time reads the game's load state (gotcha 16: say what it found).
            ctx.Log.LogInfo("Load-removed time: the game's load state from " + GameLoading.Resolve() + ".");
            ctx.RunTimer = this;   // run mode reads the timer through it (T-0466)
        }

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("run.manualSplit", KeyCode.F12, "Manual split / finish", ManualAdvance);
            map.Add("run.abort", KeyCode.LeftBracket, "Abort practice run", AbortRun);
        }

        // --- arming --------------------------------------------------------------------

        /// Arms `s` (Source set first): no clock yet, the start trigger
        /// primed, no splits, the item baseline read from a fresh inventory.
        /// Events already fired are not this run's. A running attempt is
        /// dropped - the driver keeps what it wants of it first. `anchor`:
        /// the attempt's first sample if the clock is started by hand.
        public void Arm(Segment s, Vector3 anchor, string route)
        {
            _arms++;
            _eventsSeen = Ctx.Events.Count;

            // The item ids its triggers and autosplits read: fresh before
            // the timer takes the baseline.
            _timer.Prepare(s);
            if (_timer.ItemIds.Count > 0)
            {
                Ctx.Inventory.Resolve();
                Ctx.Inventory.Refresh();
            }

            Ctx.PlayerState.Resolve();   // T-0397: names must exist before the copy
            _timer.Recorder.StateChannels = Ctx.PlayerState.Channels;
            _timer.Recorder.ChannelSource = _channels;
            _timer.Arm(s, anchor, _live, route);
        }

        /// Drops the run (armed or running), nothing said: the driver's
        /// own aborts, each with what it keeps first.
        public void Abort()
        {
            _timer.Abort();
        }

        /// The events fired until now are not the timer's to act on (a
        /// frame it is not stepped: nothing timed, the replay camera).
        public void SkipEvents()
        {
            _eventsSeen = Ctx.Events.Count;
        }

        // --- one frame -----------------------------------------------------------------

        /// One frame of the armed run at `position`: the inventory its item
        /// triggers read, the events since the last frame in order, the
        /// clock. The driver calls it once a frame while the run is timed.
        public void Step(Vector3 position)
        {
            // Item triggers read the live inventory, so it has to be fresh.
            RefreshTriggerInventory();
            Ctx.PlayerState.Resolve();

            _stepAt = position;
            int arms = _arms;
            int seen = _timer.Step(this, _eventsSeen, position, _live, Time.frameCount, this);
            if (arms == _arms) _eventsSeen = seen;

            // Load-removed time: the game's load state this frame (Game/GameLoading).
            // The first frame after a checkpoint resume adds nothing (Data/RunTimer).
            _timer.Tick(position, Ctx.Player.HorizontalSpeed, Time.unscaledDeltaTime, GameLoading.Now);
        }

        // Refresh() rebuilds and sorts the stack list - garbage every call, and
        // this ran every frame of a run with an item trigger (or an item
        // autosplit). With the item hooks in (Game/ItemCounter) it runs when an
        // item amount was written, or the inventory object is another one, and
        // on a quarter-second clock besides for a write the hooks missed.
        // Without the hooks it is every frame as before.
        private const float InventoryRefreshEvery = 0.25f;
        private int _invVersion = -1, _invGeneration = -1;
        private float _invAt = -10f;

        private void RefreshTriggerInventory()
        {
            if (_timer.ItemIds.Count == 0) return;
            Ctx.Inventory.Resolve();
            float now = Time.unscaledTime;
            if (ItemCounter.Watching && ItemCounter.Version == _invVersion && Ctx.Inventory.Generation == _invGeneration &&
                now >= _invAt && now - _invAt < InventoryRefreshEvery)
                return;
            _invVersion = ItemCounter.Version;
            _invGeneration = Ctx.Inventory.Generation;
            _invAt = now;
            Ctx.Inventory.Refresh();
        }

        int ITimerEvents.Count { get { return Ctx.Events.Count; } }
        string ITimerEvents.NameAt(int i) { return Ctx.Events.NameAt(i); }
        bool ITimerEvents.CompanionAt(int i) { return Ctx.Events.CompanionAt(i); }

        void ITimerSink.GameEvent(int index)
        {
            if (EventRead != null) EventRead(index, _stepAt);
        }

        /// The ASL splits once per update, whatever else also matched.
        void ITimerSink.AutoSplitting(string why)
        {
            Ctx.Log.LogInfo("Run '" + _timer.Segment.Id + "': autosplit on " + why + " at " + RunTimer.Format(_timer.Elapsed) + ".");
        }

        void ITimerSink.Stepped(TimerStep step)
        {
            switch (step)
            {
                case TimerStep.Started:
                    // Crossing, not entering: the spawn usually sits inside
                    // the start zone, so the run begins when you leave it.
                    if (ClockStarted != null) ClockStarted(false);
                    break;

                case TimerStep.Split:
                    Splitted(false);
                    break;

                case TimerStep.EndBlocked:
                    // Said, not silently ignored: a run that will not
                    // finish looks exactly like a broken end trigger.
                    Segment s = _timer.Segment;
                    Ctx.Log.LogInfo("Run '" + s.Id + "': end reached with checkpoint " + (_timer.Next + 1) +
                                    " (" + _timer.Heading.Describe() + ") outstanding" + ItemReading(_timer.Heading) + ".");
                    if (EndBlocked != null) EndBlocked();
                    break;

                case TimerStep.Finished:
                    if (FinishDue != null) FinishDue();
                    break;
            }
        }

        // What an item trigger reads now - the log line a "checkpoint never
        // fired" report needs.
        private string ItemReading(Trigger t)
        {
            if (t.Kind != TriggerKind.Item) return "";
            return "; holding " + _live.AmountOf(t.ItemId) + ", " + _timer.Baseline.AmountOf(t.ItemId) + " at the start";
        }

        /// The split just taken: run mode's Split, the log line, the driver.
        private void Splitted(bool byHand)
        {
            int row = _timer.Splits.Count - 1;
            float t = _timer.Elapsed;
            if (Split != null) Split(row, t);
            Segment s = _timer.Segment;
            if (byHand)
                Ctx.Log.LogInfo("Run '" + s.Id + "': checkpoint " + _timer.Splits.Count + " split by hand at " +
                                RunTimer.Format(t) + ".");
            else
                Ctx.Log.LogInfo("Run '" + s.Id + "': checkpoint " + _timer.Splits.Count + "/" +
                                s.Checkpoints.Count + " at " + RunTimer.Format(t) + ".");
            if (SplitTaken != null) SplitTaken(row, t, byHand);
        }

        // --- by hand -------------------------------------------------------------------

        /// Manual split (F12), or finish when all checkpoints are done; armed,
        /// it starts the clock. Lets a segment be driven by hand while its
        /// triggers are still being worked out.
        public void ManualAdvance()
        {
            bool armed = _timer.Armed;
            if (armed && !(Ctx.Player.Found && _timer.Segment != null)) return;
            Vector3 pos = armed ? Ctx.Player.Transform.position : Vector3.zero;
            switch (_timer.Advance(pos, _live))
            {
                case TimerStep.Started:
                    if (ClockStarted != null) ClockStarted(true);
                    return;

                case TimerStep.Split:
                    Splitted(true);
                    return;

                case TimerStep.Finished:
                    if (FinishDue != null) FinishDue();
                    return;

                default:
                    if (NothingArmed != null) NothingArmed();
                    return;
            }
        }

        /// The abort key: the run is thrown away without a time, then armed
        /// again by the driver.
        public void AbortRun()
        {
            if (Dropping != null) Dropping("aborted");
            _timer.Abort();
            if (Aborted != null) Aborted();
        }

        // --- IRunTimer -------------------------------------------------------------

        /// The running timer in ms, -1 when none (run mode's chain).
        public long TimerMs()
        {
            return _timer.TimerMs();
        }

        /// Every ms of load the timer has counted, ever (run mode's `load`
        /// lines take the difference over one load).
        public long TimedLoadMs()
        {
            return _timer.TimedLoadMs();
        }

        public event System.Action<int, float> Split;
        public event RunFinishedHandler Finished;

        /// A timed run finished (before its results show): what the results
        /// panel adds of it (null = nothing).
        public RunModeOutcome ReportFinished(Segment segment, float seconds)
        {
            return Finished != null ? Finished(segment, seconds) : null;
        }
    }
}
