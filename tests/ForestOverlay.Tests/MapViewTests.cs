using System;
using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // The Map tab's camera and geometry (Data/MapView): the island's
    // terrain is (3500 x 3500) at (-1750, -1742.63) (docs/website.md).
    // ------------------------------------------------------------------
    public class MapViewTests
    {
        private static MapView Island(float w = 600f, float h = 400f)
        {
            MapView v = new MapView();
            v.SetViewport(w, h);
            v.SetBounds(-1750f, -1742.63f, 3500f, 3500f);
            v.Fit();
            return v;
        }

        [Fact]
        public void FitShowsTheWholeIslandCentred()
        {
            MapView v = Island();
            Assert.Equal(400f / 3500f, v.Scale, 5);
            Vector2 c = v.WorldToView(0f, -1742.63f + 1750f);
            Assert.Equal(300f, c.x, 3);
            Assert.Equal(200f, c.y, 3);
            Assert.True(v.AtFit);
        }

        [Fact]
        public void NorthIsUpAndEastIsRight()
        {
            MapView v = Island();
            Vector2 a = v.WorldToView(0f, 0f);
            Vector2 north = v.WorldToView(0f, 100f);
            Vector2 east = v.WorldToView(100f, 0f);
            Assert.True(north.y < a.y);
            Assert.True(east.x > a.x);
        }

        [Fact]
        public void ViewAndWorldRoundTrip()
        {
            MapView v = Island();
            v.ZoomAt(3f, 120f, 80f);
            Vector2 p = v.WorldToView(428f, -4f);
            Vector2 w = v.ViewToWorld(p.x, p.y);
            Assert.Equal(428f, w.x, 2);
            Assert.Equal(-4f, w.y, 2);
        }

        [Fact]
        public void ZoomKeepsThePointUnderThePointer()
        {
            MapView v = Island();
            Vector2 before = v.ViewToWorld(250f, 150f);
            v.ZoomAt(2f, 250f, 150f);
            Vector2 after = v.ViewToWorld(250f, 150f);
            Assert.Equal(before.x, after.x, 2);
            Assert.Equal(before.y, after.y, 2);
            Assert.Equal(2f * 400f / 3500f, v.Scale, 5);
        }

        [Fact]
        public void ZoomIsClampedBothWays()
        {
            MapView v = Island();
            v.ZoomAt(0.1f, 300f, 200f);
            Assert.Equal(v.FitScale, v.Scale, 6);
            for (int i = 0; i < 40; i++) v.ZoomAt(2f, 300f, 200f);
            Assert.Equal(MapView.MaxScale, v.Scale, 6);
        }

        [Fact]
        public void AtFitAPanCannotMoveTheIsland()
        {
            MapView v = Island();
            float cx = v.CentreX, cz = v.CentreZ;
            v.Pan(200f, -150f);
            Assert.Equal(cx, v.CentreX, 3);
            Assert.Equal(cz, v.CentreZ, 3);
        }

        [Fact]
        public void PanFollowsThePointerAndStopsAtTheEdge()
        {
            MapView v = Island();
            v.ZoomAt(8f, 300f, 200f);
            Vector2 grab = v.ViewToWorld(300f, 200f);
            v.Pan(40f, 25f);
            Vector2 now = v.WorldToView(grab.x, grab.y);
            Assert.Equal(340f, now.x, 2);
            Assert.Equal(225f, now.y, 2);

            v.Pan(100000f, -100000f);   // far past the west and south edges
            Vector2 tl = v.WorldToView(v.MinX, v.MaxZ);
            Assert.Equal(0f, tl.x, 2);
            Vector2 br = v.WorldToView(v.MaxX, v.MinZ);
            Assert.Equal(v.ViewH, br.y, 2);
        }

        [Fact]
        public void TextureWindowAtFitIsTheWholeTexture()
        {
            MapView v = Island();
            MapBox view, uv;
            Assert.True(v.TextureWindow(out view, out uv));
            Assert.Equal(100f, view.X0, 2);   // (600 - 400) / 2
            Assert.Equal(500f, view.X1, 2);
            Assert.Equal(0f, view.Y0, 2);
            Assert.Equal(400f, view.Y1, 2);
            Assert.Equal(0f, uv.X0, 4); Assert.Equal(0f, uv.Y0, 4);
            Assert.Equal(1f, uv.X1, 4); Assert.Equal(1f, uv.Y1, 4);
        }

        [Fact]
        public void TextureWindowZoomedOnTheNorthEastIsThatCorner()
        {
            MapView v = Island(400f, 400f);
            v.ZoomAt(4f, 400f, 0f);   // pinned to the north-east corner
            MapBox view, uv;
            Assert.True(v.TextureWindow(out view, out uv));
            Assert.Equal(0f, view.X0, 2); Assert.Equal(400f, view.X1, 2);
            Assert.Equal(0.75f, uv.X0, 3); Assert.Equal(1f, uv.X1, 3);
            Assert.Equal(0.75f, uv.Y0, 3); Assert.Equal(1f, uv.Y1, 3);
        }

        [Fact]
        public void ClipKeepsInsideAndCutsAtTheEdges()
        {
            float ax = 10, ay = 10, bx = 20, by = 30;
            Assert.True(MapGeometry.ClipSegment(ref ax, ref ay, ref bx, ref by, 0, 0, 100, 100));
            Assert.Equal(20f, bx); Assert.Equal(30f, by);

            ax = -50; ay = 50; bx = 150; by = 50;
            Assert.True(MapGeometry.ClipSegment(ref ax, ref ay, ref bx, ref by, 0, 0, 100, 100));
            Assert.Equal(0f, ax, 3); Assert.Equal(100f, bx, 3);
            Assert.Equal(50f, ay, 3); Assert.Equal(50f, by, 3);

            ax = -50; ay = -10; bx = 150; by = -10;
            Assert.False(MapGeometry.ClipSegment(ref ax, ref ay, ref bx, ref by, 0, 0, 100, 100));

            ax = -10; ay = 50; bx = 50; by = -10;   // across the top-left corner
            Assert.True(MapGeometry.ClipSegment(ref ax, ref ay, ref bx, ref by, 0, 0, 100, 100));
            Assert.Equal(0f, ax, 3); Assert.Equal(40f, ay, 3);
            Assert.Equal(40f, bx, 3); Assert.Equal(0f, by, 3);
        }

        [Fact]
        public void PickingTakesTheNearestWithinReach()
        {
            float[] xs = { 10f, 50f, 52f, 200f };
            float[] ys = { 10f, 50f, 50f, 200f };
            Assert.Equal(2, MapGeometry.Nearest(xs, ys, 4, 53f, 50f, 8f));
            Assert.Equal(1, MapGeometry.Nearest(xs, ys, 4, 49f, 50f, 8f));
            Assert.Equal(-1, MapGeometry.Nearest(xs, ys, 4, 120f, 120f, 8f));
            Assert.Equal(-1, MapGeometry.Nearest(xs, ys, 1, 50f, 50f, 8f));   // only the first counts
        }

        [Fact]
        public void BoxCornersAreInsideTheTriggerTheyOutline()
        {
            float[] xs = new float[4], zs = new float[4];
            Trigger t = new Trigger
            {
                Kind = TriggerKind.Zone, Shape = ZoneShape.Box,
                Position = new Vector3(100f, 50f, -20f), Extents = new Vector3(6f, 2f, 3f), Yaw = 35f
            };
            MapGeometry.BoxCorners(t.Position.x, t.Position.z, t.Extents.x, t.Extents.z, t.Yaw, xs, zs);
            for (int i = 0; i < 4; i++)
            {
                // A hair towards the centre is inside, a hair outwards is not.
                float ix = xs[i] + (t.Position.x - xs[i]) * 0.01f, iz = zs[i] + (t.Position.z - zs[i]) * 0.01f;
                float ox = xs[i] - (t.Position.x - xs[i]) * 0.01f, oz = zs[i] - (t.Position.z - zs[i]) * 0.01f;
                Assert.True(TriggerEvaluator.IsSatisfied(t, new Vector3(ix, 50f, iz), null, null, null));
                Assert.False(TriggerEvaluator.IsSatisfied(t, new Vector3(ox, 50f, oz), null, null, null));
            }
        }

        [Fact]
        public void ThinDropsClosePointsAndKeepsTheEnd()
        {
            List<RunSample> s = new List<RunSample>();
            for (int i = 0; i <= 100; i++) s.Add(new RunSample { T = i * 0.1f, P = new Vector3(i * 0.5f, 0f, 0f) });
            float[] xs = new float[100], zs = new float[100];
            int n = MapGeometry.Thin(s, 2f, xs, zs);
            Assert.Equal(26, n);   // 0, 2, 4 ... 50
            Assert.Equal(0f, xs[0]);
            Assert.Equal(50f, xs[n - 1]);

            float[] small = new float[5], smallZ = new float[5];
            n = MapGeometry.Thin(s, 2f, small, smallZ);
            Assert.Equal(5, n);
            Assert.Equal(50f, small[4]);   // full: the end still ends it
        }

        [Fact]
        public void PaletteIsStableAndIgnoresCase()
        {
            int a = MapGeometry.PaletteIndex("My spots", 8);
            Assert.Equal(a, MapGeometry.PaletteIndex(" my SPOTS ", 8));
            Assert.InRange(a, 0, 7);
            Assert.Equal(0, MapGeometry.PaletteIndex("x", 0));
        }

        [Fact]
        public void UndergroundIsACaveOrWellBelowTheTerrain()
        {
            Assert.True(MapGeometry.IsUnderground(80f, 75f, "cave06"));
            Assert.True(MapGeometry.IsUnderground(-70f, 60f, ""));
            Assert.False(MapGeometry.IsUnderground(73f, 75f, ""));   // a dip, not a cave
            Assert.False(MapGeometry.IsUnderground(-70f, float.NaN, ""));
        }
    }

    public class ReliefImageTests
    {
        [Fact]
        public void PngRoundTripsBottomUpRgb()
        {
            int w = 37, h = 2000;   // over one 65535-byte stored block
            byte[] rgb = new byte[w * h * 3];
            for (int i = 0; i < rgb.Length; i++) rgb[i] = (byte)(i * 7 + i / 13);
            byte[] png = ReliefImage.EncodePng(w, h, rgb);

            int rw, rh;
            byte[] back;
            Assert.True(ReliefImage.TryDecodePng(png, out rw, out rh, out back));
            Assert.Equal(w, rw); Assert.Equal(h, rh);
            Assert.Equal(rgb, back);
        }

        [Fact]
        public void PngWritesTheTopRowFirst()
        {
            // 1 x 2: bottom red, top blue. The first scanline is the top.
            byte[] rgb = { 255, 0, 0, 0, 0, 255 };
            byte[] png = ReliefImage.EncodePng(1, 2, rgb);
            int idat = IndexOf(png, "IDAT") + 4;
            // zlib header (2) + stored block header (5) + filter byte.
            Assert.Equal(0, png[idat + 7]);
            Assert.Equal(0, png[idat + 8]); Assert.Equal(0, png[idat + 9]); Assert.Equal(255, png[idat + 10]);
        }

        [Fact]
        public void DecodeRefusesDamageAndOtherPngs()
        {
            byte[] rgb = new byte[4 * 4 * 3];
            byte[] png = ReliefImage.EncodePng(4, 4, rgb);
            int rw, rh;
            byte[] back;

            byte[] bad = (byte[])png.Clone();
            bad[bad.Length / 2] ^= 0x55;
            Assert.False(ReliefImage.TryDecodePng(bad, out rw, out rh, out back));
            Assert.False(ReliefImage.TryDecodePng(new byte[10], out rw, out rh, out back));
            Assert.False(ReliefImage.TryDecodePng(null, out rw, out rh, out back));
        }

        [Fact]
        public void ShadeColoursSeaAndLightsNorthWestSlopes()
        {
            int w = 3, h = 3;
            float[] flatSea = new float[w * h];
            byte[] rgb = new byte[w * h * 3];
            ReliefImage.Shade(flatSea, w, h, 1f, 1f, ReliefImage.SeaLevel, rgb);
            Assert.True(rgb[2] > rgb[0]);   // blue over red: water

            // A slope rising to the south-east faces north-west: lit.
            float[] nw = new float[w * h], se = new float[w * h];
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    nw[z * w + x] = 100f + (x - z) * 2f;   // up east, up south
                    se[z * w + x] = 100f - (x - z) * 2f;
                }
            byte[] a = new byte[w * h * 3], b = new byte[w * h * 3];
            ReliefImage.Shade(nw, w, h, 1f, 1f, ReliefImage.SeaLevel, a);
            ReliefImage.Shade(se, w, h, 1f, 1f, ReliefImage.SeaLevel, b);
            int mid = (1 * w + 1) * 3 + 1;
            Assert.True(a[mid] > b[mid]);
        }

        private static int IndexOf(byte[] b, string s)
        {
            for (int i = 0; i + s.Length <= b.Length; i++)
            {
                bool ok = true;
                for (int k = 0; k < s.Length && ok; k++) ok = b[i + k] == s[k];
                if (ok) return i;
            }
            return -1;
        }
    }
}
