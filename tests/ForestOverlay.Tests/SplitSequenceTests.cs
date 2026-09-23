using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // Checkpoints fire in order, and the end only once they have.
    //
    // Runner report (v0.22.6): a keycard checkpoint (item 210 >= 1) was
    // "bypassed", and checkpoints in general seemed to be. Two causes: the
    // end fired with checkpoints outstanding, and a checkpoint whose
    // condition already held when it became current never fired.
    // ------------------------------------------------------------------
    public class SplitSequenceTests
    {
        private static readonly Vector3 Far = new Vector3(1000f, 0f, 0f);

        private static Trigger Zone(float x, float r)
        {
            Trigger t = new Trigger();
            t.Kind = TriggerKind.Zone;
            t.Position = new Vector3(x, 0f, 0f);
            t.Radius = r;
            return t;
        }

        private static Trigger Item(int id, int amount)
        {
            Trigger t = new Trigger();
            t.Kind = TriggerKind.Item;
            t.ItemId = id;
            t.Compare = Comparison.AtLeast;
            t.Amount = amount;
            return t;
        }

        private static Trigger Event(string name)
        {
            Trigger t = new Trigger();
            t.Kind = TriggerKind.Event;
            t.EventName = name;
            return t;
        }

        private static SplitEvent At(SplitSequence s, Vector3 p, IItemCounts items = null, string evt = null)
        {
            return s.Evaluate(p, items, evt, null);
        }

        [Fact]
        public void EndWaitsForOutstandingCheckpoints()
        {
            SplitSequence s = new SplitSequence();
            s.Begin(new List<Trigger> { Zone(50f, 2f) }, Zone(100f, 2f));

            Assert.Equal(SplitEvent.None, At(s, Far));
            // Straight to the end, checkpoint skipped: reported, not finished.
            Assert.Equal(SplitEvent.EndBlocked, At(s, new Vector3(100f, 0f, 0f)));
            Assert.Equal(SplitEvent.None, At(s, new Vector3(100f, 0f, 0f)));

            Assert.Equal(SplitEvent.Split, At(s, new Vector3(50f, 0f, 0f)));
            Assert.Equal(SplitEvent.None, At(s, Far));
            Assert.Equal(SplitEvent.Finished, At(s, new Vector3(100f, 0f, 0f)));
        }

        [Fact]
        public void KeycardAlreadyHeldWhenItBecomesCurrentFires()
        {
            // The keycard is picked up on the way to checkpoint 1; checkpoint
            // 2 asks for it. It already holds when checkpoint 1 fires.
            FakeItems items = new FakeItems().Set(210, 0);
            SplitSequence s = new SplitSequence();
            s.Begin(new List<Trigger> { Zone(50f, 2f), Item(210, 1) }, Zone(100f, 2f));

            Assert.Equal(SplitEvent.None, At(s, Far, items));
            items.Set(210, 1);
            Assert.Equal(SplitEvent.Split, At(s, new Vector3(50f, 0f, 0f), items));
            Assert.Equal(SplitEvent.Split, At(s, new Vector3(50f, 0f, 0f), items));
            Assert.Equal(2, s.Next);
        }

        [Fact]
        public void ItemCheckpointThatHoldsAtRunStartFires()
        {
            FakeItems items = new FakeItems().Set(210, 1);
            SplitSequence s = new SplitSequence();
            s.Begin(new List<Trigger> { Item(210, 1) }, Zone(100f, 2f));

            Assert.Equal(SplitEvent.Split, At(s, Far, items));
        }

        [Fact]
        public void ItemCheckpointFiresWhenPickedUp()
        {
            FakeItems items = new FakeItems().Set(210, 0);
            SplitSequence s = new SplitSequence();
            s.Begin(new List<Trigger> { Item(210, 1) }, Zone(100f, 2f));

            Assert.Equal(SplitEvent.None, At(s, Far, items));
            Assert.Equal(SplitEvent.None, At(s, Far, items));
            items.Set(210, 1);
            Assert.Equal(SplitEvent.Split, At(s, Far, items));
        }

        [Fact]
        public void LoopRouteDoesNotFinishAtTheStart()
        {
            // No checkpoints; the end zone is the start zone, and the clock
            // starts on its edge.
            SplitSequence s = new SplitSequence();
            s.Begin(new List<Trigger>(), Zone(0f, 3f));

            Assert.Equal(SplitEvent.None, At(s, new Vector3(2.9f, 0f, 0f)));
            Assert.Equal(SplitEvent.None, At(s, new Vector3(20f, 0f, 0f)));
            Assert.Equal(SplitEvent.Finished, At(s, new Vector3(1f, 0f, 0f)));
        }

        [Fact]
        public void ZoneCurrentAtRunStartNeedsAnEntry()
        {
            SplitSequence s = new SplitSequence();
            s.Begin(new List<Trigger> { Zone(0f, 5f) }, Zone(100f, 2f));

            Assert.Equal(SplitEvent.None, At(s, new Vector3(1f, 0f, 0f)));
            Assert.Equal(SplitEvent.None, At(s, Far));
            Assert.Equal(SplitEvent.Split, At(s, new Vector3(1f, 0f, 0f)));
        }

        [Fact]
        public void StandingInTheNextZoneWhenTheLastSplitFiresSplitsAgain()
        {
            // Overlapping zones: inside the second when the first fires.
            SplitSequence s = new SplitSequence();
            s.Begin(new List<Trigger> { Zone(50f, 3f), Zone(52f, 3f) }, Zone(100f, 2f));

            Assert.Equal(SplitEvent.None, At(s, Far));
            Assert.Equal(SplitEvent.Split, At(s, new Vector3(51f, 0f, 0f)));
            Assert.Equal(SplitEvent.Split, At(s, new Vector3(51f, 0f, 0f)));
        }

        [Fact]
        public void EndAlreadyReachedWhenTheLastCheckpointFiresFinishes()
        {
            FakeItems items = new FakeItems().Set(210, 0);
            SplitSequence s = new SplitSequence();
            s.Begin(new List<Trigger> { Item(210, 1) }, Zone(100f, 2f));

            Assert.Equal(SplitEvent.None, At(s, Far, items));
            Assert.Equal(SplitEvent.EndBlocked, At(s, new Vector3(100f, 0f, 0f), items));
            items.Set(210, 1);
            Assert.Equal(SplitEvent.Split, At(s, new Vector3(100f, 0f, 0f), items));
            Assert.Equal(SplitEvent.Finished, At(s, new Vector3(100f, 0f, 0f), items));
        }

        [Fact]
        public void LaterCheckpointsAreNotEvaluatedEarly()
        {
            SplitSequence s = new SplitSequence();
            s.Begin(new List<Trigger> { Zone(50f, 2f), Zone(70f, 2f) }, Zone(100f, 2f));

            Assert.Equal(SplitEvent.None, At(s, Far));
            Assert.Equal(SplitEvent.None, At(s, new Vector3(70f, 0f, 0f)));
            Assert.Equal(0, s.Next);
        }

        [Fact]
        public void EventCheckpointFiresOnlyOnItsEvent()
        {
            SplitSequence s = new SplitSequence();
            s.Begin(new List<Trigger> { Event("vault-door") }, Event("game-end"));

            Assert.Equal(SplitEvent.None, At(s, Far));
            Assert.Equal(SplitEvent.EndBlocked, At(s, Far, null, "game-end"));
            Assert.Equal(SplitEvent.Split, At(s, Far, null, "vault-door"));
            Assert.Equal(SplitEvent.None, At(s, Far));
            Assert.Equal(SplitEvent.Finished, At(s, Far, null, "game-end"));
        }

        [Fact]
        public void ManualSkipMovesOn()
        {
            SplitSequence s = new SplitSequence();
            s.Begin(new List<Trigger> { Zone(50f, 2f) }, Zone(100f, 2f));

            s.SkipCheckpoint();
            Assert.True(s.OnlyEndLeft);
            Assert.Equal(SplitEvent.Finished, At(s, new Vector3(100f, 0f, 0f)));
        }

        [Fact]
        public void BeginResetsAPreviousRun()
        {
            SplitSequence s = new SplitSequence();
            List<Trigger> cps = new List<Trigger> { Zone(50f, 2f) };
            s.Begin(cps, Zone(100f, 2f));
            At(s, Far);
            At(s, new Vector3(50f, 0f, 0f));
            Assert.Equal(1, s.Next);

            s.Begin(cps, Zone(100f, 2f));
            Assert.Equal(0, s.Next);
            Assert.Equal(SplitEvent.None, At(s, new Vector3(100f, 0f, 0f)));
        }
    }
}
