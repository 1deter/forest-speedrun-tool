using System;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Which Harmony patches are BepInEx's own (T-0112). BepInEx patches
    // .NET itself at startup; run mode must not call those "another mod",
    // but every other patch - .NET methods included - is flagged (author,
    // 2026-10-08). Seen live on BepInEx 5.4.23.5 (v0.24.348, PatchManager
    // .PatchInfos), the only patches not ours, all with a patch method in
    // BepInEx/core/BepInEx.Preloader.dll:
    //   Console.SetOut           ConsoleSetOutFix    harmony-auto-<guid>
    //   Assembly.LoadFile / From HarmonyInteropFix   org.bepinex.fixes.harmonyinterop
    //   Assembly.get_Location /
    //     get_CodeBase           UnityPatches        harmony-auto-<guid>
    //   Trace.DoTrace            TraceFix            com.bepis.bepinex.tracefix
    // The harmony-auto ids are random per launch (Harmony.CreateAndPatchAll
    // with no id), so the owner alone cannot say it: the patch method's
    // file must be the preloader in BepInEx's core folder too. Anyone can
    // use a harmony-auto id; nobody else's patch method lives in that file.
    // ------------------------------------------------------------------
    public static class PlatformPatches
    {
        /// BepInEx's fixed owner ids (seen live).
        public static readonly string[] Owners = { "org.bepinex.fixes.harmonyinterop", "com.bepis.bepinex.tracefix" };

        /// Harmony's id for a patch made without one.
        public const string AutoOwnerPrefix = "harmony-auto-";

        /// The file BepInEx's runtime fixes live in, under BepInEx/core.
        public const string PreloaderFile = "BepInEx.Preloader.dll";

        /// True when the patch is BepInEx's own: a BepInEx owner id (or
        /// Harmony's auto id) and a patch method loaded from
        /// `<coreDir>/BepInEx.Preloader.dll`. Paths compare without case and
        /// slash direction; null / empty never matches.
        public static bool IsBepInEx(string owner, string patchFile, string coreDir)
        {
            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(patchFile) || string.IsNullOrEmpty(coreDir)) return false;
            bool known = owner.StartsWith(AutoOwnerPrefix, StringComparison.Ordinal);
            for (int i = 0; !known && i < Owners.Length; i++) known = owner == Owners[i];
            if (!known) return false;
            string core = Norm(coreDir).TrimEnd('/');
            return Norm(patchFile) == core + "/" + PreloaderFile.ToLowerInvariant();
        }

        private static string Norm(string p)
        {
            return p.Replace('\\', '/').ToLowerInvariant();
        }
    }
}
