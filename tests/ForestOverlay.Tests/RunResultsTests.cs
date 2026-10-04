using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class RunResultsTests
    {
        private const float N = float.NaN;

        private static Attempt Run(float duration, params float[] splits)
        {
            Attempt a = new Attempt();
            a.Duration = duration;
            a.Completed = true;
            a.Splits = splits;
            return a;
        }

        private static readonly string[] Names = { "Cave", "Rope", "End" };

        // Before this run: PB 28 (12 / 19 / 28), golds 9 / 7 / 9, sum of best 25.
        private static SplitStats Before()
        {
            return SplitStats.Build(new List<Attempt> { Run(30f, 10f, 20f), Run(28f, 12f, 19f), Run(33f, 9f, 22f) }, 2);
        }

        [Fact]
        public void NewPbWithGoldsAndSavedLostPerSplit()
        {
            SplitStats st = Before();
            // Segments 11 / 6 / 9.5: row 2 is a gold (6 < 7), row 3 is not (9.5 > 9).
            float[] times = { 11f, 17f, 26.5f };
            RunResult r = RunResults.Build(st, st.PbSplits, true, "Personal best", times, Names, 2, 2);

            Assert.True(r.NewPb);
            Assert.False(r.FirstFinish);
            Assert.Equal("26.50", r.Headline);
            Assert.Equal("New personal best by 1.50 s! (was 28.00)", r.Verdict);
            Assert.Equal("", r.CompareLine);   // the comparison is the PB: the verdict says it
            Assert.Equal(1, r.Golds);
            Assert.Equal("Gold: Rope.", r.GoldLine);
            // Sum of best with this run's gold: 9 + 6 + 9 = 24.
            Assert.Equal(24f, r.SumOfBestAfter);
            Assert.Equal("24.00", r.BestPossible);

            Assert.Equal(3, r.Rows.Length);
            Assert.Equal("Cave", r.Rows[0].Name);
            Assert.Equal("11.00", r.Rows[0].Time);
            Assert.Equal("-1.00", r.Rows[0].Delta);        // 11 vs 12
            Assert.Equal("saved 1.00", r.Rows[0].SavedLost);
            Assert.True(r.Rows[0].Saved);
            Assert.True(r.Rows[1].Gold);
            Assert.Equal(SplitColour.Gold, r.Rows[1].Colour);
            Assert.Equal("6.00", r.Rows[1].Segment);
            Assert.Equal("lost 0.50", r.Rows[2].SavedLost); // 9.5 vs the PB's 9
            Assert.True(r.Rows[2].Lost);
            Assert.False(r.Rows[2].Gold);
        }

        [Fact]
        public void SlowerRunAgainstAnotherComparison()
        {
            SplitStats st = Before();
            float[] times = { 12f, 20f, 30f };
            RunResult r = RunResults.Build(st, st.BestSegmentSplits, false, "Best segments", times, Names, 2, 2);

            Assert.False(r.NewPb);
            Assert.Equal("2.00 s behind your personal best (28.00).", r.Verdict);
            Assert.Equal("vs Best segments: +5.00", r.CompareLine);
            Assert.Equal("No golds this run.", r.GoldLine);
            Assert.Equal(25f, r.SumOfBestAfter);
            Assert.Equal("+3.00", r.Rows[0].Delta);   // 12 vs the gold split 9
        }

        [Fact]
        public void FirstFinishedRunAndTie()
        {
            SplitStats empty = SplitStats.Build(new List<Attempt>(), 0);
            RunResult first = RunResults.Build(empty, empty.PbSplits, true, "Personal best", new[] { 40f }, new[] { "End" }, 2, 2);
            Assert.True(first.FirstFinish);
            Assert.True(first.NewPb);
            Assert.Equal("First finished run - your personal best.", first.Verdict);
            Assert.Equal("", first.GoldLine);   // one row: no gold line
            Assert.Equal("", first.Rows[0].Delta);
            Assert.Equal("40.00", first.BestPossible);

            SplitStats one = SplitStats.Build(new List<Attempt> { Run(40f) }, 0);
            RunResult tie = RunResults.Build(one, one.PbSplits, true, "Personal best", new[] { 40.0001f }, null, 2, 2);
            Assert.True(tie.Tied);
            Assert.False(tie.NewPb);
            Assert.Equal("Tied your personal best (40.00).", tie.Verdict);
            Assert.Equal("End", tie.Rows[0].Name);
        }

        [Fact]
        public void WordsForTimesAndDeltas()
        {
            Assert.Equal("New personal best by 1:05.00! (was 3:00.00)", RunResults.PbVerdict(115f, 180f, 2));
            Assert.Equal("even", RunResults.SavedLost(0f, 2));
            Assert.Equal("", RunResults.SavedLost(N, 2));
            Assert.Equal("saved 0.4", RunResults.SavedLost(-0.4f, 1));
            Assert.Equal("vs Last run: no time to compare yet.", RunResults.CompareText("Last run", 10f, N, 2));
            Assert.True(float.IsNaN(RunResults.SumOfBestAfter(new[] { N, 5f }, new[] { N, 10f })));
            Assert.Equal(7f, RunResults.SumOfBestAfter(new[] { N, 5f }, new[] { 2f, 10f }));
        }

        [Fact]
        public void BadInputGivesAnEmptyResult()
        {
            SplitStats st = Before();
            RunResult r = RunResults.Build(st, null, false, "x", new[] { 1f }, Names, 2, 2);
            Assert.Empty(r.Rows);
            Assert.Equal("", r.Verdict);
            // A comparison of the wrong length counts as none.
            RunResult r2 = RunResults.Build(st, new[] { 1f }, false, "Runner", new[] { 11f, 17f, 26.5f }, Names, 2, 2);
            Assert.Equal("vs Runner: no time to compare yet.", r2.CompareLine);
            Assert.Equal("", r2.Rows[0].Delta);
        }

        [Fact]
        public void UploadWordsAndRunModeLines()
        {
            Assert.Equal("sent - green: checked online", RunResults.UploadWords(AttemptUpload.Sent, "green"));
            Assert.Equal("waiting to be sent", RunResults.UploadWords(AttemptUpload.Waiting, null));
            Assert.Equal("refused by the site: HTTP 409", RunResults.UploadWords(AttemptUpload.Refused, "HTTP 409"));
            Assert.Equal("", RunResults.UploadWords(AttemptUpload.Unknown, null));

            RunModeOutcome o = new RunModeOutcome();
            o.Attempt = 3;
            o.Label = "Any%";
            o.AttemptId = "a-1";
            o.Code = "K7Q2";
            o.Online = true;
            o.SendOn = true;
            o.Report = "clean (Steam game, no other mods, no cheats)";
            List<string> lines = RunResults.RunModeLines(o, "", "https://site/attempt/a-1");
            Assert.Equal(new[]
            {
                "Run mode attempt 3 (Any%) - last code K7Q2, online (the site has its start code).",
                "Report: clean (Steam game, no other mods, no cheats).",
                "Receipt: waiting to be sent.",
                "Attempt page: https://site/attempt/a-1",
            }, lines.ToArray());

            o.Online = false;
            o.SendOn = false;
            o.Code = AttemptChain.NoCode;
            lines = RunResults.RunModeLines(o, "sent - green: checked online", "x");
            Assert.Equal("Run mode attempt 3 (Any%), offline - checked by the video's codes only.", lines[0]);
            Assert.Equal("Receipt: not sent - sending attempts is off (the log is in run-reports).", lines[2]);
            Assert.Equal(3, lines.Count);
            Assert.Empty(RunResults.RunModeLines(null, "", ""));
        }
    }
}
