using System;
using System.Collections.Generic;
using System.Globalization;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Unity's crash folders and the session log that belongs to one
    // (T-0276). A native crash leaves crash.dmp + error.log in a folder
    // beside TheForest.exe named after the process start,
    // yyyy-MM-dd_HHmmss (2026-10-04_083030: a session from 08:30 that
    // crashed at 13:12). Our kept session logs are named after the
    // plugin's start (LogArchive), a few seconds to a minute later. Kept
    // logs rotate (3 sessions), so at the next launch the crashed
    // session's log is copied into its crash folder before it can go.
    //
    // Pure: no file system, tested.
    // ------------------------------------------------------------------
    public static class CrashFolders
    {
        public const string KeptLogName = "ForestOverlay-LogOutput.log";
        public const string KeptUnityLogName = "ForestOverlay-unity.log";
        private const string StampFormat = "yyyy-MM-dd_HHmmss";

        /// How far a session log's start may sit from the folder's:
        /// the plugin starts after the process, never long before.
        public static readonly TimeSpan Before = TimeSpan.FromMinutes(1);
        public static readonly TimeSpan After = TimeSpan.FromMinutes(10);

        public static bool TryParse(string folderName, out DateTime start)
        {
            start = DateTime.MinValue;
            if (folderName == null || folderName.Length != StampFormat.Length) return false;
            return DateTime.TryParseExact(folderName, StampFormat, CultureInfo.InvariantCulture,
                                          DateTimeStyles.None, out start);
        }

        /// The kept session log (LogArchive name) for a crash folder:
        /// the earliest one that started within Before / After of it, or
        /// null.
        public static string SessionFor(string folderName, IList<string> sessionFiles)
        {
            DateTime crashStart;
            if (!TryParse(folderName, out crashStart) || sessionFiles == null) return null;
            string best = null;
            DateTime bestStart = DateTime.MaxValue;
            for (int i = 0; i < sessionFiles.Count; i++)
            {
                DateTime s;
                if (!LogArchive.TryParseStart(sessionFiles[i], out s)) continue;
                if (s < crashStart - Before || s > crashStart + After) continue;
                if (s < bestStart)
                {
                    best = sessionFiles[i];
                    bestStart = s;
                }
            }
            return best;
        }
    }
}
