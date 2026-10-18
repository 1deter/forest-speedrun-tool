using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class SpeedCheckTests
    {
        [Theory]
        [InlineData(0f, false)]
        [InlineData(1f, false)]
        [InlineData(0.1f, true)]
        [InlineData(2f, true)]
        [InlineData(0.5f, true)]
        [InlineData(1.0001f, true)]
        public void IrregularIsAnythingButZeroAndOne(float scale, bool expected)
        {
            Assert.Equal(expected, SpeedCheck.Irregular(scale));
        }

        [Fact]
        public void FlagTextShowsTheValue()
        {
            Assert.Equal("the game's speed was changed (time scale 0.5)", SpeedCheck.FlagText(0.5f));
        }

        private static string F(TimeScaleWatch w, float scale, float dt, out string ended, bool excused = false, bool zeroOk = false)
        {
            return w.Frame(scale, excused, zeroOk, dt, out ended);
        }

        [Fact]
        public void EpisodeFlagsOnceThenSummarisesWithDuration()
        {
            var w = new TimeScaleWatch();
            string end;
            Assert.Equal("the game's speed was changed (time scale 2)", F(w, 2f, 0.1f, out end));
            Assert.Null(end);
            Assert.Null(F(w, 2f, 0.1f, out end));
            Assert.Null(F(w, 2f, 0.2f, out end));
            Assert.Null(F(w, 1f, 0.1f, out end));
            Assert.Equal("time scale 2 lasted 0.4 s (back to normal)", end);
            Assert.Null(F(w, 1f, 0.1f, out end));
            Assert.Null(end);
        }

        [Fact]
        public void ExcusedFramesStartNoEpisode()
        {
            var w = new TimeScaleWatch();
            string end;
            Assert.Null(F(w, 0.1f, 0.1f, out end, excused: true));
            Assert.Null(F(w, 6f, 0.1f, out end, excused: true));
            Assert.Null(F(w, 1f, 0.1f, out end));
            Assert.Null(end);
            Assert.Null(w.End());
        }

        [Fact]
        public void ExcusedFramesLeaveARunningEpisodeAlone()
        {
            var w = new TimeScaleWatch();
            string end;
            F(w, 2f, 0.1f, out end);
            Assert.Null(F(w, 1f, 0.5f, out end, excused: true));
            Assert.Null(end);
            Assert.Null(F(w, 2f, 0.1f, out end));
            Assert.Equal("time scale 2 lasted 0.2 s (back to normal)", w.End());
        }

        [Fact]
        public void TwoBurstsOfTheSameValueEachFlagAndEachEndWithADuration()
        {
            var w = new TimeScaleWatch();
            string end;
            Assert.NotNull(F(w, 2f, 0.1f, out end));
            F(w, 2f, 0.1f, out end);
            F(w, 1f, 0.1f, out end);
            Assert.Equal("time scale 2 lasted 0.2 s (back to normal)", end);
            Assert.NotNull(F(w, 2f, 0.3f, out end));
            F(w, 1f, 0.1f, out end);
            Assert.Equal("time scale 2 lasted 0.3 s (back to normal)", end);
        }

        [Fact]
        public void PeakIsTheValueFurthestFromOne()
        {
            var w = new TimeScaleWatch();
            string end;
            F(w, 1.5f, 0.1f, out end);
            F(w, 3f, 0.1f, out end);
            F(w, 2f, 0.1f, out end);
            Assert.Equal("time scale 3 lasted 0.3 s (back to normal)", w.End());
        }

        [Fact]
        public void EndClosesARunningEpisodeAndResetForgetsIt()
        {
            var w = new TimeScaleWatch();
            string end;
            F(w, 2f, 0.26f, out end);
            Assert.Equal("time scale 2 lasted 0.3 s (back to normal)", w.End());
            Assert.Null(w.End());
            F(w, 2f, 0.1f, out end);
            w.Reset();
            Assert.Null(w.End());
        }

        [Fact]
        public void ZeroInAMenuIsSilentHoweverLong()
        {
            var w = new TimeScaleWatch();
            string end;
            for (int i = 0; i < 100; i++) Assert.Null(F(w, 0f, 0.1f, out end, zeroOk: true));
            Assert.Null(w.End());
        }

        [Fact]
        public void ZeroInPlainPlayPastTheGraceIsFlagged()
        {
            var w = new TimeScaleWatch();
            string end;
            Assert.Null(F(w, 0f, 0.3f, out end));
            Assert.Equal("the game's speed was changed (time scale 0)", F(w, 0f, 0.3f, out end));
            Assert.Null(F(w, 0f, 0.4f, out end));
            F(w, 1f, 0.1f, out end);
            Assert.Equal("time scale 0 lasted 1.0 s (back to normal)", end);
        }

        [Fact]
        public void ZeroBrieflyUnderTheGraceIsSilentAndTheGraceRestarts()
        {
            var w = new TimeScaleWatch();
            string end;
            Assert.Null(F(w, 0f, 0.3f, out end));
            Assert.Null(F(w, 1f, 0.1f, out end));
            Assert.Null(F(w, 0f, 0.3f, out end));   // 0.3 again, not 0.6 in total
            Assert.Null(end);
            Assert.Null(w.End());
        }

        [Fact]
        public void ZeroWhenAMenuOpensMidwayEndsTheEpisode()
        {
            var w = new TimeScaleWatch();
            string end;
            F(w, 0f, 0.6f, out end);
            F(w, 0f, 0.1f, out end, zeroOk: true);
            Assert.Equal("time scale 0 lasted 0.6 s (back to normal)", end);
        }
    }
}
