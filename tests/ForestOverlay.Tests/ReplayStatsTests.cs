using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class ReplayStatsTests
    {
        private static Attempt Run()
        {
            Attempt a = new Attempt();
            a.Channels = new[] { "Health", "Stamina" };
            a.States.Add(new StateSample { T = 0f, Values = new[] { 100f, 90f } });
            a.States.Add(new StateSample { T = 2f, Values = new[] { 60f, 40f } });
            a.Samples.Add(new RunSample { T = 0f, Speed = 1f });
            a.Samples.Add(new RunSample { T = 1f, Speed = 5.5f });
            a.Samples.Add(new RunSample { T = 3f, Speed = 2f });
            return a;
        }

        [Fact]
        public void ShowsTheLastSnapshotAtOrBeforeT()
        {
            Assert.Equal("Health 100   Stamina 90   Speed 5.5 m/s", ReplayStats.Line(Run(), 1.5f));
            Assert.Equal("Health 60   Stamina 40   Speed 2.0 m/s", ReplayStats.Line(Run(), 3.5f));
        }

        [Fact]
        public void LeavesOutWhatTheRunLacks()
        {
            Attempt a = new Attempt();
            Assert.Equal(ReplayStats.NoState, ReplayStats.Line(a, 1f));
            Assert.Equal("", ReplayStats.Line(null, 1f));
            a.Samples.Add(new RunSample { T = 0f, Speed = 3f });
            Assert.Equal(ReplayStats.NoState + "   Speed 3.0 m/s", ReplayStats.Line(a, 1f));
        }

        [Fact]
        public void RunWithNoStateTrackSaysSo()
        {
            Attempt a = new Attempt();
            a.Samples.Add(new RunSample { T = 0f, Speed = 3f });
            Assert.Equal("Health and stamina: not recorded in this run   Speed 3.0 m/s", ReplayStats.Line(a, 1f));
        }

        [Fact]
        public void BeforeTheFirstSnapshotOnlySpeedShows()
        {
            Attempt a = Run();
            a.States.Clear();
            a.States.Add(new StateSample { T = 1f, Values = new[] { 100f, 90f } });
            Assert.Equal("Speed 1.0 m/s", ReplayStats.Line(a, 0.5f));
        }

        [Fact]
        public void CacheRebuildsOnlyOnChange()
        {
            Attempt a = Run();
            ReplayStats.Cache c = new ReplayStats.Cache();
            c.Get(a, 1.1f); c.Get(a, 1.2f); c.Get(a, 1.9f);
            Assert.Equal(1, c.Builds);
            Assert.Equal("Health 60   Stamina 40   Speed 5.5 m/s", c.Get(a, 2.1f));
            Assert.Equal(2, c.Builds);
        }
    }
}
