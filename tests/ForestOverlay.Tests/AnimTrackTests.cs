using System;
using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // The arms' animator track (Data/AnimTrack, T-0387): changes only by
    // dead reckoning - keys when a state changes or its advance drifts -
    // recording by the RunRecorder, the anim / ak / af / ay / ah lines,
    // and the pose at a time.
    // ------------------------------------------------------------------
    public class AnimTrackTests
    {
        private static readonly string[] Names = { "vSpeed", "normCamY" };

        /// Two layers: layer 0 state `h0` at `t0` (1 per second), layer 1
        /// `h1` at weight `w1` (0 = off).
        private static AnimSnapshot Snap(int h0, float t0, int h1, float t1, float w1)
        {
            AnimSnapshot s = new AnimSnapshot();
            s.Size(2, Names);
            s.Hash[0] = h0; s.Time[0] = t0; s.Rate[0] = 1f; s.Weight[0] = 1f;
            s.Hash[1] = h1; s.Time[1] = t1; s.Rate[1] = 0.5f; s.Weight[1] = w1;
            s.Held = new int[0];
            return s;
        }

        private static int KeysOf(AnimTrack an, int layer)
        {
            int n = 0;
            foreach (AnimLayerKey k in an.LayerKeys) if (k.Layer == layer) n++;
            return n;
        }

        [Fact]
        public void AStateAdvancingAtItsRateIsKeyedOnce()
        {
            AnimTrack an = new AnimTrack(2, Names);
            for (int i = 0; i <= 60; i++)
            {
                float t = i / 60f;
                an.AddLayers(t, Snap(111, t, 222, 0.5f * t, 1f));
            }
            Assert.Equal(1, KeysOf(an, 0));
            Assert.Equal(1, KeysOf(an, 1));
        }

        [Fact]
        public void DriftChangeAndOffAreKeyed()
        {
            AnimTrack an = new AnimTrack(2, Names);
            an.AddLayers(0f, Snap(111, 0f, 222, 0f, 1f));
            an.AddLayers(0.1f, Snap(111, 0.1f, 222, 0.05f, 1f));   // as predicted: no key
            an.AddLayers(0.2f, Snap(111, 0.25f, 222, 0.1f, 1f));   // layer 0 off by 0.05: key
            an.AddLayers(0.3f, Snap(333, 0f, 222, 0.15f, 1f));     // a new state: key
            an.AddLayers(0.4f, Snap(333, 0.1f, 222, 0.2f, 0f));    // layer 1 off: key, hash 0
            an.AddLayers(0.5f, Snap(333, 0.2f, 222, 0.25f, 0f));   // still off: nothing
            Assert.Equal(3, KeysOf(an, 0));
            Assert.Equal(2, KeysOf(an, 1));
            AnimLayerKey off = an.LayerKeys[an.LayerKeys.Count - 1];
            Assert.Equal(1, off.Layer);
            Assert.Equal(0, off.S.Hash);
        }

        [Fact]
        public void ACrossfadeIsKeyedAtItsStartAndEndAndPlaysItsProgress()
        {
            AnimTrack an = new AnimTrack(1, new string[0]);
            AnimSnapshot s = new AnimSnapshot();
            s.Size(1, new string[0]);
            s.Held = new int[0];
            // 0.0: state 5; 0.1 .. 0.3: crossfading to 6 over 0.2 s; 0.4: in 6
            for (int i = 0; i <= 4; i++)
            {
                float t = i * 0.1f;
                s.Hash[0] = i < 3 || i == 3 ? 5 : 6; s.Time[0] = t; s.Rate[0] = 1f; s.Weight[0] = 1f;
                s.Next[0] = 0;
                if (i >= 1 && i <= 3) { s.Next[0] = 6; s.NextTime[0] = (i - 1) * 0.1f + 0.1f; s.NextRate[0] = 1f; s.P[0] = Math.Min(1f, i * 0.33f); }
                if (i == 4) { s.Hash[0] = 6; s.Time[0] = 0.4f; }
                an.AddLayers(t, s);
            }
            // 0.0 plain, 0.1 crossfade start (progress 0.33, 3.3/s), 0.4 the end
            Assert.Equal(3, an.LayerKeys.Count);
            Assert.Equal(6, an.LayerKeys[1].S.Next);
            Assert.Equal(3.3f, an.LayerKeys[1].S.PRate, 2);

            AnimLayerState[] l = new AnimLayerState[1];
            float yaw;
            Assert.True(an.PoseAt(0.25f, l, new float[0], out yaw));
            Assert.Equal(5, l[0].Hash);
            Assert.Equal(6, l[0].Next);
            Assert.Equal(0.825f, l[0].P, 3);      // 0.33 + 0.15 * 3.3
            Assert.Equal(0.25f, l[0].Time, 3);
            an.PoseAt(0.39f, l, new float[0], out yaw);
            Assert.Equal(1f, l[0].P, 3);           // never past the end
        }

        [Fact]
        public void BeforeTheFirstKeyTheFirstKeyHolds()
        {
            AnimTrack an = new AnimTrack(2, Names);
            an.AddLayers(1f, Snap(111, 0.5f, 222, 0.2f, 0.8f));
            AnimLayerState[] l = new AnimLayerState[2];
            float yaw;
            Assert.True(an.PoseAt(0f, l, new float[2], out yaw));
            Assert.Equal(111, l[0].Hash);
            Assert.Equal(0.5f, l[0].Time, 3);
            Assert.Equal(0.8f, l[1].Weight, 3);
            Assert.False(new AnimTrack(1, new string[0]).PoseAt(0f, l, new float[0], out yaw));
        }

        [Fact]
        public void WeightFadesAtItsRateAndStaysInRange()
        {
            AnimTrack an = new AnimTrack(2, Names);
            for (int i = 0; i <= 20; i++)
            {
                float t = i * 0.05f;
                an.AddLayers(t, Snap(111, t, 222, 0.5f * t, Math.Min(1f, 0.1f + t)));   // 1 / s
            }
            Assert.InRange(KeysOf(an, 1), 2, 3);
            AnimLayerState[] l = new AnimLayerState[2];
            float yaw;
            an.PoseAt(0.5f, l, new float[2], out yaw);
            Assert.Equal(0.6f, l[1].Weight, 2);
            an.PoseAt(5f, l, new float[2], out yaw);
            Assert.True(l[1].Weight <= 1f);
        }

        [Fact]
        public void FloatsAreKeyedWhenTheirExtrapolationDrifts()
        {
            AnimTrack an = new AnimTrack(1, Names);
            AnimSnapshot s = new AnimSnapshot();
            s.Size(1, Names);
            s.Held = new int[0];
            for (int i = 0; i <= 30; i++)   // vSpeed ramps 2 / s, normCamY holds
            {
                float t = i / 30f;
                s.Floats[0] = 2f * t; s.Floats[1] = 0.25f;
                an.AddFloats(t, s);
            }
            int ramp = 0, hold = 0;
            foreach (AnimFloatKey k in an.FloatKeys) if (k.Index == 0) ramp++; else hold++;
            Assert.Equal(2, ramp);   // the first poll, then the rate
            Assert.Equal(1, hold);

            s.Floats[0] = 2f; s.Floats[1] = 0.25f;   // vSpeed stops at 2
            an.AddFloats(31f / 30f, s);
            AnimLayerState[] l = new AnimLayerState[1];
            float[] f = new float[2];
            float yaw;
            an.AddLayers(0f, Snap1());
            an.PoseAt(0.5f, l, f, out yaw);
            Assert.Equal(1f, f[0], 2);
            Assert.Equal(0.25f, f[1], 3);
            an.PoseAt(40f, l, f, out yaw);
            Assert.Equal(2f, f[0], 2);   // the stop was keyed
        }

        private static AnimSnapshot Snap1()
        {
            AnimSnapshot s = new AnimSnapshot();
            s.Size(1, Names);
            s.Hash[0] = 1; s.Weight[0] = 1f;
            s.Held = new int[0];
            return s;
        }

        [Fact]
        public void AFloatIsNeverExtrapolatedPastTheLimit()
        {
            AnimTrack an = new AnimTrack(1, Names);
            AnimFloatKey k;
            k.T = 0f; k.Index = 0; k.Value = 0f; k.Rate = 1f;
            an.AddFloatKey(k);
            an.AddLayers(0f, Snap1());
            float[] f = new float[2];
            float yaw;
            an.PoseAt(10f, new AnimLayerState[1], f, out yaw);
            Assert.Equal(AnimTrack.MaxExtrapolate, f[0], 3);
        }

        [Fact]
        public void YawTurnsTheShortWay()
        {
            AnimTrack an = new AnimTrack(1, new string[0]);
            AnimSnapshot s = new AnimSnapshot();
            s.Size(1, new string[0]);
            s.Hash[0] = 1; s.Weight[0] = 1f; s.Held = new int[0];
            s.Yaw = 350f;
            an.AddLayers(0f, s);
            an.AddFloats(0f, s);
            s.Yaw = 10f;
            an.AddFloats(1f, s);   // +20 deg over 1 s, not -340
            Assert.Equal(2, an.YawKeys.Count);
            Assert.Equal(20f, an.YawKeys[1].Rate, 1);
            float yaw;
            an.PoseAt(1.5f, new AnimLayerState[1], new float[0], out yaw);
            Assert.Equal(20f, yaw, 1);
        }

        [Fact]
        public void HeldIsKeptWhenItChanges()
        {
            AnimTrack an = new AnimTrack(1, new string[0]);
            AnimSnapshot s = Snap1();
            s.Size(1, new string[0]);
            s.Held = new[] { 80, 48 };
            an.AddFloats(0f, s);
            an.AddFloats(0.1f, s);
            s.Held = new[] { 56, 48 };
            an.AddFloats(0.2f, s);
            Assert.Equal(2, an.HeldChanges.Count);
            Assert.Equal(new[] { 80, 48 }, an.HeldAt(0.1f));
            Assert.Equal(new[] { 56, 48 }, an.HeldAt(0.3f));
            Assert.Null(an.HeldAt(-0.5f));
            List<int> ids = new List<int>();
            an.AllHeldIds(ids);
            Assert.Equal(new[] { 80, 48, 56 }, ids.ToArray());
        }

        private static Attempt Sample()
        {
            Attempt a = new Attempt();
            RunSample p; p.T = 0f; p.P = Vector3.zero; p.Speed = 0f;
            a.Samples.Add(p);
            AnimTrack an = new AnimTrack(2, Names);
            AnimSnapshot s = Snap(111, 0f, 222, 0f, 0.75f);
            s.Floats[0] = 0f; s.Floats[1] = 0.3f; s.Yaw = 90f; s.Held = new[] { 80, 48 };
            for (int i = 0; i <= 12; i++)
            {
                float t = i / 30f;
                s.Time[0] = i < 6 ? t * 1.3f : t;              // drifts: keys
                s.Time[1] = 0.5f * t;
                s.Weight[1] = i >= 9 ? 0f : 0.75f;            // off from 0.3
                s.Next[0] = i >= 4 && i < 8 ? 333 : 0;          // a crossfade 0.133 .. 0.233
                s.NextTime[0] = 0.1f * i; s.NextRate[0] = 2f; s.P[0] = (i - 3) * 0.25f;
                s.Floats[0] = 0.7f * t;
                s.Yaw = 90f + 30f * t;
                if (i == 10) s.Held = new[] { 43, 48 };
                an.AddLayers(t, s);
                an.AddFloats(t, s);
            }
            a.Anim = an;
            return a;
        }

        [Fact]
        public void LinesRoundTripAndPlayTheSame()
        {
            Attempt a = Sample();
            string text = AttemptFormat.Write(a);
            Assert.Contains("anim|2|vSpeed|normCamY", text);
            Assert.Contains("ak|0.000|0|111|0.000|1.0000|1.000|0.000", text);
            Assert.Contains("ak|0.300|1|-", text);
            Assert.Contains("|333|", text);
            Assert.Contains("af|0.000|0=0.000:0.000|1=0.300:0.000", text);
            Assert.Contains("ay|0.000|90.0|0.0", text);
            Assert.Contains("ah|0.333|43|48", text);

            Attempt back = AttemptFormat.Parse(text.Split('\n'));
            AnimTrack x = a.Anim, y = back.Anim;
            Assert.NotNull(y);
            Assert.Equal(2, y.Layers);
            Assert.Equal(Names, y.Floats);
            Assert.Equal(x.LayerKeys.Count, y.LayerKeys.Count);
            Assert.Equal(x.FloatKeys.Count, y.FloatKeys.Count);
            Assert.Equal(x.YawKeys.Count, y.YawKeys.Count);
            Assert.Equal(new[] { 43, 48 }, y.HeldChanges[1].Ids);

            AnimLayerState[] lx = new AnimLayerState[2], ly = new AnimLayerState[2];
            float[] fx = new float[2], fy = new float[2];
            for (float t = -0.1f; t < 0.6f; t += 0.017f)
            {
                float yx, yy;
                x.PoseAt(t, lx, fx, out yx);
                y.PoseAt(t, ly, fy, out yy);
                for (int l = 0; l < 2; l++)
                {
                    Assert.Equal(lx[l].Hash, ly[l].Hash);
                    Assert.Equal(lx[l].Next, ly[l].Next);
                    Assert.Equal(lx[l].Time, ly[l].Time, 3);
                    Assert.Equal(lx[l].Weight, ly[l].Weight, 3);
                    Assert.Equal(lx[l].NextTime, ly[l].NextTime, 3);
                    Assert.Equal(lx[l].P, ly[l].P, 3);
                }
                Assert.Equal(fx[0], fy[0], 3);
                Assert.Equal(fx[1], fy[1], 3);
                Assert.Equal(yx, yy, 1);
            }
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
            string run = "ak|0.000|0|1|0|1|1|0\naf|0.000|0=1\nzz|1|2\ns|0.000|0|0|0|0\n";
            Attempt back = AttemptFormat.Parse(run.Split('\n'));
            Assert.Null(back.Anim);
            Assert.Single(back.Samples);
        }

        [Fact]
        public void TheFirstBuildsSampleLinesAreIgnored()
        {
            // c8c0edf6 (never released) wrote 10 Hz `a|` samples and `af|` without rates.
            string run = "anim|1|vSpeed\na|0.000|90.0|111:0.000\naf|0.000|0=0.500\ns|0.000|0|0|0|0\n";
            Attempt back = AttemptFormat.Parse(run.Split('\n'));
            Assert.NotNull(back.Anim);
            Assert.False(back.Anim.HasPose);
            Assert.Single(back.Anim.FloatKeys);
            Assert.Equal(0.5f, back.Anim.FloatKeys[0].Value, 3);
        }

        [Fact]
        public void RunnerLineGoesBeforeTheAnimLines()
        {
            string run = "anchor|x\nduration|1.000\nanim|1|f\nak|0.000|0|5|0.000|1.0000|1.000|0.000\ns|0.000|0|0|0|0\n";
            string named = AttemptFormat.WithRunner(run, "abc", "Name");
            Assert.True(named.IndexOf("runner|abc|Name") < named.IndexOf("anim|1"));
        }

        [Fact]
        public void RecorderReadsLayersEveryFrameAndFloatsWithThePosition()
        {
            var r = new RunRecorder();
            int frames = 0, floats = 0;
            r.AnimSource = (s, withFloats) =>
            {
                frames++;
                if (withFloats) floats++;
                s.Size(frames == 50 ? 3 : 2, Names);
                s.Hash[0] = frames < 60 ? 1 : 2;
                s.Weight[0] = 1f;
                s.Held = new int[0];
                return true;
            };
            r.Arm(Vector3.zero, "s-test");
            r.ForceStart(Vector3.zero);
            for (int i = 0; i < 120; i++) r.Tick(new Vector3(i, 0f, 0f), 1f, 1f / 60f);   // 2 s at 60 fps
            Attempt a = r.Finish();
            Assert.Equal(120, frames);
            Assert.Equal(a.Samples.Count - 1, floats);   // with each position sample (~30 Hz) after the first
            Assert.InRange(floats, 40, 62);
            Assert.NotNull(a.Anim);
            Assert.True(a.Anim.HasPose);
            // layer 0: its first state, then state 2 from frame 60; the
            // 3-layer reading (frame 50) was skipped
            Assert.Equal(2, KeysOf(a.Anim, 0));
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
