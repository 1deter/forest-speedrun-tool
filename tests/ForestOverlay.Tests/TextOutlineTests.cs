using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class TextOutlineTests
    {
        [Fact]
        public void EightDistinctNonZeroDirections()
        {
            Assert.Equal(8, TextOutline.Dx.Length);
            Assert.Equal(8, TextOutline.Dy.Length);
            var seen = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < 8; i++)
            {
                Assert.False(TextOutline.Dx[i] == 0f && TextOutline.Dy[i] == 0f);
                Assert.True(seen.Add(TextOutline.Dx[i] + "," + TextOutline.Dy[i]));
            }
        }

        [Fact]
        public void UnitIsScreenHeightOver720()
        {
            Assert.Equal(1.5f, TextOutline.UnitPx(1080f), 3);
            Assert.Equal(2f, TextOutline.UnitPx(1440f), 3);
            Assert.Equal(1f, TextOutline.UnitPx(0f), 3);
        }

        [Fact]
        public void DistanceIsInDrawnUnits()
        {
            Assert.Equal(1.5f, TextOutline.Distance(1080f, 1f), 3);
            Assert.Equal(2f / 1.875f, TextOutline.Distance(1440f, 1.875f), 3);
            Assert.Equal(1f, TextOutline.Distance(720f, 0f), 3);
        }
    }
}
