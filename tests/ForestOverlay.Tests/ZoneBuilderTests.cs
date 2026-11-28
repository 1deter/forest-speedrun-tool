using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // The zone editor's three clicks (T-0492): the box it builds must fire
    // exactly where it was drawn - tested through TriggerEvaluator, the
    // same test a run uses.
    // ------------------------------------------------------------------
    public class ZoneBuilderTests
    {
        private static bool Inside(Trigger t, Vector3 p)
        {
            return TriggerEvaluator.IsSatisfied(t, p, null, null, null);
        }

        [Fact]
        public void TwoCornersAndAHeightMakeTheBoxBetweenThem()
        {
            Trigger t = ZoneBuilder.Finish(new Vector3(10, 5, 20), new Vector3(14, 6, 26), 9f, 0f);
            Assert.Equal(TriggerKind.Zone, t.Kind);
            Assert.Equal(ZoneShape.Box, t.Shape);
            Assert.Equal(12f, t.Position.x, 3);
            Assert.Equal(23f, t.Position.z, 3);
            Assert.Equal(7f, t.Position.y, 3);   // from the lower corner (5) to 9
            Assert.Equal(new Vector3(2, 2, 3), t.Extents);
            Assert.True(Inside(t, new Vector3(10.1f, 5.1f, 20.1f)));
            Assert.True(Inside(t, new Vector3(13.9f, 8.9f, 25.9f)));
            Assert.False(Inside(t, new Vector3(9.9f, 6f, 22f)));
            Assert.False(Inside(t, new Vector3(12f, 9.2f, 22f)));
        }

        [Fact]
        public void ATurnedGridMakesATurnedBoxThatFiresWhereDrawn()
        {
            const float yaw = 30f;
            // Corners drawn in the grid's frame, 8 m by 4 m.
            Vector3 a = ZoneBuilder.FromGrid(new Vector2(0, 0), 0f, yaw);
            Vector3 b = ZoneBuilder.FromGrid(new Vector2(8, 4), 0f, yaw);
            Trigger t = ZoneBuilder.Finish(a, b, 3f, yaw);
            Assert.Equal(30f, t.Yaw);
            Assert.Equal(4f, t.Extents.x, 3);
            Assert.Equal(2f, t.Extents.z, 3);

            Assert.True(Inside(t, ZoneBuilder.FromGrid(new Vector2(0.2f, 0.2f), 1f, yaw)));
            Assert.True(Inside(t, ZoneBuilder.FromGrid(new Vector2(7.8f, 3.8f), 1f, yaw)));
            Assert.False(Inside(t, ZoneBuilder.FromGrid(new Vector2(8.3f, 2f), 1f, yaw)));
            Assert.False(Inside(t, ZoneBuilder.FromGrid(new Vector2(4f, -0.3f), 1f, yaw)));
        }

        [Fact]
        public void BelowTheFloorAndTinyClicksStillMakeAnEnterableBox()
        {
            Trigger hung = ZoneBuilder.Finish(new Vector3(0, 10, 0), new Vector3(2, 10, 2), 6f, 0f);
            Assert.Equal(8f, hung.Position.y, 3);
            Assert.Equal(2f, hung.Extents.y, 3);

            Trigger dot = ZoneBuilder.Finish(new Vector3(1, 1, 1), new Vector3(1, 1, 1), 1f, 0f);
            Assert.Equal(ZoneBuilder.MinHalf, dot.Extents.x);
            Assert.Equal(ZoneBuilder.MinHalf, dot.Extents.y);
            Assert.Equal(ZoneBuilder.MinHalf, dot.Extents.z);
        }

        [Fact]
        public void TheFootprintStandsOnTheLowerCorner()
        {
            Trigger t = ZoneBuilder.Footprint(new Vector3(0, 4, 0), new Vector3(6, 2, 6), 0f);
            Assert.Equal(2f + ZoneBuilder.DefaultHalfHeight, t.Position.y, 3);
            Assert.Equal(ZoneBuilder.DefaultHalfHeight, t.Extents.y, 3);
        }

        [Fact]
        public void SnappingRoundsInTheGridsFrameAndLeavesHeight()
        {
            Assert.Equal(new Vector3(2, 7.3f, 4), ZoneBuilder.Snap(new Vector3(2.4f, 7.3f, 3.6f), 1f, 0f));
            Assert.Equal(new Vector3(2.5f, 1, 3.5f), ZoneBuilder.Snap(new Vector3(2.4f, 1, 3.6f), 0.5f, 0f));
            Vector3 free = new Vector3(2.4f, 1, 3.6f);
            Assert.Equal(free, ZoneBuilder.Snap(free, 0f, 0f));

            // On a turned grid the snapped point lies on that grid's lines.
            Vector3 s = ZoneBuilder.Snap(new Vector3(5.3f, 0, 1.2f), 2f, 45f);
            Vector2 g = ZoneBuilder.ToGrid(s, 45f);
            Assert.Equal(0f, Mathf.Abs(g.x / 2f - ZoneBuilder.Rnd(g.x / 2f)), 3);
            Assert.Equal(0f, Mathf.Abs(g.y / 2f - ZoneBuilder.Rnd(g.y / 2f)), 3);
        }

        [Fact]
        public void TheGridCycleWrapsToOff()
        {
            int i = 0;
            for (int k = 0; k < ZoneBuilder.GridSizes.Length; k++) i = ZoneBuilder.NextGrid(i);
            Assert.Equal(0, i);
            Assert.Equal(0f, ZoneBuilder.GridSizes[0]);
        }

        [Fact]
        public void TheThirdClickReadsHeightOnTheUprightPlaneFacingTheCamera()
        {
            // Camera 10 m south of the middle, eye at 2 m, looking up 45 degrees north.
            float top;
            Assert.True(ZoneBuilder.HeightFromRay(new Vector3(0, 2, -10), new Vector3(0, 0.7071f, 0.7071f), new Vector3(0, 0, 0), out top));
            Assert.Equal(12f, top, 2);
            // Looking away from the zone: no height.
            Assert.False(ZoneBuilder.HeightFromRay(new Vector3(0, 2, -10), new Vector3(0, 0, -1), new Vector3(0, 0, 0), out top));
            // Right over the middle: no plane faces the camera.
            Assert.False(ZoneBuilder.HeightFromRay(new Vector3(0, 5, 0), new Vector3(0, -1, 0), new Vector3(0, 0, 0), out top));
            Assert.Equal(4f, ZoneBuilder.SnapHeight(4.3f, 2f));
        }
    }
}
