using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class ItemCapsTests
    {
        [Fact]
        public void ParsesInOrderClampsAndSkipsJunk()
        {
            List<KeyValuePair<int, int>> caps = ItemCaps.Parse("53:50; 57:30;x:1;0:5;54:0;56:99999;53:60");
            Assert.Equal(4, caps.Count);
            Assert.Equal(new KeyValuePair<int, int>(53, 60), caps[0]);   // a repeat replaces, keeps its place
            Assert.Equal(new KeyValuePair<int, int>(57, 30), caps[1]);
            Assert.Equal(1, caps[2].Value);                               // clamped up
            Assert.Equal(ItemCaps.MaxCap, caps[3].Value);                 // clamped down
            Assert.Equal("53:60;57:30;54:1;56:9999", ItemCaps.Format(caps));
            Assert.Empty(ItemCaps.Parse(""));
            Assert.Empty(ItemCaps.Parse(null));
        }
    }
}
