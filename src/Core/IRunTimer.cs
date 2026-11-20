using ForestOverlay.Data;

namespace ForestOverlay.Core
{
    /// A timed run finished: the handler answers with what the results
    /// panel shows of it (null = nothing to add).
    public delegate RunModeOutcome RunFinishedHandler(Segment segment, float seconds);

    /// The run timer as run mode sees it (T-0466): the timer and the load
    /// clock it reads, and the split and finish events it reacts to. The
    /// Runs module provides it (ModuleContext.RunTimer); run mode never
    /// names that module.
    public interface IRunTimer
    {
        /// The running timer in ms, -1 when none.
        long TimerMs();

        /// Every ms of load the timer ever counted: a `load` line's timer ms
        /// is its growth over the load.
        long TimedLoadMs();

        /// A split was taken: row, seconds on the timer.
        event System.Action<int, float> Split;

        /// A timed run finished (before its results show).
        event RunFinishedHandler Finished;
    }
}
