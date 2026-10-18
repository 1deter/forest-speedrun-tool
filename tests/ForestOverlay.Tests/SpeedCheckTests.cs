using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class SpeedCheckTests
    {
        [Fact]
        public void ZeroAndOneAreSilent()
        {
            Assert.Null(SpeedCheck.TimeScaleFlag(0f));
            Assert.Null(SpeedCheck.TimeScaleFlag(1f));
        }

        [Theory]
        [InlineData(0.1f, "0.1")]
        [InlineData(2f, "2")]
        [InlineData(0.5f, "0.5")]
        [InlineData(1.0001f, "1")]
        public void OtherValuesAreFlagged(float scale, string shown)
        {
            Assert.Equal("the game's speed was changed (time scale " + shown + ")", SpeedCheck.TimeScaleFlag(scale));
        }
    }
}
