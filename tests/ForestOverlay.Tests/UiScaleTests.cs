using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class UiScaleTests
    {
        private static UiScale At(int rw, int rh, int sw, int sh, bool follow)
        {
            UiScale s = new UiScale { SizesFollow = follow };
            s.SetReference(rw, rh);
            s.Update(sw, sh);
            return s;
        }

        [Fact]
        public void AtTheReferenceNothingMoves()
        {
            UiScale s = At(1920, 1080, 1920, 1080, true);
            Assert.Equal(1f, s.F);
            Assert.Equal(137f, s.PosX(137f));
            Assert.Equal(42f, s.PosY(42f));
            Assert.Equal(137f, s.ToRefX(137f));
        }

        [Fact]
        public void NoReferenceYetIsPlainPixels()
        {
            UiScale s = new UiScale();
            s.Update(2560, 1440);
            Assert.Equal(1f, s.F);
            Assert.Equal(10f, s.PosX(10f));
            Assert.Equal(10f, s.ToRefY(10f));
        }

        [Fact]
        public void SizesFollowKeepsThePlaceRelativeToTheFrame()
        {
            UiScale s = At(1920, 1080, 2560, 1440, true);
            Assert.Equal(4f / 3f, s.F, 3);
            Assert.Equal(1920f, s.VW, 2);
            Assert.Equal(1080f, s.VH, 2);
            // Drawn in the virtual screen then scaled by F: the same fraction of the frame.
            Assert.Equal(480f / 1920f, s.PosX(480f) * s.F / 2560f, 4);
            Assert.Equal(270f / 1080f, s.PosY(270f) * s.F / 1440f, 4);
        }

        [Fact]
        public void PixelSizesStillMoveRelatively()
        {
            UiScale s = At(1920, 1080, 960, 540, false);
            Assert.Equal(1f, s.F);
            Assert.Equal(240f, s.PosX(480f), 3);
            Assert.Equal(135f, s.PosY(270f), 3);
            Assert.Equal(480f, s.ToRefX(240f), 3);
        }

        [Fact]
        public void AnotherAspectKeepsTheFraction()
        {
            UiScale s = At(1920, 1080, 2560, 1080, true);   // ultrawide-ish
            Assert.Equal(1f, s.F);
            Assert.Equal(960f * 2560f / 1920f, s.PosX(960f), 2);
            Assert.Equal(960f, s.ToRefX(s.PosX(960f)), 2);
        }

        [Fact]
        public void SentinelsStay()
        {
            UiScale s = At(1920, 1080, 1280, 720, true);
            Assert.Equal(-1f, s.PosX(-1f));
            Assert.Equal(-1f, s.PosY(-1f));
        }

        [Fact]
        public void UpdateReportsChangesOnly()
        {
            UiScale s = new UiScale();
            s.SetReference(1920, 1080);
            Assert.True(s.Update(1920, 1080));
            Assert.False(s.Update(1920, 1080));
            Assert.True(s.Update(1280, 720));
            s.SizesFollow = false;
            Assert.True(s.Update(1280, 720));
            Assert.Equal(1f, s.F);
        }

        [Fact]
        public void FactorIsClamped()
        {
            UiScale s = At(1920, 1080, 100, 100, true);
            Assert.Equal(UiScale.MinFactor, s.F);
            s = At(480, 270, 7680, 4320, true);
            Assert.Equal(UiScale.MaxFactor, s.F);
        }

        [Fact]
        public void TheWindowScalesWithTheHeightAbove768Only()
        {
            Assert.Equal(1f, UiScale.WindowFactor(768));
            Assert.Equal(1f, UiScale.WindowFactor(720));
            Assert.Equal(1f, UiScale.WindowFactor(0));
            Assert.Equal(1080f / 768f, UiScale.WindowFactor(1080), 4);
            Assert.Equal(1440f / 768f, UiScale.WindowFactor(1440), 4);
            Assert.Equal(UiScale.MaxFactor, UiScale.WindowFactor(100000));
        }

        [Fact]
        public void WindowFontsRoundDownSoTextIsNeverWider()
        {
            float k1080 = UiScale.WindowFactor(1080), k1440 = UiScale.WindowFactor(1440);
            Assert.Equal(18, UiScale.WindowFontPx(13, k1080));   // 18.28
            Assert.Equal(24, UiScale.WindowFontPx(13, k1440));   // 24.375
            Assert.Equal(16, UiScale.WindowFontPx(12, k1080));   // 16.875: down, not 17
            Assert.Equal(26, UiScale.WindowFontPx(13, 2f));      // an exact product stays
            Assert.Equal(13, UiScale.WindowFontPx(13, 1f));
            Assert.Equal(11, UiScale.WindowFontPx(11, 1f));
            Assert.Equal(0, UiScale.WindowFontPx(0, k1080));     // 0 = the skin's size, left to the caller
            Assert.Equal(1, UiScale.WindowFontPx(1, 0.5f));
        }

        [Fact]
        public void WindowPixelsLandOnWholePixels()
        {
            float k = UiScale.WindowFactor(1080);   // 1.40625
            Assert.Equal(11f, UiScale.WindowPx(8f, k));      // 11.25
            Assert.Equal(84f, UiScale.WindowPx(60f, k));     // 84.375
            Assert.Equal(0f, UiScale.WindowPx(0f, k));
            Assert.Equal(-14f, UiScale.WindowPx(-10f, k));   // -14.06
            Assert.Equal(137f, UiScale.WindowPx(137f, 1f));
            // A scroll offset through pixels and back is stable after one trip.
            float px = UiScale.WindowPx(33.3f, k);
            Assert.Equal(px, UiScale.WindowPx(px / k, k));
        }
    }
}
