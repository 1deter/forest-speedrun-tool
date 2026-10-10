using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // Game/WreckNav's rule (T-0278): a restore's new wreck skips its nav
    // cut only when a twin at the same pose has its cut in the navmesh,
    // and the old wreck the overlay removes skips its removal only while a
    // twin keeps the cut. Everything else runs the game's update.
    // ------------------------------------------------------------------
    public class WreckCutsTests
    {
        private sealed class Wreck
        {
            public bool Alive = true;
            public bool Pending;
            public Vector3 At, Forward = new Vector3(0f, 0f, 1f), Up = new Vector3(0f, 1f, 0f);
            public Wreck(float x, float y, float z) { At = new Vector3(x, y, z); }
        }

        private static WreckCuts<Wreck> Cuts()
        {
            return new WreckCuts<Wreck>(w => w.Alive, w => w.At, w => w.Forward, w => w.Up);
        }

        private static bool Pending(Wreck w) { return w.Pending; }

        private static Wreck At() { return new Wreck(360.61f, 83.28f, 1076.2f); }

        [Fact]
        public void TheFirstWreckCutsAsTheGameDoes()
        {
            Assert.Null(Cuts().NewCut(At(), Pending));
        }

        [Fact]
        public void ARestoresWreckOverACutTwinSkipsItsCut()
        {
            WreckCuts<Wreck> c = Cuts();
            Wreck old = At(), fresh = At();
            Assert.Null(c.NewCut(old, Pending));
            Assert.Same(old, c.NewCut(fresh, Pending));
        }

        [Fact]
        public void ATwinStillWaitingInTheGamesListDoesNotCount()
        {
            WreckCuts<Wreck> c = Cuts();
            Wreck old = At(), fresh = At();
            c.NewCut(old, Pending);
            old.Pending = true;
            Assert.Null(c.NewCut(fresh, Pending));
        }

        [Fact]
        public void AnInheritedCutCarriesOverRestoreAfterRestore()
        {
            WreckCuts<Wreck> c = Cuts();
            Wreck first = At(), second = At(), third = At();
            c.NewCut(first, Pending);
            Assert.Same(first, c.NewCut(second, Pending));
            c.Removing(first);
            Assert.Same(second, c.Removal(first));
            first.Alive = false;
            Assert.Same(second, c.NewCut(third, Pending));
        }

        [Fact]
        public void AnOtherRouteCutIsNoTwinForACutButKeepsARemoval()
        {
            WreckCuts<Wreck> c = Cuts();
            Wreck old = At(), fresh = At();
            c.OtherCut(old);
            Assert.Null(c.NewCut(fresh, Pending));
            c.Removing(old);
            Assert.Same(fresh, c.Removal(old));
        }

        [Fact]
        public void ATwinTheOverlayIsRemovingIsNoTwin()
        {
            WreckCuts<Wreck> c = Cuts();
            Wreck old = At(), fresh = At();
            c.NewCut(old, Pending);
            c.Removing(old);
            Assert.Null(c.NewCut(fresh, Pending));
        }

        [Fact]
        public void AWreckElsewhereOrTurnedCutsAsTheGameDoes()
        {
            WreckCuts<Wreck> c = Cuts();
            Wreck old = At();
            c.NewCut(old, Pending);
            Assert.Null(c.NewCut(new Wreck(360.63f, 83.28f, 1076.2f), Pending));   // 2 cm off
            Wreck turned = At();
            turned.Forward = new Vector3(0.01f, 0f, 0.99995f);                       // ~0.6 degree
            Assert.Null(c.NewCut(turned, Pending));
        }

        [Fact]
        public void ARemovalTheGameMakesRunsAsTheGames()
        {
            WreckCuts<Wreck> c = Cuts();
            Wreck old = At(), fresh = At();
            c.NewCut(old, Pending);
            c.NewCut(fresh, Pending);
            Assert.Null(c.Removal(old));   // not removed by the overlay (a scene unload, a load)
        }

        [Fact]
        public void ARemovalWithNoTwinLeftRunsAsTheGames()
        {
            WreckCuts<Wreck> c = Cuts();
            Wreck old = At(), fresh = At();
            c.NewCut(old, Pending);
            c.NewCut(fresh, Pending);
            fresh.Alive = false;
            c.Removing(old);
            Assert.Null(c.Removal(old));
        }

        [Fact]
        public void ARemovalIsAnsweredTheSameWhileTheWreckIsBeingDestroyed()
        {
            WreckCuts<Wreck> c = Cuts();
            Wreck old = At(), fresh = At();
            c.NewCut(old, Pending);
            c.NewCut(fresh, Pending);
            c.Removing(old);
            old.Alive = false;   // Unity's null during OnDestroy
            Assert.Same(fresh, c.Removal(old));
            Assert.Same(fresh, c.Removal(old));
        }

        [Fact]
        public void AWreckNotYetCutIsNoTwinForARemoval()
        {
            WreckCuts<Wreck> c = Cuts();
            Wreck old = At();
            c.NewCut(old, Pending);
            c.Removing(old);
            Assert.Null(c.Removal(old));   // the new wreck has not started its cut
        }
    }
}
