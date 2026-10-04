using System;
using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // Checkpoint states (restart from checkpoint N): the meta file, which
    // files make a usable state, the split sequence resumed part-way, and
    // which times a resumed run may set (golds yes, a PB never).
    // ------------------------------------------------------------------
    public class CheckpointStatesTests
    {
        private static Trigger Zone(float x, float r)
        {
            Trigger t = new Trigger();
            t.Kind = TriggerKind.Zone;
            t.Position = new Vector3(x, 0f, 0f);
            t.Radius = r;
            return t;
        }

        private static Trigger Item(int id, int amount)
        {
            Trigger t = new Trigger();
            t.Kind = TriggerKind.Item;
            t.ItemId = id;
            t.Compare = Comparison.AtLeast;
            t.Amount = amount;
            return t;
        }

        private static readonly Vector3 Far = new Vector3(1000f, 0f, 0f);

        private static CheckpointState State(int index, string route, float resume, params float[] splits)
        {
            CheckpointState s = new CheckpointState();
            s.Index = index;
            s.Route = route;
            s.CapturedUtc = new DateTime(2026, 10, 4, 12, 30, 15, DateTimeKind.Utc);
            s.ResumeAt = resume;
            s.Splits = splits;
            return s;
        }

        // --- the meta file ---------------------------------------------------

        [Fact]
        public void MetaRoundTrips()
        {
            CheckpointState s = State(1, "abc123", 41.25f, 12.5f, 40.125f);
            s.Baseline.Add(new KeyValuePair<int, int>(210, 1));
            s.Baseline.Add(new KeyValuePair<int, int>(48, 0));

            string error;
            CheckpointState back = CheckpointState.Parse(s.Write(), out error);
            Assert.Null(error);
            Assert.Equal(1, back.Index);
            Assert.Equal("abc123", back.Route);
            Assert.Equal(s.CapturedUtc, back.CapturedUtc);
            Assert.Equal(41.25f, back.ResumeAt, 3);
            Assert.Equal(2, back.Splits.Length);
            Assert.Equal(40.125f, back.Splits[1], 3);
            Assert.Equal(2, back.Baseline.Count);
            Assert.Equal(210, back.Baseline[0].Key);
            Assert.Equal(1, back.Baseline[0].Value);
            Assert.Contains("checkpoint = 2", s.Write());   // 1-based in the file
        }

        [Fact]
        public void MetaWithCrLfAndNoBaselineParses()
        {
            string text = "# x\r\ncheckpoint = 1\r\nroute = r\r\ncaptured = 20261004T120000Z\r\nresume = 3.5\r\nsplits = 3.000\r\nbaseline = \r\n";
            string error;
            CheckpointState s = CheckpointState.Parse(text, out error);
            Assert.Null(error);
            Assert.Equal(0, s.Index);
            Assert.Empty(s.Baseline);
        }

        [Fact]
        public void DamagedMetaIsRefusedWithAReason()
        {
            string error;
            Assert.Null(CheckpointState.Parse("route = r\nresume = 1\nsplits = 1\ncaptured = 20261004T120000Z\n", out error));
            Assert.Contains("checkpoint", error);
            Assert.Null(CheckpointState.Parse("checkpoint = 1\nresume = x\nsplits = 1\ncaptured = 20261004T120000Z\n", out error));
            Assert.Contains("resume", error);
            Assert.Null(CheckpointState.Parse("checkpoint = 0\nresume = 1\nsplits = 1\ncaptured = 20261004T120000Z\n", out error));
            Assert.Null(CheckpointState.Parse("", out error));
        }

        // --- usable for this route -------------------------------------------

        [Fact]
        public void UsableOnTheSameRoute()
        {
            Assert.Null(State(1, "r", 41f, 12f, 40f).Check("r", 3));
        }

        [Fact]
        public void AnotherRouteIsNotUsable()
        {
            Assert.Contains("earlier version", State(1, "old", 41f, 12f, 40f).Check("new", 3));
        }

        [Fact]
        public void ACheckpointTheSegmentNoLongerHasIsNotUsable()
        {
            Assert.Contains("1 checkpoint", State(1, "r", 41f, 12f, 40f).Check("r", 1));
        }

        [Fact]
        public void BrokenTimesAreNotUsable()
        {
            Assert.NotNull(State(1, "r", 41f, 12f).Check("r", 3));            // one split short
            Assert.NotNull(State(1, "r", 41f, 40f, 12f).Check("r", 3));       // out of order
            Assert.NotNull(State(1, "r", 39f, 12f, 40f).Check("r", 3));       // resumes before its split
        }

        // --- which files exist -------------------------------------------------

        [Fact]
        public void FileNamesMapToCheckpointIndices()
        {
            Assert.Equal("s-0123.cp1", CheckpointFiles.BaseName("s-0123", 0));
            Assert.Equal(0, CheckpointFiles.IndexOf("s-0123.cp1.fosave", "s-0123", ".fosave"));
            Assert.Equal(11, CheckpointFiles.IndexOf("S-0123.CP12.meta", "s-0123", ".meta"));
            Assert.Equal(-1, CheckpointFiles.IndexOf("s-0123.fosave", "s-0123", ".fosave"));       // the start state
            Assert.Equal(-1, CheckpointFiles.IndexOf("s-0123.cp0.fosave", "s-0123", ".fosave"));
            Assert.Equal(-1, CheckpointFiles.IndexOf("s-0123.cp01.fosave", "s-0123", ".fosave"));
            Assert.Equal(-1, CheckpointFiles.IndexOf("s-0123.cpx.fosave", "s-0123", ".fosave"));
            Assert.Equal(-1, CheckpointFiles.IndexOf("s-01234.cp1.fosave", "s-0123", ".fosave"));  // another segment
            Assert.Equal(-1, CheckpointFiles.IndexOf("s-0123.cp1.fosave", "s-0123", ".meta"));
        }

        [Fact]
        public void OnlyStatesWithTheirMetaAreComplete()
        {
            List<string> files = new List<string>
            {
                "s-1.fosave", "s-1.cp3.fosave", "s-1.cp3.meta", "s-1.cp1.fosave", "s-1.cp1.meta",
                "s-1.cp2.fosave",            // its meta never written: a write cut short
                "s-1.cp4.meta",              // no state
                "s-2.cp1.fosave", "s-2.cp1.meta",
            };
            Assert.Equal(new List<int> { 0, 2 }, CheckpointFiles.Complete(files, "s-1", ".fosave"));
        }

        [Fact]
        public void HotkeyPicksTheLastUsedElseTheLatest()
        {
            List<int> usable = new List<int> { 0, 2 };
            Assert.Equal(0, CheckpointFiles.PickForHotkey(usable, 0));
            Assert.Equal(2, CheckpointFiles.PickForHotkey(usable, 1));   // 1 has no state
            Assert.Equal(2, CheckpointFiles.PickForHotkey(usable, -1));
            Assert.Equal(-1, CheckpointFiles.PickForHotkey(new List<int>(), 0));
        }

        // --- the split sequence, resumed ---------------------------------------

        [Fact]
        public void ResumedSequenceWatchesTheNextCheckpoint()
        {
            SplitSequence s = new SplitSequence();
            s.Resume(new List<Trigger> { Zone(50f, 2f), Zone(70f, 2f) }, Zone(100f, 2f), 1);

            Assert.Equal(1, s.Next);
            // The first checkpoint counts as fired: walking into it does nothing.
            Assert.Equal(SplitEvent.None, s.Evaluate(new Vector3(50f, 0f, 0f), null, null, null));
            // The end still waits for the second.
            Assert.Equal(SplitEvent.EndBlocked, s.Evaluate(new Vector3(100f, 0f, 0f), null, null, null));
            Assert.Equal(SplitEvent.Split, s.Evaluate(new Vector3(70f, 0f, 0f), null, null, null));
            Assert.Equal(SplitEvent.Finished, s.Evaluate(new Vector3(100f, 0f, 0f), null, null, null));
        }

        [Fact]
        public void ResumedAtTheLastCheckpointOnlyTheEndIsLeft()
        {
            SplitSequence s = new SplitSequence();
            s.Resume(new List<Trigger> { Zone(50f, 2f) }, Zone(100f, 2f), 1);
            Assert.True(s.OnlyEndLeft);
            Assert.Equal(SplitEvent.Finished, s.Evaluate(new Vector3(100f, 0f, 0f), null, null, null));
        }

        [Fact]
        public void ResumedNextTriggerFiresIfItAlreadyHolds()
        {
            // As in the run that captured it: the checkpoint after the
            // resume point becomes current "because the last one fired", so
            // a keycard already in the bag splits at once.
            FakeItems items = new FakeItems().Set(210, 1);
            SplitSequence s = new SplitSequence();
            s.Resume(new List<Trigger> { Zone(50f, 2f), Item(210, 1) }, Zone(100f, 2f), 1);
            Assert.Equal(SplitEvent.Split, s.Evaluate(Far, items, null, null));
        }

        [Fact]
        public void ResumeFromNothingIsAPlainBegin()
        {
            // A zone current at the start must be entered - the Begin rule.
            SplitSequence s = new SplitSequence();
            s.Resume(new List<Trigger> { Zone(0f, 5f) }, Zone(100f, 2f), 0);
            Assert.Equal(SplitEvent.None, s.Evaluate(new Vector3(1f, 0f, 0f), null, null, null));
        }

        [Fact]
        public void ResumeClampsItsCount()
        {
            SplitSequence s = new SplitSequence();
            s.Resume(new List<Trigger> { Zone(50f, 2f) }, Zone(100f, 2f), 7);
            Assert.True(s.OnlyEndLeft);
            Assert.Equal(1, s.Next);
        }

        // --- the recorder, resumed -------------------------------------------------

        [Fact]
        public void RecorderResumesItsClockAtTheCheckpointTime()
        {
            RunRecorder rec = new RunRecorder();
            rec.Arm(Vector3.zero, "s-1");
            rec.Resume(new Vector3(5f, 0f, 0f), 41.25f);

            Assert.Equal(RunRecorder.RunState.Running, rec.State);
            Assert.Equal(41.25f, rec.Elapsed, 3);
            Assert.Single(rec.Current.Samples);
            Assert.Equal(41.25f, rec.Current.Samples[0].T, 3);
            Assert.Equal(5f, rec.Current.Samples[0].P.x, 3);

            rec.Tick(new Vector3(6f, 0f, 0f), 1f, 0.5f);
            Assert.Equal(41.75f, rec.Elapsed, 3);
            Assert.Equal(2, rec.Current.Samples.Count);
            Assert.Equal(41.75f, rec.Current.Samples[1].T, 3);

            Attempt done = rec.Finish();
            Assert.Equal(41.75f, done.Duration, 3);
        }

        [Fact]
        public void RecorderResumesOnlyWhenArmed()
        {
            RunRecorder rec = new RunRecorder();
            rec.Resume(Vector3.zero, 10f);
            Assert.Equal(RunRecorder.RunState.Idle, rec.State);
        }

        // --- golds yes, a PB never ------------------------------------------------

        [Fact]
        public void OnlySegmentsRunAfterTheResumeAreLive()
        {
            // Resumed after checkpoint 2 (index 1): rows 0 and 1 are copied
            // from the run that captured it.
            float[] times = { 10f, 25f, 33f, 50f };
            Assert.True(float.IsNaN(PracticeGolds.LiveSegment(times, 1, 0)));
            Assert.True(float.IsNaN(PracticeGolds.LiveSegment(times, 1, 1)));
            Assert.Equal(8f, PracticeGolds.LiveSegment(times, 1, 2), 3);
            Assert.Equal(17f, PracticeGolds.LiveSegment(times, 1, 3), 3);
            float[] notYet = { 10f, 25f, float.NaN, float.NaN };
            Assert.True(float.IsNaN(PracticeGolds.LiveSegment(notYet, 1, 2)));
        }

        private static Attempt Run(float duration, params float[] splits)
        {
            Attempt a = new Attempt();
            a.Completed = true;
            a.Duration = duration;
            a.Splits = splits;
            a.RecordedUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            return a;
        }

        private static PracticeSegment Seg(string route, int row, float seconds)
        {
            PracticeSegment p = new PracticeSegment();
            p.Route = route;
            p.Row = row;
            p.Seconds = seconds;
            p.Utc = new DateTime(2026, 10, 4, 9, 0, 0, DateTimeKind.Utc);
            return p;
        }

        [Fact]
        public void PracticeSegmentsLowerGoldsButNeverThePb()
        {
            // Full runs: segments 10 / 15 / 20 at best, PB 45.
            List<Attempt> runs = new List<Attempt> { Run(45f, 10f, 25f), Run(50f, 12f, 30f) };
            SplitStats st = SplitStats.Build(runs, 2);
            Assert.Equal(45f, st.SumOfBest, 3);

            List<PracticeSegment> practice = new List<PracticeSegment>
            {
                Seg("r", 2, 18f),     // a gold on the last segment
                Seg("r", 1, 15f),     // a tie: not a new gold
                Seg("old", 1, 5f),    // another route: ignored
                Seg("r", 9, 1f),      // a row the segment does not have
            };
            int golds = PracticeGolds.Apply(st, practice, "r");

            Assert.Equal(1, golds);
            Assert.Equal(18f, st.BestSegments[2], 3);
            Assert.Equal(new DateTime(2026, 10, 4, 9, 0, 0, DateTimeKind.Utc), st.BestSegmentSetUtc[2]);
            Assert.Equal(15f, st.BestSegments[1], 3);
            Assert.Equal(43f, st.SumOfBest, 3);
            Assert.Equal(43f, st.BestSegmentSplits[2], 3);
            // The PB and its splits are full runs' only.
            Assert.Equal(45f, st.Pb, 3);
            Assert.Equal(25f, st.PbSplits[1], 3);
        }

        [Fact]
        public void PracticeSegmentsGiveGoldsWithNoFullRunYet()
        {
            SplitStats st = SplitStats.Build(new List<Attempt>(), 1);
            Assert.Equal(1, PracticeGolds.Apply(st, new List<PracticeSegment> { Seg("r", 1, 7f) }, "r"));
            Assert.Equal(7f, st.BestSegments[1], 3);
            Assert.True(float.IsNaN(st.Pb));
            Assert.True(float.IsNaN(st.SumOfBest));   // row 0 still unknown
        }

        [Fact]
        public void PracticeSegmentLinesRoundTrip()
        {
            PracticeSegment p = Seg("abc", 3, 12.345f);
            PracticeSegment back;
            Assert.True(PracticeSegment.TryParse(p.Write() + "\r", out back));
            Assert.Equal("abc", back.Route);
            Assert.Equal(3, back.Row);
            Assert.Equal(12.345f, back.Seconds, 3);
            Assert.Equal(p.Utc, back.Utc);

            List<PracticeSegment> all = PracticeSegment.ParseAll(p.Write() + "\n# note\n\nbroken line\n" + Seg("abc", 0, 1f).Write() + "\n");
            Assert.Equal(2, all.Count);
            Assert.False(PracticeSegment.TryParse("20261004T090000Z\tr\t1\t-2", out back));
        }
    }
}
