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
        public void SpeedIsTheLastSampleAtOrBeforeT()
        {
            float v;
            Assert.True(ReplayStats.SpeedAt(Run().Samples, 1.5f, out v));
            Assert.Equal(5.5f, v);
            Assert.True(ReplayStats.SpeedAt(Run().Samples, 9f, out v));
            Assert.Equal(2f, v);
            Assert.False(ReplayStats.SpeedAt(new Attempt().Samples, 1f, out v));
        }

        [Fact]
        public void IndexAtFindsTheLastAtOrBefore()
        {
            Attempt a = Run();
            Assert.Equal(-1, ReplayStats.IndexAt(a.States, -0.5f));
            Assert.Equal(0, ReplayStats.IndexAt(a.States, 1.9f));
            Assert.Equal(1, ReplayStats.IndexAt(a.States, 2f));
            Assert.Equal(2, ReplayStats.IndexAt(a.Samples, 4f));
        }
    }
}
