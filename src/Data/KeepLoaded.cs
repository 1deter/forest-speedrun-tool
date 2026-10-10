namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Keep loaded (T-0212): when a restart may skip its start state.
    //
    // WHY (author, 2026-10-08 / 10-10): lab skip practice needs the endgame
    // loaded, which the start state gives, but restoring it on every
    // restart is not needed once it is. A segment's `keep = loaded` asks
    // for one real restore, then cheap restarts (teleport + the player and
    // the endgame movers put back) - "and the savestate must still reload
    // when the world it depends on changed since". Changed = any scene
    // loaded or unloaded since the restore settled (the author's pick over
    // "the areas line differs"), another restore / load, or a death.
    //
    // WHAT: the module reports each step with the game's counters
    // (SceneCache.SceneEvents, SavestateBridge.Restores); Check says null
    // (cheap) or why the start state is restored again. Pure - tested.
    // ------------------------------------------------------------------
    public sealed class KeepLoaded
    {
        private enum Phase { Off, Restoring, Armed }

        private Phase _phase;
        private string _segment = "";
        private int _sceneEvents;
        private int _restores;
        private string _dropped = "";

        /// The segment the world is kept for ("" = none).
        public string Segment { get { return _phase == Phase.Armed ? _segment : ""; } }

        /// A real restore of `segmentId`'s start state is starting.
        public void Restoring(string segmentId)
        {
            _phase = Phase.Restoring;
            _segment = segmentId ?? "";
            _dropped = "";
        }

        /// The restore settled (areas checked). `matches`: the world is the
        /// one the start state was captured in. The counters are taken as
        /// the baseline the next restarts compare with.
        public void Settled(string segmentId, bool matches, int sceneEvents, int restores)
        {
            if (_phase != Phase.Restoring || _segment != (segmentId ?? "")) return;
            if (!matches) { Drop("the areas after the restore differ from the start state's"); return; }
            _phase = Phase.Armed;
            _sceneEvents = sceneEvents;
            _restores = restores;
        }

        /// Forget the kept world (a death, the toggle off, a failed restore).
        public void Drop(string why)
        {
            if (_phase == Phase.Off) return;
            _phase = Phase.Off;
            _dropped = why ?? "";
        }

        /// Null = this restart may be cheap; otherwise why the start state
        /// is restored (for the restart's log line).
        public string Check(string segmentId, bool keepOn, bool runStart, int sceneEvents, int restores)
        {
            if (!keepOn) return "keep loaded is off";
            if (runStart) return "a run start always loads";
            if (_phase == Phase.Restoring) return "the last restore has not settled yet";
            if (_phase == Phase.Off) return _dropped.Length > 0 ? _dropped : "the first restart loads it";
            if (_segment != (segmentId ?? "")) return "the world is kept for another spot";
            if (restores != _restores) return "another restore or load ran since";
            if (sceneEvents != _sceneEvents) return "a scene was loaded or unloaded since (" + (sceneEvents - _sceneEvents) + ")";
            // A cheap restart does not move the baseline: a scene its own
            // teleport streamed in makes the next restart a real one.
            return null;
        }
    }
}
