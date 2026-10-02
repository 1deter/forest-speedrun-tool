using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // Run mode's report (phase 1): plain-words findings a verifier reads,
    // a verdict that is clean only when every check passed.
    // ------------------------------------------------------------------
    public class RunReportTests
    {
        private static RunReport Clean()
        {
            RunReport r = new RunReport();
            r.Attempt = 2;
            r.Started = "Normal";
            r.GameHash = RunReport.KnownGameHashes[0];
            return r;
        }

        [Fact]
        public void KnownGameAndNothingElseIsClean()
        {
            RunReport r = Clean();
            Assert.True(r.Clean);
            Assert.Equal("clean (Steam game, no other mods, no cheats)", r.Summary());
            foreach (string line in r.Findings()) Assert.StartsWith("OK  ", line);
        }

        [Fact]
        public void HashMatchIgnoresCase()
        {
            Assert.True(RunReport.IsKnownGame(RunReport.KnownGameHashes[0].ToUpperInvariant()));
            Assert.False(RunReport.IsKnownGame(""));
            Assert.False(RunReport.IsKnownGame(null));
        }

        [Fact]
        public void ChangedGameIsNamedInPlainWords()
        {
            RunReport r = Clean();
            r.GameHash = "0123456789abcdef0123";
            Assert.False(r.Clean);
            Assert.Contains("NOT OK  The game's code is not the Steam game's - it was changed or is another version (0123456789ab).", r.Findings());
            Assert.StartsWith("NOT VALID: The game's code is not the Steam game's", r.Summary());
        }

        [Fact]
        public void PendingHashIsNotAVerdict()
        {
            RunReport r = Clean();
            r.GameHash = "";
            Assert.False(r.Clean);
            Assert.Equal("checks pending", r.Summary());
            Assert.Contains("Note  The game's files are still being checked.", r.Findings());
        }

        [Fact]
        public void EachProblemIsItsOwnLine()
        {
            RunReport r = Clean();
            r.OtherPlugins.Add("Megan Helper 1.0 (megan.dll)");
            r.ForeignPatches.Add("BossAttacks.Choose (by someone.megan)");
            r.Cheats.Add("GodMode");
            r.Flags.Add("the test bridge is on");
            var f = r.Findings();
            Assert.Contains("NOT OK  Another mod is loaded: Megan Helper 1.0 (megan.dll).", f);
            Assert.Contains("NOT OK  Another mod changes the game's code: BossAttacks.Choose (by someone.megan).", f);
            Assert.Contains("NOT OK  A game cheat is on: GodMode.", f);
            Assert.Contains("NOT OK  The test bridge is on.", f);
            Assert.DoesNotContain("OK  No other mods loaded.", f);
            Assert.Equal("NOT VALID: Another mod is loaded: Megan Helper 1.0 (megan.dll); Another mod changes the game's code: " +
                         "BossAttacks.Choose (by someone.megan); A game cheat is on: GodMode; The test bridge is on", r.Summary());
        }

        [Fact]
        public void PracticeBeforeIsANoteNotAProblem()
        {
            RunReport r = Clean();
            r.PracticeBefore = "savestate restore (in place)";
            Assert.True(r.Clean);
            Assert.Contains("Note  Practice was used before this attempt (savestate restore (in place)); the new game started clean.", r.Findings());
        }

        [Fact]
        public void FileHasKeysVerdictAndFindings()
        {
            RunReport r = Clean();
            r.OtherPatchers.Add("Cheat.Patcher.dll");
            string text = r.Format();
            Assert.StartsWith("[runreport]\nattempt = 2\nstarted = Normal\n", text);
            Assert.Contains("verdict = not valid\n", text);
            Assert.Contains("otherpatcher = Cheat.Patcher.dll\n", text);
            Assert.Contains("# NOT OK  Another BepInEx patcher is installed: Cheat.Patcher.dll.\n", text);
        }
    }
}
