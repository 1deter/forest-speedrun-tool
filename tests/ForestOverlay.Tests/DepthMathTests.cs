using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // Reading the depth texture for paint (T-0296): stored depth -> metres,
    // and the normal from neighbouring points, edge-aware.
    // ------------------------------------------------------------------
    public class DepthMathTests
    {
        private const float Near = 0.2f, Far = 6000f;

        // What the GPU stores for a view depth z (the inverse of EyeDepth).
        private static float Stored(float z, bool reversed)
        {
            float d = (1f / z - 1f / Far) / (1f / Near - 1f / Far);   // 1 at near, 0 at far
            return reversed ? d : 1f - d;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void PlanesMapToNearAndFar(bool reversed)
        {
            Assert.Equal(Near, DepthMath.EyeDepth(reversed ? 1f : 0f, Near, Far, reversed), 3);
            // float precision at the far plane (non-reversed loses most): within 0.5 %
            Assert.InRange(DepthMath.EyeDepth(reversed ? 0f : 1f, Near, Far, reversed), Far * 0.995f, Far * 1.005f);
        }

        [Theory]
        [InlineData(1.5f)]
        [InlineData(12f)]
        [InlineData(80f)]
        public void ReversedDepthRoundTrips(float z)
        {
            Assert.Equal(z, DepthMath.EyeDepth(Stored(z, true), Near, Far, true), 2);
        }

        [Fact]
        public void FlatGroundGivesUpTowardsTheEye()
        {
            Vector3 eye = new Vector3(0f, 2f, 0f);
            Vector3 c = new Vector3(0f, 0f, 5f);
            Vector3 n;
            Assert.True(DepthMath.Normal(eye, c,
                new Vector3(-0.1f, 0f, 5f), new Vector3(0.1f, 0f, 5f),
                new Vector3(0f, 0f, 4.9f), new Vector3(0f, 0f, 5.1f), out n));
            Assert.Equal(1f, n.y, 4);
        }

        [Fact]
        public void TheNormalFacesTheEyeWhicheverWayTheAxesRun()
        {
            Vector3 eye = new Vector3(0f, 2f, 0f);
            Vector3 c = new Vector3(0f, 0f, 5f);
            Vector3 n;
            // Up / down swapped (a flipped texture): still up.
            Assert.True(DepthMath.Normal(eye, c,
                new Vector3(-0.1f, 0f, 5f), new Vector3(0.1f, 0f, 5f),
                new Vector3(0f, 0f, 5.1f), new Vector3(0f, 0f, 4.9f), out n));
            Assert.Equal(1f, n.y, 4);
        }

        [Fact]
        public void APointAtAWallsEdgeTakesTheWallNotTheDropBehind()
        {
            // A wall facing -z at z = 5; the right neighbour falls off its edge
            // onto ground 20 m further back.
            Vector3 eye = new Vector3(0f, 1f, 0f);
            Vector3 c = new Vector3(0f, 1f, 5f);
            Vector3 n;
            Assert.True(DepthMath.Normal(eye, c,
                new Vector3(-0.1f, 1f, 5f), new Vector3(0.1f, 1f, 25f),
                new Vector3(0f, 0.9f, 5f), new Vector3(0f, 1.1f, 5f), out n));
            Assert.Equal(-1f, n.z, 4);
        }

        [Fact]
        public void DegeneratePointsAreRefused()
        {
            Vector3 p = new Vector3(1f, 1f, 1f);
            Vector3 n;
            Assert.False(DepthMath.Normal(Vector3.zero, p, p, p, p, p, out n));
        }
    }
}
