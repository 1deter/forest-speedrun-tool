using System;
using System.Collections.Generic;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Anything external running beside the game (T-0308): another BepInEx
    // plugin, a patcher other than ours, a DLL loaded from outside the
    // game's own folders, a Harmony patch owned by someone else. The
    // session is flagged like ModAPI - a label, never a gate (rule 12).
    // This is the pure part: names from the lists Game/RunIntegrity
    // gathers, merged for the session, and the flag text.
    // ------------------------------------------------------------------
    public static class ExternalMods
    {
        /// The attempt flag / .run flag: "Mods Detected: A, B". The site
        /// reads the same prefix (AttemptFormat / RunReport keep a copy).
        public const string FlagPrefix = "Mods Detected: ";

        /// Names shown before "+N more" (the full list stays in the report).
        public const int ShownNames = 4;

        /// Adds the names the four lists show to `into` (once each, in
        /// order). Plugins: "Name 1.0 (file.dll)" -> "Name"; patchers and
        /// code: the file; patches: the owner's id. Failures of the
        /// gatherer ("could not list ...") name nothing.
        public static void Collect(IList<string> plugins, IList<string> patchers, IList<string> code,
                                   IList<string> patches, List<string> into)
        {
            if (plugins != null)
                for (int i = 0; i < plugins.Count; i++) Add(into, PluginName(plugins[i]));
            if (patchers != null)
                for (int i = 0; i < patchers.Count; i++) Add(into, Plain(patchers[i]));
            if (code != null)
                for (int i = 0; i < code.Count; i++) Add(into, Plain(code[i]));
            if (patches != null)
                for (int i = 0; i < patches.Count; i++) Add(into, PatchOwner(patches[i]));
        }

        /// "Name 1.0 (file.dll)" -> "Name" (the version is the last word
        /// before the file); a name with no such shape is kept whole.
        public static string PluginName(string entry)
        {
            string s = Plain(entry);
            if (s.Length == 0) return "";
            int paren = s.LastIndexOf(" (", StringComparison.Ordinal);
            if (paren > 0) s = s.Substring(0, paren);
            int sp = s.LastIndexOf(' ');
            if (sp > 0 && sp + 1 < s.Length && char.IsDigit(s[sp + 1])) s = s.Substring(0, sp);
            return s.Trim();
        }

        /// "Type.Method (by owner)" -> "owner"; "Type.Method (by owner, from
        /// Assembly)" -> "owner"; "Type.Method (a detour, not Harmony:
        /// Assembly)" -> "Assembly".
        public static string PatchOwner(string entry)
        {
            string s = Plain(entry);
            if (!s.EndsWith(")", StringComparison.Ordinal)) return "";
            const string detour = "(a detour, not Harmony: ";
            int d = s.LastIndexOf(detour, StringComparison.Ordinal);
            if (d >= 0) return s.Substring(d + detour.Length, s.Length - d - detour.Length - 1).Trim();
            int at = s.LastIndexOf("(by ", StringComparison.Ordinal);
            if (at < 0) return "";
            string owner = s.Substring(at + 4, s.Length - at - 5);
            int from = owner.LastIndexOf(", from ", StringComparison.Ordinal);
            if (from >= 0) owner = owner.Substring(0, from);
            return owner.Trim();
        }

        // ------------------------------------------------------------------
        // Which loaded code is the game's or the platform's (T-0414 F7-F9).
        // Live baseline on the author's clean install (BepInEx 5.4.23.5).

        /// Assemblies with no file that the game and the platform load
        /// themselves (both launches of the red team, T-0414).
        public static readonly string[] KnownInMemory =
        {
            "Rewired_Windows", "Rewired_Config_Manager", "Rewired_Config_ManagerA", "MonoMod.Utils.GetManagedSizeHelper",
        };

        /// The *.dll files of a clean TheForest_Data/Managed (49).
        public static readonly string[] ManagedFiles =
        {
            "AmplifyOcclusion.dll", "Assembly-CSharp-firstpass.dll", "Assembly-CSharp.dll", "Assembly-UnityScript-firstpass.dll",
            "Assembly-UnityScript.dll", "AsyncBridge.dll", "BaseModLib.dll", "Boo.Lang.dll", "I18N.CJK.dll", "I18N.MidEast.dll",
            "I18N.Other.dll", "I18N.Rare.dll", "I18N.West.dll", "I18N.dll", "Ionic.Zip.dll", "Mono.Data.Tds.dll", "Mono.Posix.dll",
            "Mono.Security.dll", "Newtonsoft.Json.dll", "Pathfinding.ClipperLib.dll", "Pathfinding.Ionic.Zip.Reduced.dll",
            "Pathfinding.JsonFx.dll", "Pathfinding.Poly2Tri.dll", "PlayMaker.dll", "RestSharp.dll", "Rewired_Core.dll",
            "Rewired_Windows_Lib.dll", "SteamworksManaged.dll", "System.Configuration.dll", "System.Core.dll", "System.Data.dll",
            "System.EnterpriseServices.dll", "System.Security.dll", "System.Threading.dll", "System.Transactions.dll",
            "System.Xml.Linq.dll", "System.Xml.dll", "System.dll", "TheForest.Commons.dll", "TheForest.Modding.dll",
            "UnityEngine.Networking.dll", "UnityEngine.UI.dll", "UnityEngine.dll", "UnityScript.Lang.dll", "bolt.dll",
            "bolt.user.dll", "mscorlib.dll", "udpkit.common.dll", "udpkit.dll",
        };

        /// The *.dll files of BepInEx/core in a clean BepInEx 5.4.23.5.
        public static readonly string[] CoreFiles =
        {
            "0Harmony.dll", "0Harmony20.dll", "BepInEx.dll", "BepInEx.Harmony.dll", "BepInEx.Preloader.dll", "HarmonyXInterop.dll",
            "Mono.Cecil.dll", "Mono.Cecil.Mdb.dll", "Mono.Cecil.Pdb.dll", "Mono.Cecil.Rocks.dll", "MonoMod.RuntimeDetour.dll",
            "MonoMod.Utils.dll",
        };

        /// Our patcher's file name; only the one directly in BepInEx/patchers is ours.
        public const string UpdaterFile = "ForestOverlay.Updater.dll";

        /// True for an assembly with no file that the game or the platform loads itself.
        public static bool IsKnownInMemory(string name)
        {
            return Array.IndexOf(KnownInMemory, name) >= 0;
        }

        /// The report's line for code with no file.
        public static string InMemoryEntry(string name)
        {
            return name + " (loaded from memory)";
        }

        /// True when `file` is one a clean install has directly in `managedDir`
        /// or `coreDir` (paths compare without case and slash direction; a
        /// subfolder or another name is not trusted).
        public static bool IsTrustedFile(string file, string managedDir, string coreDir)
        {
            if (string.IsNullOrEmpty(file)) return false;
            string f = Norm(file);
            int slash = f.LastIndexOf('/');
            if (slash < 0) return false;
            string dir = f.Substring(0, slash), name = f.Substring(slash + 1);
            if (!string.IsNullOrEmpty(managedDir) && dir == Norm(managedDir).TrimEnd('/')) return Has(ManagedFiles, name);
            if (!string.IsNullOrEmpty(coreDir) && dir == Norm(coreDir).TrimEnd('/')) return Has(CoreFiles, name);
            return false;
        }

        /// True for our own patcher: exactly `<patchersDir>/ForestOverlay.Updater.dll`.
        public static bool IsOurPatcher(string file, string patchersDir)
        {
            if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(patchersDir)) return false;
            return Norm(file) == Norm(patchersDir).TrimEnd('/') + "/" + UpdaterFile.ToLowerInvariant();
        }

        private static bool Has(string[] names, string lowerName)
        {
            for (int i = 0; i < names.Length; i++)
                if (names[i].ToLowerInvariant() == lowerName) return true;
            return false;
        }

        private static string Norm(string p)
        {
            return p.Replace('\\', '/').ToLowerInvariant();
        }

        private static string Plain(string s)
        {
            if (string.IsNullOrEmpty(s) || s.StartsWith("could not", StringComparison.Ordinal)) return "";
            return s.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        private static void Add(List<string> into, string name)
        {
            if (name.Length > 0 && !into.Contains(name)) into.Add(name);
        }

        /// The entries of `after` not in `before` (in order): what turned up
        /// since the last look, for the log line.
        public static List<string> Added(IList<string> before, IList<string> after)
        {
            List<string> added = new List<string>();
            if (after == null) return added;
            for (int i = 0; i < after.Count; i++)
                if ((before == null || !before.Contains(after[i])) && !added.Contains(after[i])) added.Add(after[i]);
            return added;
        }

        /// "A, B, C, D +2 more": the names for the HUD and the flag.
        public static string Join(IList<string> names)
        {
            if (names == null || names.Count == 0) return "";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int shown = Math.Min(names.Count, ShownNames);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(names[i]);
            }
            if (names.Count > shown) sb.Append(" +").Append(names.Count - shown).Append(" more");
            return sb.ToString();
        }

        public static string Flag(string names)
        {
            return FlagPrefix + names;
        }

        /// The names part of the last mods flag in `flags` ("" when none): a
        /// mod that turns up mid-attempt appends a longer list.
        public static string FromFlags(IList<string> flags)
        {
            if (flags == null) return "";
            for (int i = flags.Count - 1; i >= 0; i--)
                if (flags[i] != null && flags[i].StartsWith(FlagPrefix, StringComparison.Ordinal))
                    return flags[i].Substring(FlagPrefix.Length);
            return "";
        }
    }

    // ------------------------------------------------------------------
    // When to gather the mods again (T-0414 live, v0.24.398): a one-shot
    // Assembly.Load(bytes) raised no AssemblyLoad event the watch saw, so
    // the loaded assemblies are counted too, beside the patched methods
    // and MonoMod's detours. Game/ExternalModWatch and run mode's Watch
    // each keep one. Not a bare count (live, v0.24.399): Assembly.Load(bytes)
    // under a name already loaded swaps the domain's entry and the count
    // stays - so the assemblies are a signature over each one's identity.
    // ------------------------------------------------------------------
    public sealed class ModCounts
    {
        private bool _seen;
        private int _patches, _detours;
        private ulong _assemblies;

        /// The signature of no assemblies; FoldAssembly adds one each.
        public const ulong EmptySignature = 0x9E3779B97F4A7C15UL;

        /// Adds one loaded assembly (`identity` = its object's identity hash,
        /// RuntimeHelpers.GetHashCode) to `signature`. Order does not count
        /// (a sum of mixed values), a swapped or added assembly does.
        public static ulong FoldAssembly(ulong signature, int identity)
        {
            ulong z = (ulong)(uint)identity + 0x9E3779B97F4A7C15UL;   // splitmix64
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return unchecked(signature + z + 1UL);
        }

        /// True when a look is due: the first call, a load event, or any
        /// count / the assembly signature moved since the last call (remembered).
        public bool Due(int patches, int detours, ulong assemblies, bool loadEvent)
        {
            bool due = !_seen || loadEvent || patches != _patches || detours != _detours || assemblies != _assemblies;
            _seen = true;
            _patches = patches;
            _detours = detours;
            _assemblies = assemblies;
            return due;
        }
    }
}
