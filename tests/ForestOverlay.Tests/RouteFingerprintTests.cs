using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // The route fingerprint separates "the same entry" from "the same
    // route". Attempts are keyed on the segment id so they can be compared
    // between players, but moving a start zone changes what the times mean
    // while leaving the id alone - so the fingerprint has to notice.
    // ------------------------------------------------------------------
    public class RouteFingerprintTests
    {
        private static Segment Route()
        {
            Segment s = new Segment();
            s.Id = "deter/route.test";
            TriggerParser.Parse("zone 0 0 0 3", out s.Start);
            TriggerParser.Parse("zone 100 0 0 5", out s.End);
            return s;
        }

        [Fact]
        public void IsStableForTheSameRoute()
        {
            Assert.Equal(Route().RouteFingerprint(), Route().RouteFingerprint());
        }

        [Fact]
        public void ChangesWhenTheStartZoneMoves()
        {
            Segment a = Route();
            string before = a.RouteFingerprint();

            TriggerParser.Parse("zone 10 0 0 3", out a.Start);

            Assert.NotEqual(before, a.RouteFingerprint());
        }

        [Fact]
        public void ChangesWhenTheRadiusChanges()
        {
            // A wider start zone is a different route: it starts the clock
            // somewhere else.
            Segment a = Route();
            string before = a.RouteFingerprint();

            TriggerParser.Parse("zone 0 0 0 9", out a.Start);

            Assert.NotEqual(before, a.RouteFingerprint());
        }

        [Fact]
        public void ChangesWhenACheckpointIsAdded()
        {
            Segment a = Route();
            string before = a.RouteFingerprint();

            Trigger c;
            TriggerParser.Parse("zone 50 0 0 4", out c);
            a.Checkpoints.Add(c);

            Assert.NotEqual(before, a.RouteFingerprint());
        }

        [Fact]
        public void IgnoresThingsThatDoNotAffectTheRun()
        {
            // Renaming an entry, recategorising it or moving where you
            // teleport in does not change what is being timed.
            Segment a = Route();
            string before = a.RouteFingerprint();

            a.Name = "Renamed";
            a.Category = "Elsewhere";
            a.Notes = "changed my mind";
            a.HasSpawn = true;
            a.SpawnPosition = new Vector3(999f, 999f, 999f);

            Assert.Equal(before, a.RouteFingerprint());
        }

        [Fact]
        public void CheckpointOrderMatters()
        {
            Trigger c1, c2;
            TriggerParser.Parse("zone 10 0 0 3", out c1);
            TriggerParser.Parse("zone 20 0 0 3", out c2);

            Segment a = Route();
            a.Checkpoints.Add(c1);
            a.Checkpoints.Add(c2);

            Segment b = Route();
            b.Checkpoints.Add(c2);
            b.Checkpoints.Add(c1);

            // Checkpoints fire in order, so a reordered route is a
            // different route.
            Assert.NotEqual(a.RouteFingerprint(), b.RouteFingerprint());
        }

        [Fact]
        public void SeparatorPreventsConcatenationCollisions()
        {
            // "ab" + "c" must not hash the same as "a" + "bc".
            Segment a = new Segment();
            TriggerParser.Parse("event ab", out a.Start);
            TriggerParser.Parse("event c", out a.End);

            Segment b = new Segment();
            TriggerParser.Parse("event a", out b.Start);
            TriggerParser.Parse("event bc", out b.End);

            Assert.NotEqual(a.RouteFingerprint(), b.RouteFingerprint());
        }
    }
}
