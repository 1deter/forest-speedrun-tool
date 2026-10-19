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
        public void BasicIsTheSitePresetWithSpeedFirst()
        {
            Assert.Equal("Speed", ReplayValues.Basic[0]);
            Assert.Equal(13, ReplayValues.Basic.Length);
            Assert.Contains("BatteryCharge", ReplayValues.Basic);
            Assert.Equal(new List<string>(ReplayValues.Basic), ReplayValues.Parse(ReplayValues.BasicConfig));
        }

        [Fact]
        public void LineShowsTheChosenValuesAtT()
        {
            List<string> basic = new List<string>(ReplayValues.Basic);
            Assert.Equal("Speed 5.5 m/s   Health 100   Stamina 90   Fullness 50%   Light on no",
                         ReplayValues.Line(Run(), 1.5f, basic));
            Assert.Equal("Speed 2.0 m/s   Health 60   Stamina 40.4   Fullness 25%   Light on yes",
                         ReplayValues.Line(Run(), 3.5f, basic));
        }

        [Fact]
        public void AnyRecordedChannelCanBeChosen()
        {
            Assert.Equal("Hunger 4", ReplayValues.Line(Run(), 2f, new[] { "Hunger" }));
            Assert.Equal("", ReplayValues.Line(Run(), 2f, new string[0]));
            Assert.Equal("", ReplayValues.Line(null, 2f, new[] { "Health" }));
        }

        [Fact]
        public void ARunWithNoStateSaysSo()
        {
            Attempt a = new Attempt();
            a.Samples.Add(new RunSample { T = 0f, Speed = 3f });
            Assert.Equal(ReplayValues.NotRecorded + "   Speed 3.0 m/s", ReplayValues.Line(a, 1f, new[] { "Speed", "Health" }));
            Assert.Equal("Speed 3.0 m/s", ReplayValues.Line(a, 1f, new[] { "Speed" }));
        }

        [Fact]
        public void FormatsFollowTheSite()
        {
            Assert.Equal("37.5 °C", ReplayValues.Format("BodyTemp", 37.5f));
            Assert.Equal("80%", ReplayValues.Format("BatteryCharge", 80f));
            Assert.Equal("yes", ReplayValues.Format("Cold", 1f));
            Assert.Equal("1.50e+5", ReplayValues.Format("PedometerSteps", 150000f));
            Assert.Equal("Cold armour", ReplayValues.Label("ColdArmor"));
        }

        [Fact]
        public void ChoicesAreBasicThenTheRest()
        {
            List<string> c = ReplayValues.Choices(new[] { "Health", "Hunger", "Run" });
            Assert.Equal(15, c.Count);
            Assert.Equal("Hunger", c[13]);
            Assert.Equal("Run", c[14]);
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
            Assert.Empty(ReplayValues.Parse(""));
        }

        [Fact]
        public void CacheRebuildsOnlyOnChange()
        {
            Attempt a = Run();
            ReplayValues.Cache c = new ReplayValues.Cache();
            string[] chosen = { "Health" };
            c.Get(a, 1.1f, chosen, 1); c.Get(a, 1.2f, chosen, 1); c.Get(a, 1.9f, chosen, 1);
            Assert.Equal(1, c.Builds);
            Assert.Equal("Health 60", c.Get(a, 2.1f, chosen, 1));
            Assert.Equal(2, c.Builds);
            c.Get(a, 2.1f, new[] { "Stamina" }, 2);
            Assert.Equal(3, c.Builds);
        }
    }
}
