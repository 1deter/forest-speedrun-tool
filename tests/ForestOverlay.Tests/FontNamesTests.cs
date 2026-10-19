using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class FontNamesTests
    {
        private static readonly string[] Names = { "Arial", "Consolas", "Verdana" };

        [Fact]
        public void Find_IgnoresCase_AndEmptyIsNoMatch()
        {
            Assert.Equal("Consolas", FontNames.Find(Names, " consolas "));
            Assert.Null(FontNames.Find(Names, "Comic"));
            Assert.Null(FontNames.Find(Names, ""));
        }

        [Fact]
        public void Step_WalksTheRing()
        {
            Assert.Equal("Arial", FontNames.Step(Names, "", 1));
            Assert.Equal("Consolas", FontNames.Step(Names, "Arial", 1));
            Assert.Equal("", FontNames.Step(Names, "Verdana", 1));
            Assert.Equal("Verdana", FontNames.Step(Names, "", -1));
            Assert.Equal("", FontNames.Step(Names, "Arial", -1));
        }

        [Fact]
        public void Step_UnknownCurrentIsDefault_NoFontsGivesDefault()
        {
            Assert.Equal("Arial", FontNames.Step(Names, "Gone", 1));
            Assert.Equal("", FontNames.Step(new string[0], "Arial", 1));
        }
    }
}
