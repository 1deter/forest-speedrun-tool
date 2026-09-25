using System;
using System.Collections.Generic;
using System.Globalization;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Names and pruning for the kept session logs (Core/LogKeeper).
    //
    // BepInEx truncates LogOutput.log in its preloader, before any patcher
    // or plugin runs, so the previous session cannot be copied at startup.
    // Instead each session is mirrored into its own file as it runs:
    // LogOutput-yyyy-MM-dd_HH-mm-ss.log. The timestamp sorts as text, so
    // the oldest files are simply the first ones.
    //
    // Pure: no file system, tested.
    // ------------------------------------------------------------------
    public static class LogArchive
    {
        public const string Prefix = "LogOutput-";
        public const string Suffix = ".log";
        private const string StampFormat = "yyyy-MM-dd_HH-mm-ss";

        public static string SessionFileName(DateTime start)
        {
            return Prefix + start.ToString(StampFormat, CultureInfo.InvariantCulture) + Suffix;
        }

        /// True for a name SessionFileName could have produced.
        public static bool IsSessionFile(string name)
        {
            if (name == null) return false;
            if (!name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (!name.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase)) return false;
            string stamp = name.Substring(Prefix.Length, name.Length - Prefix.Length - Suffix.Length);
            DateTime unused;
            return DateTime.TryParseExact(stamp, StampFormat, CultureInfo.InvariantCulture,
                                          DateTimeStyles.None, out unused);
        }

        /// The session files to delete so that at most keepPrevious remain
        /// besides current (the running session's file, never deleted).
        /// Other files in the folder are left alone.
        public static List<string> ToDelete(IList<string> names, string current, int keepPrevious)
        {
            if (keepPrevious < 0) keepPrevious = 0;
            List<string> sessions = new List<string>();
            for (int i = 0; i < names.Count; i++)
            {
                string n = names[i];
                if (!IsSessionFile(n)) continue;
                if (current != null && string.Equals(n, current, StringComparison.OrdinalIgnoreCase)) continue;
                sessions.Add(n);
            }
            sessions.Sort(StringComparer.OrdinalIgnoreCase);

            List<string> delete = new List<string>();
            int excess = sessions.Count - keepPrevious;
            for (int i = 0; i < excess; i++) delete.Add(sessions[i]);
            return delete;
        }
    }
}
