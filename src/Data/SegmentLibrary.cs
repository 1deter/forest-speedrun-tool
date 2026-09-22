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
                    if (LoadFile(files[i])) fileCount++;

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

        private static int Compare(Segment a, Segment b)
        {
            int c = string.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
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
            Segment current = null;
            int added = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                if (line[0] == '[')
                {
                    if (Commit(current, fileName, i)) added++;
                    current = new Segment();
                    current.SourceFile = fileName;
                    continue;
                }

                if (current == null) continue;   // stray line before any header

                int eq = line.IndexOf('=');
                if (eq < 0)
                {
                    _log.LogWarning(fileName + ":" + (i + 1) + " ignored (no '='): " + line);
                    continue;
                }

                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string value = line.Substring(eq + 1).Trim();

                Apply(current, key, value, fileName, i + 1);
            }

            if (Commit(current, fileName, lines.Length)) added++;
            return added > 0;
        }

        private bool Commit(Segment s, string fileName, int line)
        {
            if (s == null) return false;

            if (!s.IsValid)
            {
                _log.LogWarning(fileName + ": segment '" +
                                (s.Id.Length > 0 ? s.Id : "(no id)") +
                                "' skipped - needs id, start and end.");
                return false;
            }

            if (ById(s.Id) != null)
            {
                _log.LogWarning(fileName + ": duplicate segment id '" + s.Id + "' skipped.");
                return false;
            }

            if (s.Name.Length == 0) s.Name = s.Id;
            _all.Add(s);
            return true;
        }

        private void Apply(Segment s, string key, string value, string file, int line)
        {
            switch (key)
            {
                case "id": s.Id = value; return;
                case "name": s.Name = value; return;
                case "category": s.Category = value.Length > 0 ? value : "Segments"; return;
                case "notes": s.Notes = value; return;

                case "spawn":
                    {
                        string[] p = TriggerParser.Split(value);
                        float x, y, z;
                        if (p.Length >= 3 && TriggerParser.F(p[0], out x) && TriggerParser.F(p[1], out y) && TriggerParser.F(p[2], out z))
                        {
                            s.SpawnPosition = new Vector3(x, y, z);
                            s.HasSpawn = true;
                            if (p.Length > 3) TriggerParser.F(p[3], out s.SpawnYaw);
                            if (p.Length > 4) TriggerParser.F(p[4], out s.SpawnPitch);
                        }
                        else Warn(file, line, "bad spawn: " + value);
                        return;
                    }

                case "start":
                    if (!TriggerParser.Parse(value, out s.Start)) Warn(file, line, "bad start: " + value);
                    return;

                case "end":
                    if (!TriggerParser.Parse(value, out s.End)) Warn(file, line, "bad end: " + value);
                    return;

                case "check":
                case "checkpoint":
                    {
                        Trigger t;
                        if (TriggerParser.Parse(value, out t)) s.Checkpoints.Add(t);
                        else Warn(file, line, "bad checkpoint: " + value);
                        return;
                    }

                default:
                    Warn(file, line, "unknown key '" + key + "'");
                    return;
            }
        }

        private void Warn(string file, int line, string message)
        {
            _log.LogWarning(file + ":" + line + " " + message);
        }

        // ------------------------------------------------------------------
        /// Writes a segment captured in game. Appended to the user's own
        /// file, kept apart from contributed route sets for the same
        /// reason personal spots are.
        public bool Append(Segment s)
        {
            try
            {
                if (!Directory.Exists(_folder)) Directory.CreateDirectory(_folder);
                string path = Path.Combine(_folder, "my-segments.txt");

                StringBuilder sb = new StringBuilder();
                sb.Append(Environment.NewLine).Append("[segment]").Append(Environment.NewLine);
                sb.Append("id       = ").Append(s.Id).Append(Environment.NewLine);
                sb.Append("name     = ").Append(s.Name).Append(Environment.NewLine);
                sb.Append("category = ").Append(s.Category).Append(Environment.NewLine);

                if (s.HasSpawn)
                {
                    sb.Append("spawn    = ")
                      .Append(TriggerParser.Num(s.SpawnPosition.x)).Append(' ')
                      .Append(TriggerParser.Num(s.SpawnPosition.y)).Append(' ')
                      .Append(TriggerParser.Num(s.SpawnPosition.z)).Append(' ')
                      .Append(TriggerParser.Num(s.SpawnYaw)).Append(' ')
                      .Append(TriggerParser.Num(s.SpawnPitch)).Append(Environment.NewLine);
                }

                sb.Append("start    = ").Append(TriggerParser.Write(s.Start)).Append(Environment.NewLine);
                for (int i = 0; i < s.Checkpoints.Count; i++)
                    sb.Append("check    = ").Append(TriggerParser.Write(s.Checkpoints[i])).Append(Environment.NewLine);
                sb.Append("end      = ").Append(TriggerParser.Write(s.End)).Append(Environment.NewLine);

                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Could not append segment: " + ex.Message);
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
