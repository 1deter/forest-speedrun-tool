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
        public void ACheatThatIsAlsoAFlagIsSaidOnce()
        {
            RunReport r = Clean();
            r.Cheats.Add("GodMode");
            r.Flags.Add("a game cheat is on: GodMode");
            int n = 0;
            foreach (string line in r.Findings()) if (line == "NOT OK  A game cheat is on: GodMode.") n++;
            Assert.Equal(1, n);
        }

        [Fact]
        public void PracticeBeforeIsANoteNotAProblem()
        {
            RunReport r = Clean();
            r.PracticeBefore = "savestate restore (in place)";
            Assert.True(r.Clean);
            Assert.Contains("Note  Practice was used before this attempt (savestate restore (in place)); the attempt started clean.", r.Findings());
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

        [Fact]
        public void ParseReadsFormatBack()
        {
            RunReport r = Clean();
            r.AttemptId = "a-0123456789abcdef";
            r.PluginVersion = "0.24.217";
            r.OtherPlugins.Add("Other 1.0 (other.dll)");
            r.ForeignPatches.Add("PlayerStats.Update (by other.mod)");
            r.Cheats.Add("GodMode");
            r.Flags.Add("the test bridge is on");
            r.PracticeBefore = "Go";
            r.OwnPatchedMethods = 12;
            r.GameHash = "0123456789abcdef";
            r.TypeHashes.Add("PlayerStats 0123456789abcdef");
            r.TypeHashes.Add("TheForest.Utils.LocalPlayer fedcba9876543210");
            RunReport back = RunReport.Parse(r.Format());
            Assert.Equal(r.Format(), back.Format());
            Assert.Equal(r.Findings(), back.Findings());
            Assert.Equal(r.TypeHashes, back.TypeHashes);
        }

        // T-0295: a ModAPI session's flag goes through the report's flag lines.
        [Fact]
        public void ModApiFlagIsAFlagLineReadBack()
        {
            RunReport r = Clean();
            Assert.False(r.ModApi);
            r.Flags.Add(RunReport.ModApiFlag);
            Assert.True(r.ModApi);
            Assert.False(r.Clean);
            string text = r.Format();
            Assert.Contains("flag = ModAPI Detected\n", text);
            Assert.Contains("# NOT OK  ModAPI Detected.\n", text);
            Assert.True(RunReport.Parse(text).ModApi);
        }

        [Fact]
        public void HasModApiIsTheExactFlag()
        {
            Assert.True(RunReport.HasModApi(new[] { "the test bridge is on", "ModAPI Detected" }));
            Assert.False(RunReport.HasModApi(new[] { "practice action during the run: ModAPI Detected" }));
            Assert.False(RunReport.HasModApi(new string[0]));
            Assert.False(RunReport.HasModApi(null));
        }

        [Fact]
        public void LoadedKnownButFileDifferentIsOkWithANote()
        {
            RunReport r = Clean();
            r.GameFileHash = new string('a', 64);
            Assert.True(r.Clean);
            string text = r.Format();
            Assert.Contains("gamefile = " + r.GameFileHash + "\n", text);
            Assert.Contains("# Note  The game's file on disk is not the code that was loaded (aaaaaaaaaaaa).\n", text);
            Assert.Contains("# OK  The game's code is the unmodified Steam game.\n", text);
            RunReport back = RunReport.Parse(text);
            Assert.Equal(r.GameFileHash, back.GameFileHash);
            Assert.Equal("", back.GameFrom);
        }

        [Fact]
        public void LoadedUnknownIsNotOkEvenIfTheFileIsKnown()
        {
            RunReport r = Clean();
            r.GameHash = new string('b', 64);
            r.GameFileHash = RunReport.KnownGameHashes[0];
            Assert.False(r.Clean);
            Assert.StartsWith("NOT OK  The game's code is not the Steam game's", r.Findings()[0]);
        }

        [Fact]
        public void FallbackToTheFileIsANote()
        {
            RunReport r = Clean();
            r.GameFrom = "32-bit game";
            Assert.True(r.Clean);
            string text = r.Format();
            Assert.Contains("gamefrom = 32-bit game\n", text);
            Assert.DoesNotContain("gamefile =", text);
            Assert.Contains("# Note  The game's code was checked from its file, not from memory (32-bit game).\n", text);
            Assert.Equal("32-bit game", RunReport.Parse(text).GameFrom);
        }

        [Fact]
        public void OneLineKeepsAMessageOnOneLine()
        {
            Assert.Equal("a b  c", RunReport.OneLine("a b\r\nc\n"));
            Assert.Equal(120, RunReport.OneLine(new string('x', 500)).Length);
            Assert.Equal("", RunReport.OneLine(null));
            RunReport r = Clean();
            r.GameFrom = RunReport.OneLine("bad\nthing\n[runreport]");
            Assert.Equal("bad thing [runreport]", RunReport.Parse(r.Format()).GameFrom);
        }

        [Fact]
        public void OldReportsHaveNoNotes()
        {
            RunReport r = RunReport.Parse(Clean().Format());
            Assert.Equal("", r.GameFileHash);
            Assert.Equal("", r.GameFrom);
            foreach (string line in r.Findings()) Assert.StartsWith("OK  ", line);
        }

        private static byte[] Head(int lfanew)
        {
            byte[] h = new byte[64];
            h[0] = (byte)'M'; h[1] = (byte)'Z';
            h[60] = (byte)lfanew; h[61] = (byte)(lfanew >> 8); h[62] = (byte)(lfanew >> 16);
            return h;
        }

        [Fact]
        public void LoadedImagePlausibility()
        {
            byte[] pe = { (byte)'P', (byte)'E', 0, 0 };
            long len = 8231936;
            Assert.True(RunReport.PlausibleImage(Head(128), len, pe));
            Assert.False(RunReport.PlausibleImage(Head(128), 1000, pe));
            Assert.False(RunReport.PlausibleImage(Head(128), 300L * 1024 * 1024, pe));
            byte[] noMz = Head(128); noMz[0] = 0;
            Assert.False(RunReport.PlausibleImage(noMz, len, pe));
            Assert.False(RunReport.PlausibleImage(Head(128), len, new byte[] { (byte)'P', (byte)'E', 1, 0 }));
            Assert.False(RunReport.PlausibleImage(Head(0), len, pe));
            Assert.False(RunReport.PlausibleImage(Head(9000000), len, pe));
            Assert.False(RunReport.PlausibleImage(new byte[10], len, pe));
        }

        [Fact]
        public void ParseOfNothingIsPending()
        {
            RunReport r = RunReport.Parse("");
            Assert.Equal("", r.GameHash);
            Assert.Equal("Note  The game's files are still being checked.", r.Findings()[0]);
        }
    }
}
