using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class HudTrendTests
    {
        // The HUD's tick: 10 samples a second.
        private const float Step = 0.1f;

        private static TrendState Feed(TrendTracker t, float[] values, ref float now)
        {
            TrendState s = t.State;
            for (int i = 0; i < values.Length; i++)
            {
                s = t.Update(values[i], now);
                now += Step;
            }
            return s;
        }

        [Fact]
        public void FirstSampleIsSteady()
        {
            TrendTracker t = new TrendTracker();
            Assert.Equal(TrendState.Steady, t.Update(5f, 0f));
        }

        [Fact]
        public void GainShowsOnTheFirstSamplePastTheDeadBand()
        {
            TrendTracker t = new TrendTracker();
            t.Update(5f, 0f);
            Assert.Equal(TrendState.Gaining, t.Update(5.5f, 0.1f));   // 5 u/s per second
        }

        [Fact]
        public void LossShowsAtOnceFromSteady()
        {
            TrendTracker t = new TrendTracker();
            t.Update(8f, 0f);
            Assert.Equal(TrendState.Losing, t.Update(7f, 0.1f));
        }

        [Fact]
        public void NoiseInsideTheDeadBandStaysSteady()
        {
            TrendTracker t = new TrendTracker();
            float now = 0f;
            // +-0.02 per 0.1 s = 0.2 u/s per second, under 0.5.
            TrendState s = Feed(t, new[] { 6f, 6.02f, 6f, 6.02f, 6f, 5.98f, 6f, 6.01f }, ref now);
            Assert.Equal(TrendState.Steady, s);
        }

        [Fact]
        public void OneSteadySampleDoesNotDropAGain()
        {
            TrendTracker t = new TrendTracker();
            float now = 0f;
            Feed(t, new[] { 5f, 5.5f, 6f }, ref now);
            Assert.Equal(TrendState.Gaining, t.State);
            Assert.Equal(TrendState.Gaining, t.Update(6f, now));    // 0.1 s since the last gain
            now += Step;
            Assert.Equal(TrendState.Gaining, t.Update(6f, now));    // 0.2 s
        }

        [Fact]
        public void SteadyReturnsAfterTheHold()
        {
            TrendTracker t = new TrendTracker();
            float now = 0f;
            Feed(t, new[] { 5f, 5.5f, 6f }, ref now);               // last gain at 0.2
            TrendState s = Feed(t, new[] { 6f, 6f, 6f, 6f }, ref now);   // 0.3 .. 0.6
            Assert.Equal(TrendState.Steady, s);
        }

        [Fact]
        public void AGainJustShownIsNotFlippedByOneLosingSample()
        {
            TrendTracker t = new TrendTracker();
            t.Update(5f, 0f);
            Assert.Equal(TrendState.Gaining, t.Update(6f, 0.1f));
            Assert.Equal(TrendState.Gaining, t.Update(5f, 0.2f));   // gain shown 0.1 s ago
        }

        [Fact]
        public void AHeldGainTurnsToLossAtOnce()
        {
            TrendTracker t = new TrendTracker();
            float now = 0f;
            Feed(t, new[] { 5f, 5.5f, 6f, 6.5f, 7f, 7.5f }, ref now);
            Assert.Equal(TrendState.Gaining, t.State);
            Assert.Equal(TrendState.Losing, t.Update(6f, now));
        }

        [Fact]
        public void AlternatingSamplesDoNotFlickerEveryTick()
        {
            TrendTracker t = new TrendTracker();
            float now = 0f;
            t.Update(5f, now);
            int changes = 0;
            TrendState last = t.State;
            for (int i = 0; i < 30; i++)
            {
                now += Step;
                TrendState s = t.Update(i % 2 == 0 ? 6f : 5f, now);
                if (s != last) changes++;
                last = s;
            }
            // At most one turn per Hold (0.3 s = every third sample), not one a sample.
            Assert.True(changes <= 11, "changes: " + changes);
        }

        [Fact]
        public void AGapStartsOverSteady()
        {
            TrendTracker t = new TrendTracker();
            t.Update(5f, 0f);
            Assert.Equal(TrendState.Gaining, t.Update(6f, 0.1f));
            // The value hidden for 2 s, then back much higher: no rate across the gap.
            Assert.Equal(TrendState.Steady, t.Update(20f, 2.1f));
            Assert.Equal(TrendState.Steady, t.Update(20f, 2.2f));
        }

        [Fact]
        public void TimeGoingBackStartsOver()
        {
            TrendTracker t = new TrendTracker();
            t.Update(5f, 10f);
            t.Update(6f, 10.1f);
            Assert.Equal(TrendState.Steady, t.Update(9f, 1f));
        }

        [Fact]
        public void TheSameMomentTwiceChangesNothing()
        {
            TrendTracker t = new TrendTracker();
            t.Update(5f, 0f);
            Assert.Equal(TrendState.Steady, t.Update(9f, 0f));
            Assert.Equal(TrendState.Gaining, t.Update(6f, 0.1f));
        }

        [Fact]
        public void NotANumberResets()
        {
            TrendTracker t = new TrendTracker();
            t.Update(5f, 0f);
            t.Update(6f, 0.1f);
            Assert.Equal(TrendState.Steady, t.Update(float.NaN, 0.2f));
            Assert.Equal(TrendState.Steady, t.Update(9f, 0.3f));    // a fresh start
        }

        [Fact]
        public void ResetIsSteady()
        {
            TrendTracker t = new TrendTracker();
            t.Update(5f, 0f);
            t.Update(6f, 0.1f);
            t.Reset();
            Assert.Equal(TrendState.Steady, t.State);
        }

        [Fact]
        public void ColoursParseAndFormat()
        {
            uint c;
            Assert.True(HudTrend.TryParse("#33DD55", out c));
            Assert.Equal(0x33DD55FFu, c);
            Assert.True(HudTrend.TryParse(" 33dd55 ", out c));
            Assert.Equal(0x33DD55FFu, c);
            Assert.True(HudTrend.TryParse("#FF444480", out c));
            Assert.Equal(0xFF444480u, c);
            Assert.Equal("#33DD55", HudTrend.Format(0x33DD55FFu));
            Assert.Equal("#FF444480", HudTrend.Format(0xFF444480u));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("#")]
        [InlineData("#12345")]
        [InlineData("#1234567")]
        [InlineData("#GG0000")]
        [InlineData("+12345")]
        [InlineData("green")]
        public void BadColoursAreRefused(string text)
        {
            uint c;
            Assert.False(HudTrend.TryParse(text, out c));
            Assert.Equal(HudTrend.DefaultLoss, HudTrend.ParseOr(text, HudTrend.DefaultLoss));
        }

        [Fact]
        public void DefaultsRoundTrip()
        {
            Assert.Equal(HudTrend.DefaultGain, HudTrend.ParseOr(HudTrend.Format(HudTrend.DefaultGain), 0));
            Assert.Equal(HudTrend.DefaultLoss, HudTrend.ParseOr(HudTrend.Format(HudTrend.DefaultLoss), 0));
            Assert.Equal(HudTrend.DefaultSteady, HudTrend.ParseOr(HudTrend.Format(HudTrend.DefaultSteady), 0));
        }
    }
}
