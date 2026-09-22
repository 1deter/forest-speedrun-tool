using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    internal sealed class FakeItems : IItemCounts
    {
        private readonly System.Collections.Generic.Dictionary<int, int> _counts
            = new System.Collections.Generic.Dictionary<int, int>();

        public FakeItems Set(int id, int amount) { _counts[id] = amount; return this; }

        public int AmountOf(int itemId)
        {
            int v;
            return _counts.TryGetValue(itemId, out v) ? v : 0;
        }
    }

    public class TriggerParseTests
    {
        [Fact]
        public void ParsesZone()
        {
            Trigger t;
            Assert.True(TriggerParser.Parse("zone 10 20 30 4.5", out t));

            Assert.Equal(TriggerKind.Zone, t.Kind);
            Assert.Equal(new Vector3(10f, 20f, 30f), t.Position);
            Assert.Equal(4.5f, t.Radius, 3);
        }

        [Fact]
        public void ParsesItemWithEachOperator()
        {
            Trigger t;

            Assert.True(TriggerParser.Parse("item 78 >= 2", out t));
            Assert.Equal(TriggerKind.Item, t.Kind);
            Assert.Equal(78, t.ItemId);
            Assert.Equal(Comparison.AtLeast, t.Compare);
            Assert.Equal(2, t.Amount);

            Assert.True(TriggerParser.Parse("item 78 <= 2", out t));
            Assert.Equal(Comparison.AtMost, t.Compare);

            Assert.True(TriggerParser.Parse("item 78 == 2", out t));
            Assert.Equal(Comparison.Exactly, t.Compare);
        }

        [Fact]
        public void ParsesEventAndManual()
        {
            Trigger t;

            Assert.True(TriggerParser.Parse("event endgame.timmy", out t));
            Assert.Equal(TriggerKind.Event, t.Kind);
            Assert.Equal("endgame.timmy", t.EventName);

            Assert.True(TriggerParser.Parse("manual", out t));
            Assert.Equal(TriggerKind.Manual, t.Kind);
        }

        [Theory]
        [InlineData("")]
        [InlineData("zone 1 2 3")]            // missing radius
        [InlineData("zone 1 2 3 0")]          // zero radius is not a zone
        [InlineData("zone a b c d")]          // non-numeric
        [InlineData("item 78 !! 2")]          // bad operator
        [InlineData("item 78 >=")]            // missing amount
        [InlineData("event")]                 // missing name
        [InlineData("nonsense 1 2 3")]
        public void RejectsMalformed(string text)
        {
            Trigger t;
            Assert.False(TriggerParser.Parse(text, out t));
        }

        [Fact]
        public void RoundTripsThroughWrite()
        {
            // A captured segment is written back out as text, so write and
            // parse have to agree or in-game capture silently corrupts.
            string[] samples =
            {
                "zone 10.00 20.00 30.00 4.50",
                "item 78 >= 2",
                "item 12 <= 0",
                "event endgame.megan",
                "manual",
            };

            foreach (string text in samples)
            {
                Trigger parsed;
                Assert.True(TriggerParser.Parse(text, out parsed), text);
                Assert.Equal(text, TriggerParser.Write(parsed));
            }
        }
    }

    public class TriggerEvaluatorTests
    {
        private static readonly Vector3 Origin = new Vector3(0f, 0f, 0f);

        private static Trigger Zone(float r)
        {
            Trigger t = new Trigger();
            t.Kind = TriggerKind.Zone;
            t.Position = Origin;
            t.Radius = r;
            return t;
        }

        // --- level behaviour ----------------------------------------------

        [Fact]
        public void ZoneIsSatisfiedInsideAndOnTheBoundary()
        {
            Trigger t = Zone(5f);

            Assert.True(TriggerEvaluator.IsSatisfied(t, new Vector3(0f, 0f, 0f), null, null));
            Assert.True(TriggerEvaluator.IsSatisfied(t, new Vector3(5f, 0f, 0f), null, null));
            Assert.False(TriggerEvaluator.IsSatisfied(t, new Vector3(5.1f, 0f, 0f), null, null));
        }

        [Fact]
        public void ItemComparisonsBehave()
        {
            FakeItems items = new FakeItems().Set(78, 3);

            Trigger atLeast;
            TriggerParser.Parse("item 78 >= 3", out atLeast);
            Assert.True(TriggerEvaluator.IsSatisfied(atLeast, Origin, items, null));

            Trigger atMost;
            TriggerParser.Parse("item 78 <= 2", out atMost);
            Assert.False(TriggerEvaluator.IsSatisfied(atMost, Origin, items, null));

            Trigger exactly;
            TriggerParser.Parse("item 78 == 3", out exactly);
            Assert.True(TriggerEvaluator.IsSatisfied(exactly, Origin, items, null));
        }

        [Fact]
        public void MissingItemCountsAsZero()
        {
            FakeItems items = new FakeItems();

            Trigger t;
            TriggerParser.Parse("item 999 == 0", out t);
            Assert.True(TriggerEvaluator.IsSatisfied(t, Origin, items, null));
        }

        [Fact]
        public void EventMatchesByNameIgnoringCase()
        {
            Trigger t;
            TriggerParser.Parse("event Endgame.Timmy", out t);

            Assert.True(TriggerEvaluator.IsSatisfied(t, Origin, null, "endgame.timmy"));
            Assert.False(TriggerEvaluator.IsSatisfied(t, Origin, null, "endgame.megan"));
            Assert.False(TriggerEvaluator.IsSatisfied(t, Origin, null, null));
        }

        [Fact]
        public void ManualNeverSatisfiesItself()
        {
            Trigger t;
            TriggerParser.Parse("manual", out t);
            Assert.False(TriggerEvaluator.IsSatisfied(t, Origin, null, "anything"));
        }

        // --- edge behaviour -----------------------------------------------

        [Fact]
        public void FirstEvaluationPrimesRatherThanFiring()
        {
            // This is the important one. Teleporting INTO a start zone must
            // not fire the start trigger before you have moved - otherwise
            // every attempt starts the instant you spawn.
            Trigger t = Zone(5f);
            TriggerState state = new TriggerState();

            Assert.False(TriggerEvaluator.Fired(t, ref state, Origin, null, null));
            Assert.True(state.Satisfied);
        }

        [Fact]
        public void FiresOnEntryNotWhileInside()
        {
            Trigger t = Zone(5f);
            TriggerState state = new TriggerState();

            // Prime while outside.
            Assert.False(TriggerEvaluator.Fired(t, ref state, new Vector3(50f, 0f, 0f), null, null));

            // Enter: fires once.
            Assert.True(TriggerEvaluator.Fired(t, ref state, Origin, null, null));

            // Still inside: must not fire again.
            Assert.False(TriggerEvaluator.Fired(t, ref state, new Vector3(1f, 0f, 0f), null, null));
            Assert.False(TriggerEvaluator.Fired(t, ref state, new Vector3(2f, 0f, 0f), null, null));
        }

        [Fact]
        public void FiresAgainAfterLeavingAndReentering()
        {
            Trigger t = Zone(5f);
            TriggerState state = new TriggerState();

            TriggerEvaluator.Fired(t, ref state, new Vector3(50f, 0f, 0f), null, null);
            Assert.True(TriggerEvaluator.Fired(t, ref state, Origin, null, null));

            Assert.False(TriggerEvaluator.Fired(t, ref state, new Vector3(50f, 0f, 0f), null, null));
            Assert.True(TriggerEvaluator.Fired(t, ref state, Origin, null, null));
        }

        [Fact]
        public void ResetRePrimes()
        {
            Trigger t = Zone(5f);
            TriggerState state = new TriggerState();

            TriggerEvaluator.Fired(t, ref state, new Vector3(50f, 0f, 0f), null, null);
            Assert.True(TriggerEvaluator.Fired(t, ref state, Origin, null, null));

            TriggerEvaluator.Reset(ref state);

            // After a reset, standing inside primes again rather than firing.
            Assert.False(TriggerEvaluator.Fired(t, ref state, Origin, null, null));
        }

        [Fact]
        public void ItemPickupFiresOnTheRisingEdge()
        {
            FakeItems items = new FakeItems().Set(78, 0);

            Trigger t;
            TriggerParser.Parse("item 78 >= 1", out t);
            TriggerState state = new TriggerState();

            Assert.False(TriggerEvaluator.Fired(t, ref state, Origin, items, null));  // prime

            items.Set(78, 1);
            Assert.True(TriggerEvaluator.Fired(t, ref state, Origin, items, null));   // picked up

            items.Set(78, 2);
            Assert.False(TriggerEvaluator.Fired(t, ref state, Origin, items, null));  // still held
        }
    }
}
