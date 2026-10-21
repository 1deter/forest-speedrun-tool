using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // Other mods beside the game (T-0308): the names, the HUD marker, the .run flag.
    public class ExternalModsTests
    {
        [Fact]
        public void NamesComeFromAllFourLists()
        {
            var into = new List<string>();
            ExternalMods.Collect(
                new[] { "Cool Cheats 1.2.3 (CoolCheats.dll)", "could not list the plugins: x" },
                new[] { "Patcher.dll" },
                new[] { "Helper.dll", "Patcher.dll" },
                new[] { "Player.Update (by com.other.mod)", "Foo.Bar (by com.other.mod)" }, into);
            Assert.Equal(new[] { "Cool Cheats", "Patcher.dll", "Helper.dll", "com.other.mod" }, into.ToArray());
        }

        [Fact]
        public void NothingExternalNamesNothing()
        {
            var into = new List<string>();
            ExternalMods.Collect(new string[0], new string[0], new string[0], new string[0], into);
            Assert.Empty(into);
            Assert.Equal("", ExternalMods.Join(into));
        }

        [Fact]
        public void JoinCapsTheNames()
        {
            Assert.Equal("a, b, c, d +2 more", ExternalMods.Join(new[] { "a", "b", "c", "d", "e", "f" }));
            Assert.Equal("a, b", ExternalMods.Join(new[] { "a", "b" }));
        }

        [Fact]
        public void FlagRoundTripsThroughTheFlagList()
        {
            Assert.Equal("A, B", ExternalMods.FromFlags(new[] { "x", ExternalMods.Flag("A, B") }));
            Assert.Equal("", ExternalMods.FromFlags(new[] { "ModAPI Detected" }));
            Assert.Equal(ExternalMods.FlagPrefix, RunReport.ModsFlagPrefix);
            Assert.Equal(ExternalMods.FlagPrefix, AttemptFormat.ModsFlagPrefix);
        }

        [Fact]
        public void ALaterLongerFlagWins()
        {
            var flags = new[] { ExternalMods.Flag("A"), "other", ExternalMods.Flag("A, B") };
            Assert.Equal("A, B", ExternalMods.FromFlags(flags));
            Assert.Equal("A, B", RunReport.ModsOf(flags));
        }

        [Fact]
        public void MarkerPutsModsUnderTheModApiFlag()
        {
            Assert.Equal("", HudLines.Marker(false, "", null, null));
            Assert.Equal("Mods: A", HudLines.Marker(false, "A", null, null));
            Assert.Equal("ModAPI\nMods: A", HudLines.Marker(true, "A", null, null));
            Assert.Equal("ModAPI\nMods: A\nteleport", HudLines.Marker(true, "A", null, "teleport: Cave 5 (top)"));
            Assert.Equal("RUN MODE - attempt 1", HudLines.Marker(true, "A", "RUN MODE - attempt 1", null));
        }

        private static Attempt OneSample()
        {
            var a = new Attempt { AnchorLabel = "s-1", Duration = 1f };
            RunSample s;
            s.T = 0f; s.P = new Vector3(1, 2, 3); s.Speed = 0f;
            a.Samples.Add(s);
            return a;
        }

        [Fact]
        public void RunFileRoundTripsAlongsideModApi()
        {
            Attempt a = OneSample();
            a.Mods = "A, B +1 more";
            string only = AttemptFormat.Write(a);
            Assert.Contains("flags|Mods Detected: A, B +1 more", only);
            Attempt back = AttemptFormat.Parse(only.Split((char)10));
            Assert.Equal("A, B +1 more", back.Mods);
            Assert.False(back.ModApi);

            a.ModApi = true;
            back = AttemptFormat.Parse(AttemptFormat.Write(a).Split((char)10));
            Assert.True(back.ModApi);
            Assert.Equal("A, B +1 more", back.Mods);
        }

        [Fact]
        public void OldRunsHaveNoMods()
        {
            Assert.Equal("", AttemptFormat.Parse(AttemptFormat.Write(OneSample()).Split((char)10)).Mods);
        }

        // ---- T-0414 F5-F9 ----

        [Fact]
        public void PatchOwnerReadsTheNewShapes()
        {
            Assert.Equal("redteam.cheat", ExternalMods.PatchOwner("ItemDatabase.ItemIdByName (by redteam.cheat)"));
            Assert.Equal("com.deter.forestoverlay.cheat",
                         ExternalMods.PatchOwner("ItemDatabase.ItemIdByName (by com.deter.forestoverlay.cheat, from RedTeamProbe)"));
            Assert.Equal("code with no file", ExternalMods.PatchOwner("ItemDatabase.ItemIdByName (a detour, not Harmony: code with no file)"));
            Assert.Equal("RedTeamProbe", ExternalMods.PatchOwner("ItemDatabase.ItemIdByName (a detour, not Harmony: RedTeamProbe)"));
            Assert.Equal("", ExternalMods.PatchOwner("could not list the detours: x"));
        }

        [Fact]
        public void TheKnownInMemoryFourAreSkippedOthersListed()
        {
            Assert.True(ExternalMods.IsKnownInMemory("Rewired_Windows"));
            Assert.True(ExternalMods.IsKnownInMemory("Rewired_Config_Manager"));
            Assert.True(ExternalMods.IsKnownInMemory("Rewired_Config_ManagerA"));
            Assert.True(ExternalMods.IsKnownInMemory("MonoMod.Utils.GetManagedSizeHelper"));
            Assert.False(ExternalMods.IsKnownInMemory("RedTeamProbe"));
            Assert.False(ExternalMods.IsKnownInMemory("rewired_windows"));
            Assert.Equal("RedTeamProbe (loaded from memory)", ExternalMods.InMemoryEntry("RedTeamProbe"));
        }

        private const string Managed = @"G:\SteamLibrary\steamapps\common\The Forest\TheForest_Data\Managed";
        private const string Core = @"G:\SteamLibrary\steamapps\common\The Forest\BepInEx\core";
        private const string Patchers = @"G:\SteamLibrary\steamapps\common\The Forest\BepInEx\patchers";

        [Fact]
        public void ACleanInstallsFilesAreTrusted()
        {
            Assert.Equal(49, ExternalMods.ManagedFiles.Length);
            Assert.True(ExternalMods.IsTrustedFile(Managed + @"\Assembly-CSharp.dll", Managed, Core));
            Assert.True(ExternalMods.IsTrustedFile("g:/steamlibrary/steamapps/common/the forest/theforest_data/managed/unityengine.dll", Managed + "\\", Core));
            Assert.True(ExternalMods.IsTrustedFile(Core + @"\0Harmony.dll", Managed, Core));
            Assert.True(ExternalMods.IsTrustedFile(Core + @"\Mono.Cecil.Rocks.dll", Managed, Core));
            Assert.True(ExternalMods.IsTrustedFile(Core + @"\MonoMod.RuntimeDetour.dll", Managed, Core));
        }

        [Fact]
        public void AnyOtherFileInTheTrustedFoldersIsNot()
        {
            Assert.False(ExternalMods.IsTrustedFile(Managed + @"\RedTeamExtra.dll", Managed, Core));
            Assert.False(ExternalMods.IsTrustedFile(Managed + @"\sub\Assembly-CSharp.dll", Managed, Core));
            Assert.False(ExternalMods.IsTrustedFile(Core + @"\Cheat.dll", Managed, Core));
            Assert.False(ExternalMods.IsTrustedFile(Core + @"\Assembly-CSharp.dll", Managed, Core));   // a Managed name in core
            Assert.False(ExternalMods.IsTrustedFile(@"G:\Elsewhere\UnityEngine.dll", Managed, Core));
            Assert.False(ExternalMods.IsTrustedFile("", Managed, Core));
        }

        [Fact]
        public void OnlyOurPatchersOwnPathIsOurs()
        {
            Assert.True(ExternalMods.IsOurPatcher(Patchers + @"\ForestOverlay.Updater.dll", Patchers));
            Assert.True(ExternalMods.IsOurPatcher("g:/steamlibrary/steamapps/common/the forest/bepinex/patchers/forestoverlay.updater.dll", Patchers + "\\"));
            Assert.False(ExternalMods.IsOurPatcher(Patchers + @"\redteam\ForestOverlay.Updater.dll", Patchers));
            Assert.False(ExternalMods.IsOurPatcher(@"G:\SteamLibrary\steamapps\common\The Forest\BepInEx\plugins\ForestOverlay.Updater.dll", Patchers));
            Assert.False(ExternalMods.IsOurPatcher(Patchers + @"\RedTeamPatcher.dll", Patchers));
        }
    }
}
