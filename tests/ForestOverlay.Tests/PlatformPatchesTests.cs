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
    }
}
