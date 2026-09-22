using System;
using System.IO;

namespace ForestOverlay.Updater
{
    // ------------------------------------------------------------------
    // Moves a staged download into place. No BepInEx or Unity here, so it
    // is tested directly against real temp folders.
    //
    //   ForestOverlay.dll.pending  -> ForestOverlay.dll
    //   ForestOverlay.dll          -> ForestOverlay.dll.bak   (one level)
    //
    // The rule is that a failure at any step leaves a loadable plugin:
    // a download that fails validation is renamed .rejected and never
    // touches the installed DLL, and a failed move puts the backup back.
    // An updater that bricks the install is worse than none, because the
    // runner then has no working overlay to tell them what happened.
    //
    // None of these suffixes end in .dll, so BepInEx never tries to load
    // a backup or a half-downloaded file as a plugin.
    // ------------------------------------------------------------------
    public static class PendingSwap
    {
        public const string PendingSuffix = ".pending";
        public const string BackupSuffix = ".bak";
        public const string RejectedSuffix = ".rejected";

        public enum Result { NothingPending, Applied, Rejected, Failed }

        /// `target` is the installed DLL. `isValid` gets the pending file's
        /// path and decides whether it is really our plugin; it may be null.
        public static Result Apply(string target, Func<string, bool> isValid, Action<string> log)
        {
            string pending = target + PendingSuffix;
            string backup = target + BackupSuffix;

            if (!File.Exists(pending)) return Result.NothingPending;

            if (!LooksLikeDll(pending) || (isValid != null && !SafeValid(isValid, pending)))
            {
                Replace(pending, target + RejectedSuffix);
                log("Staged update is not a valid ForestOverlay.dll - kept the installed version.");
                return Result.Rejected;
            }

            bool backedUp = false;

            try
            {
                if (File.Exists(target))
                {
                    if (File.Exists(backup)) File.Delete(backup);
                    File.Move(target, backup);
                    backedUp = true;
                }

                File.Move(pending, target);
                log("Installed staged update" + (backedUp ? " (previous version kept as " + Path.GetFileName(backup) + ")" : "") + ".");
                return Result.Applied;
            }
            catch (Exception ex)
            {
                if (backedUp && !File.Exists(target))
                {
                    try { File.Move(backup, target); }
                    catch (Exception) { }
                }

                log("Could not install staged update: " + ex.Message + " - kept the installed version.");
                return Result.Failed;
            }
        }

        /// A PE file starts with "MZ". Catches the classic failure of
        /// saving an HTML error page as the plugin.
        private static bool LooksLikeDll(string path)
        {
            try
            {
                using (FileStream s = File.OpenRead(path))
                    return s.Length > 2 && s.ReadByte() == 0x4D && s.ReadByte() == 0x5A;
            }
            catch (Exception) { return false; }
        }

        private static bool SafeValid(Func<string, bool> isValid, string path)
        {
            try { return isValid(path); }
            catch (Exception) { return false; }
        }

        private static void Replace(string from, string to)
        {
            try
            {
                if (File.Exists(to)) File.Delete(to);
                File.Move(from, to);
            }
            catch (Exception) { }
        }
    }
}
