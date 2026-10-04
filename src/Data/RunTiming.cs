namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Who times a timed spot (Modules/PracticeRunModule).
    //
    // Practice mode (F9) arms a run on every placement at a timed spot. A
    // run spot's Restart in run mode is a run (docs/run-mode.md): it arms
    // the spot's timer whatever F9 says - a new runner who never pressed
    // F9 still gets split times in the attempt log, the splits and the
    // results panel. That arming belongs to run mode: it never writes the
    // runner's PracticeMode setting, never turns on what F9 gates besides
    // timing (run lines, auto-restart, the practice revive), and ends with
    // run mode - back to the runner's own F9 state.
    // ------------------------------------------------------------------
    public enum ArmSource
    {
        None,
        Practice,   // F9 on
        RunMode     // a run spot's run start
    }

    public static class RunTiming
    {
        /// Who arms a placement at a spot: a run spot's run start is run
        /// mode's, whatever F9 says; otherwise F9.
        public static ArmSource Source(bool practiceOn, bool runStart)
        {
            if (runStart) return ArmSource.RunMode;
            return practiceOn ? ArmSource.Practice : ArmSource.None;
        }

        /// The timer works (triggers, HUD, splits, results): F9 on, or a
        /// run mode run still in run mode.
        public static bool TimingOn(bool practiceOn, ArmSource source, bool runModeActive)
        {
            return practiceOn || (source == ArmSource.RunMode && runModeActive);
        }

        /// F9 pressed: does it touch the armed run? Not a run mode run while
        /// run mode is on - F9 off would abort the run, F9 on re-arm it.
        public static bool ToggleTouchesRun(ArmSource source, bool runModeActive)
        {
            return !(source == ArmSource.RunMode && runModeActive);
        }

        /// Run mode has ended under a run mode run: dropped unless F9 is on
        /// (then it is the runner's practice from here).
        public static bool DropOnRunModeEnd(bool practiceOn, ArmSource source)
        {
            return source == ArmSource.RunMode && !practiceOn;
        }
    }
}
