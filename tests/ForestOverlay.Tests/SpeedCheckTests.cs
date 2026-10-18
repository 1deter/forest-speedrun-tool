using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class SpeedCheckTests
    {
        [Fact]
        public void ZeroAndOneAreSilent()
        {
            Assert.Null(SpeedCheck.TimeScaleFlag(0f));
            Assert.Null(SpeedCheck.TimeScaleFlag(1f));
        }

        [Theory]
        [InlineData(0.1f, "0.1")]
        [InlineData(2f, "2")]
        [InlineData(0.5f, "0.5")]
        [InlineData(1.0001f, "1")]
        public void OtherValuesAreFlagged(float scale, string shown)
        {
            Assert.Equal("the game's speed was changed (time scale " + shown + ")", SpeedCheck.TimeScaleFlag(scale));
        }

        private static string F(TimeScaleWatch w, float scale, bool excused, float dt, out string ended)
        {
            return w.Frame(scale, excused, dt, out ended);
        }

        [Fact]
        public void EpisodeFlagsOnceThenSummarisesWithDuration()
        {
            var w = new TimeScaleWatch();
            string end;
            Assert.Equal("the game's speed was changed (time scale 2)", F(w, 2f, false, 0.1f, out end));
            Assert.Null(end);
            Assert.Null(F(w, 2f, false, 0.1f, out end));
            Assert.Null(F(w, 2f, false, 0.2f, out end));
            Assert.Null(F(w, 1f, false, 0.1f, out end));
            Assert.Equal("time scale 2 lasted 0.4 s (back to normal)", end);
            Assert.Null(F(w, 1f, false, 0.1f, out end));
            Assert.Null(end);
        }

        [Fact]
        public void ExcusedFramesStartNoEpisode()
        {
            var w = new TimeScaleWatch();
            string end;
            Assert.Null(F(w, 0.1f, true, 0.1f, out end));
            Assert.Null(F(w, 6f, true, 0.1f, out end));
            Assert.Null(F(w, 1f, false, 0.1f, out end));
            Assert.Null(end);
            Assert.Null(w.End());
        }

        [Fact]
        public void ExcusedFramesLeaveARunningEpisodeAlone()
        {
            var w = new TimeScaleWatch();
            string end;
            F(w, 2f, false, 0.1f, out end);
            Assert.Null(F(w, 1f, true, 0.5f, out end));
            Assert.Null(end);
            Assert.Null(F(w, 2f, false, 0.1f, out end));
            Assert.Equal("time scale 2 lasted 0.2 s (back to normal)", w.End());
        }

        [Fact]
        public void TwoEpisodesDifferentValuesFlagTwiceSameValueOnce()
        {
            var w = new TimeScaleWatch();
            string end;
            Assert.NotNull(F(w, 2f, false, 0.1f, out end));
            F(w, 1f, false, 0.1f, out end);
            Assert.NotNull(end);
            Assert.NotNull(F(w, 0.5f, false, 0.1f, out end));
            F(w, 1f, false, 0.1f, out end);
            Assert.NotNull(end);
            Assert.Null(F(w, 0.5f, false, 0.1f, out end));
            F(w, 1f, false, 0.1f, out end);
            Assert.Null(end);   // not flagged, so no summary either
        }

        [Fact]
        public void PeakIsTheValueFurthestFromOne()
        {
            var w = new TimeScaleWatch();
            string end;
            F(w, 1.5f, false, 0.1f, out end);
            F(w, 3f, false, 0.1f, out end);
            F(w, 2f, false, 0.1f, out end);
            Assert.Equal("time scale 3 lasted 0.3 s (back to normal)", w.End());
        }

        [Fact]
        public void EndClosesARunningEpisodeAndResetForgetsEverything()
        {
            var w = new TimeScaleWatch();
            string end;
            F(w, 2f, false, 0.26f, out end);
            Assert.Equal("time scale 2 lasted 0.3 s (back to normal)", w.End());
            Assert.Null(w.End());
            w.Reset();
            Assert.NotNull(F(w, 2f, false, 0.1f, out end));   // same value flags again after a reset
        }
    }
}
