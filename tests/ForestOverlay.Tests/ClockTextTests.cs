using System;
using System.Globalization;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // The HUD's running clock is built in one go now (fewer strings while a
    // run is on): it must read as the old formatter did - mm:ss.mmm and a
    // signed two-decimal delta - and the split names that fill the table
    // are the same string every time, not a new one per refresh.
    public class ClockTextTests
    {
        private static string Old(float seconds)
        {
            int m = (int)(seconds / 60f);
            float s = seconds - m * 60f;
            return m.ToString("00") + ":" + s.ToString("00.000");
        }

        private static double Seconds(string text)
        {
            string sep = NumberFormatInfo.CurrentInfo.NumberDecimalSeparator;
            int colon = text.IndexOf(':');
            Assert.True(colon > 0, text);
            double m = double.Parse(text.Substring(0, colon), CultureInfo.InvariantCulture);
            double s = double.Parse(text.Substring(colon + 1).Replace(sep, "."), CultureInfo.InvariantCulture);
            return m * 60.0 + s;
        }

        [Fact]
        public void ExactValuesReadLikeTheOldFormat()
        {
            float[] values = { 0f, 0.5f, 1f, 12.5f, 59.25f, 65.25f, 600f, 3599.5f, 3600f, 7322.25f };
            foreach (float v in values) Assert.Equal(Old(v), ClockText.Clock(v));
        }

        [Fact]
        public void EveryValueIsWithinAMillisecondOfTheOldFormat()
        {
            Random r = new Random(7);
            for (int i = 0; i < 5000; i++)
            {
                float v = (float)(r.NextDouble() * (i % 3 == 0 ? 5000.0 : 200.0));
                string text = ClockText.Clock(v);
                Assert.Matches(@"^\d\d+:\d\d\.\d\d\d$", text.Replace(NumberFormatInfo.CurrentInfo.NumberDecimalSeparator, "."));
                Assert.InRange(Math.Abs(Seconds(text) - Seconds(Old(v))), 0.0, 0.00101);
            }
        }

        [Fact]
        public void DeltaIsSignedWithTwoDecimals()
        {
            string sep = NumberFormatInfo.CurrentInfo.NumberDecimalSeparator;
            Assert.Equal("00:12" + sep + "500   +1" + sep + "25", ClockText.ClockWithDelta(12.5f, 1.25f));
            Assert.Equal("00:12" + sep + "500   -0" + sep + "45", ClockText.ClockWithDelta(12.5f, -0.45f));
            Assert.Equal("00:12" + sep + "500   +0" + sep + "00", ClockText.ClockWithDelta(12.5f, 0f));
            Assert.Equal("+12" + sep + "30", ClockText.Signed(12.3f));
        }

        [Fact]
        public void StrangeValuesFallBackInsteadOfBreaking()
        {
            Assert.Equal(Old(-1f), ClockText.Clock(-1f));
            Assert.Equal(Old(float.NaN), ClockText.Clock(float.NaN));
            Assert.NotNull(ClockText.ClockWithDelta(1f, float.NaN));
        }

        [Fact]
        public void UnnamedCheckpointsKeepTheirText()
        {
            Segment s = new Segment();
            s.Checkpoints.Add(new Trigger());
            s.Checkpoints.Add(new Trigger());
            Assert.Equal("Checkpoint 1", s.SplitName(0));
            Assert.Equal("Checkpoint 2", s.SplitName(1));
            Assert.Equal("End", s.SplitName(2));
            Assert.Same(s.SplitName(0), s.SplitName(0));
            Segment many = new Segment();
            for (int i = 0; i < 100; i++) many.Checkpoints.Add(new Trigger());
            Assert.Equal("Checkpoint 100", many.SplitName(99));
        }
    }
}
