using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Logging;

namespace ForestOverlay.Data
{
    public sealed class CollectionEntry
    {
        public string Category = "";
        public string Name = "";

        /// Resolved at load. -1 when nothing matched, which is shown
        /// rather than hidden so a typo in a shared list is fixable.
        public int ItemId = -1;

        /// Set in the file as "= 210" or "= KeycardElevator". An
        /// explicit id skips name matching entirely, which is what the
        /// shipped list uses: the game's internal names are nothing
        /// like the ones the admins publish, so matching on the display
        /// name was never going to work.
        public string Lookup = "";

        /// How many are needed. Some pieces are one item held twice
        /// rather than two items.
        public int Required = 1;

        public int Held;

        /// Latched once seen. Story items stay in the inventory, but
        /// latching means a checklist cannot un-tick itself if something
        /// is dropped or consumed mid-run.
        public bool Seen;
    }

    // ------------------------------------------------------------------
    // The 100% collection checklist.
    //
    // Driven by a text file rather than a hardcoded list, because what
    // counts for 100% is decided by the category's admins and will change
    // without the plugin changing. Same format reasoning as everything
    // else here: line-oriented so it diffs, merges and can be edited by
    // someone who has never written code.
    //
    //   Display name = <id>   or  = <internal name>  or  = <id> xN
    //
    // The display name is what the admins publish; the lookup on the right
    // is the game's own id, because the internal names are nothing like
    // the published ones. Anything that fails to resolve is reported in
    // the UI rather than silently dropped.
    // ------------------------------------------------------------------
    public sealed class CollectionList
    {
        private readonly ManualLogSource _log;
        private readonly string _folder;

        private readonly List<CollectionEntry> _entries = new List<CollectionEntry>();
        private readonly List<string> _categories = new List<string>();

        public IList<CollectionEntry> Entries { get { return _entries; } }
        public IList<string> Categories { get { return _categories; } }
        public string Folder { get { return _folder; } }
        public string Status { get; private set; }
        public int Unresolved { get; private set; }

        public CollectionList(ManualLogSource log, string configDirectory)
        {
            _log = log;
            _folder = Path.Combine(configDirectory, "collectibles");
            Status = "(not loaded)";
        }

        public int Total { get { return _entries.Count; } }

        public int SeenCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _entries.Count; i++) if (_entries[i].Seen) n++;
                return n;
            }
        }

        public int SeenIn(string category)
        {
            int n = 0;
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Category == category && _entries[i].Seen) n++;
            return n;
        }

        public int TotalIn(string category)
        {
            int n = 0;
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Category == category) n++;
            return n;
        }

        public void Reload()
        {
            _entries.Clear();
            _categories.Clear();
            Unresolved = 0;

            try
            {
                if (!Directory.Exists(_folder)) Directory.CreateDirectory(_folder);

                string[] files = Directory.GetFiles(_folder, "*.txt");
                int loaded = 0;

                for (int i = 0; i < files.Length; i++)
                {
                    string name = Path.GetFileName(files[i]);
                    if (string.Equals(name, "README.txt", StringComparison.OrdinalIgnoreCase)) continue;
                    if (LoadFile(files[i])) loaded++;
                }

                RebuildCategories();
                Status = _entries.Count + " entries / " + loaded + " file(s)";
                _log.LogInfo("Collection list: " + Status);
            }
            catch (Exception ex)
            {
                Status = "load failed - see log";
                _log.LogWarning("Collection list failed: " + ex.Message);
            }
        }

        private bool LoadFile(string path)
        {
            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch (Exception) { return false; }

            int added = 0;
            string currentCategory = "Uncategorised";

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                // A bare [Heading] sets the category for the lines under it,
                // so a long list does not repeat the category on every row.
                if (line[0] == '[')
                {
                    currentCategory = line.Trim('[', ']').Trim();
                    if (currentCategory.Length == 0) currentCategory = "Uncategorised";
                    continue;
                }

                CollectionEntry e = new CollectionEntry();

                // Optional "= lookup", where lookup is an item id or the
                // game's internal item name.
                int eq = line.IndexOf('=');
                if (eq >= 0)
                {
                    e.Lookup = line.Substring(eq + 1).Trim();
                    line = line.Substring(0, eq).Trim();

                    // Trailing "x2" on the lookup is a required count.
                    int x = e.Lookup.LastIndexOf('x');
                    if (x > 0)
                    {
                        int count;
                        if (int.TryParse(e.Lookup.Substring(x + 1).Trim(), out count) && count > 0)
                        {
                            e.Required = count;
                            e.Lookup = e.Lookup.Substring(0, x).Trim();
                        }
                    }
                }

                int bar = line.IndexOf('|');
                if (bar >= 0)
                {
                    e.Category = line.Substring(0, bar).Trim();
                    e.Name = line.Substring(bar + 1).Trim();
                }
                else
                {
                    e.Category = currentCategory;
                    e.Name = line;
                }

                if (e.Name.Length == 0) continue;
                if (e.Category.Length == 0) e.Category = currentCategory;

                _entries.Add(e);
                added++;
            }

            return added > 0;
        }

        private void RebuildCategories()
        {
            for (int i = 0; i < _entries.Count; i++)
                if (!_categories.Contains(_entries[i].Category))
                    _categories.Add(_entries[i].Category);
        }

        /// Resolves entries to ids. An explicit lookup wins; otherwise
        /// the display name is tried. `lookup` returns -1 for no match.
        public void Resolve(Func<string, int> lookup)
        {
            Unresolved = 0;

            for (int i = 0; i < _entries.Count; i++)
            {
                CollectionEntry e = _entries[i];
                e.ItemId = -1;

                if (e.Lookup.Length > 0)
                {
                    int id;
                    if (int.TryParse(e.Lookup, out id)) e.ItemId = id;
                    else e.ItemId = lookup(e.Lookup);
                }

                if (e.ItemId < 0) e.ItemId = lookup(e.Name);
                if (e.ItemId < 0) Unresolved++;
            }

            if (Unresolved > 0)
                _log.LogInfo("Collection list: " + Unresolved + " name(s) did not match an item.");
        }

        public void WriteReadmeIfMissing()
        {
            try
            {
                string path = Path.Combine(_folder, "README.txt");
                if (File.Exists(path)) return;
                if (!Directory.Exists(_folder)) Directory.CreateDirectory(_folder);

                string nl = Environment.NewLine;
                File.WriteAllText(path,
                    "ForestOverlay - 100% collection checklist" + nl +
                    "=========================================" + nl + nl +
                    "What counts for 100% is decided by the category's admins, so it" + nl +
                    "lives here as data rather than in the plugin." + nl + nl +
                    "One item per line, under a [Heading]:" + nl + nl +
                    "  [Unique weapons]" + nl +
                    "  Modern Axe" + nl +
                    "  Katana" + nl + nl +
                    "Or with an explicit category:" + nl + nl +
                    "  Unique weapons | Modern Axe" + nl + nl +
                    "Names are matched against the game's own item list, so use the" + nl +
                    "in-game name. Anything that does not match is shown in the 100%" + nl +
                    "tab as unresolved rather than silently dropped." + nl,
                    Encoding.UTF8);
            }
            catch (Exception) { }
        }
    }
}
