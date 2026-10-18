using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class LitFiresTests
    {
        private const string Cave = "Cave_FiresAll/C6_Fires/Cave_FireBuilt(Clone)/Trigger@891.2,46.1,73.4";
        private const string Built = "FireBuilt(Clone)/Trigger@431,76.1,-4.5";

        [Fact]
        public void RoundTripKeepsEveryKeyAndIgnoresCulture()
        {
            var old = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            try
            {
                string line = LitFires.Format(new List<string> { Cave, Built });
                Assert.Equal(Cave + ";" + Built, line);
                HashSet<string> back = LitFires.Parse(line);
                Assert.Equal(2, back.Count);
                Assert.Contains(Cave, back);
                Assert.Contains(Built, back);
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = old; }
        }

        [Fact]
        public void EmptyLineIsAnEmptySet()
        {
            Assert.Equal("", LitFires.Format(new List<string>()));
            Assert.Equal("", LitFires.Format(null));
            Assert.Empty(LitFires.Parse(""));
            Assert.Empty(LitFires.Parse(null));
        }

        [Fact]
        public void GarbageGivesAnEmptySet()
        {
            Assert.Empty(LitFires.Parse("not a fire;@1,2,3;Trigger@1,2;Trigger@a,b,c;;;"));
            // A good key among garbage survives.
            HashSet<string> one = LitFires.Parse("junk;" + Cave + ";x@1,2");
            Assert.Single(one);
            Assert.Contains(Cave, one);
        }

        [Fact]
        public void FormatDropsKeysThatWouldNotParse()
        {
            Assert.Equal(Built, LitFires.Format(new List<string> { "a;b@1,2,3", Built, "" }));
        }

        [Fact]
        public void SinceIsTheMarkForThisWorldOnly()
        {
            Assert.Equal(7, LitFires.SinceOf("3f2a9c1e-41234:7", "3f2a9c1e-41234"));
            Assert.Equal(-1, LitFires.SinceOf("3f2a9c1e-41234:7", "3f2a9c1e-99999"));   // another world
            Assert.Equal(-1, LitFires.SinceOf("", "3f2a9c1e-41234"));                    // no mark
            Assert.Equal(-1, LitFires.SinceOf("3f2a9c1e-41234:7", ""));                  // not in a game
            Assert.Equal(-1, LitFires.SinceOf("3f2a9c1e-41234:x", "3f2a9c1e-41234"));
            Assert.Equal(0, LitFires.SinceOf("w:0", "w"));
            Assert.True(LitFires.SameWorld("w:0", "w"));
            Assert.False(LitFires.SameWorld("", "w"));
            Assert.False(LitFires.SameWorld(null, "w"));
            Assert.False(LitFires.SameWorld("w", "w"));
            Assert.False(LitFires.SameWorld("other-1:3", "w"));
        }

        [Fact]
        public void LitAfterTheMarkGoesOutAndAnotherWorldPutsOutAll()
        {
            Assert.True(LitFires.PutOut(8, 7));     // lit since the capture
            Assert.False(LitFires.PutOut(7, 7));    // the capture's own last number
            Assert.False(LitFires.PutOut(3, 7));    // burning at the capture
            Assert.True(LitFires.PutOut(3, -1));    // another world: as a load
            Assert.True(LitFires.PutOut(1, 0));
        }

        [Fact]
        public void DescribeCountsByKindAndIsEmptyWhenNothingChanged()
        {
            Assert.Equal("", LitFires.Describe(new int[LitFires.Kinds], 0, 0));
            Assert.Equal("", LitFires.Describe(null, 0, 0));

            int[] c = new int[LitFires.Kinds];
            c[LitFires.Tree] = 1;
            c[LitFires.Bush] = 2;
            c[LitFires.Cannibal] = 1;
            Assert.Equal("fire: 4 put out (tree x1, bush x2, cannibal x1), 1 orphan bush view removed",
                         LitFires.Describe(c, 1, 0));

            int[] e = new int[LitFires.Kinds];
            e[LitFires.Fire] = 1;
            e[LitFires.Effigy] = 1;
            e[LitFires.Animal] = 2;
            Assert.Equal("fire: 4 put out (fire x1, effigy x1, animal x2), 2 orphan bush views removed, 1 fire lit again (burning at the capture)",
                         LitFires.Describe(e, 2, 1));

            Assert.Equal("fire: 0 put out, 2 fires lit again (burning at the capture)",
                         LitFires.Describe(new int[LitFires.Kinds], 0, 2));
        }
    }
}
