using System;
using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // The splits panel's timer (T-0336): its characters read as the table's
    // own times (SplitTable.Time), and its colour follows LiveSplit's Timer.
    public class TimerTextTests
    {
        private static string Text(float t, int decimals)
        {
            char[] buf = new char[TimerText.MaxLength];
            return new string(buf, 0, TimerText.Write(buf, t, decimals));
        }

        [Fact]
        public void ReadsAsTheSplitTimes()
        {
            float[] values = { 0f, 0.004f, 0.5f, 9.995f, 11.197f, 59.999f, 60f, 65.25f, 599.5f, 3599.999f, 3600f, 7322.25f, 360000f };
            for (int d = 0; d <= 3; d++)
                foreach (float v in values)
                    Assert.Equal(SplitTable.Time(v, d).Replace(",", "."), Text(v, d));
            Assert.Equal("11.197", Text(11.197f, 3));
            Assert.Equal("1:05.25", Text(65.25f, 2));
            Assert.Equal("2:02:02.3", Text(7322.3f, 1));
        }

        [Fact]
        public void EveryValueMatchesAtEachAccuracy()
        {
            Random r = new Random(11);
            for (int i = 0; i < 5000; i++)
            {
                float v = (float)(r.NextDouble() * (i % 4 == 0 ? 20000.0 : 120.0));
                int d = i % 4;
                Assert.Equal(SplitTable.Time(v, d).Replace(",", "."), Text(v, d));
            }
        }

        [Fact]
        public void UnknownNegativeAndHugeValuesStayInTheBuffer()
        {
            Assert.Equal("0.00", Text(float.NaN, 2));
            Assert.Equal("0.00", Text(-3f, 2));
            Assert.Equal("0.00", Text(float.PositiveInfinity, 2));
            Assert.True(Text(float.MaxValue, 3).Length <= TimerText.MaxLength);
            Assert.Equal("0", Text(0.2f, 0));
            Assert.Equal("1.000", Text(1f, 9));   // decimals clamp to 3
        }

        // --- the colour ---------------------------------------------------------

        private static Attempt Run(float duration, params float[] splits)
        {
            Attempt a = new Attempt();
            a.Duration = duration;
            a.Completed = true;
            a.Splits = splits;
            return a;
        }

        private const float N = float.NaN;

        private static SplitColour Colour(float[] times, bool running, float elapsed)
        {
            // PB: 10, 20, 30 (segments of 10).
            SplitStats st = SplitStats.Build(new List<Attempt> { Run(30f, 10f, 20f) }, 2);
            SplitRow[] rows = new SplitRow[3];
            SplitTable.Fill(st, st.PbSplits, times, running, elapsed, rows);
            return SplitTable.TimerColour(rows, times, running, elapsed);
        }

        [Fact]
        public void RunningIsAheadOrBehindAtTheCurrentRow()
        {
            // Row 0 at 8 s of 10: ahead, and gaining on the row so far.
            Assert.Equal(SplitColour.AheadGaining, Colour(new[] { N, N, N }, true, 8f));
            // Row 1 split at 9 (ahead by 1), 12 s in: the row so far is 3 of 10 - gaining.
            Assert.Equal(SplitColour.AheadGaining, Colour(new[] { 9f, N, N }, true, 12f));
            // Split at 9, now 19.5: still ahead (-0.5) but the row took 10.5 of 10 - losing.
            Assert.Equal(SplitColour.AheadLosing, Colour(new[] { 9f, N, N }, true, 19.5f));
            // Split at 12 (behind by 2), now 21: behind (+1), the row so far 9 of 10 - gaining.
            Assert.Equal(SplitColour.BehindGaining, Colour(new[] { 12f, N, N }, true, 21f));
            // Split at 12, now 23: behind, the row 11 of 10 - losing.
            Assert.Equal(SplitColour.BehindLosing, Colour(new[] { 12f, N, N }, true, 23f));
        }

        [Fact]
        public void BetweenRunsItIsTheFinishedRunsOrNone()
        {
            Assert.Equal(SplitColour.AheadGaining, Colour(new[] { 9f, 19f, 29f }, false, 0f));
            Assert.Equal(SplitColour.BehindLosing, Colour(new[] { 9f, 19f, 31f }, false, 0f));
            // Cut short, or not started: no colour.
            Assert.Equal(SplitColour.None, Colour(new[] { 9f, N, N }, false, 0f));
            Assert.Equal(SplitColour.None, Colour(new[] { N, N, N }, false, 0f));
        }

        [Fact]
        public void NoComparisonOrNoRowsIsNone()
        {
            SplitStats st = SplitStats.Build(new List<Attempt>(), 1);
            SplitRow[] rows = new SplitRow[2];
            float[] times = { N, N };
            SplitTable.Fill(st, st.PbSplits, times, true, 5f, rows);
            Assert.Equal(SplitColour.None, SplitTable.TimerColour(rows, times, true, 5f));
            Assert.Equal(SplitColour.None, SplitTable.TimerColour(new SplitRow[0], new float[0], true, 5f));
            Assert.Equal(SplitColour.None, SplitTable.TimerColour(null, null, false, 0f));
        }
    }
}
