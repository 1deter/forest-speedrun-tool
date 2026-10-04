using System;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // Load-removed time (Data/LoadTimes): the timer minus the loads it
    // counted, carried by the .run file and run mode's attempt log - old
    // files and old logs must read (and verify) exactly as before.
    // ------------------------------------------------------------------
    public class LoadTimesTests
    {
        [Fact]
        public void ClockCountsOnlyTheTimeTheTimerCountedWhileLoading()
        {
            LoadClock c = new LoadClock();
            c.Tick(1f, false);
            c.Tick(0.5f, true);    // a load starts
            c.Tick(2f, true);      // a frozen frame inside it
            c.Tick(0f, true);      // a frame the timer did not advance (no player)
            c.Tick(1f, false);     // over
            Assert.Equal(1, c.Loads);
            Assert.Equal(2.5f, c.LoadTime, 4);
            Assert.False(c.InLoad);
            Assert.Equal(2f, c.Lrt(4.5f), 4);

            c.Tick(0.25f, true);   // a second load
            c.Tick(0.25f, false);
            Assert.Equal(2, c.Loads);
            Assert.Equal(2.75f, c.LoadTime, 4);
            Assert.Equal(2.75, c.Ever, 4);

            c.Reset();             // the next run
            Assert.Equal(0, c.Loads);
            Assert.Equal(0f, c.LoadTime);
            Assert.Equal(2.75, c.Ever, 4);   // never reset: run mode's lines take differences
            c.Tick(1f, true);
            Assert.Equal(3.75, c.Ever, 4);
        }

        [Fact]
        public void WithoutNeverGoesBelowZeroAndKeepsUnknown()
        {
            Assert.Equal(7f, LoadClock.Without(10f, 3f), 4);
            Assert.Equal(0f, LoadClock.Without(1f, 3f));
            Assert.True(float.IsNaN(LoadClock.Without(float.NaN, 1f)));
            Assert.True(float.IsNaN(LoadClock.Without(1f, float.NaN)));
        }

        [Fact]
        public void DescribeSaysHowManyAndHowLong()
        {
            Assert.Equal("no loads", LoadClock.Describe(0, 0f));
            Assert.Equal("1 load, 4.25 s", LoadClock.Describe(1, 4.25f));
            Assert.Equal("3 loads, 1:02.50", LoadClock.Describe(3, 62.5f));
        }

        [Fact]
        public void SpanFollowsTheLoadInRealTime()
        {
            LoadSpan s = new LoadSpan();
            LoadSpanInfo info;
            Assert.False(s.Update(false, 100, out info));
            Assert.False(s.Update(true, 200, out info));
            Assert.True(s.InLoad);
            Assert.Equal(200, s.StartMs);
            Assert.False(s.Update(true, 5000, out info));
            Assert.True(s.Update(false, 9200, out info));
            Assert.Equal(200, info.StartMs);
            Assert.Equal(9200, info.EndMs);
            Assert.Equal(9000, info.LengthMs);
            Assert.False(s.InLoad);
            Assert.Equal(-1, s.StartMs);

            // The attempt ends inside a load: cut there.
            s.Update(true, 10000, out info);
            Assert.True(s.Finish(10400, out info));
            Assert.Equal(400, info.LengthMs);
            Assert.False(s.Finish(10500, out info));   // nothing under way
        }

        [Fact]
        public void RecorderStampsTheLoadsOnTheAttempt()
        {
            RunRecorder r = new RunRecorder();
            r.Arm(Vector3.zero, "spot");
            r.ForceStart(Vector3.zero);
            r.Tick(Vector3.zero, 0f, 1f);
            r.LoadingNow = true;
            r.Tick(Vector3.zero, 0f, 3f);
            r.LoadingNow = false;
            r.Tick(Vector3.zero, 0f, 1f);
            Assert.Equal(5f, r.Elapsed, 4);
            Assert.Equal(2f, r.Lrt, 4);   // the real-time timer is untouched; LRT beside it
            Attempt a = r.Finish();
            Assert.True(a.HasLoads);
            Assert.Equal(1, a.Loads);
            Assert.Equal(3f, a.LoadTime, 4);
            Assert.Equal(5f, a.Duration, 4);
            Assert.Equal(2f, LoadClock.LrtOf(a), 4);

            // The next run starts with none.
            r.Arm(Vector3.zero, "spot");
            r.ForceStart(Vector3.zero);
            Assert.Equal(0, r.LoadClock.Loads);
            Assert.Equal(0f, r.Lrt);
        }

        [Fact]
        public void RunFileCarriesTheLoadsAndOldFilesReadAsNone()
        {
            Attempt a = new Attempt();
            a.AnchorLabel = "spot";
            a.Duration = 90f;
            a.Completed = true;
            a.Splits = new[] { 20f, 60f };
            a.HasLoads = true;
            a.Loads = 2;
            a.LoadTime = 12.5f;
            a.SplitLoads = new[] { 0f, 8f };
            a.Samples.Add(new RunSample { T = 0f, P = Vector3.zero });
            string text = AttemptFormat.Write(a);
            Assert.Contains("loads|2|12.500|0.000|8.000\n", text);

            Attempt b = AttemptFormat.Parse(text.Split('\n'));
            Assert.True(b.HasLoads);
            Assert.Equal(2, b.Loads);
            Assert.Equal(12.5f, b.LoadTime, 3);
            Assert.Equal(new[] { 0f, 8f }, b.SplitLoads);
            Assert.Equal(77.5f, LoadClock.LrtOf(b), 3);
            float[] lrt = LoadClock.LrtSplitsOf(b, 3);
            Assert.Equal(20f, lrt[0], 3);
            Assert.Equal(52f, lrt[1], 3);
            Assert.Equal(77.5f, lrt[2], 3);

            // No loads: one short line, no split loads.
            a.Loads = 0; a.LoadTime = 0f; a.SplitLoads = new float[0];
            Assert.Contains("loads|0|0.000\n", AttemptFormat.Write(a));

            // A file from before loads were tracked: no line, LRT = the time.
            string old = "anchor|spot\nduration|90.000\nsplits|20.000|60.000\ns|0.000|0|0|0|0\n";
            Attempt o = AttemptFormat.Parse(old.Split('\n'));
            Assert.False(o.HasLoads);
            Assert.Equal(90f, LoadClock.LrtOf(o), 3);
            Assert.Equal(60f, LoadClock.LrtSplitsOf(o, 3)[1], 3);
            Assert.DoesNotContain("loads|", AttemptFormat.Write(o));   // and it stays that way
        }

        [Fact]
        public void ResultsLineOnlyWhenThereIsSomethingToSay()
        {
            Assert.Equal("", RunResults.LoadLine(80f, 0, 0f, 2, false));
            Assert.Equal("Load-removed time: 1:20.00 (no loads).", RunResults.LoadLine(80f, 0, 0f, 2, true));
            Assert.Equal("Load-removed time: 1:10.00 (1 load, 10.00 s).", RunResults.LoadLine(80f, 1, 10f, 2, false));
            Assert.Equal("", RunResults.LoadLine(float.NaN, 1, 10f, 2, true));
        }

        // --- run mode's attempt log -------------------------------------------------

        private static AttemptChain Header()
        {
            AttemptChain c = new AttemptChain();
            c.Header("a-0123456789abcdef", "r-00000000000000aa", "Runner", "0.24.246", "Any%", "s-0123456789ab", "abc123",
                     "seed0001", new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc));
            return c;
        }

        [Fact]
        public void LoadLinesFoldAndReplay()
        {
            AttemptChain c = Header();
            c.Step(1000, 0, true, 1f, 2f, 3f);
            c.Load(12000, 10500, 9800);      // a Reload save on death
            c.Step(13000, 3200, true, 1f, 2f, 3f);
            c.Load(14000, 400, 0);            // a load the timer did not count
            c.End(20000, "finished", 19000);
            AttemptChain.Replay r = AttemptChain.Read(c.Text);
            Assert.Null(r.Error);
            Assert.Equal(2, r.Loads.Count);
            Assert.Equal(12000, r.Loads[0].RealMs);
            Assert.Equal(10500, r.Loads[0].LengthMs);
            Assert.Equal(9800, r.Loads[0].TimedMs);
            Assert.Equal(9800, r.LoadTimedMs);
            Assert.Equal(19000 - 9800, r.LrtMs);
            Assert.Equal(c.Head, r.FinalHead);
            Assert.Contains("load|12000|10500|9800\n", c.Text);
        }

        [Fact]
        public void LoadLinesAreCheckedLikeTheOthers()
        {
            AttemptChain c = Header();
            c.Step(5000, 0, true, 0f, 0f, 0f);
            string ok = c.Text;
            Assert.Contains("time goes backwards", AttemptChain.Read(ok + "load|4000|100|0\n").Error);
            Assert.Contains("bad load line", AttemptChain.Read(ok + "load|6000|7000|0\n").Error);   // longer than the log
            Assert.Contains("bad load line", AttemptChain.Read(ok + "load|6000|100\n").Error);
            Assert.Contains("bad load line", AttemptChain.Read(ok + "load|6000|-1|0\n").Error);
            Assert.Null(AttemptChain.Read(ok + "load|6000|1000|0\n").Error);
        }

        [Fact]
        public void LoadClampsWhatItWrites()
        {
            AttemptChain c = Header();
            c.Load(500, 900, -5);
            Assert.Contains("load|500|500|0\n", c.Text);
            Assert.Null(AttemptChain.Read(c.Text).Error);
        }

        [Fact]
        public void LogsFromBeforeLoadRemovedTimeStillVerify()
        {
            // An attempt log as v0.24.245 wrote it, with the head the site
            // stored for it: the chain is unchanged by the new line kind.
            string old =
                "forest-attempt 1\n" +
                "attempt|a-0123456789abcdef\n" +
                "runner|r-00000000000000aa|Runner\n" +
                "plugin|0.24.245\n" +
                "category|Any%\n" +
                "spot|s-0123456789ab|abc123\n" +
                "seed|seed0001\n" +
                "started|2026-10-04T12:00:00Z\n" +
                "step|1|1000|0|100|200|300\n" +
                "split|1500|0|500\n" +
                "step|2|2000|1000|-|-|-\n" +
                "end|2100|finished|1100\n";
            AttemptChain.Replay r = AttemptChain.Read(old);
            Assert.Null(r.Error);
            Assert.Empty(r.Loads);
            Assert.Equal(1100, r.LrtMs);   // no loads: the timer itself
            Assert.Equal(OldHead, r.FinalHead);

            // Written today, the same lines give the same head.
            AttemptChain c = Header();
            c.Step(1000, 0, true, 1f, 2f, 3f);
            c.Split(1500, 0, 500);
            c.Step(2000, 1000, false, 0, 0, 0);
            c.End(2100, "finished", 1100);
            string today = c.Text.Replace("plugin|0.24.246", "plugin|0.24.245");
            Assert.Equal(old, today);
        }

        // SHA-256 chain computed outside this code (Python hashlib).
        private const string OldHead = "9040c651f1789035e60eb9be45b5bf9155582d6e02b7b680827a8a8736acb761";
    }
}
