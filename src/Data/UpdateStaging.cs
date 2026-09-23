using System;
using System.IO;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Where a downloaded update goes, whatever the plugin file is called.
    //
    // The patcher (patcher/UpdaterPatcher.cs) installs only
    // ForestOverlay.dll.pending -> ForestOverlay.dll. Up to v0.23.6 the
    // plugin staged "<its own file name>.pending", so a browser's
    // ForestOverlay(1).dll staged ForestOverlay(1).dll.pending, which
    // nothing ever installed, and the same update was offered every launch
    // (runner report, 2026-09-23).
    //
    // Now the download is always staged as ForestOverlay.dll.pending next to
    // the running plugin, and a plugin running under another name renames
    // itself to ForestOverlay.dll.bak (a loaded DLL can be renamed on
    // Windows, just not overwritten - Core/UpdaterInstaller relies on the
    // same). After the restart the folder holds one ForestOverlay.dll and the
    // old version as the usual backup. Fixed in the plugin, not the
    // patcher: the patcher updates less reliably (CLAUDE.md).
    //
    // Pure file operations, no BepInEx or Unity: tested against real temp
    // folders like patcher/PendingSwap.cs.
    // ------------------------------------------------------------------
    public static class UpdateStaging
    {
        public const string PluginFile = "ForestOverlay.dll";
        public const string AssemblyName = "ForestOverlay";
        public const string PendingSuffix = ".pending";
        public const string BackupSuffix = ".bak";
        public const string AsideSuffix = ".old";

        /// The name the patcher installs.
        public static string CanonicalPath(string ownPath)
        {
            return Path.Combine(Path.GetDirectoryName(ownPath), PluginFile);
        }

        public static string PendingPath(string ownPath)
        {
            return CanonicalPath(ownPath) + PendingSuffix;
        }

        public static bool IsCanonical(string ownPath)
        {
            return string.Equals(Path.GetFileName(ownPath), PluginFile, StringComparison.OrdinalIgnoreCase);
        }

        /// Where a differently named plugin moves itself: the backup the
        /// patcher would have made. Null when already ForestOverlay.dll.
        public static string AsidePath(string ownPath)
        {
            return IsCanonical(ownPath) ? null : CanonicalPath(ownPath) + BackupSuffix;
        }

        public sealed class Result
        {
            public string Pending;
            /// Where the running plugin was moved; null when it did not
            /// need to be (or could not be - see AsideError).
            public string Aside;
            public string AsideError;
        }

        /// Writes the download and, when the plugin runs under another
        /// name, moves it aside. Throws only if the download cannot be
        /// written; a failed move is reported in the result.
        public static Result Stage(string ownPath, byte[] data)
        {
            Result r = new Result();
            r.Pending = PendingPath(ownPath);
            File.WriteAllBytes(r.Pending, data);

            string aside = AsidePath(ownPath);
            if (aside == null) return r;

            // A second Download (a retry) after the first one moved it.
            if (!File.Exists(ownPath) && File.Exists(aside)) { r.Aside = aside; return r; }

            try
            {
                if (File.Exists(aside)) File.Delete(aside);
                File.Move(ownPath, aside);
                r.Aside = aside;
            }
            catch (Exception ex)
            {
                r.AsideError = ex.Message;
            }
            return r;
        }

        /// A file in the plugin folder that could be a second copy of this
        /// plugin: "ForestOverlay*.dll" other than ForestOverlay.dll. The
        /// caller confirms by assembly name before touching it.
        public static bool MaybeStrayCopy(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            if (string.Equals(fileName, PluginFile, StringComparison.OrdinalIgnoreCase)) return false;
            return fileName.StartsWith(AssemblyName, StringComparison.OrdinalIgnoreCase) &&
                   fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        }

        /// A stale "<other name>.dll.pending" left by v0.23.6 or older,
        /// which the patcher never installs.
        public static bool IsStalePending(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            if (!fileName.EndsWith(".dll" + PendingSuffix, StringComparison.OrdinalIgnoreCase)) return false;
            return MaybeStrayCopy(fileName.Substring(0, fileName.Length - PendingSuffix.Length));
        }
    }
}
