using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class ProfileUnitsTests
    {
        [Fact]
        public void XFollowsWidthYAndSizeFollowHeight()
        {
            Assert.Equal(200f, ProfileUnits.Convert(100f, ProfileUnit.X, 1920, 1080, 3840, 2160));
            Assert.Equal(150f, ProfileUnits.Convert(100f, ProfileUnit.Y, 1920, 1080, 2560, 1620));
            Assert.Equal(75f, ProfileUnits.Convert(100f, ProfileUnit.Size, 1920, 1080, 1440, 810));
            Assert.Equal(75f, ProfileUnits.Convert(100f, ProfileUnit.X, 1920, 1080, 1440, 1080));
        }

        [Fact]
        public void SentinelsNoUnitAndSameReferenceStay()
        {
            Assert.Equal(-1f, ProfileUnits.Convert(-1f, ProfileUnit.X, 1920, 1080, 3840, 2160));
            Assert.Equal(100f, ProfileUnits.Convert(100f, ProfileUnit.None, 1920, 1080, 3840, 2160));
            Assert.Equal(100f, ProfileUnits.Convert(100f, ProfileUnit.X, 1920, 1080, 1920, 1080));
            Assert.Equal(100f, ProfileUnits.Convert(100f, ProfileUnit.X, 0, 0, 3840, 2160));
        }

        [Fact]
        public void ParseDimIsForgiving()
        {
            Assert.Equal(1920, ProfileUnits.ParseDim(" 1920 "));
            Assert.Equal(0, ProfileUnits.ParseDim(null));
            Assert.Equal(0, ProfileUnits.ParseDim("x"));
            Assert.Equal(0, ProfileUnits.ParseDim("-5"));
        }
    }
}
