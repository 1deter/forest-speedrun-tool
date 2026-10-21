using System;
using System.Collections.Generic;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The Inventory tab's Filter box (T-0289). The held rows a filter
    // keeps, and - so an item can be pinned to the HUD before it is
    // picked up - the game's items it matches that are not held (author,
    // 2026-10-10: listed greyed as x0, a click pins them like any row).
    //
    // Case is ignored and nothing is allocated per name (IndexOf with
    // OrdinalIgnoreCase): the list is rebuilt a few times a second while
    // the tab is open. Pure: linked into the tests.
    // ------------------------------------------------------------------
    public static class InventoryFilter
    {
        /// True when `name` or its runner word (ItemWords, T-0407) contains
        /// `filter`, case ignored; an empty filter keeps every name, a null
        /// name none.
        public static bool Matches(string name, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            if (name == null) return false;
            return At(name, filter) >= 0;
        }

        // Where `filter` is in the game's name, else in its runner word
        // (a constant string: nothing allocated); -1 when in neither.
        private static int At(string name, string filter)
        {
            int at = name.IndexOf(filter, StringComparison.OrdinalIgnoreCase);
            if (at >= 0) return at;
            string word = ItemWords.Say(name);
            return ReferenceEquals(word, name) ? -1 : word.IndexOf(filter, StringComparison.OrdinalIgnoreCase);
        }

        /// Fills `into` with the indices into `catalog` of the names that
        /// match `filter` and are not in `held` (case ignored), each name
        /// once: those starting with the filter first, then those only
        /// containing it, each part in the catalogue's order. An empty
        /// filter lists nothing: the whole item list is only offered to a
        /// search.
        public static void NotHeld(IList<string> catalog, IList<string> held, string filter, List<int> into)
        {
            into.Clear();
            if (string.IsNullOrEmpty(filter) || catalog == null) return;

            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < catalog.Count; i++)
                {
                    string name = catalog[i];
                    if (name == null) continue;
                    int at = At(name, filter);
                    if (at < 0 || (pass == 0) != (at == 0)) continue;
                    if (Contains(held, name, held == null ? 0 : held.Count)) continue;
                    if (AlreadyIn(catalog, into, name)) continue;
                    into.Add(i);
                }
            }
        }

        private static bool Contains(IList<string> names, string name, int count)
        {
            for (int i = 0; i < count; i++)
                if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // The game's list can carry one name under two ids; a pin is by name.
        private static bool AlreadyIn(IList<string> catalog, List<int> picked, string name)
        {
            for (int i = 0; i < picked.Count; i++)
                if (string.Equals(catalog[picked[i]], name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
