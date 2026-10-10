using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The player's state as a start state restore left it, for Keep loaded
    // (T-0212): taken right after the real restore, put back on each cheap
    // restart (author, 2026-10-10: "Player + endgame movers").
    //
    // THE GAME (live, bridge 2026-10-10, v0.24.270): PlayerStats fields
    // Health, HealthTarget, Stamina (+ setStamina(float), which keeps the
    // FSM's copy in step), Energy, Fullness, Thirst; PlayerInventory
    // _possessedItems (List<InventoryItem>: _itemId, _amount), AddItem(id,
    // amount, preventAutoEquip, fromCraftingCog, properties), RemoveItem(id,
    // amount, allowAmountOverflow, shouldEquipPrevious). Logs (item 78) are
    // not in the list (LogStore). The held items come back from the start
    // state file (SavestateBridge.ReEquip), not from here.
    // ------------------------------------------------------------------
    public sealed class PlayerKeep
    {
        private static readonly string[] StatNames = { "Health", "HealthTarget", "Energy", "Fullness", "Thirst" };

        private bool _resolved;
        private FieldInfo[] _stats;
        private FieldInfo _stamina;
        private MethodInfo _setStamina;
        private FieldInfo _possessed, _itemId, _amount;
        private MethodInfo _addItem, _removeItem;

        private float[] _values;
        private float _staminaValue;
        private Dictionary<int, int> _items;

        public bool Has { get { return _values != null; } }

        public void Forget() { _values = null; _items = null; }

        private bool Resolve()
        {
            if (_resolved) return _stats != null;
            _resolved = true;
            BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type stats = GameBridge.FindGameType("PlayerStats");
            Type inv = GameBridge.FindGameType("TheForest.Items.Inventory.PlayerInventory");
            Type item = GameBridge.FindGameType("TheForest.Items.Inventory.InventoryItem");
            if (stats == null || inv == null || item == null) return false;

            FieldInfo[] f = new FieldInfo[StatNames.Length];
            for (int i = 0; i < f.Length; i++)
            {
                f[i] = stats.GetField(StatNames[i], inst);
                if (f[i] == null || f[i].FieldType != typeof(float)) return false;
            }
            _stamina = stats.GetField("Stamina", inst);
            _setStamina = stats.GetMethod("setStamina", inst, null, new[] { typeof(float) }, null);
            _possessed = inv.GetField("_possessedItems", inst);
            _itemId = item.GetField("_itemId", inst);
            _amount = item.GetField("_amount", inst);
            foreach (MethodInfo m in inv.GetMethods(inst))
            {
                if (m.Name == "AddItem" && m.GetParameters().Length == 5) _addItem = m;
                else if (m.Name == "RemoveItem" && m.GetParameters().Length == 4) _removeItem = m;
            }
            if (_stamina == null || _possessed == null || _itemId == null || _amount == null ||
                _addItem == null || _removeItem == null) return false;
            _stats = f;
            return true;
        }

        private static object Stats() { return Live(GameBridge.ReadStaticField("TheForest.Utils.LocalPlayer", "Stats")); }
        private static object Inventory() { return Live(GameBridge.ReadStaticField("TheForest.Utils.LocalPlayer", "Inventory")); }

        private static object Live(object o)
        {
            UnityEngine.Object u = o as UnityEngine.Object;
            return u != null ? o : null;
        }

        /// Takes the live player's state; a note when it could not.
        public string Take()
        {
            Forget();
            if (!Resolve()) return "player: PlayerStats / PlayerInventory members not found";
            object stats = Stats(), inv = Inventory();
            if (stats == null || inv == null) return "player: no live player";
            try
            {
                float[] v = new float[_stats.Length];
                for (int i = 0; i < v.Length; i++) v[i] = (float)_stats[i].GetValue(stats);
                _staminaValue = (float)_stamina.GetValue(stats);
                _items = Items(inv);
                _values = v;
                return "";
            }
            catch (Exception ex) { return "player: taking failed (" + ex.Message + ")"; }
        }

        private Dictionary<int, int> Items(object inv)
        {
            Dictionary<int, int> d = new Dictionary<int, int>();
            IList list = _possessed.GetValue(inv) as IList;
            if (list == null) return d;
            for (int i = 0; i < list.Count; i++)
            {
                object it = list[i];
                if (it == null) continue;
                int id = (int)_itemId.GetValue(it), n = (int)_amount.GetValue(it);
                int had;
                d.TryGetValue(id, out had);
                d[id] = had + n;
            }
            return d;
        }

        /// Puts the taken state back; the note for the restart's line.
        public string Restore()
        {
            if (_values == null) return "player: nothing taken";
            object stats = Stats(), inv = Inventory();
            if (stats == null || inv == null) return "player: no live player";
            try
            {
                for (int i = 0; i < _stats.Length; i++) _stats[i].SetValue(stats, _values[i]);
                if (_setStamina != null) _setStamina.Invoke(stats, new object[] { _staminaValue });
                else _stamina.SetValue(stats, _staminaValue);

                Dictionary<int, int> now = Items(inv);
                int changed = 0, failed = 0;
                foreach (KeyValuePair<int, int> want in _items)
                {
                    int have;
                    now.TryGetValue(want.Key, out have);
                    if (have == want.Value) continue;
                    changed++;
                    if (!Change(inv, want.Key, want.Value - have)) failed++;
                }
                foreach (KeyValuePair<int, int> extra in now)
                {
                    if (_items.ContainsKey(extra.Key) || extra.Value == 0) continue;
                    changed++;
                    if (!Change(inv, extra.Key, -extra.Value)) failed++;
                }
                return "player: health " + _values[0].ToString("0") + ", stamina " + _staminaValue.ToString("0") +
                       ", " + changed + " item(s) set back" + (failed > 0 ? " (" + failed + " refused)" : "");
            }
            catch (Exception ex) { return "player: putting back failed (" + ex.Message + ")"; }
        }

        private bool Change(object inv, int id, int by)
        {
            object ok = by > 0
                ? _addItem.Invoke(inv, new object[] { id, by, true, false, null })
                : _removeItem.Invoke(inv, new object[] { id, -by, false, false });
            return ok is bool && (bool)ok;
        }
    }
}
