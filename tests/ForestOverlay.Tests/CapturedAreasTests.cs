using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // Whether a capture had the endgame lab (Game/EndgameLoader loads it
    // on a restore). The lines are real `areas` headers from the log.
    // ------------------------------------------------------------------
    public class CapturedAreasTests
    {
        // Slot 1, captured on the surface after a tp out of the lab
        // (2026-10-04): endgame_animPrefabs lingered, the lab was gone.
        private const string SurfaceAfterTp =
            "caves no, endgame no, overlook no | scenes: ForestMain_v08, MainSceneGreebles, MainSceneWorldStorySpots, endgame_animPrefabs | streamed: MainSceneWorldStorySpots loaded";

        // Slot 1, captured inside the lab.
        private const string InLab =
            "caves no, endgame yes, overlook no | scenes: ForestMain_v08, MainSceneGreebles, MainSceneWorldStorySpots, endgame_animPrefabs, endgame_streaming | streamed: MainSceneWorldStorySpots loaded";

        // The vault door's own load caught mid-way (v0.24.82).
        private const string VaultDoorOpening =
            "caves no, endgame yes, overlook no | scenes: ForestMain_v08, MainSceneGreebles, MainSceneWorldStorySpots, endgame_animPrefabs | streamed: MainSceneWorldStorySpots loaded";

        private const string Surface =
            "caves no, endgame no, overlook no | scenes: ForestMain_v08, MainSceneGreebles, MainSceneWorldStorySpots | streamed: MainSceneWorldStorySpots loaded";

        [Fact]
        public void ALingeringAnimSceneIsNotTheLab()
        {
            Assert.False(CapturedAreas.HadEndgame(SurfaceAfterTp));
            Assert.False(CapturedAreas.ScenesToWaitFor(SurfaceAfterTp).Contains(CapturedAreas.AnimScene));
            Assert.True(CapturedAreas.ScenesToWaitFor(SurfaceAfterTp).Contains("ForestMain_v08"));
        }

        [Fact]
        public void TheLabCounts()
        {
            Assert.True(CapturedAreas.HadEndgame(InLab));
            Assert.True(CapturedAreas.ScenesToWaitFor(InLab).Contains(CapturedAreas.StreamingScene));
            Assert.True(CapturedAreas.ScenesToWaitFor(InLab).Contains(CapturedAreas.AnimScene));
        }

        [Fact]
        public void TheVaultDoorsLoadCountsWithTheEndgameFlag()
        {
            Assert.True(CapturedAreas.HadEndgame(VaultDoorOpening));
            Assert.True(CapturedAreas.ScenesToWaitFor(VaultDoorOpening).Contains(CapturedAreas.AnimScene));
        }

        [Fact]
        public void TheLabStillLoadingCounts()
        {
            string line = "caves no, endgame yes, overlook no | scenes: ForestMain_v08, endgame_animPrefabs, endgame_streaming (loading) | streamed: (none)";
            Assert.True(CapturedAreas.HadEndgame(line));
            // A scene still loading at capture is not waited for.
            Assert.False(CapturedAreas.ScenesToWaitFor(line).Contains(CapturedAreas.StreamingScene));
            Assert.True(CapturedAreas.Scenes(line, true).Contains(CapturedAreas.StreamingScene));
        }

        [Fact]
        public void NoEndgameAndOldFiles()
        {
            Assert.False(CapturedAreas.HadEndgame(Surface));
            Assert.False(CapturedAreas.HadEndgame(""));
            Assert.False(CapturedAreas.HadEndgame(null));
            Assert.Empty(CapturedAreas.ScenesToWaitFor(""));
            // An unreadable flag never makes the anim scene alone count.
            Assert.False(CapturedAreas.HadEndgame("caves ?, endgame ?, overlook ? | scenes: ForestMain_v08, endgame_animPrefabs | streamed: (none bound)"));
        }

        // Cave 6's body slide spot, captured in the cave (2026-10-09).
        private const string InCave6 =
            "caves yes, endgame no, overlook no | scenes: CaveProps_Streaming, Cave_06_Props_Streaming, ForestMain_v08, endgame_animPrefabs | streamed: MainSceneWorldStorySpots unloaded";

        [Fact]
        public void AnOutsideCaptureRestoredInTheEndgameLeavesIt()
        {
            // T-0075: restarted from the lab, the flag stayed and Cave 6's
            // props scene never loaded.
            Assert.True(CapturedAreas.ShouldLeaveEndgame(InCave6, true));
            Assert.True(CapturedAreas.ShouldLeaveEndgame(Surface, true));
            Assert.True(CapturedAreas.ShouldLeaveEndgame(SurfaceAfterTp, true));
            // Already out, or captured in the endgame: nothing to do.
            Assert.False(CapturedAreas.ShouldLeaveEndgame(InCave6, false));
            Assert.False(CapturedAreas.ShouldLeaveEndgame(InLab, true));
            Assert.False(CapturedAreas.ShouldLeaveEndgame(VaultDoorOpening, true));
            // No header (old files, a slot reload) or an unreadable flag.
            Assert.False(CapturedAreas.ShouldLeaveEndgame("", true));
            Assert.False(CapturedAreas.ShouldLeaveEndgame(null, true));
            Assert.False(CapturedAreas.ShouldLeaveEndgame("caves ?, endgame ?, overlook ? | scenes: ForestMain_v08", true));
        }

        [Fact]
        public void FlagsAreRead()
        {
            Assert.Equal(true, CapturedAreas.Flag(InLab, "endgame"));
            Assert.Equal(false, CapturedAreas.Flag(InLab, "caves"));
            Assert.Null(CapturedAreas.Flag(InLab, "nothing"));
            Assert.Null(CapturedAreas.Flag("area report failed: x", "endgame"));
        }
    }
}
