using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // Game/WreckClearing's rule (T-0148): a wreck starting where a live one
    // stands repeats the clearing; the first of a scene, one at a new place
    // and one after a load (the old wrecks dead) do not. The live wreck's
    // place from the bridge: Hull(Clone)/PlaneReal/Hull (360.61, 83.28, 1076.2).
    // ------------------------------------------------------------------
    public class WreckSitesTests
    {
        private sealed class Wreck
        {
            public bool Alive = true;
            public Vector3 At;
            public Wreck(float x, float y, float z) { At = new Vector3(x, y, z); }
        }

        private static WreckSites<Wreck> Sites()
        {
            return new WreckSites<Wreck>(w => w.Alive, w => w.At);
        }

        [Fact]
        public void TheFirstWreckRuns()
        {
            Wreck w = new Wreck(360.61f, 83.28f, 1076.2f);
            Assert.False(Sites().IsRepeat(w, w.At));
        }

        [Fact]
        public void ARestoresWreckAtTheSamePlaceIsARepeat()
        {
            WreckSites<Wreck> s = Sites();
            Wreck old = new Wreck(360.61f, 83.28f, 1076.2f);
            Wreck fresh = new Wreck(360.61f, 83.28f, 1076.2f);
            Assert.False(s.IsRepeat(old, old.At));
            Assert.True(s.IsRepeat(fresh, fresh.At));
            // The old one removed (ClearOldPlaneHulls), the next restore's:
            old.Alive = false;
            Wreck next = new Wreck(360.6f, 83.3f, 1076.2f);
            Assert.True(s.IsRepeat(next, next.At));
        }

        [Fact]
        public void AWreckAtANewPlaceRuns()
        {
            WreckSites<Wreck> s = Sites();
            Wreck here = new Wreck(360.61f, 83.28f, 1076.2f);
            Wreck other = new Wreck(-24.9f, 55.7f, 1061.3f);   // another save's crash site
            s.IsRepeat(here, here.At);
            Assert.False(s.IsRepeat(other, other.At));
            Wreck near = new Wreck(362f, 83.28f, 1076.2f);       // 1.4 m off: not the same place
            Assert.False(s.IsRepeat(near, near.At));
        }

        [Fact]
        public void AfterALoadTheWreckRuns()
        {
            WreckSites<Wreck> s = Sites();
            Wreck before = new Wreck(360.61f, 83.28f, 1076.2f);
            s.IsRepeat(before, before.At);
            before.Alive = false;   // a Full load destroyed the scene
            Wreck after = new Wreck(360.61f, 83.28f, 1076.2f);
            Assert.False(s.IsRepeat(after, after.At));
            Assert.Equal(1, s.Count);
        }

        [Fact]
        public void TheSameWreckTwiceIsNotARepeat()
        {
            WreckSites<Wreck> s = Sites();
            Wreck w = new Wreck(360.61f, 83.28f, 1076.2f);
            s.IsRepeat(w, w.At);
            Assert.False(s.IsRepeat(w, w.At));
            Assert.Equal(1, s.Count);
        }
    }
}
