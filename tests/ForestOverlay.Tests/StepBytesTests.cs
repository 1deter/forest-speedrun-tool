using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // Data/StepBytes (T-0202): a restore's garbage per step - differences
    // of a running counter, a step seen twice adds up, the line lists the
    // biggest first and folds the small ones.
    // ------------------------------------------------------------------
    public class StepBytesTests
    {
        private const long Mb = 1024 * 1024;

        [Fact]
        public void Steps_AreDifferences_BiggestFirst()
        {
            StepBytes s = new StepBytes();
            s.Begin(100 * Mb);
            s.Mark("read", 101 * Mb);
            s.Mark("load", 106 * Mb);
            s.Mark("keepers", 108 * Mb);
            Assert.Equal(8 * Mb, s.Total);
            Assert.Equal("8.0 MB: load 5.0, keepers 2.0, read 1.0", s.Describe());
        }

        [Fact]
        public void SameStepTwice_AddsUp()
        {
            StepBytes s = new StepBytes();
            s.Begin(0);
            s.Mark("frames", 2 * Mb);
            s.Mark("other", 3 * Mb);
            s.Mark("frames", 5 * Mb);
            Assert.Equal(4 * Mb, s.BytesOf("frames"));
            Assert.Equal("5.0 MB: frames 4.0, other 1.0", s.Describe());
        }

        [Fact]
        public void SmallSteps_Folded()
        {
            StepBytes s = new StepBytes();
            s.Begin(0);
            s.Mark("big", 3 * Mb);
            s.Mark("tiny", 3 * Mb + 1000);
            s.Mark("tiny2", 3 * Mb + 2000);
            Assert.Equal("3.0 MB: big 3.0, 2 small", s.Describe());
        }

        [Fact]
        public void MarkBeforeBegin_Ignored()
        {
            StepBytes s = new StepBytes();
            s.Mark("x", 5 * Mb);
            Assert.False(s.Started);
            Assert.Equal(0, s.Total);
        }

        [Fact]
        public void Begin_StartsOver()
        {
            StepBytes s = new StepBytes();
            s.Begin(0);
            s.Mark("a", Mb);
            s.Begin(10 * Mb);
            s.Mark("b", 12 * Mb);
            Assert.Equal(0, s.BytesOf("a"));
            Assert.Equal("2.0 MB: b 2.0", s.Describe());
        }
    }
}
