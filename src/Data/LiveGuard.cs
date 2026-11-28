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
    //            BepInEx, Harmony, Bolt - and inside UnityEngine the
    //            types in RefusedTypes (networking, files, profiler logs,
    //            microphone / webcam / location, code loading), the
    //            Application / AssetBundle / clipboard members below, and
    //            by name on any type Invoke*, CancelInvoke, StartCoroutine*,
    //            SendMessage*, BroadcastMessage
    //   receiver the object a member is used on must be an allowed type
    //            too (an overlay behaviour's UnityEngine `enabled` is
    //            refused); Game/ObjectProbe also refuses the overlay's own
    //            GameObjects
    //
    // Pure strings so it is tested without a game: the probe passes the
    // declaring type's assembly name, full name and the member's name.
    // ------------------------------------------------------------------
    public static class LiveGuard
    {
        public const string Refusal = "not allowed in a live session";

        private static readonly string[] AllowedAssemblies = { "Assembly-CSharp", "UnityEngine", "Rewired", "PlayMaker" };

        // Whole types (or namespaces, with the trailing dot; or a name
        // prefix: "UnityEngine.WWW" covers WWWForm too) refused inside the
        // allowed assemblies. From UnityEngine 5.6's type list (tools/ILScan
        // over the 5.6.1 reference UnityEngine.dll, T-0490): everything that
        // reaches files, the network, other processes, the clipboard, a
        // microphone / camera / location, or loads code.
        private static readonly string[] RefusedTypes =
        {
            "UnityEngine.WWW", "UnityEngine.Networking.", "UnityEngine.Experimental.Networking.",
            "UnityEngine.Network",            // Network, NetworkView, NetworkPlayer... (legacy networking)
            "UnityEngine.MasterServer", "UnityEngine.Ping", "UnityEngine.ClusterNetwork",
            "UnityEngine.ScreenCapture",
            "UnityEngine.Windows.",           // Windows.File / Directory (WSA), Speech
            "UnityEngine.Security",           // LoadAndVerifyAssembly, PrefetchSocketPolicy
            "UnityEngine.Profiling.",         // Profiler.logFile / enableBinaryLog write files
            "UnityEngine.Microphone", "UnityEngine.WebCam", "UnityEngine.LocationService",
            "UnityEngine.AndroidJ", "UnityEngine._AndroidJNI", "UnityEngine.AndroidReflection",
            "UnityEngine.Caching", "UnityEngine.CrashReport", "UnityEngine.Analytics.",
            "UnityEngine.RemoteSettings", "UnityEngine.Connect.", "UnityEngine.Social",
            "UnityEngine.Handheld",
        };

        // Refused by name on any type: they run a method named by a string,
        // or start one, on whatever behaviour they are given - the overlay's
        // own too.
        private static readonly string[] RefusedNames =
        {
            "Invoke", "InvokeRepeating", "CancelInvoke", "BroadcastMessage",
        };
        private static readonly string[] RefusedNamePrefixes = { "StartCoroutine", "SendMessage" };

        /// May anything of this assembly be reached (FindType)?
        public static bool AllowsAssembly(string assembly)
        {
            if (string.IsNullOrEmpty(assembly)) return false;
            for (int i = 0; i < AllowedAssemblies.Length; i++)
                if (assembly.StartsWith(AllowedAssemblies[i], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // The few mscorlib types a game path walks through: text, arrays,
        // lists, dictionaries and numbers. Their members only read, measure
        // and index data the game already holds; none reaches a file, the
        // network, a process or reflection (a Type, a delegate or a stream
        // is not on this list, so those stay refused as receivers). Exact
        // names, or the name with generic arguments after it.
        private static readonly string[] SafeSystemTypes =
        {
            "System.String", "System.Array", "System.Math", "System.Boolean", "System.Byte", "System.Char",
            "System.Int16", "System.Int32", "System.Int64", "System.Single", "System.Double", "System.UInt32",
            "System.Collections.Generic.List`1", "System.Collections.Generic.Dictionary`2",
            "System.Collections.Generic.KeyValuePair`2",
        };

        private static bool IsSafeSystemType(string assembly, string typeFullName)
        {
            if (!string.Equals(assembly, "mscorlib", StringComparison.OrdinalIgnoreCase)) return false;
            string n = typeFullName;
            while (n.EndsWith("[]", StringComparison.Ordinal)) n = n.Substring(0, n.Length - 2);
            for (int i = 0; i < SafeSystemTypes.Length; i++)
            {
                string s = SafeSystemTypes[i];
                if (n == s || (n.Length > s.Length && n.StartsWith(s, StringComparison.Ordinal) && n[s.Length] == '[')) return true;
            }
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
        /// called? `declaringType` is the type that declares it; a property's
        /// accessor (get_X / set_X) counts as the property.
        public static bool AllowsMember(string assembly, string declaringType, string member)
        {
            if (!AllowsReceiver(assembly, declaringType)) return false;
            if (string.IsNullOrEmpty(member)) return false;
            string m = member.StartsWith("get_", StringComparison.Ordinal) || member.StartsWith("set_", StringComparison.Ordinal)
                ? member.Substring(4) : member;
            for (int i = 0; i < RefusedNames.Length; i++)
                if (m == RefusedNames[i]) return false;
            for (int i = 0; i < RefusedNamePrefixes.Length; i++)
                if (m.StartsWith(RefusedNamePrefixes[i], StringComparison.Ordinal)) return false;
            switch (declaringType)
            {
                case "UnityEngine.Application":
                    return m != "OpenURL" && m != "CaptureScreenshot" && m != "ExternalCall" && m != "ExternalEval" &&
                           m != "Internal_ExternalCall" && m != "RequestAdvertisingIdentifierAsync" &&
                           m != "RequestUserAuthorization" &&
                           // the runner's folders (a Windows user name is in them)
                           m != "dataPath" && m != "persistentDataPath" && m != "streamingAssetsPath" &&
                           m != "temporaryCachePath" && m != "consoleLogPath";
                case "UnityEngine.AssetBundle":
                    return !m.StartsWith("LoadFrom", StringComparison.Ordinal);
                case "UnityEngine.GUIUtility":
                    return m != "systemCopyBuffer";       // the runner's clipboard
                case "UnityEngine.TextEditor":
                    return m != "Copy" && m != "Paste" && m != "CanPaste";
            }
            return true;
        }

        /// May an object of this type be the receiver of a member access
        /// (a path starts from or walks through it, a call or set targets
        /// it)? The same rule as a type: a member UnityEngine declares
        /// (Behaviour.enabled, Object.name) is still refused on an overlay,
        /// BepInEx or System object.
        public static bool AllowsReceiver(string assembly, string typeFullName)
        {
            return IsSafeSystemType(assembly, typeFullName) || AllowsType(assembly, typeFullName);
        }
    }
}
