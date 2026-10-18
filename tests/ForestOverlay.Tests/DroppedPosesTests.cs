using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class DroppedPosesTests
    {
        private static DroppedPoses.Pose P(int item, float x)
        {
            DroppedPoses.Pose p;
            p.Item = item; p.X = x; p.Y = 76.08f; p.Z = -4.12f;
            p.Qx = 0f; p.Qy = 0.7071f; p.Qz = 0f; p.Qw = 0.7071f;
            return p;
        }

        [Fact]
        public void RoundTripKeepsNegativeIdsAndIgnoresCulture()
        {
            var old = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            try
            {
                var list = new List<KeyValuePair<int, DroppedPoses.Pose>>();
                list.Add(new KeyValuePair<int, DroppedPoses.Pose>(-197640, P(53, 430.11f)));
                list.Add(new KeyValuePair<int, DroppedPoses.Pose>(-5, P(7, -1.5f)));
                string line = DroppedPoses.Format(list);
                Assert.DoesNotContain("430,11", line);
                var map = DroppedPoses.Parse(line);
                Assert.Equal(2, map.Count);
                Assert.Equal(53, map[-197640].Item);
                Assert.Equal(430.11f, map[-197640].X);
                Assert.Equal(-1.5f, map[-5].X);
                Assert.Equal(0.7071f, map[-5].Qw);
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = old; }
        }

        [Fact]
        public void GarbageAndEmptyGiveAnEmptyMap()
        {
            Assert.Empty(DroppedPoses.Parse(""));
            Assert.Empty(DroppedPoses.Parse(null));
            Assert.Empty(DroppedPoses.Parse("nonsense;1:2;x:1@a,b,c@1,2,3,4;3:4@1,2@1,2,3,4"));
            Assert.Equal("", DroppedPoses.Format(new List<KeyValuePair<int, DroppedPoses.Pose>>()));
        }

        [Fact]
        public void DiffersAtThresholds()
        {
            DroppedPoses.Pose p = P(1, 10f);
            Assert.False(DroppedPoses.Differs(p, 10.01f, 76.08f, -4.12f, 0f, 0.7071f, 0f, 0.7071f));
            Assert.True(DroppedPoses.Differs(p, 10.2f, 76.08f, -4.12f, 0f, 0.7071f, 0f, 0.7071f));
            Assert.True(DroppedPoses.Differs(p, 10f, 76.08f, -4.12f, 0f, 0f, 0f, 1f));
        }

        [Fact]
        public void ThrownKindLabels()
        {
            Assert.Equal("bomb", DroppedPoses.ThrownKind(true, false, false, false, "Bomb_Timed(Clone)", false));
            Assert.Equal("explosion", DroppedPoses.ThrownKind(false, true, false, false, "Explosion", true));
            Assert.Equal("molotov", DroppedPoses.ThrownKind(false, false, true, false, "Molotov(Clone)", true));
            Assert.Equal("flare", DroppedPoses.ThrownKind(false, false, false, true, "Flare(Clone)", false));
            Assert.Equal("flare", DroppedPoses.ThrownKind(false, false, false, false, "FlareSpawn(Clone)", true));
            Assert.Equal("effect", DroppedPoses.ThrownKind(false, false, false, false, "rabbitExploded(Clone)", true));
            Assert.Equal("", DroppedPoses.ThrownKind(false, false, false, false, "x", false));
        }
    }
}
