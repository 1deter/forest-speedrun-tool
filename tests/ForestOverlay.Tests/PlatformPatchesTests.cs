using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // BepInEx's own Harmony patches vs everyone else's (T-0112).
    public class PlatformPatchesTests
    {
        private const string Core = @"G:\SteamLibrary\steamapps\common\The Forest\BepInEx\core";
        private const string Preloader = @"G:\SteamLibrary\steamapps\common\The Forest\BepInEx\core\BepInEx.Preloader.dll";

        [Fact]
        public void TheFourFixesSeenLiveAreBepInEx()
        {
            Assert.True(PlatformPatches.IsBepInEx("harmony-auto-fd9acae5-76f7-4376-bd4e-725a25237107", Preloader, Core));
            Assert.True(PlatformPatches.IsBepInEx("org.bepinex.fixes.harmonyinterop", Preloader, Core));
            Assert.True(PlatformPatches.IsBepInEx("com.bepis.bepinex.tracefix", Preloader, Core));
        }

        [Fact]
        public void PathsCompareWithoutCaseOrSlashDirection()
        {
            Assert.True(PlatformPatches.IsBepInEx("com.bepis.bepinex.tracefix",
                "g:/steamlibrary/steamapps/common/the forest/bepinex/core/bepinex.preloader.dll", Core + "\\"));
        }

        [Fact]
        public void AnotherModsAutoIdIsForeign()
        {
            // A cheat made with Harmony.CreateAndPatchAll gets a harmony-auto id too.
            Assert.False(PlatformPatches.IsBepInEx("harmony-auto-1234", @"G:\Game\BepInEx\plugins\Cheat.dll", @"G:\Game\BepInEx\core"));
        }

        [Fact]
        public void ABepInExIdFromAnotherFileIsForeign()
        {
            Assert.False(PlatformPatches.IsBepInEx("com.bepis.bepinex.tracefix", @"G:\Game\BepInEx\plugins\Cheat.dll", @"G:\Game\BepInEx\core"));
            Assert.False(PlatformPatches.IsBepInEx("org.bepinex.fixes.harmonyinterop", Core + @"\BepInEx.dll", Core));
        }

        [Fact]
        public void AnUnknownOwnerInThePreloaderIsForeign()
        {
            Assert.False(PlatformPatches.IsBepInEx("com.other.mod", Preloader, Core));
        }

        [Fact]
        public void NothingKnownIsForeign()
        {
            Assert.False(PlatformPatches.IsBepInEx(null, Preloader, Core));
            Assert.False(PlatformPatches.IsBepInEx("com.bepis.bepinex.tracefix", "", Core));
            Assert.False(PlatformPatches.IsBepInEx("com.bepis.bepinex.tracefix", Preloader, null));
        }

        // ---- ours by GUID and assembly (T-0414 F5) ----

        private const string Guid = "com.deter.forestoverlay";

        [Fact]
        public void OursNeedsTheGuidAndOurAssembly()
        {
            Assert.True(PlatformPatches.IsOurs(Guid, Guid, true));
            Assert.True(PlatformPatches.IsOurs(Guid + ".practice", Guid, true));
            Assert.False(PlatformPatches.IsOurs(Guid + ".cheat", Guid, false));   // the red team's borrowed prefix
            Assert.False(PlatformPatches.IsOurs("redteam.cheat", Guid, true));
            Assert.False(PlatformPatches.IsOurs(null, Guid, true));
            Assert.False(PlatformPatches.IsOurs(Guid, "", true));
        }

        [Fact]
        public void ABorrowedPrefixIsListedWithItsAssembly()
        {
            Assert.Equal("ItemDatabase.ItemIdByName (by com.deter.forestoverlay.cheat, from RedTeamProbe)",
                         PlatformPatches.PatchEntry("ItemDatabase.ItemIdByName", Guid + ".cheat", Guid, "RedTeamProbe"));
            Assert.Equal("ItemDatabase.ItemIdByName (by com.deter.forestoverlay.cheat, from code with no file)",
                         PlatformPatches.PatchEntry("ItemDatabase.ItemIdByName", Guid + ".cheat", Guid, ""));
            Assert.Equal("ItemDatabase.ItemIdByName (by redteam.cheat)",
                         PlatformPatches.PatchEntry("ItemDatabase.ItemIdByName", "redteam.cheat", Guid, "RedTeamProbe"));
        }

        // ---- detours (T-0414 F6) ----

        [Fact]
        public void ThePlatformsDetoursSeenLiveAreSkipped()
        {
            Assert.True(PlatformPatches.IsPlatformDetour("MonoMod.RuntimeDetour", "0Harmony"));   // ILHook+Context.Refresh
            Assert.True(PlatformPatches.IsPlatformDetour("mscorlib", "0Harmony"));                // StackFrame.GetMethod
            Assert.True(PlatformPatches.IsPlatformDetour("BepInEx.Preloader", ""));               // HarmonyInteropFix.LoadFile
        }

        [Fact]
        public void AnyOtherDetourIsListed()
        {
            Assert.False(PlatformPatches.IsPlatformDetour("Assembly-CSharp", ""));               // a MonoMod Hook<...>
            Assert.False(PlatformPatches.IsPlatformDetour("Assembly-CSharp", "RedTeamProbe"));
            Assert.False(PlatformPatches.IsPlatformDetour("mscorlib", "Cheat"));
            Assert.Equal("ItemDatabase.ItemIdByName (a detour, not Harmony: code with no file)",
                         PlatformPatches.DetourEntry("ItemDatabase.ItemIdByName", ""));
            Assert.Equal("ItemDatabase.ItemIdByName (a detour, not Harmony: RedTeamProbe)",
                         PlatformPatches.DetourEntry("ItemDatabase.ItemIdByName", "RedTeamProbe"));
        }
    }
}
