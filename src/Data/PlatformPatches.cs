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
    // Also which patches are ours and which detours are the platform's
    // (T-0414 F5, F6).
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

        // ------------------------------------------------------------------
        // Our own patches (T-0414 F5): an owner id that starts with our GUID
        // is not enough - anyone can borrow the prefix. Every one of ours
        // has its patch method in ForestOverlay's own assembly (live).

        /// True when the patch is ForestOverlay's: the owner starts with our
        /// GUID and the patch method is in our loaded assembly (the caller
        /// compares the Assembly objects - a name can be copied; null /
        /// empty never matches).
        public static bool IsOurs(string owner, string ownGuid, bool inOurAssembly)
        {
            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(ownGuid)) return false;
            return owner.StartsWith(ownGuid, StringComparison.Ordinal) && inOurAssembly;
        }

        /// The report's line for a foreign patch: "Type.Method (by owner)",
        /// and "Type.Method (by owner, from Assembly)" when the owner
        /// borrows our GUID (the assembly says whose it really is).
        public static string PatchEntry(string method, string owner, string ownGuid, string patchAssembly)
        {
            bool borrowed = !string.IsNullOrEmpty(owner) && !string.IsNullOrEmpty(ownGuid) &&
                            owner.StartsWith(ownGuid, StringComparison.Ordinal);
            return method + " (by " + owner +
                   (borrowed ? ", from " + (string.IsNullOrEmpty(patchAssembly) ? NoFile : patchAssembly) : "") + ")";
        }

        // ------------------------------------------------------------------
        // Detours that are not Harmony's (T-0414 F6). MonoMod keeps every
        // detour in Detour._DetourMap; Harmony makes one per patched method.
        // BepInEx / Harmony's own, seen live on BepInEx 5.4.23.5: the keys
        // ILHook+Context.Refresh and StackFrame.GetMethod with a target in
        // 0Harmony, and HarmonyInteropFix.LoadFile, declared in
        // BepInEx.Preloader.

        /// What code with no file is called in the report.
        public const string NoFile = "code with no file";

        /// True for the platform's own detours: a target in Harmony's
        /// assembly, or a key declared in BepInEx's preloader.
        public static bool IsPlatformDetour(string keyAssembly, string targetAssembly)
        {
            return targetAssembly == "0Harmony" || keyAssembly == "BepInEx.Preloader";
        }

        /// The report's line for a detour: "Type.Method (a detour, not Harmony: Assembly)".
        public static string DetourEntry(string method, string targetAssembly)
        {
            return method + " (a detour, not Harmony: " + (string.IsNullOrEmpty(targetAssembly) ? NoFile : targetAssembly) + ")";
        }

        private static string Norm(string p)
        {
            return p.Replace('\\', '/').ToLowerInvariant();
        }
    }
}
