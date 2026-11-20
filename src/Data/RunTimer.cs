using System.Collections.Generic;
using UnityEngine;

namespace ForestOverlay.Data
{
    /// What one step of the run timer did.
    public enum TimerStep
    {
        None,
        /// The clock started (the start trigger crossed, or F12 while armed).
        Started,
        /// A checkpoint was reached: Splits gained its time.
        Split,
        /// The end was reached with a checkpoint still outstanding - the
        /// run goes on (said, so a run that will not finish is explained).
        EndBlocked,
        /// The end was reached with every checkpoint done: the caller
        /// finishes the run (Finish), saving and showing it its own way.
        Finished,
    }

    /// The game's events as the timer reads them, oldest first
    /// (Game/GameEvents; a list in the tests).
    public interface ITimerEvents
    {
        int Count { get; }
        string NameAt(int i);
        /// A companion name of an occurrence already listed under another
        /// (cave-enter after cave-enter-cave06): Data/OccurrenceGate.
        bool CompanionAt(int i);
    }

    /// What one frame of the timer (RunTimer.Step) tells its owner, in the
    /// order it happens.
    public interface ITimerSink
    {
        /// Event `index` is read - before the occurrence gate, so a
        /// companion name the gate skips still arrives (the replay marks
        /// take their detail from it).
        void GameEvent(int index);

        /// An autosplit acts on `why` (an event name, or "an item"), just
        /// before its evaluation.
        void AutoSplitting(string why);

        /// The timer moved: Started, Split, EndBlocked or Finished. On
        /// Finished the sink finishes the run (Finish) before the next
        /// event is read, so the events after the end move nothing.
        void Stepped(TimerStep step);
    }

    /// A frozen copy of item counts. Only the ids a segment references are
    /// stored, because that is all anything will ask for: the run's start
    /// counts, so a relative item trigger ("gain 3 more rope") is measured
    /// from where the run began rather than from zero.
    public sealed class ItemSnapshot : IItemCounts
    {
        private readonly Dictionary<int, int> _counts = new Dictionary<int, int>();

        public void Capture(IItemCounts source, IList<int> ids)
        {
            _counts.Clear();
            if (source == null || ids == null) return;

            for (int i = 0; i < ids.Count; i++)
            {
                if (_counts.ContainsKey(ids[i])) continue;
                _counts[ids[i]] = source.AmountOf(ids[i]);
            }
        }

        public int AmountOf(int itemId)
        {
            int v;
            return _counts.TryGetValue(itemId, out v) ? v : 0;
        }

        /// A run's start counts put back (a checkpoint state's baseline).
        public void Load(IList<KeyValuePair<int, int>> counts)
        {
            _counts.Clear();
            if (counts == null) return;
            for (int i = 0; i < counts.Count; i++) _counts[counts[i].Key] = counts[i].Value;
        }
    }

    // ------------------------------------------------------------------
    // The run timer's state machine (T-0467): the timer of record, so it is
    // pure and unit tested (RunTimerTests). Idle -> Armed -> Running ->
    // Idle, driven by a timed segment's triggers:
    //
    // - Arm: a timed segment is placed. The start trigger is primed on its
    //   first evaluation, so standing in the start zone after a teleport
    //   starts nothing; crossing it (leaving, or entering from outside)
    //   starts the clock. Lining up costs nothing.
    // - Running: checkpoints split in order, then the end finishes
    //   (Data/SplitSequence; the end waits for the checkpoints). F12 starts,
    //   skips a checkpoint or finishes by hand (Advance).
    // - Each row (the checkpoints, then the end) keeps its time and the
    //   timer's load seconds when it was reached: the splits panel's times
    //   and LRT column, the attempt's Splits and SplitLoads. They stay after
    //   a finish (shown until the next run starts) - ResetRows clears them.
    // - Resume: a run picked up from a checkpoint state, with the clock at
    //   the capture's time and the earlier checkpoints done.
    //
    // The clock and the recorded attempt are Data/RunRecorder's; the game
    // side (where the player is, the inventory, the events, the load state)
    // is passed in by the caller every frame.
    // ------------------------------------------------------------------
    public sealed class RunTimer
    {
        /// The clock and the attempt being recorded (its samplers are set
        /// by the caller).
        public readonly RunRecorder Recorder = new RunRecorder();

        private readonly SplitSequence _sequence = new SplitSequence();
        private readonly OccurrenceGate _occurrence = new OccurrenceGate();
        private readonly AutoSplitWatch _auto = new AutoSplitWatch();
        private readonly ItemSnapshot _baseline = new ItemSnapshot();
        private readonly List<int> _itemIds = new List<int>();
        private readonly List<float> _splits = new List<float>();
        private TriggerState _startState;
        private Segment _segment;
        private float[] _times = new float[0];
        private float[] _loadsAt = new float[0];
        private int _autoFrame = -1;
        private bool _skipDt;

