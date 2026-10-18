using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class AnimalStatesTests
    {
        private static AnimalStates.Entry E(int id, int hp, bool p, params float[] d)
        {
            AnimalStates.Entry e = new AnimalStates.Entry { Id = id, Health = hp, Poisoned = p };
            for (int i = 0; i < 4 && i < d.Length; i++) e.Damage[i] = d[i];
            return e;
        }

        [Fact]
        public void RoundTripUnderACommaCulture()
        {
            var old = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            try
            {
                string line = AnimalStates.Format(new List<AnimalStates.Entry> { E(-1234, 4, false, 0, 0.5f, 0, 0), E(5678, 2, true, 1, 0, 0.25f, 0) });
                Assert.Equal("-1234:4@0,0.5,0,0;5678:2@1,0,0.25,0@p", line);
                List<AnimalStates.Entry> back = AnimalStates.Parse(line);
                Assert.Equal(2, back.Count);
                Assert.Equal(-1234, back[0].Id);
                Assert.Equal(4, back[0].Health);
                Assert.False(back[0].Poisoned);
                Assert.Equal(0.5f, back[0].Damage[1]);
                Assert.True(back[1].Poisoned);
                Assert.Equal(0.25f, back[1].Damage[2]);
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = old; }
        }

        [Fact]
        public void EmptyAndGarbage()
        {
            Assert.Equal("", AnimalStates.Format(null));
            Assert.Empty(AnimalStates.Parse(""));
            Assert.Empty(AnimalStates.Parse(null));
            Assert.Empty(AnimalStates.Parse("junk;1:2;1:x@0,0,0,0;1:2@0,0,0;1:2@0,0,0,0@q"));
            Assert.Single(AnimalStates.Parse("junk;7:3@0,0,0,0"));
        }

        [Fact]
        public void Note()
        {
            Assert.Equal("", AnimalStates.Describe(0, 0, 0, 0));
            Assert.Equal("animals: 1 put back (health x1, skin x1, poison stopped x1)", AnimalStates.Describe(1, 1, 1, 1));
            Assert.Equal("animals: 2 put back (health x2)", AnimalStates.Describe(2, 2, 0, 0));
        }
    }
}
