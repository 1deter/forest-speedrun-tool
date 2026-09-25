using System;
using System.Reflection;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Closes the survival book on a reset (runner sxczurass: restarting a
    // savestate with the book open left it in the hands).
    //
    // The book is not an equipped item, so the restore's StashHands never
    // sees it: the inventory stays in its Book view (the player locked,
    // the HUD hidden). AnimReset alone switched `bookHeld` off under it.
    //
    // WHAT (IL): Create.CloseBookForInventory - the game's fast close,
    // used when the inventory is opened from the book. CloseTheBook(true)
    // puts the view back to World at once (showEquipped: RestoreEquipement,
    // inventory enabled) and fastCloseBook plays the quick close on the
    // book model. Runs before AnimReset.Cancel, which would otherwise
    // clear `bookHeld` first.
    // ------------------------------------------------------------------
    public static class BookClose
    {
        private static FieldInfo _inventory;     // static LocalPlayer.Inventory
        private static FieldInfo _create;        // static LocalPlayer.Create
        private static PropertyInfo _view;       // PlayerInventory.CurrentView
        private static MethodInfo _close;        // Create.CloseBookForInventory()
        private static object _bookView;         // PlayerViews.Book
        private static bool _resolved;

        /// Closes the book when it is open. Says what it did; "" when the
        /// book was not open.
        public static string IfOpen()
        {
            try
            {
                if (!Resolve()) return "";
                object inv = _inventory.GetValue(null);
                object create = _create.GetValue(null);
                if (inv == null || create == null) return "";
                if (!Equals(_view.GetValue(inv, null), _bookView)) return "";
                _close.Invoke(create, null);
                return "closed the book";
            }
            catch (Exception ex)
            {
                return "closing the book failed: " + (ex.InnerException ?? ex).Message;
            }
        }

        private static bool Resolve()
        {
            if (_resolved) return _close != null;
            _resolved = true;
            const BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
            Type inv = GameBridge.FindGameType("TheForest.Items.Inventory.PlayerInventory");
            Type create = GameBridge.FindGameType("TheForest.Buildings.Creation.Create");
            if (local == null || inv == null || create == null) return false;

            _inventory = local.GetField("Inventory", stat);
            _create = local.GetField("Create", stat);
            _view = inv.GetProperty("CurrentView", inst);
            Type views = inv.GetNestedType("PlayerViews", BindingFlags.Public | BindingFlags.NonPublic);
            if (views != null && Enum.IsDefined(views, "Book")) _bookView = Enum.Parse(views, "Book");
            MethodInfo close = create.GetMethod("CloseBookForInventory", inst, null, Type.EmptyTypes, null);

            if (_inventory == null || _create == null || _view == null || _bookView == null) return false;
            _close = close;
            return _close != null;
        }
    }
}
