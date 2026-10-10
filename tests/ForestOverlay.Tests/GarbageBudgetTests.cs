using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // Data/GarbageBudget (T-0202): a restore's forced collection runs once
    // the garbage since the last one reaches the budget, and always when
    // no collection has been seen yet.
    // ------------------------------------------------------------------
    public class GarbageBudgetTests
    {
        private const long Mb = 1024 * 1024;

        [Fact]
        public void UnderBudget_Skipped()
        {
            Assert.False(GarbageBudget.Due(300 * Mb, 280 * Mb, 64 * Mb));
        }

        [Fact]
        public void AtOrOverBudget_Runs()
        {
            Assert.True(GarbageBudget.Due(344 * Mb, 280 * Mb, 64 * Mb));
            Assert.True(GarbageBudget.Due(400 * Mb, 280 * Mb, 64 * Mb));
        }

        [Fact]
        public void NoCollectionSeen_Runs()
        {
            Assert.True(GarbageBudget.Due(300 * Mb, 0, 64 * Mb));
        }

        [Fact]
        public void HeapBelowBaseline_Skipped()
        {
            // A heap reading under the last baseline (memory given back)
            // is no garbage.
            Assert.False(GarbageBudget.Due(270 * Mb, 280 * Mb, 64 * Mb));
        }

        [Fact]
        public void Budget_IsUnderTheCollectorsOwnVolume()
        {
            // game-notes: 218 MB of garbage = one collection on a fresh heap.
            Assert.True(GarbageBudget.Bytes < 100 * Mb);
        }
    }
}
