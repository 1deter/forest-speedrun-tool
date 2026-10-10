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
                         StartStrip.Text(null, false, false, "", false, false, Idle));
        }

        [Fact]
        public void NoSpawnComesFirst()
        {
            Assert.Equal("'a' has no spawn point.", StartStrip.Text("a", false, true, "Any%", true, true, Idle));
        }

        [Fact]
        public void RunSpotStartsARunOnlyWithAStartState()
        {
            Assert.Equal("'a': Restart starts a run (Any%, Full load).", StartStrip.Text("a", true, true, "Any%", true, false, Idle));
            // Without a start state its Restart is a plain spot's (PracticeModule.Restart).
            Assert.Equal("'a': idle - Restart to arm.", StartStrip.Text("a", true, true, "Any%", false, true, Idle));
            // Its run in progress says so.
            Assert.Equal("'a': running.", StartStrip.Text("a", true, true, "Any%", true, false, RunRecorder.RunState.Running));
        }

        [Fact]
        public void PlainSpot()
        {
            Assert.Equal("'a' is a spot, not a timed segment.", StartStrip.Text("a", true, false, "", true, true, Idle));
        }

        [Fact]
        public void TimedSegmentStates()
        {
            Assert.Equal("'a': idle - Restart turns practice mode on and arms it.", StartStrip.Text("a", true, true, "", false, false, Idle));
            Assert.Equal("'a': idle - Restart to arm.", StartStrip.Text("a", true, true, "", false, true, Idle));
            Assert.Equal("'a': armed - the timer starts at its start.", StartStrip.Text("a", true, true, "", true, true, RunRecorder.RunState.Armed));
            Assert.Equal("'a': running.", StartStrip.Text("a", true, true, "", true, true, RunRecorder.RunState.Running));
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
