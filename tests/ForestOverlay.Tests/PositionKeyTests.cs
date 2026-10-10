using System;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // Data/PositionKey (T-0202): the number for a position equals the
    // number read back from PickupKey's text, and two positions share a
    // number exactly when they share the text - so PanelKeeper can match
    // the `panels` header without building 490 strings a restore.
    // ------------------------------------------------------------------
    public class PositionKeyTests
    {
        private static string Text(float x, float y, float z)
        {
            string k = SavestateFile.PickupKey(0, x, y, z);
            return k.Substring(k.IndexOf('@') + 1);
        }

        private static long Parsed(string text)
        {
            long key;
            Assert.True(PositionKey.TryParse(text, 0, out key), text);
            return key;
        }

        [Fact]
        public void Number_MatchesTheText_ForManyPositions()
        {
            Random r = new Random(202);
            for (int i = 0; i < 20000; i++)
            {
                float x = (float)(r.NextDouble() * 4000 - 2000);
                float y = (float)(r.NextDouble() * 1000 - 500);
                float z = (float)(r.NextDouble() * 4000 - 2000);
                Assert.Equal(Parsed(Text(x, y, z)), PositionKey.Of(x, y, z));
            }
        }

        [Fact]
        public void EqualNumbers_ExactlyWhenEqualText()
        {
            Random r = new Random(7);
            for (int i = 0; i < 20000; i++)
            {
                float x = (float)(r.NextDouble() * 200 - 100);
                float y = (float)(r.NextDouble() * 20 - 10);
                float z = (float)(r.NextDouble() * 200 - 100);
                // A neighbour within a few centimetres: sometimes the same tenth.
                float x2 = x + (float)(r.NextDouble() * 0.1 - 0.05);
                bool sameText = Text(x, y, z) == Text(x2, y, z);
                bool sameKey = PositionKey.Of(x, y, z) == PositionKey.Of(x2, y, z);
                Assert.Equal(sameText, sameKey);
            }
        }

        [Fact]
        public void NegativeZero_IsZero()
        {
            Assert.Equal("0.0,0.0,0.0", Text(-0.04f, 0f, 0.04f));
            Assert.Equal(PositionKey.Of(0f, 0f, 0f), PositionKey.Of(-0.04f, 0f, 0.04f));
            Assert.Equal(PositionKey.Of(0f, 0f, 0f), Parsed("-0.0,0.0,0.0"));
        }

        [Fact]
        public void Entry_ReadsValueAndPosition()
        {
            int h;
            long key;
            Assert.True(PositionKey.TryParseEntry("50@-442.2,-19.6,669.3", out h, out key));
            Assert.Equal(50, h);
            Assert.Equal(PositionKey.Of(-442.2f, -19.6f, 669.3f), key);
            Assert.True(PositionKey.TryParseEntry("-3@1.0,2.0,3.0", out h, out key));
            Assert.Equal(-3, h);
        }

        [Fact]
        public void OtherNumberForms_GoThroughTheSameRounding()
        {
            Assert.Equal(PositionKey.Of(1.25f, 2f, -3.04f), Parsed("1.25,2,-3.04"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("1.0,2.0")]
        [InlineData("1.0,2.0,3.0,4.0")]
        [InlineData("a,2.0,3.0")]
        [InlineData("1.0,,3.0")]
        public void NotThreeNumbers_Refused(string text)
        {
            long key;
            Assert.False(PositionKey.TryParse(text, 0, out key));
        }

        [Theory]
        [InlineData("@1.0,2.0,3.0")]
        [InlineData("x@1.0,2.0,3.0")]
        [InlineData("50-1.0,2.0,3.0")]
        [InlineData(null)]
        public void BadEntry_Refused(string e)
        {
            int h;
            long key;
            Assert.False(PositionKey.TryParseEntry(e, out h, out key));
        }
    }
}
