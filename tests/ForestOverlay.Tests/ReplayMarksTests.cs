using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // Replays that show what happened: the run's event and building tracks
    // (`e|` / `b|` lines), which game events become markers, what shows at
    // a replay time and the labels nearest the player.
    // ------------------------------------------------------------------
    public class ReplayMarksTests
    {
        private static RunRecorder Running()
        {
            var r = new RunRecorder();
            r.Arm(Vector3.zero, "s-test");
            r.ForceStart(Vector3.zero);
            return r;
        }

        private static RunBuilding Building(float t, string state, string kind, Vector3 p)
        {
            RunBuilding b = default(RunBuilding);
            b.T = t; b.State = state; b.Kind = kind; b.P = p;
            b.Euler = new Vector3(0f, 90f, 0f);
            b.Center = new Vector3(0f, 1.5f, 0f);
            b.Size = new Vector3(4f, 3f, 0.6f);
            return b;
        }

        [Fact]
        public void RecorderStampsEventsAndBuildingsOnlyWhileRunning()
        {
            var idle = new RunRecorder();
            Assert.False(idle.RecordEvent(RunAudit.Crafted, "Bomb", Vector3.zero));

            RunRecorder r = Running();
            r.Tick(Vector3.zero, 0f, 1f);
            Assert.True(r.RecordEvent(RunAudit.Crafted, "Bomb", new Vector3(1f, 2f, 3f)));
            Assert.True(r.RecordBuilding(Building(99f, RunBuilding.Placed, "Wall", Vector3.zero)));
            r.Tick(Vector3.zero, 0f, 1f);
            Assert.True(r.RecordEvent(RunAudit.Used, null, Vector3.zero));
            Attempt a = r.Finish();

            Assert.Equal(2, a.Events.Count);
            Assert.Equal(1f, a.Events[0].T, 3);
            Assert.Equal("Bomb", a.Events[0].Detail);
            Assert.Equal("", a.Events[1].Detail);
            Assert.Equal(2f, a.Events[1].T, 3);
            Assert.Single(a.Buildings);
            Assert.Equal(1f, a.Buildings[0].T, 3);   // the recorder's time, not the caller's
        }

        [Fact]
        public void EventsStopAtTheCap()
        {
            RunRecorder r = Running();
            for (int i = 0; i < RunRecorder.MaxEvents; i++) Assert.True(r.RecordEvent(RunAudit.Tree, null, Vector3.zero));
            Assert.False(r.RecordEvent(RunAudit.Tree, null, Vector3.zero));
            Assert.Equal(RunRecorder.MaxEvents, r.Finish().Events.Count);
        }

        [Fact]
        public void TracksRoundTrip()
        {
            RunRecorder r = Running();
            r.Tick(new Vector3(1f, 0f, 0f), 2f, 0.5f);
            r.RecordEvent(RunAudit.Crafted, "Bomb | timed", new Vector3(1.5f, -2f, 3.25f));
            r.RecordEvent("cave-enter", "Cave 6", new Vector3(4f, 5f, 6f));
            r.RecordBuilding(Building(0f, RunBuilding.Placed, "LogCabin", new Vector3(10f, 20f, 30f)));
            r.Tick(new Vector3(2f, 0f, 0f), 2f, 0.5f);
            Attempt a = r.Finish();

            Attempt back = AttemptFormat.Parse(AttemptFormat.Write(a).Split('\n'));
            Assert.Equal(2, back.Events.Count);
            Assert.Equal(RunAudit.Crafted, back.Events[0].Kind);
            Assert.Equal("Bomb   timed", back.Events[0].Detail);   // a separator never splits a line
            Assert.Equal(-2f, back.Events[0].P.y, 3);
            Assert.Equal("Cave 6", back.Events[1].Detail);
            Assert.Single(back.Buildings);
            RunBuilding b = back.Buildings[0];
            Assert.Equal(RunBuilding.Placed, b.State);
            Assert.Equal("LogCabin", b.Kind);
            Assert.Equal(30f, b.P.z, 3);
            Assert.Equal(90f, b.Euler.y, 3);
            Assert.Equal(1.5f, b.Center.y, 3);
            Assert.Equal(0.6f, b.Size.z, 3);
            Assert.Equal(0.5f, b.T, 3);
        }

        [Fact]
        public void OldRunsParseWithEmptyTracks()
        {
            string old = "anchor|s-x\nduration|2.000\ns|0.000|0|0|0|0\ns|2.000|1|0|0|1\ni|0.000|Soda:1\n";
            Attempt a = AttemptFormat.Parse(old.Split('\n'));
            Assert.NotNull(a);
            Assert.Empty(a.Events);
            Assert.Empty(a.Buildings);
            Assert.Single(a.Items);
        }

        [Fact]
        public void MalformedTrackLinesAreSkipped()
        {
            string text = "anchor|s-x\ns|0.000|0|0|0|0\ne|1.0|crafted\ne|1.0||1|2|3|x\nb|1.0|built|Wall|1|2|3\n";
            Attempt a = AttemptFormat.Parse(text.Split('\n'));
            Assert.Empty(a.Events);
            Assert.Empty(a.Buildings);
        }

        [Fact]
        public void RunnerLineGoesBeforeTheTracks()
        {
            string text = "anchor|s-x\ne|1.000|crafted|0|0|0|Bomb\ns|0.000|0|0|0|0\n";
            string named = AttemptFormat.WithRunner(text, "abc", "Maks");
            Assert.Equal("anchor|s-x\nrunner|abc|Maks\ne|1.000|crafted|0|0|0|Bomb\ns|0.000|0|0|0|0\n", named);
        }

        [Theory]
        [InlineData("crafted", "Bomb", "crafted", "Bomb")]
        [InlineData("kill-enemy", "mutant_male", "kill", "mutant_male")]
        [InlineData("kill-animal", "deer", "animal", "deer")]
        [InlineData("hit-by-enemy", "regular", "hit", "regular")]
        [InlineData("tree-cut", null, "tree", "")]
        [InlineData("slept", null, "sleep", "")]
        [InlineData("ride-start", "zipline", "ride-start", "zipline")]
        [InlineData("endgame-area-enter", null, "endgame", "entered the endgame area")]
        [InlineData("cave-enter", "Cave 6", "cave-enter", "Cave 6")]
        [InlineData("clothing-12", null, "clothing", "12")]
        [InlineData("clothing-12", "Snow camo", "clothing", "Snow camo")]
        [InlineData("keycard-door", "the vault door", "keycard-door", "the vault door")]
        [InlineData("megan-pickup", null, "megan-pickup", "")]
        public void GameEventsBecomeMarkers(string name, string detail, string kind, string text)
        {
            string got;
            Assert.Equal(kind, ReplayMarks.KindFor(name, detail, out got));
            Assert.Equal(text, got);
        }

        [Theory]
        [InlineData("crafted-bomb-timed")]   // the specific copy: its general one carries it
        [InlineData("cave-enter-cave06")]
        [InlineData("passenger-3")]
        [InlineData("moving")]
        [InlineData("hold-interact")]
        [InlineData("first-input")]
        [InlineData("endgame-cutscene")]
        [InlineData("vault-door")]
        [InlineData("zipline-start")]
        [InlineData("")]
        [InlineData(null)]
        public void OtherEventsAreNotMarkers(string name)
        {
            string text;
            Assert.Null(ReplayMarks.KindFor(name, "x", out text));
        }

        [Fact]
        public void LabelsUseTheAuditWordsAndAreCut()
        {
            RunEvent e = default(RunEvent);
            e.Kind = RunAudit.Crafted; e.Detail = "Bomb";
            Assert.Equal("Crafted: Bomb", ReplayMarks.Label(e));
            e.Detail = "";
            Assert.Equal("Crafted", ReplayMarks.Label(e));
            e.Detail = new string('x', 100);
            string cut = ReplayMarks.Label(e);
            Assert.Equal(ReplayMarks.LabelChars, cut.Length);
            Assert.EndsWith("...", cut);
        }

        [Fact]
        public void UpToCountsEventsAtOrBeforeT()
        {
            var ev = new List<RunEvent>();
            foreach (float t in new[] { 1f, 2f, 2f, 5f }) { RunEvent e = default(RunEvent); e.T = t; ev.Add(e); }
            Assert.Equal(0, ReplayMarks.UpTo(ev, 0.5f));
            Assert.Equal(1, ReplayMarks.UpTo(ev, 1f));
            Assert.Equal(3, ReplayMarks.UpTo(ev, 2f));
            Assert.Equal(3, ReplayMarks.UpTo(ev, 4.9f));
            Assert.Equal(4, ReplayMarks.UpTo(ev, float.PositiveInfinity));
            Assert.Equal(0, ReplayMarks.UpTo(null, 3f));
        }

        [Fact]
        public void APlacedBlueprintShowsUntilItIsFinishedThere()
        {
            var b = new List<RunBuilding>
            {
                Building(1f, RunBuilding.Placed, "Wall", new Vector3(0f, 0f, 0f)),
                Building(2f, RunBuilding.Placed, "Wall", new Vector3(10f, 0f, 0f)),   // never finished
                Building(3f, RunBuilding.Built, "Wall", new Vector3(0.5f, 0f, 0f)),   // the first one, finished
                Building(4f, RunBuilding.Built, "Fire", new Vector3(10f, 0f, 0f)),    // another kind: not the second
            };
            float[] until = ReplayMarks.Until(b);
            Assert.Equal(3f, until[0]);
            Assert.True(float.IsPositiveInfinity(until[1]));
            Assert.True(float.IsPositiveInfinity(until[2]));

            Assert.False(ReplayMarks.Shows(b[0], until[0], 0.5f));   // not placed yet
            Assert.True(ReplayMarks.Shows(b[0], until[0], 2f));      // the blueprint
            Assert.False(ReplayMarks.Shows(b[0], until[0], 3f));     // replaced by the finished one
            Assert.True(ReplayMarks.Shows(b[2], until[2], 3f));
            Assert.True(ReplayMarks.Shows(b[1], until[1], 100f));

            float all = ReplayMarks.ShownUpTo(false, 2f);
            Assert.True(ReplayMarks.Shows(b[2], until[2], all));
            Assert.False(ReplayMarks.Shows(b[0], until[0], all));    // idle: the end state
            Assert.Equal(2f, ReplayMarks.ShownUpTo(true, 2f));
        }

        [Fact]
        public void NearestPicksTheClosestWithinTheRadius()
        {
            var ev = new List<RunEvent>();
            foreach (float x in new[] { 30f, 5f, 1f, 20f, 3f, 2f })
            {
                RunEvent e = default(RunEvent);
                e.P = new Vector3(x, 0f, 0f);
                ev.Add(e);
            }
            int[] idx = new int[3];
            float[] dist = new float[3];
            int n = ReplayMarks.Nearest(ev, ev.Count, Vector3.zero, 25f, idx, dist);
            Assert.Equal(3, n);
            Assert.Equal(new[] { 2, 5, 4 }, idx);   // 1 m, 2 m, 3 m

            // Only the first `count` events (those shown so far).
            n = ReplayMarks.Nearest(ev, 2, Vector3.zero, 25f, idx, dist);
            Assert.Equal(1, n);
            Assert.Equal(1, idx[0]);

            Assert.Equal(0, ReplayMarks.Nearest(ev, ev.Count, new Vector3(500f, 0f, 0f), 25f, idx, dist));
        }

        [Fact]
        public void DefaultSizeByKind()
        {
            Assert.Equal(new Vector3(7f, 5f, 7f), ReplayMarks.DefaultSize("LogCabin"));
            Assert.Equal(new Vector3(4f, 3f, 0.6f), ReplayMarks.DefaultSize("WallDefensive"));
            Assert.Equal(new Vector3(2f, 2f, 2f), ReplayMarks.DefaultSize("SomethingNew"));
            Assert.Equal(new Vector3(2f, 2f, 2f), ReplayMarks.DefaultSize(null));
        }
    }
}
