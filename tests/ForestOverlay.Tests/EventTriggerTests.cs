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
    }
}
