using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // After a Full load, which pickups did the capture have? Positions from
    // real bridge sessions (cave 5, v0.24.51): the coin pile, a bottle that
    // settled 0.27 m away, the modern axe 1.2 m away.
    // ------------------------------------------------------------------
    public class PickupMatchTests
    {
        private static PickupMatch.Entry E(int id, float x, float y, float z)
        {
            PickupMatch.Entry e;
            e.Id = id;
            e.Position = new Vector3(x, y, z);
            return e;
        }

        private static List<PickupMatch.Entry> Keys(params string[] keys)
        {
            List<PickupMatch.Entry> l = new List<PickupMatch.Entry>();
            foreach (string k in keys)
            {
                PickupMatch.Entry e;
                Assert.True(PickupMatch.TryParse(k, out e), k);
                l.Add(e);
            }
            return l;
        }

        [Fact]
        public void ParsesThePickupKey()
        {
            PickupMatch.Entry e;
            Assert.True(PickupMatch.TryParse(SavestateFile.PickupKey(38, 814.1f, 11.56f, -835.26f), out e));
            Assert.Equal(38, e.Id);
            Assert.Equal(814.1f, e.Position.x, 3);
            Assert.Equal(11.6f, e.Position.y, 3);
            Assert.Equal(-835.3f, e.Position.z, 3);

            Assert.False(PickupMatch.TryParse("", out e));
            Assert.False(PickupMatch.TryParse("38", out e));
            Assert.False(PickupMatch.TryParse("x@1,2,3", out e));
            Assert.False(PickupMatch.TryParse("38@1,2", out e));
        }

        [Fact]
        public void APileTakenBeforeTheCaptureIsUnmatched()
        {
            // Captured: cash elsewhere only. Live: that cash plus the pile.
            List<PickupMatch.Entry> cap = Keys("38@750.8,76.1,625.2");
            List<PickupMatch.Entry> live = new List<PickupMatch.Entry>
            {
                E(38, 750.8f, 76.1f, 625.19f),
                E(38, 814.1f, 11.56f, 835.26f), E(38, 814.21f, 11.56f, 834.68f),
                E(91, 813.94f, 11.66f, 835.38f),
            };
            bool[] m = PickupMatch.Match(cap, live, 2.5f);
            Assert.Equal(new[] { true, false, false, false }, m);
        }

        [Fact]
        public void PartOfAPileTakenLeavesOnlyTheExtras()
        {
            // Three coins close together, one taken: two captured, three live.
            List<PickupMatch.Entry> cap = Keys("91@814.7,11.7,835.3", "91@814.1,11.7,834.6");
            List<PickupMatch.Entry> live = new List<PickupMatch.Entry>
            {
                E(91, 814.74f, 11.66f, 835.28f), E(91, 814.74f, 11.66f, 835.05f), E(91, 814.06f, 11.66f, 834.59f),
            };
            bool[] m = PickupMatch.Match(cap, live, 2.5f);
            int unmatched = 0;
            foreach (bool b in m) if (!b) unmatched++;
            Assert.Equal(1, unmatched);
            Assert.True(m[0]);
            Assert.True(m[2]);
        }

        [Fact]
        public void SettledOrRespawnedElsewhereStillMatches()
        {
            // The bottle 0.27 m off, the modern axe 1.2 m off, and an item
            // at another spawn point 300 m away.
            List<PickupMatch.Entry> cap = Keys("37@818.2,6.6,846.5", "88@-268.7,-67.8,1007.9", "50@0.0,0.0,0.0");
            List<PickupMatch.Entry> live = new List<PickupMatch.Entry>
            {
                E(37, 818.05f, 6.57f, 846.72f), E(88, -269.9f, -68.1f, 1007.4f), E(50, 300f, 0f, 0f),
            };
            Assert.Equal(new[] { true, true, true }, PickupMatch.Match(cap, live, 2.5f));
        }

        [Fact]
        public void NearestPairsWinOverFarOnes()
        {
            // One captured cash between two live ones: the near one is it.
            List<PickupMatch.Entry> cap = Keys("38@10.0,0.0,0.0");
            List<PickupMatch.Entry> live = new List<PickupMatch.Entry> { E(38, 12f, 0f, 0f), E(38, 10.1f, 0f, 0f) };
            Assert.Equal(new[] { false, true }, PickupMatch.Match(cap, live, 2.5f));
        }

        [Fact]
        public void AReRolledGreebleSlotIsTaken()
        {
            List<PickupMatch.Entry> cap = Keys("37@818.9,6.9,846.1");
            Assert.True(PickupMatch.SpotTaken(cap, new Vector3(818.92f, 6.89f, 846.06f), 0.1f));
            Assert.False(PickupMatch.SpotTaken(cap, new Vector3(814.1f, 11.56f, 835.26f), 0.1f));
        }
    }
}
