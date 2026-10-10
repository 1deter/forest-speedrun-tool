using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    public class LssAutoSplitTests
    {
        // Trimmed from the author's real "The Forest Coop Any%.lss"
        // (LiveSplit 1.8.37): velocity start, Cave 6 enter / exit, two item
        // pickups (Rebreather 143, Keycard 210), endgame cutscenes. Other
        // caves are switched off at the cave level with their enter / exit
        // left ticked - the parent rule has to win.
        private const string Real = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Run version=""1.7.0"">
  <GameName>The Forest</GameName>
  <CategoryName>Coop Any%</CategoryName>
  <AttemptCount>3</AttemptCount>
  <AttemptHistory />
  <Segments>
    <Segment><Name>c6 enter</Name><SplitTimes /></Segment>
    <Segment><Name>c6 exit</Name><SplitTimes /></Segment>
    <Segment><Name>elevator</Name><SplitTimes /></Segment>
    <Segment><Name>end :)</Name><SplitTimes /></Segment>
  </Segments>
  <AutoSplitterSettings>
    <Version>1.5</Version>
    <ScriptPath>C:\Users\deter\Downloads\Transparent_LiveSplit_9a81e48\Components\The%20Forest.ASL</ScriptPath>
    <Start>True</Start>
    <Reset>True</Reset>
    <Split>True</Split>
    <CustomSettings>
      <Setting id=""Preferences"" type=""bool"">True</Setting>
      <Setting id=""mealStart"" type=""bool"">False</Setting>
      <Setting id=""velocityStart"" type=""bool"">True</Setting>
      <Setting id=""menuReset"" type=""bool"">True</Setting>
      <Setting id=""endSplits"" type=""bool"">True</Setting>
      <Setting id=""Cave Splits"" type=""bool"">True</Setting>
      <Setting id=""Cave01"" type=""bool"">False</Setting>
      <Setting id=""Cave01EnterSplit"" type=""bool"">True</Setting>
      <Setting id=""Cave01ExitSplit"" type=""bool"">True</Setting>
      <Setting id=""Cave05"" type=""bool"">False</Setting>
      <Setting id=""Cave05EnterSplit"" type=""bool"">True</Setting>
      <Setting id=""Cave05ExitSplit"" type=""bool"">True</Setting>
      <Setting id=""Cave06"" type=""bool"">True</Setting>
      <Setting id=""Cave06EnterSplit"" type=""bool"">True</Setting>
      <Setting id=""Cave06ExitSplit"" type=""bool"">True</Setting>
      <Setting id=""Cave07"" type=""bool"">False</Setting>
      <Setting id=""HellCave"" type=""bool"">False</Setting>
      <Setting id=""SnowCave"" type=""bool"">False</Setting>
      <Setting id=""UnderwaterCave"" type=""bool"">False</Setting>
      <Setting id=""Cave02"" type=""bool"">False</Setting>
      <Setting id=""Cave03"" type=""bool"">False</Setting>
      <Setting id=""Cave04"" type=""bool"">False</Setting>
      <Setting id=""Cave08"" type=""bool"">False</Setting>
      <Setting id=""Cave09"" type=""bool"">False</Setting>
      <Setting id=""Cave10"" type=""bool"">False</Setting>
      <Setting id=""UnderwaterCave2"" type=""bool"">False</Setting>
      <Setting id=""UnderwaterCave3"" type=""bool"">False</Setting>
      <Setting id=""Item Splits"" type=""bool"">True</Setting>
      <Setting id=""itemSplit_29"" type=""bool"">False</Setting>
      <Setting id=""multiItemSplit_29"" type=""bool"">True</Setting>
      <Setting id=""itemSplit_143"" type=""bool"">True</Setting>
      <Setting id=""multiItemSplit_143"" type=""bool"">False</Setting>
      <Setting id=""itemSplit_210"" type=""bool"">True</Setting>
      <Setting id=""multiItemSplit_210"" type=""bool"">False</Setting>
      <Setting id=""Clothing Splits"" type=""bool"">False</Setting>
      <Setting id=""clothingSplit_1"" type=""bool"">True</Setting>
      <Setting id=""Passenger Splits"" type=""bool"">False</Setting>
      <Setting id=""passengerSplit_1"" type=""bool"">False</Setting>
    </CustomSettings>
  </AutoSplitterSettings>
