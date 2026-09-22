using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // Box zones and relative item amounts.
    //
    // Both are format changes, so the round trip matters as much as the
    // behaviour: a segment authored today has to still parse tomorrow.
    // ------------------------------------------------------------------
    public class BoxZoneTests
    {
        [Fact]
        public void ParsesBox()
        {
            Trigger t;
            Assert.True(TriggerParser.Parse("box 10 20 30 2 3 4", out t));

            Assert.Equal(TriggerKind.Zone, t.Kind);
            Assert.Equal(ZoneShape.Box, t.Shape);
            Assert.Equal(new Vector3(10f, 20f, 30f), t.Position);
            Assert.Equal(new Vector3(2f, 3f, 4f), t.Extents);
        }

        [Fact]
        public void ZoneStillParsesAsSphere()
        {
            Trigger t;
            Assert.True(TriggerParser.Parse("zone 1 2 3 4", out t));

            Assert.Equal(ZoneShape.Sphere, t.Shape);
            Assert.Equal(4f, t.Radius, 3);
        }

        [Theory]
        [InlineData("box 1 2 3 4 5")]      // missing an extent
        [InlineData("box 1 2 3 0 5 6")]    // zero extent is not a volume
        [InlineData("box a b c d e f")]
        public void RejectsMalformedBox(string text)
        {
            Trigger t;
            Assert.False(TriggerParser.Parse(text, out t));
        }

        [Fact]
        public void BoxContainsIsPerAxis()
        {
            Trigger t;
            TriggerParser.Parse("box 0 0 0 2 1 3", out t);

            Assert.True(TriggerEvaluator.IsSatisfied(t, new Vector3(0f, 0f, 0f), null, null));
            Assert.True(TriggerEvaluator.IsSatisfied(t, new Vector3(2f, 1f, 3f), null, null));

            // Outside on one axis only is still outside - the failure a
            // sphere check would not catch.
            Assert.False(TriggerEvaluator.IsSatisfied(t, new Vector3(0f, 1.5f, 0f), null, null));
            Assert.False(TriggerEvaluator.IsSatisfied(t, new Vector3(2.5f, 0f, 0f), null, null));
        }

        [Fact]
        public void BoxRoundTrips()
        {
            const string text = "box 10.00 20.00 30.00 2.00 3.00 4.00";

            Trigger t;
            Assert.True(TriggerParser.Parse(text, out t));
            Assert.Equal(text, TriggerParser.Write(t));
        }
    }

    public class RelativeItemTests
    {
        private sealed class Counts : IItemCounts
        {
            private readonly int _value;
            public Counts(int value) { _value = value; }
            public int AmountOf(int itemId) { return _value; }
        }

        [Fact]
        public void ParsesAndWritesTheRelativeMarker()
        {
            Trigger t;
            Assert.True(TriggerParser.Parse("item 78 >= +3", out t));

            Assert.True(t.Relative);
            Assert.Equal(3, t.Amount);
            Assert.Equal("item 78 >= +3", TriggerParser.Write(t));
        }

        [Fact]
        public void AbsoluteStaysAbsolute()
        {
            Trigger t;
            TriggerParser.Parse("item 78 >= 3", out t);

            Assert.False(t.Relative);
            Assert.Equal("item 78 >= 3", TriggerParser.Write(t));
        }

        [Fact]
        public void RelativeMeasuresFromTheBaseline()
        {
            Trigger t;
            TriggerParser.Parse("item 78 >= +3", out t);

            // Started holding 5, so the trigger wants 8.
            IItemCounts baseline = new Counts(5);

            Assert.False(TriggerEvaluator.IsSatisfied(t, Vector3.zero, new Counts(7), null, baseline));
            Assert.True(TriggerEvaluator.IsSatisfied(t, Vector3.zero, new Counts(8), null, baseline));
        }

        [Fact]
        public void RelativeDoesNotFireImmediatelyWhenAlreadyHolding()
        {
            // The whole point: an absolute "have 3 rope" fires the instant a
            // segment starts if you already carry 3. The relative form must
            // not.
            Trigger relative;
            TriggerParser.Parse("item 78 >= +3", out relative);

            Trigger absolute;
            TriggerParser.Parse("item 78 >= 3", out absolute);

            IItemCounts holding = new Counts(3);

            Assert.True(TriggerEvaluator.IsSatisfied(absolute, Vector3.zero, holding, null, holding));
            Assert.False(TriggerEvaluator.IsSatisfied(relative, Vector3.zero, holding, null, holding));
        }

        [Fact]
        public void MissingBaselineTreatsItAsZero()
        {
            Trigger t;
            TriggerParser.Parse("item 78 >= +3", out t);

            Assert.True(TriggerEvaluator.IsSatisfied(t, Vector3.zero, new Counts(3), null, null));
            Assert.False(TriggerEvaluator.IsSatisfied(t, Vector3.zero, new Counts(2), null, null));
        }

        [Fact]
        public void RelativeWorksWithEveryOperator()
        {
            IItemCounts baseline = new Counts(4);

            Trigger atMost;
            TriggerParser.Parse("item 78 <= +0", out atMost);
            Assert.True(TriggerEvaluator.IsSatisfied(atMost, Vector3.zero, new Counts(4), null, baseline));
            Assert.False(TriggerEvaluator.IsSatisfied(atMost, Vector3.zero, new Counts(5), null, baseline));

            Trigger exactly;
            TriggerParser.Parse("item 78 == +2", out exactly);
            Assert.True(TriggerEvaluator.IsSatisfied(exactly, Vector3.zero, new Counts(6), null, baseline));
        }

        [Fact]
        public void PlusAloneIsNotAnAmount()
        {
            Trigger t;
            Assert.False(TriggerParser.Parse("item 78 >= +", out t));
        }
    }
}
