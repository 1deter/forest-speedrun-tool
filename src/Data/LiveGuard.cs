using System;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // What the test bridge may touch while a live session is on (T-0490,
    // author 2026-11-28: "full bridge but guarded while remote"). The
    // bridge's reflection reaches any type in the process; a live session
    // is driven from outside the runner's machine, so it keeps to the
    // game and its engine:
    //
    //   allowed  members declared in Assembly-CSharp*, UnityEngine*,
    //            Rewired*, PlayMaker* assemblies
    //   refused  everything else - mscorlib / System.* (Process, File,
    //            reflection, AppDomain), ForestOverlay's own types,
    //            BepInEx, Harmony, Bolt - and inside UnityEngine:
    //            Application.OpenURL / CaptureScreenshot / ExternalCall /
    //            ExternalEval, WWW*, UnityEngine.Networking.* (UnityWebRequest),
    //            ScreenCapture, AssetBundle.LoadFrom*, SendMessage /
    //            BroadcastMessage (they reach any behaviour, the
    //            overlay's own too)
    //
    // Pure strings so it is tested without a game: the probe passes the
    // declaring type's assembly name, full name and the member's name.
    // ------------------------------------------------------------------
    public static class LiveGuard
    {
        public const string Refusal = "not allowed in a live session";

        private static readonly string[] AllowedAssemblies = { "Assembly-CSharp", "UnityEngine", "Rewired", "PlayMaker" };

        // Whole types (or namespaces, with the trailing dot) refused inside
        // the allowed assemblies.
        private static readonly string[] RefusedTypes =
        {
            "UnityEngine.WWW", "UnityEngine.Networking.", "UnityEngine.Experimental.Networking.",
            "UnityEngine.ScreenCapture",
        };

        /// May anything of this assembly be reached (FindType)?
        public static bool AllowsAssembly(string assembly)
        {
            if (string.IsNullOrEmpty(assembly)) return false;
            for (int i = 0; i < AllowedAssemblies.Length; i++)
                if (assembly.StartsWith(AllowedAssemblies[i], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// May this type be named (static:Type, type, members) or its
        /// members listed?
        public static bool AllowsType(string assembly, string typeFullName)
        {
            if (!AllowsAssembly(assembly)) return false;
            if (string.IsNullOrEmpty(typeFullName)) return false;
            for (int i = 0; i < RefusedTypes.Length; i++)
                if (typeFullName.StartsWith(RefusedTypes[i], StringComparison.Ordinal)) return false;
            return true;
        }

        /// May this member (field, property or method) be read, written or
        /// called? `declaringType` is the type that declares it.
        public static bool AllowsMember(string assembly, string declaringType, string member)
        {
            if (!AllowsType(assembly, declaringType)) return false;
            if (member == null) return false;
            if (declaringType == "UnityEngine.Application")
                return member != "OpenURL" && member != "CaptureScreenshot" &&
                       member != "ExternalCall" && member != "ExternalEval";
            if (declaringType == "UnityEngine.AssetBundle")
                return !member.StartsWith("LoadFrom", StringComparison.Ordinal);
            if (member == "SendMessage" || member == "SendMessageUpwards" || member == "BroadcastMessage")
                return !declaringType.StartsWith("UnityEngine.", StringComparison.Ordinal);
            return true;
        }
    }
}
