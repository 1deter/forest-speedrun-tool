using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class HudSnapTests
    {
        private const float W = 1920f, H = 1080f, T = HudSnap.Threshold;
        private static readonly List<SnapBox> None = new List<SnapBox>();

        [Fact]
        public void FarFromEveryLineNothingSnaps()
        {
            SnapResult r = HudSnap.Snap(new SnapBox(100f, 100f, 50f, 20f), W, H, None, T);
            Assert.False(r.SnappedX);
            Assert.False(r.SnappedY);
            Assert.Equal(100f, r.X);
            Assert.Equal(100f, r.Y);
        }

        [Fact]
        public void TheBoxCentreSnapsToTheScreenCentre()
        {
            // Centre at 963 / 536: 3 and 4 px off the screen's centre lines.
            SnapResult r = HudSnap.Snap(new SnapBox(913f, 526f, 100f, 20f), W, H, None, T);
            Assert.True(r.SnappedX);
            Assert.True(r.SnappedY);
            Assert.Equal(910f, r.X);
            Assert.Equal(530f, r.Y);
            Assert.Equal(960f, r.GuideX);
            Assert.Equal(540f, r.GuideY);
        }

        [Fact]
        public void EdgesSnapToAQuarterAndThreeQuartersOfTheScreen()
        {
            // Left edge 6 px right of 25 %; bottom edge 5 px above 75 %.
            SnapResult r = HudSnap.Snap(new SnapBox(486f, 765f, 40f, 40f), W, H, None, T);
            Assert.Equal(480f, r.X);
            Assert.Equal(480f, r.GuideX);
            Assert.Equal(770f, r.Y);
            Assert.Equal(810f, r.GuideY);
        }

        [Fact]
        public void TheThresholdIsInclusiveAndOnePixelMoreIsFree()
        {
            Assert.True(HudSnap.Snap(new SnapBox(480f + T, 100f, 10f, 10f), W, H, None, T).SnappedX);
            Assert.False(HudSnap.Snap(new SnapBox(480f + T + 1f, 100f, 10f, 10f), W, H, None, T).SnappedX);
        }

        [Fact]
        public void TheAxesSnapOnTheirOwn()
        {
            // x near 25 %, y nowhere near a line.
            SnapResult r = HudSnap.Snap(new SnapBox(483f, 100f, 10f, 10f), W, H, None, T);
            Assert.True(r.SnappedX);
            Assert.False(r.SnappedY);
            Assert.Equal(100f, r.Y);
        }

        [Fact]
        public void AnotherPiecesEdgesAndCentreAreLines()
        {
            List<SnapBox> others = new List<SnapBox> { new SnapBox(200f, 300f, 100f, 40f) };
            // Left edge to its right edge (300), top to its bottom (340).
            SnapResult r = HudSnap.Snap(new SnapBox(305f, 337f, 60f, 20f), W, H, others, T);
            Assert.Equal(300f, r.X);
            Assert.Equal(300f, r.GuideX);
            Assert.Equal(340f, r.Y);
            Assert.Equal(340f, r.GuideY);

            // Centre to its centre (250, 320).
            r = HudSnap.Snap(new SnapBox(222f, 312f, 60f, 20f), W, H, others, T);
            Assert.Equal(220f, r.X);
            Assert.Equal(250f, r.GuideX);
            Assert.Equal(310f, r.Y);
            Assert.Equal(320f, r.GuideY);

            // Right edge to its left edge (200).
            r = HudSnap.Snap(new SnapBox(143f, 100f, 60f, 20f), W, H, others, T);
            Assert.Equal(140f, r.X);
            Assert.Equal(200f, r.GuideX);
        }

        [Fact]
        public void TheNearestLineWins()
        {
            // Two pieces' edges 3 and 2 px from the box's left edge.
            List<SnapBox> others = new List<SnapBox>
            {
                new SnapBox(97f, 600f, 50f, 50f),
                new SnapBox(50f, 700f, 52f, 50f),
            };
            SnapResult r = HudSnap.Snap(new SnapBox(100f, 100f, 30f, 10f), W, H, others, T);
            Assert.Equal(102f, r.X);
        }

        [Fact]
        public void OnATieTheScreenLineComesFirst()
        {
            // 25 % is at 480; a piece's right edge at 476. The box's left edge
            // is at 478: 2 px from both.
            List<SnapBox> others = new List<SnapBox> { new SnapBox(376f, 600f, 100f, 50f) };
            SnapResult r = HudSnap.Snap(new SnapBox(478f, 100f, 30f, 10f), W, H, others, T);
            Assert.Equal(480f, r.X);
            Assert.Equal(480f, r.GuideX);
        }

        [Fact]
        public void ALineThatPushesTheBoxOffTheScreenIsNotACandidate()
        {
            // A piece against the right edge: the box's left edge would snap to
            // its right edge (1920) - off the screen.
            List<SnapBox> others = new List<SnapBox> { new SnapBox(1820f, 600f, 100f, 50f) };
            SnapResult r = HudSnap.Snap(new SnapBox(1655f, 100f, 260f, 10f), W, H, others, T);
            // Right edge 1915 -> 1920 is fine: the box ends at the screen's edge.
            Assert.True(r.SnappedX);
            Assert.Equal(1660f, r.X);

            r = HudSnap.Snap(new SnapBox(1915f, 100f, 50f, 10f), W, H, others, T);
            Assert.False(r.SnappedX);
        }

        [Fact]
        public void APieceWithNoSizeIsSkipped()
        {
            List<SnapBox> others = new List<SnapBox> { new SnapBox(300f, 300f, 0f, 0f) };
            SnapResult r = HudSnap.Snap(new SnapBox(303f, 303f, 20f, 20f), W, H, others, T);
            Assert.False(r.SnappedX);
            Assert.False(r.SnappedY);
        }

        [Fact]
        public void NoThresholdNoSnap()
        {
            SnapResult r = HudSnap.Snap(new SnapBox(910f, 530f, 100f, 20f), W, H, None, 0f);
            Assert.False(r.SnappedX);
            Assert.False(r.SnappedY);
        }
    }
}
