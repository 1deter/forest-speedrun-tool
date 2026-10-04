using System.Globalization;
using System.Threading;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // A savestate's `weather` header (Game/WeatherKeeper puts the rain,
    // clouds and fog back from it): key=value tokens, invariant culture,
    // missing values left alone, unknown keys ignored.
    // ------------------------------------------------------------------
    public class WeatherStateTests
    {
        private static WeatherState Rain()
        {
            WeatherState s = new WeatherState();
            s.State = "Raining";
            s.Type = "Heavy";
            s.HasDice = true;
            s.RainDice = 4; s.RainDiceStop = 1; s.RainStopRolls = 2;
            s.OvercastCurrent = 1f; s.OvercastTarget = 1f;
            s.OpacityCurrent = 2.1684f; s.OpacityTarget = 2f;
            s.AlphaCurrent = 2.5177f; s.AlphaTarget = 2f;
            s.SkyCurrent = 0f; s.SkyTarget = 0f;
            s.CoverageCurrent = 0.574f; s.CoverageTarget = 0.6f;
            s.MatOvercast = 1f; s.MatOpacity = 2.1684f; s.MatAlpha = 2.5177f;
            s.VCloudsCoverage = -0.1001f;
            s.FogTarget = 300f; s.FogDrawn = 299f;
            return s;
        }

        [Fact]
        public void WritesTheLiveRainAsRead()
        {
            Assert.Equal("state=Raining type=Heavy dice=4,1,2 overcast=1,1 opacity=2.1684,2 alpha=2.5177,2 sky=0,0 " +
                         "coverage=0.574,0.6 mat=1,2.1684,2.5177 vclouds=-0.1001 fog=300,299", Rain().Write());
        }

        [Fact]
        public void RoundTripsEveryValue()
        {
            WeatherState back;
            Assert.True(WeatherState.TryParse(Rain().Write(), out back));
            Assert.Equal("Raining", back.State);
            Assert.Equal("Heavy", back.Type);
            Assert.True(back.HasDice);
            Assert.Equal(4, back.RainDice);
            Assert.Equal(1, back.RainDiceStop);
            Assert.Equal(2, back.RainStopRolls);
            Assert.Equal(1f, back.OvercastCurrent);
            Assert.Equal(2f, back.OpacityTarget);
            Assert.Equal(2.5177f, back.AlphaCurrent, 4);
            Assert.Equal(0f, back.SkyTarget);
            Assert.Equal(0.6f, back.CoverageTarget, 4);
            Assert.Equal(2.1684f, back.MatOpacity, 4);
            Assert.Equal(-0.1001f, back.VCloudsCoverage, 4);
            Assert.Equal(300f, back.FogTarget);
            Assert.Equal(299f, back.FogDrawn);
            Assert.Equal(Rain().Write(), back.Write());
        }

        [Fact]
        public void IgnoresTheMachinesCulture()
        {
            CultureInfo was = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                string text = Rain().Write();
                Assert.Contains("opacity=2.1684,2 ", text);
                WeatherState back;
                Assert.True(WeatherState.TryParse(text, out back));
                Assert.Equal(2.1684f, back.OpacityCurrent, 4);
            }
            finally { Thread.CurrentThread.CurrentCulture = was; }
        }

        [Fact]
        public void MissingValuesStayUnsetAndUnknownKeysAreIgnored()
        {
            WeatherState back;
            Assert.True(WeatherState.TryParse("state=Idle type=None later=1,2,3 fog=1294,1294", out back));
            Assert.Equal("Idle", back.State);
            Assert.False(back.HasDice);
            Assert.True(float.IsNaN(back.OvercastCurrent));
            Assert.True(float.IsNaN(back.MatOvercast));
            Assert.Equal(1294f, back.FogDrawn);
            Assert.Equal("state=Idle type=None fog=1294,1294", back.Write());
        }

        [Fact]
        public void AnUnsetValueIsNotWritten()
        {
            WeatherState s = Rain();
            s.FogDrawn = float.NaN;
            Assert.DoesNotContain("fog=", s.Write());
        }

        [Fact]
        public void RejectsEmptyAndMalformedText()
        {
            WeatherState back;
            Assert.False(WeatherState.TryParse("", out back));
            Assert.False(WeatherState.TryParse(null, out back));
            Assert.False(WeatherState.TryParse("garbage", out back));
            Assert.False(WeatherState.TryParse("overcast=1 fog=a,b dice=1,2", out back));
            // A bad value drops only its own token.
            Assert.True(WeatherState.TryParse("state=Idle overcast=x,1", out back));
            Assert.True(float.IsNaN(back.OvercastCurrent));
        }

        [Fact]
        public void DescribesForTheLog()
        {
            Assert.Equal("Raining (Heavy), overcast 1, fog 299 m", Rain().Describe());
            WeatherState back;
            WeatherState.TryParse("state=Idle type=None mat=0,1.1,2.7 fog=1294,1294", out back);
            Assert.Equal("Idle, overcast 0, fog 1294 m", back.Describe());
        }
    }
}
