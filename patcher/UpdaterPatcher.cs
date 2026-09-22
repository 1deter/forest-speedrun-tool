using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace ForestOverlay.Updater
{
    // ------------------------------------------------------------------
    // BepInEx preloader patcher that installs ForestOverlay updates.
    //
    // The plugin can download a new version but cannot replace itself:
    // Windows will not let a loaded assembly be overwritten. Patchers run
    // in the preloader, before the chainloader has loaded any plugin, so
    // at that moment ForestOverlay.dll is just a file.
    //
    // It patches nothing. BepInEx only accepts a patcher that has
    // TargetDLLs and Patch, so both exist; TargetDLLs is empty and Patch
    // is never called. The work is in Initialize, which runs first.
    //
    // Installed by the plugin itself (ShippedData embeds this DLL and
    // writes it to BepInEx/patchers), so a runner still installs one file.
    // Keep this small and stable: it is the one piece that can break an
    // install, and it is updated less reliably than the plugin because it
    // is loaded whenever the game is running.
    // ------------------------------------------------------------------
    public static class UpdaterPatcher
    {
        private const string PluginFile = "ForestOverlay.dll";
        private const string PluginAssemblyName = "ForestOverlay";

        public static IEnumerable<string> TargetDLLs { get { return new string[0]; } }

        public static void Patch(AssemblyDefinition assembly) { }

        private static ManualLogSource _log;

        public static void Initialize()
        {
            _log = Logger.CreateLogSource("ForestOverlay.Updater");

            try
            {
                string[] staged = Directory.GetFiles(Paths.PluginPath, PluginFile + PendingSwap.PendingSuffix,
                                                     SearchOption.AllDirectories);

                if (staged.Length == 0)
                {
                    _log.LogInfo("No staged update.");
                    return;
                }

                for (int i = 0; i < staged.Length; i++)
                {
                    string target = staged[i].Substring(0, staged[i].Length - PendingSwap.PendingSuffix.Length);
                    PendingSwap.Apply(target, IsOurPlugin, Log);
                }
            }
            catch (Exception ex)
            {
                // Never let the updater stop the game from starting.
                _log.LogError("Update check in preloader failed: " + ex);
            }
        }

        private static void Log(string message)
        {
            _log.LogInfo(message);
        }

        /// Reads the staged file's assembly name without loading it. Only
        /// the ForestOverlay assembly is ever moved into place.
        private static bool IsOurPlugin(string path)
        {
            using (AssemblyDefinition asm = AssemblyDefinition.ReadAssembly(path))
            {
                bool ok = asm.Name.Name == PluginAssemblyName;
                if (ok) _log.LogInfo("Staged update is " + asm.Name.Name + " v" + asm.Name.Version);
                return ok;
            }
        }
    }
}
