using System;
using System.Reflection;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Closes the game's pause menu or inventory on a reset (runner Tom,
    // v0.24.112: F7 with the ESC menu open "half-loads the savestate
    // until you close the menu").
    //
    // WHY (bridge, 2026-09-26): the pause menu and the inventory both set
    // Time.timeScale to 0. A Quick load runs over frames of game time
    // (the hands are put away, the keepers wait), so it sat "busy" for as
    // long as the menu stayed open - 120 s and counting - and finished
    // within a second of the menu closing. The options screens (graphics,
    // audio...) are children of HudGui.PauseMenu, so closing the pause
    // menu closes them too.
    //
    // WHAT (IL): the game's own closes. PlayerInventory.TogglePauseMenu
    // (the ESC key's path; from the Pause view it calls HudGui
    // .TogglePauseMenu(false): view World, input state back, PauseMenu
    // off, timeScale 1, view unlocked, blur off). PlayerInventory.Close
    // for the inventory and a storage (Loot) view. The book has its own
    // close (BookClose).
    // ------------------------------------------------------------------
    public static class MenuClose
    {
        private static FieldInfo _inventory;     // static LocalPlayer.Inventory
        private static PropertyInfo _view;       // PlayerInventory.CurrentView
        private static MethodInfo _togglePause;  // PlayerInventory.TogglePauseMenu()
        private static MethodInfo _close;        // PlayerInventory.Close()
        private static object _pauseView, _inventoryView, _lootView;
        private static bool _resolved;

        /// Closes the pause menu or the inventory when one is open. Says
        /// what it did; "" when neither was.
        public static string IfOpen()
        {
            try
            {
                if (!Resolve()) return "";
                object inv = _inventory.GetValue(null);
                if (inv == null) return "";
                object view = _view.GetValue(inv, null);

                if (Equals(view, _pauseView) && _togglePause != null)
                {
                    _togglePause.Invoke(inv, null);
                    return "closed the pause menu" + TimeNote();
                }
                if ((Equals(view, _inventoryView) || Equals(view, _lootView)) && _close != null)
                {
                    _close.Invoke(inv, null);
                    return "closed the inventory" + TimeNote();
                }
                return "";
            }
            catch (Exception ex)
            {
                return "closing the game's menu failed: " + (ex.InnerException ?? ex).Message;
            }
        }

        // The restore needs game time; say so if the close left it stopped.
        private static string TimeNote()
        {
            return Time.timeScale > 0f ? "" : " (time still stopped: timeScale 0)";
        }

        private static bool Resolve()
        {
            if (_resolved) return _inventory != null && _view != null;
            _resolved = true;
            const BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
            Type inv = GameBridge.FindGameType("TheForest.Items.Inventory.PlayerInventory");
            if (local == null || inv == null) return false;

            _inventory = local.GetField("Inventory", stat);
            _view = inv.GetProperty("CurrentView", inst);
            _togglePause = inv.GetMethod("TogglePauseMenu", inst, null, Type.EmptyTypes, null);
            _close = inv.GetMethod("Close", inst, null, Type.EmptyTypes, null);
            Type views = inv.GetNestedType("PlayerViews", BindingFlags.Public | BindingFlags.NonPublic);
            if (views != null)
            {
                if (Enum.IsDefined(views, "Pause")) _pauseView = Enum.Parse(views, "Pause");
                if (Enum.IsDefined(views, "Inventory")) _inventoryView = Enum.Parse(views, "Inventory");
                if (Enum.IsDefined(views, "Loot")) _lootView = Enum.Parse(views, "Loot");
            }
            return _inventory != null && _view != null;
        }
    }
}
