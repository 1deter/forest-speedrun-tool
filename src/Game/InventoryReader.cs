using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Game
{
    /// One entry from the game item database, for name lookup and search.
    public struct ItemInfo
    {
        public int Id;
        public string Name;
    }

    public struct ItemStack
    {
        public int Id;
        public int Amount;
        public string Name;
        public bool Equipped;
    }

    // ------------------------------------------------------------------
    // Reads the player's inventory. INFO-ONLY.
    //
    // Field layout confirmed from an F11 filtered dump:
    //
    //   TheForest.Items.Inventory.PlayerInventory
    //     ._possessedItems      List<InventoryItem>
    //     ._possessedItemsCount int
    //     ._itemDatabase        ItemDatabase
    //     ._equipmentSlotsIds   int[]   - ids currently equipped
    //
    //   TheForest.Items.Inventory.InventoryItem
    //     ._itemId int / ._amount int / ._maxAmount int
    //
    //   TheForest.Items.ItemDatabase
    //     .ItemById(int) -> Item, whose ._name is the display name.
    //
    // THE INVENTORY HANDLE MUST NOT BE CACHED ACROSS A LOAD.
    // An earlier version cached a FindObjectOfType result, which goes
    // stale when a save is loaded - the counter then froze at whatever it
    // read at load time. The canonical handle is the static
    // TheForest.Utils.LocalPlayer.Inventory, which is what the game's own
    // code (and the author's LiveSplit autosplitter) reads, so that is
    // re-read on every refresh.
    //
    // The list is read through the non-generic IList interface, which
    // avoids constructing a List<InventoryItem> generic type by
    // reflection - the kind of thing that upsets the old Mono runtime.
    // ------------------------------------------------------------------
    public sealed class InventoryReader
    {
        // ------------------------------------------------------------------
        // Phantom item filtering.
        //
        // _possessedItems contains entries that are not real inventory
        // contents: dev/ghost ids, and multiplayer-only items that are
        // listed but unreachable in singleplayer. These bounds come from
        // the author's own LiveSplit autosplitter for this game
        // (github.com/1deter/auto-splitters), where they are already
        // proven against real runs:
        //
        //     if (itemId > 311 || itemId < 29 || itemId == 302) continue;
        //
        // Kept as named constants rather than inlined so the provenance
        // stays attached to the numbers.
        // ------------------------------------------------------------------
        public const int MinValidItemId = 29;
        public const int MaxValidItemId = 311;
        public const int GhostItemId = 302;

        private readonly ManualLogSource _log;

        private Component _inventory;
        private Type _inventoryType;

        private FieldInfo _possessedCountField;
        private FieldInfo _possessedItemsField;
        private FieldInfo _itemDatabaseField;
        private FieldInfo _equipmentSlotIdsField;

        private FieldInfo _itemIdField;
        private FieldInfo _amountField;
        private bool _itemFieldsResolved;

        private object _itemDatabase;
        private MethodInfo _itemByIdMethod;
        private FieldInfo _itemNameField;

        private readonly Dictionary<int, string> _nameCache = new Dictionary<int, string>();
        private readonly List<ItemStack> _stacks = new List<ItemStack>();
        private readonly List<int> _equipped = new List<int>();

        /// When false, entries outside the valid id range are shown too.
        /// Useful while exploring what the game actually exposes.
        public bool FilterPhantomItems = true;

        /// When false, stacks with amount 0 are hidden. They are shown by
        /// default because an equipped item legitimately reads 0 while
        /// held, and hiding it looks like the item vanished.
        public bool ShowZeroAmounts = true;

        public bool Available { get { return _inventory != null && _possessedItemsField != null; } }
        public IList<ItemStack> Stacks { get { return _stacks; } }

        /// Total across visible stacks. Derived from the live list rather
        /// than _possessedItemsCount, which does not track reliably.
        public int TotalItems { get; private set; }
        public int TotalStacks { get; private set; }
        public int FilteredOut { get; private set; }

        // ------------------------------------------------------------------
        // Item catalogue.
        //
        // The whole database, not just what is held - trigger editing
        // needs to find "rope" before you own any. Built once from
        // ItemDatabase.Items and cached, because it does not change.
        // ------------------------------------------------------------------
        private readonly List<ItemInfo> _catalog = new List<ItemInfo>();
        private bool _catalogBuilt;

        public IList<ItemInfo> Catalog { get { return _catalog; } }

        /// Human-readable state, shown in the UI. A catalogue that
        /// silently yields nothing is indistinguishable from "no matches",
        /// which is exactly how the first version failed.
        public string CatalogStatus { get; private set; }

        public void BuildCatalog()
        {
            if (_catalogBuilt) return;

            object db = FindItemDatabase();
            if (db == null)
            {
                CatalogStatus = "item database not found yet";
                return;
            }

            try
            {
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                     BindingFlags.NonPublic;

                // Property first, then the backing field. The property is
                // the documented surface but goes through a getter that can
                // be lazy; the field is what actually holds the array.
                IEnumerable items = null;

                PropertyInfo itemsProp = db.GetType().GetProperty("Items", flags);
                if (itemsProp != null) items = itemsProp.GetValue(db, null) as IEnumerable;

                if (items == null)
                {
                    FieldInfo itemsField = db.GetType().GetField("_items", flags);
                    if (itemsField != null) items = itemsField.GetValue(db) as IEnumerable;
                }

                if (items == null)
                {
                    CatalogStatus = "item database has no readable Items";
                    _log.LogWarning(CatalogStatus);
                    return;
                }

                FieldInfo idField = null;
                FieldInfo nameField = null;
                int seen = 0;

                foreach (object item in items)
                {
                    if (item == null) continue;
                    seen++;

                    if (idField == null)
                    {
                        idField = item.GetType().GetField("_id", flags);
                        nameField = item.GetType().GetField("_name", flags);

                        if (idField == null || nameField == null)
                        {
                            CatalogStatus = "item type has no _id/_name";
                            _log.LogWarning(CatalogStatus + " (" + item.GetType().Name + ")");
                            return;
                        }
                    }

                    ItemInfo info;
                    info.Id = (int)idField.GetValue(item);
                    info.Name = nameField.GetValue(item) as string;

                    if (string.IsNullOrEmpty(info.Name)) continue;

                    // NOT filtered by the phantom-id range here. That range
                    // describes what can appear in _possessedItems, not what
                    // exists in the database, and applying it hid real items
                    // from the search.
                    _catalog.Add(info);
                }

                if (_catalog.Count == 0)
                {
                    // Do not latch: the database may simply not be populated
                    // yet, and latching would leave search permanently dead.
                    CatalogStatus = "database held " + seen + " entries, none usable";
                    _log.LogWarning(CatalogStatus);
                    return;
                }

                _catalog.Sort(CompareCatalog);
                _catalogBuilt = true;
                CatalogStatus = _catalog.Count + " items";
                _log.LogInfo("Item catalogue: " + CatalogStatus);
            }
            catch (Exception ex)
            {
                CatalogStatus = "catalogue failed: " + ex.Message;
                _log.LogWarning(CatalogStatus);
            }
        }

        // ------------------------------------------------------------------
        // The database is found independently of the player.
        //
        // Hanging it off PlayerInventory._itemDatabase meant the whole
        // search depended on the inventory having resolved, so editing a
        // trigger before the player existed - or after any link in that
        // chain failed - returned an empty list that looked exactly like
        // "no matches".
        // ------------------------------------------------------------------
        private object FindItemDatabase()
        {
            if (_itemDatabase != null) return _itemDatabase;

            // The type keeps its own static instance.
            object viaStatic = GameBridge.ReadStaticField("TheForest.Items.ItemDatabase", "_instance");
            if (viaStatic != null) { _itemDatabase = viaStatic; return _itemDatabase; }

            // Otherwise whatever the inventory is holding.
            ResolveDatabase();
            if (_itemDatabase != null) return _itemDatabase;

            // Last resort: it is a ScriptableObject, so it will not be found
            // by FindObjectOfType but IS in the loaded object set.
            Type t = GameBridge.FindGameType("TheForest.Items.ItemDatabase");
            if (t == null) return null;

            try
            {
                UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(t);
                if (all != null && all.Length > 0) _itemDatabase = all[0];
            }
            catch (Exception) { }

            return _itemDatabase;
        }

        private static int CompareCatalog(ItemInfo a, ItemInfo b)
        {
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        public string NameForId(int id)
        {
            BuildCatalog();

            for (int i = 0; i < _catalog.Count; i++)
                if (_catalog[i].Id == id) return _catalog[i].Name;

            return null;
        }

        /// Substring match, ranked so that a name STARTING with the query
        /// beats one merely containing it - typing "rope" should offer
        /// Rope before Rope Bridge Kit.
        public void SearchItems(string query, List<ItemInfo> results, int limit)
        {
            results.Clear();
            BuildCatalog();

            if (string.IsNullOrEmpty(query)) return;
            string q = query.ToLowerInvariant();

            for (int pass = 0; pass < 2 && results.Count < limit; pass++)
            {
                for (int i = 0; i < _catalog.Count && results.Count < limit; i++)
                {
                    string name = _catalog[i].Name.ToLowerInvariant();
                    int at = name.IndexOf(q, StringComparison.Ordinal);

                    if (at < 0) continue;
                    if (pass == 0 && at != 0) continue;
                    if (pass == 1 && at == 0) continue;

                    results.Add(_catalog[i]);
                }
            }
        }

        public InventoryReader(ManualLogSource log)
        {
            _log = log;
        }

        public void Reset()
        {
            _inventory = null;
            _inventoryType = null;
            _itemFieldsResolved = false;
            _itemDatabase = null;
            _itemByIdMethod = null;
            _nameCache.Clear();
            _stacks.Clear();
        }

        // ------------------------------------------------------------------
        public void Resolve()
        {
            // Re-read the static every time. Cheap, and it is what makes
            // the counters survive a save load.
            object live = GameBridge.ReadStaticField("TheForest.Utils.LocalPlayer", "Inventory");
            Component comp = live as Component;

            if (comp == null)
            {
                // Fallback for a build where the static is missing or the
                // player has not spawned yet.
                Type invType = GameBridge.FindGameType("TheForest.Items.Inventory.PlayerInventory");
                if (invType == null) return;

                try { comp = UnityEngine.Object.FindObjectOfType(invType) as Component; }
                catch (Exception) { return; }

                if (comp == null) return;
            }

            if (ReferenceEquals(comp, _inventory) && _inventory != null) return;

            _inventory = comp;
            _inventoryType = comp.GetType();
            BindFields();
        }

        private void BindFields()
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            _possessedCountField = _inventoryType.GetField("_possessedItemsCount", flags);
            _possessedItemsField = _inventoryType.GetField("_possessedItems", flags);
            _itemDatabaseField = _inventoryType.GetField("_itemDatabase", flags);
            _equipmentSlotIdsField = _inventoryType.GetField("_equipmentSlotsIds", flags);

            // The database hangs off this inventory instance, so it has to
            // be re-resolved whenever the inventory is rebound.
            _itemDatabase = null;
            _itemByIdMethod = null;

            _log.LogInfo("PlayerInventory bound. items:" + (_possessedItemsField != null) +
                         " count:" + (_possessedCountField != null) +
                         " db:" + (_itemDatabaseField != null) +
                         " equipIds:" + (_equipmentSlotIdsField != null));
        }

        /// The game's own count field. Kept for comparison only - it is
        /// not what the HUD shows.
        public int ReportedCount()
        {
            if (_inventory == null || _possessedCountField == null) return -1;
            try { return (int)_possessedCountField.GetValue(_inventory); }
            catch (Exception) { return -1; }
        }

        // ------------------------------------------------------------------
        /// Refills Stacks from the live inventory. Call from Tick on a
        /// throttle, never from OnGUI.
        public void Refresh()
        {
            _stacks.Clear();
            TotalItems = 0;
            TotalStacks = 0;
            FilteredOut = 0;

            if (_inventory == null || _possessedItemsField == null) return;

            RefreshEquipped();

            IList list;
            try { list = _possessedItemsField.GetValue(_inventory) as IList; }
            catch (Exception) { return; }
            if (list == null) return;

            for (int i = 0; i < list.Count; i++)
            {
                object item = list[i];
                if (item == null) continue;

                if (!_itemFieldsResolved) ResolveItemFields(item.GetType());
                if (_itemIdField == null || _amountField == null) return;

                int id, amount;
                try
                {
                    id = (int)_itemIdField.GetValue(item);
                    amount = (int)_amountField.GetValue(item);
                }
                catch (Exception) { continue; }

                if (FilterPhantomItems && !IsRealItem(id)) { FilteredOut++; continue; }
                if (!ShowZeroAmounts && amount <= 0) continue;

                ItemStack stack;
                stack.Id = id;
                stack.Amount = amount;
                stack.Name = NameFor(id);
                stack.Equipped = _equipped.Contains(id);

                _stacks.Add(stack);
                TotalStacks++;
                if (amount > 0) TotalItems += amount;
            }

            _stacks.Sort(CompareStacks);
        }

        public static bool IsRealItem(int id)
        {
            if (id < MinValidItemId || id > MaxValidItemId) return false;
            if (id == GhostItemId) return false;
            return true;
        }

        // Equipped ids explain the "x0" entries: an item that is currently
        // held reads amount 0 in _possessedItems, which looks like a ghost
        // until you can see it is in a slot.
        private void RefreshEquipped()
        {
            _equipped.Clear();
            if (_equipmentSlotIdsField == null) return;

            try
            {
                int[] ids = _equipmentSlotIdsField.GetValue(_inventory) as int[];
                if (ids == null) return;

                for (int i = 0; i < ids.Length; i++)
                    if (ids[i] > 0 && !_equipped.Contains(ids[i])) _equipped.Add(ids[i]);
            }
            catch (Exception) { }
        }

        private static int CompareStacks(ItemStack a, ItemStack b)
        {
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        private void ResolveItemFields(Type itemType)
        {
            _itemFieldsResolved = true;
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _itemIdField = itemType.GetField("_itemId", flags);
            _amountField = itemType.GetField("_amount", flags);

            _log.LogInfo("InventoryItem fields. _itemId:" + (_itemIdField != null) +
                         " _amount:" + (_amountField != null));
        }

        // ------------------------------------------------------------------
        private string NameFor(int id)
        {
            string cached;
            if (_nameCache.TryGetValue(id, out cached)) return cached;

            string name = ResolveName(id);
            if (string.IsNullOrEmpty(name)) name = "item " + id;

            _nameCache[id] = name;
            return name;
        }

        private string ResolveName(int id)
        {
            if (_itemDatabase == null) ResolveDatabase();
            if (_itemDatabase == null || _itemByIdMethod == null) return null;

            try
            {
                object item = _itemByIdMethod.Invoke(_itemDatabase, new object[] { id });
                if (item == null) return null;

                if (_itemNameField == null)
                {
                    _itemNameField = item.GetType().GetField("_name",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (_itemNameField == null) return null;
                }

                return _itemNameField.GetValue(item) as string;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void ResolveDatabase()
        {
            if (_inventory == null || _itemDatabaseField == null) return;

            try { _itemDatabase = _itemDatabaseField.GetValue(_inventory); }
            catch (Exception) { return; }

            if (_itemDatabase == null) return;

            _itemByIdMethod = _itemDatabase.GetType().GetMethod("ItemById",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            _log.LogInfo("ItemDatabase resolved. ItemById:" + (_itemByIdMethod != null));
        }
    }
}
