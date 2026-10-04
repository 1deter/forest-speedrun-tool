using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class RunTimingTests
    {
        [Fact]
        public void RunStartArmsWhateverF9Says()
        {
            Assert.Equal(ArmSource.RunMode, RunTiming.Source(false, true));
            Assert.Equal(ArmSource.RunMode, RunTiming.Source(true, true));
        }

        [Fact]
        public void OtherPlacementsFollowF9()
        {
            Assert.Equal(ArmSource.Practice, RunTiming.Source(true, false));
            Assert.Equal(ArmSource.None, RunTiming.Source(false, false));
        }

        [Fact]
        public void RunModeRunIsTimedOnlyWhileRunModeIsOn()
        {
            Assert.True(RunTiming.TimingOn(false, ArmSource.RunMode, true));
            Assert.False(RunTiming.TimingOn(false, ArmSource.RunMode, false));
            Assert.False(RunTiming.TimingOn(false, ArmSource.None, true));
            Assert.False(RunTiming.TimingOn(false, ArmSource.Practice, true));   // F9 since turned off
            Assert.True(RunTiming.TimingOn(true, ArmSource.None, false));
        }

        [Fact]
        public void F9LeavesARunModeRunAlone()
        {
            Assert.False(RunTiming.ToggleTouchesRun(ArmSource.RunMode, true));
            Assert.True(RunTiming.ToggleTouchesRun(ArmSource.RunMode, false));
            Assert.True(RunTiming.ToggleTouchesRun(ArmSource.Practice, true));
            Assert.True(RunTiming.ToggleTouchesRun(ArmSource.None, false));
        }

        [Fact]
        public void RunModeEndGoesBackToTheRunnersF9()
        {
            Assert.True(RunTiming.DropOnRunModeEnd(false, ArmSource.RunMode));
            Assert.False(RunTiming.DropOnRunModeEnd(true, ArmSource.RunMode));
            Assert.False(RunTiming.DropOnRunModeEnd(false, ArmSource.Practice));
            Assert.False(RunTiming.DropOnRunModeEnd(false, ArmSource.None));
        }
    }
}
