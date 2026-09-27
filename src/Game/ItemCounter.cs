using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Every item the player carries, by the game's database name, for a
    // run's item track (`i|` lines, Data/AttemptFormat; author, 2026-09-27:
    // "dynamically count items" - sodas, coins, stamina mixes, whatever a
    // route picks up). Replaces v0.24.160's fixed list of 16 channels.
    //
    // Cost: PlayerInventory._possessedItems holds only what the player owns
    // (6 entries in a mid-game save; speedruns carry little), read at the
    // recorder's 5 Hz state cadence. Per entry: one boxed id read, then the
    // game's own AmountOf(id, false) through a delegate bound to the live
    // inventory - the held item included (an equipped entry reads amount 0
    // in the list; checked: a Molotov stays 2 after Equip). Names are
    // cached per id. Only changes reach the file (Data/RunRecorder).
    //
    // EVENT-DRIVEN (author, 2026-09-27: "only really updates when an item
    // is changed"): postfixes on every game method that writes an item
    // amount (ilscan `writes InventoryItem::_amount`: InventoryItem
    // Add / Remove / RemoveOverflow, PlayerInventory AddItemNF /
    // RemoveItemNF / FixMaxAmountBonuses / Add- / SetMaxAmountBonus, the
    // OnDeserialized coroutine) bump a version; the inventory is read only
    // when it moved. An unchanged bag costs one int compare per sample.
    // A full read every 5 s anyway covers a path the list missed.
    // ------------------------------------------------------------------
    public sealed class ItemCounter
    {
        private const float SafetyInterval = 5f;

        private static int _version;
        private static Harmony _harmony;

        private int _seenVersion = -1;
        private float _lastRead = -1000f;
        private int _reads;

        private readonly ManualLogSource _log;
        private readonly InventoryReader _names;
        private readonly Dictionary<int, string> _nameById = new Dictionary<int, string>();

        private object _inventory;
        private FieldInfo _possessed;
        private FieldInfo _itemId;
        private Func<int, bool, int> _amountOf;
        private bool _logged;

        public ItemCounter(ManualLogSource log, InventoryReader names)
        {
            _log = log;
            _names = names;
        }

        /// Reads so far (the bridge's "did it see anything" count).
        public int Reads { get { return _reads; } }
        public static int Version { get { return _version; } }

        public static void Install(ManualLogSource log, string harmonyId)
        {
            try
            {
                Type inv = GameBridge.FindGameType("TheForest.Items.Inventory.PlayerInventory");
                Type item = GameBridge.FindGameType("TheForest.Items.Inventory.InventoryItem");
                if (inv == null || item == null) { log.LogWarning("Run item track: inventory types not found - items re-read every 5 s instead."); return; }

                _harmony = new Harmony(harmonyId + ".itemcounter");
                HarmonyMethod post = new HarmonyMethod(typeof(ItemCounter).GetMethod("Changed", BindingFlags.Static | BindingFlags.NonPublic));
                int n = 0;
                n += PatchAll(inv, post, "AddItemNF", "RemoveItemNF", "FixMaxAmountBonuses", "AddMaxAmountBonus", "SetMaxAmountBonus");
                n += PatchAll(item, post, "Add", "Remove", "RemoveOverflow");
                foreach (Type nested in inv.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
                    if (nested.Name.StartsWith("<OnDeserialized>")) n += PatchAll(nested, post, "MoveNext");
                log.LogInfo("Run item track: " + n + " inventory methods watched.");
            }
            catch (Exception e) { log.LogWarning("Run item track: " + e.Message + " - items re-read every 5 s instead."); }
        }

        private static int PatchAll(Type t, HarmonyMethod post, params string[] names)
        {
            int n = 0;
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (Array.IndexOf(names, m.Name) >= 0 && !m.IsAbstract) { _harmony.Patch(m, postfix: post); n++; }
            return n;
        }

        public static void Uninstall()
        {
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception) { }
        }

        private static void Changed() { _version++; }

        /// The recorder's ItemSource: fills `counts` (already cleared) with
        /// what the player carries and returns true, or returns false when
        /// nothing moved since the last read (not forced, same inventory,
        /// the safety interval not up). Empty when there is no inventory.
        public bool Fill(Dictionary<string, int> counts, bool force)
        {
            object before = _inventory;
            bool bound = Bind();
            bool same = bound && ReferenceEquals(before, _inventory);
            float now = Time.realtimeSinceStartup;
            if (!force && same && _version == _seenVersion && now - _lastRead < SafetyInterval) return false;
            _seenVersion = _version;
            _lastRead = now;
            _reads++;
            if (!bound) return true;

            IList list;
            try { list = _possessed.GetValue(_inventory) as IList; }
            catch (Exception) { return true; }
            if (list == null) return true;

            for (int i = 0; i < list.Count; i++)
            {
                object item = list[i];
                if (item == null) continue;
                if (_itemId == null) _itemId = item.GetType().GetField("_itemId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (_itemId == null) return true;

                int id;
                try { id = (int)_itemId.GetValue(item); }
                catch (Exception) { continue; }
                if (!InventoryReader.IsRealItem(id)) continue;

                string name = NameOf(id);
                if (counts.ContainsKey(name)) continue;   // one entry per id

                int n;
                try { n = _amountOf(id, false); }
                catch (Exception) { _inventory = null; return true; }
                counts[name] = n;
            }

            if (!_logged)
            {
                _logged = true;
                _log.LogInfo("Run item track: reading " + list.Count + " inventory entries (" + counts.Count + " items).");
            }
            return true;
        }

        private string NameOf(int id)
        {
            string name;
            if (_nameById.TryGetValue(id, out name)) return name;
            name = _names != null ? _names.NameForId(id) : null;
            if (string.IsNullOrEmpty(name)) name = "item" + id;
            _nameById[id] = name;
            return name;
        }

        /// Follows the live inventory (a load replaces it).
        private bool Bind()
        {
            object live = GameBridge.ReadStaticField("TheForest.Utils.LocalPlayer", "Inventory");
            if (live == null || (live as UnityEngine.Object) == null) { _inventory = null; return false; }
            if (ReferenceEquals(live, _inventory)) return _amountOf != null && _possessed != null;

            _inventory = live;
            _amountOf = null;
            Type t = live.GetType();
            _possessed = t.GetField("_possessedItems", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo m = t.GetMethod("AmountOf", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(int), typeof(bool) }, null);
            if (m == null || _possessed == null)
            {
                _log.LogWarning("Run item track: PlayerInventory has no " + (m == null ? "AmountOf(int, bool)" : "_possessedItems") + " - items not recorded.");
                return false;
            }
            try { _amountOf = (Func<int, bool, int>)Delegate.CreateDelegate(typeof(Func<int, bool, int>), live, m); }
            catch (Exception e) { _log.LogWarning("Run item track: AmountOf not bound - " + e.Message); return false; }
            return true;
        }
    }
}
