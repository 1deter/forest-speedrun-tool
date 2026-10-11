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
    }
}
