using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class ItemWordsTests
    {
        [Fact]
        public void CodeNamesBecomeRunnerWords()
        {
            Assert.Equal("Survival book", ItemWords.Say("SurvivalBook"));
            Assert.Equal("Walkie talkie", ItemWords.Say("WalkyTalky"));
        }

        [Fact]
        public void OtherNamesAreUnchanged()
        {
            Assert.Equal("Axe", ItemWords.Say("Axe"));
            Assert.Equal("", ItemWords.Say(null));
        }
    }
}
