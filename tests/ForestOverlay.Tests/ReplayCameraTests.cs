using System;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // The replay camera and the ghost figure (Data/ReplayCamera): the look
    // track in the .run, the ghost's pose at a time, each camera view,
    // smoothing, the replay clock and the figure's lines.
    // ------------------------------------------------------------------
    public class ReplayCameraTests
    {
        private static Attempt Straight(float speed, float seconds, bool looks)
        {
            Attempt a = new Attempt();
            for (int i = 0; i <= (int)(seconds * 30f); i++)
            {
                float t = i / 30f;
                RunSample s;
                s.T = t;
                s.P = new Vector3(100f, 10f, 200f + speed * t);   // north (+z)
                s.Speed = speed;
                a.Samples.Add(s);
                if (looks)
                {
                    LookSample l;
                    l.T = t; l.Yaw = 0f; l.Pitch = 10f; l.Eye = 1.5f;
                    a.Looks.Add(l);
                }
            }
            a.Duration = seconds;
            return a;
        }

        private static float Dot(Vector3 a, Vector3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }

        // --- the look track ------------------------------------------------------

        [Fact]
        public void RecorderTakesALookWithEachPositionSample()
        {
            var r = new RunRecorder();
            int asked = 0;
            r.LookSource = (out float yaw, out float pitch, out float eye) =>
            {
                asked++;
                yaw = 45f; pitch = -5f; eye = 1.62f;
                return asked != 2;   // the second one: no camera
            };
            r.Arm(Vector3.zero, "s-test");
            r.ForceStart(Vector3.zero);
            r.Tick(new Vector3(1f, 0f, 0f), 1f, 0.05f);
            r.Tick(new Vector3(2f, 0f, 0f), 1f, 0.05f);
            r.Tick(new Vector3(3f, 0f, 0f), 1f, 0.05f);
            Attempt a = r.Finish();
            Assert.Equal(3, asked);
            Assert.Equal(2, a.Looks.Count);
            Assert.Equal(45f, a.Looks[0].Yaw, 3);
            Assert.Equal(0.05f, a.Looks[0].T, 3);
            Assert.Equal(0.15f, a.Looks[1].T, 3);
        }

        [Fact]
        public void LookLinesRoundTripAndOldRunsHaveNone()
        {
            Attempt a = Straight(5f, 1f, true);
            LookSample l = a.Looks[3];
            l.Yaw = -179.987f; l.Pitch = 33.333f; l.Eye = 0.95f;
            a.Looks[3] = l;
            string text = AttemptFormat.Write(a);
            Assert.Contains("l|0.100|-179.99|33.33|0.95", text);

            Attempt back = AttemptFormat.Parse(text.Split('\n'));
            Assert.Equal(a.Looks.Count, back.Looks.Count);
            Assert.Equal(-179.99f, back.Looks[3].Yaw, 2);
            Assert.Equal(0.95f, back.Looks[3].Eye, 2);

            Attempt old = AttemptFormat.Parse(AttemptFormat.Write(Straight(5f, 1f, false)).Split('\n'));
            Assert.Empty(old.Looks);
            Assert.Equal(31, old.Samples.Count);
        }

        [Fact]
        public void RunnerLineGoesBeforeLookLines()
        {
            string run = "anchor|x\nduration|1.000\nl|0.000|1.00|2.00|1.50\ns|0.000|0|0|0|0\n";
            string named = AttemptFormat.WithRunner(run, "abc", "Name");
            Assert.True(named.IndexOf("runner|abc|Name") < named.IndexOf("l|0.000"));
        }

        // --- angles ----------------------------------------------------------------

        [Fact]
        public void AnglesWrapTheShortWay()
        {
            Assert.Equal(-170f, ReplayCamera.Wrap180(190f), 3);
            Assert.Equal(180f, ReplayCamera.Wrap180(-180f), 3);
            Assert.Equal(20f, ReplayCamera.DeltaAngle(350f, 10f), 3);
            Assert.Equal(-20f, ReplayCamera.DeltaAngle(10f, 350f), 3);
            Assert.Equal(360f, ReplayCamera.LerpAngle(350f, 10f, 0.5f), 3);
        }

        [Fact]
        public void LookAtMatchesUnityAngles()
        {
            float pitch = 0f, yaw = 0f;
            ReplayCamera.LookAt(Vector3.zero, new Vector3(1f, 0f, 0f), ref pitch, ref yaw);
            Assert.Equal(90f, yaw, 3);
            Assert.Equal(0f, pitch, 3);
            ReplayCamera.LookAt(Vector3.zero, new Vector3(0f, -1f, 1f), ref pitch, ref yaw);
            Assert.Equal(0f, yaw, 3);
            Assert.Equal(45f, pitch, 3);   // down is positive
            Vector3 d = ReplayCamera.Direction(45f, 0f);
            Assert.True(d.y < 0f && d.z > 0f);
            Vector3 r = ReplayCamera.Right(90f);
            Assert.Equal(-1f, r.z, 3);
        }

        // --- the ghost's pose --------------------------------------------------------

        [Fact]
        public void PoseInterpolatesPositionAndLook()
        {
            Attempt a = Straight(6f, 2f, true);
            LookSample l0 = a.Looks[30], l1 = a.Looks[31];
            l0.Yaw = 350f; l1.Yaw = 10f; l0.Eye = 1f; l1.Eye = 2f;
            a.Looks[30] = l0; a.Looks[31] = l1;

            GhostPose g;
            Assert.True(ReplayCamera.PoseAt(a, 1f + 1f / 60f, 0f, out g));
            Assert.True(g.HasLook);
            Assert.Equal(206.1f, g.P.z, 2);
            Assert.Equal(0f, g.Yaw, 2);      // through north, not round the other way
            Assert.Equal(1.5f, g.Eye, 2);

            // Clamped to the run.
            Assert.True(ReplayCamera.PoseAt(a, 99f, 0f, out g));
            Assert.Equal(212f, g.P.z, 2);
            Assert.True(ReplayCamera.PoseAt(a, -5f, 0f, out g));
            Assert.Equal(200f, g.P.z, 2);
        }

        [Fact]
        public void ReplayLastsTheRunsTimeNotItsLastSample()
        {
            // T-0297: a run of 11.197 whose last sample is at 11.186.
            Attempt a = Straight(6f, 11.186f, true);
            a.Duration = 11.197f;
            Assert.Equal(11.197f, ReplayCamera.EndOf(a), 3);

            GhostPose g;
            Assert.True(ReplayCamera.PoseAt(a, 11.197f, 0f, out g));
            Assert.Equal(a.Samples[a.Samples.Count - 1].P.z, g.P.z, 3);   // holds the last sample

            a.Duration = 0f;   // unfinished / old runs: the last sample
            Assert.Equal(a.Samples[a.Samples.Count - 1].T, ReplayCamera.EndOf(a), 3);
        }

        [Fact]
        public void WithoutLooksTheGhostFacesWhereItMoves()
        {
            Attempt a = new Attempt();
            for (int i = 0; i <= 60; i++)
            {
                RunSample s;
                s.T = i / 30f;
                s.P = i <= 30 ? new Vector3(i * 0.2f, 0f, 0f) : new Vector3(6f, 0f, 0f);   // east, then standing
                s.Speed = 0f;
                a.Samples.Add(s);
            }
            a.Duration = 2f;
            GhostPose g;
            ReplayCamera.PoseAt(a, 0.5f, 0f, out g);
            Assert.False(g.HasLook);
            Assert.Equal(90f, g.Yaw, 2);
            Assert.Equal(ReplayCamera.DefaultEye, g.Eye, 3);
            ReplayCamera.PoseAt(a, 1.8f, 123f, out g);
            Assert.Equal(123f, g.Yaw, 2);   // standing: keeps the last
        }

        // --- the views ---------------------------------------------------------------

        [Fact]
        public void ChaseIsBehindAboveAndLooksAtTheGhost()
        {
            GhostPose g = default(GhostPose);
            g.P = new Vector3(10f, 5f, 10f);
            g.Yaw = 90f;   // facing east
            CamPose c = ReplayCamera.Chase(g);
            Assert.True(c.Position.x < g.P.x - 3f);
            Assert.True(c.Position.y > g.P.y + 1f);
            Assert.Equal(g.P.z, c.Position.z, 3);
            Assert.Equal(90f, c.Yaw, 2);
            Assert.True(c.Pitch > 0f && c.Pitch < 45f);   // looking a little down
        }

        [Fact]
        public void FirstPersonIsAtTheRecordedEye()
        {
            GhostPose g = default(GhostPose);
            g.P = new Vector3(1f, 2f, 3f);
            g.Yaw = 30f; g.Pitch = 95f; g.Eye = 1.2f; g.HasLook = true;
            CamPose c = ReplayCamera.FirstPerson(g);
            Assert.Equal(3.2f, c.Position.y, 3);
            Assert.Equal(30f, c.Yaw, 3);
            Assert.Equal(89f, c.Pitch, 3);
            g.HasLook = false;
            Assert.Equal(2f + ReplayCamera.DefaultEye, ReplayCamera.FirstPerson(g).Position.y, 3);
        }

        [Fact]
        public void TrajectoryFramesThePathSideOn()
        {
            // A boost: 20 m/s east and up, falling back - an arc.
            Attempt a = new Attempt();
            for (int i = 0; i <= 180; i++)
            {
                float t = i / 30f;
                RunSample s;
                s.T = t;
                s.P = new Vector3(20f * t, 15f * t - 8f * t * t, 0f);
                s.Speed = 20f;
                a.Samples.Add(s);
            }
            a.Duration = 6f;
            Vector3[] window = new Vector3[64];
            int count;
            const float vfov = 60f, aspect = 16f / 9f;
            CamPose c = ReplayCamera.Trajectory(a, 2f, ReplayCamera.TrajectoryBack, ReplayCamera.TrajectoryAhead, vfov, aspect, 90f, window, out count);
            Assert.True(count > 10 && count <= window.Length);

            // Side-on: the camera is off to the side of the travel (z), not behind it.
            Assert.True(Math.Abs(c.Position.z) > 20f);
            Vector3 f = ReplayCamera.Direction(c.Pitch, c.Yaw);
            Vector3 r = ReplayCamera.Right(c.Yaw);
            Vector3 u = ReplayCamera.Up(c.Pitch, c.Yaw);
            double tanV = Math.Tan(vfov * 0.5 * Math.PI / 180.0), tanH = tanV * aspect;
            for (int i = 0; i < count; i++)
            {
                Vector3 d = window[i] - c.Position;
                float depth = Dot(d, f);
                Assert.True(depth > 0f);
                Assert.True(Math.Abs(Dot(d, r)) / depth <= tanH, "point " + i + " off the side");
                Assert.True(Math.Abs(Dot(d, u)) / depth <= tanV, "point " + i + " off the top / bottom");
            }
            Assert.Equal(0f, window[0].x, 2);           // from t - 2 = 0
            Assert.Equal(120f, window[count - 1].x, 1); // to the run's end (t + 4 = 6)
        }

        [Fact]
        public void TrajectoryOfAStandingRunKeepsItsDistance()
        {
            Attempt a = new Attempt();
            for (int i = 0; i < 10; i++) { RunSample s; s.T = i / 30f; s.P = new Vector3(5f, 0f, 5f); s.Speed = 0f; a.Samples.Add(s); }
            a.Duration = 0.3f;
            int count;
            CamPose c = ReplayCamera.Trajectory(a, 0.1f, 2f, 4f, 60f, 1.5f, 0f, new Vector3[8], out count);
            float dx = c.Position.x - 5f, dz = c.Position.z - 5f;
            Assert.True(Math.Sqrt(dx * dx + dz * dz) >= ReplayCamera.TrajectoryMinDistance - 0.01);
        }

        // --- smoothing and the clock --------------------------------------------------

        [Fact]
        public void SmoothingConvergesIndependentOfFrameRate()
        {
            CamPose from = default(CamPose), to = default(CamPose);
            to.Position = new Vector3(10f, 0f, 0f);
            to.Yaw = -170f; from.Yaw = 170f;

            CamPose a = from, b = from;
            for (int i = 0; i < 60; i++) a = ReplayCamera.Smooth(a, to, 6f, 1f / 60f);
            for (int i = 0; i < 240; i++) b = ReplayCamera.Smooth(b, to, 6f, 1f / 240f);
            Assert.Equal(a.Position.x, b.Position.x, 2);
            Assert.True(a.Position.x > 9.9f);
            Assert.True(Math.Abs(ReplayCamera.DeltaAngle(a.Yaw, -170f)) < 0.1f);

            // Half way through a turn the short way: near 180, not near 0.
            CamPose h = ReplayCamera.Smooth(from, to, 6f, (float)(Math.Log(2.0) / 6.0));
            Assert.True(Math.Abs(ReplayCamera.Wrap180(h.Yaw)) > 175f);

            // A far target is cut to.
            to.Position = new Vector3(500f, 0f, 0f);
            Assert.Equal(500f, ReplayCamera.Smooth(from, to, 6f, 0.01f).Position.x, 3);
        }

        [Fact]
        public void ClockPlaysSeeksAndStopsAtTheEnd()
        {
            var c = new ReplayClock();
            c.Start(10f, 3f);
            c.Advance(1f);
            Assert.Equal(4f, c.T, 3);
            c.Slower(); c.Slower();   // 0.25x
            c.Advance(2f);
            Assert.Equal(4.5f, c.T, 3);
            c.Seek(-10f);
            Assert.Equal(0f, c.T, 3);
            c.Faster(); c.Faster(); c.Faster(); c.Faster();
            Assert.Equal(2f, c.Speed, 3);
            c.Advance(100f);
            Assert.True(c.AtEnd && c.Paused);
            c.TogglePause();   // play at the end: from the start
            Assert.False(c.Paused);
            Assert.Equal(0f, c.T, 3);
        }

        [Fact]
        public void TheReplayCameraIsLockedInRunModeByDefault()
        {
            Assert.Equal("replaycam", RunCategory.FeatureOfMark("replay camera").Key);
            Assert.Equal(RunCategory.Locked, new RunCategory().Policy("replaycam"));
        }

        // --- the figure -------------------------------------------------------------

        [Fact]
        public void FigureFacesItsYawAndFitsItsSize()
        {
            Vector3[] v = new Vector3[GhostFigure.Vertices];
            Vector3 feet = new Vector3(50f, 3f, -20f);
            int n = GhostFigure.Build(feet, 90f, 0f, 1.8f, v);
            Assert.Equal(GhostFigure.Vertices, n);
            Assert.Equal(0, n % 2);

            float r = GhostFigure.BodyRadius(1.8f);
            float maxReach = r + GhostFigure.ArrowLength + 0.01f;
            float top = float.MinValue, bottom = float.MaxValue, east = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                Vector3 d = v[i] - feet;
                Assert.True(Math.Sqrt(d.x * d.x + d.z * d.z) <= maxReach + GhostFigure.GazeLength, "vertex " + i);
                top = Math.Max(top, d.y); bottom = Math.Min(bottom, d.y); east = Math.Max(east, d.x);
            }
            Assert.Equal(0f, bottom, 2);
            Assert.True(top >= 1.79f && top <= 1.81f);
            Assert.True(east > r + GhostFigure.ArrowLength - 0.05f);   // the arrow points east

            Assert.Equal(0, GhostFigure.Build(feet, 0f, 0f, 1.8f, new Vector3[10]));
        }

        [Fact]
        public void FigureHeightFollowsTheRecordedEye()
        {
            GhostPose g = default(GhostPose);
            Assert.Equal(ReplayCamera.DefaultHeight, ReplayCamera.FigureHeight(g), 3);
            g.HasLook = true; g.Eye = 1.0f;
            Assert.Equal(1.13f, ReplayCamera.FigureHeight(g), 3);
            g.Eye = 40f;   // a cutscene camera far above: clamped
            Assert.Equal(2.2f, ReplayCamera.FigureHeight(g), 3);
        }
    }
}
