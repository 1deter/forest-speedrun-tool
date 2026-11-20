using ForestOverlay.Data;

namespace ForestOverlay.Core
{
    /// What a run is compared against (the Runs tab's "Compare to"). The
    /// first four are the runner's own runs (the timer's, Modules/
    /// RunTimerModule); another runner's and a LiveSplit file's come from
    /// the practice tools (ITimerCompanion). The cycle key steps the first
    /// four in this order.
    public enum RunComparison { Best, Last, Average, BestSegments, Runner, LiveSplit }

    /// What the run timer's splits panel takes from the practice tools
    /// (T-0467): the comparisons they bring, the replay being watched, the
    /// results panel, checkpoint practice's golds. Modules/PracticeRunModule implements it and sets itself as
    /// RunTimerModule.Companion; without one (a run file) the timer
    /// compares against the runner's own runs and draws its panel itself.
    public interface ITimerCompanion
    {
        /// Another runner's or a LiveSplit comparison's split times
        /// (`kind` Runner or LiveSplit); `pb` when it has none to give now.
        float[] OtherSplits(RunComparison kind, float[] pb);

        /// Its name ("Personal best" when it has none).
        string OtherName(RunComparison kind);

        /// The comparison key from `kind` (Runner, LiveSplit or
        /// BestSegments): picks the companion's next comparison and says
        /// true, or false when it has none left (the timer moves on).
        bool NextOther(RunComparison kind);

        /// The comparison changed under the timer (the cycle key): the
        /// attempt the lines, the ghost and the delta race follows.
        void ComparisonChanged();

        /// Its tab is in view (the cycle key's notice is not needed).
        bool TabShowing { get; }

        /// The comparison run being watched (the replay camera), null when
        /// none: the panel shows its splits as its clock reaches them.
        Attempt Watched { get; }

        /// The watched run's clock, seconds.
        float WatchClock { get; }

        /// The results panel is up: the splits panel steps aside.
        bool ResultsShowing { get; }

        /// Golds from checkpoint practice, merged into freshly built stats.
        void ApplyGolds(SplitStats stats);

        /// The splits were reset (the next run started, another segment or
        /// route): the results panel closes.
        void SplitsReset();
    }
}
