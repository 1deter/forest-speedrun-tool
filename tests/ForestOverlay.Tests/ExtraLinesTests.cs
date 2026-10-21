using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // T-0374: the extra comparison lines' picks, the crosshair aim and the best-segments run.
    public class ExtraLinesTests
    {
        [Fact]
        public void Picks_RoundTripAndCap()
        {
            List<string> l = ExtraPicks.Parse("pb, last,pb,runner:ab,,best");
            Assert.Equal(4, l.Count);
            Assert.Equal("pb,last,runner:ab,best", ExtraPicks.Serialize(l));
            for (int i = 0; i < 10; i++) ExtraPicks.Set(l, "x" + i, true);
            Assert.Equal(ExtraPicks.Max, l.Count);
            Assert.False(ExtraPicks.Set(l, "one-more", true));
            Assert.True(ExtraPicks.Set(l, "pb", false));
            Assert.True(ExtraPicks.Set(l, "one-more", true));
            Assert.Equal(-1, ExtraPicks.IndexOf(l, "pb"));
        }

        private static readonly Vector3 Fwd = new Vector3(0, 0, 1), Right = new Vector3(1, 0, 0), Up = new Vector3(0, 1, 0);

        [Fact]
        public void Aim_FindsTheSegmentUnderTheCentre()
        {
            // A line crossing the view at depth 10, 0.5 above the aim.
            Vector3[] pts = { new Vector3(-5, 0.5f, 10), new Vector3(5, 0.5f, 10), new Vector3(5, 0.5f, 20) };
            int i; float u, d; Vector3 p;
            Assert.True(LineAim.Nearest(pts, 0, 3, Vector3.zero, Fwd, Right, Up, 0.1f, out i, out u, out p, out d));
            Assert.Equal(0, i);
            Assert.InRange(d, 0.049f, 0.051f);          // 0.5 / 10
            Assert.InRange(u, 0.49f, 0.51f);            // the middle
            Assert.InRange(p.x, -0.1f, 0.1f);
        }

        [Fact]
        public void Aim_BehindTheCameraIsNothing()
        {
            Vector3[] pts = { new Vector3(0, 0, -5), new Vector3(1, 0, -9) };
            int i; float u, d; Vector3 p;
            Assert.False(LineAim.Nearest(pts, 0, 2, Vector3.zero, Fwd, Right, Up, 0.1f, out i, out u, out p, out d));
        }

        [Fact]
        public void Aim_ClipsAtTheNearPlane()
        {
            // Runs from behind the eye to far ahead along the aim: passes through the centre.
            Vector3[] pts = { new Vector3(0, 0, -10), new Vector3(0, 0, 10) };
            int i; float u, d; Vector3 p;
            Assert.True(LineAim.Nearest(pts, 0, 2, Vector3.zero, Fwd, Right, Up, 0.1f, out i, out u, out p, out d));
            Assert.InRange(d, 0f, 0.0001f);
            Assert.True(p.z >= 0.1f - 0.001f);
        }

        [Fact]
        public void Aim_PerspectiveCorrectAlongTheSegment()
        {
            // Depth 2 to depth 20 on the same side; the answer is the true nearest point.
            Vector3[] pts = { new Vector3(-1, 0.2f, 2), new Vector3(1, 0.2f, 20) };
            int i; float u, d; Vector3 p;
            Assert.True(LineAim.Nearest(pts, 0, 2, Vector3.zero, Fwd, Right, Up, 0.1f, out i, out u, out p, out d));
            for (int k = 0; k <= 100; k++)
            {
                Vector3 q = pts[0] + (pts[1] - pts[0]) * (k / 100f);
                float off = new Vector2(q.x / q.z, q.y / q.z).magnitude;
                Assert.True(off >= d - 0.002f);
            }
            Assert.InRange(new Vector2(p.x / p.z, p.y / p.z).magnitude, d - 0.002f, d + 0.002f);
        }

        private static Attempt Run(float[] splits, float duration)
        {
            Attempt a = new Attempt();
            a.Completed = true;
            a.Splits = splits;
            a.Duration = duration;
            for (float t = 0f; t <= duration + 0.001f; t += 0.5f)
            {
                RunSample s = new RunSample();
                s.T = t; s.P = new Vector3(t, 0, 0); s.Speed = 1f;
                a.Samples.Add(s);
            }
            return a;
        }

        [Fact]
        public void BestSegments_TakesEachStretchFromItsFastestRun()
        {
            // a: segments 4, 6, 5 (15 s). b: 6, 3, 4 (13 s). Best = 4, 3, 4 = 11.
            Attempt a = Run(new[] { 4f, 10f }, 15f);
            Attempt b = Run(new[] { 6f, 9f }, 13f);
            Attempt c = BestSegmentsRun.Build(new List<Attempt> { a, b }, 2);
            Assert.NotNull(c);
            Assert.Equal(11f, c.Duration, 3);
            Assert.Equal(2, c.Splits.Length);
            Assert.Equal(4f, c.Splits[0], 3);
            Assert.Equal(7f, c.Splits[1], 3);
            for (int i = 1; i < c.Samples.Count; i++) Assert.True(c.Samples[i].T >= c.Samples[i - 1].T);
            Assert.Equal(0f, c.Samples[0].T, 3);
            // At clock 5 (second stretch, from b: x = 6..9 over t 6..9, shifted by -2) x is 7.
            bool found = false;
            for (int i = 0; i < c.Samples.Count; i++)
                if (System.Math.Abs(c.Samples[i].T - 5f) < 0.001f) { Assert.Equal(7f, c.Samples[i].P.x, 3); found = true; }
            Assert.True(found);
        }

        [Fact]
        public void BestSegments_NeedsAFinishedRunWithTheSameRows()
        {
            Attempt a = Run(new[] { 4f }, 10f);
            Attempt notDone = Run(new[] { 4f, 10f }, 15f);
            notDone.Completed = false;
            Assert.Null(BestSegmentsRun.Build(new List<Attempt> { a, notDone }, 2));
            Assert.Null(BestSegmentsRun.Build(new List<Attempt>(), 1));
            Assert.NotNull(BestSegmentsRun.Build(new List<Attempt> { a }, 1));
        }
    }
}
