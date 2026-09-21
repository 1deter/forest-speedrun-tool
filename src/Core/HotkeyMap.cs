using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // Declarative, rebindable hotkey table.
    //
    // Every binding is backed by a BepInEx ConfigEntry, so keys persist in
    // BepInEx/config/com.deter.forestoverlay.cfg, can be edited by hand,
    // and show up in ConfigurationManager if the user has it. Rebinding
    // from our own settings panel just writes the same entry.
    //
    // Modules register their own keys, so the list can never drift from
    // the handlers - the settings panel and the startup log line are both
    // generated from this table.
    //
    // Rewired owns the game's own bindings. Plain Input.GetKeyDown still
    // sees raw keys, which is why the defaults are F-keys: they are
    // unlikely to collide with a game action.
    // ------------------------------------------------------------------
    public sealed class HotkeyMap
    {
        public sealed class Binding
        {
            public string Id;
            public string Description;
            public Action Action;
            public ConfigEntry<KeyCode> Entry;
            public KeyCode Default;

            public KeyCode Key
            {
                get { return Entry != null ? Entry.Value : Default; }
                set { if (Entry != null) Entry.Value = value; }
            }
        }

        private readonly List<Binding> _bindings = new List<Binding>();
        private readonly ConfigFile _config;

        public IList<Binding> Bindings { get { return _bindings; } }

        /// Set while the settings panel is waiting for a key press.
        /// Dispatch is suppressed so the key being bound does not also
        /// fire the action it is being bound to.
        public Binding AwaitingRebind;

        public HotkeyMap(ConfigFile config)
        {
            _config = config;
        }

        public void Add(string id, KeyCode defaultKey, string description, Action action)
        {
            Binding b = new Binding();
            b.Id = id;
            b.Description = description;
            b.Action = action;
            b.Default = defaultKey;

            if (_config != null)
            {
                b.Entry = _config.Bind("Hotkeys", id, defaultKey, description);
            }

            _bindings.Add(b);
        }

        public void Dispatch()
        {
            if (AwaitingRebind != null) return;

            for (int i = 0; i < _bindings.Count; i++)
            {
                Binding b = _bindings[i];
                if (b.Key == KeyCode.None) continue;
                if (!Input.GetKeyDown(b.Key)) continue;
                b.Action();
            }
        }

        /// Returns the binding that already uses this key, if any. Used to
        /// warn about a clash rather than silently creating one.
        public Binding Conflict(KeyCode key, Binding ignoring)
        {
            if (key == KeyCode.None) return null;

            for (int i = 0; i < _bindings.Count; i++)
            {
                if (_bindings[i] == ignoring) continue;
                if (_bindings[i].Key == key) return _bindings[i];
            }
            return null;
        }

        public void ResetToDefaults()
        {
            for (int i = 0; i < _bindings.Count; i++)
                _bindings[i].Key = _bindings[i].Default;
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
