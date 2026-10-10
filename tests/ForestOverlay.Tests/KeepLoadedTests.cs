using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // Keep loaded (T-0212): one real restore, then cheap restarts until the
    // world changes - any scene load / unload, another restore or load, a
    // death (author, 2026-10-10).
    // ------------------------------------------------------------------
    public class KeepLoadedTests
    {
        private static KeepLoaded ArmedFor(string id, int scenes, int restores)
        {
            KeepLoaded k = new KeepLoaded();
            k.Restoring(id);
            k.Settled(id, true, scenes, restores);
            return k;
        }

        [Fact]
        public void TheFirstRestartRestores()
        {
            Assert.NotNull(new KeepLoaded().Check("a", true, false, 0, 0));
        }

        [Fact]
        public void AfterASettledRestoreTheNextRestartIsCheap()
        {
            KeepLoaded k = ArmedFor("a", 7, 3);
            Assert.Null(k.Check("a", true, false, 7, 3));
            Assert.Null(k.Check("a", true, false, 7, 3));   // and the one after
            Assert.Equal("a", k.Segment);
        }

        [Fact]
        public void BeforeItSettlesTheRestartRestores()
        {
            KeepLoaded k = new KeepLoaded();
            k.Restoring("a");
            Assert.NotNull(k.Check("a", true, false, 7, 3));
            Assert.Equal("", k.Segment);
        }

        [Fact]
        public void AnySceneEventSinceRestores()
        {
            Assert.Contains("scene", ArmedFor("a", 7, 3).Check("a", true, false, 8, 3));
        }

        [Fact]
        public void AnotherRestoreSinceRestores()
        {
            Assert.Contains("restore", ArmedFor("a", 7, 3).Check("a", true, false, 7, 4));
        }

        [Fact]
        public void AnotherSpotRestores()
        {
            Assert.Contains("another spot", ArmedFor("a", 7, 3).Check("b", true, false, 7, 3));
        }

        [Fact]
        public void ToggleOffOrARunStartRestores()
        {
            KeepLoaded k = ArmedFor("a", 7, 3);
            Assert.NotNull(k.Check("a", false, false, 7, 3));
            Assert.NotNull(k.Check("a", true, true, 7, 3));
        }

        [Fact]
        public void ADropRestoresAndSaysWhy()
        {
            KeepLoaded k = ArmedFor("a", 7, 3);
            k.Drop("a death");
            Assert.Equal("a death", k.Check("a", true, false, 7, 3));
            Assert.Equal("", k.Segment);
        }

        [Fact]
        public void AWorldThatDoesNotMatchTheStartStateIsNotKept()
        {
            KeepLoaded k = new KeepLoaded();
            k.Restoring("a");
            k.Settled("a", false, 7, 3);
            Assert.Contains("areas", k.Check("a", true, false, 7, 3));
        }

        [Fact]
        public void ASettleForAnotherSegmentIsIgnored()
        {
            KeepLoaded k = new KeepLoaded();
            k.Restoring("a");
            k.Settled("b", true, 7, 3);
            Assert.NotNull(k.Check("a", true, false, 7, 3));
        }

        [Fact]
        public void ANewRestoreReplacesTheBaseline()
        {
            KeepLoaded k = ArmedFor("a", 7, 3);
            k.Restoring("a");
            k.Settled("a", true, 12, 4);
            Assert.Null(k.Check("a", true, false, 12, 4));
        }
    }
}
