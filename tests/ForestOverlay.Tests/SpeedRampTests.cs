using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    public class SpeedRampTests
    {
        [Fact]
        public void SlowIsTheFirstBucketAndFastTheLast()
        {
            Assert.Equal(0, SpeedRamp.Bucket(2f, 2f, 10f));
            Assert.Equal(SpeedRamp.Buckets - 1, SpeedRamp.Bucket(10f, 2f, 10f));
            Assert.Equal(SpeedRamp.Buckets / 2, SpeedRamp.Bucket(6f, 2f, 10f));
        }

        [Fact]
        public void AFlatRangeIsTheMiddleAndOutOfRangeClamps()
        {
            Assert.Equal(SpeedRamp.Buckets / 2, SpeedRamp.Bucket(5f, 5f, 5f));
            Assert.Equal(0, SpeedRamp.Bucket(-3f, 2f, 10f));
            Assert.Equal(SpeedRamp.Buckets - 1, SpeedRamp.Bucket(99f, 2f, 10f));
        }

        [Fact]
        public void TheRampRunsRedToGreen()
        {
            float r0, g0, b0, r1, g1, b1;
            SpeedRamp.Colour(0, out r0, out g0, out b0);
            SpeedRamp.Colour(SpeedRamp.Buckets - 1, out r1, out g1, out b1);
            Assert.True(r0 > g0);   // red
            Assert.True(g1 > r1);   // green
        }

        [Fact]
        public void TheBufferKeepsEachPointsSpeedAndTheRange()
        {
            LineBuffer b = new LineBuffer(0.1f);
            for (int i = 0; i < 300; i++) b.Append(new Vector3(i, 0f, 0f), i, 3f + (i % 7));
            Assert.Equal(3f, b.MinSpeed);
            Assert.Equal(9f, b.MaxSpeed);
            Assert.Equal(3f + (299 % 7), b.Speeds[299]);   // survived the buffer growing
            b.Clear();
            Assert.True(b.MinSpeed > b.MaxSpeed);
        }

        [Fact]
        public void TheRibbonCarriesValuesToItsKeptPoints()
        {
            RibbonPath r = new RibbonPath();
            r.SetView(Vector3.zero, new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), 0.1f,
                      1000f, 960f, 540f, 0f, 0.25f, 0f);
            r.Add(new[] { new Vector3(0f, -1f, 10f), new Vector3(0f, -1f, 11f), new Vector3(0f, -1f, 12f) },
                  new[] { 1f, 2f, 3f }, 0, 3);
            Assert.Equal(3, r.Count);
            Assert.Equal(1f, r.Values[0]);
            Assert.Equal(2f, r.Values[1]);
            Assert.Equal(3f, r.Values[2]);
        }
    }
}
