using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Data
{
    public sealed class Location
    {
        public string Category;
        public string Name;
        public Vector3 Position;
        public float Yaw;
        public float Pitch;
        public string Notes;
        public string SourceFile;

        // Cached so the GUI never builds a label string inside OnGUI.
        public GUIContent Label;
        public GUIContent Tooltip;
    }

    // ------------------------------------------------------------------
    // Loads practice locations from plain text files.
    //
    // WHY A TEXT FORMAT AND NOT JSON
    // These files are meant to be contributed by runners through pull
    // requests. A line-oriented format diffs cleanly, merges without
    // conflicts when two people add spots at once, and can be edited in
    // Notepad by someone who has never written code. It also needs no
    // serialiser, which matters on net35 where there is no framework JSON
    // and pulling one in would mean shipping another DLL.
    //
    // Every file in the locations folder is loaded and merged, so adding
    // a set is literally dropping a file in - nothing needs recompiling
    // and no index has to be updated. That is what lets the list grow as
    // the community adds to it.
    //
    //   category | name | x | y | z | yaw | notes | pitch
    //
    // Blank lines and lines starting with # are ignored. yaw and notes
    // are optional.
    // ------------------------------------------------------------------
    public sealed class LocationLibrary
    {
        private readonly ManualLogSource _log;
        private readonly string _folder;

        private readonly List<Location> _all = new List<Location>();
        private readonly List<string> _categories = new List<string>();

        public const string UserFileName = "my-spots.txt";

        public IList<Location> All { get { return _all; } }
        public IList<string> Categories { get { return _categories; } }
        public string Folder { get { return _folder; } }
        public string Status { get; private set; }

        public LocationLibrary(ManualLogSource log, string configDirectory)
        {
            _log = log;
            _folder = Path.Combine(configDirectory, "locations");
            Status = "(not loaded)";
        }

        public void Reload()
        {
            _all.Clear();
            _categories.Clear();

            try
            {
                if (!Directory.Exists(_folder))
                {
                    Directory.CreateDirectory(_folder);
                    WriteStarterFile();
                }

                string[] files = Directory.GetFiles(_folder, "*.txt");
                int fileCount = 0;

                for (int i = 0; i < files.Length; i++)
                {
                    if (LoadFile(files[i])) fileCount++;
                }

                _all.Sort(CompareLocations);
                RebuildCategories();

                Status = _all.Count + " spots / " + fileCount + " file(s)";
                _log.LogInfo("Locations: " + Status + " from " + _folder);
            }
            catch (Exception ex)
            {
                Status = "load failed - see log";
                _log.LogWarning("Location load failed: " + ex.Message);
            }
        }

        private static int CompareLocations(Location a, Location b)
        {
            int c = string.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        private void RebuildCategories()
        {
            for (int i = 0; i < _all.Count; i++)
            {
                string c = _all[i].Category;
                if (!_categories.Contains(c)) _categories.Add(c);
            }
        }

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
            int added = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                Location loc = ParseLine(line, fileName);
                if (loc == null)
                {
                    _log.LogWarning(fileName + ":" + (i + 1) + " skipped (malformed): " + line);
                    continue;
                }

                _all.Add(loc);
                added++;
            }

            return added > 0;
        }

        private static Location ParseLine(string line, string fileName)
        {
            string[] parts = line.Split('|');
            if (parts.Length < 5) return null;

            float x, y, z;
            if (!TryFloat(parts[2], out x)) return null;
            if (!TryFloat(parts[3], out y)) return null;
            if (!TryFloat(parts[4], out z)) return null;

            float yaw = 0f;
            if (parts.Length > 5) TryFloat(parts[5], out yaw);

            Location loc = new Location();
            loc.Category = parts[0].Trim();
            loc.Name = parts[1].Trim();
            loc.Position = new Vector3(x, y, z);
            loc.Yaw = yaw;
            loc.Notes = parts.Length > 6 ? parts[6].Trim() : "";

            // Pitch is column 8, appended rather than inserted so files
            // written before it existed still parse. Missing means level.
            if (parts.Length > 7) TryFloat(parts[7], out loc.Pitch);
            loc.SourceFile = fileName;

            if (loc.Category.Length == 0) loc.Category = "Uncategorised";
            if (loc.Name.Length == 0) return null;

            BuildLabels(loc);
            return loc;
        }

        private static void BuildLabels(Location loc)
        {
            loc.Label = new GUIContent(loc.Name);

            string tip = loc.Position.x.ToString("F0") + ", " +
                         loc.Position.y.ToString("F0") + ", " +
                         loc.Position.z.ToString("F0");
            if (loc.Notes.Length > 0) tip += "  -  " + loc.Notes;
            tip += "   [" + loc.SourceFile + "]";
            loc.Tooltip = new GUIContent(tip);
        }

        // InvariantCulture is deliberate. These files get shared between
        // runners, and on a machine with a comma decimal separator a
        // locale-sensitive parse would silently reject every coordinate.
        private static bool TryFloat(string s, out float value)
        {
            return float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string Fmt(float f)
        {
            return f.ToString("F2", CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------
        // Appends a captured spot to the personal file. Kept separate from
        // contributed sets so a git pull never clobbers personal spots and
        // a personal file never ends up in a pull request.
        public bool Append(string category, string name, Vector3 pos, float yaw, string notes, float pitch)
        {
            try
            {
                if (!Directory.Exists(_folder)) Directory.CreateDirectory(_folder);
                string path = Path.Combine(_folder, UserFileName);

                if (!File.Exists(path))
                {
                    File.WriteAllText(path,
                        "# ForestOverlay - personal spots" + Environment.NewLine +
                        "# Contributed sets live in other files here, so updating them" + Environment.NewLine +
                        "# will never overwrite what you capture." + Environment.NewLine +
                        "# category | name | x | y | z | yaw | notes | pitch" + Environment.NewLine,
                        Encoding.UTF8);
                }

                StringBuilder sb = new StringBuilder();
                sb.Append(Sanitise(category)).Append(" | ")
                  .Append(Sanitise(name)).Append(" | ")
                  .Append(Fmt(pos.x)).Append(" | ")
                  .Append(Fmt(pos.y)).Append(" | ")
                  .Append(Fmt(pos.z)).Append(" | ")
                  .Append(Fmt(yaw)).Append(" | ")
                  .Append(Sanitise(notes)).Append(" | ")
                  .Append(Fmt(pitch));

                File.AppendAllText(path, sb.ToString() + Environment.NewLine, Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Could not append location: " + ex.Message);
                return false;
            }
        }

        // The pipe is the field separator, so it cannot survive in a field.
        private static string Sanitise(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        private void WriteStarterFile()
        {
            string path = Path.Combine(_folder, "README.txt");
            File.WriteAllText(path,
                "ForestOverlay - practice locations" + Environment.NewLine +
                "==================================" + Environment.NewLine +
                Environment.NewLine +
                "Every .txt file in this folder is loaded and merged, so you can add a" + Environment.NewLine +
                "set by dropping a file in here. Nothing needs recompiling." + Environment.NewLine +
                Environment.NewLine +
                "One spot per line:" + Environment.NewLine +
                Environment.NewLine +
                "    category | name | x | y | z | yaw | notes | pitch" + Environment.NewLine +
                Environment.NewLine +
                "  * yaw, notes and pitch are optional" + Environment.NewLine +
                "  * blank lines and lines starting with # are ignored" + Environment.NewLine +
                "  * use a dot for decimals, not a comma" + Environment.NewLine +
                Environment.NewLine +
                "Example:" + Environment.NewLine +
                Environment.NewLine +
                "    Start | Plane Crash | -1000.50 | 90.20 | 550.10 | 180 | opening spawn" + Environment.NewLine +
                "    Caves | Cave 2 entrance | 123.00 | 45.00 | 678.00 | 90 |" + Environment.NewLine +
                Environment.NewLine +
                "Capture the spot you are standing on with the capture hotkey; it is" + Environment.NewLine +
                "appended to " + UserFileName + ", which is never overwritten by updates." + Environment.NewLine +
                "To share spots, copy those lines into a new file and open a pull" + Environment.NewLine +
                "request against the repo." + Environment.NewLine,
                Encoding.UTF8);

            _log.LogInfo("Wrote locations README to " + path);
        }
    }
}
