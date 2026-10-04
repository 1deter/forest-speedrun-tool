namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Load-removed time (LRT) beside the real-time timer (runner sxczurass +
    // the author, QA #general 2026-10-04). The real-time timer is left
    // exactly as it is; LRT is that timer minus the part of it the game
    // spent loading.
    //
    // What counts as a load (docs/run-mode.md *Load-removed time*): the
    // game's own load state - a save / level load under way
    // (LevelSerializer.LevelLoadingOperation set, the scene loading behind
    // the game's loading screen) or the game not finished loading
    // (Scene.FinishGameLoad false: the activation sequence, the field the
    // LiveSplit autosplitter reads as "loaded"). Read by Game/GameLoading.
    // Cave streaming, the cave door fades and the endgame's streamed scenes
    // are not loads: the game shows no loading screen for them and the
    // flags stay put.
    //
    // LoadClock is fed every frame the run's timer advances, with the
    // seconds that frame added to the timer and whether the game was
    // loading: LRT never subtracts time the timer did not count. LoadSpan
    // follows the same state in real time for run mode's attempt log (a
    // `load` line per load, Data/AttemptChain).
    //
    // Pure: linked into the tests and the site.
    // ------------------------------------------------------------------
    public sealed class LoadClock
    {
        /// Loads that began while the timer ran.
        public int Loads { get; private set; }

        /// Seconds of the timer that were loads.
        public float LoadTime { get; private set; }

        /// A load is under way (as of the last Tick).
        public bool InLoad { get; private set; }

        /// Every second of load this clock ever counted, across runs (never
        /// reset): run mode's attempt log takes the difference over one
        /// load for its `load` line's timer ms.
        public double Ever { get; private set; }

        public void Reset()
        {
            Loads = 0;
            LoadTime = 0f;
            InLoad = false;
        }

        /// One frame of the run's timer: `dt` = the seconds it just added,
        /// `loading` = the game was loading.
        public void Tick(float dt, bool loading)
        {
            if (loading)
            {
                if (!InLoad) Loads++;
                InLoad = true;
                if (dt > 0f) { LoadTime += dt; Ever += dt; }
            }
            else InLoad = false;
        }

        /// The timer's `elapsed` with the loads taken out.
        public float Lrt(float elapsed)
        {
            return Without(elapsed, LoadTime);
        }

        /// `time` minus `loads`, never below zero; NaN in, NaN out.
        public static float Without(float time, float loads)
        {
            if (float.IsNaN(time) || float.IsNaN(loads)) return float.NaN;
            float t = time - loads;
            return t < 0f ? 0f : t;
        }

        // --- an attempt's LRT ----------------------------------------------------

        /// The attempt's load-removed time. An attempt saved before loads
        /// were tracked (no `loads` line) counts as having none.
        public static float LrtOf(Attempt a)
        {
            if (a == null) return float.NaN;
            return Without(a.Duration, a.LoadTime);
        }

        /// The attempt's split times as rows (Data/SplitTable's SplitsOf)
        /// with the loads before each one taken out.
        public static float[] LrtSplitsOf(Attempt a, int rows)
        {
            float[] cum = SplitStats.SplitsOf(a, rows);
            if (a == null) return cum;
            for (int i = 0; i < rows; i++)
            {
                float loads = i == rows - 1 ? a.LoadTime
                            : a.SplitLoads != null && i < a.SplitLoads.Length ? a.SplitLoads[i] : 0f;
                cum[i] = Without(cum[i], loads);
            }
            return cum;
        }

        /// "no loads", "1 load, 4.25 s", "3 loads, 1:02.50".
        public static string Describe(int loads, float seconds)
        {
            if (loads <= 0 || !(seconds > 0f)) return "no loads";
            return loads + (loads == 1 ? " load, " : " loads, ") + SplitTable.Time(seconds, 2) + (seconds < 59.995f ? " s" : "");
        }
    }

    /// One load in real time: when it ended and how long it took.
    public struct LoadSpanInfo
    {
        public long StartMs;
        public long EndMs;
        public long LengthMs { get { return EndMs - StartMs; } }
    }

    /// The game's loading state followed in real time (run mode's attempt
    /// log: ms since the attempt started).
    public sealed class LoadSpan
    {
        private bool _in;
        private long _start;

        public bool InLoad { get { return _in; } }

        /// Since when the load under way runs (-1 = none).
        public long StartMs { get { return _in ? _start : -1; } }

        public void Reset() { _in = false; _start = 0; }

        /// The state at `realMs`. True when a load ended here (`span`
        /// filled); a load still under way ends at Finish.
        public bool Update(bool loading, long realMs, out LoadSpanInfo span)
        {
            span = new LoadSpanInfo();
            if (loading)
            {
                if (!_in) { _in = true; _start = realMs; }
                return false;
            }
            if (!_in) return false;
            _in = false;
            span.StartMs = _start;
            span.EndMs = realMs < _start ? _start : realMs;
            return true;
        }

        /// The attempt ends during a load: that load, cut at `realMs`.
        public bool Finish(long realMs, out LoadSpanInfo span)
        {
            return Update(false, realMs, out span);
        }
    }
}
