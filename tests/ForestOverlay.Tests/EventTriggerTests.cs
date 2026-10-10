using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // PracticeRunModule evaluates triggers once per frame with no event, or
    // once per event fired since the last frame. These pin down that an
    // event - an instant, not a state - fires a trigger exactly once.
    public class EventTriggerTests
    {
        private static Trigger Event(string name)
        {
            Trigger t = new Trigger();
            t.Kind = TriggerKind.Event;
            t.EventName = name;
            return t;
        }

        [Fact]
        public void AnEventStartTriggerStartsOnTheEventFrameOnly()
        {
            Trigger start = Event("timmy-pickup");
            TriggerState s = new TriggerState();

            // Armed: first frame primes.
            Assert.False(TriggerEvaluator.Crossed(start, ref s, Vector3.zero, null, null, null));
            Assert.False(TriggerEvaluator.Crossed(start, ref s, Vector3.zero, null, "megan-pickup", null));
            Assert.True(TriggerEvaluator.Crossed(start, ref s, Vector3.zero, null, "timmy-pickup", null));
        }

        [Fact]
        public void AnEventCheckpointFiresOncePerOccurrence()
        {
            Trigger cp = Event("keycard-door");
            TriggerState s = new TriggerState();

            Assert.False(TriggerEvaluator.Fired(cp, ref s, Vector3.zero, null, null, null));          // prime
            Assert.True(TriggerEvaluator.Fired(cp, ref s, Vector3.zero, null, "keycard-door", null));
            Assert.False(TriggerEvaluator.Fired(cp, ref s, Vector3.zero, null, null, null));          // next frame
            Assert.True(TriggerEvaluator.Fired(cp, ref s, Vector3.zero, null, "keycard-door", null)); // next door
        }

        [Fact]
        public void TwoEventsInOneFrameEachGetTheirOwnPass()
        {
            // endgame-cutscene and keycard-door arrive in the same frame;
            // the checkpoint waiting on the second must still fire.
            Trigger cp = Event("keycard-door");
            TriggerState s = new TriggerState();
            TriggerEvaluator.Fired(cp, ref s, Vector3.zero, null, null, null);

            Assert.False(TriggerEvaluator.Fired(cp, ref s, Vector3.zero, null, "endgame-cutscene", null));
            Assert.True(TriggerEvaluator.Fired(cp, ref s, Vector3.zero, null, "keycard-door", null));
        }

        [Fact]
        public void EventNamesMatchIgnoringCase()
        {
            Trigger t = Event("Game-End");
            Assert.True(TriggerEvaluator.IsSatisfied(t, Vector3.zero, null, "game-end", null));
        }

        // The velocity start is gone (T-0282): a stored `moving` reads as
        // `first-input`, and the trigger's text (so its route fingerprint)
        // changes with it - old times retire (author 2026-10-10).
        [Theory]
        [InlineData("event moving", "event first-input")]
        [InlineData("event MOVING", "event first-input")]
        [InlineData("event hold-interact|moving", "event hold-interact|first-input")]
        [InlineData("event moving|first-input", "event first-input")]
        [InlineData("event hold-interact", "event hold-interact")]
        [InlineData("event moving-platform", "event moving-platform")]
        public void AStoredMovingStartReadsAsFirstInput(string stored, string read)
        {
            Trigger t;
            Assert.True(TriggerParser.Parse(stored, out t));
            Assert.Equal(read, TriggerParser.Write(t));
        }

        [Fact]
        public void TheMigratedStartRetiresOldTimes()
        {
            // Old attempts carry the fingerprint of the `moving` text they
            // were timed under; the same route read today has another one.
            Segment old = new Segment(), read = new Segment();
            old.Start = Event("moving");                          // as it was before T-0282
            TriggerParser.Parse("event moving", out read.Start);  // the same file, read now
            TriggerParser.Parse("zone 0 0 0 5", out old.End);
            TriggerParser.Parse("zone 0 0 0 5", out read.End);
            Assert.Equal("first-input", read.Start.EventName);
            Assert.NotEqual(old.RouteFingerprint(), read.RouteFingerprint());
        }
    }
}
