using System;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    public class RibbonPathTests
    {
        // The eye at the origin looking down +z; 1000 px per unit at depth 1,
        // a 1920 x 1080 screen.
        private static RibbonPath View(float drawDistance = 0f, float minPixels = 0f)
        {
            RibbonPath r = new RibbonPath();
            r.SetView(Vector3.zero, new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), 0.1f,
                      1000f, 960f, 540f, drawDistance, 0.25f, minPixels);
            return r;
        }

        private static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }

        [Fact]
        public void ALineInFrontIsOneStripWithSidesOfOnePixel()
        {
            RibbonPath r = View();
            r.Add(new[] { V(0f, -1f, 10f), V(0f, -1f, 11f), V(0f, -1f, 12f) }, 0, 3);
            Assert.Equal(1, r.StripCount);
            Assert.Equal(3, r.Count);
            for (int i = 0; i < 3; i++)
            {
                Vector3 s = r.Sides[i];
                // Across the line (x) and as long as a pixel at that depth.
                Assert.Equal(r.Points[i].z / 1000f, Math.Abs(s.x), 5);
                Assert.Equal(0f, s.z, 5);
                Assert.Equal(1f, r.Fade[i]);
            }
        }

        [Fact]
        public void ASegmentThroughTheNearPlaneIsCutAtIt()
        {
            // Bug re-inserted (no clip): the point behind the eye is kept
            // and projects to the other side of the screen.
            RibbonPath r = View();
            r.Add(new[] { V(0f, -1f, 5f), V(0f, -1f, -5f) }, 0, 2);
            Assert.Equal(2, r.Count);
            Assert.Equal(0.1f, r.Points[1].z, 4);
            for (int i = 0; i < r.Count; i++) Assert.True(r.Points[i].z >= 0.1f - 1e-4f);
        }

        [Fact]
        public void ALineGoingBehindTheEyeAndBackIsTwoStrips()
        {
            RibbonPath r = View();
            r.Add(new[] { V(1f, 0f, 5f), V(1f, 0f, -5f), V(-1f, 0f, -5f), V(-1f, 0f, 5f) }, 0, 4);
            Assert.Equal(2, r.StripCount);
            for (int i = 0; i < r.Count; i++) Assert.True(r.Points[i].z >= 0.1f - 1e-4f);
            // Each strip starts or ends on the near plane, never joined across it.
            Assert.Equal(0.1f, r.Points[r.Starts[1] - 1].z, 4);
            Assert.Equal(0.1f, r.Points[r.Starts[1]].z, 4);
        }

        [Fact]
        public void PointsCloserThanMinPixelsAreSkippedButTheLastIsKept()
        {
            // 0.01 apart at depth 100: 0.1 px each on screen.
            Vector3[] pts = new Vector3[50];
            for (int i = 0; i < pts.Length; i++) pts[i] = V(i * 0.01f, 0f, 100f);
            RibbonPath r = View(0f, 2f);
            r.Add(pts, 0, pts.Length);
            Assert.True(r.Count < 10, "kept " + r.Count);
            Assert.Equal(pts[0].x, r.Points[0].x, 5);
            Assert.Equal(pts[49].x, r.Points[r.Count - 1].x, 5);
        }

        [Fact]
        public void TheFarPartFadesAndBeyondTheDrawDistanceIsDropped()
        {
            // Draw distance 100, fading from 75.
            RibbonPath r = View(100f);
            r.Add(new[] { V(0f, 0f, 10f), V(0f, 0f, 87.5f), V(0f, 0f, 150f), V(0f, 0f, 200f) }, 0, 4);
            Assert.Equal(1, r.StripCount);
            Assert.Equal(3, r.Count);           // 200 is gone, 150 ends the strip
            Assert.Equal(1f, r.Fade[0]);
            Assert.Equal(0.5f, r.Fade[1], 3);
            Assert.Equal(0f, r.Fade[2]);
        }

        [Fact]
        public void ASegmentOffOneSideOfTheScreenIsDropped()
        {
            RibbonPath r = View();
            // At depth 10 the screen's left edge is x = -9.6.
            r.Add(new[] { V(-50f, 0f, 10f), V(-40f, 0f, 10f), V(-30f, 0f, 10f) }, 0, 3);
            Assert.Equal(0, r.Count);
            // Crossing the screen from one side to the other is kept.
            r.Add(new[] { V(-50f, 0f, 10f), V(50f, 0f, 10f) }, 0, 2);
            Assert.Equal(2, r.Count);
        }

        [Fact]
        public void ABendKeepsItsWidth()
        {
            // A right angle in the screen plane: the mitre is sqrt 2 pixels long.
            RibbonPath r = View();
            r.Add(new[] { V(-1f, 0f, 10f), V(0f, 0f, 10f), V(0f, 1f, 10f) }, 0, 3);
            Assert.Equal(3, r.Count);
            float px = 10f / 1000f;
            Assert.Equal(px * (float)Math.Sqrt(2), r.Sides[1].magnitude, 4);
            Assert.Equal(px, r.Sides[0].magnitude, 4);
        }

        [Fact]
        public void TheArraysGrowPastTheirFirstSize()
        {
            // More points and strips than the arrays start with (256 / 16).
            Vector3[] pts = new Vector3[1000];
            for (int i = 0; i < pts.Length; i++) pts[i] = V((i % 2 == 0 ? -1f : 1f) * (i % 40 == 39 ? 100f : 1f), 0f, 10f + i * 0.1f);
            RibbonPath r = View();
            r.Add(pts, 0, pts.Length);
            Assert.True(r.Count > 256, "kept " + r.Count);
            for (int s = 0; s < r.StripCount; s++)
            {
                int end = s + 1 < r.StripCount ? r.Starts[s + 1] : r.Count;
                Assert.True(end - r.Starts[s] >= 2);
            }
        }

        [Fact]
        public void AStripOfOnePointIsDroppedAndClearEmpties()
        {
            RibbonPath r = View();
            r.Add(new[] { V(0f, 0f, 10f) }, 0, 1);
            Assert.Equal(0, r.Count);
            r.Add(new[] { V(0f, 0f, 10f), V(1f, 0f, 10f) }, 0, 2);
            Assert.Equal(2, r.Count);
            Assert.Equal(1, r.StripCount);
            r.Clear();
            Assert.Equal(0, r.Count + r.StripCount);
        }
    }
}
