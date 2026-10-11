using System;
using System.Text;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class LevelTextTests
    {
        // The game's wrapper, as LevelSerializer.SerializeLevel(bool) builds it (source).
        private static string Wrapper(byte[] bytes)
        {
            StringBuilder sb = new StringBuilder("NOCOMPRESSION");
            sb.Append(Convert.ToBase64String(bytes));
            return sb.ToString();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(193519)]
        public void FromBytesIsTheWrappersTextAndCharsItsLength(int n)
        {
            byte[] bytes = new byte[n];
            new Random(n).NextBytes(bytes);
            string text = LevelText.FromBytes(bytes);
            Assert.Equal(Wrapper(bytes), text);
            Assert.Equal(text.Length, LevelText.Chars(null, bytes));
            Assert.Equal(text.Length, LevelText.Chars(text, null));
        }

        [Fact]
        public void NothingIsZero()
        {
            Assert.Equal(0, LevelText.Chars(null, null));
        }
    }
}
