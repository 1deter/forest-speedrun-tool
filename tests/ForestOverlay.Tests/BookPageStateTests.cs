using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // The book page state rides in a savestate header. A layout that does
    // not match must be refused, never applied to the wrong pages.
    // ------------------------------------------------------------------
    public class BookPageStateTests
    {
        [Fact]
        public void RoundTrip()
        {
            bool[] pages = { false, false, true, false, true };
            string value = BookPageState.Encode(pages);
            Assert.Equal("5:00101", value);

            bool[] back;
            string why;
            Assert.True(BookPageState.TryDecode(value, 5, out back, out why));
            Assert.Null(why);
            Assert.Equal(pages, back);
        }

        [Fact]
        public void OtherPageCountIsRefused()
        {
            bool[] back;
            string why;
            Assert.False(BookPageState.TryDecode("5:00101", 6, out back, out why));
            Assert.Null(back);
            Assert.Contains("6", why);
            Assert.Contains("5", why);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("00101")]
        [InlineData(":00101")]
        [InlineData("5:0010")]
        [InlineData("5:00121")]
        [InlineData("-5:00101")]
        public void MalformedIsRefused(string value)
        {
            bool[] back;
            string why;
            Assert.False(BookPageState.TryDecode(value, 5, out back, out why));
            Assert.Null(back);
            Assert.NotNull(why);
        }

        [Fact]
        public void SavestateHeaderCarriesIt()
        {
            SavestateFile s = new SavestateFile();
            s.Name = "book";
            s.Book = "3:010";
            s.Data = "abc";

            string error;
            SavestateFile back = SavestateFile.Parse(s.Write(), out error);
            Assert.Null(error);
            Assert.Equal("3:010", back.Book);
        }

        [Fact]
        public void SavestateHeaderCarriesHeldItems()
        {
            SavestateFile s = new SavestateFile();
            s.Held = new System.Collections.Generic.List<int> { 80, 53 };
            s.Data = "abc";

            string error;
            SavestateFile back = SavestateFile.Parse(s.Write(), out error);
            Assert.Null(error);
            Assert.Equal(new[] { 80, 53 }, back.Held);

            // Nothing held is still a held line, not an old file.
            s.Held.Clear();
            back = SavestateFile.Parse(s.Write(), out error);
            Assert.NotNull(back.Held);
            Assert.Empty(back.Held);
        }

        [Fact]
        public void OlderSavestateHasNoBook()
        {
            string error;
            SavestateFile back = SavestateFile.Parse(SavestateFile.Magic + "\nname = old\ndata = abc\n", out error);
            Assert.Null(error);
            Assert.Equal("", back.Book);
            Assert.Null(back.Held);
        }
    }
}
