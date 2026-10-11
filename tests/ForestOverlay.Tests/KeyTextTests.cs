using System.Text.RegularExpressions;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class KeyTextTests
    {
        [Fact]
        public void WithNamesTheKeyOnlyWhenBound()
        {
            Assert.Equal("restart (R)", KeyText.With("restart", "R"));
            Assert.Equal("restart (F7)", KeyText.With("restart", "F7"));
            Assert.Equal("restart", KeyText.With("restart", null));
            Assert.Equal("restart", KeyText.With("restart", ""));
        }

        [Fact]
        public void FillPutsTheKeyInItsSlotOrTakesTheUnboundText()
        {
            Assert.Equal("The spot R restarts.", KeyText.Fill("The spot {key} restarts.", "R", "none"));
            Assert.Equal("none", KeyText.Fill("The spot {key} restarts.", null, "none"));
            // No slot: the template as it is, bound or not.
            Assert.Equal("Speed.", KeyText.Fill("Speed.", null, "none"));
            Assert.Equal("Speed.", KeyText.Fill("Speed.", "R", "none"));
        }

        [Fact]
        public void HudDescriptionsReadTheRunnersKey()
        {
            int keyed = 0;
            foreach (HudLine l in HudLines.All)
            {
                // T-0262: never a fixed default key in a description.
                Assert.False(Regex.IsMatch(l.Description, @"\bF\d{1,2}\b"), l.Name + ": " + l.Description);
                if (l.KeyId == null)
                {
                    Assert.DoesNotContain(KeyText.Slot, l.Description);
                    Assert.Equal(l.Description, HudLines.Describe(l, "R"));
                    continue;
                }
                keyed++;
                Assert.Contains(KeyText.Slot, l.Description);
                Assert.False(string.IsNullOrEmpty(l.Unbound), l.Name);
                Assert.DoesNotContain(KeyText.Slot, l.Unbound);
                string bound = HudLines.Describe(l, "LeftBracket");
                Assert.Contains("LeftBracket", bound);
                Assert.DoesNotContain(KeyText.Slot, bound);
                Assert.Equal(l.Unbound, HudLines.Describe(l, null));
            }
            Assert.Equal(2, keyed);   // the dump status, the current spot
            Assert.Equal("The spot R restarts.", HudLines.Describe(HudLines.All[HudLines.Find("practice", "Spot")], "R"));
        }
    }
}
