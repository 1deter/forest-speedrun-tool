using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // Savestates are meant to travel with a segment. The game's data line
    // must come back byte for byte, and a header value must never be able
    // to break the file.
    // ------------------------------------------------------------------
    public class SavestateFileTests
    {
        private static SavestateFile Sample()
        {
            SavestateFile s = new SavestateFile();
            s.Name = "Plane crash start";
            s.Level = "ForestMain_v07";
            s.Difficulty = "Normal";
            s.Created = "2026-09-23 21:04:11";
            s.PluginVersion = "0.20.0";
            s.X = -1000.5f; s.Y = 90.25f; s.Z = 550.125f;
            s.InCave = true;
            s.Data = "H4sIAAAAAAAEAO29B2AcSZYlJi9tynt/SvVK1+B0oQiAYBMk2JBAEOzBiM3mkuwdaUcjKasqgcplVmVdZhZAzO2dvPfee++99+==";
            return s;
        }

        [Fact]
        public void RoundTripKeepsEveryField()
        {
            string error;
            SavestateFile back = SavestateFile.Parse(Sample().Write(), out error);

            Assert.Null(error);
            Assert.NotNull(back);
            SavestateFile s = Sample();
            Assert.Equal(s.Name, back.Name);
            Assert.Equal(s.Level, back.Level);
            Assert.Equal(s.Difficulty, back.Difficulty);
            Assert.Equal(s.Created, back.Created);
            Assert.Equal(s.PluginVersion, back.PluginVersion);
            Assert.Equal(-1000.5f, back.X);
            Assert.Equal(90.25f, back.Y);
            Assert.Equal(550.13f, back.Z, 2);
            Assert.True(back.InCave);
            Assert.Equal(s.Data, back.Data);
        }

        [Fact]
        public void DataWithEqualsSignsSurvives()
        {
            // Base64 padding is '='; the key/value split must use the first
            // '=' only.
            SavestateFile s = Sample();
            s.Data = "abc==";
            string error;
            Assert.Equal("abc==", SavestateFile.Parse(s.Write(), out error).Data);
        }

        [Fact]
        public void CrLfLineEndingsParse()
        {
            string error;
            SavestateFile back = SavestateFile.Parse(Sample().Write().Replace("\n", "\r\n"), out error);
            Assert.Null(error);
            Assert.Equal(Sample().Data, back.Data);
            Assert.Equal("Plane crash start", back.Name);
        }

        [Fact]
        public void NewlineInNameCannotInjectAKey()
        {
            SavestateFile s = Sample();
            s.Name = "evil\ndata = nope";
            string error;
            SavestateFile back = SavestateFile.Parse(s.Write(), out error);
            Assert.Equal(Sample().Data, back.Data);
            Assert.Equal("evil data = nope", back.Name);
        }

        [Fact]
        public void WrongHeaderIsRejected()
        {
            string error;
            Assert.Null(SavestateFile.Parse("id = deter/route.x\nname = y\n", out error));
            Assert.Contains("not a ForestOverlay savestate", error);
        }

        [Fact]
        public void MissingDataIsRejected()
        {
            string error;
            Assert.Null(SavestateFile.Parse(SavestateFile.Magic + "\nname = x\n", out error));
            Assert.Equal("no data line", error);
        }

        [Fact]
        public void EmptyIsRejected()
        {
            string error;
            Assert.Null(SavestateFile.Parse("", out error));
            Assert.NotNull(error);
        }

        [Fact]
        public void UnknownKeysAreIgnored()
        {
            string error;
            SavestateFile back = SavestateFile.Parse(Sample().Write() + "future = 1\n", out error);
            Assert.Null(error);
            Assert.Equal(Sample().Data, back.Data);
        }

        [Theory]
        [InlineData("Plane crash start", "Plane-crash-start")]
        [InlineData("cave 5: keycard?", "cave-5-keycard")]
        [InlineData("a/b\\c", "a-b-c")]
        [InlineData("  ..  ", "savestate")]
        [InlineData("", "savestate")]
        [InlineData(null, "savestate")]
        public void SafeFileName(string name, string expected)
        {
            Assert.Equal(expected, SavestateFile.SafeFileName(name));
        }
    }
}
