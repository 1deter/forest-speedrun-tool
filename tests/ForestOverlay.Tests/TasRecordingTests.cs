using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // TAS recordings: change-only storage, the text round trip, the
    // per-frame playback (down / up / axis-down derived), and the drift.
    // ------------------------------------------------------------------
    public class TasRecordingTests
    {
        private static TasRecording Sample()
        {
            TasRecording r = new TasRecording();
            r.SegmentId = "s-0123456789ab";
            r.SegmentName = "Cave 5 | entry";
            r.RecordedUtc = "2026-10-04T12:00:00Z";
            int jump = r.ButtonChannel("Jump");
            int run = r.ButtonChannel("Run");
            int mx = r.AxisChannel("Mouse X");
            for (int f = 0; f < 10; f++)
            {
                r.SetFps(f, f < 5 ? 144 : 60);
                r.SetButton(f, jump, f == 2 || f == 3);
                r.SetButton(f, run, f >= 1);
                r.SetAxis(f, mx, f == 4 ? 0.1f + 0.2f : 0f);
                r.EndFrame(f);
            }
            return r;
        }

        [Fact]
        public void OnlyChangesAreStored()
        {
            TasRecording r = Sample();
            // fps x2, jump down/up, run down, mouse on/off
            Assert.Equal(7, r.Changes.Count);
            Assert.Equal(10, r.Frames);
        }

        [Fact]
        public void TextRoundTripsExactly()
        {
            TasRecording r = Sample();
            TasSample s;
            s.Frame = 3; s.T = 0.0333333f; s.X = 1.5f; s.Y = -2.25f; s.Z = 1000.125f; s.Yaw = 359.9f; s.Pitch = -12.5f;
            r.AddSample(s);
            string text = r.Write();
            string err;
            TasRecording back = TasRecording.Parse(text, out err);
            Assert.Null(err);
            Assert.Equal(text, back.Write());
            Assert.Equal("Cave 5 / entry", back.SegmentName);
            Assert.Equal(new[] { "Jump", "Run" }, back.Buttons.ToArray());
            Assert.Equal(0.1f + 0.2f, new TasPlayback(back).Axis(back.FindAxis("mouse x"), 4));
            Assert.Equal(1000.125f, back.Samples[0].Z);
        }

        [Fact]
        public void PlaybackDerivesDownAndUp()
        {
            TasPlayback p = new TasPlayback(Sample());
            int jump = p.FindButton("jump");
            Assert.False(p.Held(jump, 1));
            Assert.True(p.Down(jump, 2));
            Assert.True(p.Held(jump, 3));
            Assert.False(p.Down(jump, 3));
            Assert.True(p.Up(jump, 4));
            Assert.False(p.Up(jump, 5));
            int run = p.FindButton("Run");
            Assert.True(p.Held(run, 9));
            Assert.False(p.Held(run, 10));      // past the end: nothing
            Assert.Equal(-1, p.FindButton("Crouch"));
            Assert.False(p.Held(-1, 3));
            Assert.Equal(144, p.Fps(0));
            Assert.Equal(60, p.Fps(9));
        }

        [Fact]
        public void AxisDownIsTheGamesRule()
        {
            TasPlayback p = new TasPlayback(Sample());
            int mx = p.FindAxis("Mouse X");
            Assert.Equal(0.1f + 0.2f, p.AxisDown(mx, 4));   // previous 0
            Assert.Equal(0f, p.AxisDown(mx, 5));
        }

        [Fact]
        public void ParseRefusesOtherFiles()
        {
            string err;
            Assert.Null(TasRecording.Parse("hello", out err));
            Assert.NotNull(err);
            Assert.Null(TasRecording.Parse("version|1\nbuttons|Jump\nf|0|+5\n", out err));
            Assert.Contains("bad change", err);
            Assert.Null(TasRecording.Parse("version|99\n", out err));
            Assert.Contains("newer", err);
        }

        [Fact]
        public void SamplesComeAt30Hz()
        {
            TasRecording r = new TasRecording();
            int taken = 0;
            for (int f = 0; f < 144; f++)           // 1 s at 144 fps
            {
                float t = f / 144f;
                if (!r.SampleDue(t)) continue;
                TasSample s = default(TasSample);
                s.Frame = f; s.T = t;
                r.AddSample(s);
                taken++;
            }
            Assert.InRange(taken, 29, 31);
        }

        [Fact]
        public void DriftFindsTheWorstFrame()
        {
            List<TasSample> rec = new List<TasSample>();
            List<TasSample> rep = new List<TasSample>();
            for (int i = 0; i < 5; i++)
            {
                TasSample a = default(TasSample);
                a.Frame = i * 4; a.T = i / 30f; a.X = i; a.Yaw = 350f;
                rec.Add(a);
                TasSample b = a;
                if (i == 3) b.X += 2f;
                if (i == 4) { b.X += 0.5f; b.Yaw = 5f; }
                if (i != 1) rep.Add(b);              // a frame the replay missed
            }
            TasDrift d = TasDrift.Compare(rec, rep);
            Assert.Equal(4, d.Compared);
            Assert.Equal(2f, d.Max, 3);
            Assert.Equal(12, d.MaxAtFrame);
            Assert.Equal(0.5f, d.End, 3);
            Assert.Equal(15f, d.MaxLook, 3);
            Assert.Contains("max drift 2.00 m", d.Describe());
        }
    }
}
