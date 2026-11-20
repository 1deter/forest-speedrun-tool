using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // One frame of the run timer (Data/RunTimer.Step, T-0467 step 4): the
    // order Modules/RunTimerModule hands a frame to the practice runs in -
    // every game event before the occurrence gate, one pass per event, the
    // finish before the next event is read, an autosplit once a frame.
    // ------------------------------------------------------------------
    public class RunTimerStepTests
    {
        private static readonly Vector3 StartPos = new Vector3(0f, 0f, 0f);
        private static readonly Vector3 Away = new Vector3(20f, 0f, 0f);

        private sealed class Events : ITimerEvents
        {
            private readonly List<string> _names = new List<string>();
            private readonly List<bool> _companions = new List<bool>();

            public Events Add(string name, bool companion = false)
            {
                _names.Add(name);
                _companions.Add(companion);
                return this;
            }

            public int Count { get { return _names.Count; } }
            public string NameAt(int i) { return _names[i]; }
            public bool CompanionAt(int i) { return _companions[i]; }
        }

        /// Writes down what it is told; finishes the run on Finished, as
        /// the practice runs do.
        private sealed class Sink : ITimerSink
        {
            public readonly List<string> Said = new List<string>();
            public RunTimer Timer;

            public void GameEvent(int index) { Said.Add("event " + index); }
            public void AutoSplitting(string why) { Said.Add("autosplit " + why); }

            public void Stepped(TimerStep step)
            {
                Said.Add(step.ToString());
                if (step == TimerStep.Finished) Timer.Finish();
            }
        }

        private static Trigger Zone(Vector3 p, float r)
        {
            Trigger t = new Trigger();
            t.Kind = TriggerKind.Zone;
            t.Position = p;
            t.Radius = r;
            return t;
        }

        private static Trigger Event(string name)
        {
            Trigger t = new Trigger();
            t.Kind = TriggerKind.Event;
            t.EventName = name;
            return t;
        }

        private static Segment Seg(params string[] checkpointEvents)
        {
            Segment s = new Segment();
            s.Id = "seg-step";
            s.Name = "Step";
            s.Start = Zone(StartPos, 5f);
            for (int i = 0; i < checkpointEvents.Length; i++) s.Checkpoints.Add(Event(checkpointEvents[i]));
            s.End = Event("end");
            return s;
        }

        /// Armed, then stepped out of the start zone: running, every event
        /// so far read.
        private static RunTimer Running(Segment s, Events ev, Sink sink, out int seen)
        {
            RunTimer t = new RunTimer();
            sink.Timer = t;
            t.Prepare(s);
            t.Arm(s, StartPos, null, s.RouteFingerprint());
            t.ResetRows(s.Checkpoints.Count + 1);
            seen = t.Step(ev, ev.Count, StartPos, null, 1, sink);
            seen = t.Step(ev, seen, Away, null, 2, sink);
            // One more frame, as the game evaluates every one (the end's
            // watch is primed on its first).
            seen = t.Step(ev, seen, Away, null, 2, sink);
            Assert.True(t.Running);
            Assert.Equal(new List<string> { "Started" }, sink.Said);
            sink.Said.Clear();
            return t;
        }

        [Fact]
        public void AFrameWithNoEventIsOnePassWithNone()
        {
            Events ev = new Events().Add("old");
            Sink sink = new Sink();
            RunTimer t = new RunTimer();
            sink.Timer = t;
            Segment s = Seg();
            t.Prepare(s);
            t.Arm(s, StartPos, null, "");
            Assert.Equal(1, t.Step(ev, 1, StartPos, null, 1, sink));
            Assert.Empty(sink.Said);   // primed, not fired
            Assert.Equal(1, t.Step(ev, 1, Away, null, 2, sink));
            Assert.Equal(new List<string> { "Started" }, sink.Said);
        }

        [Fact]
        public void EveryEventIsSeenBeforeTheGateAndSplitsInOrder()
        {
            Events ev = new Events();
            Sink sink = new Sink();
            int seen;
            RunTimer t = Running(Seg("cave-enter-cave06", "cave-enter"), ev, sink, out seen);

            // One occurrence under two names: the first splits, the companion
            // is skipped by the gate - but still read.
            ev.Add("cave-enter-cave06").Add("cave-enter", true);
            seen = t.Step(ev, seen, Away, null, 3, sink);
            Assert.Equal(2, seen);
            Assert.Equal(new List<string> { "event 0", "Split", "event 1" }, sink.Said);
            Assert.Single(t.Splits);

            // The next occurrence splits the second checkpoint.
            sink.Said.Clear();
            ev.Add("cave-enter");
            seen = t.Step(ev, seen, Away, null, 4, sink);
            Assert.Equal(new List<string> { "event 2", "Split" }, sink.Said);
            Assert.Equal(2, t.Splits.Count);
        }

        [Fact]
        public void TwoEventsInOneFrameBothCount()
        {
            Events ev = new Events();
            Sink sink = new Sink();
            int seen;
            RunTimer t = Running(Seg("a", "b"), ev, sink, out seen);
            ev.Add("a").Add("b");
            t.Step(ev, seen, Away, null, 3, sink);
            Assert.Equal(new List<string> { "event 0", "Split", "event 1", "Split" }, sink.Said);
        }

        [Fact]
        public void TheEndBeforeACheckpointIsBlocked()
        {
            Events ev = new Events();
            Sink sink = new Sink();
            int seen;
            RunTimer t = Running(Seg("a"), ev, sink, out seen);
            ev.Add("end");
            t.Step(ev, seen, Away, null, 3, sink);
            Assert.Equal(new List<string> { "event 0", "EndBlocked" }, sink.Said);
            Assert.True(t.Running);
        }

        [Fact]
        public void TheEventsAfterTheFinishMoveNothing()
        {
            Events ev = new Events();
            Sink sink = new Sink();
            int seen;
            RunTimer t = Running(Seg("a"), ev, sink, out seen);
            ev.Add("a").Add("end").Add("a").Add("end");
            Assert.Equal(4, t.Step(ev, seen, Away, null, 3, sink));
            Assert.Equal(new List<string> { "event 0", "Split", "event 1", "Finished", "event 2", "event 3" }, sink.Said);
            Assert.False(t.Running);
        }

        [Fact]
        public void AnAutoSplitActsOnceAFrame()
        {
            Events ev = new Events();
            Sink sink = new Sink();
            Segment s = Seg(Segment.AutoSplitEvent, Segment.AutoSplitEvent);
            s.AutoSplit.Add("cave-enter");
            int seen;
            RunTimer t = Running(s, ev, sink, out seen);

            ev.Add("cave-enter").Add("cave-enter");
            seen = t.Step(ev, seen, Away, null, 3, sink);
            Assert.Equal(new List<string> { "event 0", "autosplit cave-enter", "Split", "event 1" }, sink.Said);
            Assert.Single(t.Splits);

            sink.Said.Clear();
            ev.Add("cave-enter");
            t.Step(ev, seen, Away, null, 4, sink);
            Assert.Equal(new List<string> { "event 2", "autosplit cave-enter", "Split" }, sink.Said);
            Assert.Equal(2, t.Splits.Count);
        }

        [Fact]
        public void AnEventCountThatWentBackIsTakenAsRead()
        {
            Events ev = new Events().Add("x");
            Sink sink = new Sink();
            RunTimer t = new RunTimer();
            sink.Timer = t;
            Assert.Equal(1, t.Step(ev, 5, StartPos, null, 1, sink));
            Assert.Empty(sink.Said);
        }
    }
}
