using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class MarkLabelsTests
    {
        private static Attempt Run()
        {
            Attempt a = new Attempt();
            a.Channels = new[] { "Health", "Stamina", "Armor", "BodyTemp" };
            a.States.Add(new StateSample { T = 0f, Values = new[] { 100f, 90f, 0f, 37f } });
            a.States.Add(new StateSample { T = 5f, Values = new[] { 60.5f, 40f, 10f, 36.5f } });
            a.Items.Add(new ItemChange { T = 0f, Name = "Lighter", Count = 1 });
            a.Items.Add(new ItemChange { T = 1f, Name = "Soda", Count = 2 });
            a.Items.Add(new ItemChange { T = 5f, Name = "Soda", Count = 3 });
            a.Samples.Add(new RunSample { T = 0f, Speed = 1f });
            a.Samples.Add(new RunSample { T = 4f, Speed = 5.5f });
            return a;
        }

        private static readonly float[] Times = { 2f, 6f, float.NaN };

        [Fact]
        public void DefaultIsTimeDeltaSpeedHealthStaminaArmor()
        {
            MarkSpec s = MarkSpec.Parse(null);
            Assert.Equal(MarkSpec.Default, s.Serialize());
            Assert.Equal("time,delta,speed,ch:Health,ch:Stamina,ch:Armor", MarkSpec.Default);
            Assert.Equal(MarkSpec.Default, MarkSpec.Parse(MarkSpec.Default).Serialize());
            // Armor 0 is left out (author: "armor (if any)").
            Assert.Equal(SplitTable.Time(2f, 3) + "  1.0 m/s  Health 100  Stamina 90", MarkLabels.LabelText(s, Run(), Times, 0, 3));
            Assert.Equal(SplitTable.Time(6f, 3) + "  5.5 m/s  Health 60.5  Stamina 40  Armor 10", MarkLabels.LabelText(s, Run(), Times, 1, 3));
            Assert.Equal(SplitTable.Delta(0.5f, 2), MarkLabels.DeltaText(s, 0.5f, 2));
            Assert.Equal("", MarkLabels.DeltaText(s, float.NaN, 2));
        }

        [Fact]
        public void WrapLayoutRowsJoinsAndOldSpecsReadAsOneLine()
        {
            string t = SplitTable.Time(2f, 3);
            MarkSpec one = MarkSpec.Parse("time,speed,ch:Health,item:Soda");
            Assert.False(one.Wrap);
            Assert.Equal(t + "  1.0 m/s  Health 100  Soda 2", MarkLabels.LabelText(one, Run(), Times, 0, 3));

            MarkSpec w = MarkSpec.Parse("time,speed,ch:Health,item:Soda,wrap");
            Assert.True(w.Wrap);
            Assert.Equal(t + "  1.0 m/s\nHealth 100\nSoda 2", MarkLabels.LabelText(w, Run(), Times, 0, 3));
            Assert.Equal("time,speed,ch:Health,item:Soda,wrap", w.Serialize());

            MarkSpec j = MarkSpec.Parse("time,speed,ch:Health,item:Soda,wrap,join:stats");
            Assert.Equal(t + "  1.0 m/s  Health 100\nSoda 2", MarkLabels.LabelText(j, Run(), Times, 0, 3));
            Assert.Equal("time,speed,ch:Health,item:Soda,wrap,join:stats", j.Serialize());

            // An empty group takes no row; delta alone is the Line group.
            MarkSpec e = MarkSpec.Parse("delta,item:Soda,wrap");
            Assert.Equal("Soda 2", MarkLabels.LabelText(e, Run(), Times, 0, 3));
            Assert.Equal(SplitTable.Delta(0.5f, 2), MarkLabels.DeltaText(e, 0.5f, 2));
            // Items join the stats row that sits above (the Line group is empty).
            MarkSpec ji = MarkSpec.Parse("ch:Health,item:Soda,wrap,join:items");
            Assert.Equal("Health 100  Soda 2", MarkLabels.LabelText(ji, Run(), Times, 0, 3));
            // Join flags do nothing off wrap and are not kept.
            Assert.Equal("time", MarkSpec.Parse("time,join:stats").Serialize());
        }

        [Fact]
        public void OldDefaultStillParsesAsItsOwnChoice()
        {
            MarkSpec s = MarkSpec.Parse(MarkSpec.OldDefault);
            Assert.True(s.Time); Assert.True(s.Delta); Assert.False(s.Speed);
            Assert.Empty(s.Channels);
            Assert.Equal("time,delta", s.Serialize());
        }

        [Fact]
        public void OldDefaultMovesOnlyOnce()
        {
            Assert.Equal(MarkSpec.Default, MarkSpec.MoveOldDefault(MarkSpec.OldDefault, false));
            Assert.Equal(MarkSpec.OldDefault, MarkSpec.MoveOldDefault(MarkSpec.OldDefault, true));
            Assert.Equal("time,ch:Thirst", MarkSpec.MoveOldDefault("time,ch:Thirst", false));
            Assert.Equal("off", MarkSpec.MoveOldDefault("off", false));
        }

        [Fact]
        public void StatsInRunnerWordsAndItemsCount()
        {
            Assert.Equal(7, MarkSpec.Stats.Length);
            MarkSpec s = MarkSpec.Parse("ch:BodyTemp,item:Soda,item:Soda,item:Nope");
            Assert.Equal(2, s.Items.Count);
            Assert.Equal("ch:BodyTemp,item:Soda,item:Nope", s.Serialize());
            Assert.Equal("Body temp 37  Soda 2  Nope 0", MarkLabels.LabelText(s, Run(), Times, 0, 1));
            Assert.Equal("Body temp 36.5  Soda 3  Nope 0", MarkLabels.LabelText(s, Run(), Times, 1, 1));
            s.SetItem("Soda", false);
            Assert.False(s.HasItem("Soda"));
            Assert.Equal(2, s.FieldCount);   // Body temp + Nope
            Assert.Contains("Body temp", MarkLabels.Status(MarkSpec.Parse("ch:BodyTemp"), new Attempt(), 2, 1));
        }

        [Fact]
        public void ItemNamesAreTheRunsInFirstOrder()
        {
            var names = new System.Collections.Generic.List<string> { "stale" };
            MarkLabels.ItemNames(Run(), names);
            Assert.Equal(new[] { "Lighter", "Soda" }, names.ToArray());
            MarkLabels.ItemNames(null, names);
            Assert.Empty(names);
        }

        [Fact]
        public void ParseRoundTripsAndFallsBack()
        {
            Assert.Equal(MarkSpec.Kind.Off, MarkSpec.Parse("OFF").Mode);
            Assert.Equal(MarkSpec.Kind.Dots, MarkSpec.Parse("dot").Mode);
            MarkSpec s = MarkSpec.Parse("time, speed,ch:Health,ch:health,bogus");
            Assert.True(s.Time); Assert.True(s.Speed); Assert.False(s.Delta);
            Assert.Single(s.Channels);
            Assert.Equal("time,speed,ch:Health", s.Serialize());
            Assert.Equal(MarkSpec.Default, MarkSpec.Parse("bogus,,").Serialize());
            Assert.Equal(MarkSpec.Default, MarkSpec.Parse("").Serialize());
        }

        [Fact]
        public void FieldsJoinInOrder()
        {
            MarkSpec s = MarkSpec.Parse("time,segment,speed,ch:Health");
            string expect = SplitTable.Time(6f, 1) + "  seg " + SplitTable.Time(4f, 1) + "  5.5 m/s  Health 60.5";
            Assert.Equal(expect, MarkLabels.LabelText(s, Run(), Times, 1, 1));
        }

        [Fact]
        public void MissingDataIsLeftOutNotBlank()
        {
            MarkSpec s = MarkSpec.Parse("speed,ch:Nope,ch:Stamina");
            // No samples, no channels: nothing, no gaps.
            Assert.Equal("", MarkLabels.LabelText(s, new Attempt(), Times, 0, 1));
            // No run at all: nothing.
            Assert.Equal("", MarkLabels.LabelText(s, null, Times, 0, 1));
            // The unknown marker has no label at all.
            Assert.Equal("", MarkLabels.LabelText(s, Run(), Times, 2, 1));
            // A channel the run lacks is skipped while the others show.
            string t = MarkLabels.LabelText(s, Run(), Times, 1, 1);
            Assert.DoesNotContain("Nope", t);
            Assert.DoesNotContain("NaN", t);
            Assert.Equal("5.5 m/s  Stamina 40", t);
        }

        [Fact]
        public void SegmentNeedsTheEarlierMarker()
        {
            MarkSpec s = MarkSpec.Parse("segment");
            Assert.Equal("", MarkLabels.LabelText(s, Run(), new[] { float.NaN, 6f }, 1, 1));
            Assert.Equal("seg " + SplitTable.Time(6f, 1), MarkLabels.LabelText(s, Run(), new[] { 6f }, 0, 1));
        }

        [Fact]
        public void OffAndDotsDrawNoText()
        {
            Assert.Equal("", MarkLabels.LabelText(MarkSpec.Parse("off"), Run(), Times, 0, 2));
            Assert.Equal("", MarkLabels.LabelText(MarkSpec.Parse("dot"), Run(), Times, 0, 2));
            Assert.Equal("", MarkLabels.DeltaText(MarkSpec.Parse("dot"), 1f, 2));
            Assert.Equal("", MarkLabels.DeltaText(MarkSpec.Parse("time"), 1f, 2));
        }

        [Fact]
        public void StatusSaysWhy()
        {
            MarkSpec def = MarkSpec.Parse(null);
            Assert.Contains("off", MarkLabels.Status(MarkSpec.Parse("off"), Run(), 2, 1));
            Assert.Contains("No comparison", MarkLabels.Status(def, null, 0, 0));
            Assert.Contains("no splits", MarkLabels.Status(def, Run(), 1, 0));
            Assert.Equal("", MarkLabels.Status(def, Run(), 2, 1));
            // T-0401: a plain spot names itself, not the comparison left from the last spot.
            Assert.Equal("This spot is not timed: no checkpoints to show.", MarkLabels.Status(def, Run(), 2, 1, true));
            Assert.Equal("This spot is not timed: no checkpoints to show.", MarkLabels.Status(def, null, 0, 0, true));
            Assert.Contains("off", MarkLabels.Status(MarkSpec.Parse("off"), Run(), 2, 1, true));
            Assert.Contains("Nope", MarkLabels.Status(MarkSpec.Parse("time,ch:Nope"), Run(), 2, 1));
        }
    }
}
