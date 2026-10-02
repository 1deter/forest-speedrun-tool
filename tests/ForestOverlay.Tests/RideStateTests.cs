using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // A savestate's `ride` header (Game/RideModes puts the ride back from
    // it): a kind and two vectors, invariant culture, nothing else.
    // ------------------------------------------------------------------
    public class RideStateTests
    {
        [Fact]
        public void WritesAndParsesEveryKind()
        {
            string[] kinds = { RideState.Zipline, RideState.Sled, RideState.Glider, RideState.GliderHeld, RideState.Cliff };
            foreach (string kind in kinds)
            {
                RideState s = new RideState(kind, new Vector3(-711.5f, -432.25f, 967f), new Vector3(0f, 359.71f, -0.125f));
                RideState back;
                Assert.True(RideState.TryParse(s.Write(), out back));
                Assert.Equal(kind, back.Kind);
                Assert.Equal(-711.5f, back.A.x);
                Assert.Equal(-432.25f, back.A.y);
                Assert.Equal(967f, back.A.z);
                Assert.Equal(359.71f, back.B.y, 3);
                Assert.Equal(-0.125f, back.B.z);
            }
        }

        [Fact]
        public void NoneWritesNothing()
        {
            Assert.Equal("", new RideState().Write());
            Assert.True(new RideState().IsNone);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("zipline")]
        [InlineData("zipline 1,2,3")]
        [InlineData("zipline 1,2 4,5,6")]
        [InlineData("zipline 1,2,x 4,5,6")]
        [InlineData("zipline 1,2,3 4,5,6 7,8,9")]
        public void RefusesMalformed(string text)
        {
            RideState s;
            Assert.False(RideState.TryParse(text, out s));
            Assert.True(s.IsNone);
        }

        [Fact]
        public void ALaterKindStillParses()
        {
            RideState s;
            Assert.True(RideState.TryParse("raft 1,2,3 0,0,0", out s));
            Assert.Equal("raft", s.Kind);
        }
    }
}