        public RunRecorder.RunState State { get { return Recorder.State; } }
        public bool Running { get { return Recorder.State == RunRecorder.RunState.Running; } }
        public bool Armed { get { return Recorder.State == RunRecorder.RunState.Armed; } }
        public float Elapsed { get { return Recorder.Elapsed; } }
        public float Lrt { get { return Recorder.Lrt; } }
        public LoadClock LoadClock { get { return Recorder.LoadClock; } }
        public Attempt Current { get { return Recorder.Current; } }

        /// The segment last armed (null before any).
        public Segment Segment { get { return _segment; } }

        /// This run's checkpoint times, in order (the end is not in it).
        public List<float> Splits { get { return _splits; } }

        /// Per row (the checkpoints, then the end): the time it was reached,
        /// NaN = not reached. Kept after a finish until ResetRows.
        public float[] Times { get { return _times; } }

        /// Per row: the timer's load seconds when it was reached (NaN = not
        /// reached or unknown).
        public float[] LoadsAt { get { return _loadsAt; } }

        /// The item ids the segment's triggers and autosplits read: the
        /// inventory must be fresh for them.
        public List<int> ItemIds { get { return _itemIds; } }

        /// The item counts when the run was armed (relative item triggers).
        public ItemSnapshot Baseline { get { return _baseline; } }

        /// The checkpoint the run heads for (Count = only the end is left).
        public int Next { get { return _sequence.Next; } }
        public bool OnlyEndLeft { get { return _sequence.OnlyEndLeft; } }
        /// The trigger the runner is heading for now (valid while running).
        public Trigger Heading { get { return _sequence.Current; } }

        /// -1 = a full run; else the checkpoint (0-based) this run resumed after.
        public int ResumedFrom { get; private set; }
        public bool Resumed { get { return ResumedFrom >= 0; } }

        /// The segment's autosplit list has entries (events or items).
        public bool AutoSplits { get { return _auto.Any; } }
        public bool AutoSplitsOnItems { get { return _auto.ItemIds.Count > 0; } }

        public RunTimer()
        {
            ResumedFrom = -1;
        }

        /// A run time as the log lines, the status and the saved runs'
        /// texts write it: "01:05.250".
        public static string Format(float seconds)
        {
            int m = (int)(seconds / 60f);
            float s = seconds - m * 60f;
            return m.ToString("00") + ":" + s.ToString("00.000");
        }

        /// The running timer in ms, -1 when none (run mode's chain).
        public long TimerMs()
        {
            return Running ? (long)System.Math.Round(Recorder.Elapsed * 1000.0) : -1;
        }

        /// Every ms of load the timer ever counted, across runs (run mode's
        /// `load` lines take the difference over one load).
        public long TimedLoadMs()
        {
            return (long)System.Math.Round(Recorder.LoadClock.Ever * 1000.0);
        }

        // --- arming --------------------------------------------------------------

        /// First step of arming `s`: which item ids its triggers and its
        /// autosplit list read (the caller refreshes the inventory for them
        /// before Arm captures the baseline).
        public void Prepare(Segment s)
        {
            _itemIds.Clear();
            if (s == null) { _auto.Configure(null); return; }

            AddItemId(s.Start);
            AddItemId(s.End);
            for (int i = 0; i < s.Checkpoints.Count; i++) AddItemId(s.Checkpoints[i]);

            // Autosplit items are read every frame like item triggers.
            _auto.Configure(s.AutoSplit);
            for (int i = 0; i < _auto.ItemIds.Count; i++)
                if (!_itemIds.Contains(_auto.ItemIds[i])) _itemIds.Add(_auto.ItemIds[i]);
        }

        private void AddItemId(Trigger t)
        {
            if (t.Kind != TriggerKind.Item) return;
            if (_itemIds.Contains(t.ItemId)) return;
            _itemIds.Add(t.ItemId);
        }

        /// Arms `s` (after Prepare): no clock yet, a full run, no splits, the
        /// item baseline taken from `live`. A running attempt is dropped -
        /// the caller keeps what it wants of it first. `anchor` is the
        /// attempt's first sample if the clock is started by hand.
        public void Arm(Segment s, Vector3 anchor, IItemCounts live, string route)
        {
            _segment = s;
            // Priming (rather than firing) on the first evaluation is what
            // stops the start trigger going off while the runner still stands
            // in the start zone after teleporting in.
            TriggerEvaluator.Reset(ref _startState);
            ResumedFrom = -1;
            _skipDt = false;
            _splits.Clear();
            _baseline.Capture(live, _itemIds);
            Recorder.Route = route ?? "";
            Recorder.Arm(anchor, s != null ? s.Id : "");
        }

        /// Drops the run (armed or running): Idle, nothing kept.
        public void Abort()
        {
            Recorder.Abort();
        }

