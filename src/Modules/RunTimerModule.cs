using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // The run timer (T-0467, REVIEW.md Top 10 item 7): the timer of record
    // once the tool replaces LiveSplit, in a Core module so a run file
    // without the practice tools still has it.
    //
    // It owns the timer's state machine (Data/RunTimer: arming, the clock,
    // the start crossing, splits in order, the rows the splits panel shows,
    // load-removed time) and provides IRunTimer to run mode
    // (ModuleContext.RunTimer): the clock, the load clock, and the split and
    // finish events.
    //
    // Moving in steps (tasks/notes/T-0467.md): today PracticeRunModule
    // still drives the machine - it arms it at a spot, feeds it the game
    // each frame, and reports splits and finishes through ReportSplit /
    // ReportFinished; the splits panel is still drawn there. The arming,
    // the frame step and the panel move here next.
    // ------------------------------------------------------------------
    public sealed class RunTimerModule : OverlayModule, IRunTimer
    {
        public override string Id { get { return "runtimer"; } }
        public override string DisplayName { get { return "Run timer"; } }
        public override ModuleKind Kind { get { return ModuleKind.Core; } }

        private readonly RunTimer _timer = new RunTimer();

        /// The state machine (the practice runs drive it until the arming
        /// and the frame step move here).
        public RunTimer Machine { get { return _timer; } }

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);
            // Load-removed time reads the game's load state (gotcha 16: say what it found).
            ctx.Log.LogInfo("Load-removed time: the game's load state from " + GameLoading.Resolve() + ".");
            ctx.RunTimer = this;   // run mode reads the timer through it (T-0466)
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

        // --- what the driver reports -------------------------------------------------

        /// A split was taken: row, seconds on the timer.
        public void ReportSplit(int row, float seconds)
        {
            if (Split != null) Split(row, seconds);
        }

        /// A timed run finished (before its results show): what the results
        /// panel adds of it (null = nothing).
        public RunModeOutcome ReportFinished(Segment segment, float seconds)
        {
            return Finished != null ? Finished(segment, seconds) : null;
        }
    }
}
