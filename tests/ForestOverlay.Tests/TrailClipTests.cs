using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class TrailClipTests
    {
        [Fact]
        public void InsideKeepsAll()
        {
            float a, b;
            Assert.True(TrailClip.Clip(1, 1, 5, 5, 0, 0, 10, 10, out a, out b));
            Assert.Equal(0f, a); Assert.Equal(1f, b);
        }

        [Fact]
        public void ReachesTheEdge()
        {
            float a, b;
            Assert.True(TrailClip.Clip(8, 5, 12, 5, 0, 0, 10, 10, out a, out b));
            Assert.Equal(0f, a); Assert.Equal(0.5f, b, 4);
        }

        [Fact]
        public void OutsideDropped()
        {
            float a, b;
            Assert.False(TrailClip.Clip(11, 5, 15, 5, 0, 0, 10, 10, out a, out b));
        }
        [Fact]
        public void ClipDiagonal()
        {
            float a, b;
            Assert.True(TrailClip.Clip(-5, -5, 15, 15, 0, 0, 10, 10, out a, out b));
            Assert.Equal(0.25f, a, 4); Assert.Equal(0.75f, b, 4);
        }

        [Fact]
        public void ClipReversedDirection()
        {
            float a, b;
            Assert.True(TrailClip.Clip(12, 5, 8, 5, 0, 0, 10, 10, out a, out b));
            Assert.Equal(0.5f, a, 4); Assert.Equal(1f, b, 4);
        }

        [Fact]
        public void ClipZeroLength()
        {
            float a, b;
            Assert.False(TrailClip.Clip(15, 5, 15, 5, 0, 0, 10, 10, out a, out b));   // a point outside
            Assert.True(TrailClip.Clip(5, 5, 5, 5, 0, 0, 10, 10, out a, out b));    // a point inside: kept whole, drawn as nothing (Quad skips it)
        }

        private static int Split(float px, float py, float tx, float ty, float[] o)
        {
            return TrailClip.Split(px, py, tx, ty, 0, 0, 10, 0, o);
        }

        [Fact]
        public void SplitNoWrapIsOnePiece()
        {
            float[] o = new float[TrailClip.MaxPieces * TrailClip.PieceStride];
            Assert.Equal(1, Split(1, 2, 4, 6, o));
            Assert.Equal(1f, o[0]); Assert.Equal(2f, o[1]); Assert.Equal(4f, o[2]); Assert.Equal(6f, o[3]);
            Assert.Equal(0f, o[4]); Assert.Equal(1f, o[5]);
        }

        [Fact]
        public void SplitHorizontalReachesBothEdges()
        {
            // newer at x=-8 (left), older at x=8 (right): the mouse went right and wrapped past the right edge.
            float[] o = new float[TrailClip.MaxPieces * TrailClip.PieceStride];
            Assert.Equal(2, Split(-8, 3, 8, 3, o));
            // piece 0: older end 8 -> newer shifted to 12, clipped at the right edge 10
            Assert.Equal(10f, o[0], 4); Assert.Equal(8f, o[2], 4);
            // piece 1: from the left edge -10 to the newer point -8
            Assert.Equal(-8f, o[6], 4); Assert.Equal(-10f, o[8], 4);
            // the fade fractions cover the whole segment between them
            Assert.Equal(0.5f, o[4], 4); Assert.Equal(1f, o[5], 4);
            Assert.Equal(0f, o[10], 4); Assert.Equal(0.5f, o[11], 4);
        }

        [Fact]
        public void SplitVertical()
        {
            float[] o = new float[TrailClip.MaxPieces * TrailClip.PieceStride];
            Assert.Equal(2, Split(2, 9, 2, -9, o));
            // newer at the bottom-ish (9), older at -9: newer shifted to -11, piece to the edge -10
            Assert.Equal(-10f, o[1], 4); Assert.Equal(-9f, o[3], 4);
            Assert.Equal(9f, o[7], 4); Assert.Equal(10f, o[9], 4);
        }

        [Fact]
        public void SplitCornerWrapMakesFourOrFewerInsideTheBox()
        {
            float[] o = new float[TrailClip.MaxPieces * TrailClip.PieceStride];
            int n = Split(-9, -9, 9, 9, o);
            Assert.InRange(n, 2, 4);
            for (int q = 0; q < n * TrailClip.PieceStride; q++)
                if (q % TrailClip.PieceStride < 4) Assert.InRange(o[q], -10.001f, 10.001f);
        }

        [Fact]
        public void SplitKeepsQuadCornersInsideTheBox()
        {
            float[] o = new float[TrailClip.MaxPieces * TrailClip.PieceStride];
            float thick = 4f;
            int n = TrailClip.Split(-8, -6, 7, 6, 0, 0, 10, thick, o);
            Assert.Equal(2, n);
            for (int q = 0; q < n; q++)
            {
                int i = q * TrailClip.PieceStride;
                float dx = o[i + 2] - o[i], dy = o[i + 3] - o[i + 1];
                float len = (float)System.Math.Sqrt(dx * dx + dy * dy);
                float nx = System.Math.Abs(dy) / len * thick * 0.5f, ny = System.Math.Abs(dx) / len * thick * 0.5f;
                for (int e = 0; e < 2; e++)
                {
                    Assert.True(System.Math.Abs(o[i + e * 2]) + nx <= 10.001f);
                    Assert.True(System.Math.Abs(o[i + e * 2 + 1]) + ny <= 10.001f);
                }
            }
        }
    }
}
