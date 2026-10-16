using System;
using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The game's input actions for the input overlay's "game input"
    // (T-0361). Every Rewired and TheForest.Utils.Input name lives here.
    //
    // - The table (which action each key / mouse button stands for) is the
    //   shipped default bindings, Rewired's ReInput.UserData (author
    //   2026-10-16: a tile shows the action, never the runner's bound key).
    // - Whether an action is held NOW is read from the running Rewired
    //   Player by action id, so a rebind follows the action. NEVER through
    //   TheForest.Utils.Input.GetButton*/GetAxis*: InputInject counts those
    //   reads and a TAS recording would take the overlay's for the game's.
    // - The bridge's injected input and a TAS replay replace the game's reads
    //   but not Rewired's, so they are asked for here (InputInject.State,
    //   TasInput.Replay*), in the order a replay > the real hands + injected.
    // ------------------------------------------------------------------
    public static class GameActions
    {
        public static InputActionTable Table { get; private set; }

        /// "ready" or why the actions cannot be read (shown in Edit HUD).
        public static string Status = "not bound yet";

        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static int[] _ids = new int[0];
        private static string[] _names = new string[0];
        private static Func<int, bool> _button, _negative;
        private static Func<int, float> _axisRaw;
        private static MethodInfo _mButton, _mNegative, _mAxis;
        private static FieldInfo _playerField;
        private static object _boundPlayer;
        private static float _nextTry;
        private static bool _told;

        public static bool Ready { get { return Table != null && _mButton != null; } }

        /// Builds the table once Rewired has its data; tried again every few
        /// seconds until it works. Cheap when done.
        public static void Ensure(ManualLogSource log)
        {
            if (Table != null) return;
            float now = Time.realtimeSinceStartup;
            if (now < _nextTry) return;
            _nextTry = now + 3f;
            try { Build(log); }
            catch (Exception ex)
            {
                Status = "cannot read the game's actions (" + ex.Message + ")";
                if (!_told) { _told = true; log.LogWarning("GameActions: " + Status); }
            }
        }

        private static object Get(object o, string name)
        {
            if (o == null) return null;
            Type t = o.GetType();
            PropertyInfo p = t.GetProperty(name, Any);
            if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(o, null);
            FieldInfo f = t.GetField(name, Any);
            return f != null ? f.GetValue(o) : null;
        }

        private static object GetStatic(Type t, string name)
        {
            PropertyInfo p = t.GetProperty(name, Any);
            if (p != null) return p.GetValue(null, null);
            FieldInfo f = t.GetField(name, Any);
            return f != null ? f.GetValue(null) : null;
        }

        private static void Build(ManualLogSource log)
        {
            Type input = GameBridge.FindGameType("TheForest.Utils.Input");
            _playerField = input != null ? input.GetField("player", Any) : null;
            if (_playerField == null) { Status = "the game's input class changed"; return; }
            Type player = _playerField.FieldType;
            _mButton = player.GetMethod("GetButton", new[] { typeof(int) });
            _mNegative = player.GetMethod("GetNegativeButton", new[] { typeof(int) });
            _mAxis = player.GetMethod("GetAxisRaw", new[] { typeof(int) });
            Type reInput = player.Assembly.GetType("Rewired.ReInput", false);
            if (_mButton == null || _mNegative == null || _mAxis == null || reInput == null) { Status = "Rewired changed"; return; }

            object mapping = GetStatic(reInput, "mapping");
            IEnumerable actions = mapping != null ? Get(mapping, "Actions") as IEnumerable : null;
            if (actions == null) { Status = "Rewired has no actions yet"; return; }

            InputActionTable table = new InputActionTable();
            System.Collections.Generic.List<int> ids = new System.Collections.Generic.List<int>();
            foreach (object a in actions)
            {
                if (a == null) continue;
                object id = Get(a, "id"), cat = Get(a, "categoryId");
                ids.Add(id != null ? Convert.ToInt32(id) : -1);
                table.AddAction(Get(a, "name") as string, cat != null ? Convert.ToInt32(cat) : 0);
            }
            if (ids.Count == 0) { Status = "Rewired has no actions yet"; return; }

            object userData = GetStatic(reInput, "UserData");
            if (userData == null) { Status = "Rewired has no default bindings yet"; return; }

            _ids = ids.ToArray();
            _names = new string[_ids.Length];
            for (int i = 0; i < _names.Length; i++) _names[i] = table.NameOf(i);

            int keys = AddMaps(table, Get(userData, "keyboardMaps") as IEnumerable, false);
            int mouse = AddMaps(table, Get(userData, "mouseMaps") as IEnumerable, true);
            if (keys == 0) { Status = "no default key bindings found"; return; }
            Table = table;
            Status = "ready";
            log.LogInfo("GameActions: " + _ids.Length + " actions, " + keys + " keyboard + " + mouse + " mouse bindings (default).");
        }

        private static int AddMaps(InputActionTable table, IEnumerable maps, bool mouse)
        {
            int n = 0;
            if (maps == null) return 0;
            foreach (object map in maps)
            {
                object cat = Get(map, "categoryId");
                if (cat == null || Convert.ToInt32(cat) != 0) continue;   // the Default map category: what the player does in play
                IEnumerable elements = Get(map, "actionElementMaps") as IEnumerable;
                if (elements == null) continue;
                foreach (object e in elements)
                {
                    if (e == null) continue;
                    object actionId = Get(e, "actionId");
                    if (actionId == null) continue;
                    int index = Array.IndexOf(_ids, Convert.ToInt32(actionId));
                    if (index < 0) continue;
                    object pole = Get(e, "axisContribution");
                    int sign = pole != null && pole.ToString() == "Negative" ? -1 : 1;
                    string key;
                    if (mouse)
                    {
                        object el = Get(e, "elementIdentifierId");
                        int element = el != null ? Convert.ToInt32(el) : -1;
                        key = element == 3 ? "Mouse0" : element == 4 ? "Mouse1" : null;   // 0 / 1 / 2 are the look axes and the wheel
                    }
                    else
                    {
                        object code = Get(e, "keyCode");
                        key = code != null ? code.ToString() : null;
                        object mod = Get(e, "modifierKey1");
                        if (mod != null && mod.ToString() != "None") key = null;   // a chord, not the key alone
                        if (key == "None") key = null;
                    }
                    if (key == null) continue;
                    table.AddBinding(key, index, sign);
                    n++;
                }
            }
            return n;
        }

        // The Rewired player (Input.player), rebound if the game replaces it.
        private static bool BindPlayer()
        {
            if (_playerField == null || _mButton == null) return false;
            object p = _playerField.GetValue(null);
            if (p == null) { _boundPlayer = null; return false; }
            if (!ReferenceEquals(p, _boundPlayer))
            {
                _boundPlayer = p;
                _button = (Func<int, bool>)Delegate.CreateDelegate(typeof(Func<int, bool>), p, _mButton);
                _negative = (Func<int, bool>)Delegate.CreateDelegate(typeof(Func<int, bool>), p, _mNegative);
                _axisRaw = (Func<int, float>)Delegate.CreateDelegate(typeof(Func<int, float>), p, _mAxis);
            }
            return true;
        }

        /// Is the action held now, at `sign` (+1 a button or an axis's positive
        /// end, -1 its negative end)? A replay wins, a blocked start reads false,
        /// otherwise the real hands or the bridge's injected input. No allocation.
        public static bool Held(int action, int sign)
        {
            if (action < 0 || action >= _names.Length) return false;
            TasInput.Mode mode = TasInput.Current;
            if (mode == TasInput.Mode.Blocking) return false;
            string name = _names[action];
            if (mode == TasInput.Mode.Playing) return TasInput.ReplayHeld(name, sign);
            if (BindPlayer() && (sign > 0 ? _button(_ids[action]) : _negative(_ids[action]))) return true;
            if (!InputInject.State.Any) return false;
            int frame = Time.frameCount;
            float now = Time.realtimeSinceStartup, v;
            if (sign > 0 && InputInject.State.Held(name, frame, now)) return true;
            return InputInject.State.TryAxis(name, frame, now, out v) && (sign > 0 ? v > 0.1f : v < -0.1f);
        }

        /// An axis action by name ("Mouse X"), the same numbers as Unity's in play;
        /// a replay's value while one runs. NaN when the action is not there.
        public static float Axis(string name)
        {
            TasInput.Mode mode = TasInput.Current;
            if (mode == TasInput.Mode.Blocking) return 0f;
            if (mode == TasInput.Mode.Playing) return TasInput.ReplayAxis(name);
            if (Table == null) return float.NaN;
            int action = Table.IndexOf(name);
            if (action < 0 || !BindPlayer()) return float.NaN;
            return _axisRaw(_ids[action]);
        }
    }
}
