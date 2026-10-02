using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // A savestate's `blueprints` header (Game/BlueprintKeeper rebuilds a
    // blueprint whose counts changed since) and the build HUD's tally.
    // ------------------------------------------------------------------
    public class BlueprintStateTests
    {
        [Fact]
        public void WritesAndParses()
        {
            string text = BlueprintState.Write(new[] { "a3aabd9b-1c2d", "77e0-ff" },
                                               new List<int[]> { new[] { 1, 1, 0 }, new int[0] });
            Assert.Equal("a3aabd9b-1c2d:1,1,0;77e0-ff:", text);

            Dictionary<string, int[]> back = BlueprintState.Parse(text);
            Assert.Equal(2, back.Count);
            Assert.Equal(new[] { 1, 1, 0 }, back["a3aabd9b-1c2d"]);
            Assert.Empty(back["77e0-ff"]);
        }

        [Fact]
        public void NoLineIsNullAndNoneIsEmpty()
        {
            Assert.Null(BlueprintState.Parse(null));
            Assert.Empty(BlueprintState.Parse(""));
            Assert.Equal("", BlueprintState.Write(new string[0], new List<int[]>()));
        }

        [Fact]
        public void SkipsMalformedEntriesAndIds()
        {
            Dictionary<string, int[]> back = BlueprintState.Parse("good:2,3;bad:x,1;:4;noamounts");
            Assert.Single(back);
            Assert.Equal(new[] { 2, 3 }, back["good"]);

            Assert.Equal("ok:5", BlueprintState.Write(new[] { "a;b", "", "ok" },
                                                      new List<int[]> { new[] { 1 }, new[] { 1 }, new[] { 5 } }));
        }

        [Fact]
        public void SameComparesCounts()
        {
            Assert.True(BlueprintState.Same(new[] { 1, 1, 0 }, new[] { 1, 1, 0 }));
            Assert.False(BlueprintState.Same(new[] { 1, 1, 0 }, new[] { 3, 1, 0 }));
            Assert.False(BlueprintState.Same(new[] { 1, 1 }, new[] { 1, 1, 0 }));
        }

        [Fact]
        public void TallySumsWhatEachBlueprintStillNeeds()
        {
            // The audit's shelter (7 logs, 7 sticks, 6 rocks; 1 log and 1
            // stick in) and a log holder (logs only, done) - "GATHER" 6 / 6 / 6.
            Dictionary<int, int> tally = new Dictionary<int, int>();
            BlueprintState.AddNeeded(tally, new[] { 78, 57, 53 }, new[] { 7, 7, 6 }, new[] { 1, 1, 0 });
            BlueprintState.AddNeeded(tally, new[] { 78 }, new[] { 10 }, new[] { 12 });
            Assert.Equal(6, tally[78]);
            Assert.Equal(6, tally[57]);
            Assert.Equal(6, tally[53]);

            BlueprintState.AddNeeded(tally, new[] { 78, 57 }, new[] { 4, 2 }, null);
            Assert.Equal(10, tally[78]);
            Assert.Equal(8, tally[57]);
        }
    }
}
