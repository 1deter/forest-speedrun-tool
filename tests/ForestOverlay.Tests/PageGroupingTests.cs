using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class PageGroupingTests
    {
        // book(1) > pages(2) > birds(10) / fish(20), ticks 100+
        [Fact]
        public void TicksGroupByTheChildOfTheirCommonAncestor()
        {
            List<int[]> chains = new List<int[]>
            {
                new[] { 1, 2, 10, 101 },
                new[] { 1, 2, 10, 102 },
                new[] { 1, 2, 20, 201 },
            };

            Assert.Equal(new[] { 10, 10, 20 }, PageGrouping.PageIds(chains));
        }

        [Fact]
        public void DeeperNestingOnOnePageStillGroupsByPage()
        {
            List<int[]> chains = new List<int[]>
            {
                new[] { 1, 2, 10, 11, 12, 101 },
                new[] { 1, 2, 10, 102 },
                new[] { 1, 2, 20, 201 },
            };

            Assert.Equal(new[] { 10, 10, 20 }, PageGrouping.PageIds(chains));
        }

        [Fact]
        public void MissingTicksAreLeftUngrouped()
        {
            List<int[]> chains = new List<int[]>
            {
                new[] { 1, 2, 10, 101 },
                null,
                new int[0],
                new[] { 1, 2, 20, 201 },
            };

            Assert.Equal(new[] { 10, PageGrouping.NoPage, PageGrouping.NoPage, 20 },
                         PageGrouping.PageIds(chains));
        }

        [Fact]
        public void ASingleTickBelongsToItsParent()
        {
            List<int[]> chains = new List<int[]> { new[] { 1, 2, 10, 101 } };

            Assert.Equal(new[] { 10 }, PageGrouping.PageIds(chains));
        }

        [Fact]
        public void TicksWithNoCommonRootStillGetAPage()
        {
            List<int[]> chains = new List<int[]>
            {
                new[] { 1, 101 },
                new[] { 5, 501 },
            };

            Assert.Equal(new[] { 1, 5 }, PageGrouping.PageIds(chains));
        }

        [Fact]
        public void NothingToGroup()
        {
            Assert.Empty(PageGrouping.PageIds(new List<int[]>()));
        }
    }
}
