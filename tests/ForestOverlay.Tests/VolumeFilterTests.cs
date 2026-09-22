using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class VolumeFilterTests
    {
        [Fact]
        public void ExcludeFieldSplitsTrimsAndDropsBlanks()
        {
            Assert.Equal(new[] { "CaveLoad", "AreaBounds" },
                         VolumeFilter.ParseExclude(" CaveLoad ,, AreaBounds , "));
            Assert.Empty(VolumeFilter.ParseExclude(""));
            Assert.Empty(VolumeFilter.ParseExclude(null));
        }

        [Fact]
        public void ExcludeMatchesSubstringIgnoringCase()
        {
            string[] ex = { "caveload" };
            Assert.True(VolumeFilter.IsExcluded("Cave5_CaveLoadTrigger", ex));
            Assert.False(VolumeFilter.IsExcluded("KeycardDoor", ex));
            Assert.False(VolumeFilter.IsExcluded("anything", new string[0]));
        }

        [Fact]
        public void AddToExcludeAppendsOnceAndSkipsCoveredNames()
        {
            string t = VolumeFilter.AddToExclude("", "CaveLoad");
            Assert.Equal("CaveLoad", t);

            t = VolumeFilter.AddToExclude(t, "Terrain");
            Assert.Equal("CaveLoad, Terrain", t);

            // Already covered by "CaveLoad" - no duplicate.
            Assert.Equal(t, VolumeFilter.AddToExclude(t, "Cave5_CaveLoad"));
        }

        [Fact]
        public void LargestKeepsTopNLargestFirst()
        {
            LargestList l = new LargestList(3);
            l.Add("a", 5f);
            l.Add("b", 50f);
            l.Add("c", 20f);
            l.Add("d", 1f);   // too small, list is full
            l.Add("e", 30f);  // pushes "a" out

            Assert.Equal(3, l.Count);
            Assert.Equal(new[] { "b", "e", "c" }, new[] { l.Names[0], l.Names[1], l.Names[2] });
            Assert.Equal(new[] { 50f, 30f, 20f }, new[] { l.Sizes[0], l.Sizes[1], l.Sizes[2] });
        }

        [Fact]
        public void LargestKeepsOneEntryPerNameAtItsBiggestSize()
        {
            LargestList l = new LargestList(3);
            l.Add("tree", 10f);
            l.Add("tree", 40f);
            l.Add("tree", 5f);
            l.Add("rock", 20f);

            Assert.Equal(2, l.Count);
            Assert.Equal("tree", l.Names[0]);
            Assert.Equal(40f, l.Sizes[0]);
            Assert.Equal("rock", l.Names[1]);
        }

        [Fact]
        public void WouldRankOnlyWhenRoomOrBiggerThanTheSmallest()
        {
            LargestList l = new LargestList(2);
            Assert.True(l.WouldRank(1f));
            l.Add("a", 10f);
            l.Add("b", 20f);
            Assert.False(l.WouldRank(5f));
            Assert.True(l.WouldRank(15f));

            l.Clear();
            Assert.Equal(0, l.Count);
            Assert.True(l.WouldRank(0.1f));
        }
    }
}
