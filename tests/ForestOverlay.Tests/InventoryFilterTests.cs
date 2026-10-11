using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class InventoryFilterTests
    {
        // Stand-in names: the filter only sees strings.
        private static readonly string[] Catalog = { "Alpha", "Beta Rope", "Rope", "rope", "Ropes", "Stick", null, "Tape" };

        private static List<string> NotHeld(string filter, params string[] held)
        {
            List<int> idx = new List<int>();
            InventoryFilter.NotHeld(Catalog, held, filter, idx);
            List<string> names = new List<string>();
            for (int i = 0; i < idx.Count; i++) names.Add(Catalog[idx[i]]);
            return names;
        }

        [Fact]
        public void MatchesIgnoresCaseAndKeepsAllOnAnEmptyFilter()
        {
            Assert.True(InventoryFilter.Matches("Duct Tape", "TAPE"));
            Assert.True(InventoryFilter.Matches("Duct Tape", ""));
            Assert.True(InventoryFilter.Matches("Duct Tape", null));
            Assert.False(InventoryFilter.Matches("Duct Tape", "rope"));
            Assert.False(InventoryFilter.Matches(null, "rope"));
        }

        [Fact]
        public void ListsMatchesNotHeldPrefixFirstEachNameOnce()
        {
            // "Rope" and "rope" are one name; "Beta Rope" only contains it.
            Assert.Equal(new[] { "Rope", "Ropes", "Beta Rope" }, NotHeld("rope"));
        }

        [Fact]
        public void LeavesOutWhatIsHeldCaseIgnored()
        {
            Assert.Equal(new[] { "Ropes", "Beta Rope" }, NotHeld("RoPe", "ROPE", "Stick"));
            Assert.Empty(NotHeld("stick", "Stick"));
        }

        [Fact]
        public void AnEmptyFilterOrNoCatalogueListsNothing()
        {
            Assert.Empty(NotHeld(""));
            Assert.Empty(NotHeld(null));
            List<int> idx = new List<int> { 3 };
            InventoryFilter.NotHeld(null, null, "rope", idx);
            Assert.Empty(idx);   // cleared, not appended to
        }

        [Fact]
        public void NoHeldListMeansNothingIsHeld()
        {
            List<int> idx = new List<int>();
            InventoryFilter.NotHeld(Catalog, null, "tape", idx);
            Assert.Equal(new List<int> { 7 }, idx);
        }
    }
}
