using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class RunLineRulesTests
    {
        [Fact]
        public void RunLinesOffNeverShows()
        {
            for (int m = 0; m < 8; m++)
                Assert.False(RunLineRules.ShowOwn(false, (m & 1) != 0, (m & 2) != 0, (m & 4) != 0));
        }

        [Fact]
        public void LiveRunHidesOwnLineByDefault()
        {
            Assert.False(RunLineRules.ShowOwn(true, false, false, true));
        }

        [Fact]
        public void SettingShowsItLive()
        {
            Assert.True(RunLineRules.ShowOwn(true, true, false, true));
        }

        [Fact]
        public void ReplayCameraShowsItWhileRunning()
        {
            Assert.True(RunLineRules.ShowOwn(true, false, true, true));
            Assert.True(RunLineRules.ShowOwn(true, false, true, false));
        }

        [Fact]
        public void NotRunningShows()
        {
            Assert.True(RunLineRules.ShowOwn(true, false, false, false));
            Assert.True(RunLineRules.ShowOwn(true, true, false, false));
        }
    }
}
