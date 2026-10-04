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
            Assert.Equal("1:00:05.20", SplitTable.Time(3605.2f));
            Assert.Equal("-", SplitTable.Time(float.NaN));
            Assert.Equal("+1.23", SplitTable.Delta(1.234f));
            Assert.Equal("-0.45", SplitTable.Delta(-0.45f));
            Assert.Equal("+1:02.30", SplitTable.Delta(62.3f));
            Assert.Equal("", SplitTable.Delta(float.NaN));
        }

        [Fact]
        public void PrecisionIsZeroToThreeDecimals()
        {
            Assert.Equal("9", SplitTable.Time(9.4f, 0));
            Assert.Equal("9.5", SplitTable.Time(9.46f, 1));
            Assert.Equal("9.457", SplitTable.Time(9.457f, 3));
            Assert.Equal("1:00.000", SplitTable.Time(59.9996f, 3));   // rounds up into the minute
            Assert.Equal("+0.057", SplitTable.Delta(0.0571f, 3));
            Assert.Equal("-1:02", SplitTable.Delta(-62.3f, 0));
            Assert.Equal("9.457", SplitTable.Time(9.457f, 7));       // clamped to 3
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

        [Fact]
        public void WithRunnerNamesAnOldAttemptInItsHeader()
        {
            string old = "anchor|x\r\nduration|12.000\r\nchannels|Health\r\ns|0.000|1.000|2.000|3.000|0.000\r\nv|0.000|100.000\r\n";
            string named = AttemptFormat.WithRunner(old, "r-00112233aabbccdd", "de|ter\nx");

            Assert.Equal("anchor|x\r\nduration|12.000\r\nrunner|r-00112233aabbccdd|de ter x\r\nchannels|Health\r\n" +
                         "s|0.000|1.000|2.000|3.000|0.000\r\nv|0.000|100.000\r\n", named);
            // The header is where the owner check looks (it stops at the samples).
            Assert.Equal("r-00112233aabbccdd", AttemptOwners.RunnerIdOf(named));
            Attempt b = AttemptFormat.Parse(named.Split('\n'));
            Assert.Equal("r-00112233aabbccdd", b.RunnerId);
            Assert.Equal("de ter x", b.RunnerName);
            Assert.Single(b.Samples);
            Assert.Equal(12f, b.Duration);
        }

        [Fact]
        public void WithRunnerNeverRenamesAnAttempt()
        {
            Attempt a = new Attempt();
            a.AnchorLabel = "x";
            a.RunnerId = "r-aaaaaaaaaaaaaaaa";
            a.RunnerName = "maks";
            a.Samples.Add(new RunSample { T = 0f, P = new Vector3(1, 2, 3), Speed = 4f });
            string text = AttemptFormat.Write(a);

            Assert.Same(text, AttemptFormat.WithRunner(text, "r-bbbbbbbbbbbbbbbb", "deter"));
            // A runner line anywhere counts (Parse reads lines in any order).
            string late = "anchor|x\ns|0.000|1.000|2.000|3.000|0.000\nrunner|r-cc|Tom\n";
            Assert.Same(late, AttemptFormat.WithRunner(late, "r-bbbbbbbbbbbbbbbb", "deter"));
            // No id to give: unchanged.
            string old = "anchor|x\ns|0.000|1.000|2.000|3.000|0.000\n";
            Assert.Same(old, AttemptFormat.WithRunner(old, "", "deter"));
            Assert.Same(old, AttemptFormat.WithRunner(old, null, "deter"));
        }

        [Fact]
        public void WithRunnerOnAHeaderOnlyTextAppends()
        {
            Assert.Equal("anchor|x\nrunner|r-a|\n", AttemptFormat.WithRunner("anchor|x", "r-a", null));
            Assert.Equal("anchor|x\nrunner|r-a|n\n", AttemptFormat.WithRunner("anchor|x\n", "r-a", "n"));
        }

        [Fact]
        public void ANamedAttemptSurvivesABundle()
        {
            string old = "anchor|s-0123456789ab\nrecorded|2026-09-20T10:00:00.0000000Z\nduration|12.000\ns|0.000|1.000|2.000|3.000|0.000\n";
            SegmentBundle bundle = new SegmentBundle();
            bundle.Segment = new Segment { Id = "s-0123456789ab", Name = "test" };
            bundle.Attempts.Add(AttemptFormat.WithRunner(old, "r-00112233aabbccdd", "deter"));

            string error;
            SegmentBundle back = SegmentBundle.Parse(bundle.Write(), out error, null);
            Assert.Null(error);
            Attempt b = AttemptFormat.Parse(back.Attempts[0].Split('\n'));
            Assert.Equal("r-00112233aabbccdd", b.RunnerId);
            Assert.Equal("deter", b.RunnerName);
            Assert.False(AttemptOwners.IsOwn(b.RunnerId, "r-ffffffffffffffff"));   // the importer's comparison, not their PB
        }
    }
}
