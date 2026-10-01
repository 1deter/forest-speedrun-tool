using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class LssFileTests
    {
        // Written by hand after LiveSplit 1.8's own output. Four attempts:
        //   1  finished 1500.50   300.00  600.25  450.00  150.25
        //   2  reset after Vault  290.50  620.00
        //   3  finished 1520.00   310.00  (skip)  1060.00 150.00  - Vault Door skipped
        //   4  finished 1480.00   295.00  605.00  440.00  140.00  - the PB
        // Cave 5's gold (285) is below its history: history cleared once.
        private const string Sample = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Run version=""1.8.0"">
  <GameIcon />
  <GameName>The Forest</GameName>
  <CategoryName>Any%</CategoryName>
  <LayoutPath>
  </LayoutPath>
  <Metadata>
    <Run id="""" />
    <Platform usesEmulator=""False"">PC</Platform>
    <Region>
    </Region>
    <Variables />
  </Metadata>
  <Offset>00:00:00</Offset>
  <AttemptCount>4</AttemptCount>
  <AttemptHistory>
    <Attempt id=""1"" started=""09/20/2026 18:02:11"" isStartedSynced=""True"" ended=""09/20/2026 18:27:12"" isEndedSynced=""True"">
      <RealTime>00:25:00.5000000</RealTime>
    </Attempt>
    <Attempt id=""2"" started=""09/20/2026 18:30:00"" isStartedSynced=""True"" ended=""09/20/2026 18:45:10"" isEndedSynced=""True"" />
    <Attempt id=""3"" started=""09/21/2026 10:00:00"" isStartedSynced=""True"" ended=""09/21/2026 10:25:20"" isEndedSynced=""True"">
      <RealTime>00:25:20</RealTime>
    </Attempt>
    <Attempt id=""4"" started=""09/22/2026 20:00:00"" isStartedSynced=""True"" ended=""09/22/2026 20:24:40"" isEndedSynced=""True"">
      <RealTime>00:24:40</RealTime>
      <GameTime>00:24:20</GameTime>
    </Attempt>
  </AttemptHistory>
  <Segments>
    <Segment>
      <Name>Cave 5</Name>
      <Icon />
      <SplitTimes>
        <SplitTime name=""Personal Best"">
          <RealTime>00:04:55</RealTime>
          <GameTime>00:04:50</GameTime>
        </SplitTime>
        <SplitTime name=""WR"">
          <RealTime>00:04:30</RealTime>
        </SplitTime>
      </SplitTimes>
      <BestSegmentTime>
        <RealTime>00:04:45</RealTime>
        <GameTime>00:04:40</GameTime>
      </BestSegmentTime>
      <SegmentHistory>
        <Time id=""1""><RealTime>00:05:00</RealTime></Time>
        <Time id=""2""><RealTime>00:04:50.5000000</RealTime></Time>
        <Time id=""3""><RealTime>00:05:10</RealTime></Time>
        <Time id=""4""><RealTime>00:04:55</RealTime></Time>
      </SegmentHistory>
    </Segment>
    <Segment>
      <Name>Vault Door</Name>
      <Icon />
      <SplitTimes>
        <SplitTime name=""Personal Best"">
          <RealTime>00:15:00</RealTime>
          <GameTime>00:14:50</GameTime>
        </SplitTime>
        <SplitTime name=""WR"">
          <RealTime>00:14:00</RealTime>
        </SplitTime>
      </SplitTimes>
      <BestSegmentTime>
        <RealTime>00:10:00.2500000</RealTime>
      </BestSegmentTime>
      <SegmentHistory>
        <Time id=""1""><RealTime>00:10:00.2500000</RealTime></Time>
        <Time id=""2""><RealTime>00:10:20</RealTime></Time>
        <Time id=""3"" />
        <Time id=""4""><RealTime>00:10:05</RealTime></Time>
      </SegmentHistory>
    </Segment>
    <Segment>
      <Name>Red Elevator</Name>
      <Icon />
      <SplitTimes>
        <SplitTime name=""Personal Best"">
          <RealTime>00:22:20</RealTime>
          <GameTime>00:22:05</GameTime>
        </SplitTime>
        <SplitTime name=""WR"" />
      </SplitTimes>
      <BestSegmentTime>
        <RealTime>00:07:20</RealTime>
      </BestSegmentTime>
      <SegmentHistory>
        <Time id=""1""><RealTime>00:07:30</RealTime></Time>
        <Time id=""3""><RealTime>00:17:40</RealTime></Time>
        <Time id=""4""><RealTime>00:07:20</RealTime></Time>
      </SegmentHistory>
    </Segment>
    <Segment>
      <Name>Game End</Name>
      <Icon />
      <SplitTimes>
        <SplitTime name=""Personal Best"">
          <RealTime>00:24:40</RealTime>
          <GameTime>00:24:20</GameTime>
        </SplitTime>
        <SplitTime name=""WR"">
          <RealTime>00:23:00</RealTime>
        </SplitTime>
      </SplitTimes>
      <BestSegmentTime>
        <RealTime>00:02:20</RealTime>
      </BestSegmentTime>
      <SegmentHistory>
        <Time id=""1""><RealTime>00:02:30.2500000</RealTime></Time>
        <Time id=""3""><RealTime>00:02:30</RealTime></Time>
        <Time id=""4""><RealTime>00:02:20</RealTime></Time>
      </SegmentHistory>
    </Segment>
  </Segments>
  <AutoSplitterSettings />
</Run>";

        private const float N = float.NaN;

        private static LssRun Load(string text)
        {
            string error;
            LssRun run = LssFile.Parse(text, out error);
            Assert.True(run != null, error);
            Assert.Null(error);
            return run;
        }

        private static string Error(string text)
        {
            string error;
            Assert.Null(LssFile.Parse(text, out error));
            Assert.False(string.IsNullOrEmpty(error));
            return error;
        }

        private static void Close(float[] expected, float[] actual)
        {
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                if (float.IsNaN(expected[i])) Assert.True(float.IsNaN(actual[i]), "row " + i + " = " + actual[i]);
                else Assert.Equal(expected[i], actual[i], 3);
            }
        }

        // --- the file ---------------------------------------------------------

        [Fact]
        public void FromMapKeepsTheHandMapAndListsWhatIsLeft()
        {
            string[] rows = { "Cave 2", "Vault", "End" };
            string[] splits = { "Cave 1", "Cave 2", "Cave 5", "Vault Door", "Game End" };
            LssMatch m = LssMatch.FromMap(new[] { 1, 3, 9 }, rows, splits);
            Assert.Equal(new[] { 1, 3, -1 }, m.Map);
            Assert.Equal(0, m.StartAfter);
            Assert.Equal(new[] { "End" }, m.UnmatchedRows.ToArray());
            Assert.Equal(new[] { "Cave 5", "Game End" }, m.UnmatchedSplits.ToArray());
            Assert.True(m.InOrder);
            Assert.False(LssMatch.FromMap(new[] { 3, 1, 4 }, rows, splits).InOrder);
        }

        [Fact]
        public void LinksRoundTrip()
        {
            List<LssLink> links = new List<LssLink>();
            links.Add(new LssLink { SegmentId = "s-0123456789ab", File = "any% pb.lss", Timing = LssTiming.GameTime, Map = new[] { 2, -1, 4 } });
            links.Add(new LssLink { SegmentId = "spot.my.new-spot-3", File = "a.lss" });
            List<LssLink> back = LssLink.ParseAll(LssLink.FormatAll(links).Replace("\n", "\r\n"));
            Assert.Equal(2, back.Count);
            Assert.Equal("any% pb.lss", back[0].File);
            Assert.Equal(LssTiming.GameTime, back[0].Timing);
            Assert.Equal(new[] { 2, -1, 4 }, back[0].Map);
            Assert.Equal(LssTiming.RealTime, back[1].Timing);
            Assert.Null(back[1].Map);
            Assert.Empty(LssLink.ParseAll("# only a comment\nbroken line\n"));
        }

        [Fact]
        public void ReadsTheRunHeaderAttemptsAndSegments()
        {
            LssRun run = Load(Sample);
            Assert.Equal("1.8.0", run.Version);
            Assert.Equal("The Forest", run.GameName);
            Assert.Equal("Any%", run.CategoryName);
            Assert.Equal(4, run.AttemptCount);
            Assert.Equal(0.0, run.Offset);
            Assert.Equal(new[] { "Cave 5", "Vault Door", "Red Elevator", "Game End" }, run.SegmentNames());
            Assert.Equal(new List<string> { "Personal Best", "WR" }, run.Comparisons);

            Assert.Equal(4, run.Attempts.Count);
            Assert.Equal(1500.5, run.Attempts[0].Time.Real, 4);
            Assert.False(run.Attempts[1].Finished);   // reset
            Assert.Equal("09/20/2026 18:30:00", run.Attempts[1].Started);
            Assert.Equal(1460.0, run.Attempts[3].Time.Game, 4);
        }

        [Fact]
        public void ReadsPbGoldsAndHistoryPerSegment()
        {
            LssRun run = Load(Sample);
            Assert.Equal(new[] { 295.0, 900.0, 1340.0, 1480.0 }, run.SplitTimes(LssRun.PersonalBest, LssTiming.RealTime));
            Assert.Equal(new[] { 285.0, 600.25, 440.0, 140.0 }, run.BestSegments(LssTiming.RealTime));

            LssSegment vault = run.Segments[1];
            Assert.Equal(4, vault.History.Count);
            Assert.True(vault.History[3].IsEmpty);          // skipped in attempt 3
            Assert.False(run.Segments[2].History.ContainsKey(2));   // attempt 2 reset before it
            Assert.Equal(290.5, run.Segments[0].History[2].Real, 4);
        }

        [Fact]
        public void EmptyAndPartialTimesAreNaN()
        {
            LssRun run = Load(Sample);
            double[] wr = run.SplitTimes("WR", LssTiming.RealTime);
            Assert.Equal(270.0, wr[0]);
            Assert.True(double.IsNaN(wr[2]));   // <SplitTime name="WR" />
            Assert.True(double.IsNaN(run.SplitTimes("No such comparison", LssTiming.RealTime)[0]));

            // Game time only where the file has it.
            double[] golds = run.BestSegments(LssTiming.GameTime);
            Assert.Equal(280.0, golds[0]);
            Assert.True(double.IsNaN(golds[1]));
        }

        [Fact]
        public void GameTimeAndRealTimeAreSeparate()
        {
            LssRun run = Load(Sample);
            Assert.Equal(new[] { 290.0, 890.0, 1325.0, 1460.0 }, run.SplitTimes(LssRun.PersonalBest, LssTiming.GameTime));
            Assert.Equal(4, run.CountTimes(LssTiming.RealTime));
            Assert.Equal(4, run.CountTimes(LssTiming.GameTime));
            Assert.Equal(LssTiming.RealTime, run.PreferredTiming());

            // A file timed in game time only prefers it.
            string gameOnly = Sample.Replace("<RealTime>", "<GameTime>").Replace("</RealTime>", "</GameTime>")
                                    .Replace("<GameTime>00:04:50</GameTime>", "");
            LssRun g = Load(gameOnly);
            Assert.Equal(0, g.CountTimes(LssTiming.RealTime));
            Assert.Equal(LssTiming.GameTime, g.PreferredTiming());
        }

        [Fact]
        public void ReadsFilesOlderThanLiveSplit16()
        {
            const string old = @"<Run>
  <GameName>The Forest</GameName><CategoryName>Any%</CategoryName>
  <Offset>-00:00:01.5000000</Offset>
  <Segments>
    <Segment><Name>Cave 5</Name>
      <PersonalBestSplitTime>00:05:00</PersonalBestSplitTime>
      <BestSegmentTime>00:04:40</BestSegmentTime>
      <SegmentHistory><Time id=""1"">00:05:00</Time><Time id=""2"" /></SegmentHistory>
    </Segment>
    <Segment><Name>End</Name>
      <SplitTimes><SplitTime name=""Personal Best"">00:09:00.5</SplitTime></SplitTimes>
      <BestSegmentTime />
    </Segment>
  </Segments>
</Run>";
            LssRun run = Load(old);
            Assert.Equal(-1.5, run.Offset, 4);
            Assert.Equal(new[] { 300.0, 540.5 }, run.SplitTimes(LssRun.PersonalBest, LssTiming.RealTime));
            Assert.Equal(280.0, run.Segments[0].BestSegment.Real);
            Assert.True(run.Segments[1].BestSegment.IsEmpty);
            Assert.True(run.Segments[0].History[2].IsEmpty);
            Assert.Equal(new List<string> { "Personal Best" }, run.Comparisons);
        }

        [Theory]
        [InlineData("00:05:12.3450000", 312.345)]
        [InlineData("01:00:00", 3600.0)]
        [InlineData("1.02:00:00", 93600.0)]
        [InlineData("-00:00:01.5000000", -1.5)]
        [InlineData("05:12.5", 312.5)]
        [InlineData("12.5", 12.5)]
        [InlineData(" 00:00:07 ", 7.0)]
        public void ParsesTimeSpanText(string text, double seconds)
        {
            double v;
            Assert.True(LssFile.TryParseTime(text, out v));
            Assert.Equal(seconds, v, 6);
        }

        [Theory]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData("00:61:00")]
        [InlineData("00:00:60")]
        [InlineData("1:2:3:4")]
        [InlineData("00:00:1,5")]
        [InlineData("00::05")]
        [InlineData("-")]
        public void RefusesBadTimes(string text)
        {
            double v;
            Assert.False(LssFile.TryParseTime(text, out v));
        }

        [Fact]
        public void BadInputSaysWhy()
        {
            Assert.Contains("empty", Error("   "));
            Assert.Contains("not valid XML", Error("<Run><Segments>"));
            Assert.Contains("layout (.lsl)", Error("<Layout version=\"1.6.1\"><Mode>Vertical</Mode></Layout>"));
            Assert.Contains("<Foo>", Error("<Foo />"));
            Assert.Contains("no segments", Error("<Run><GameName>The Forest</GameName><Segments /></Run>"));

            string bad = Sample.Replace("<RealTime>00:15:00</RealTime>", "<RealTime>fifteen minutes</RealTime>");
            string e = Error(bad);
            Assert.Contains("'fifteen minutes'", e);
            Assert.Contains("Vault Door", e);
            Assert.Contains("Personal Best", e);

            Assert.Contains("attempt id", Error("<Run><AttemptHistory><Attempt id=\"x\" /></AttemptHistory></Run>"));
        }

        [Fact]
        public void NeverFetchesWhatTheFileNames()
        {
            // An external entity is not resolved: the file either fails or
            // reads without the entity's content - never a fetch.
            const string xxe = @"<?xml version=""1.0""?>
<!DOCTYPE Run [ <!ENTITY x SYSTEM ""file:///etc/passwd""> ]>
<Run><Segments><Segment><Name>&x;Cave 5</Name></Segment></Segments></Run>";
            string error;
            LssRun run = LssFile.Parse(xxe, out error);
            if (run != null) Assert.Equal("Cave 5", run.Segments[0].Name);
        }

        // --- ranges and golds -----------------------------------------------

        [Fact]
        public void BestRangeUsesGoldsForOneSegmentAndHistoryForSeveral()
        {
            LssRun run = Load(Sample);
            // One segment: the file's gold, even below its history.
            Assert.Equal(285.0, run.BestRange(-1, 0, LssTiming.RealTime));
            // Vault + Red Elevator: attempt 1 1050.25, attempt 3 1060 (skip
            // rolled in), attempt 4 1045 - the golds' 1040.25 is only a floor.
            Assert.Equal(1045.0, run.BestRange(0, 2, LssTiming.RealTime), 4);
            // Red Elevator + Game End after Vault Door: attempt 3 skipped the
            // Vault Door split, so its Red Elevator time starts earlier - out.
            Assert.Equal(580.0, run.BestRange(1, 3, LssTiming.RealTime), 4);
            // No history in game time: the golds summed, NaN if one is missing.
            Assert.True(double.IsNaN(run.BestRange(-1, 1, LssTiming.GameTime)));
            Assert.True(double.IsNaN(run.BestRange(2, 1, LssTiming.RealTime)));
        }

        // --- matching -----------------------------------------------------------

        [Fact]
        public void NamesMatchIgnoringCaseSpacesAndPunctuation()
        {
            Assert.Equal(LssMatch.Key("Vault Door"), LssMatch.Key("vault-door"));
            Assert.Equal(LssMatch.Key("Vault Door"), LssMatch.Key("  VAULT_DOOR "));
            Assert.Equal("cave5", LssMatch.Key("-Cave 5"));          // subsplit
            Assert.Equal("cave6", LssMatch.Key("{Caves} Cave 6"));   // section's last split
            Assert.NotEqual(LssMatch.Key("Cave 5"), LssMatch.Key("Cave 6"));
        }

        [Fact]
        public void EndgameEventsMatchTheAutosplittersLabels()
        {
            string[] splits = { "Vault Door", "Finding Timmy (Artifact)", "Approaching Megan",
                                "Putting Megan in Artifact", "Gold Keycard (Automatic Door)",
                                "Gold Keycard (Red Elevator)", "Game End" };
            string[] rows = { "vault-door", "timmy-pickup", "megan-transform", "megan-to-machine",
                              "gold-door", "red-elevator", "game-end" };
            LssMatch m = LssMatch.Match(rows, splits, false);
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6 }, m.Map);
            Assert.True(m.Complete);
            Assert.Empty(m.UnmatchedSplits);
        }

        [Fact]
        public void AWholeRunSegmentTakesThePbAndGolds()
        {
            LssRun run = Load(Sample);
            string[] rows = { "cave 5", "Vault door", "RED ELEVATOR", "End" };
            LssMatch m = LssMatch.Match(rows, run.SegmentNames(), true);
            Assert.Equal(new[] { 0, 1, 2, 3 }, m.Map);   // "End" takes the last split
            Assert.Equal(-1, m.StartAfter);
            Assert.True(m.Complete);

            LssComparison c = LssComparison.Build(run, m, LssTiming.RealTime);
            Close(new[] { 295f, 900f, 1340f, 1480f }, c.PbSplits);
            Close(new[] { 285f, 600.25f, 440f, 140f }, c.Golds);
            Close(new[] { 285f, 885.25f, 1325.25f, 1465.25f }, c.GoldSplits);
            Assert.Equal(1480f, c.Pb, 3);
            Assert.Equal(1465.25f, c.SumOfBest, 3);

            Close(new[] { 270f, 840f, N, 1380f }, LssComparison.Other(run, m, "WR", LssTiming.RealTime));
            Close(new[] { 290f, 890f, 1325f, 1460f }, LssComparison.Build(run, m, LssTiming.GameTime).PbSplits);

            // Without endToLast, "End" is not "Game End".
            LssMatch strict = LssMatch.Match(rows, run.SegmentNames(), false);
            Assert.Equal(new List<string> { "End" }, strict.UnmatchedRows);
            Assert.Equal(new List<string> { "Game End" }, strict.UnmatchedSplits);
        }

        [Fact]
        public void APartOfTheRunStartsWhereTheSplitBeforeItEnded()
        {
            LssRun run = Load(Sample);
            LssMatch m = LssMatch.Match(new[] { "Vault Door", "Red Elevator" }, run.SegmentNames(), false);
            Assert.Equal(0, m.StartAfter);   // our clock starts at the Cave 5 split
            Assert.Equal(new List<string> { "Game End" }, m.UnmatchedSplits);   // Cave 5 is before our start

            LssComparison c = LssComparison.Build(run, m, LssTiming.RealTime);
            Close(new[] { 605f, 1045f }, c.PbSplits);
            Close(new[] { 600.25f, 440f }, c.Golds);
        }

        [Fact]
        public void ARowSpanningSeveralLiveSplitSegmentsTakesTheirBestRun()
        {
            LssRun run = Load(Sample);
            LssMatch m = LssMatch.Match(new[] { "Cave 5", "Red Elevator" }, run.SegmentNames(), false);
            Assert.Equal(new[] { 0, 2 }, m.Map);
            Assert.Equal(new List<string> { "Vault Door", "Game End" }, m.UnmatchedSplits);

            LssComparison c = LssComparison.Build(run, m, LssTiming.RealTime);
            Close(new[] { 295f, 1340f }, c.PbSplits);
            Close(new[] { 285f, 1045f }, c.Golds);
        }

        [Fact]
        public void UnmatchedRowsAreReportedAndStayBlank()
        {
            LssRun run = Load(Sample);
            LssMatch m = LssMatch.Match(new[] { "Cave 5", "Sinkhole", "Game End" }, run.SegmentNames(), false);
            Assert.Equal(new[] { 0, -1, 3 }, m.Map);
            Assert.False(m.Complete);
            Assert.Equal(new List<string> { "Sinkhole" }, m.UnmatchedRows);

            LssComparison c = LssComparison.Build(run, m, LssTiming.RealTime);
            Close(new[] { 295f, N, 1480f }, c.PbSplits);   // cumulative: the end still compares
            Close(new[] { 285f, N, N }, c.Golds);          // the end's span is unknown
            Assert.True(float.IsNaN(c.SumOfBest));

            // Nothing matches row 0: times count from LiveSplit's start.
            LssMatch late = LssMatch.Match(new[] { "Nowhere", "Red Elevator" }, run.SegmentNames(), false);
            Assert.Equal(-1, late.StartAfter);
            Close(new[] { N, 1340f }, late.Project(run.SplitTimes(LssRun.PersonalBest, LssTiming.RealTime)));
        }

        [Fact]
        public void MatchingKeepsRouteOrder()
        {
            string[] splits = { "Stick", "Stick", "Cave 5", "End" };
            // Repeated names pair up in turn.
            Assert.Equal(new[] { 0, 1 }, LssMatch.Match(new[] { "stick", "STICK" }, splits, false).Map);
            // A name that only exists earlier is not matched backwards.
            LssMatch m = LssMatch.Match(new[] { "Cave 5", "Stick" }, splits, false);
            Assert.Equal(new[] { 2, -1 }, m.Map);
            Assert.Equal(new List<string> { "Stick" }, m.UnmatchedRows);
            // An empty name never matches an empty one.
            Assert.Equal(new[] { -1 }, LssMatch.Match(new[] { "" }, new[] { "" }, false).Map);
        }

        [Fact]
        public void TheProjectionPlugsIntoSplitTable()
        {
            LssRun run = Load(Sample);
            LssMatch m = LssMatch.Match(new[] { "Cave 5", "Vault Door", "Red Elevator", "End" }, run.SegmentNames(), true);
            LssComparison c = LssComparison.Build(run, m, LssTiming.RealTime);

            // Our own stats (3 checkpoints -> 4 rows), compared with the LiveSplit PB.
            SplitStats own = SplitStats.Build(new List<Attempt>(), 3);
            SplitRow[] rows = new SplitRow[own.Rows];
            float[] times = { 300f, 890f, N, N };
            SplitTable.Fill(own, c.PbSplits, times, true, 1000f, rows);

            Assert.Equal(5f, rows[0].Delta, 3);
            Assert.Equal(-10f, rows[1].Delta, 3);
            Assert.Equal(-15f, rows[1].SegmentDelta, 3);
            Assert.True(rows[2].Current);
        }
    }
}
