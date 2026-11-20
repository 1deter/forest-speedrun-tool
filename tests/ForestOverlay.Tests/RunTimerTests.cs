using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // The run timer's state machine (Data/RunTimer, T-0467): the timer of
    // record once the tool replaces LiveSplit, so every transition a timed
    // run goes through is pinned here - arming, the start crossing, splits
    // in order, the blocked end, F12, finishing, aborting, the rows the
    // splits panel shows, loads, and a run resumed from a checkpoint.
    // ------------------------------------------------------------------
    public class RunTimerTests
    {
        private static readonly Vector3 StartPos = new Vector3(0f, 0f, 0f);
        private static readonly Vector3 Away = new Vector3(20f, 0f, 0f);
        private static readonly Vector3 Cp1 = new Vector3(50f, 0f, 0f);
        private static readonly Vector3 Cp2 = new Vector3(70f, 0f, 0f);
        private static readonly Vector3 EndPos = new Vector3(100f, 0f, 0f);

        private static Trigger Zone(Vector3 p, float r)
        {
            Trigger t = new Trigger();
            t.Kind = TriggerKind.Zone;
            t.Position = p;
            t.Radius = r;
            return t;
        }

        private static Trigger Item(int id, int amount, bool relative)
        {
            Trigger t = new Trigger();
            t.Kind = TriggerKind.Item;
            t.ItemId = id;
            t.Compare = Comparison.AtLeast;
            t.Amount = amount;
            t.Relative = relative;
            return t;
        }

        private static Trigger Event(string name)
        {
            Trigger t = new Trigger();
            t.Kind = TriggerKind.Event;
            t.EventName = name;
            return t;
        }

        /// Start zone at the origin, checkpoints at 50 and 70, end at 100.
        private static Segment Seg(int checkpoints = 2)
        {
            Segment s = new Segment();
            s.Id = "seg-test";
            s.Name = "Test";
            s.Start = Zone(StartPos, 5f);
            if (checkpoints > 0) s.Checkpoints.Add(Zone(Cp1, 2f));
            if (checkpoints > 1) s.Checkpoints.Add(Zone(Cp2, 2f));
            s.End = Zone(EndPos, 2f);
            return s;
        }

        private static RunTimer Armed(Segment s, IItemCounts items = null)
        {
            RunTimer t = new RunTimer();
            t.Prepare(s);
            t.Arm(s, StartPos, items, s.RouteFingerprint());
            t.ResetRows(s.Checkpoints.Count + 1);
            return t;
        }

        /// Armed, standing in the start zone, then out of it: running - and
        /// one more frame away from every zone (the game evaluates every
        /// frame; a zone current at the start is primed on its first one).
        private static RunTimer Running(Segment s, IItemCounts items = null)
        {
            RunTimer t = Armed(s, items);
            Assert.Equal(TimerStep.None, t.Evaluate(StartPos, items, null));
            Assert.Equal(TimerStep.Started, t.Evaluate(Away, items, null));
            Assert.Equal(TimerStep.None, t.Evaluate(Away, items, null));
            return t;
        }

        [Fact]
        public void AZoneTheRunnerStandsInAtTheStartIsPrimedNotFired()
        {
            // A loop route: the end zone is where the clock starts.
            Segment s = Seg(0);
            s.End = Zone(Away, 5f);
            RunTimer t = Armed(s);
            t.Evaluate(StartPos, null, null);
            Assert.Equal(TimerStep.Started, t.Evaluate(Away, null, null));
            Assert.Equal(TimerStep.None, t.Evaluate(Away, null, null));
            Assert.Equal(TimerStep.None, t.Evaluate(EndPos, null, null));
            Assert.Equal(TimerStep.Finished, t.Evaluate(Away, null, null));
        }

        [Fact]
        public void StandingInTheStartZoneAfterArmingStartsNothing()
        {
            RunTimer t = Armed(Seg());
            Assert.True(t.Armed);
            for (int i = 0; i < 5; i++) Assert.Equal(TimerStep.None, t.Evaluate(StartPos, null, null));
            t.Tick(StartPos, 0f, 1f, false);
            Assert.Equal(0f, t.Elapsed);
            Assert.Equal(-1L, t.TimerMs());
        }

        [Fact]
        public void LeavingTheStartZoneStartsTheClock()
        {
            RunTimer t = Running(Seg());
            Assert.True(t.Running);
            t.Tick(Away, 3f, 1.5f, false);
            Assert.Equal(1.5f, t.Elapsed, 4);
            Assert.Equal(1500L, t.TimerMs());
            Assert.NotNull(t.Current);
            Assert.Equal("seg-test", t.Current.AnchorLabel);
        }

        [Fact]
        public void EnteringTheStartZoneFromOutsideStartsTheClock()
        {
            RunTimer t = Armed(Seg());
            Assert.Equal(TimerStep.None, t.Evaluate(Away, null, null));      // primed: outside
            Assert.Equal(TimerStep.Started, t.Evaluate(StartPos, null, null));
        }

        [Fact]
        public void CheckpointsSplitInOrderAndFillTheirRows()
        {
            RunTimer t = Running(Seg());
            t.Tick(Away, 0f, 2f, false);
            Assert.Equal(TimerStep.Split, t.Evaluate(Cp1, null, null));
            Assert.Equal(new List<float> { 2f }, t.Splits);
            Assert.Equal(2f, t.Times[0]);
            Assert.True(float.IsNaN(t.Times[1]));
            Assert.Equal(1, t.Next);

            t.Tick(Cp1, 0f, 1f, false);
            Assert.Equal(TimerStep.None, t.Evaluate(Cp1, null, null));
            Assert.Equal(TimerStep.Split, t.Evaluate(Cp2, null, null));
            Assert.Equal(3f, t.Times[1]);
            Assert.True(t.OnlyEndLeft);
        }

        [Fact]
        public void TheEndWaitsForTheCheckpoints()
        {
            RunTimer t = Running(Seg(1));
            Assert.Equal(TimerStep.EndBlocked, t.Evaluate(EndPos, null, null));
            Assert.True(t.Running);
            Assert.Equal(TimerStep.Split, t.Evaluate(Cp1, null, null));
            Assert.Equal(TimerStep.None, t.Evaluate(Away, null, null));
            Assert.Equal(TimerStep.Finished, t.Evaluate(EndPos, null, null));
            Assert.True(t.Running);   // Finished asks the caller to Finish
        }

        [Fact]
        public void FinishStampsTheSplitsAndTheEndRow()
        {
            RunTimer t = Running(Seg(1));
            t.Tick(Away, 0f, 4f, false);
            t.Evaluate(Cp1, null, null);
            t.Tick(Cp1, 0f, 6f, false);
            Assert.Equal(TimerStep.Finished, t.Evaluate(EndPos, null, null));

            Attempt done = t.Finish();
            Assert.NotNull(done);
            t.StampSplits(done);
            t.FinishRows(done);
            Assert.Equal(RunRecorder.RunState.Idle, t.State);
            Assert.True(done.Completed);
            Assert.Equal(10f, done.Duration, 4);
            Assert.Equal(new[] { 4f }, done.Splits);
            Assert.Empty(done.SplitLoads);   // no loads: none listed
            Assert.Equal(10f, t.Times[1], 4);
            Assert.Equal(4f, t.Times[0]);   // the rows stay after the finish
            Assert.Equal(-1L, t.TimerMs());
        }

        [Fact]
        public void AnIncompleteSplitListIsNotStamped()
        {
            Segment s = Seg(2);
            RunTimer t = Running(s);
            t.Evaluate(Cp1, null, null);
            Attempt done = t.Finish();   // finished by hand with a checkpoint short
            t.StampSplits(done);
            Assert.Empty(done.Splits);
        }

        [Fact]
        public void LoadsAreCountedPerRow()
        {
            RunTimer t = Running(Seg(1));
            t.Tick(Away, 0f, 1f, false);
            t.Tick(Away, 0f, 2f, true);    // a 2 s load before the checkpoint
            t.Tick(Away, 0f, 1f, false);
            t.Evaluate(Cp1, null, null);
            Assert.Equal(2f, t.LoadsAt[0], 4);
            t.Tick(Cp1, 0f, 1f, true);     // and a 1 s one after it
            t.Tick(Cp1, 0f, 1f, false);
            Assert.Equal(3f, t.Lrt, 4);

            Attempt done = t.Finish();
            t.StampSplits(done);
            t.FinishRows(done);
            Assert.Equal(2, done.Loads);
            Assert.Equal(3f, done.LoadTime, 4);
            Assert.Equal(new[] { 2f }, done.SplitLoads);
            Assert.Equal(3f, t.LoadsAt[1], 4);
            Assert.Equal(3000L, t.TimedLoadMs());
        }

        [Fact]
        public void TheLoadMsNeverResetsAcrossRuns()
        {
            Segment s = Seg(0);
            RunTimer t = Running(s);
            t.Tick(Away, 0f, 1f, true);
            t.Abort();
            t.Arm(s, StartPos, null, "");
            t.Evaluate(StartPos, null, null);
            t.Evaluate(Away, null, null);
            t.Tick(Away, 0f, 0.5f, true);
            Assert.Equal(1500L, t.TimedLoadMs());
            Assert.Equal(0.5f, t.LoadClock.LoadTime, 4);
        }

        [Fact]
        public void F12StartsSplitsAndFinishes()
        {
            Segment s = Seg(1);
            RunTimer t = Armed(s);
            Assert.Equal(TimerStep.Started, t.Advance(StartPos, null));
            t.Tick(StartPos, 0f, 1f, false);
            Assert.Equal(TimerStep.Split, t.Advance(StartPos, null));
            Assert.Equal(new List<float> { 1f }, t.Splits);
            Assert.True(t.OnlyEndLeft);
            // The skipped checkpoint's zone no longer splits.
            Assert.Equal(TimerStep.None, t.Evaluate(Cp1, null, null));
            Assert.Equal(TimerStep.Finished, t.Advance(StartPos, null));
        }

        [Fact]
        public void F12DoesNothingWhenIdle()
        {
            RunTimer t = new RunTimer();
            Assert.Equal(TimerStep.None, t.Advance(StartPos, null));
            Assert.Equal(TimerStep.None, t.Evaluate(StartPos, null, null));
            Assert.Null(t.Finish());
        }

        [Fact]
        public void AbortDropsTheRun()
        {
            RunTimer t = Running(Seg());
            t.Tick(Away, 0f, 3f, false);
            t.Abort();
            Assert.Equal(RunRecorder.RunState.Idle, t.State);
            Assert.Equal(0f, t.Elapsed);
            Assert.Null(t.Current);
            Assert.Null(t.Unfinished());
        }

        [Fact]
        public void ReArmingClearsTheSplitsButKeepsTheRows()
        {
            Segment s = Seg(1);
            RunTimer t = Running(s);
            t.Tick(Away, 0f, 2f, false);
            t.Evaluate(Cp1, null, null);
            t.Arm(s, StartPos, null, "");
            Assert.True(t.Armed);
            Assert.Empty(t.Splits);
            Assert.Equal(2f, t.Times[0]);    // in view until the next start's ResetRows
            t.ResetRows(2);
            Assert.True(float.IsNaN(t.Times[0]));
            Assert.True(float.IsNaN(t.LoadsAt[0]));
        }

        [Fact]
        public void UnfinishedKeepsTheTimeAndTheSplitsSoFar()
        {
            RunTimer t = Running(Seg(2));
            t.Tick(Away, 0f, 2f, false);
            t.Evaluate(Cp1, null, null);
            t.Tick(Cp1, 0f, 1f, false);
            UnfinishedAttempt u = t.Unfinished();
            Assert.NotNull(u);
            Assert.Equal(3f, u.Duration, 4);
            Assert.Equal(new[] { 2f }, u.Splits);
            Assert.Equal(t.Current.Route, u.Route);
        }

        [Fact]
        public void RelativeItemTriggersCountFromTheArmingBaseline()
        {
            Segment s = Seg(0);
            s.End = Item(78, 3, true);   // gain 3 rope
            FakeItems items = new FakeItems().Set(78, 5);
            RunTimer t = Running(s, items);
            Assert.Contains(78, t.ItemIds);
            Assert.Equal(5, t.Baseline.AmountOf(78));
            items.Set(78, 7);
            Assert.Equal(TimerStep.None, t.Evaluate(Away, items, null));
            items.Set(78, 8);
            Assert.Equal(TimerStep.Finished, t.Evaluate(Away, items, null));
        }

        [Fact]
        public void AnEventStartedSegmentStartsOnItsEvent()
        {
            Segment s = Seg(0);
            s.Start = Event("first-input");
            RunTimer t = Armed(s);
            Assert.Equal(TimerStep.None, t.Evaluate(StartPos, null, null));
            Assert.Equal(TimerStep.None, t.Evaluate(StartPos, null, "cave-enter"));
            Assert.Equal(TimerStep.Started, t.Evaluate(StartPos, null, "first-input"));
        }

        [Fact]
        public void ResumeFromACheckpointState()
        {
            Segment s = Seg(2);
            RunTimer t = Armed(s);
            CheckpointState st = new CheckpointState();
            st.Index = 0;
            st.ResumeAt = 12.5f;
            st.Splits = new[] { 12f };
            st.Baseline.Add(new KeyValuePair<int, int>(78, 4));
            t.Resume(Cp1, st, null);

            Assert.True(t.Running);
            Assert.True(t.Resumed);
            Assert.Equal(0, t.ResumedFrom);
            Assert.Equal(12.5f, t.Elapsed, 4);
            Assert.Equal(new List<float> { 12f }, t.Splits);
            Assert.Equal(12f, t.Times[0]);
            Assert.Equal(0f, t.LoadsAt[0]);
            Assert.True(float.IsNaN(t.Times[1]));
            Assert.Equal(4, t.Baseline.AmountOf(78));
            Assert.Equal(1, t.Next);

            // The first frame holds the restore: no time added.
            t.Tick(Cp1, 0f, 0.8f, false);
            Assert.Equal(12.5f, t.Elapsed, 4);
            t.Tick(Cp1, 0f, 0.5f, false);
            Assert.Equal(13f, t.Elapsed, 4);

            Assert.Equal(TimerStep.Split, t.Evaluate(Cp2, null, null));
            Assert.Equal(13f, t.Times[1], 4);

            t.EndResume();
            Assert.False(t.Resumed);
        }

        [Fact]
        public void ArmingEndsAResume()
        {
            Segment s = Seg(1);
            RunTimer t = Armed(s);
            CheckpointState st = new CheckpointState();
            st.Index = 0;
            st.ResumeAt = 5f;
            st.Splits = new[] { 5f };
            t.Resume(Cp1, st, null);
            t.Arm(s, StartPos, null, "");
            Assert.False(t.Resumed);
            // And the next frame counts in full.
            t.Evaluate(StartPos, null, null);
            t.Evaluate(Away, null, null);
            t.Tick(Away, 0f, 1f, false);
            Assert.Equal(1f, t.Elapsed, 4);
        }

        [Fact]
        public void ResumeNeedsAnArmedRun()
        {
            RunTimer t = Running(Seg(1));
            CheckpointState st = new CheckpointState();
            st.Index = 0;
            st.ResumeAt = 5f;
            st.Splits = new[] { 5f };
            t.Resume(Cp1, st, null);
            Assert.False(t.Resumed);
            Assert.Equal(0f, t.Elapsed);
        }

        [Fact]
        public void AutoSplitsOncePerFrame()
        {
            Segment s = Seg(1);
            s.AutoSplit.Add("cave-enter");
            RunTimer t = Armed(s);
            Assert.True(t.AutoSplits);
            Assert.False(t.AutoSplitOn("cave-enter"));   // not running yet
            t.Evaluate(StartPos, null, null);
            t.Evaluate(Away, null, null);
            Assert.True(t.AutoSplitOn("cave-enter"));
            Assert.False(t.AutoSplitOn("cave-exit"));
            Assert.True(t.TakeAutoSplitFrame(10));
            Assert.False(t.TakeAutoSplitFrame(10));
            Assert.True(t.TakeAutoSplitFrame(11));
        }

        [Fact]
        public void AnAutoSplitItemIsWatched()
        {
            Segment s = Seg(1);
            s.AutoSplit.Add(LssAutoSplit.ItemFirst + "210");
            FakeItems items = new FakeItems().Set(210, 0);
            RunTimer t = Armed(s, items);
            Assert.True(t.AutoSplitsOnItems);
            Assert.Contains(210, t.ItemIds);
            t.Evaluate(StartPos, items, null);
            t.Evaluate(Away, items, null);
            Assert.False(t.AutoSplitOnItems(items));
            items.Set(210, 1);
            Assert.True(t.AutoSplitOnItems(items));
            Assert.False(t.AutoSplitOnItems(items));
        }

        [Fact]
        public void ACompanionEventIsSkippedOnceTheRunMoved()
        {
            RunTimer t = Running(Seg(1));
            Assert.True(t.Gate(false));
            t.Moved();
            Assert.False(t.Gate(true));    // the same occurrence's other name
            Assert.True(t.Gate(false));    // the next occurrence
        }

        [Fact]
        public void PrepareListsEveryItemIdOnce()
        {
            Segment s = Seg(0);
            s.Start = Item(10, 1, false);
            s.Checkpoints.Add(Item(10, 2, false));
            s.Checkpoints.Add(Item(11, 1, false));
            s.End = Item(11, 2, false);
            RunTimer t = new RunTimer();
            t.Prepare(s);
            Assert.Equal(new List<int> { 10, 11 }, t.ItemIds);
            t.Prepare(Seg(0));
            Assert.Empty(t.ItemIds);
        }
    }
}
