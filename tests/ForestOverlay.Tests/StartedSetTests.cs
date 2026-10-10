using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // Game/ElevatorRides' rules (T-0184): an elevator that started a ride
    // is noted once; reading the set hands out the ones still alive and
    // moving on an active object, drops the gone and the stopped, and keeps
    // a switched-off one that is still moving (handed out once back on).
    // ------------------------------------------------------------------
    public class StartedSetTests
    {
        private sealed class Ride
        {
            public bool Alive = true;
            public bool Moving = true;
            public bool Active = true;
        }

        private static bool Alive(Ride r) { return r.Alive; }
        private static bool Moving(Ride r) { return r.Moving; }
        private static bool Active(Ride r) { return r.Active; }

        private static List<Ride> Read(StartedSet<Ride> set)
        {
            List<Ride> into = new List<Ride>();
            set.Running(Alive, Moving, Active, into);
            return into;
        }

        [Fact]
        public void Empty_HandsOutNothing()
        {
            Assert.Empty(Read(new StartedSet<Ride>()));
        }

        [Fact]
        public void NotedTwice_HandedOutOnce()
        {
            StartedSet<Ride> set = new StartedSet<Ride>();
            Ride a = new Ride();
            set.Note(a);
            set.Note(a);
            set.Note(null);
            Assert.Equal(1, set.Count);
            Assert.Equal(new[] { a }, Read(set));
        }

        [Fact]
        public void StoppedAndGone_AreDropped()
        {
            StartedSet<Ride> set = new StartedSet<Ride>();
            Ride stopped = new Ride(), gone = new Ride(), riding = new Ride();
            set.Note(stopped);
            set.Note(gone);
            set.Note(riding);
            stopped.Moving = false;
            gone.Alive = false;
            Assert.Equal(new[] { riding }, Read(set));
            Assert.Equal(1, set.Count);
            // A ride stopped later (StopRides) is dropped on the next read.
            riding.Moving = false;
            Assert.Empty(Read(set));
            Assert.Equal(0, set.Count);
        }

        [Fact]
        public void SwitchedOff_KeptButNotHandedOut()
        {
            StartedSet<Ride> set = new StartedSet<Ride>();
            Ride off = new Ride();
            set.Note(off);
            off.Active = false;
            Assert.Empty(Read(set));
            Assert.Equal(1, set.Count);
            off.Active = true;
            Assert.Equal(new[] { off }, Read(set));
        }

        [Fact]
        public void HandedOut_InTheOrderStarted()
        {
            StartedSet<Ride> set = new StartedSet<Ride>();
            Ride a = new Ride(), b = new Ride(), c = new Ride();
            set.Note(a);
            set.Note(b);
            set.Note(c);
            b.Moving = false;
            Assert.Equal(new[] { a, c }, Read(set));
        }

        [Fact]
        public void Clear_ForgetsAll()
        {
            StartedSet<Ride> set = new StartedSet<Ride>();
            set.Note(new Ride());
            set.Clear();
            Assert.Equal(0, set.Count);
            Assert.Empty(Read(set));
        }
    }
}
