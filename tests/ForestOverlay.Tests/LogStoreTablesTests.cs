using System.Collections.Generic;
using ForestOverlay.Game;
using Xunit;

namespace ForestOverlay.Tests
{
    public class LogStoreTablesTests
    {
        private static List<string> Keys(string[][] t)
        {
            List<string> k = new List<string>();
            foreach (string[] r in t) k.Add(r[0] + "." + r[1]);
            return k;
        }

        [Fact]
        public void TablesHaveNoDuplicatesAndAreDisjoint()
        {
            List<string> h = Keys(LogStoreTables.Holder), a = Keys(LogStoreTables.Arms);
            Assert.Equal(h.Count, new HashSet<string>(h).Count);
            Assert.Equal(a.Count, new HashSet<string>(a).Count);
            foreach (string k in h) Assert.DoesNotContain(k, a);
        }

        [Fact]
        public void ArmsCoversTheReaders()
        {
            List<string> a = Keys(LogStoreTables.Arms);
            Assert.Contains("TheForest.Items.Inventory.PlayerInventory.Equip", a);
            Assert.Contains("TheForest.Items.Inventory.PlayerInventory.HideAllEquiped", a);
            Assert.Equal(23, a.Count);
            Assert.Equal(10, LogStoreTables.Holder.Length);
        }
    }
}
