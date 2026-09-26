using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    public class CoordsFieldTests
    {
        [Theory]
        [InlineData("12.5 -3 40", "12.5 -3 40")]
        [InlineData("12a.5 -3 40", "12.5 -3 40")]
        [InlineData("(1, 2, 3)", "1, 2, 3")]
        [InlineData("1;2\t3", "1 2 3")]
        [InlineData("x=1 y=2 z=3", "1 2 3")]
        [InlineData("abc", "")]
        [InlineData("", "")]
        public void FiltersToNumberCharacters(string typed, string kept)
        {
            Assert.Equal(kept, TriggerParser.FilterCoords(typed));
        }

        [Fact]
        public void CleanTextIsReturnedAsIs()
        {
            string s = "1 2 3";
            Assert.Same(s, TriggerParser.FilterCoords(s));
        }

        [Theory]
        [InlineData("1 2 3 ", "1 2 3")]
        [InlineData(" 1 2 3,  ", "1 2 3")]
        [InlineData("1 2 ", "1 2 ")]
        [InlineData("1, ", "1, ")]
        [InlineData("1 2 3", "1 2 3")]
        public void TidyDropsSeparatorsOnlyAroundACompleteValue(string typed, string kept)
        {
            Assert.Equal(kept, TriggerParser.TidyCoords(typed));
        }

        [Fact]
        public void ParsesThreeNumbers()
        {
            Vector3 v;
            Assert.True(TriggerParser.ParseCoords("1, -2.5 3", out v));
            Assert.Equal(new Vector3(1f, -2.5f, 3f), v);
            Assert.True(TriggerParser.ParseCoords("(4 5 6)", out v));
            Assert.Equal(new Vector3(4f, 5f, 6f), v);
        }

        [Theory]
        [InlineData("1 2")]
        [InlineData("1 2 3 4")]
        [InlineData("1 - 3")]
        [InlineData("")]
        public void IncompleteIsRejected(string text)
        {
            Vector3 v;
            Assert.False(TriggerParser.ParseCoords(text, out v));
        }
    }
}
