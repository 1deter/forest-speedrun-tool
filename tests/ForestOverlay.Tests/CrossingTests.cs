using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // Start-trigger crossing.
    //
    // This is the case that shipped broken: the spawn sits inside the
    // start zone, so a rising-edge check primed as "satisfied" and then
    // ignored the player walking out, and the clock never started.
    // ------------------------------------------------------------------
    public class CrossingTests
    {
        private static readonly Vector3 Inside = new Vector3(0f, 0f, 0f);
        private static readonly Vector3 Outside = new Vector3(50f, 0f, 0f);

        private static Trigger Zone(float r)
        {
            Trigger t = new Trigger();
            t.Kind = TriggerKind.Zone;
            t.Shape = ZoneShape.Sphere;
            t.Position = Vector3.zero;
            t.Radius = r;
            return t;
        }

        [Fact]
        public void StartingInsideFiresOnLeaving()
        {
            Trigger t = Zone(5f);
            TriggerState state = new TriggerState();

            // Teleported in: primes as inside, must not fire yet.
            Assert.False(TriggerEvaluator.Crossed(t, ref state, Inside, null, null, null));

            // Shuffling about inside is still not a start.
            Assert.False(TriggerEvaluator.Crossed(t, ref state, new Vector3(2f, 0f, 0f), null, null, null));

            // Leaving is.
            Assert.True(TriggerEvaluator.Crossed(t, ref state, Outside, null, null, null));
        }

        [Fact]
        public void StartingOutsideFiresOnEntering()
        {
            Trigger t = Zone(5f);
            TriggerState state = new TriggerState();

            Assert.False(TriggerEvaluator.Crossed(t, ref state, Outside, null, null, null));
            Assert.False(TriggerEvaluator.Crossed(t, ref state, new Vector3(30f, 0f, 0f), null, null, null));

            Assert.True(TriggerEvaluator.Crossed(t, ref state, Inside, null, null, null));
        }

        [Fact]
        public void RisingEdgeWouldHaveMissedTheLeavingCase()
        {
            // Documents the actual bug: the same sequence under Fired never
            // reports a start at all.
            Trigger t = Zone(5f);
            TriggerState state = new TriggerState();

            Assert.False(TriggerEvaluator.Fired(t, ref state, Inside, null, null));
            Assert.False(TriggerEvaluator.Fired(t, ref state, Outside, null, null));
            Assert.False(TriggerEvaluator.Fired(t, ref state, new Vector3(80f, 0f, 0f), null, null));
        }

        [Fact]
        public void ResetRePrimesAgainstWhereYouNowAre()
        {
            Trigger t = Zone(5f);
            TriggerState state = new TriggerState();

            TriggerEvaluator.Crossed(t, ref state, Inside, null, null, null);
            Assert.True(TriggerEvaluator.Crossed(t, ref state, Outside, null, null, null));

            // Re-arming while outside must prime as outside, so walking
            // back in starts the next attempt.
            TriggerEvaluator.Reset(ref state);
            Assert.False(TriggerEvaluator.Crossed(t, ref state, Outside, null, null, null));
            Assert.True(TriggerEvaluator.Crossed(t, ref state, Inside, null, null, null));
        }

        [Fact]
        public void ItemStartFiresWhenTheCountChanges()
        {
            // Crossing is not zone-specific: a segment that starts on
            // picking something up works the same way.
            FakeItems items = new FakeItems().Set(78, 0);

            Trigger t;
            TriggerParser.Parse("item 78 >= 1", out t);
            TriggerState state = new TriggerState();

            Assert.False(TriggerEvaluator.Crossed(t, ref state, Vector3.zero, items, null, null));

            items.Set(78, 1);
            Assert.True(TriggerEvaluator.Crossed(t, ref state, Vector3.zero, items, null, null));
        }

        [Fact]
        public void BoxStartAlsoCrosses()
        {
            Trigger t;
            TriggerParser.Parse("box 0 0 0 2 2 2", out t);
            TriggerState state = new TriggerState();

            Assert.False(TriggerEvaluator.Crossed(t, ref state, Vector3.zero, null, null, null));
            Assert.True(TriggerEvaluator.Crossed(t, ref state, new Vector3(3f, 0f, 0f), null, null, null));
        }
    }
}
