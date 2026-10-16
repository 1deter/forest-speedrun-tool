using System;
using System.Collections.Generic;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The game's input actions as the input overlay's "game input" shows
    // them (T-0361). The table is filled by Game/GameActions from the
    // game's shipped default bindings (Rewired UserData); this file is the
    // pure part: which actions a key stands for, in which order, and what
    // each is called.
    //
    // Author 2026-10-16: a tile stands for its key's SHIPPED-DEFAULT
    // actions (a runner's rebind moves where the action is pressed, not the
    // tile), is labelled with the action performed, and lights whenever one
    // of them fires, whatever key the runner bound it to.
    // ------------------------------------------------------------------
    public sealed class InputActionTable
    {
        public const int MaxPerKey = 4;

        private sealed class Row
        {
            public string Key;
            public int Action, Sign, Order;
        }

        private readonly List<string> _names = new List<string>();
        private readonly List<int> _categories = new List<int>();
        private readonly List<Row> _rows = new List<Row>();

        public int ActionCount { get { return _names.Count; } }
        public int RowCount { get { return _rows.Count; } }

        public int AddAction(string name, int category)
        {
            _names.Add(name ?? "");
            _categories.Add(category);
            return _names.Count - 1;
        }

        public string NameOf(int action) { return action >= 0 && action < _names.Count ? _names[action] : ""; }

        public int IndexOf(string name)
        {
            for (int i = 0; i < _names.Count; i++)
                if (string.Equals(_names[i], name, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// A key (a Unity KeyCode name) bound to an action; sign +1 / -1 for the
        /// two ends of an axis action (W is Vertical +1, S Vertical -1).
        public void AddBinding(string key, int action, int sign)
        {
            if (key == null || action < 0 || action >= _names.Count) return;
            sign = sign < 0 ? -1 : 1;
            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                if (r.Action == action && r.Sign == sign && r.Key == key) return;
            }
            Row n = new Row();
            n.Key = key;
            n.Action = action;
            n.Sign = sign;
            n.Order = _rows.Count;
            _rows.Add(n);
        }

        /// Menu (1) and chat (7) actions only stand in for a key that has nothing else.
        public static bool IsMenuCategory(int category) { return category == 1 || category == 7; }

        /// Lower is chosen first: moves 6, actions 2, building 3, inventory 4, the rest.
        public static int Rank(int category)
        {
            switch (category)
            {
                case 6: return 0;
                case 2: return 1;
                case 3: return 2;
                case 4: return 3;
            }
            return IsMenuCategory(category) ? 9 : 5;
        }

        /// The key's actions in pick order (best first), up to MaxPerKey, into
        /// `actions` / `signs`; returns how many. Menu / chat actions are left out
        /// when the key has any other.
        public int Resolve(string key, int[] actions, int[] signs)
        {
            int n = 0;
            bool other = false;
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i].Key == key && !IsMenuCategory(_categories[_rows[i].Action])) { other = true; break; }
            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                if (r.Key != key) continue;
                if (other && IsMenuCategory(_categories[r.Action])) continue;
                int at = n;
                while (at > 0 && Before(r, actions[at - 1], signs[at - 1])) at--;
                if (n >= actions.Length && at >= n) continue;
                int last = Math.Min(n, actions.Length - 1);
                for (int k = last; k > at; k--) { actions[k] = actions[k - 1]; signs[k] = signs[k - 1]; }
                actions[at] = r.Action;
                signs[at] = r.Sign;
                if (n < actions.Length) n++;
            }
            return n;
        }

        /// How many actions the key has in all (Resolve fits MaxPerKey of them).
        public int Total(string key)
        {
            int n = 0;
            bool other = false;
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i].Key == key && !IsMenuCategory(_categories[_rows[i].Action])) { other = true; break; }
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i].Key == key && !(other && IsMenuCategory(_categories[_rows[i].Action]))) n++;
            return n;
        }

        private bool Before(Row r, int otherAction, int otherSign)
        {
            int a = Rank(_categories[r.Action]), b = Rank(_categories[otherAction]);
            return a < b;   // equal rank keeps the map's order
        }

        /// What a tile says for an action: Vertical / Horizontal by direction, a short word
        /// for the long names, otherwise the game's own name.
        public static string Label(string action, int sign)
        {
            switch (action)
            {
                case "Vertical": return sign < 0 ? "Back" : "Forward";
                case "Horizontal": return sign < 0 ? "Left" : "Right";
                case "Fire1": return "Attack";
                case "AltFire": return "Alt fire";
                case "RestKey": return "Rest";
                case "SurvivalBook": return "Book";
                case "ItemSlot1": return "Slot 1";
                case "ItemSlot2": return "Slot 2";
                case "ItemSlot3": return "Slot 3";
                case "ItemSlot4": return "Slot 4";
                case "WalkyTalky": return "Walkie";
                case "checkArms": return "Arms";
                case "birdOnHand": return "Bird";
            }
            return action ?? "";
        }
    }
}
