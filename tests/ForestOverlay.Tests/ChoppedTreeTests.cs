using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // T-0067: a half-chopped tree's record (PineTreeThin_414, bridge
    // 2026-10-18: Health 3, chunk 2 gone, TreeDmg1 damage 2) and the sapling
    // sticks beside a greeble stick.
    public class ChoppedTreeTests
    {
        [Fact]
        public void RecordRoundTrips()
        {
            ChoppedTree t = new ChoppedTree();
            t.Id = 2543;
            t.Health = 3;
            t.Add(1, 2f);
            t.Add(3, 0f);
            t.Add(4, 1.5f);
            Assert.Equal("2543:3:1=2,3=0,4=1.5", t.Format());
            ChoppedTree back;
            Assert.True(ChoppedTree.TryParse(t.Format(), out back));
            Assert.Equal(2543, back.Id);
            Assert.Equal(3, back.Health);
            Assert.Equal(new List<int> { 1, 3, 4 }, back.Slots);
            Assert.Equal(new List<float> { 2f, 0f, 1.5f }, back.Damage);
        }

        [Fact]
        public void MalformedRecordsAreRefused()
        {
            ChoppedTree t;
            Assert.False(ChoppedTree.TryParse("", out t));
            Assert.False(ChoppedTree.TryParse("2543:3", out t));
            Assert.False(ChoppedTree.TryParse("x:3:1=2", out t));
            Assert.False(ChoppedTree.TryParse("2543:3:9=2", out t));
            Assert.False(ChoppedTree.TryParse("2543:3:1=-2", out t));
            Assert.False(ChoppedTree.TryParse("2543:3:1", out t));
            Assert.True(ChoppedTree.TryParse("2543:1:", out t));
            Assert.Empty(t.Slots);
        }

        [Fact]
        public void FileLineRoundTripsSkipsBadRecordsAndIsNullWhenAbsent()
        {
            string error;
            SavestateFile s = new SavestateFile();
            s.Name = "n";
            s.Data = "AAAA";
            Assert.Null(SavestateFile.Parse(s.Write(), out error).Chopped);

            s.Chopped = new List<string> { "2543:3:1=2,3=0,4=0", "77:4:1=0,2=0,3=0,4=0" };
            SavestateFile back = SavestateFile.Parse(s.Write(), out error);
            Assert.Null(error);
            Assert.Equal(s.Chopped, back.Chopped);

            s.Chopped = new List<string> { "bad", "77:4:1=0" };
            Assert.Equal(new List<string> { "77:4:1=0" }, SavestateFile.Parse(s.Write(), out error).Chopped);
        }

        [Fact]
        public void ActiveFakeFollowsTheHitThresholds()
        {
            Assert.Equal(1, ChoppedTree.ActiveFake(0f));
            Assert.Equal(2, ChoppedTree.ActiveFake(1.5f));
            Assert.Equal(3, ChoppedTree.ActiveFake(2f));
            Assert.Equal(4, ChoppedTree.ActiveFake(3.9f));
        }

        [Fact]
        public void MatchesWhenHealthChunksAndDamageAreTheSame()
        {
            ChoppedTree t;
            Assert.True(ChoppedTree.TryParse("2543:3:1=2,3=0,4=0", out t));
            Assert.True(t.Matches(3, new[] { 4, 1, 3 }, new[] { 0f, 2f, 0f }));
            Assert.False(t.Matches(4, new[] { 1, 3, 4 }, new[] { 2f, 0f, 0f }));
            Assert.False(t.Matches(3, new[] { 1, 2, 3, 4 }, new[] { 2f, 0f, 0f, 0f }));
            Assert.False(t.Matches(3, new[] { 1, 3, 4 }, new[] { 1f, 0f, 0f }));
        }

        [Fact]
        public void ANewSaplingStickIsNotAccountedForByANearbyCapturedGreebleStick()
        {
            // Captured: the greeble stick only. Live: the greeble (0 m) and
            // the two sticks the new cut dropped, one 2.46 m from the greeble.
            List<PickupMatch.Entry> cap = new List<PickupMatch.Entry> { E(57, 384.37f, 75.17f, 292.39f) };
            List<PickupMatch.Entry> live = new List<PickupMatch.Entry>
            {
                E(57, 382.5f, 75.35f, 293.98f),
                E(57, 384.37f, 75.17f, 292.39f),
                E(57, 381.95f, 75.34f, 295.31f),
            };
            bool[] remove = PickupMatch.Removable(cap, live, new[] { true, false, true }, 2.5f, false);
            Assert.Equal(new[] { true, false, true }, remove);
        }

        private static PickupMatch.Entry E(int id, float x, float y, float z)
        {
            PickupMatch.Entry e;
            e.Id = id;
            e.Position = new Vector3(x, y, z);
            return e;
        }
    }
}
