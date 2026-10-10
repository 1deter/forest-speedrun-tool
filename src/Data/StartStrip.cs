namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The Practice tab's start strip (Modules/PracticeModule, T-0256): one
    // line saying what the selected spot's Restart does, or how its run
    // stands. A timed segment starts from there in two clicks (select,
    // Restart) - a runner's Restart on one turns practice mode on
    // (author, 2026-10-10; decisions.md *Plugin: One journey, one place*).
    // ------------------------------------------------------------------
    public static class StartStrip
    {
        /// What the strip says for the selected spot. `name` null = nothing
        /// selected. `runCategory` non-empty with a start state = a run spot
        /// (its Restart starts a run mode run). `state` is the run's state
        /// for this spot (Idle when the armed run is another spot's).
        public static string Text(string name, bool hasSpawn, bool timed, string runCategory,
                                  bool hasStartState, bool practiceOn, RunRecorder.RunState state)
        {
            if (name == null) return "Select a spot in the list, then Restart.";
            string quoted = "'" + name + "'";
            if (!hasSpawn) return quoted + " has no spawn point.";
            // A run in progress first - a run spot's run mode run too.
            if (state == RunRecorder.RunState.Running) return quoted + ": running.";
            if (state == RunRecorder.RunState.Armed) return quoted + ": armed - the timer starts at its start.";
            if (!string.IsNullOrEmpty(runCategory) && hasStartState)
                return quoted + ": Restart starts a run (" + runCategory + ", Full load).";
            if (!timed) return quoted + " is a spot, not a timed segment.";
            return practiceOn
                ? quoted + ": idle - Restart to arm."
                : quoted + ": idle - Restart turns practice mode on and arms it.";
        }

        /// What F7 and the Runs tab's Restart act on (author, 2026-10-10):
        /// the selection when it changed since the last placement, else the
        /// current spot - a Go to a row, then F7, still restarts there.
        /// Ids, not objects: a Reload rebuilds the entries under the same ids.
        public static bool FollowsSelection(string selectedId, bool selectedHasSpawn, string selectedIdAtPlace)
        {
            return selectedId != null && selectedHasSpawn && selectedId != selectedIdAtPlace;
        }

        /// A runner's Restart turns practice mode on only for a timed
        /// segment that is not a run spot (run mode times those itself,
        /// Data/RunTiming), and only when it is off.
        public static bool TurnsPracticeOn(bool timed, bool runStart, bool practiceOn)
        {
            return timed && !runStart && !practiceOn;
        }
    }
}