        // --- running -------------------------------------------------------------

        /// One evaluation pass with what the game shows now (`firedEvent`:
        /// one game event, null = none). Armed: the start trigger crossed
        /// starts the clock. Running: at most one checkpoint splits, or the
        /// end is reached (Finished: the caller calls Finish).
        public TimerStep Evaluate(Vector3 position, IItemCounts live, string firedEvent)
        {
            if (_segment == null) return TimerStep.None;
            if (Armed)
            {
                // Crossing, not entering: the spawn usually sits inside the
                // start zone, so the run begins when the runner leaves it.
                if (!TriggerEvaluator.Crossed(_segment.Start, ref _startState, position, live, firedEvent, _baseline))
                    return TimerStep.None;
                Start(position, live);
                return TimerStep.Started;
            }
            if (!Running) return TimerStep.None;

            switch (_sequence.Evaluate(position, live, firedEvent, _baseline))
            {
                case SplitEvent.Split:
                    AddSplit();
                    return TimerStep.Split;
                case SplitEvent.EndBlocked:
                    return TimerStep.EndBlocked;
                case SplitEvent.Finished:
                    return TimerStep.Finished;
            }
            return TimerStep.None;
        }

        /// By hand (F12): armed, start the clock at `position`; running,
        /// split the next checkpoint (skipping its trigger), or with only the
        /// end left, Finished (the caller calls Finish). None when idle.
        public TimerStep Advance(Vector3 position, IItemCounts live)
        {
            if (Armed)
            {
                if (_segment == null) return TimerStep.None;
                Start(position, live);
                return TimerStep.Started;
            }
            if (!Running) return TimerStep.None;
            if (_segment != null && !_sequence.OnlyEndLeft)
            {
                AddSplit();
                _sequence.SkipCheckpoint();
                return TimerStep.Split;
            }
            return TimerStep.Finished;
        }

        private void Start(Vector3 position, IItemCounts live)
        {
            _auto.Begin(live);   // what is held now is the autosplits' baseline
            Recorder.ForceStart(position);
            _sequence.Begin(_segment.Checkpoints, _segment.End);
        }

        private void AddSplit()
        {
            float t = Recorder.Elapsed;
            _splits.Add(t);
            int row = _splits.Count - 1;
            if (row < _times.Length) _times[row] = t;
            if (row < _loadsAt.Length) _loadsAt[row] = Recorder.LoadClock.LoadTime;
        }

        /// Advances the clock by `dt` real seconds (`loading`: the game is
        /// loading - load-removed time). The first frame after a resume adds
        /// nothing: it holds the restore's own frame, not the runner's.
        public void Tick(Vector3 position, float horizontalSpeed, float dt, bool loading)
        {
            if (_skipDt) dt = 0f;
            _skipDt = false;
            Recorder.LoadingNow = loading;
            Recorder.Tick(position, horizontalSpeed, dt, null);
        }

        /// Ends the running attempt and returns it (null when none ran; the
        /// timer is Idle either way). Its splits are not stamped yet:
        /// StampSplits and FinishRows, as the caller saves it.
        public Attempt Finish()
        {
            return Recorder.Finish();
        }

        /// A finished attempt's split times - only when every checkpoint was
        /// reached - and the loads before each (only when there were any:
        /// none listed reads as none before any).
        public void StampSplits(Attempt done)
        {
            if (done == null) return;
            if (_segment != null && _splits.Count == _segment.Checkpoints.Count) done.Splits = _splits.ToArray();
            if (done.Loads > 0 && done.Splits.Length > 0 && _loadsAt.Length == done.Splits.Length + 1)
            {
                float[] at = new float[done.Splits.Length];
                for (int i = 0; i < at.Length; i++) at[i] = float.IsNaN(_loadsAt[i]) ? 0f : _loadsAt[i];
                done.SplitLoads = at;
            }
        }

        /// The end row reached by `done`: its time and loads.
        public void FinishRows(Attempt done)
        {
            if (done == null) return;
            if (_times.Length > 0) _times[_times.Length - 1] = done.Duration;
            if (_loadsAt.Length > 0) _loadsAt[_loadsAt.Length - 1] = done.LoadTime;
        }

        /// No row reached: `rows` rows (the checkpoints and the end) of NaN.
        /// When a run starts or another segment / route is armed - not on a
        /// re-arm, so the last run's times stay in view until the next start.
        public void ResetRows(int rows)
        {
            if (rows < 0) rows = 0;
            if (_times.Length != rows) _times = new float[rows];
            for (int i = 0; i < rows; i++) _times[i] = float.NaN;
            if (_loadsAt.Length != rows) _loadsAt = new float[rows];
            for (int i = 0; i < rows; i++) _loadsAt[i] = float.NaN;
        }