</Run>";

        private static LssAutoSplit ReadReal()
        {
            string error;
            LssAutoSplit a = LssAutoSplit.Read(Real, out error);
            Assert.Null(error);
            Assert.NotNull(a);
            return a;
        }

        [Fact]
        public void TheRealFileGivesItsStartAndSplitEvents()
        {
            LssAutoSplit a = ReadReal();
            Assert.Equal("splits file", a.Source);
            Assert.Equal(new[] { "first-input" }, a.StartEvents());   // the velocity start is the first input (T-0282)
            Assert.Equal(new[] { "item-143", "item-210", "cave-enter-cave06", "cave-exit-cave06", "endgame-cutscene" },
                         a.SplitEvents());
        }

        [Fact]
        public void AChildIsOffWhenAParentIsOff()
        {
            LssAutoSplit a = ReadReal();
            Assert.False(a.Setting("Cave01EnterSplit"));     // Cave01 off
            Assert.False(a.Setting("multiItemSplit_29"));    // itemSplit_29 off
            Assert.False(a.Setting("clothingSplit_1"));      // Clothing Splits off
            Assert.True(a.Setting("Cave06ExitSplit"));
        }

        [Fact]
        public void MissingSettingsTakeTheAslDefaults()
        {
            string text = @"<Run><Segments /><AutoSplitterSettings><ScriptPath>The Forest.asl</ScriptPath>
              <Start>True</Start><Split>True</Split><CustomSettings /></AutoSplitterSettings></Run>";
            string error;
            LssAutoSplit a = LssAutoSplit.Read(text, out error);
            Assert.NotNull(a);
            Assert.Equal(new[] { "hold-interact" }, a.StartEvents());   // mealStart on, velocityStart off
            string[] split = a.SplitEvents();
            Assert.Equal(15 * 2 + 1, split.Length);                     // every cave both ways + endgame
            Assert.Equal("cave-enter-cave01", split[0]);
            Assert.Equal("cave-exit-underwatercave3", split[29]);
            Assert.Equal("endgame-cutscene", split[30]);
        }

        [Fact]
        public void LiveSplitsOwnSwitchesTurnStartAndSplitOff()
        {
            LssAutoSplit a = LssAutoSplit.Read(Real.Replace("<Start>True</Start>", "<Start>False</Start>")
                                                    .Replace("<Split>True</Split>", "<Split>False</Split>"), out _);
            Assert.Empty(a.StartEvents());
            Assert.Empty(a.SplitEvents());
        }

        [Fact]
        public void ALayoutHoldsTheSameBlock()
        {
            string layout = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Layout version=""1.6.1"">
  <Components>
    <Component><Path>LiveSplit.Splits.dll</Path><Settings><CurrentSplitTopColor>FF3373F4</CurrentSplitTopColor></Settings></Component>
    <Component>
      <Path>LiveSplit.ScriptableAutoSplit.dll</Path>
      <Settings>
        <Version>1.5</Version>
        <ScriptPath>C:\LiveSplit\Components\The Forest.ASL</ScriptPath>
        <Start>True</Start><Reset>True</Reset><Split>True</Split>
        <CustomSettings>
          <Setting id=""mealStart"" type=""bool"">True</Setting>
          <Setting id=""velocityStart"" type=""bool"">True</Setting>
          <Setting id=""Cave Splits"" type=""bool"">False</Setting>
          <Setting id=""Passenger Splits"" type=""bool"">True</Setting>
          <Setting id=""passengerSplit_3"" type=""bool"">True</Setting>
        </CustomSettings>
      </Settings>
    </Component>
  </Components>
</Layout>";
            string error;
            LssAutoSplit a = LssAutoSplit.Read(layout, out error);
            Assert.NotNull(a);
            Assert.Equal("layout", a.Source);
            Assert.Equal(new[] { "hold-interact", "first-input" }, a.StartEvents());
            Assert.Equal(new[] { "passenger-3", "endgame-cutscene" }, a.SplitEvents());
        }

        [Fact]
        public void AFileWithoutTheBlockIsNullWithNoError()
        {
            string error;
            Assert.Null(LssAutoSplit.Read("<Run><Segments /></Run>", out error));
            Assert.Null(error);
            Assert.Null(LssAutoSplit.Read("<Run", out error));
            Assert.NotNull(error);
        }

        [Fact]
        public void AnotherGamesScriptIsNotTaken()
        {
            string text = @"<Run><AutoSplitterSettings><ScriptPath>C:\Celeste.asl</ScriptPath>
              <CustomSettings><Setting id=""chapter1"" type=""bool"">True</Setting></CustomSettings></AutoSplitterSettings></Run>";
            string error;
            Assert.Null(LssAutoSplit.Read(text, out error));
        }

        [Fact]
        public void TheBuiltSegmentSplitsOnTheNextAutosplitPerLiveSplitSplit()
        {
            string error;
            LssRun run = LssFile.Parse(Real, out error);
            Assert.NotNull(run);
            Segment s = LssSegmentBuilder.Build(run, ReadReal(), "file");

            Assert.Equal("The Forest Coop Any%", s.Name);
            run.CategoryName = "";
            Assert.Equal("my file", LssSegmentBuilder.Build(run, null, "my file").Name);
            Assert.Equal("LiveSplit", s.Category);
            Assert.True(s.IsTimed);
            Assert.Equal("event first-input", TriggerParser.Write(s.Start));
            Assert.Equal(3, s.Checkpoints.Count);
            for (int i = 0; i < 3; i++) Assert.Equal("event autosplit", TriggerParser.Write(s.Checkpoints[i]));
            Assert.Equal("event autosplit", TriggerParser.Write(s.End));
            Assert.Equal(new[] { "c6 enter", "c6 exit", "elevator", "end :)" },
                         new[] { s.SplitName(0), s.SplitName(1), s.SplitName(2), s.SplitName(3) });
            Assert.Equal(5, s.AutoSplit.Count);

            // Every row matches the file by name.
            LssMatch m = LssMatch.Match(new[] { s.SplitName(0), s.SplitName(1), s.SplitName(2), s.SplitName(3) },
                                        run.SegmentNames(), false);
            Assert.True(m.Complete);
        }

        [Fact]
        public void BothStartsBecomeOneEitherTrigger()
        {
            string error;
            LssRun run = LssFile.Parse(Real, out error);
            LssAutoSplit a = ReadReal();
            a.Values["mealStart"] = true;
            Segment s = LssSegmentBuilder.Build(run, a, "file");
            Assert.Equal("event hold-interact|first-input", TriggerParser.Write(s.Start));

            TriggerState st = new TriggerState();
            Assert.False(TriggerEvaluator.Crossed(s.Start, ref st, Vector3.zero, null, null, null));
            Assert.True(TriggerEvaluator.Crossed(s.Start, ref st, Vector3.zero, null, "FIRST-INPUT", null));
            Assert.False(TriggerEvaluator.EventMatches("hold-interact|moving", "hold"));
            Assert.False(TriggerEvaluator.EventMatches("hold-interact|moving", "moving|"));
            Assert.True(TriggerEvaluator.EventMatches("hold-interact | moving", "hold-interact"));
        }

        [Fact]
        public void NoSettingsBuildsManualSplits()
        {
            string error;
            LssRun run = LssFile.Parse(Real, out error);
            Segment s = LssSegmentBuilder.Build(run, null, "file");
            Assert.Equal(TriggerKind.Manual, s.Start.Kind);
            Assert.Equal(TriggerKind.Manual, s.End.Kind);
            Assert.Empty(s.AutoSplit);
            Assert.Equal(3, s.Checkpoints.Count);
        }

        [Fact]
        public void TheAutosplitListRoundTripsAndChangesTheRoute()
        {
            string error;
            LssRun run = LssFile.Parse(Real, out error);
            Segment s = LssSegmentBuilder.Build(run, ReadReal(), "file");
            s.Id = "s-0123456789ab";
            s.HasSpawn = true;

            var sb = new System.Text.StringBuilder();
            SegmentFormat.WriteSegment(sb, s, "\n");
            List<Segment> back = SegmentFormat.ParseAll(sb.ToString().Split('\n'), null);
            Assert.Single(back);
            Assert.Equal(s.AutoSplit, back[0].AutoSplit);
            Assert.Equal(s.RouteFingerprint(), back[0].RouteFingerprint());
            Assert.Equal("end :)", back[0].SplitName(3));

            string before = s.RouteFingerprint();
            s.AutoSplit.RemoveAt(0);
            Assert.NotEqual(before, s.RouteFingerprint());
            s.AutoSplit.Clear();
            Segment plain = new Segment();
            Assert.Equal(plain.RouteFingerprint(), new Segment().RouteFingerprint());
        }

        [Fact]
        public void FourAutosplitsRunTheWholeSegment()
        {
            string error;
            LssRun run = LssFile.Parse(Real, out error);
            Segment s = LssSegmentBuilder.Build(run, ReadReal(), "file");
            SplitSequence seq = new SplitSequence();
            seq.Begin(s.Checkpoints, s.End);
            Assert.Equal(SplitEvent.None, seq.Evaluate(Vector3.zero, null, null, null));
            Assert.Equal(SplitEvent.Split, seq.Evaluate(Vector3.zero, null, "autosplit", null));
            Assert.Equal(SplitEvent.None, seq.Evaluate(Vector3.zero, null, null, null));
            Assert.Equal(SplitEvent.None, seq.Evaluate(Vector3.zero, null, "cave-enter-cave06", null));
            Assert.Equal(SplitEvent.Split, seq.Evaluate(Vector3.zero, null, "autosplit", null));
            Assert.Equal(SplitEvent.Split, seq.Evaluate(Vector3.zero, null, "autosplit", null));
            Assert.Equal(SplitEvent.Finished, seq.Evaluate(Vector3.zero, null, "autosplit", null));
        }

        private sealed class Bag : IItemCounts
        {
            public readonly Dictionary<int, int> N = new Dictionary<int, int>();
            public int AmountOf(int id) { int n; return N.TryGetValue(id, out n) ? n : 0; }
        }

        [Fact]
        public void TheWatchSplitsLikeTheAsl()
        {
            AutoSplitWatch w = new AutoSplitWatch();
            w.Configure(new List<string> { "item-143", "item-29", "item-change-29", "cave-enter-cave06", "clothing-4", "endgame-cutscene" });
            Assert.Equal(new List<int> { 143, 29 }, w.ItemIds);

            Bag bag = new Bag();
            bag.N[29] = 2;               // two bombs held at the start: already seen
            w.Begin(bag);

            Assert.False(w.OnItems(bag));
            bag.N[143] = 1;              // the rebreather picked up
            Assert.True(w.OnItems(bag));
            Assert.False(w.OnItems(bag));
            bag.N[143] = 0;              // dropped and picked up again: first pickup only
            Assert.False(w.OnItems(bag));
            bag.N[143] = 1;
            Assert.False(w.OnItems(bag));
            bag.N[29] = 1;               // a bomb used: every change
            Assert.True(w.OnItems(bag));

            Assert.True(w.OnEvent("cave-enter-cave06"));
            Assert.False(w.OnEvent("cave-enter"));
            Assert.False(w.OnEvent("cave-exit-cave06"));
            Assert.True(w.OnEvent("Endgame-Cutscene"));
            Assert.True(w.OnEvent("clothing-4"));
            Assert.False(w.OnEvent("clothing-4"));   // worn again: once per run

            w.Begin(bag);                // the next run
            Assert.True(w.OnEvent("clothing-4"));
        }
    }
}
