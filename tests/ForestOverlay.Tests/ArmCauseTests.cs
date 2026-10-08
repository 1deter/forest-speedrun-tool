using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class ArmCauseTests
    {
        [Fact]
        public void NothingPending_GivesDefault()
        {
            Assert.Equal("spot reached", new ArmCause().Take());
        }

        [Fact]
        public void Set_IsTakenOnce()
        {
            ArmCause c = new ArmCause();
            c.Set("Go");
            Assert.Equal("Go", c.Take());
            Assert.Equal("spot reached", c.Take());
        }

        [Fact]
        public void LatestSetWins_AndEmptyClears()
        {
            ArmCause c = new ArmCause();
            c.Set("Go");
            c.Set("start-state restore");
            Assert.Equal("start-state restore", c.Take());
            c.Set("Go");
            c.Set("");
            Assert.Equal("spot reached", c.Take());
        }
    }
}