        /// The running attempt as an unfinished one (a reset, LiveSplit's
        /// word): when it started, its route, its time so far and the
        /// checkpoints reached. Null when nothing runs.
        public UnfinishedAttempt Unfinished()
        {
            Attempt cur = Recorder.Current;
            if (!Running || cur == null) return null;
            UnfinishedAttempt u = new UnfinishedAttempt();
            u.StartedUtc = cur.RecordedUtc;
            u.Route = cur.Route ?? "";
            u.Duration = Recorder.Elapsed;
            u.Splits = _splits.ToArray();
            return u;
        }

        // --- a run from a checkpoint state -------------------------------------------

        /// Picks the armed run up from a checkpoint state (call Arm first):
        /// the clock at the capture's time, its checkpoints done with their
        /// split times, the start's item counts put back, and the next
        /// checkpoint armed as if the last one had just fired.
        public void Resume(Vector3 position, CheckpointState st, IItemCounts live)
        {
            if (_segment == null || st == null || !Armed) return;
            _baseline.Load(st.Baseline);
            _splits.Clear();
            _splits.AddRange(st.Splits);
            _auto.Begin(live);
            Recorder.Resume(position, st.ResumeAt);
            _sequence.Resume(_segment.Checkpoints, _segment.End, st.Index + 1);
            ResetRows(_segment.Checkpoints.Count + 1);
            for (int i = 0; i < st.Splits.Length && i < _times.Length; i++) { _times[i] = st.Splits[i]; _loadsAt[i] = 0f; }
            ResumedFrom = st.Index;
            _skipDt = true;
        }

        /// The resumed run is over (finished and shown).
        public void EndResume()
        {
            ResumedFrom = -1;
        }

        // --- one frame ------------------------------------------------------------------

        /// One frame's evaluation (not the clock: Tick). `seen` events of
        /// `events` were read before; returns how many are read now. With
        /// no new event, one pass with none; else one pass per event, in
        /// order - so two cutscenes starting in one frame both count.
        /// Events are instants: satisfied only in the pass that carries
        /// them, which gives the rising edge by itself. One occurrence under
        /// several names moves the run once (Gate); the segment's autosplit
        /// list splits at most once a frame (`frame`, the ASL's update).
        public int Step(ITimerEvents events, int seen, Vector3 position, IItemCounts live, int frame, ITimerSink sink)
        {
            int count = events.Count;
            if (seen > count) seen = count;

            if (seen == count) Report(Evaluate(position, live, null), sink);
            while (seen < count)
            {
                int at = seen++;
                string fired = events.NameAt(at);
                sink.GameEvent(at);   // before the gate: the companion carries the detail
                if (!Gate(events.CompanionAt(at))) continue;
                RunRecorder.RunState stateBefore = State;
                int splitsBefore = _splits.Count;
                Report(Evaluate(position, live, fired), sink);
                // `event autosplit` (v0.24.186, a LiveSplit import): the ASL's
                // per-run memory (Data/AutoSplitWatch).
                if (AutoSplitOn(fired)) AutoSplit(position, live, fired, frame, sink);
                if (State != stateBefore || _splits.Count != splitsBefore) Moved();
            }
            if (AutoSplitOnItems(live)) AutoSplit(position, live, "an item", frame, sink);
            return seen;
        }

        private void AutoSplit(Vector3 position, IItemCounts live, string why, int frame, ITimerSink sink)
        {
            if (!TakeAutoSplitFrame(frame)) return;
            sink.AutoSplitting(why);
            Report(Evaluate(position, live, Segment.AutoSplitEvent), sink);
        }

        private static void Report(TimerStep step, ITimerSink sink)
        {
            if (step != TimerStep.None) sink.Stepped(step);
        }

        // --- several names, autosplits -------------------------------------------------

        /// Per game event, in order: false = skip it, a companion name of an
        /// occurrence that already moved the run (Data/OccurrenceGate).
        public bool Gate(bool companion)
        {
            return _occurrence.Evaluate(companion);
        }

        /// The event just evaluated started, split or finished the run.
        public void Moved()
        {
            _occurrence.Moved();
        }

        /// The segment's autosplit list splits on this event (running only).
        public bool AutoSplitOn(string firedEvent)
        {
            return _auto.Any && Running && _auto.OnEvent(firedEvent);
        }

        /// A watched item's count changed in a way the list splits on
        /// (running only). Reads every watched item whatever the answer.
        public bool AutoSplitOnItems(IItemCounts live)
        {
            return _auto.ItemIds.Count > 0 && Running && _auto.OnItems(live);
        }

        /// An autosplit may act this frame: the ASL splits once per update,
        /// whatever else also matched. False when one already did.
        public bool TakeAutoSplitFrame(int frame)
        {
            if (frame == _autoFrame) return false;
            _autoFrame = frame;
            return true;
        }
    }
}
