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
    // Loads segment definitions from text files.
    //
    // WHY A BLOCK FORMAT RATHER THAN THE PIPE FORMAT LOCATIONS USE
    // A location is a fixed set of fields, so one line works. A segment
    // has a variable number of checkpoints and several kinds of trigger,
    // and cramming that onto one line would make it unreadable and
    // unmergeable - the opposite of why the text format was chosen. So
    // segments use key = value lines under a [segment] header: still
    // diffable, still hand-editable, still no serialiser.
    //
    //   [segment]
    //   id       = plane-to-cave5
    //   name     = Plane Crash -> Cave 5
    //   category = Main route
    //   spawn    = -1000.5 90.2 550.1 180 0
    //   start    = zone -1000.5 90.2 550.1 3
    //   check    = zone 120 40 600 5
    //   check    = item 78 >= 1
    //   end      = zone 123 45 678 5
    //   notes    = drop down on the left
    //
    // Trigger syntax:
    //   zone  <x> <y> <z> <radius>
    //   item  <id> <op> <amount>        op is >=, <= or ==
    //   event <name>
    //   manual
    // ------------------------------------------------------------------
    public sealed class SegmentLibrary
    {
        private readonly ManualLogSource _log;
        private readonly string _folder;

        private readonly List<Segment> _all = new List<Segment>();

        public IList<Segment> All { get { return _all; } }
        public string Folder { get { return _folder; } }
        public string Status { get; private set; }

        public SegmentLibrary(ManualLogSource log, string configDirectory)
        {
            _log = log;
            _folder = Path.Combine(configDirectory, "segments");
            Status = "(not loaded)";
        }

        public void Reload()
        {
            _all.Clear();

            try
            {
                if (!Directory.Exists(_folder))
                {
                    Directory.CreateDirectory(_folder);
                    WriteReadme();
                }

                string[] files = Directory.GetFiles(_folder, "*.txt");
                int fileCount = 0;

                for (int i = 0; i < files.Length; i++)
                {
                    // Our own README sits in this folder and is not data.
                    if (string.Equals(Path.GetFileName(files[i]), "README.txt", StringComparison.OrdinalIgnoreCase)) continue;
                    if (LoadFile(files[i])) fileCount++;
                }

                fileCount += ImportLegacySpots();

                _all.Sort(Compare);

                Status = _all.Count + " segments / " + fileCount + " file(s)";
                _log.LogInfo("Segments: " + Status + " from " + _folder);
            }
            catch (Exception ex)
            {
                Status = "load failed - see log";
                _log.LogWarning("Segment load failed: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // Legacy import.
        //
        // Spots used to live in their own folder and format before spots
        // and segments were recognised as the same thing. Those files are
        // still read, as spawn-only entries, so contributed sets and
        // personal captures are not silently lost. They are marked
        // read-only-ish by keeping their source file, so saving writes
        // back where they came from.
        private int ImportLegacySpots()
        {
            string legacy = Path.Combine(Path.GetDirectoryName(_folder), "locations");
            if (!Directory.Exists(legacy)) return 0;

            int files = 0;

            foreach (string path in Directory.GetFiles(legacy, "*.txt"))
            {
                string name = Path.GetFileName(path);
                if (string.Equals(name, "README.txt", StringComparison.OrdinalIgnoreCase)) continue;

                string[] lines;
                try { lines = File.ReadAllLines(path); }
                catch (Exception) { continue; }

                int added = 0;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line[0] == (char)35) continue;

                    Segment s = ParseLegacySpot(line);
                    if (s == null) continue;

                    s.SourceFile = name;

                    // Already migrated: saving from the Practice tab writes
                    // legacy spots into segments/ under the same id, and the
                    // old file stays. Not a conflict - skip without warning.
                    if (ById(s.Id) != null) continue;

                    if (Commit(s, name)) added++;
                }

                if (added > 0) files++;
            }

            return files;
        }

        /// category | name | x | y | z | yaw | notes | pitch
        private static Segment ParseLegacySpot(string line)
        {
            string[] p = line.Split((char)124);
            if (p.Length < 5) return null;

            float x, y, z;
            if (!TriggerParser.F(p[2], out x)) return null;
            if (!TriggerParser.F(p[3], out y)) return null;
            if (!TriggerParser.F(p[4], out z)) return null;

            Segment s = new Segment();
            s.Category = p[0].Trim();
            s.Name = p[1].Trim();
            if (s.Name.Length == 0) return null;
            if (s.Category.Length == 0) s.Category = "Spots";

            s.SpawnPosition = new Vector3(x, y, z);
            s.HasSpawn = true;
            if (p.Length > 5) TriggerParser.F(p[5], out s.SpawnYaw);
            if (p.Length > 6) s.Notes = p[6].Trim();
            if (p.Length > 7) TriggerParser.F(p[7], out s.SpawnPitch);

            // Legacy spots had no id; derive a stable one from the name so
            // times recorded against it survive a reload.
            s.Id = "spot." + Slug(s.Category) + "." + Slug(s.Name);
            return s;
        }

        private static string Slug(string text)
        {
            StringBuilder sb = new StringBuilder(text.Length);

            for (int i = 0; i < text.Length; i++)
            {
                char c = char.ToLowerInvariant(text[i]);

                if ((c >= (char)97 && c <= (char)122) || (c >= (char)48 && c <= (char)57)) sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != (char)45) sb.Append((char)45);
            }

            string slug = sb.ToString().Trim((char)45);
            return slug.Length == 0 ? "unnamed" : slug;
        }

        /// Category, then name, both ignoring case and surrounding spaces -
        /// the Practice list groups by the same rule (SameCategory).
        public static int Compare(Segment a, Segment b)
        {
            int c = string.Compare(CategoryKey(a.Category), CategoryKey(b.Category), StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        public static string CategoryKey(string category)
        {
            return category == null ? "" : category.Trim();
        }

        public static bool SameCategory(string a, string b)
        {
            return string.Equals(CategoryKey(a), CategoryKey(b), StringComparison.OrdinalIgnoreCase);
        }

        public Segment ById(string id)
        {
            for (int i = 0; i < _all.Count; i++)
                if (string.Equals(_all[i].Id, id, StringComparison.OrdinalIgnoreCase)) return _all[i];
            return null;
        }

        // ------------------------------------------------------------------
        private bool LoadFile(string path)
        {
            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch (Exception ex)
            {
                _log.LogWarning("Could not read " + Path.GetFileName(path) + ": " + ex.Message);
                return false;
            }

            string fileName = Path.GetFileName(path);

            List<Segment> parsed = SegmentFormat.ParseAll(lines,
                delegate(int line, string message) { Warn(fileName, line, message); });

            int added = 0;
            for (int i = 0; i < parsed.Count; i++)
            {
                parsed[i].SourceFile = fileName;
                if (Commit(parsed[i], fileName)) added++;
            }

            return added > 0;
        }

        private bool Commit(Segment s, string fileName)
        {
            if (s == null) return false;

            if (!s.IsValid)
            {
                _log.LogWarning(fileName + ": segment (" +
                                (s.Id.Length > 0 ? s.Id : "no id") +
                                ") skipped - needs id, start and end.");
                return false;
            }

            if (ById(s.Id) != null)
            {
                _log.LogWarning(fileName + ": duplicate segment id " + s.Id + " skipped.");
                return false;
            }

            if (s.Name.Length == 0) s.Name = s.Id;
            _all.Add(s);
            return true;
        }

        private void Warn(string file, int line, string message)
        {
            _log.LogWarning(file + ":" + line + " " + message);
        }

        // ------------------------------------------------------------------
        // Editing.
        //
        // The editor works on the in-memory list and then rewrites whole
        // files, rather than appending. Appending cannot express an edit or
        // a delete, and a half-updated route file is worse than none.
        //
        // A segment remembers the file it came from, so editing a
        // contributed set writes back to that set; anything new lands in
        // the user's own file and is never mixed into a shared one.
        // ------------------------------------------------------------------
        public const string UserFileName = "my-segments.txt";

        public void Add(Segment s)
        {
            if (s == null) return;
            if (s.SourceFile == null || s.SourceFile.Length == 0) s.SourceFile = UserFileName;
            _all.Add(s);
        }

        public void Remove(Segment s)
        {
            if (s != null) _all.Remove(s);
        }

        /// True when the id is free (or already belongs to `owner`).
        public bool IsIdAvailable(string id, Segment owner)
        {
            if (string.IsNullOrEmpty(id)) return false;

            for (int i = 0; i < _all.Count; i++)
            {
                if (ReferenceEquals(_all[i], owner)) continue;
                if (string.Equals(_all[i].Id, id, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        /// Rewrites one file from whatever is currently in memory for it.
        /// Passing a file with no segments left deletes it, which is how a
        /// delete of the last segment tidies up after itself.
        public bool SaveFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) fileName = UserFileName;

            try
            {
                if (!Directory.Exists(_folder)) Directory.CreateDirectory(_folder);
                string path = Path.Combine(_folder, fileName);

                List<Segment> mine = new List<Segment>();
                for (int i = 0; i < _all.Count; i++)
                    if (string.Equals(_all[i].SourceFile, fileName, StringComparison.OrdinalIgnoreCase))
                        mine.Add(_all[i]);

                if (mine.Count == 0)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return true;
                }

                StringBuilder sb = new StringBuilder();
                sb.Append("# ForestOverlay segments").Append(Environment.NewLine);
                sb.Append("# Written by the in-game editor. Hand-editing is fine;").Append(Environment.NewLine);
                sb.Append("# see README.txt for the format.").Append(Environment.NewLine);

                for (int i = 0; i < mine.Count; i++)
                    SegmentFormat.WriteSegment(sb, mine[i], Environment.NewLine);

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                _log.LogInfo("Wrote " + mine.Count + " segment(s) to " + fileName);
                return true;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Could not save segments: " + ex.Message);
                return false;
            }
        }

        private void WriteReadme()
        {
            string nl = Environment.NewLine;
            File.WriteAllText(Path.Combine(_folder, "README.txt"),
                "ForestOverlay - practice segments" + nl +
                "=================================" + nl + nl +
                "A segment is a named piece of run to practise: a start, an end, and" + nl +
                "optional checkpoints that split. Every .txt file here is loaded and" + nl +
                "merged, so a shared route set is just a file you drop in." + nl + nl +
                "  [segment]" + nl +
                "  id       = plane-to-cave5" + nl +
                "  name     = Plane Crash -> Cave 5" + nl +
                "  category = Main route" + nl +
                "  spawn    = -1000.5 90.2 550.1 180 0" + nl +
                "  start    = zone -1000.5 90.2 550.1 3" + nl +
                "  check    = zone 120 40 600 5" + nl +
                "  end      = zone 123 45 678 5" + nl +
                "  notes    = drop down on the left" + nl + nl +
                "id must be unique - it is what run comparisons are keyed on, so" + nl +
                "keep it stable once people have times against it." + nl + nl +
                "Triggers:" + nl +
                "  zone  <x> <y> <z> <radius>     entering the sphere fires it" + nl +
                "  item  <id> <op> <amount>       op is >=, <= or ==" + nl +
                "  event <name>                   a named game event" + nl +
                "  manual                         only fires on the hotkey" + nl + nl +
                "spawn is optional: x y z [yaw] [pitch]. Without it the segment can" + nl +
                "still be timed, just not practised from a teleport." + nl + nl +
                "Use a dot for decimals, never a comma." + nl,
                Encoding.UTF8);

            _log.LogInfo("Wrote segments README to " + _folder);
        }
    }
}
