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

        [Theory]
        [InlineData(13.5f, 3.75f, false)]
        [InlineData(1.2f, 3f, false)]
        [InlineData(0f, 0f, false)]
        [InlineData(60f, 3.75f, true)]
        [InlineData(13.5f, 20f, true)]
        public void RunSpeedFlagsOnlyAboveTheGames(float run, float swim, bool flagged)
        {
            Assert.Equal(flagged, SpeedCheck.RunSpeedFlag(run, swim) != null);
        }

        [Fact]
        public void RunSpeedFlagText()
        {
            Assert.Equal("the player's run speed was changed (run 60, swim 20; the game's 13.5 / 3.75)", SpeedCheck.RunSpeedFlag(60f, 20f));
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

        // ---- ClockWatch (T-0414 F1) ----

        [Fact]
        public void ClockVanillaFramesAreSilent()
        {
            var w = new ClockWatch();
            string end;
            Assert.Null(w.Frame(0.016f, 0.016f, 1f, false, out end));   // play
            Assert.Null(w.Frame(0f, 0.016f, 0f, false, out end));       // menu
            Assert.Null(w.Frame(0.1f, 1.5f, 0.1f, false, out end));     // a load frame
            Assert.Null(w.Frame(0.333f, 4f, 1f, false, out end));       // a long frame, deltaTime capped
            Assert.Null(w.Frame(0.01005f, 0.01f, 1f, false, out end));  // ratio 1.005: noise
            Assert.Null(w.Frame(0.01f, 0f, 1f, false, out end));        // no unscaled time
            Assert.Null(end);
            Assert.Null(w.End());
        }

        [Fact]
        public void ClockAheadFlagsOncePerEpisodeWithAnEndLine()
        {
            var w = new ClockWatch();
            string end;
            Assert.Equal("the game's clock ran faster than real time (x2, time scale read 1)", w.Frame(0.02f, 0.01f, 1f, false, out end));
            Assert.Null(w.Frame(0.02f, 0.01f, 1f, false, out end));
            Assert.Null(w.Frame(0.03f, 0.01f, 1f, false, out end));
            Assert.Null(w.Frame(0.01f, 0.01f, 1f, false, out end));
            Assert.Equal("game clock x3 lasted 0.0 s (back to real time)", end);
            Assert.NotNull(w.Frame(0.0333f, 0.0063f, 1f, false, out end));   // capture rate 30 at ~160 fps
            Assert.Equal("game clock x5.29 lasted 0.0 s (back to real time)", w.End());
        }

        [Fact]
        public void ClockEpisodeSumsTheUnscaledSeconds()
        {
            var w = new ClockWatch();
            string end;
            for (int i = 0; i < 50; i++) w.Frame(0.2f, 0.1f, 1f, false, out end);
            Assert.Equal("game clock x2 lasted 5.0 s (back to real time)", w.End());
        }

        [Fact]
        public void ClockExcusedFramesStartNothingAndLeaveAnEpisodeAlone()
        {
            var w = new ClockWatch();
            string end;
            Assert.Null(w.Frame(0.02f, 0.01f, 1f, true, out end));
            Assert.Null(w.End());
            Assert.NotNull(w.Frame(0.02f, 0.01f, 1f, false, out end));
            Assert.Null(w.Frame(0.01f, 0.01f, 1f, true, out end));
            Assert.Null(end);
            Assert.Null(w.Frame(0.02f, 0.01f, 1f, false, out end));
            w.Reset();
            Assert.Null(w.End());
        }

        [Fact]
        public void CaptureRateFlag()
        {
            Assert.Null(SpeedCheck.CaptureRateFlag(0));
            Assert.Equal("the game's clock was locked to 30 frames a second (capture frame rate)", SpeedCheck.CaptureRateFlag(30));
        }

        // ---- MovementFlag (T-0414 F3, D1) ----

        private static string Move(float walk = 6.5f, float strafe = 6f, float crouch = 4.5f, float jump = 8f, float gravity = 10f,
                                   float px = 0f, float py = -16f, float pz = 0f, float fixedDt = 1f / 60f, bool mp = false)
        {
            return SpeedCheck.MovementFlag(walk, strafe, crouch, jump, gravity, px, py, pz, fixedDt, mp);
        }

        [Fact]
        public void MovementVanillaIsSilent()
        {
            Assert.Null(Move());
            Assert.Null(Move(fixedDt: 0.0166667f));
        }

        [Theory]
        [InlineData(1.2f)]
        [InlineData(0f)]
        [InlineData(1f)]
        public void MovementLoweredHitReactionValuesAreSilent(float v)
        {
            Assert.Null(Move(walk: v, strafe: v));
        }

        [Fact]
        public void MovementNamesEveryValueOff()
        {
            Assert.Equal("the game's movement values were changed: walk speed 20 (the game's 6.5), jump height 40 (the game's 8), " +
                         "player gravity 2 (the game's 10)", Move(walk: 20f, jump: 40f, gravity: 2f));
            Assert.Contains("strafe speed 20", Move(strafe: 20f));
            Assert.Contains("crouch speed 20", Move(crouch: 20f));
            Assert.Contains("crouch speed 1", Move(crouch: 1f));
            Assert.Equal("the game's movement values were changed: physics gravity (0, -4, 0) (the game's (0, -16, 0))", Move(py: -4f));
            Assert.Contains("physics gravity (1, -16, 0)", Move(px: 1f));
        }

        [Fact]
        public void MovementFlagsAnyPhysicsRateButTheDefault()
        {
            Assert.Equal("the game's movement values were changed: physics rate 120 Hz (the game's 60 Hz)", Move(fixedDt: 0.00833f));
            Assert.Equal("the game's movement values were changed: physics rate 30 Hz (the game's 60 Hz; 30 is its hidden Low quality physics setting)",
                         Move(fixedDt: 1f / 30f));   // D1
            Assert.Null(Move(fixedDt: 1f / 30f, mp: true));
            Assert.Null(Move(fixedDt: 0.00833f, mp: true));
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
