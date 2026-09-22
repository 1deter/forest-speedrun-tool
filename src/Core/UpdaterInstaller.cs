using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // Writes the embedded update patcher to BepInEx/patchers.
    //
    // The patcher (patcher/, built into this DLL as a resource) is what
    // moves a downloaded ForestOverlay.dll.pending into place on the next
    // launch. Installing it from here keeps "drop in one DLL" as the whole
    // install, and means the first plugin carrying it arms every update
    // after it.
    //
    // The patcher is loaded while the game runs, so overwriting it can
    // fail. A loaded file can usually still be renamed on Windows, so the
    // old copy is moved aside to .old and the new one written in its
    // place; if even that fails the old patcher keeps working and the
    // next launch tries again.
    // ------------------------------------------------------------------
    public static class UpdaterInstaller
    {
        public const string FileName = "ForestOverlay.Updater.dll";
        private const string Resource = "patcher/" + FileName;

        /// Shown in the Updates tab; "installed" is what makes an update
        /// apply itself on restart.
        public static string Status { get; private set; }
        public static bool Installed { get; private set; }

        public static void Install(ManualLogSource log)
        {
            Status = "not installed";

            try
            {
                string dir = Paths.PatcherPluginPath;
                string path = Path.Combine(dir, FileName);
                string old = path + ".old";

                // Left behind by the previous launch's rename; not loaded now.
                if (File.Exists(old))
                {
                    try { File.Delete(old); } catch (Exception) { }
                }

                byte[] shipped = ReadResource();
                if (shipped == null)
                {
                    Status = "missing from this build";
                    log.LogWarning("Updater: patcher is not embedded in this build.");
                    return;
                }

                if (File.Exists(path) && SameBytes(File.ReadAllBytes(path), shipped))
                {
                    Installed = true;
                    Status = "installed";
                    return;
                }

                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                bool existed = File.Exists(path);

                try
                {
                    File.WriteAllBytes(path, shipped);
                }
                catch (IOException)
                {
                    // Loaded this session: step it aside and write fresh.
                    File.Move(path, old);
                    File.WriteAllBytes(path, shipped);
                }

                Installed = true;
                Status = existed ? "updated" : "installed - updates now apply on restart";
                log.LogInfo("Updater: " + Status + " -> " + path);
            }
            catch (Exception ex)
            {
                Installed = File.Exists(Path.Combine(Paths.PatcherPluginPath, FileName));
                Status = Installed ? "installed (could not refresh: " + ex.Message + ")"
                                   : "could not install: " + ex.Message;
                log.LogWarning("Updater: " + Status);
            }
        }

        private static byte[] ReadResource()
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource))
            {
                if (s == null) return null;
                byte[] data = new byte[s.Length];
                int read = 0;
                while (read < data.Length)
                {
                    int n = s.Read(data, read, data.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                return data;
            }
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
