using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class KeptBushTests
    {
        private const int Mark = 7;   // the capture's `bushes` number

        [Fact]
        public void ABushBurningAtTheCaptureIsNotCutThenAndComesBackOnceBurntOut()
        {
            // Lit by the spread before the capture, still standing at it:
            // not numbered, so not in `cutbushes` (a Full load leaves it).
            Assert.Equal(KeptBush.Fate.Wait, KeptBush.AtCapture(false, true));
            // Burnt out after the capture (the game deleted it): a Quick
            // load to that capture puts it back.
            Assert.Equal(KeptBush.Fate.Back, KeptBush.AtRestore(KeptBush.Standing, Mark, true, false));
            // Numbered by a later capture (it was gone then): back for the
            // earlier capture, gone for the later one.
            Assert.Equal(KeptBush.Fate.Back, KeptBush.AtRestore(9, Mark, true, false));
            Assert.Equal(KeptBush.Fate.Stay, KeptBush.AtRestore(9, 9, true, false));
        }

        [Fact]
        public void ABushBurntOutBeforeTheCaptureIsCutThenAndStaysGone()
        {
            Assert.Equal(KeptBush.Fate.Cut, KeptBush.AtCapture(true, false));
            Assert.Equal(KeptBush.Fate.Stay, KeptBush.AtRestore(5, Mark, true, false));
            // Another world: every gone bush comes back, as after a load.
            Assert.Equal(KeptBush.Fate.Back, KeptBush.AtRestore(5, -1, true, false));
        }

        [Fact]
        public void AStandingBushIsKeptWhileBurningAndForgottenOnceOut()
        {
            Assert.Equal(KeptBush.Fate.Drop, KeptBush.AtCapture(false, false));
            // Lit before the capture, still burning after the load: decide later.
            Assert.Equal(KeptBush.Fate.Wait, KeptBush.AtRestore(KeptBush.Standing, Mark, false, true));
            // Put out by the load (lit since the capture): nothing to bring back.
            Assert.Equal(KeptBush.Fate.Drop, KeptBush.AtRestore(KeptBush.Standing, Mark, false, false));
        }

        [Fact]
        public void CutsBehaveAsBefore()
        {
            Assert.Equal(KeptBush.Fate.Back, KeptBush.AtRestore(8, Mark, true, false));    // cut since
            Assert.Equal(KeptBush.Fate.Stay, KeptBush.AtRestore(Mark, Mark, true, false)); // cut before
            Assert.Equal(KeptBush.Fate.Drop, KeptBush.AtRestore(8, Mark, false, false));   // original alive
            Assert.Equal(KeptBush.Fate.Drop, KeptBush.AtRestore(8, Mark, false, true));
        }
    }
}
