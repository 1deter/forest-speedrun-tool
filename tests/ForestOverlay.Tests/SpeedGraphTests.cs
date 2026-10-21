using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    public class SpeedGraphTests
    {
        private static List<RunSample> Run(params float[] speeds)
        {
            List<RunSample> l = new List<RunSample>();
            for (int i = 0; i < speeds.Length; i++)
            {
                RunSample s = new RunSample();
                s.T = i; s.Speed = speeds[i]; s.P = Vector3.zero;
                l.Add(s);
            }
            return l;
        }

        [Fact]
        public void ColumnClampedKeepsASampleNewerThanTheRefreshedTMax()
        {
            // The live run grew past tMax (refreshed every 0.25 s): ColumnOf says -1
            // (the curve flickered), the clamp puts it in the last column.
            Assert.Equal(-1, SpeedGraph.ColumnOf(10.2f, 10f, 100));
            Assert.Equal(99, SpeedGraph.ColumnClamped(10.2f, 10f, 100));
            Assert.Equal(99, SpeedGraph.ColumnClamped(10f, 10f, 100));
            Assert.Equal(0, SpeedGraph.ColumnClamped(0f, 10f, 100));
            Assert.Equal(SpeedGraph.ColumnOf(5f, 10f, 100), SpeedGraph.ColumnClamped(5f, 10f, 100));
            Assert.Equal(-1, SpeedGraph.ColumnClamped(-1f, 10f, 100));
        }

        [Fact]
        public void Median5RemovesATwoSampleSpikeButNotTheStoredRun()
        {
            List<RunSample> r = Run(5, 5, 5, 50, 50, 5, 5, 5, 5);
            float[] m = new float[r.Count]; SpeedGraph.Median5(r, m);
            for (int i = 0; i < m.Length; i++) Assert.Equal(5f, m[i]);
            Assert.Equal(50f, r[3].Speed);
        }

        private static float[] SmoothOf(List<RunSample> r) { float[] m = new float[r.Count]; SpeedGraph.Median5(r, m); return m; }

        [Fact]
        public void OneSampleSpikeIsSmoothedAwayInTheColumns()
        {
            List<RunSample> r = Run(5, 5, 5, 5, 50, 5, 5, 5, 5);
            float[] lo = new float[3], hi = new float[3];
            Assert.True(SpeedGraph.Columns(r, 8f, 3, SmoothOf(r), lo, hi));
            for (int c = 0; c < 3; c++) { Assert.Equal(5f, lo[c]); Assert.Equal(5f, hi[c]); }
        }

        [Fact]
        public void ThreeSampleSpikeSurvivesMinMaxWithFewerColumnsThanSamples()
        {
            // 12 samples (t 0..11) in 3 columns: the 50s at t 4..6 all land in column 1.
            List<RunSample> r = Run(5, 5, 5, 5, 50, 50, 50, 5, 5, 5, 5, 5);
            float[] lo = new float[3], hi = new float[3];
            Assert.True(SpeedGraph.Columns(r, 11f, 3, SmoothOf(r), lo, hi));
            Assert.Equal(50f, hi[1]);
            Assert.Equal(5f, lo[1]);
            Assert.Equal(5f, hi[0]);
            Assert.Equal(5f, hi[2]);
        }

        [Fact]
        public void Median5KeepsAThreeSampleChange()
        {
            List<RunSample> r = Run(5, 5, 5, 50, 50, 50, 5, 5, 5); float[] m = new float[r.Count]; SpeedGraph.Median5(r, m);
            Assert.Equal(50f, m[4]);
        }

        [Fact]
        public void ColumnsKeepMinAndMaxOfEverySampleInIt()
        {
            // 9 samples over 8 s in 3 columns: the middle column holds t=3..5.
            List<RunSample> r = Run(1, 1, 1, 2, 9, 3, 4, 4, 4);
            float[] lo = new float[3], hi = new float[3];
            float[] m = new float[9]; SpeedGraph.Median5(r, m);
            Assert.True(SpeedGraph.Columns(r, 8f, 3, m, lo, hi));
            Assert.Equal(1f, lo[0]);
            Assert.Equal(1f, hi[0]);
            Assert.Equal(4f, hi[2]);
        }

        [Fact]
        public void ColumnsFillGapsBetweenSparseSamples()
        {
            List<RunSample> r = Run(0, 10);
            float[] lo = new float[11], hi = new float[11];
            Assert.True(SpeedGraph.Columns(r, 1f, 11, SmoothOf(r), lo, hi));
            Assert.Equal(5f, lo[5], 3);
            Assert.Equal(5f, hi[5], 3);
        }

        [Fact]
        public void NothingToDrawForShortRuns()
        {
            float[] a = new float[10], b = new float[10];
            Assert.False(SpeedGraph.Columns(Run(3), 1f, 10, new float[1], a, b));
            Assert.False(SpeedGraph.Columns(null, 1f, 10, new float[1], a, b));
        }

        [Fact]
        public void SpeedAtInterpolatesTheStoredValue()
        {
            List<RunSample> r = Run(0, 10, 20);
            float v;
            Assert.True(SpeedGraph.SpeedAt(r, 1.5f, out v));
            Assert.Equal(15f, v, 3);
            Assert.False(SpeedGraph.SpeedAt(r, 2.5f, out v));
        }

        [Fact]
        public void AxisTopIsAMultipleOfFive()
        {
            Assert.Equal(5f, SpeedGraph.AxisTop(0f));
            Assert.Equal(10f, SpeedGraph.AxisTop(6.2f));
            Assert.Equal(15f, SpeedGraph.AxisTop(15f));
        }
    }
}
