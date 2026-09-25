using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // A pooled tree's greeble zone at capture: the record must come back
    // exactly (a wrong seed is a different stick layout), and "already
    // matches" must look only at the seed and the taken flags.
    // ------------------------------------------------------------------
    public class GreebleRecordTests
    {
        private static GreebleRecord Sample()
        {
            GreebleRecord r = new GreebleRecord();
            r.X = 501.23f; r.Y = 76.37f; r.Z = 90.3f;
            r.Seed = 11525;
            r.States = new byte[] { 253, 255, 252, 254 };
            return r;
        }

        [Fact]
        public void RoundTrips()
        {
            GreebleRecord r = Sample();
            Assert.Equal("501.23,76.37,90.30:11525:fdfffcfe", r.Format());

            GreebleRecord back;
            Assert.True(GreebleRecord.TryParse(r.Format(), out back));
            Assert.Equal(r.Seed, back.Seed);
            Assert.Equal(r.States, back.States);
            Assert.Equal(501.23f, back.X, 2);
            Assert.Equal(90.3f, back.Z, 2);
        }

        [Fact]
        public void NegativeSeedsAndNoInstancesParse()
        {
            GreebleRecord back;
            Assert.True(GreebleRecord.TryParse("-3.50,0.00,12.00:-42:", out back));
            Assert.Equal(-42, back.Seed);
            Assert.Empty(back.States);
        }

        [Theory]
        [InlineData("")]
        [InlineData("1,2,3:4")]
        [InlineData("1,2:4:ff")]
        [InlineData("1,2,3:x:ff")]
        [InlineData("1,2,3:4:f")]
        [InlineData("1,2,3:4:zz")]
        public void RejectsBadEntries(string text)
        {
            GreebleRecord r;
            Assert.False(GreebleRecord.TryParse(text, out r));
        }

        [Fact]
        public void ParseAllCountsBadEntries()
        {
            int bad;
            List<GreebleRecord> list = GreebleRecord.ParseAll(new[] { Sample().Format(), "junk" }, out bad);
            Assert.Single(list);
            Assert.Equal(1, bad);
            Assert.Empty(GreebleRecord.ParseAll(null, out bad));
        }

        [Fact]
        public void FindsTheRecordAtAPlace()
        {
            List<GreebleRecord> list = new List<GreebleRecord> { Sample() };
            Assert.Equal(0, GreebleRecord.IndexAt(list, 501.25f, 76.36f, 90.31f));
            Assert.Equal(-1, GreebleRecord.IndexAt(list, 501.5f, 76.37f, 90.3f));
        }

        [Fact]
        public void MatchesOnSeedAndTakenFlagsOnly()
        {
            GreebleRecord r = Sample();
            // Active-state flavours differ between spawns; taken does not.
            Assert.True(r.Matches(11525, new byte[] { 252, 255, 253, 253 }));
            Assert.False(r.Matches(11680, new byte[] { 253, 255, 252, 254 }));
            Assert.False(r.Matches(11525, new byte[] { 253, 253, 252, 254 }));
            Assert.False(r.Matches(11525, new byte[] { 253, 255, 252 }));
            Assert.False(r.Matches(11525, null));
        }
    }
}
