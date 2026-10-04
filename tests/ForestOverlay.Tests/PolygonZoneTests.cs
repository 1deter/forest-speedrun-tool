using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // Polygon zones: a prism over an outline of (x, z) points. The text
    // must round-trip (capture writes triggers back out), old shapes must
    // write exactly as before, containment must match the outline, and
    // the edge semantics must be the other shapes' (gotcha 7).
    // ------------------------------------------------------------------
    public class PolygonZoneTests
    {
        // An L: 10 x 10 with the top-right 5 x 5 cut away, middle y 50,
        // half height 2.
        private const string LShape = "poly 50.00 2.00 0.00 0.00 10.00 0.00 10.00 5.00 5.00 5.00 5.00 10.00 0.00 10.00";

        private static Trigger Parse(string text)
        {
            Trigger t;
            Assert.True(TriggerParser.Parse(text, out t));
            return t;
        }

        private static bool In(Trigger t, float x, float y, float z)
        {
            return TriggerEvaluator.IsSatisfied(t, new Vector3(x, y, z), null, null);
        }

        [Fact]
        public void ParsesAndRoundTrips()
        {
            Trigger t = Parse(LShape);
            Assert.Equal(TriggerKind.Zone, t.Kind);
            Assert.Equal(ZoneShape.Polygon, t.Shape);
            Assert.Equal(6, t.Points.Length);
            Assert.Equal(10f, t.Points[2].x);
            Assert.Equal(5f, t.Points[2].y);
            Assert.Equal(50f, t.Position.y);
            Assert.Equal(2f, t.Extents.y);
            Assert.Equal(LShape, TriggerParser.Write(t));
            Assert.Equal(LShape, TriggerParser.Write(Parse(TriggerParser.Write(t))));
        }

        [Fact]
        public void PositionIsThePointsMean()
        {
            Trigger t = Parse("poly 3 1 0 0 4 0 4 2 0 2");
            Assert.Equal(2f, t.Position.x, 4);
            Assert.Equal(3f, t.Position.y, 4);
            Assert.Equal(1f, t.Position.z, 4);
            Assert.StartsWith("poly (", t.Describe());
        }

        [Theory]
        [InlineData("poly 0 1 0 0 1 0")]                 // two points
        [InlineData("poly 0 1 0 0 1 0 1 1 2")]           // an odd coordinate left over
        [InlineData("poly 0 0 0 0 1 0 1 1")]             // no height
        [InlineData("poly 0 -1 0 0 1 0 1 1")]            // negative height
        [InlineData("poly 0 1 0 0 1 x 1 1")]             // not a number
        [InlineData("poly 0")]
        public void BadPolygonsAreRejected(string text)
        {
            Trigger t;
            Assert.False(TriggerParser.Parse(text, out t));
        }

        [Theory]
        [InlineData(2f, 2f, true)]     // the L's corner
        [InlineData(8f, 2f, true)]     // the bottom arm
        [InlineData(2f, 8f, true)]     // the left arm
        [InlineData(8f, 8f, false)]    // the cut-away square - a box would hold it
        [InlineData(-1f, 5f, false)]
        [InlineData(11f, 2f, false)]
        [InlineData(5f, -0.5f, false)]
        public void ContainmentFollowsTheOutline(float x, float z, bool inside)
        {
            Assert.Equal(inside, In(Parse(LShape), x, 50f, z));
        }

        [Fact]
        public void HeightIsAroundTheMiddle()
        {
            Trigger t = Parse(LShape);
            Assert.True(In(t, 2f, 48.1f, 2f));
            Assert.True(In(t, 2f, 51.9f, 2f));
            Assert.False(In(t, 2f, 47.9f, 2f));
            Assert.False(In(t, 2f, 52.1f, 2f));
        }

        [Fact]
        public void PointOrderDoesNotMatter()
        {
            // The same square clockwise and anticlockwise.
            Trigger a = Parse("poly 0 5 0 0 4 0 4 4 0 4");
            Trigger b = Parse("poly 0 5 0 4 4 4 4 0 0 0");
            Assert.True(In(a, 2f, 0f, 2f));
            Assert.True(In(b, 2f, 0f, 2f));
            Assert.False(In(a, 5f, 0f, 2f));
            Assert.False(In(b, 5f, 0f, 2f));
        }

        [Fact]
        public void TooFewPointsIsNeverInside()
        {
            Assert.False(ZonePolygon.Contains(null, 0f, 0f));
            Assert.False(ZonePolygon.Contains(new[] { new Vector2(0, 0), new Vector2(1, 1) }, 0.5f, 0.5f));
        }

        [Fact]
        public void EdgesFireAsOtherZonesDo()
        {
            Trigger t = Parse(LShape);

            // Checkpoint / end: on entry, and teleporting in only primes.
            TriggerState s = new TriggerState();
            Assert.False(TriggerEvaluator.Fired(t, ref s, new Vector3(2, 50, 2), null, null));
            Assert.False(TriggerEvaluator.Fired(t, ref s, new Vector3(2, 50, 3), null, null));
            Assert.False(TriggerEvaluator.Fired(t, ref s, new Vector3(8, 50, 8), null, null));   // out (the notch)
            Assert.True(TriggerEvaluator.Fired(t, ref s, new Vector3(8, 50, 2), null, null));    // back in

            // Start: crossing - spawned inside, it fires on leaving.
            TriggerState c = new TriggerState();
            Assert.False(TriggerEvaluator.Crossed(t, ref c, new Vector3(2, 50, 2), null, null, null));
            Assert.False(TriggerEvaluator.Crossed(t, ref c, new Vector3(4, 50, 4), null, null, null));
            Assert.True(TriggerEvaluator.Crossed(t, ref c, new Vector3(8, 50, 8), null, null, null));
        }

        [Fact]
        public void ThePolygonIsInTheRouteFingerprint()
        {
            Segment a = new Segment();
            a.Id = "s-1";
            a.Start = Parse("zone 0 0 0 5");
            a.End = Parse(LShape);
            string before = a.RouteFingerprint();

            a.End = Parse(TriggerParser.Write(a.End));
            Assert.Equal(before, a.RouteFingerprint());

            a.End = Parse(LShape.Replace("10.00 5.00 5.00 5.00", "10.00 6.00 5.00 6.00"));
            Assert.NotEqual(before, a.RouteFingerprint());

            a.End = Parse(LShape.Replace("poly 50.00 2.00", "poly 50.00 3.00"));
            Assert.NotEqual(before, a.RouteFingerprint());
        }

        [Fact]
        public void OldShapesWriteUnchanged()
        {
            Assert.Equal("zone 1.00 2.00 3.00 4.00", TriggerParser.Write(Parse("zone 1 2 3 4")));
            Assert.Equal("box 1.00 2.00 3.00 4.00 5.00 6.00", TriggerParser.Write(Parse("box 1 2 3 4 5 6")));
        }

        [Fact]
        public void ASegmentWithAPolygonRoundTripsThroughTheFile()
        {
            Segment s = new Segment();
            s.Id = "s-0123456789ab";
            s.Name = "Poly test";
            s.Start = Parse("zone 0 0 0 5");
            s.Checkpoints.Add(Parse(LShape));
            s.End = Parse("poly 1 1 0 0 3 0 0 3");

            var sb = new System.Text.StringBuilder();
            SegmentFormat.WriteSegment(sb, s, "\n");
            string text = sb.ToString();
            Assert.Contains("check    = " + LShape, text);

            int warnings = 0;
            var back = SegmentFormat.ParseAll(text.Split('\n'), (line, msg) => warnings++);
            Assert.Equal(0, warnings);
            Assert.Single(back);
            Assert.Equal(ZoneShape.Polygon, back[0].Checkpoints[0].Shape);
            Assert.Equal(3, back[0].End.Points.Length);
            Assert.Equal(s.RouteFingerprint(), back[0].RouteFingerprint());
        }

        [Fact]
        public void EditsReturnNewArrays()
        {
            Trigger t = Parse("poly 0 1 0 0 4 0 4 4 0 4");
            Trigger copy = t;   // a struct copy shares the array

            t.Points = ZonePolygon.WithPoint(t.Points, 2, new Vector2(8, 8));
            Assert.Equal(4f, copy.Points[2].x);

            t.Points = ZonePolygon.Added(t.Points, new Vector2(-1, 2));
            Assert.Equal(5, t.Points.Length);
            Assert.Equal(4, copy.Points.Length);

            t.Points = ZonePolygon.Removed(t.Points, 0);
            Assert.Equal(4f, t.Points[0].x);
            Assert.Equal(4, t.Points.Length);

            Vector2[] moved = ZonePolygon.Translated(copy.Points, 10f, -2f);
            Assert.Equal(10f, moved[0].x);
            Assert.Equal(-2f, moved[0].y);
            Assert.Equal(0f, copy.Points[0].x);

            ZonePolygon.Recentre(ref t);
            Vector2 c = ZonePolygon.Centre(t.Points);
            Assert.Equal(c.x, t.Position.x);
            Assert.Equal(c.y, t.Position.z);
        }

        [Fact]
        public void ASquareMatchesTheBoxItReplaces()
        {
            // The same turned box and the square made from it hold the same points.
            Trigger box = Parse("box 10 0 20 3 5 3 30");
            Trigger sq = new Trigger();
            sq.Kind = TriggerKind.Zone;
            sq.Shape = ZoneShape.Polygon;
            sq.Points = ZonePolygon.Square(10f, 20f, 3f, 30f);
            sq.Position = new Vector3(10f, 0f, 20f);
            sq.Extents = new Vector3(0f, 5f, 0f);

            for (float x = 5f; x <= 15f; x += 0.7f)
                for (float z = 15f; z <= 25f; z += 0.7f)
                {
                    // Skip points right on an edge, where rounding decides.
                    bool b = In(box, x, 0f, z), p = In(sq, x, 0f, z);
                    if (b != p)
                    {
                        Trigger nb = box; nb.Extents = new Vector3(2.95f, 5f, 2.95f);
                        Trigger wb = box; wb.Extents = new Vector3(3.05f, 5f, 3.05f);
                        Assert.True(In(wb, x, 0f, z) && !In(nb, x, 0f, z), "differs at " + x + ", " + z);
                    }
                }
        }

        [Theory]
        [InlineData("1 2", true, 1f, 2f)]
        [InlineData("(1.5, -2)", true, 1.5f, -2f)]
        [InlineData("1 2 3", false, 0f, 0f)]
        [InlineData("1", false, 0f, 0f)]
        public void PointsParse(string text, bool ok, float x, float z)
        {
            Vector2 v;
            Assert.Equal(ok, TriggerParser.ParsePoint(text, out v));
            if (ok) { Assert.Equal(x, v.x); Assert.Equal(z, v.y); }
        }

        [Fact]
        public void TidyPointDropsTrailingSeparatorsOnlyWhenComplete()
        {
            Assert.Equal("1 2", TriggerParser.TidyPoint("1 2 "));
            Assert.Equal("1 ", TriggerParser.TidyPoint("1 "));
        }
    }
}
