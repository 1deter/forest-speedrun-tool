using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // Data/KeptRead (T-0202): when a kept state file / level data is
    // still the one asked for - anything that changes the file (size,
    // write time, another path) or the text reads anew.
    // ------------------------------------------------------------------
    public class KeptReadTests
    {
        private const string Path = @"C:\g\config\ForestOverlay\savestates\spot-a.fosave";

        private static KeptRead Kept()
        {
            KeptRead k = new KeptRead();
            k.Remember(Path, 300000, 638000000000000000);
            return k;
        }

        [Fact]
        public void NothingRemembered_NeverMatches()
        {
            KeptRead k = new KeptRead();
            Assert.False(k.Holds);
            Assert.False(k.Matches(Path, 0, 0));
            Assert.False(k.Matches(null, 0, 0));
        }

        [Fact]
        public void SameFile_Matches_PathCaseIgnored()
        {
            KeptRead k = Kept();
            Assert.True(k.Holds);
            Assert.True(k.Matches(Path, 300000, 638000000000000000));
            Assert.True(k.Matches(Path.ToUpperInvariant(), 300000, 638000000000000000));
        }

        [Fact]
        public void ACaptureOverIt_ReadsAnew()
        {
            KeptRead k = Kept();
            // Same size, new write time (a capture over the file).
            Assert.False(k.Matches(Path, 300000, 638000000000000001));
            // Same write time, another size.
            Assert.False(k.Matches(Path, 300001, 638000000000000000));
        }

        [Fact]
        public void AnotherFile_ReadsAnew()
        {
            KeptRead k = Kept();
            Assert.False(k.Matches(Path.Replace("spot-a", "spot-b"), 300000, 638000000000000000));
            Assert.False(k.Matches(null, 300000, 638000000000000000));
        }

        [Fact]
        public void Forget_ReadsAnew()
        {
            KeptRead k = Kept();
            k.Forget();
            Assert.False(k.Holds);
            Assert.False(k.Matches(Path, 300000, 638000000000000000));
        }

        [Fact]
        public void SameText_ByReferenceOrContent()
        {
            string a = "NOCOMPRESSION" + new string('x', 64);
            string equal = "NOCOMPRESSION" + new string('x', 64);
            Assert.False(ReferenceEquals(a, equal));
            Assert.True(KeptRead.SameText(a, a));
            Assert.True(KeptRead.SameText(a, equal));
        }

        [Fact]
        public void SameText_OtherTextOrNothing_IsNot()
        {
            string a = "NOCOMPRESSION" + new string('x', 64);
            Assert.False(KeptRead.SameText(a, "NOCOMPRESSION" + new string('x', 63) + "y"));   // same length
            Assert.False(KeptRead.SameText(a, a + "x"));
            Assert.False(KeptRead.SameText(null, a));
            Assert.False(KeptRead.SameText(a, null));
        }
    }
}
