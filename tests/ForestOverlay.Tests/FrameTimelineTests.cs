using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class FrameTimelineTests
    {
        // 1000 ticks a second: one tick = 1 ms.
        private const long Freq = 1000;

        // One frame ending at `t0 + 20`: waiting 2, to Update 1, Update 5,
        // to render 1, main camera 6 (+2 after), HUD 1 (+2 after, OnGUI).
        private static void Frame(FrameTimeline t, long t0, int main, int hud, bool physics)
        {
            t.FrameStart(t0 + 2, t0 + 3);
            if (physics) t.FixedStep(t0 + 3);
            t.Update(t0 + 3);
            t.LateUpdate(t0 + 8);
            t.PreCull(main, t0 + 9);
            t.PostRender(main, t0 + 15);
            t.PreCull(hud, t0 + 17);
            t.PostRender(hud, t0 + 18);
            t.EndOfFrame(t0 + 20);
        }

        [Fact]
        public void SplitsAFrameIntoPhasesAndCameras()
        {
            FrameTimeline t = new FrameTimeline();
            int main = t.AddCamera(10, "MainCamNew");
            int hud = t.AddCamera(20, "Camera_HUD");
            t.EndOfFrame(0);
            Frame(t, 0, main, hud, false);
            Frame(t, 20, main, hud, true);

            Assert.Equal(2, t.Frames);
            var lines = t.Report(1f, Freq, 5);
            Assert.Equal(2, lines.Count);
            Assert.Equal("Frame (1 s, 2 frames): 20.00 ms/frame = waiting 2.00 (GPU / render thread / present)" +
                         " + start to Update 1.00 (1.00 without a physics step, 1.00 with, 50% of frames)" +
                         " + Update to LateUpdate 5.00 + to rendering 1.00 + cameras and OnGUI 11.00", lines[0]);
            Assert.Equal("cameras: MainCamNew 6.00 +2.00 after, Camera_HUD 1.00 +2.00 after", lines[1]);
        }

        [Fact]
        public void ACameraRenderedInsideAnotherIsChargedToItself()
        {
            FrameTimeline t = new FrameTimeline();
            int main = t.AddCamera(1, "Main");
            int refl = t.AddCamera(2, "Reflection");
            t.EndOfFrame(0);
            t.FrameStart(0, 0);
            t.Update(0);
            t.LateUpdate(0);
            t.PreCull(main, 0);
            t.PreCull(refl, 3);       // Camera.Render from inside the main camera
            t.PostRender(refl, 7);
            t.PostRender(main, 10);
            t.EndOfFrame(10);

            var lines = t.Report(1f, Freq, 5);
            Assert.Equal("cameras: Main 6.00, Reflection 4.00", lines[1]);
        }

        [Fact]
        public void AMissingPostRenderDoesNotWedgeTheStack()
        {
            FrameTimeline t = new FrameTimeline();
            int a = t.AddCamera(1, "A");
            int b = t.AddCamera(2, "B");
            t.EndOfFrame(0);
            t.FrameStart(0, 0);
            t.Update(0);
            t.PreCull(a, 0);
            t.PreCull(b, 2);          // A's post-render never came
            t.PostRender(b, 5);
            t.EndOfFrame(6);
            // Next frame starts clean.
            t.FrameStart(6, 6);
            t.Update(6);
            t.PreCull(b, 6);
            t.PostRender(b, 8);
            t.EndOfFrame(8);
            Assert.Equal(2, t.Frames);
            Assert.Equal(8.0 / 2, t.FrameMs(Freq), 3);
        }

        [Fact]
        public void FrameStartIsClampedToTheLastEndAndNow()
        {
            FrameTimeline t = new FrameTimeline();
            t.EndOfFrame(100);
            t.FrameStart(90, 104);    // before the last end of frame (float rounding)
            t.Update(104);
            t.EndOfFrame(110);
            Assert.Equal(0.0, t.WaitMs(Freq), 3);

            t.FrameStart(120, 115);   // after "now"
            t.Update(115);
            t.EndOfFrame(116);
            Assert.Equal(5.0 / 2, t.WaitMs(Freq), 3);
        }

        [Fact]
        public void ResetClearsCountersAndCameras()
        {
            FrameTimeline t = new FrameTimeline();
            int main = t.AddCamera(10, "MainCamNew");
            t.EndOfFrame(0);
            Frame(t, 0, main, -1, false);
            t.Reset();
            Assert.Equal(0, t.Frames);
            Assert.Equal(-1, t.CameraSlot(10));
            Assert.Empty(t.Report(1f, Freq, 5));
        }

        [Fact]
        public void AFullCameraTableGoesToOther()
        {
            FrameTimeline t = new FrameTimeline();
            for (int i = 0; i < FrameTimeline.MaxCameras; i++) Assert.Equal(i, t.AddCamera(i + 1, "c" + i));
            Assert.Equal(-1, t.AddCamera(999, "extra"));
            t.EndOfFrame(0);
            t.FrameStart(0, 0);
            t.Update(0);
            t.PreCull(-1, 0);
            t.PostRender(-1, 3);
            t.EndOfFrame(3);
            Assert.Equal("cameras: other cameras 3.00", t.Report(1f, Freq, 5)[1]);
        }
    }
}
