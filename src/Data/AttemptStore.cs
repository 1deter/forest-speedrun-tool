using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Persists attempts so a best time survives a restart, and so saved
    // run lines can be raced later.
    //
    // Same reasoning as LocationLibrary: a line-oriented text format,
    // because these files are meant to be shareable and diffable, and
    // net35 has no framework JSON. One file per attempt, grouped in a
    // folder per anchor, so a folder is a "track" and can be zipped and
    // sent to someone else as-is.
    //
    // The file format itself is Data/AttemptFormat (pure, tested).
    // ------------------------------------------------------------------
    public sealed class AttemptStore
    {
        private readonly ManualLogSource _log;
        private readonly string _root;

        public string Root { get { return _root; } }

        public AttemptStore(ManualLogSource log, string configDirectory)
        {
            _log = log;
            _root = Path.Combine(configDirectory, "runs");
        }

        private string FolderFor(string anchorLabel)
        {
            return Path.Combine(_root, Sanitise(anchorLabel));
        }

        public bool Save(Attempt attempt)
        {
            if (attempt == null || attempt.Samples.Count == 0) return false;

            try
            {
                string dir = FolderFor(attempt.AnchorLabel);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string name = attempt.RecordedUtc.ToString("yyyyMMdd_HHmmss") + "_" +
                              attempt.Duration.ToString("F3", CultureInfo.InvariantCulture) + ".run";

                File.WriteAllText(Path.Combine(dir, name), AttemptFormat.Write(attempt), Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Could not save attempt: " + ex.Message);
                return false;
            }
        }

        public List<Attempt> LoadAll(string anchorLabel)
        {
            List<Attempt> result = new List<Attempt>();

            try
            {
                string dir = FolderFor(anchorLabel);
                if (!Directory.Exists(dir)) return result;

                string[] files = Directory.GetFiles(dir, "*.run");
                for (int i = 0; i < files.Length; i++)
                {
                    Attempt a = Load(files[i]);
                    if (a != null) result.Add(a);
                }

                result.Sort(CompareByTime);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Could not list attempts: " + ex.Message);
            }

            return result;
        }

        /// How many saved attempts count for `route` - what changing the
        /// route would retire. Reads only each file's header, not its
        /// samples. No route line counts as current, as in LoadAll's caller.
        public int CountOnRoute(string anchorLabel, string route)
        {
            int n = 0;
            try
            {
                string dir = FolderFor(anchorLabel);
                if (!Directory.Exists(dir)) return 0;

                string[] files = Directory.GetFiles(dir, "*.run");
                for (int i = 0; i < files.Length; i++)
                {
                    string r = RouteOf(files[i]);
                    if (r.Length == 0 || r == route) n++;
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning("Could not count attempts: " + ex.Message);
            }
            return n;
        }

        /// How many attempt files the segment has, any route.
        public int CountFiles(string anchorLabel)
        {
            try
            {
                string dir = FolderFor(anchorLabel);
                return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.run").Length : 0;
            }
            catch (Exception) { return 0; }
        }

        /// The saved attempt files' texts, oldest name first - for a
        /// segment export (Data/SegmentBundle).
        public List<string> RunTexts(string anchorLabel)
        {
            List<string> texts = new List<string>();
            try
            {
                string dir = FolderFor(anchorLabel);
                if (!Directory.Exists(dir)) return texts;
                string[] files = Directory.GetFiles(dir, "*.run");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < files.Length; i++) texts.Add(File.ReadAllText(files[i], Encoding.UTF8));
            }
            catch (Exception ex)
            {
                _log.LogWarning("Could not read attempts for export: " + ex.Message);
            }
            return texts;
        }

        /// Writes an imported attempt under its own name. False when a file
        /// of that name is there already (the same attempt) - never replaced.
        public bool ImportRun(string anchorLabel, string fileName, string text)
        {
            string dir = FolderFor(anchorLabel);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, Sanitise(fileName));
            if (File.Exists(path)) return false;
            File.WriteAllText(path, text, Encoding.UTF8);
            return true;
        }

        private static string RouteOf(string path)
        {
            using (StreamReader r = new StreamReader(path, Encoding.UTF8))
            {
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    if (line.StartsWith("route|")) return line.Substring(6).Trim();
                    if (line.StartsWith("s|") || line.StartsWith("v|")) break;   // header over
                }
            }
            return "";
        }

        private static int CompareByTime(Attempt a, Attempt b)
        {
            return a.RecordedUtc.CompareTo(b.RecordedUtc);
        }

        private Attempt Load(string path)
        {
            try
            {
                return AttemptFormat.Parse(File.ReadAllLines(path));
            }
            catch (Exception ex)
            {
                _log.LogWarning("Could not read " + Path.GetFileName(path) + ": " + ex.Message);
                return null;
            }
        }

        // Anchor labels become folder names, and they come from
        // user-authored location files, so they can contain anything.
        private static string Sanitise(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unnamed";

            char[] bad = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder(s.Length);

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool ok = true;
                for (int b = 0; b < bad.Length; b++)
                    if (c == bad[b]) { ok = false; break; }

                sb.Append(ok ? c : '_');
            }

            string cleaned = sb.ToString().Trim();
            return cleaned.Length == 0 ? "unnamed" : cleaned;
        }
    }
}
