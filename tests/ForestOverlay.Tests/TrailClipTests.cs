using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class TrailClipTests
    {
        [Fact]
        public void InsideKeepsAll()
        {
            float a, b;
            Assert.True(TrailClip.Clip(1, 1, 5, 5, 0, 0, 10, 10, out a, out b));
            Assert.Equal(0f, a); Assert.Equal(1f, b);
        }

        [Fact]
        public void ReachesTheEdge()
        {
            float a, b;
            Assert.True(TrailClip.Clip(8, 5, 12, 5, 0, 0, 10, 10, out a, out b));
            Assert.Equal(0f, a); Assert.Equal(0.5f, b, 4);
        }

        [Fact]
        public void OutsideDropped()
        {
            float a, b;
            Assert.False(TrailClip.Clip(11, 5, 15, 5, 0, 0, 10, 10, out a, out b));
        }
    }
}
