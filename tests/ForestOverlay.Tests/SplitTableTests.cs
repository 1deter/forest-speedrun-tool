using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    public class SplitTableTests
    {
        private static Attempt Run(float duration, params float[] splits)
        {
            Attempt a = new Attempt();
            a.Duration = duration;
            a.Completed = true;
            a.Splits = splits;
            return a;
        }

        private static float[] Times(params float[] t) { return t; }
        private const float N = float.NaN;

        [Fact]
        public void StatsFindPbGoldsSumOfBestAndAverage()
        {
            // Two checkpoints -> three rows.
            List<Attempt> runs = new List<Attempt>
            {
                Run(30f, 10f, 20f),   // segments 10 10 10
                Run(28f, 12f, 19f),   // segments 12  7  9  (PB)
                Run(33f, 9f, 22f),    // segments  9 13 11
            };
            SplitStats st = SplitStats.Build(runs, 2);

            Assert.Equal(3, st.Rows);
            Assert.Equal(28f, st.Pb);
            Assert.Equal(new[] { 12f, 19f, 28f }, st.PbSplits);
            Assert.Equal(new[] { 9f, 7f, 9f }, st.BestSegments);
            Assert.Equal(25f, st.SumOfBest);
            Assert.Equal(new[] { 9f, 16f, 25f }, st.BestSegmentSplits);
            Assert.Equal(new[] { 9f, 22f, 33f }, st.LastSplits);
            Assert.Equal(10.333f, st.AverageSplits[0], 3);
            Assert.Equal(30.333f, st.AverageSplits[2], 3);
            Assert.Equal(3, st.WithSplits);
        }

        [Fact]
        public void OldAttemptsWithoutSplitsCountOnlyForTheirTotal()
        {
            List<Attempt> runs = new List<Attempt> { Run(25f), Run(30f, 10f, 20f) };
            SplitStats st = SplitStats.Build(runs, 2);

            Assert.Equal(25f, st.Pb);
            Assert.True(float.IsNaN(st.PbSplits[0]));
            Assert.Equal(25f, st.PbSplits[2]);
            // Golds come only from the attempt that has splits.
            Assert.Equal(new[] { 10f, 10f, 10f }, st.BestSegments);
            Assert.Equal(1, st.WithSplits);
        }

        [Fact]
        public void NoCheckpointsIsOneRowWhoseGoldIsTheBestTotal()
        {
            SplitStats st = SplitStats.Build(new List<Attempt> { Run(12f), Run(11f) }, 0);
            Assert.Equal(1, st.Rows);
            Assert.Equal(11f, st.BestSegments[0]);
            Assert.Equal(11f, st.SumOfBest);
        }

        [Fact]
        public void FinishedRowsGetDeltasAndColours()
        {
            SplitStats st = SplitStats.Build(new List<Attempt> { Run(30f, 10f, 20f) }, 2);
            SplitRow[] rows = new SplitRow[3];
            // Row 0: 9 vs 10 -> ahead, and a gold (9 < 10).
            // Row 1: 21 vs 20 (segment 12 vs 10) -> behind, losing.
            SplitSummary sum = SplitTable.Fill(st, st.PbSplits, Times(9f, 21f, N), true, 25f, rows);

            Assert.Equal(-1f, rows[0].Delta, 3);
            Assert.Equal(SplitColour.Gold, rows[0].Colour);
            Assert.Equal(1f, rows[1].Delta, 3);
            Assert.Equal(2f, rows[1].SegmentDelta, 3);
            Assert.Equal(SplitColour.BehindLosing, rows[1].Colour);
            Assert.True(rows[2].Current);
            Assert.Equal(2f, sum.PreviousSegment, 3);
        }

        [Fact]
        public void TheCurrentRowShowsALiveDeltaOnlyWhenBehindOrLosing()
        {
            SplitStats st = SplitStats.Build(new List<Attempt> { Run(30f, 10f, 20f) }, 2);
            SplitRow[] rows = new SplitRow[3];

            // 5 s into row 0 (comparison 10): ahead and not losing - blank.
            SplitTable.Fill(st, st.PbSplits, Times(N, N, N), true, 5f, rows);
            Assert.True(float.IsNaN(rows[0].Delta));
            Assert.False(rows[0].Live);

            // 12 s in: 2 s behind, live.
            SplitTable.Fill(st, st.PbSplits, Times(N, N, N), true, 12f, rows);
            Assert.True(rows[0].Live);
            Assert.Equal(2f, rows[0].Delta, 3);
            Assert.Equal(SplitColour.BehindLosing, rows[0].Colour);

            // Ahead overall (split at 7) but row 1 already 11 s long (comparison 10): live, ahead-losing.
            SplitTable.Fill(st, st.PbSplits, Times(7f, N, N), true, 18f, rows);
            Assert.True(rows[1].Live);
            Assert.Equal(-2f, rows[1].Delta, 3);
            Assert.Equal(SplitColour.AheadLosing, rows[1].Colour);
        }

        [Fact]
        public void BestPossibleAndCurrentPace()
        {
            // Golds 9 7 9 (sum of best 25), PB 12 19 28.
            List<Attempt> runs = new List<Attempt> { Run(30f, 10f, 20f), Run(28f, 12f, 19f), Run(33f, 9f, 22f) };
            SplitStats st = SplitStats.Build(runs, 2);
            SplitRow[] rows = new SplitRow[3];

            // Split 1 at 11 (PB 12), 3 s into row 1.
            SplitSummary sum = SplitTable.Fill(st, st.PbSplits, Times(11f, N, N), true, 14f, rows);
            Assert.Equal(11f + 7f + 9f, sum.BestPossible, 3);
            Assert.Equal(11f + (28f - 12f), sum.CurrentPace, 3);
            // Row 1's possible save vs PB: 7 (PB segment) - 7 (gold) = 0.
            Assert.Equal(0f, sum.PossibleSave, 3);

            // 10 s into row 1: longer than its gold (7), so best possible uses 10.
            sum = SplitTable.Fill(st, st.PbSplits, Times(11f, N, N), true, 21f, rows);
            Assert.Equal(11f + 10f + 9f, sum.BestPossible, 3);
        }

        [Fact]
        public void BestSegmentsIsAComparisonLikeAnyOther()
        {
            List<Attempt> runs = new List<Attempt> { Run(30f, 10f, 20f), Run(28f, 12f, 19f) };
            SplitStats st = SplitStats.Build(runs, 2);
            SplitRow[] rows = new SplitRow[3];
            SplitTable.Fill(st, st.BestSegmentSplits, Times(10f, 18f, 27f), false, 27f, rows);
            // Golds 10 7 9 -> 10 17 26.
            Assert.Equal(0f, rows[0].Delta, 3);
            Assert.Equal(1f, rows[1].Delta, 3);
            Assert.Equal(1f, rows[2].Delta, 3);
        }

        [Fact]
        public void TimeAndDeltaText()
        {
            Assert.Equal("9.50", SplitTable.Time(9.5f));
            Assert.Equal("1:02.34", SplitTable.Time(62.34f));
            Assert.Equal("1:00:05.2", SplitTable.Time(3605.2f));
            Assert.Equal("-", SplitTable.Time(float.NaN));
            Assert.Equal("+1.23", SplitTable.Delta(1.234f));
            Assert.Equal("-0.45", SplitTable.Delta(-0.45f));
            Assert.Equal("+1:02.3", SplitTable.Delta(62.3f));
            Assert.Equal("", SplitTable.Delta(float.NaN));
        }
    }

    public class AttemptFormatTests
    {
        [Fact]
        public void SplitsAndRunnerRoundTrip()
        {
            Attempt a = new Attempt();
            a.AnchorLabel = "s-0123456789ab";
            a.Duration = 28.5f;
            a.Route = "deadbeef";
            a.Splits = new[] { 10.25f, 19.5f };
            a.RunnerId = "r-00112233aabbccdd";
            a.RunnerName = "de|ter";
            a.Samples.Add(new RunSample { T = 0f, P = new Vector3(1, 2, 3), Speed = 4f });

            Attempt b = AttemptFormat.Parse(AttemptFormat.Write(a).Split('\n'));

            Assert.Equal(a.Splits, b.Splits);
            Assert.Equal("r-00112233aabbccdd", b.RunnerId);
            Assert.Equal("de ter", b.RunnerName);   // the separator cannot survive
            Assert.Equal("deadbeef", b.Route);
            Assert.Equal(28.5f, b.Duration);
            Assert.Single(b.Samples);
        }

        [Fact]
        public void OldFilesHaveNoSplitsOrRunner()
        {
            string[] old = { "anchor|x", "duration|12.000", "s|0.000|1.000|2.000|3.000|0.000" };
            Attempt b = AttemptFormat.Parse(old);
            Assert.Empty(b.Splits);
            Assert.Equal("", b.RunnerId);
        }
    }
}
