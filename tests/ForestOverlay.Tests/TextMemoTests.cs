using System.Globalization;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // The splits table's texts come from a memo now (fewer strings while a
    // run is on): the text must be exactly what SplitTable makes, and an
    // unchanged value must give back the same string, not a new one.
    public class TextMemoTests
    {
        private static readonly float[] Values =
        {
            0f, -0f, 0.004f, 0.005f, 0.0049999f, 1.2345f, 59.995f, 59.9949f, 61.5f, 3599.9995f, 3600f, 7322.25f,
            -0.5f, -12.345f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, 123456.7f,
        };

        [Fact]
        public void TextIsExactlySplitTables()
        {
            TextMemo m = new TextMemo();
            for (int d = 0; d <= 3; d++)
                for (int i = 0; i < Values.Length; i++)
                {
                    float v = Values[i];
                    // Twice each, so the second read is the remembered one.
                    for (int k = 0; k < 2; k++)
                    {
                        Assert.Equal(SplitTable.Time(v, d), m.Time(0, v, d));
                        Assert.Equal(SplitTable.Delta(v, d), m.Delta(1, v, d));
                        Assert.Equal(float.IsNaN(v) ? "" : SplitTable.Time(v, d), m.TimeOrEmpty(2, v, d));
                    }
                }
        }

        [Fact]
        public void SameInputSameString()
        {
            TextMemo m = new TextMemo();
            string a = m.Time(3, 12.5f, 2);
            Assert.Same(a, m.Time(3, 12.5f, 2));
            Assert.Same(m.Delta(4, float.NaN, 2), m.Delta(4, float.NaN, 2));
        }

        [Fact]
        public void AnyChangeFormatsAgain()
        {
            TextMemo m = new TextMemo();
            Assert.Equal(SplitTable.Time(12.5f, 2), m.Time(0, 12.5f, 2));
            Assert.Equal(SplitTable.Time(12.5f, 1), m.Time(0, 12.5f, 1));     // decimals
            Assert.Equal(SplitTable.Delta(12.5f, 1), m.Delta(0, 12.5f, 1));   // kind at the same slot
            Assert.Equal(SplitTable.Delta(12.6f, 1), m.Delta(0, 12.6f, 1));   // value
            Assert.Equal("", m.TimeOrEmpty(0, float.NaN, 1));
            m.Clear();
            Assert.Equal(SplitTable.Time(12.6f, 2), m.Time(0, 12.6f, 2));
            // Slots far apart grow the memo; a negative slot is not kept.
            Assert.Equal(SplitTable.Time(1f, 2), m.Time(500, 1f, 2));
            Assert.Equal(SplitTable.Time(2f, 2), m.Time(-1, 2f, 2));
        }

        [Fact]
        public void HudNumbersUnchangedByCachedFormats()
        {
            // HudLines.F reads its format string from a table now; the text
            // is what "F" + n made before.
            float[] vs = { 0f, 1.25f, -3.5f, 123.456f, 0.005f };
            for (int i = 0; i < vs.Length; i++)
            {
                float v = vs[i];
                string f0 = v.ToString("F0", CultureInfo.InvariantCulture);
                string f1 = v.ToString("F1", CultureInfo.InvariantCulture);
                string f2 = v.ToString("F2", CultureInfo.InvariantCulture);
                Assert.Equal(f0 + ", " + f0 + ", " + f0, HudLines.Vector(v, v, v, 0));
                Assert.Equal(f1 + ", " + f1 + ", " + f1, HudLines.Vector(v, v, v, 1));
                Assert.Equal(f2 + " u/s   (tot " + f2 + ")", HudLines.Speed(v, v, false));
            }
        }
    }
}
