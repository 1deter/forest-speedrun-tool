using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class StartStripTests
    {
        private const RunRecorder.RunState Idle = RunRecorder.RunState.Idle;

        [Fact]
        public void NothingSelectedSaysWhatToDo()
        {
            Assert.Equal("Select a spot in the list, then Restart.",
                         StartStrip.Text(null, false, false, "", false, false, Idle, false, false));
        }

        [Fact]
        public void NoSpawnComesFirst()
        {
            Assert.Equal("'a' has no spawn point.", StartStrip.Text("a", false, true, "Any%", true, true, Idle, false, false));
        }

        [Fact]
        public void RunSpotStartsARunOnlyWithAStartState()
        {
            Assert.Equal("'a': Restart starts a run (Any%, Full load).", StartStrip.Text("a", true, true, "Any%", true, false, Idle, false, false));
            // Without a start state its Restart is a plain spot's (PracticeModule.Restart).
            Assert.Equal("'a': idle - Restart to arm.", StartStrip.Text("a", true, true, "Any%", false, true, Idle, false, false));
            // Its run in progress says so.
            Assert.Equal("'a': running.", StartStrip.Text("a", true, true, "Any%", true, false, RunRecorder.RunState.Running, false, false));
        }

        [Fact]
        public void PlainSpotShowsItsNameAlone()
        {
            Assert.Equal("'a'", StartStrip.Text("a", true, false, "", true, true, Idle, false, false));
        }

        [Fact]
        public void TimedSegmentStates()
        {
            Assert.Equal("'a': idle - Restart turns practice mode on and arms it.", StartStrip.Text("a", true, true, "", false, false, Idle, false, false));
            Assert.Equal("'a': idle - Restart to arm.", StartStrip.Text("a", true, true, "", false, true, Idle, false, false));
            Assert.Equal("'a': armed - the timer starts at its start.", StartStrip.Text("a", true, true, "", true, true, RunRecorder.RunState.Armed, false, false));
            Assert.Equal("'a': running.", StartStrip.Text("a", true, true, "", true, true, RunRecorder.RunState.Running, false, false));
        }

        [Fact]
        public void AGreyedOrRefusedRestartSaysWhy()
        {
            Assert.Equal("'a': a savestate action is still running.", StartStrip.Text("a", true, true, "", true, true, Idle, true, false));
            Assert.Equal("'a': run mode locks Restart.", StartStrip.Text("a", true, true, "", true, false, Idle, false, true));
            Assert.Equal("'a': run mode locks Restart.", StartStrip.Text("a", true, false, "", true, false, Idle, false, true));
            // A run spot's Restart is the one run mode allows.
            Assert.Equal("'a': Restart starts a run (Any%, Full load).", StartStrip.Text("a", true, true, "Any%", true, false, Idle, false, true));
            // A run in progress outranks both.
            Assert.Equal("'a': running.", StartStrip.Text("a", true, true, "Any%", true, false, RunRecorder.RunState.Running, true, true));
        }

        [Fact]
        public void F7FollowsASelectionMadeAfterTheLastPlacement()
        {
            // Select A, nothing placed yet: A.
            Assert.True(StartStrip.FollowsSelection("A", true, null));
            // Go to row B with A still selected: B (the current spot), not A.
            Assert.False(StartStrip.FollowsSelection("A", true, "A"));
            // Then select C: C.
            Assert.True(StartStrip.FollowsSelection("C", true, "A"));
            // Restart C, then a Reload re-makes C under its id: still the current spot.
            Assert.False(StartStrip.FollowsSelection("C", true, "C"));
            // Nothing selected, or a selection with no spawn: the current spot.
            Assert.False(StartStrip.FollowsSelection(null, false, "A"));
            Assert.False(StartStrip.FollowsSelection("D", false, "A"));
        }

        [Fact]
        public void PracticeModeTurnsOnOnlyForATimedNonRunStartWhenOff()
        {
            Assert.True(StartStrip.TurnsPracticeOn(true, false, false));
            Assert.False(StartStrip.TurnsPracticeOn(true, false, true));    // already on
            Assert.False(StartStrip.TurnsPracticeOn(false, false, false));  // a plain spot
            Assert.False(StartStrip.TurnsPracticeOn(true, true, false));    // run mode times a run spot
        }
    }
}
