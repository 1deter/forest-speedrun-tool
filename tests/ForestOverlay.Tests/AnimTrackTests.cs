using System;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // The arms' animator track (Data/AnimTrack, T-0387): recording by the
    // RunRecorder, the anim / a / af / ah lines, and the pose at a time.
    // ------------------------------------------------------------------
    public class AnimTrackTests
    {
        private static readonly string[] Names = { "vSpeed", "overallSpeed" };

        private static AnimSnapshot Snap(int h0, float t0, int h1, float t1, float w1, float f0, float f1, params int[] held)
        {
            AnimSnapshot s = new AnimSnapshot();
            s.Size(2, Names);
            s.Hash[0] = h0; s.Time[0] = t0; s.Weight[0] = 1f;
            s.Hash[1] = h1; s.Time[1] = t1; s.Weight[1] = w1;
            s.Floats[0] = f0; s.Floats[1] = f1;
            s.Held = held;
            s.Yaw = 90f;
            return s;
        }

        private static Attempt Sample()
        {
            Attempt a = new Attempt();
            RunSample p; p.T = 0f; p.P = Vector3.zero; p.Speed = 0f;
            a.Samples.Add(p);
            AnimTrack an = new AnimTrack(2, Names);
            an.Add(0.0f, Snap(111, 0.0f, 0, 0f, 0f, 0f, 0.5f, 80, 48));
            an.Add(0.1f, Snap(111, 0.1f, 0, 0f, 0f, 0.005f, 0.5f, 80, 48));     // floats within epsilon: not written
            an.Add(0.2f, Snap(111, 0.2f, 222, 0.0f, 1f, 1f, 0.5f, 80, 48));
            an.Add(0.3f, Snap(333, 0.0f, 222, 0.5f, 0.5f, 1f, 0.5f, 43, 48));
            a.Anim = an;
            return a;
        }

        [Fact]
        public void RecordsFlatAndDiffsFloatsAndHeld()
        {
            AnimTrack an = Sample().Anim;
            Assert.Equal(4, an.Count);
            Assert.Equal(8, an.Hash.Count);
            // vSpeed on the first sample, then at 0.2; overallSpeed only on the first
            Assert.Equal(3, an.FloatChanges.Count);
            Assert.Equal(2, an.HeldChanges.Count);
            Assert.Equal(0, an.Hash[1]);   // layer 1 off at weight 0 -> hash 0
        }

        [Fact]
        public void LinesRoundTrip()
        {
            Attempt a = Sample();
            string text = AttemptFormat.Write(a);
            Assert.Contains("anim|2|vSpeed|overallSpeed", text);
            Assert.Contains("a|0.000|90.0|111:0.000|-", text);
            Assert.Contains("a|0.200|90.0|111:0.200|222:0.000:1.00", text);
            Assert.Contains("af|0.000|0=0.000|1=0.500", text);
            Assert.Contains("ah|0.300|43|48", text);

            Attempt back = AttemptFormat.Parse(text.Split('\n'));
            Assert.NotNull(back.Anim);
            Assert.Equal(2, back.Anim.Layers);
            Assert.Equal(new[] { "vSpeed", "overallSpeed" }, back.Anim.Floats);
            Assert.Equal(4, back.Anim.Count);
            Assert.Equal(222, back.Anim.Hash[2 * 2 + 1]);
            Assert.Equal(0.5f, back.Anim.Time[3 * 2 + 1], 3);
            Assert.Equal(0.5f, back.Anim.Weight[3 * 2 + 1], 2);
            Assert.Equal(0, back.Anim.Hash[1]);
            Assert.Equal(3, back.Anim.FloatChanges.Count);
            Assert.Equal(new[] { 43, 48 }, back.Anim.HeldChanges[1].Ids);
        }

        [Fact]
        public void RunsWithoutTheLinesHaveNoAnim()
        {
            Attempt a = Sample();
            a.Anim = null;
            Attempt back = AttemptFormat.Parse(AttemptFormat.Write(a).Split('\n'));
            Assert.Null(back.Anim);
            Assert.Single(back.Samples);
        }

        [Fact]
        public void LinesBeforeTheHeaderAndUnknownLinesAreSkipped()
        {
            string run = "a|0.000|0|1:0.000\naf|0.000|0=1\nzz|1|2\ns|0.000|0|0|0|0\n";
            Attempt back = AttemptFormat.Parse(run.Split('\n'));
            Assert.Null(back.Anim);
            Assert.Single(back.Samples);
        }

        [Fact]
        public void RunnerLineGoesBeforeTheAnimLines()
        {
            string run = "anchor|x\nduration|1.000\nanim|1|f\na|0.000|0.0|5:0.000\ns|0.000|0|0|0|0\n";
            string named = AttemptFormat.WithRunner(run, "abc", "Name");
            Assert.True(named.IndexOf("runner|abc|Name") < named.IndexOf("anim|1"));
        }

        [Fact]
        public void PoseLerpsTheSameStateAndStepsOnAChange()
        {
            AnimTrack an = Sample().Anim;
            int[] h = new int[2]; float[] tm = new float[2], w = new float[2], f = new float[2];
            float yaw;

            Assert.True(an.PoseAt(0.05f, h, tm, w, f, out yaw));
            Assert.Equal(111, h[0]);
            Assert.Equal(0.05f, tm[0], 3);
            Assert.Equal(90f, yaw, 2);
            Assert.Equal(0, h[1]);

            // 0.2 -> 0.3: layer 0 switches 111 -> 333: the earlier state holds
            an.PoseAt(0.25f, h, tm, w, f, out yaw);
            Assert.Equal(111, h[0]);
            Assert.Equal(0.2f, tm[0], 3);
            // layer 1 is 222 in both: lerps (weight 1 -> 0.5, time 0 -> 0.5)
            Assert.Equal(222, h[1]);
            Assert.Equal(0.25f, tm[1], 3);
            Assert.Equal(0.75f, w[1], 3);

            // after the end it holds the last sample
            an.PoseAt(5f, h, tm, w, f, out yaw);
            Assert.Equal(333, h[0]);
        }

        [Fact]
        public void FloatsLerpBetweenChangesAndHoldOutsideThem()
        {
            AnimTrack an = Sample().Anim;
            int[] h = new int[2]; float[] tm = new float[2], w = new float[2], f = new float[2];
            float yaw;
            an.PoseAt(0.1f, h, tm, w, f, out yaw);   // vSpeed: 0 at 0.0, 1 at 0.2
            Assert.Equal(0.5f, f[0], 3);
            Assert.Equal(0.5f, f[1], 3);
            an.PoseAt(0.3f, h, tm, w, f, out yaw);
            Assert.Equal(1f, f[0], 3);
            an.PoseAt(-1f, h, tm, w, f, out yaw);
            Assert.Equal(0f, f[0], 3);
        }

        [Fact]
        public void HeldAtIsTheLastChangeAtOrBefore()
        {
            AnimTrack an = Sample().Anim;
            Assert.Equal(new[] { 80, 48 }, an.HeldAt(0.1f));
            Assert.Equal(new[] { 43, 48 }, an.HeldAt(0.3f));
            Assert.Equal(-1, an.HeldIndexAt(-0.5f));
            Assert.Null(an.HeldAt(-0.5f));
            System.Collections.Generic.List<int> ids = new System.Collections.Generic.List<int>();
            an.AllHeldIds(ids);
            Assert.Equal(new[] { 80, 48, 43 }, ids.ToArray());
        }

        [Fact]
        public void YawTurnsTheShortWay()
        {
            AnimTrack an = new AnimTrack(1, new string[0]);
            AnimSnapshot s = new AnimSnapshot();
            s.Size(1, new string[0]);
            s.Hash[0] = 1; s.Yaw = 350f;
            an.Add(0f, s);
            s.Yaw = 10f;
            an.Add(1f, s);
            int[] h = new int[1]; float[] tm = new float[1], w = new float[1], f = new float[0];
            float yaw;
            an.PoseAt(0.5f, h, tm, w, f, out yaw);
            Assert.Equal(360f, yaw, 2);
        }

        [Fact]
        public void RecorderSamplesOnItsOwnCadenceAndSkipsAMismatchedAnimator()
        {
            var r = new RunRecorder();
            int asked = 0;
            r.AnimSource = s =>
            {
                asked++;
                s.Size(asked == 5 ? 3 : 2, Names);
                s.Hash[0] = 1;
                return true;
            };
            r.Arm(Vector3.zero, "s-test");
            r.ForceStart(Vector3.zero);
            for (int i = 0; i < 60; i++) r.Tick(new Vector3(i, 0f, 0f), 1f, 1f / 30f);   // 2 s
            Attempt a = r.Finish();
            Assert.InRange(asked, 19, 21);    // 10 Hz
            Assert.NotNull(a.Anim);
            Assert.Equal(asked - 1, a.Anim.Count);   // the 3-layer reading was skipped
        }

        [Fact]
        public void RecorderWithoutASourceLeavesAnimNull()
        {
            var r = new RunRecorder();
            r.Arm(Vector3.zero, "s-test");
            r.ForceStart(Vector3.zero);
            r.Tick(Vector3.zero, 0f, 0.5f);
            Assert.Null(r.Finish().Anim);
        }
    }
}
