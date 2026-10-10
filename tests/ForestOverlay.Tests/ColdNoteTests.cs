using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class ColdNoteTests
    {
        [Fact]
        public void WarmPlayer_SaysNothing()
        {
            Assert.Equal("", ColdNote.Line(37f, false, false, 0f, 0f, 0f, false));
        }

        [Fact]
        public void TemperatureAndFrost_Named()
        {
            Assert.Equal("body temperature 20 -> 37, frost 0.45 -> 0 (as a load)",
                ColdNote.Line(20f, false, true, 0f, 0.45f, 5f, false));
        }

        [Fact]
        public void ColdAtNormalTemperature_NoTemperatureWords()
        {
            Assert.Equal("cold off, frost 0.45 -> 0 (as a load)",
                ColdNote.Line(37f, true, false, 0f, 0.45f, 0f, false));
        }

        [Fact]
        public void OnlyHiddenFields_StillSaid()
        {
            Assert.Equal("cold reset (as a load)", ColdNote.Line(37f, false, false, 0.3f, 0f, 0f, false));
            Assert.Equal("cold reset (as a load)", ColdNote.Line(37f, false, false, 0f, 0f, 2f, false));
            Assert.Equal("cold reset (as a load)", ColdNote.Line(37f, false, false, 0f, 0f, 0f, true));
        }
    }
}
