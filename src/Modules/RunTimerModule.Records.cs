using System.Collections.Generic;
using ForestOverlay.Core;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // The run's records (T-0467 step 5c): the timer of record keeps the
    // runner's runs, so a run file without the practice tools still has a
    // PB to race (decisions.md *Refactor and race build*).
    //
    // - The attempts of the segment loaded, on the route armed, this
    //   runner's own only: an imported .foseg's runs are other runners'
    //   (Data/AttemptOwners) - they go back to the driver as comparisons.
    // - Runs started (AttemptStore started.txt), the unfinished ones (a
    //   reset, LiveSplit's word: PB chance and playtime) and the finished
    //   ones' time on every route.
    // - Saving a finished run: stamped (RunTimerModule.Stamp), on file, to
    //   the website's queue (Modules/RunUploadModule).
    //
    // The driver (Modules/PracticeRunModule) says when: a spot armed, a run
    // dropped or finished, the times cleared.
    // ------------------------------------------------------------------
    public sealed partial class RunTimerModule
    {
        private AttemptStore _store;
        private RunUploadModule _upload;
        private readonly List<Attempt> _attempts = new List<Attempt>();
        private string _loadedSegmentId;
        private int _otherRouteCount;
        private int _started;
        private List<UnfinishedAttempt> _unfinished = new List<UnfinishedAttempt>();
        private float _playedFinished;
        private Attempt _recordedUnfinished;

        /// This runner's attempts on the loaded segment and route, oldest
        /// first (the driver reads them; only this module adds or clears).
        public List<Attempt> Attempts { get { return _attempts; } }

        /// The segment whose attempts are loaded (null before any).
        public string LoadedSegmentId { get { return _loadedSegmentId; } }

        /// Saved times of the loaded segment on another route (left out).
        public int OtherRouteCount { get { return _otherRouteCount; } }

        /// Runs started on the loaded segment, finished or not; never fewer
        /// than the finished ones on file.
        public int Started { get { return _started; } }

        /// The loaded segment's unfinished runs, every route.
        public List<UnfinishedAttempt> Unfinished { get { return _unfinished; } }

        /// This runner's finished runs' time on the loaded segment, every
        /// route (time played is time played).
        public float PlayedFinished { get { return _playedFinished; } }

        private void InitRecords(ModuleContext ctx)
        {
            _store = new AttemptStore(ctx.Log, ctx.ConfigDirectory);
        }

        /// Loads `s`'s attempts on its route; false (nothing done) when they
        /// are the ones loaded. Moving a start zone changes what the times
        /// mean while leaving the id alone, so times from another route are
        /// kept on disk and left out rather than silently racing the new
        /// one. `others`: other runners' attempts on this route (an imported
        /// .foseg) - they compare, never count.
        public bool LoadAttempts(Segment s, out List<Attempt> others)
        {
            others = null;
            string route = s.RouteFingerprint();
            if (s.Id == _loadedSegmentId && route == ArmedRoute) return false;

            _loadedSegmentId = s.Id;
            _attempts.Clear();
            _otherRouteCount = 0;

            List<Attempt> all = _store.LoadAll(s.Id);
            string own = RunnerId();
            others = new List<Attempt>();
            for (int i = 0; i < all.Count; i++)
            {
                // An empty route means the attempt predates route
                // tracking; treat it as belonging to the current one
                // rather than throwing away someone's history.
                if (all[i].Route.Length > 0 && all[i].Route != route)
                {
                    _otherRouteCount++;
                    continue;
                }

                if (AttemptOwners.IsOwn(all[i].RunnerId, own)) _attempts.Add(all[i]);
                else others.Add(all[i]);
            }
            _started = Mathf.Max(_store.Started(s.Id), _attempts.Count);

            // Its unfinished runs and the time of this runner's finished
            // ones, every route.
            _unfinished = _store.LoadUnfinished(s.Id);
            _playedFinished = 0f;
            for (int i = 0; i < all.Count; i++)
                if (all[i].Completed && AttemptOwners.IsOwn(all[i].RunnerId, own)) _playedFinished += all[i].Duration;
            return true;
        }

        /// The clock started on `s`: one more run started.
        public void CountStarted(Segment s)
        {
            _started = Mathf.Max(_started, _attempts.Count) + 1;
            _store.SetStarted(s.Id, _started);
        }

        /// A running attempt about to be dropped (abort, restart, death,
        /// another spot, practice off, the title screen): kept as an
        /// unfinished one - LiveSplit's reset - for PB chance and playtime.
        /// Once per attempt, whichever path gets there first; false when
        /// nothing was kept.
        public bool RecordUnfinished(string why)
        {
            Attempt cur = _timer.Current;
            if (!_timer.Running || cur == null || ReferenceEquals(cur, _recordedUnfinished)) return false;
            if (_timer.Resumed) return false;   // from a checkpoint: practice, not a run that started
            _recordedUnfinished = cur;

            UnfinishedAttempt u = _timer.Unfinished();
            _store.AddUnfinished(cur.AnchorLabel, u);
            if (cur.AnchorLabel == _loadedSegmentId) _unfinished.Add(u);
            Ctx.Log.LogInfo("Run '" + cur.AnchorLabel + "': unfinished after " + RunTimer.Format(u.Duration) + " (" + why + ", " +
                            u.Splits.Length + " split(s) reached) - kept for PB chance and playtime.");
            return true;
        }

        /// A finished run of `segment` kept: stamped, one of the attempts,
        /// on file, and queued for the website.
        public void SaveFinished(Attempt done, Segment segment)
        {
            Stamp(done);
            _attempts.Add(done);
            _playedFinished += done.Duration;
            _store.Save(done);
            if (_upload != null && segment != null)
                _upload.Enqueue(segment, AttemptFormat.Write(done), done.RunnerId, done.RunnerName);
        }

        /// Clear times: the attempts leave the view; the files are kept.
        public void ClearAttempts()
        {
            _attempts.Clear();
        }

        /// The runner's own saved runs of `s` for the website - an imported
        /// .foseg's runs are other runners' and theirs to upload. Null when
        /// there are none.
        public List<string> SavedRunsFor(Segment s)
        {
            if (s == null) return null;
            List<string> texts = _store.RunTexts(s.Id);
            if (texts == null) return null;
            string own = RunnerId();
            List<string> mine = new List<string>(texts.Count);
            string name = RunnerName();
            for (int i = 0; i < texts.Count; i++)
                if (AttemptOwners.IsOwn(AttemptOwners.RunnerIdOf(texts[i]), own))
                    mine.Add(AttemptFormat.WithRunner(texts[i], own, name));   // an old one: name it, as an export does
            return mine;
        }

        /// "23 attempts (9 finished)", or "9 attempts" when every one finished.
        public string AttemptsText()
        {
            int started = Mathf.Max(_started, _attempts.Count);
            return started + " attempts" + (started > _attempts.Count ? " (" + _attempts.Count + " finished)" : "");
        }
    }
}
