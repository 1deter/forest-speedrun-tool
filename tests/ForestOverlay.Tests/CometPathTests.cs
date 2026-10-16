using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class CometPathTests
    {
        [Fact]
        public void FadeFallsWithAge()
        {
            Assert.Equal(1f, CometPath.Fade(0f, 0.4f));
            Assert.Equal(0.5f, CometPath.Fade(0.2f, 0.4f), 3);
            Assert.Equal(0f, CometPath.Fade(0.4f, 0.4f));
            Assert.Equal(0f, CometPath.Fade(1f, 0.4f));
        }

        [Fact]
        public void StillMouseLeavesOnlyTheHead()
        {
            float[] x = new float[4], y = new float[4], t = new float[4];
            Assert.Equal(0, CometPath.Live(t, 0, 0, 4, 5f, 0.4f));
            Assert.Equal(0f, CometPath.Extent(x, y, t, 0, 0, 4, 5f, 0.4f, 0f, 0f));
            // points that have all faded count for nothing
            t[0] = 1f; x[0] = 9f;
            Assert.Equal(0, CometPath.Live(t, 1, 1, 4, 5f, 0.4f));
            Assert.Equal(0f, CometPath.Extent(x, y, t, 1, 1, 4, 5f, 0.4f, 0f, 0f));
        }

        [Fact]
        public void LiveStopsAtTheFirstOldPointAcrossTheRing()
        {
            float[] x = new float[3], y = new float[3], t = new float[3];
            t[2] = 1.0f; t[0] = 1.3f; t[1] = 1.45f;   // head 2: newest is t[1]
            Assert.Equal(3, CometPath.Live(t, 2, 3, 3, 1.5f, 0.6f));
            Assert.Equal(2, CometPath.Live(t, 2, 3, 3, 1.5f, 0.4f));
        }

        [Fact]
        public void FitKeepsDirection()
        {
            float[] x = { 4f, 2f }, y = { 1f, 0.5f }, t = { 1f, 1f };
            float e = CometPath.Extent(x, y, t, 2, 2, 2, 1f, 0.4f, 0f, 0f);
            Assert.Equal(4f, e);
            float s = CometPath.FitScale(e, 2f);
            Assert.Equal(0.5f, s);
            Assert.Equal(x[0] / y[0], (x[0] * s) / (y[0] * s), 4);   // same angle
            Assert.True(x[0] * s <= 2f && y[0] * s <= 2f);
        }

        [Fact]
        public void ATailThatFitsIsNotScaled()
        {
            Assert.Equal(1f, CometPath.FitScale(1f, 2f));
            Assert.Equal(1f, CometPath.FitScale(0f, 2f));
        }
    }
}
