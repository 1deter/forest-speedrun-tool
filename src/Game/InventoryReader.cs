using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Game
{
    public struct ItemStack
    {
        public int Id;
        public int Amount;
        public string Name;
    }

    // ------------------------------------------------------------------
    // Reads the player's inventory. INFO-ONLY - nothing here writes.
    //
    // Field layout confirmed from an F11 filtered dump, not guessed:
    //
    //   TheForest.Items.Inventory.PlayerInventory
    //     ._possessedItems      List<InventoryItem>
    //     ._possessedItemsCount int
    //     ._itemDatabase        ItemDatabase
    //
    //   TheForest.Items.Inventory.InventoryItem
    //     ._itemId    int
    //     ._amount    int
    //     ._maxAmount int
    //
    //   TheForest.Items.ItemDatabase
    //     .ItemById(int) -> TheForest.Items.Item, whose ._name is the
    //     display name. Names are cached by id because ItemById walks a
    //     dictionary and this runs on every refresh.
    //
    // The list is read through the non-generic IList interface. That
    // avoids having to construct a List<InventoryItem> generic type by
    // reflection, which is exactly the kind of thing that upsets the old
    // Mono runtime.
    // ------------------------------------------------------------------
    public sealed class InventoryReader
    {
        private readonly ManualLogSource _log;

        private Component _inventory;
        private FieldInfo _possessedCountField;
        private FieldInfo _possessedItemsField;
        private FieldInfo _itemDatabaseField;
        private bool _resolved;

        private FieldInfo _itemIdField;
        private FieldInfo _amountField;
        private bool _itemFieldsResolved;

        private object _itemDatabase;
        private MethodInfo _itemByIdMethod;
        private FieldInfo _itemNameField;
        private bool _databaseResolved;

        private readonly Dictionary<int, string> _nameCache = new Dictionary<int, string>();
        private readonly List<ItemStack> _stacks = new List<ItemStack>();

        public bool Available { get { return _inventory != null && _possessedItemsField != null; } }
        public IList<ItemStack> Stacks { get { return _stacks; } }

        public InventoryReader(ManualLogSource log)
        {
            _log = log;
        }

        public void Reset()
        {
            _inventory = null;
            _possessedCountField = null;
            _possessedItemsField = null;
            _itemDatabaseField = null;
            _resolved = false;
            _itemFieldsResolved = false;
            _databaseResolved = false;
            _itemDatabase = null;
            _nameCache.Clear();
            _stacks.Clear();
        }

        public void Resolve()
        {
            if (_resolved) return;

            Type invType = GameBridge.FindGameType("TheForest.Items.Inventory.PlayerInventory");
            if (invType == null) return;

            UnityEngine.Object found;
            try { found = UnityEngine.Object.FindObjectOfType(invType); }
            catch (Exception) { return; }

            if (found == null) return;   // not spawned yet - retry next frame

            _resolved = true;
            _inventory = found as Component;

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _possessedCountField = invType.GetField("_possessedItemsCount", flags);
            _possessedItemsField = invType.GetField("_possessedItems", flags);
            _itemDatabaseField = invType.GetField("_itemDatabase", flags);

            _log.LogInfo("PlayerInventory resolved. count:" + (_possessedCountField != null) +
                         " items:" + (_possessedItemsField != null) +
                         " db:" + (_itemDatabaseField != null));
        }

        public int TotalCount()
        {
            if (_inventory == null || _possessedCountField == null) return -1;
            try { return (int)_possessedCountField.GetValue(_inventory); }
            catch (Exception) { return -1; }
        }

        /// Refills Stacks with the current inventory contents. Call from
        /// Tick on a throttle, never from OnGUI.
        public void Refresh()
        {
            _stacks.Clear();
            if (_inventory == null || _possessedItemsField == null) return;

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

                ItemStack stack;
                try
                {
                    stack.Id = (int)_itemIdField.GetValue(item);
                    stack.Amount = (int)_amountField.GetValue(item);
                }
                catch (Exception) { continue; }

                stack.Name = NameFor(stack.Id);
                _stacks.Add(stack);
            }

            _stacks.Sort(CompareStacks);
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
            if (name == null) name = "item " + id;

            _nameCache[id] = name;
            return name;
        }

        private string ResolveName(int id)
        {
            if (!_databaseResolved) ResolveDatabase();
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
            _databaseResolved = true;
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
