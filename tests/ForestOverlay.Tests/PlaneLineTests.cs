using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // The save's plane crash site in a .run (v0.24.163): written only when
    // known, round-trips, and older runs parse without one.
    // ------------------------------------------------------------------
    public class PlaneLineTests
    {
        private static Attempt OneSample()
        {
            var a = new Attempt { AnchorLabel = "s-1", Duration = 1f };
            RunSample s;
            s.T = 0f; s.P = new Vector3(1, 2, 3); s.Speed = 0f;
            a.Samples.Add(s);
            return a;
        }

        [Fact]
        public void RoundTrips()
        {
            Attempt a = OneSample();
            a.HasPlane = true;
            a.Plane = new Vector3(816.57f, 88.1f, 621.1f);
            a.PlaneYaw = 121.59f;
            string text = AttemptFormat.Write(a);
            Assert.Contains("plane|816.570|88.100|621.100|121.590", text);
            Attempt b = AttemptFormat.Parse(text.Split('\n'));
            Assert.True(b.HasPlane);
            Assert.Equal(816.57f, b.Plane.x, 2);
            Assert.Equal(621.1f, b.Plane.z, 2);
            Assert.Equal(121.59f, b.PlaneYaw, 2);
        }

        [Fact]
        public void NoPlaneNoLine()
        {
            string text = AttemptFormat.Write(OneSample());
            Assert.DoesNotContain("plane|", text);
            Assert.False(AttemptFormat.Parse(text.Split('\n')).HasPlane);
        }
    }
}
