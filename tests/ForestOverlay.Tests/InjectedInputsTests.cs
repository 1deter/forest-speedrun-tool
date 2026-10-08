using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // The bridge's pressed / held game actions: a command on frame F
    // starts on F+1, and every reader in a frame sees the same thing.
    // ------------------------------------------------------------------
    public class InjectedInputsTests
    {
        [Fact]
        public void PressIsDownHeldThenUp()
        {
            InjectedInputs s = new InjectedInputs();
            s.Press("Jump", 10, 2);
            Assert.False(s.Held("Jump", 10, 0f));
            Assert.True(s.Down("Jump", 11, 0f));
            Assert.True(s.Held("Jump", 11, 0f));
            Assert.True(s.Held("Jump", 12, 0f));
            Assert.False(s.Down("Jump", 12, 0f));
            Assert.False(s.Held("Jump", 13, 0f));
            Assert.True(s.Up("Jump", 13, 0f));
            Assert.True(s.Ended("Jump", 13, 0f));
            Assert.False(s.Ended("Jump", 12, 0f));
        }

        [Fact]
        public void APressAgainReplacesTheEntryOnce()
        {
            InjectedInputs s = new InjectedInputs();
            s.Hold("Run", 0, 0f, 0f);
            s.Press("run", 5, 1);                    // replaces the hold, any case
            List<string> o = new List<string>();
            s.Describe(5, 0f, o);
            Assert.Single(o);
            Assert.Equal(1, s.ReleaseAll(5));        // up on 6
            s.Settle(6, 0f);                         // kept through its up frame
            Assert.True(s.Any);
            s.Settle(7, 0f);
            Assert.False(s.Any);
            s.Describe(7, 0f, o);
            Assert.Equal("nothing pressed or held", o[1]);
        }

        [Fact]
        public void NamesIgnoreCase()
        {
            InjectedInputs s = new InjectedInputs();
            s.Press("jump", 0, 1);
            Assert.True(s.Down("Jump", 1, 0f));
        }

        [Fact]
        public void TimedHoldEndsOnTheFrameAfterTheTimeIsUp()
        {
            InjectedInputs s = new InjectedInputs();
            s.Hold("Run", 0, 1f, 5f);
            Assert.True(s.Held("Run", 50, 5.9f));
            Assert.True(s.Held("Run", 60, 6.0f));    // first read past the time: still held this frame
            Assert.False(s.Held("Run", 61, 6.02f));
            Assert.True(s.Up("Run", 61, 6.02f));
        }

        [Fact]
        public void HoldUntilReleased()
        {
            InjectedInputs s = new InjectedInputs();
            s.Hold("Crouch", 0, 0f, 0f);
            Assert.True(s.Held("Crouch", 500, 99f));
            Assert.True(s.Release("Crouch", 500));
            Assert.True(s.Held("Crouch", 500, 99f));
            Assert.True(s.Up("Crouch", 501, 99f));
            Assert.False(s.Release("Nothing", 500));
        }

        [Fact]
        public void AxisValueOnlyWhileActiveAndNotAButton()
        {
            InjectedInputs s = new InjectedInputs();
            s.SetAxis("Vertical", 0.5f, 0, 0f, 0f);
            float v;
            Assert.False(s.TryAxis("Vertical", 0, 0f, out v));
            Assert.True(s.TryAxis("Vertical", 1, 0f, out v));
            Assert.Equal(0.5f, v);
            Assert.True(s.AxisDown("Vertical", 1, 0f));
            Assert.False(s.Held("Vertical", 1, 0f));
            s.ReleaseAll(5);
            Assert.False(s.TryAxis("Vertical", 6, 0f, out v));
        }

        [Fact]
        public void SettleDropsFinishedEntriesAfterTheirUpFrame()
        {
            InjectedInputs s = new InjectedInputs();
            s.Press("Jump", 0, 1);
            s.Settle(2, 0f);
            Assert.True(s.Any);
            Assert.True(s.Up("Jump", 2, 0f));
            s.Settle(3, 0f);
            Assert.False(s.Any);
        }

        [Fact]
        public void DescribeSaysWhatIsHeld()
        {
            InjectedInputs s = new InjectedInputs();
            List<string> o = new List<string>();
            s.Describe(0, 0f, o);
            Assert.Equal("nothing pressed or held", o[0]);
            s.Hold("Run", 0, 0f, 0f);
            o.Clear();
            s.Describe(3, 0f, o);
            Assert.Equal("Run: held until released", o[0]);
        }
    }
}
