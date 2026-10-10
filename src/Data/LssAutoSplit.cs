using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The Forest autosplitter's settings from a LiveSplit file, and the
    // practice segment they make (author, 2026-10-01: "a singular import
    // button" that builds the spot with the splitting logic).
    //
    // WHERE THE SETTINGS ARE
    // A splits file (.lss) holds them when the autosplitter was activated
    // from the splits editor:
    //   <Run> ... <AutoSplitterSettings> <ScriptPath/> <Start/> <Reset/>
    //     <Split/> <CustomSettings> <Setting id=".." type="bool">True ...
    // A layout (.lsl) holds the same block when the autosplitter is a
    // Scriptable Auto Splitter component of the layout:
    //   <Layout> <Components> <Component> <Path>LiveSplit.ScriptableAutoSplit.dll
    //     <Settings> <ScriptPath/> <Start/> <Reset/> <Split/> <CustomSettings>
    // Read reads either: the first CustomSettings block that belongs to
    // The Forest's script.
    //
    // WHAT THEY MEAN (1deter/auto-splitters, The Forest.ASL)
    // A setting missing from the file has the ASL's default; a child is on
    // only when every parent is (LiveSplit's own rule):
    //   Preferences > mealStart (on), velocityStart (off), endSplits (on)
    //   Cave Splits (on) > Cave01 .. UnderwaterCave3 (on) > <cave>EnterSplit /
    //     <cave>ExitSplit (on)
    //   Item Splits (off) > itemSplit_<id> (off) > multiItemSplit_<id> (off)
    //   Clothing Splits (off) > clothingSplit_<id>
    //   Passenger Splits (off) > passengerSplit_<n>
    // as our event names (Game/WorldEvents, Game/GameEvents; the item ones
    // are worked out per run, Data/AutoSplitWatch):
    //   mealStart -> hold-interact, velocityStart -> first-input (the velocity
    //     start is gone; the run starts on the first input - T-0282)
    //   <cave>EnterSplit -> cave-enter-<cave in lower case>, Exit -> cave-exit-..
    //   itemSplit_<id> -> item-<id> (first pickup in the run)
    //   multiItemSplit_<id> -> item-change-<id> (every later change)
    //   clothingSplit_<id> -> clothing-<id> (first time worn in the run)
    //   passengerSplit_<n> -> passenger-<n>
    //   endSplits -> endgame-cutscene (every one: the ASL's labCutscene edge)
    //
    // Pure (System.Xml only): linked into the tests.
    // ------------------------------------------------------------------
    public sealed class LssAutoSplit
    {
        /// The ASL's cave setting names, in its order; ours are these in
        /// lower case (Game/WorldEvents.CaveIds).
        public static readonly string[] Caves =
        {
            "Cave01", "Cave02", "Cave03", "Cave04", "Cave05", "Cave06", "Cave07", "Cave08", "Cave09", "Cave10",
            "HellCave", "SnowCave", "UnderwaterCave", "UnderwaterCave2", "UnderwaterCave3",
        };

        public const string HoldInteract = "hold-interact";
        public const string FirstInput = "first-input";
        public const string EndgameCutscene = "endgame-cutscene";
        public const string ItemFirst = "item-";
        public const string ItemChange = "item-change-";

        /// LiveSplit's own switches (Start / Split / Reset in the file).
        public bool StartOn = true, SplitOn = true, ResetOn = true;
        public string ScriptPath = "";
        /// "splits file" or "layout" - where the block was.
        public string Source = "";
        public readonly Dictionary<string, bool> Values = new Dictionary<string, bool>(StringComparer.Ordinal);

        /// The Forest's settings from a .lss or .lsl text. Null with no
        /// error = the file has no autosplitter settings for The Forest;
        /// null with an error = the file could not be read.
        public static LssAutoSplit Read(string text, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0) { error = "The file is empty."; return null; }

            XmlDocument doc = new XmlDocument();
            doc.XmlResolver = null;
            try { doc.LoadXml(text); }
            catch (XmlException e) { error = "Not a LiveSplit file (not valid XML: " + e.Message + ")"; return null; }

            XmlElement root = doc.DocumentElement;
            string source = root != null && root.Name == "Layout" ? "layout" : "splits file";
            XmlNodeList blocks = doc.GetElementsByTagName("CustomSettings");
            for (int i = 0; i < blocks.Count; i++)
            {
                XmlElement custom = blocks[i] as XmlElement;
                XmlElement holder = custom != null ? custom.ParentNode as XmlElement : null;
                if (holder == null) continue;

                LssAutoSplit a = new LssAutoSplit();
                a.Source = source;
                a.ScriptPath = Text(Child(holder, "ScriptPath"));
                a.StartOn = Bool(Child(holder, "Start"), true);
                a.SplitOn = Bool(Child(holder, "Split"), true);
                a.ResetOn = Bool(Child(holder, "Reset"), true);
                foreach (XmlNode n in custom.ChildNodes)
                {
                    XmlElement s = n as XmlElement;
                    if (s == null || s.Name != "Setting") continue;
                    string id = s.GetAttribute("id");
                    string type = s.GetAttribute("type");
                    if (id.Length == 0 || (type.Length > 0 && type != "bool")) continue;
                    string v = s.InnerText.Trim();
                    a.Values[id] = string.Equals(v, "True", StringComparison.OrdinalIgnoreCase);
                }
                if (a.IsForest) return a;
            }
            return null;
        }

        /// The Forest's script: its path says so, or it has the ASL's own
        /// setting names.
        public bool IsForest
        {
            get
            {
                if (ScriptPath.IndexOf("forest", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                return Values.ContainsKey("endSplits") || Values.ContainsKey("Cave Splits") || Values.ContainsKey("mealStart");
            }
        }

        /// A setting as the ASL sees it: the file's value or the default,
        /// and off when any parent is off.
        public bool Setting(string id)
        {
            for (int guard = 0; id != null && guard < 8; guard++)
            {
                bool v;
                if (!Values.TryGetValue(id, out v)) v = Default(id);
                if (!v) return false;
                id = Parent(id);
            }
            return true;
        }

        public static string Parent(string id)
        {
            if (id == "mealStart" || id == "velocityStart" || id == "menuReset" || id == "endSplits") return "Preferences";
            if (Array.IndexOf(Caves, id) >= 0) return "Cave Splits";
            if (id.EndsWith("EnterSplit") || id.EndsWith("ExitSplit"))
            {
                string cave = id.Substring(0, id.Length - (id.EndsWith("EnterSplit") ? 10 : 9));
                if (Array.IndexOf(Caves, cave) >= 0) return cave;
            }
            if (id.StartsWith("itemSplit_")) return "Item Splits";
            if (id.StartsWith("multiItemSplit_")) return "itemSplit_" + id.Substring(15);
            if (id.StartsWith("clothingSplit_")) return "Clothing Splits";
            if (id.StartsWith("passengerSplit_")) return "Passenger Splits";
            return null;
        }

        public static bool Default(string id)
        {
            if (id == "velocityStart") return false;
            if (id == "Item Splits" || id == "Clothing Splits" || id == "Passenger Splits") return false;
            if (id.StartsWith("itemSplit_") || id.StartsWith("multiItemSplit_") ||
                id.StartsWith("clothingSplit_") || id.StartsWith("passengerSplit_")) return false;
            return true;   // Preferences, mealStart, menuReset, endSplits, caves
        }

        /// What starts the timer, as event names; empty = LiveSplit's start
        /// is off (a manual start).
        public string[] StartEvents()
        {
            List<string> e = new List<string>();
            if (!StartOn) return e.ToArray();
            if (Setting("mealStart")) e.Add(HoldInteract);
            if (Setting("velocityStart")) e.Add(FirstInput);
            return e.ToArray();
        }

        /// What splits, as event names (the segment's autosplit list); empty
        /// = LiveSplit's split is off or nothing is ticked.
        public string[] SplitEvents()
        {
            List<string> e = new List<string>();
            if (!SplitOn) return e.ToArray();

            List<int> items = Ids("itemSplit_");
            for (int i = 0; i < items.Count; i++)
            {
                string id = items[i].ToString(CultureInfo.InvariantCulture);
                if (!Setting("itemSplit_" + id)) continue;
                e.Add(ItemFirst + id);
                if (Setting("multiItemSplit_" + id)) e.Add(ItemChange + id);
            }
            for (int c = 0; c < Caves.Length; c++)
            {
                string lower = Caves[c].ToLowerInvariant();
                if (Setting(Caves[c] + "EnterSplit")) e.Add("cave-enter-" + lower);
                if (Setting(Caves[c] + "ExitSplit")) e.Add("cave-exit-" + lower);
            }
            List<int> clothing = Ids("clothingSplit_");
            for (int i = 0; i < clothing.Count; i++)
                if (Setting("clothingSplit_" + clothing[i])) e.Add("clothing-" + clothing[i]);
            List<int> passengers = Ids("passengerSplit_");
            for (int i = 0; i < passengers.Count; i++)
                if (Setting("passengerSplit_" + passengers[i])) e.Add("passenger-" + passengers[i]);
            if (Setting("endSplits")) e.Add(EndgameCutscene);
            return e.ToArray();
        }

        // The numbers after a prefix among the file's settings, sorted.
        private List<int> Ids(string prefix)
        {
            List<int> ids = new List<int>();
            foreach (KeyValuePair<string, bool> kv in Values)
            {
                if (!kv.Key.StartsWith(prefix)) continue;
                int n;
                if (int.TryParse(kv.Key.Substring(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && !ids.Contains(n))
                    ids.Add(n);
            }
            ids.Sort();
            return ids;
        }

        private static XmlElement Child(XmlElement e, string name)
        {
            foreach (XmlNode n in e.ChildNodes)
            {
                XmlElement c = n as XmlElement;
                if (c != null && c.Name == name) return c;
            }
            return null;
        }

        private static string Text(XmlElement e) { return e == null ? "" : e.InnerText.Trim(); }

        private static bool Bool(XmlElement e, bool fallback)
        {
            if (e == null) return fallback;
            string t = e.InnerText.Trim();
            if (string.Equals(t, "True", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(t, "False", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }
    }

    // ------------------------------------------------------------------
    // A LiveSplit run -> a practice segment that splits where LiveSplit
    // does. LiveSplit splits are positional (each split is "the next time
    // the autosplitter fires"), so every checkpoint and the end are the
    // same trigger, `event autosplit`, and the segment's autosplit list
    // says what counts. Names come from the file, so the LiveSplit link
    // matches every row by name. No autosplitter settings (or LiveSplit's
    // split switched off): manual checkpoints, F12 per split. The spawn is
    // the caller's (where the player stands); the id the caller's (NewId).
    // ------------------------------------------------------------------
    public static class LssSegmentBuilder
    {
        public const string Category = "LiveSplit";

        public static Segment Build(LssRun run, LssAutoSplit asl, string fallbackName)
        {
            Segment s = new Segment();
            // "The Forest Any%"; without a category the file's own name says
            // more ("The Forest Coop Any%.lss" has only a game name).
            string title = (run.GameName + " " + run.CategoryName).Trim();
            s.Name = run.CategoryName.Trim().Length > 0 || string.IsNullOrEmpty(fallbackName)
                ? (title.Length > 0 ? title : "LiveSplit run")
                : fallbackName;
            s.Category = Category;

            string[] starts = asl != null ? asl.StartEvents() : new string[0];
            if (starts.Length > 0) s.Start = Event(string.Join("|", starts));
            else s.Start.Kind = TriggerKind.Manual;

            string[] splits = asl != null ? asl.SplitEvents() : new string[0];
            s.AutoSplit.AddRange(splits);
            Trigger split = new Trigger();
            if (splits.Length > 0) split = Event(Segment.AutoSplitEvent);
            else split.Kind = TriggerKind.Manual;

            int n = run.Segments.Count;
            for (int i = 0; i < n - 1; i++)
            {
                s.Checkpoints.Add(split);
                s.SetCheckpointName(i, SplitName(run, i));
            }
            s.End = split;
            s.EndName = SplitName(run, n - 1);
            return s;
        }

        // An empty LiveSplit name would read "Checkpoint n" - name it after
        // its place instead, which still matches nothing else by accident.
        private static string SplitName(LssRun run, int i)
        {
            string name = i >= 0 && i < run.Segments.Count ? run.Segments[i].Name : "";
            return string.IsNullOrEmpty(name) ? "Split " + (i + 1) : name;
        }

        private static Trigger Event(string name)
        {
            Trigger t = new Trigger();
            t.Kind = TriggerKind.Event;
            t.EventName = name;
            return t;
        }
    }

    // ------------------------------------------------------------------
    // Per run: does this moment split, given the segment's autosplit list?
    // The ASL's own bookkeeping, reset when the clock starts:
    //   - an event in the list splits (cave, passenger, endgame cutscene);
    //   - clothing-<id> only the first time in the run (its equippedClothes);
    //   - item-<id> when the item first appears in the run, item-change-<id>
    //     on every later change of its count (its itemTracker). Unlike the
    //     ASL, what the player holds when the clock starts is already "seen"
    //     - a practice segment starts mid-game, and the ASL would split on
    //     the first frame for every held item ticked.
    // At most one split per frame is the caller's (the ASL returns once per
    // update).
    // ------------------------------------------------------------------
    public sealed class AutoSplitWatch
    {
        private readonly List<string> _events = new List<string>();
        private readonly List<int> _itemIds = new List<int>();
        private readonly List<bool> _itemFirst = new List<bool>();
        private readonly List<bool> _itemChange = new List<bool>();
        private readonly List<int> _last = new List<int>();
        private readonly List<bool> _seen = new List<bool>();
        private readonly List<string> _clothingDone = new List<string>();

        public bool Any { get { return _events.Count > 0 || _itemIds.Count > 0; } }
        /// Item ids to keep the inventory fresh for.
        public List<int> ItemIds { get { return _itemIds; } }

        public void Configure(List<string> list)
        {
            _events.Clear();
            _itemIds.Clear();
            _itemFirst.Clear();
            _itemChange.Clear();
            _last.Clear();
            _seen.Clear();
            _clothingDone.Clear();
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                string e = list[i];
                int id;
                if (e.StartsWith(LssAutoSplit.ItemChange) && Int(e.Substring(LssAutoSplit.ItemChange.Length), out id))
                    _itemChange[ItemIndex(id)] = true;
                else if (e.StartsWith(LssAutoSplit.ItemFirst) && Int(e.Substring(LssAutoSplit.ItemFirst.Length), out id))
                    _itemFirst[ItemIndex(id)] = true;
                else if (!_events.Contains(e)) _events.Add(e);
            }
        }

        private int ItemIndex(int id)
        {
            int at = _itemIds.IndexOf(id);
            if (at < 0)
            {
                at = _itemIds.Count;
                _itemIds.Add(id);
                _itemFirst.Add(false);
                _itemChange.Add(false);
                _last.Add(0);
                _seen.Add(false);
            }
            return at;
        }

        /// The clock started: what is held now is the baseline.
        public void Begin(IItemCounts items)
        {
            _clothingDone.Clear();
            for (int i = 0; i < _itemIds.Count; i++)
            {
                int n = items != null ? items.AmountOf(_itemIds[i]) : 0;
                _last[i] = n;
                _seen[i] = n > 0;
            }
        }

        /// True when this event is one the list splits on.
        public bool OnEvent(string e)
        {
            if (e == null || _events.Count == 0) return false;
            string lower = e.ToLowerInvariant();
            if (!_events.Contains(lower)) return false;
            if (lower.StartsWith("clothing-"))
            {
                if (_clothingDone.Contains(lower)) return false;
                _clothingDone.Add(lower);
            }
            return true;
        }

        /// Reads every watched item; true when one of them splits. All are
        /// updated whatever the answer, so a change is never counted twice.
        public bool OnItems(IItemCounts items)
        {
            if (items == null) return false;
            bool split = false;
            for (int i = 0; i < _itemIds.Count; i++)
            {
                int n = items.AmountOf(_itemIds[i]);
                if (n == _last[i]) continue;
                _last[i] = n;
                if (!_seen[i])
                {
                    if (n <= 0) continue;
                    _seen[i] = true;
                    if (_itemFirst[i]) split = true;
                }
                else if (_itemChange[i]) split = true;
            }
            return split;
        }

        private static bool Int(string s, out int v)
        {
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        }
    }
}
