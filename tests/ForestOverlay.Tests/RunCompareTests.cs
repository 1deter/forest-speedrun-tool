using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // RunCompare is the maths behind the ghost delta. It is the piece most
    // likely to be quietly wrong and the hardest to check by playing: a
    // delta that is off by a sample looks plausible on screen.
    // ------------------------------------------------------------------
    public class RunCompareTests
    {
        /// A straight line along +X, one sample per second, one unit apart.
        private static List<RunSample> Line(int count)
        {
            var samples = new List<RunSample>();
            for (int i = 0; i < count; i++)
            {
                RunSample s;
                s.T = i;
                s.P = new Vector3(i, 0f, 0f);
                s.Speed = 1f;
                samples.Add(s);
            }
            return samples;
        }

        private static Attempt AttemptFrom(List<RunSample> samples, float duration)
        {
            var a = new Attempt { Duration = duration, Completed = true };
            a.Samples.AddRange(samples);
            return a;
        }

        // --- ClosestIndex -------------------------------------------------

        [Fact]
        public void ClosestIndex_ReturnsMinusOne_WhenReferenceEmpty()
        {
            Assert.Equal(-1, RunCompare.ClosestIndex(new List<RunSample>(), Vector3.zero, 0, 0));
            Assert.Equal(-1, RunCompare.ClosestIndex(null, Vector3.zero, 0, 0));
        }

        [Theory]
        [InlineData(0f, 0)]
        [InlineData(3f, 3)]
        [InlineData(9f, 9)]
        [InlineData(3.4f, 3)]   // nearer the lower sample
        [InlineData(3.6f, 4)]   // nearer the upper sample
        public void ClosestIndex_FindsNearestSample(float x, int expected)
        {
            var reference = Line(10);
            int i = RunCompare.ClosestIndex(reference, new Vector3(x, 0f, 0f), 0, 0);
            Assert.Equal(expected, i);
        }

        [Fact]
        public void ClosestIndex_HonoursWindowAroundHint()
        {
            var reference = Line(100);

            // The true nearest is 90, but a window of 2 around hint 10 can
            // only see 8..12 - so it must return the best within that, not
            // silently scan the whole run. This is the optimisation that
            // keeps the per-frame lookup local, so it needs pinning down.
            int i = RunCompare.ClosestIndex(reference, new Vector3(90f, 0f, 0f), 10, 2);
            Assert.Equal(12, i);
        }

        [Fact]
        public void ClosestIndex_ZeroWindowScansEverything()
        {
            var reference = Line(100);
            int i = RunCompare.ClosestIndex(reference, new Vector3(90f, 0f, 0f), 0, 0);
            Assert.Equal(90, i);
        }

        // --- Delta ---------------------------------------------------------

        [Fact]
        public void Delta_IsZero_WhenExactlyOnPace()
        {
            var reference = Line(10);
            int hint = 0;
            float delta;

            // At x=5 the reference had taken 5s; we are also at 5s.
            bool ok = RunCompare.Delta(reference, new Vector3(5f, 0f, 0f), 5f, ref hint, out delta);

            Assert.True(ok);
            Assert.Equal(0f, delta, 3);
        }

        [Fact]
        public void Delta_IsNegative_WhenAhead()
        {
            var reference = Line(10);
            int hint = 0;
            float delta;

            // Reached x=5 in 3s where the reference took 5s: 2s ahead.
            RunCompare.Delta(reference, new Vector3(5f, 0f, 0f), 3f, ref hint, out delta);

            Assert.Equal(-2f, delta, 3);
        }

        [Fact]
        public void Delta_IsPositive_WhenBehind()
        {
            var reference = Line(10);
            int hint = 0;
            float delta;

            RunCompare.Delta(reference, new Vector3(5f, 0f, 0f), 8f, ref hint, out delta);

            Assert.Equal(3f, delta, 3);
        }

        [Fact]
        public void Delta_AdvancesTheHint()
        {
            var reference = Line(50);
            int hint = 0;
            float delta;

            RunCompare.Delta(reference, new Vector3(10f, 0f, 0f), 10f, ref hint, out delta);
            Assert.Equal(10, hint);

            RunCompare.Delta(reference, new Vector3(20f, 0f, 0f), 20f, ref hint, out delta);
            Assert.Equal(20, hint);
        }

        [Fact]
        public void Delta_ReturnsFalse_WithNoReference()
        {
            int hint = 0;
            float delta;
            Assert.False(RunCompare.Delta(new List<RunSample>(), Vector3.zero, 1f, ref hint, out delta));
        }

        // --- Best / Average ------------------------------------------------

        [Fact]
        public void Best_PicksShortestCompletedAttempt()
        {
            var attempts = new List<Attempt>
            {
                AttemptFrom(Line(3), 10f),
                AttemptFrom(Line(3), 7f),
                AttemptFrom(Line(3), 9f),
            };

            Assert.Equal(7f, RunCompare.Best(attempts).Duration);
        }

        [Fact]
        public void Best_IgnoresIncompleteAttempts()
        {
            var fast = AttemptFrom(Line(3), 1f);
            fast.Completed = false;

            var attempts = new List<Attempt> { fast, AttemptFrom(Line(3), 9f) };

            // The 1s attempt was aborted, so it must not become the PB.
            Assert.Equal(9f, RunCompare.Best(attempts).Duration);
        }

        [Fact]
        public void Best_IsNull_WhenNothingCompleted()
        {
            Assert.Null(RunCompare.Best(new List<Attempt>()));
        }

        [Fact]
        public void AverageDuration_IgnoresIncompleteAndEmpty()
        {
            var aborted = AttemptFrom(Line(3), 100f);
            aborted.Completed = false;

            var attempts = new List<Attempt>
            {
                AttemptFrom(Line(3), 4f),
                AttemptFrom(Line(3), 6f),
                aborted,
            };

            Assert.Equal(5f, RunCompare.AverageDuration(attempts), 3);
            Assert.Equal(0f, RunCompare.AverageDuration(new List<Attempt>()), 3);
        }

        // --- EvenSplits ----------------------------------------------------

        [Fact]
        public void EvenSplits_AreMonotonicAndWithinDuration()
        {
            var attempt = AttemptFrom(Line(11), 10f);
            float[] splits = RunCompare.EvenSplits(attempt, 3);

            Assert.Equal(3, splits.Length);
            for (int i = 1; i < splits.Length; i++)
                Assert.True(splits[i] >= splits[i - 1], "splits must not go backwards");

            foreach (float s in splits)
                Assert.InRange(s, 0f, attempt.Duration);
        }

        [Fact]
        public void EvenSplits_HandlesDegenerateInput()
        {
            Assert.Empty(RunCompare.EvenSplits(null, 3));
            Assert.Empty(RunCompare.EvenSplits(AttemptFrom(Line(1), 1f), 3));
            Assert.Empty(RunCompare.EvenSplits(AttemptFrom(Line(11), 10f), 0));
        }

        [Fact]
        public void EvenSplits_OnAStationaryRun_IsEmpty()
        {
            // Every sample at the same point: path length is zero, so there
            // is nothing to divide. Must not divide by zero.
            var samples = new List<RunSample>();
            for (int i = 0; i < 5; i++)
            {
                RunSample s;
                s.T = i;
                s.P = Vector3.zero;
                s.Speed = 0f;
                samples.Add(s);
            }

            Assert.Empty(RunCompare.EvenSplits(AttemptFrom(samples, 5f), 3));
        }

        // --- Attempt -------------------------------------------------------

        [Fact]
        public void TopSpeed_AndPathLength_AreComputedFromSamples()
        {
            var samples = Line(5);

            var fast = samples[2];
            fast.Speed = 17.5f;
            samples[2] = fast;

            var attempt = AttemptFrom(samples, 4f);

            Assert.Equal(17.5f, attempt.TopSpeed, 3);
            Assert.Equal(4f, attempt.PathLength, 3);   // 5 points, 1 unit apart
        }
    }

    // ------------------------------------------------------------------
    // The recorder's state machine. The subtle part is that the clock must
    // not start when you are placed at the anchor, only when you leave it -
    // otherwise lining up at the start costs time.
    // ------------------------------------------------------------------
    public class RunRecorderTests
    {
        private static RunRecorder Armed(float radius = 0.5f)
        {
            var r = new RunRecorder { StartRadius = radius, SampleInterval = 0f };
            r.Arm(Vector3.zero, "test");
            return r;
        }

        [Fact]
        public void StartsIdle()
        {
            Assert.Equal(RunRecorder.RunState.Idle, new RunRecorder().State);
        }

        [Fact]
        public void ArmingDoesNotStartTheClock()
        {
            var r = Armed();
            Assert.Equal(RunRecorder.RunState.Armed, r.State);
            Assert.Equal(0f, r.Elapsed);
        }

        [Fact]
        public void StayingInsideTheRadiusDoesNotStart()
        {
            var r = Armed();

            for (int i = 0; i < 10; i++)
                r.Tick(new Vector3(0.4f, 0f, 0f), 0f, 0.1f);

            Assert.Equal(RunRecorder.RunState.Armed, r.State);
            Assert.Equal(0f, r.Elapsed);
        }

        [Fact]
        public void LeavingTheRadiusStartsTheClock()
        {
            var r = Armed();
            r.Tick(new Vector3(0.6f, 0f, 0f), 1f, 0.1f);

            Assert.Equal(RunRecorder.RunState.Running, r.State);
        }

        [Fact]
        public void ElapsedAccumulatesOnlyWhileRunning()
        {
            var r = Armed();

            r.Tick(new Vector3(0.1f, 0f, 0f), 0f, 1f);   // still armed
            Assert.Equal(0f, r.Elapsed);

            r.Tick(new Vector3(2f, 0f, 0f), 1f, 0.5f);   // starts
            r.Tick(new Vector3(3f, 0f, 0f), 1f, 0.5f);

            Assert.Equal(1f, r.Elapsed, 3);
        }

        [Fact]
        public void FinishReturnsCompletedAttemptAndGoesIdle()
        {
            var r = Armed();
            r.Tick(new Vector3(2f, 0f, 0f), 1f, 0.5f);
            r.Tick(new Vector3(3f, 0f, 0f), 1f, 0.5f);

            Attempt done = r.Finish();

            Assert.NotNull(done);
            Assert.True(done.Completed);
            Assert.Equal(1f, done.Duration, 3);
            Assert.Equal("test", done.AnchorLabel);
            Assert.Equal(RunRecorder.RunState.Idle, r.State);
        }

        [Fact]
        public void FinishWithNoRunReturnsNull()
        {
            Assert.Null(new RunRecorder().Finish());
            Assert.Null(Armed().Finish());
        }

        [Fact]
        public void AbortDiscardsTheAttempt()
        {
            var r = Armed();
            r.Tick(new Vector3(2f, 0f, 0f), 1f, 0.5f);
            r.Abort();

            Assert.Equal(RunRecorder.RunState.Idle, r.State);
            Assert.Equal(0f, r.Elapsed);
            Assert.Null(r.Current);
        }

        [Fact]
        public void FirstSampleIsTheAnchorAtTimeZero()
        {
            var r = Armed();
            r.Tick(new Vector3(2f, 0f, 0f), 1f, 0.5f);

            Assert.Equal(Vector3.zero, r.Current.Samples[0].P);
            Assert.Equal(0f, r.Current.Samples[0].T);
        }
    }
}
