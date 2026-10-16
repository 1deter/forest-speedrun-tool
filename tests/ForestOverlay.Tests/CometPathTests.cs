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
        public void ManySmallMovesFromRestBuildATail()
        {
            // 0.01 of path per frame is under a 0.08 spacing: the origin must not reset each frame.
            // 0.004 of path per moved frame (a slow move): every frame records a point, the comet never goes dark.
            float[] x = new float[128], y = new float[128], t = new float[128];
            float lastMove = -100f, now = 10f, ux = 0f;
            int head = 0, count = 0;
            for (int f = 0; f < 60; f++)
            {
                now += 0.03f;
                if (CometPath.ShouldRestart(CometPath.Live(t, head, count, 128, now, 0.4f), now, lastMove, 0.4f)) { ux = 0f; count = 0; }
                lastMove = now;
                if (!CometPath.IsNewPoint(0.004f, 0f)) continue;
                ux += 0.004f;
                x[head] = ux; t[head] = now; head = (head + 1) % 128;
                if (count < 128) count++;
                Assert.True(CometPath.Live(t, head, count, 128, now, 0.4f) > 0);   // live on every frame
            }
            Assert.True(ux > 0.2f);
            Assert.True(CometPath.Live(t, head, count, 128, now, 0.4f) >= 13);   // 0.4 s at 30 ms a frame
            Assert.False(CometPath.IsNewPoint(0f, 0f));
        }

        [Fact]
        public void RestartsAfterRestOnly()
        {
            Assert.False(CometPath.ShouldRestart(0, 5.2f, 5.0f, 0.4f));
            Assert.True(CometPath.ShouldRestart(0, 5.5f, 5.0f, 0.4f));
            Assert.False(CometPath.ShouldRestart(1, 9f, 0f, 0.4f));
        }

        [Fact]
        public void ScaleDoesNotJumpWhenTheOldestPointFades()
        {
            // extent 4 (scale 0.25), then the far point fades and the fit says 1: it eases, no jump
            float s = 0.25f;
            s = CometPath.Ease(s, 1f, 0.016f, 1.5f);
            Assert.True(s < 0.27f);
            // growing the tail drops the scale at once
            Assert.Equal(0.1f, CometPath.Ease(0.5f, 0.1f, 0.016f, 1.5f));
        }

        [Fact]
        public void ATailThatFitsIsNotScaled()
        {
            Assert.Equal(1f, CometPath.FitScale(1f, 2f));
            Assert.Equal(1f, CometPath.FitScale(0f, 2f));
        }
    }
}
