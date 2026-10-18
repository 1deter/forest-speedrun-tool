using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class SpawnMarkTests
    {
        [Fact]
        public void FormatAndParseRoundTrip()
        {
            string line = SpawnMark.Format("w-12", -4935808);
            Assert.Equal("w-12:-4935808", line);
            Assert.Equal(-4935808, SpawnMark.Threshold(line, "w-12"));
        }

        [Fact]
        public void AnotherWorldGivesZeroUnknownMarkSkips()
        {
            Assert.Equal(0, SpawnMark.Threshold("w-12:-5", "w-13"));
            Assert.Equal(SpawnMark.Unknown, SpawnMark.Threshold("w-12:abc", "w-12"));
            Assert.Equal(SpawnMark.Unknown, SpawnMark.Threshold("w-12:7", "w-12"));
            Assert.Equal(SpawnMark.Unknown, SpawnMark.Threshold("", "w-12"));
            Assert.Equal(SpawnMark.Unknown, SpawnMark.Threshold(null, "w-12"));
            Assert.Equal(SpawnMark.Unknown, SpawnMark.Threshold("w-12:-5", ""));
            Assert.Equal("", SpawnMark.Format("", -5));
        }

        [Fact]
        public void IsAfterEdges()
        {
            Assert.True(SpawnMark.IsAfter(-4936058, -4935808));
            Assert.False(SpawnMark.IsAfter(-4935808, -4935808));
            Assert.False(SpawnMark.IsAfter(-4935000, -4935808));
            Assert.False(SpawnMark.IsAfter(12345, -4935808));
            Assert.False(SpawnMark.IsAfter(12345, 0));
            Assert.True(SpawnMark.IsAfter(-1, 0));
            Assert.False(SpawnMark.IsAfter(0, 0));
        }

        [Fact]
        public void SameWorldNeedsARealNegativeMark()
        {
            Assert.False(SpawnMark.IsSameWorld(SpawnMark.Unknown));
            Assert.False(SpawnMark.IsSameWorld(0));
            Assert.True(SpawnMark.IsSameWorld(-4935808));
            Assert.False(SpawnMark.IsSameWorld(SpawnMark.Threshold("", "w-1")));
            Assert.False(SpawnMark.IsSameWorld(SpawnMark.Threshold("w-2:-5", "w-1")));
            Assert.True(SpawnMark.IsSameWorld(SpawnMark.Threshold("w-1:-5", "w-1")));
        }
    }
}
