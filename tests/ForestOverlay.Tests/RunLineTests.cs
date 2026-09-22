using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    public class RunLineTests
    {
        private static RunSample At(float t, float x)
        {
            RunSample s;
            s.T = t;
            s.P = new Vector3(x, 0f, 0f);
            s.Speed = 0f;
            return s;
        }

        // --- LineBuffer ---------------------------------------------------

        [Fact]
        public void PointsCloserThanTheSpacingAreDropped()
        {
            LineBuffer b = new LineBuffer(1f);
            b.Append(new Vector3(0f, 0f, 0f));
            b.Append(new Vector3(0.5f, 0f, 0f));   // too close to 0
            b.Append(new Vector3(1.2f, 0f, 0f));
            b.Append(new Vector3(1.2f, 0f, 0f));   // standing still

            Assert.Equal(2, b.Count);
            Assert.Equal(1.2f, b.Points[1].x);
        }

        [Fact]
        public void SyncOnlyWalksNewSamples()
        {
            List<RunSample> samples = new List<RunSample> { At(0f, 0f), At(1f, 2f) };
            LineBuffer b = new LineBuffer(1f);

            b.Sync(samples);
            Assert.Equal(2, b.Count);
            Assert.Equal(2, b.SourceCount);

            samples.Add(At(2f, 4f));
            b.Sync(samples);
            Assert.Equal(3, b.Count);
            Assert.Equal(4f, b.Points[2].x);
        }

        [Fact]
        public void ASourceThatShrankStartsTheLineOver()
        {
            LineBuffer b = new LineBuffer(1f);
            b.Sync(new List<RunSample> { At(0f, 0f), At(1f, 5f), At(2f, 10f) });

            b.Sync(new List<RunSample> { At(0f, 100f) });

            Assert.Equal(1, b.Count);
            Assert.Equal(100f, b.Points[0].x);
        }

        [Fact]
        public void CapacityGrowsWithoutLosingPoints()
        {
            LineBuffer b = new LineBuffer(0.1f);
            for (int i = 0; i < 1000; i++) b.Append(new Vector3(i, 0f, 0f));

            Assert.Equal(1000, b.Count);
            Assert.Equal(999f, b.Points[999].x);
        }

        // --- ghost position ------------------------------------------------

        private static readonly List<RunSample> Path = new List<RunSample>
        {
            At(0f, 0f), At(1f, 10f), At(2f, 20f), At(3f, 30f),
        };

        [Fact]
        public void GhostInterpolatesBetweenSamples()
        {
            int hint = 0;
            Vector3 p;
            Assert.True(RunCompare.PositionAt(Path, 3f, 1.5f, ref hint, out p));
            Assert.Equal(15f, p.x, 3);
        }

        [Fact]
        public void GhostResumesFromTheHintAndMatchesAColdSearch()
        {
            int hint = 0;
            Vector3 p;
            for (float t = 0f; t <= 3f; t += 0.25f)
            {
                int cold = 0;
                Vector3 expected;
                RunCompare.PositionAt(Path, 3f, t, ref cold, out expected);

                Assert.True(RunCompare.PositionAt(Path, 3f, t, ref hint, out p));
                Assert.Equal(expected.x, p.x, 3);
            }
        }

        [Fact]
        public void GhostRestartsWhenTimeGoesBackwards()
        {
            int hint = 0;
            Vector3 p;
            RunCompare.PositionAt(Path, 3f, 2.5f, ref hint, out p);

            // A new run: t is back near zero.
            Assert.True(RunCompare.PositionAt(Path, 3f, 0.5f, ref hint, out p));
            Assert.Equal(5f, p.x, 3);
        }

        [Fact]
        public void NoGhostPastTheEnd()
        {
            int hint = 0;
            Vector3 p;
            Assert.False(RunCompare.PositionAt(Path, 3f, 3.5f, ref hint, out p));
        }

        // --- lazy state sampling ------------------------------------------

        [Fact]
        public void StateSourceIsOnlyReadWhenASampleIsDue()
        {
            int reads = 0;
            var r = new RunRecorder { SampleInterval = 0f, StateInterval = 0.2f };
            r.StateSource = () => { reads++; return new[] { 1f }; };

            r.Arm(Vector3.zero, "test");
            r.ForceStart(Vector3.zero);

            // 1 s of 100 Hz frames: five state samples, not a hundred reads.
            for (int i = 0; i < 100; i++) r.Tick(Vector3.zero, 0f, 0.01f);

            Assert.InRange(reads, 5, 6);
            Assert.Equal(reads, r.Current.States.Count);
        }
    }
}
