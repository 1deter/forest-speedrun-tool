using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace ForestOverlay.Updater
{
    // ------------------------------------------------------------------
    // Second patcher in this DLL (T-0246): repairs ModAPI's rebuilt
    // Assembly-CSharp before the game loads it (Updater/DefaultRepair).
    //
    // Assembly-CSharp is a target only when it references ModAPI's
    // BaseModLib, so a game without ModAPI is never patched or loaded from
    // memory. The plugin reads DomainKey: ModAPI present but the key
    // missing means this patcher did not run this launch (it was just
    // installed), and the plugin stays off until the next one; Failed
    // means the repair could not be done, and the plugin stays off.
    // ------------------------------------------------------------------
    public static class ModApiFix
    {
        public const string DomainKey = "ForestOverlay.ModApiFix";
        /// DomainKey's value when the read or the repair failed.
        public const int Failed = -1;
        private const string Target = "Assembly-CSharp.dll";

        private static ManualLogSource _log;
        private static string[] _targets;

        public static IEnumerable<string> TargetDLLs
        {
            get
            {
                if (_targets == null) _targets = NeedsRepair() ? new[] { Target } : new string[0];
                return _targets;
            }
        }

        public static void Patch(AssemblyDefinition assembly)
        {
            try
            {
                int n = DefaultRepair.Repair(assembly.MainModule, LogRepair);
                AppDomain.CurrentDomain.SetData(DomainKey, n);
                Log().LogInfo("ModAPI fix: " + n + " parameter default(s) repaired in " + Target + ".");
            }
            catch (Exception ex)
            {
                // Never let the fix stop the game from starting.
                AppDomain.CurrentDomain.SetData(DomainKey, Failed);
                Log().LogError("ModAPI fix: failed - " + ex);
            }
        }

        private static bool NeedsRepair()
        {
            try
            {
                string path = Path.Combine(Paths.ManagedPath, Target);
                if (!File.Exists(path)) return false;
                using (AssemblyDefinition asm = AssemblyDefinition.ReadAssembly(path))
                {
                    if (!DefaultRepair.ReferencesModApi(asm.MainModule)) return false;
                }
                Log().LogInfo("ModAPI fix: " + Target + " was rebuilt by ModAPI - checking its parameter defaults.");
                return true;
            }
            catch (Exception ex)
            {
                // The plugin acts on this only when ModAPI is loaded.
                AppDomain.CurrentDomain.SetData(DomainKey, Failed);
                Log().LogError("ModAPI fix: could not read " + Target + " - " + ex.Message);
                return false;
            }
        }

        private static void LogRepair(string line)
        {
            Log().LogInfo("ModAPI fix: " + line);
        }

        private static ManualLogSource Log()
        {
            if (_log == null) _log = Logger.CreateLogSource("ForestOverlay.ModApiFix");
            return _log;
        }
    }
}
