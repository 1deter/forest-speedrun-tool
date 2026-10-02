using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Timmy's drawings, piece by piece (the author's list, Next up 13;
    // v0.24.197). The game keeps every drawing inside one item (208): its
    // inventory view, DrawingsInventoryItemView on INVENTORY/Special/
    // TimmyDrawings_Inv, lists the pieces held (`_ids`) and the ones put up
    // on the wall (`_usedIds`, DrawingsPlacer -> PopLast). A piece's id is
    // its DrawingPickUp._id, an index into Prefabs.TimmyDrawingsMats
    // (11 materials, 0 the blank page; pickups 9 and 10 seen live). The
    // game counts no total, so neither does this - it says which are found.
    //
    // The view sits under its own root and is inactive: found with
    // FindObjectsOfTypeAll once per load (gotcha 11), re-found when it goes.
    // ------------------------------------------------------------------
    public static class DrawingsReader
    {
        private static Type _type;
        private static FieldInfo _ids, _used;
        private static UnityEngine.Object _view;
        private static float _nextSearch;

        /// The ids found so far (held or placed), sorted; empty when none
        /// or the view is not loaded.
        public static int[] Found()
        {
            try
            {
                if (_type == null)
                {
                    _type = GameBridge.FindGameType("TheForest.Items.Inventory.DrawingsInventoryItemView");
                    if (_type == null) return new int[0];
                    const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    _ids = _type.GetField("_ids", f);
                    _used = _type.GetField("_usedIds", f);
                }
                if (_ids == null || _used == null) return new int[0];
                if (_view == null)
                {
                    if (Time.unscaledTime < _nextSearch) return new int[0];
                    _nextSearch = Time.unscaledTime + 10f;
                    UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(_type);
                    for (int i = 0; i < all.Length; i++)
                    {
                        Component c = all[i] as Component;
                        if (c != null && c.gameObject.scene.IsValid()) { _view = c; break; }
                    }
                    if (_view == null) return new int[0];
                }
                System.Collections.Generic.List<int> found = new System.Collections.Generic.List<int>();
                Add(found, _ids.GetValue(_view) as IList);
                Add(found, _used.GetValue(_view) as IList);
                found.Sort();
                return found.ToArray();
            }
            catch (Exception) { return new int[0]; }
        }

        private static void Add(System.Collections.Generic.List<int> found, IList list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                int id = (int)list[i];
                if (!found.Contains(id)) found.Add(id);
            }
        }
    }
}
