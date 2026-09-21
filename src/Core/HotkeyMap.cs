using System;
using System.Collections.Generic;
using UnityEngine;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // Declarative hotkey table.
    //
    // Modules register their own keys, so the key list is never duplicated
    // between the handler and the help text - Describe() is generated from
    // the same data that dispatches. Rewired owns the game's bindings, but
    // plain Input.GetKeyDown still sees raw function keys, which is why
    // the overlay sticks to F-keys.
    // ------------------------------------------------------------------
    public sealed class HotkeyMap
    {
        private struct Binding
        {
            public KeyCode Key;
            public string Description;
            public Action Action;
        }

        private readonly List<Binding> _bindings = new List<Binding>();

        public void Add(KeyCode key, string description, Action action)
        {
            Binding b;
            b.Key = key;
            b.Description = description;
            b.Action = action;
            _bindings.Add(b);
        }

        public void Dispatch()
        {
            for (int i = 0; i < _bindings.Count; i++)
            {
                if (!Input.GetKeyDown(_bindings[i].Key)) continue;
                _bindings[i].Action();
            }
        }

        public string Describe()
        {
            string s = "";
            for (int i = 0; i < _bindings.Count; i++)
            {
                if (i > 0) s += " | ";
                s += _bindings[i].Key.ToString() + " " + _bindings[i].Description;
            }
            return s;
        }
    }
}
