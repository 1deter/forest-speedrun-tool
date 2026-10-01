using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Any item's carry cap, set by the runner (Inventory tab -> Item caps;
    // sxczurass, QA 2026-09-27; v0.24.192). A gameplay mod: practice.
    //
    // HOW (IL): an item's cap is InventoryItem.MaxAmount (`_maxAmount` +
    // `_maxAmountBonus` - the pouches add the bonus), read by every add /
    // overflow check, and PlayerInventory.GetMaxAmountOf(id) for items not
    // possessed yet. Postfixes on both return the runner's cap for the
    // listed ids; nothing else changes, nothing is saved. A cap below what
    // is carried leaves the extra until the game next trims overflow.
    // Logs (78) are Game/LogStore's and left alone.
    // ------------------------------------------------------------------
    public static class ItemCapPatch
    {
        public const int LogItemId = 78;

        private static readonly Dictionary<int, int> Caps = new Dictionary<int, int>();
        private static Harmony _harmony;
        private static FieldInfo _itemId;

        /// "on" / why not, for the tab.
        public static string Status = "not installed";
        public static bool Installed { get; private set; }

        public static void Install(ManualLogSource log, string harmonyId)
        {
            try
            {
                Type item = GameBridge.FindGameType("TheForest.Items.Inventory.InventoryItem");
                Type inv = GameBridge.FindGameType("TheForest.Items.Inventory.PlayerInventory");
                MethodInfo max = item != null ? item.GetProperty("MaxAmount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetGetMethod(true) : null;
                MethodInfo maxOf = inv != null ? inv.GetMethod("GetMaxAmountOf", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null) : null;
                _itemId = item != null ? item.GetField("_itemId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null;
                if (max == null || maxOf == null || _itemId == null) { Status = "the game's cap methods not found - caps do nothing"; log.LogWarning("ItemCapPatch: " + Status + "."); return; }

                _harmony = new Harmony(harmonyId + ".itemcaps");
                _harmony.Patch(max, postfix: new HarmonyMethod(typeof(ItemCapPatch).GetMethod("MaxPostfix", BindingFlags.Static | BindingFlags.NonPublic)));
                _harmony.Patch(maxOf, postfix: new HarmonyMethod(typeof(ItemCapPatch).GetMethod("MaxOfPostfix", BindingFlags.Static | BindingFlags.NonPublic)));
                Installed = true;
                Status = "on";
            }
            catch (Exception ex) { Status = "could not patch: " + ex.Message; log.LogWarning("ItemCapPatch: " + ex); }
        }

        public static void Uninstall()
        {
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception) { }
        }

        /// The caps in force (empty = the game's own everywhere).
        public static void Set(List<KeyValuePair<int, int>> caps)
        {
            Caps.Clear();
            if (caps == null) return;
            for (int i = 0; i < caps.Count; i++)
                if (caps[i].Key != LogItemId) Caps[caps[i].Key] = caps[i].Value;
        }

        public static int Count { get { return Caps.Count; } }

        private static void MaxPostfix(object __instance, ref int __result)
        {
            if (Caps.Count == 0 || __instance == null) return;
            try
            {
                int cap;
                if (Caps.TryGetValue((int)_itemId.GetValue(__instance), out cap)) __result = cap;
            }
            catch (Exception) { }
        }

        private static void MaxOfPostfix(int itemId, ref int __result)
        {
            int cap;
            if (Caps.Count > 0 && Caps.TryGetValue(itemId, out cap)) __result = cap;
        }
    }
}
