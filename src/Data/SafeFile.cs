using System;
using System.IO;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // A text file replaced so a crash mid-write cannot leave it half
    // written (T-0217: the Practice spot file is written on every edit
    // now). No BepInEx or Unity here, so it is tested against real temp
    // folders, as patcher/PendingSwap is.
    //
    //   1. the new text   -> <file>.tmp   (the old file untouched)
    //   2. <file>         -> <file>.bak
    //   3. <file>.tmp     -> <file>
    //   4. <file>.bak deleted
    //
    // Delete + Move, as the savestate writer and the updater do: those
    // already run on the game's Mono; File.Replace is not proved there.
    // Recover puts the newest whole copy back after a crash at any step:
    // a .bak exists only once the .tmp is complete, so a .tmp beside a
    // .bak is whole, and a lone .tmp beside its file is a broken step 1.
    // ------------------------------------------------------------------
    public static class SafeFile
    {
        public const string TempSuffix = ".tmp";
        public const string BackupSuffix = ".bak";

        public static void WriteAllText(string path, string text, Encoding encoding)
        {
            string tmp = path + TempSuffix;
            string bak = path + BackupSuffix;

            File.WriteAllText(tmp, text, encoding);

            if (File.Exists(path))
            {
                if (File.Exists(bak)) File.Delete(bak);
                File.Move(path, bak);
            }

            try { File.Move(tmp, path); }
            catch (Exception)
            {
                // The old copy goes back rather than leaving no file.
                if (!File.Exists(path) && File.Exists(bak)) File.Move(bak, path);
                throw;
            }

            if (File.Exists(bak)) File.Delete(bak);
        }

        /// Tidies what a crash mid-write left beside `path`. Returns what
        /// it did ("" for nothing), for the caller's log line.
        public static string Recover(string path)
        {
            string tmp = path + TempSuffix;
            string bak = path + BackupSuffix;
            bool hasTmp = File.Exists(tmp), hasBak = File.Exists(bak);
            if (!hasTmp && !hasBak) return "";

            if (!File.Exists(path))
            {
                if (hasTmp && hasBak)
                {
                    // Crashed between steps 2 and 3: the .tmp is whole and newer.
                    File.Move(tmp, path);
                    File.Delete(bak);
                    return "finished an interrupted write";
                }
                if (hasBak)
                {
                    File.Move(bak, path);
                    return "put the previous copy back";
                }
                // A first write that never finished: nothing older to lose.
                File.Delete(tmp);
                return "dropped an unfinished first write";
            }

            // The file is whole: crashed in step 1 (the .tmp may be cut
            // short) or step 4 (the .bak is the older copy).
            if (hasTmp) File.Delete(tmp);
            if (hasBak) File.Delete(bak);
            return "removed a leftover " + (hasTmp ? TempSuffix : BackupSuffix);
        }
    }
}
