using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class ReplayValuesTests
    {
        private static Attempt Run()
        {
            Attempt a = new Attempt();
            a.Channels = new[] { "Health", "Stamina", "Fullness", "IsLit", "Hunger" };
            a.States.Add(new StateSample { T = 0f, Values = new[] { 100f, 90f, 0.5f, 0f, 3f } });
            a.States.Add(new StateSample { T = 2f, Values = new[] { 60f, 40.4f, 0.25f, 1f, 4f } });
            a.Samples.Add(new RunSample { T = 0f, Speed = 1f });
            a.Samples.Add(new RunSample { T = 1f, Speed = 5.5f });
            a.Samples.Add(new RunSample { T = 3f, Speed = 2f });
            return a;
        }

        [Fact]
        public void BasicIsTheSitePresetWithSpeedAndQuickSlots()
        {
            Assert.Equal("Speed", ReplayValues.Basic[0]);
            Assert.Equal(14, ReplayValues.Basic.Length);   // the site's 12, the speed, the quick slots
            Assert.Contains("BatteryCharge", ReplayValues.Basic);
            Assert.Equal(ReplayValues.QuickSlots, ReplayValues.Basic[13]);
            Assert.Equal(new List<string>(ReplayValues.Basic), ReplayValues.Parse("Basic"));
            Assert.Equal(new List<string>(ReplayValues.Basic), ReplayValues.Parse(""));
            Assert.Equal("Basic", ReplayValues.Join(new List<string>(ReplayValues.Basic)));
        }

        [Fact]
        public void LineShowsTheChosenValuesAtT()
        {
            List<string> basic = new List<string>(ReplayValues.Basic);
            Assert.Equal("Speed 5.5 m/s   Health 100   Stamina 90   Fullness 50%   Light on no",
                         ReplayValues.Line(Run(), 1.5f, basic, null));
            Assert.Equal("Speed 2.0 m/s   Health 60   Stamina 40.4   Fullness 25%   Light on yes",
                         ReplayValues.Line(Run(), 3.5f, basic, null));
        }

        [Fact]
        public void AnyRecordedChannelCanBeChosen()
        {
            Assert.Equal("Hunger 4", ReplayValues.Line(Run(), 2f, new[] { "Hunger" }, null));
            Assert.Equal("", ReplayValues.Line(Run(), 2f, new string[0], null));
            Assert.Equal("", ReplayValues.Line(null, 2f, new[] { "Health" }, null));
        }

        [Fact]
        public void UnknownNamesAreKeptButSkipped()
        {
            Assert.Equal("Health 60", ReplayValues.Line(Run(), 2f, new[] { "NoSuchChannel", "Health" }, null));
            Assert.Equal(new[] { "Health", "NoSuchChannel" },
                         ReplayValues.Ordered(new[] { "NoSuchChannel", "Health" }, ReplayValues.Choices(null)).ToArray());
        }

        [Fact]
        public void ARunWithNoStateSaysSo()
        {
            Attempt a = new Attempt();
            a.Samples.Add(new RunSample { T = 0f, Speed = 3f });
            Assert.Equal(ReplayValues.NotRecorded + "   Speed 3.0 m/s", ReplayValues.Line(a, 1f, new[] { "Speed", "Health" }, null));
            Assert.Equal("Speed 3.0 m/s", ReplayValues.Line(a, 1f, new[] { "Speed" }, null));
        }

        [Fact]
        public void QuickSlotsGoOnTheirOwnLine()
        {
            Attempt a = Run();
            a.QuickSlots = new QuickSlotTrack();
            a.QuickSlots.Add(0f, new[] { 80, 0, 0, 0 });
            a.QuickSlots.Add(2f, new[] { 80, 0, 53, 0 });
            System.Func<int, string> name = id => id == 80 ? "Axe Plane" : "Rock";
            Assert.Equal("Health 100\nSlots: 1 Axe Plane  2 -  3 -  4 -", ReplayValues.Line(a, 1f, new[] { "Health", ReplayValues.QuickSlots }, name));
            Assert.Equal("Slots: 1 Axe Plane  2 -  3 Rock  4 -", ReplayValues.Line(a, 2f, new[] { ReplayValues.QuickSlots }, name));
            Assert.Equal("", ReplayValues.Line(Run(), 2f, new[] { ReplayValues.QuickSlots }, name));   // an older run: no slots
        }

        [Fact]
        public void FormatsFollowTheSite()
        {
            Assert.Equal("37.5 °C", ReplayValues.Format("BodyTemp", 37.5f));
            Assert.Equal("37.0 °C", ReplayValues.Format("BodyTemp", 37f));
            Assert.Equal("80%", ReplayValues.Format("Fullness", 0.8f));
            Assert.Equal("80%", ReplayValues.Format("BatteryCharge", 80f));
            Assert.Equal("yes", ReplayValues.Format("Cold", 1f));
            Assert.Equal("1.50e+5", ReplayValues.Format("PedometerSteps", 150000f));
            Assert.Equal("Cold armour", ReplayValues.Label("ColdArmor"));
        }

        [Fact]
        public void ChoicesAreBasicThenTheRest()
        {
            List<string> c = ReplayValues.Choices(new[] { "Health", "Hunger", "Run" });
            Assert.Equal(16, c.Count);
            Assert.Equal("Hunger", c[14]);
            Assert.Equal("Run", c[15]);
        }

        [Fact]
        public void OrderedFollowsTheChoices()
        {
            List<string> o = ReplayValues.Ordered(new[] { "Hunger", "Health", "Gone" }, ReplayValues.Choices(new[] { "Hunger" }));
            Assert.Equal(new[] { "Health", "Hunger", "Gone" }, o.ToArray());
        }

        [Fact]
        public void ParseDropsBlanksAndRepeats()
        {
            Assert.Equal(new[] { "Health", "Speed" }, ReplayValues.Parse(" Health, ,Speed,Health").ToArray());
            Assert.Empty(ReplayValues.Parse("none"));
            Assert.Equal("none", ReplayValues.Join(new List<string>()));
            Assert.Equal("Health,Speed", ReplayValues.Join(new List<string> { "Health", "Speed" }));
        }

        [Fact]
        public void CacheRebuildsOnlyOnChange()
        {
            Attempt a = Run();
            ReplayValues.Cache c = new ReplayValues.Cache();
            string[] chosen = { "Health" };
            c.Get(a, 1.1f, chosen, 1, 0, null); c.Get(a, 1.2f, chosen, 1, 0, null); c.Get(a, 1.9f, chosen, 1, 0, null);
            Assert.Equal(1, c.Builds);
            Assert.Equal("Health 60", c.Get(a, 2.1f, chosen, 1, 0, null));
            Assert.Equal(2, c.Builds);
            c.Get(a, 2.1f, new[] { "Stamina" }, 2, 0, null);
            Assert.Equal(3, c.Builds);
        }

        [Fact]
        public void CacheRebuildsWhenTheNamesMove()
        {
            Attempt a = Run();
            a.QuickSlots = new QuickSlotTrack();
            a.QuickSlots.Add(0f, new[] { 53, 0, 0, 0 });
            ReplayValues.Cache c = new ReplayValues.Cache();
            string[] chosen = { ReplayValues.QuickSlots };
            bool ready = false;
            System.Func<int, string> name = id => ready ? "Rock" : null;
            Assert.Equal("Slots: 1 #53  2 -  3 -  4 -", c.Get(a, 1f, chosen, 1, 0, name));
            ready = true;
            Assert.Equal("Slots: 1 #53  2 -  3 -  4 -", c.Get(a, 1f, chosen, 1, 0, name));   // same key: kept
            Assert.Equal("Slots: 1 Rock  2 -  3 -  4 -", c.Get(a, 1f, chosen, 1, 1, name));  // the names moved
        }

        [Fact]
        public void FileNamesCarryTheDateAndLength()
        {
            Attempt a = new Attempt();
            a.RecordedUtc = new System.DateTime(2026, 10, 19, 14, 3, 12, System.DateTimeKind.Utc);
            a.Duration = 83.4567f;
            string name = AttemptFormat.FileName(a);
            Assert.Equal("20261019_140312_83.457.run", name);
            System.DateTime utc;
            float d;
            Assert.True(AttemptFormat.TryParseFileName(@"C:\x\runs\recordings\" + name, out utc, out d));
            Assert.Equal(a.RecordedUtc, utc);
            Assert.Equal(System.DateTimeKind.Utc, utc.Kind);
            Assert.Equal(83.457f, d, 3);
            Assert.False(AttemptFormat.TryParseFileName("notes.txt", out utc, out d));
            Assert.False(AttemptFormat.TryParseFileName("2026x019_140312_1.000.run", out utc, out d));
        }
    }
}
