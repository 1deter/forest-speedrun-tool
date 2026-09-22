using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Game
{
    public sealed class NatureEntry
    {
        public string Name;
        public string Kind;       // CollectItem / InspectAnimal / InspectPlant
        public int PageIndex;     // into NatureGuideReader.Pages
        public bool Ticked;

        public object Source;     // the game's Entry, re-read for _ticked
        public string TickPath;   // hierarchy path, for the dump
    }

    public sealed class NaturePage
    {
        public string Name;
        public int Total;
        public int Ticked;
    }

    // ------------------------------------------------------------------
    // Reads the survival book's nature guide. INFO-ONLY.
    //
    // Confirmed from IL:
    //   TheForest.Player.TickOffSystem (on the player)
    //     Entry[] _entries
    //   TickOffSystem+Entry
    //     EntryType  _type        CollectItem / InspectAnimal / InspectPlant
    //     AnimalType _animalType  species, for the two Inspect kinds
    //     Int32      _itemId      for CollectItem
    //     Boolean    _ticked      set by the entry's own event handler
    //     GameObject _tickGo      the tick mark drawn on the book page
    //
    // The game does not record which page an entry is on. The tick mark
    // does, by where it sits in the hierarchy - see Data/PageGrouping.
    // ------------------------------------------------------------------
    public sealed class NatureGuideReader
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly ManualLogSource _log;
        private readonly Func<int, string> _itemName;

        private readonly List<NatureEntry> _entries = new List<NatureEntry>();
        private readonly List<NaturePage> _pages = new List<NaturePage>();

        private Type _hostType;
        private FieldInfo _entriesField;
        private FieldInfo _typeField, _animalField, _itemField, _tickedField, _tickGoField;
        private bool _typesResolved;

        private Component _host;

        public IList<NatureEntry> Entries { get { return _entries; } }
        public IList<NaturePage> Pages { get { return _pages; } }
        public int TickedCount { get; private set; }
        public string Status { get; private set; }

        public NatureGuideReader(ManualLogSource log, Func<int, string> itemName)
        {
            _log = log;
            _itemName = itemName;
            Status = "not read yet";
        }

        public void Refresh()
        {
            if (!ResolveTypes()) return;

            // Fake-null after a save load: the old component is destroyed
            // and a new one exists, so the structure is rebuilt.
            if (_host == null)
            {
                _entries.Clear();
                _pages.Clear();
                TickedCount = 0;

                _host = FindHost();
                if (_host == null)
                {
                    Status = "nature guide not loaded (open a save first)";
                    return;
                }

                BuildStructure();
            }

            UpdateTicks();
            Status = _entries.Count == 0
                ? "nature guide has no entries"
                : TickedCount + "/" + _entries.Count + " discovered";
        }

        // ------------------------------------------------------------------
        private bool ResolveTypes()
        {
            if (_typesResolved) return _entriesField != null;
            _typesResolved = true;

            _hostType = GameBridge.FindGameType("TheForest.Player.TickOffSystem");
            if (_hostType == null) { Status = "TickOffSystem not found in this game version"; return false; }

            _entriesField = _hostType.GetField("_entries", Flags);
            Type entry = _entriesField != null ? _entriesField.FieldType.GetElementType() : null;
            if (entry == null) { _entriesField = null; Status = "TickOffSystem._entries not found"; return false; }

            _typeField = entry.GetField("_type", Flags);
            _animalField = entry.GetField("_animalType", Flags);
            _itemField = entry.GetField("_itemId", Flags);
            _tickedField = entry.GetField("_ticked", Flags);
            _tickGoField = entry.GetField("_tickGo", Flags);

            _log.LogInfo("TickOffSystem bound. type:" + (_typeField != null) +
                         " animal:" + (_animalField != null) + " item:" + (_itemField != null) +
                         " ticked:" + (_tickedField != null) + " tickGo:" + (_tickGoField != null));

            if (_tickedField == null) { _entriesField = null; Status = "TickOffSystem entries have no _ticked"; return false; }
            return true;
        }

        private Component FindHost()
        {
            UnityEngine.Object[] found;
            try { found = Resources.FindObjectsOfTypeAll(_hostType); }
            catch (Exception) { return null; }
            if (found == null) return null;

            // FindObjectsOfTypeAll also returns prefab assets; only a
            // component in a loaded scene is the live one.
            for (int i = 0; i < found.Length; i++)
            {
                Component c = found[i] as Component;
                if (c != null && c.gameObject.scene.IsValid()) return c;
            }

            return null;
        }

        private void BuildStructure()
        {
            IList array;
            try { array = _entriesField.GetValue(_host) as IList; }
            catch (Exception) { array = null; }
            if (array == null) return;

            List<int[]> chains = new List<int[]>(array.Count);
            List<Transform> pageOf = new List<Transform>();
            Dictionary<int, Transform> byId = new Dictionary<int, Transform>();

            for (int i = 0; i < array.Count; i++)
            {
                object src = array[i];
                if (src == null) continue;

                NatureEntry e = new NatureEntry();
                e.Source = src;
                e.Kind = Read(_typeField, src);
                e.Name = NameFor(src, e.Kind);

                GameObject tick = null;
                try { if (_tickGoField != null) tick = _tickGoField.GetValue(src) as GameObject; }
                catch (Exception) { }

                chains.Add(Chain(tick, byId));
                e.TickPath = tick != null ? PathOf(tick.transform) : "(no tick)";
                _entries.Add(e);
            }

            // Pages in book order: the page objects' sibling order.
            int[] pageIds = PageGrouping.PageIds(chains);
            List<int> order = new List<int>();

            for (int i = 0; i < pageIds.Length; i++)
                if (!order.Contains(pageIds[i])) order.Add(pageIds[i]);

            // Insertion sort rather than a capturing Sort delegate - closure
            // classes are a type-load risk on this Mono.
            for (int i = 1; i < order.Count; i++)
            {
                int id = order[i], key = SiblingIndex(byId, id), j = i - 1;
                while (j >= 0 && SiblingIndex(byId, order[j]) > key) { order[j + 1] = order[j]; j--; }
                order[j + 1] = id;
            }

            for (int p = 0; p < order.Count; p++)
            {
                NaturePage page = new NaturePage();
                Transform t;
                page.Name = order[p] != PageGrouping.NoPage && byId.TryGetValue(order[p], out t)
                    ? Tidy(t.name)
                    : "Other";
                _pages.Add(page);
            }

            for (int i = 0; i < _entries.Count; i++)
            {
                _entries[i].PageIndex = order.IndexOf(pageIds[i]);
                _pages[_entries[i].PageIndex].Total++;
            }

            _log.LogInfo("Nature guide: " + _entries.Count + " entries on " + _pages.Count + " page(s).");
        }

        private void UpdateTicks()
        {
            TickedCount = 0;
            for (int p = 0; p < _pages.Count; p++) _pages[p].Ticked = 0;

            for (int i = 0; i < _entries.Count; i++)
            {
                NatureEntry e = _entries[i];

                bool ticked;
                try { ticked = (bool)_tickedField.GetValue(e.Source); }
                catch (Exception) { ticked = false; }

                e.Ticked = ticked;
                if (!ticked) continue;

                TickedCount++;
                _pages[e.PageIndex].Ticked++;
            }
        }

        // ------------------------------------------------------------------
        private string NameFor(object src, string kind)
        {
            if (kind == "CollectItem" && _itemField != null)
            {
                int id;
                try { id = (int)_itemField.GetValue(src); }
                catch (Exception) { id = -1; }

                string item = id >= 0 ? _itemName(id) : null;
                return item != null ? Tidy(item) : "item " + id;
            }

            string species = Read(_animalField, src);
            // The game's own enum misspells it.
            if (species.StartsWith("Muhshroom", StringComparison.Ordinal))
                species = "Mushroom" + species.Substring("Muhshroom".Length);
            return Tidy(species);
        }

        private static string Read(FieldInfo f, object src)
        {
            if (f == null) return "";
            try
            {
                object v = f.GetValue(src);
                return v != null ? v.ToString() : "";
            }
            catch (Exception) { return ""; }
        }

        private static int[] Chain(GameObject tick, Dictionary<int, Transform> byId)
        {
            if (tick == null) return null;

            List<int> ids = new List<int>();
            for (Transform t = tick.transform; t != null; t = t.parent)
            {
                int id = t.GetInstanceID();
                ids.Add(id);
                byId[id] = t;
            }

            ids.Reverse();
            return ids.ToArray();
        }

        private static int SiblingIndex(Dictionary<int, Transform> byId, int id)
        {
            Transform t;
            return byId.TryGetValue(id, out t) ? t.GetSiblingIndex() : int.MaxValue;
        }

        private static string PathOf(Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        /// "GlaucousWingedGull" -> "Glaucous Winged Gull", "page_birds" -> "Page birds".
        public static string Tidy(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "unknown";

            StringBuilder sb = new StringBuilder(raw.Length + 4);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '_' || c == '-') { if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' '); continue; }

                if (i > 0 && char.IsUpper(c) && char.IsLower(raw[i - 1]) && sb[sb.Length - 1] != ' ')
                    sb.Append(' ');

                sb.Append(sb.Length == 0 ? char.ToUpperInvariant(c) : c);
            }

            return sb.ToString().Trim();
        }

        // ------------------------------------------------------------------
        /// Everything the reader sees, for refining page names and checking
        /// the grouping against the real book.
        public string WriteDump()
        {
            string path = Path.Combine(GameDumper.DumpDirectory,
                                       "natureguide_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");

            using (StreamWriter w = new StreamWriter(path, false, Encoding.UTF8))
            {
                w.WriteLine("# The Forest nature guide (TickOffSystem)");
                w.WriteLine("# generated " + DateTime.Now);
                w.WriteLine("# " + Status);
                w.WriteLine("# columns: page | name | kind | ticked | tick path");
                w.WriteLine();

                for (int i = 0; i < _entries.Count; i++)
                {
                    NatureEntry e = _entries[i];
                    w.WriteLine(_pages[e.PageIndex].Name + " | " + e.Name + " | " + e.Kind + " | " +
                                (e.Ticked ? "yes" : "no") + " | " + e.TickPath);
                }
            }

            _log.LogInfo("Nature guide -> " + path);
            return path;
        }
    }
}
