using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using BepInEx.Logging;
using ForestOverlay.Data;
using HarmonyLib;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Who performed an input (T-0361, game-notes "Who performed an input").
    //
    // The game acts on an action where a script READS it through
    // TheForest.Utils.Input; on a press the script is on the stack. While the
    // input overlay shows game input, postfixes on Input.GetButtonDown /
    // GetButtonUp / GetButton (after InputInject's, so the bridge's and a
    // replay's values count; InputInject's transpiler routes the delayed /
    // tap reads through the same three) walk the stack once per press, for the
    // actions on the shown tiles only, and publish the reader's word
    // (Data/ActionFunctions). No walk on a frame without a press; a held
    // button is walked only in the 3 frames after it went active.
    //
    // The player's control FSM (pmControl) tells a light attack, a heavy one
    // and a block apart (the attack reads sit in generic scripts).
    // ------------------------------------------------------------------
    public static class ActionReaders
    {
        private const int HeldWindow = 3, MaxWalksPerFrame = 8, MaxPerSlot = 3, MaxFrames = 14;
        private const string HarmonyTail = ".actionreaders";

        private static Harmony _harmony;
        private static ManualLogSource _log;
        private static string _guid;
        private static string[] _names = new string[0];
        private static int[] _activeSince = new int[0];
        private static string[] _label = new string[0];
        private static int[] _labelFrame = new int[0];
        private static int[] _walkFrame = new int[0], _walkCount = new int[0];
        private static int _walksFrame, _walks;
        private static readonly HashSet<string> _unnamed = new HashSet<string>();

        /// Why the readers are not installed ("" when they are or nothing is watched).
        public static string Status = "";

        /// The actions on the shown tiles (by Rewired name), or none: installs the
        /// postfixes for the first and removes them when the list is empty.
        public static void Watch(ManualLogSource log, string guid, string[] names)
        {
            _log = log;
            _guid = guid;
            _names = names ?? new string[0];
            int n = _names.Length;
            _activeSince = new int[n];
            _label = new string[n];
            _labelFrame = new int[n];
            _walkFrame = new int[n];
            _walkCount = new int[n];
            for (int i = 0; i < n; i++) _activeSince[i] = -100;
            if (n > 0) Install();
            else Uninstall();
        }

        public static int SlotOf(string name)
        {
            for (int i = 0; i < _names.Length; i++)
                if (string.Equals(_names[i], name, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// Each Tick, per watched action: is it held now? (a rise opens the walk window.)
        public static void Report(int slot, bool active)
        {
            if (slot < 0 || slot >= _activeSince.Length) return;
            if (!active) { _activeSince[slot] = -100; return; }
            if (_activeSince[slot] < 0) _activeSince[slot] = Time.frameCount;
        }

        /// The last specific word a reader gave for the action, and the frame it was seen.
        public static string LabelOf(int slot, out int frame)
        {
            frame = -1;
            if (slot < 0 || slot >= _label.Length) return null;
            frame = _labelFrame[slot];
            return _label[slot];
        }

        // --- install --------------------------------------------------------------------

        private static void Install()
        {
            if (_harmony != null) return;
            try
            {
                string err = InputInject.Install(_log, _guid);   // the reads funnel through its three patched statics
                Type input = GameBridge.FindGameType("TheForest.Utils.Input");
                if (input == null) { Status = "TheForest.Utils.Input not found"; return; }
                Harmony h = new Harmony(_guid + HarmonyTail);
                int n = 0;
                n += Post(h, input, "GetButtonDown", "DownPostfix");
                n += Post(h, input, "GetButtonUp", "UpPostfix");
                n += Post(h, input, "GetButton", "HeldPostfix");
                _harmony = h;
                Status = n == 3 ? "" : "only " + n + " of 3 Input reads found";
                if (err != null) Status = "the input hook failed (" + err + ")";
                _log.LogInfo("ActionReaders: " + (Status.Length == 0 ? "watching " + _names.Length + " action(s) for the function performed." : Status));
            }
            catch (Exception ex)
            {
                Status = "could not patch: " + ex.Message;
                _log.LogWarning("ActionReaders: " + ex);
            }
        }

        private static void Uninstall()
        {
            if (_harmony == null) return;
            try { _harmony.UnpatchSelf(); }
            catch (Exception) { }
            _harmony = null;
        }

        private static int Post(Harmony h, Type input, string name, string postfix)
        {
            MethodInfo m = input.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
            if (m == null) return 0;
            HarmonyMethod hm = new HarmonyMethod(typeof(ActionReaders).GetMethod(postfix, BindingFlags.Static | BindingFlags.NonPublic));
            hm.priority = Priority.Last;
            hm.after = new[] { _guid + ".inputinject" };
            h.Patch(m, postfix: hm);
            return 1;
        }

        // --- the postfixes (parameter names follow the game's: GetButton*(string button)) ------

        private static void DownPostfix(string button, bool __result) { if (__result) Read(button, false); }
        private static void UpPostfix(string button, bool __result) { if (__result) Read(button, false); }
        private static void HeldPostfix(string button, bool __result) { if (__result) Read(button, true); }

        private static void Read(string name, bool held)
        {
            try
            {
                int slot = SlotOf(name);
                if (slot < 0) return;
                int frame = Time.frameCount;
                if (held && frame - _activeSince[slot] > HeldWindow) return;   // not a press: a held button is not walked
                if (_walksFrame != frame) { _walksFrame = frame; _walks = 0; }
                if (_walks >= MaxWalksPerFrame) return;
                if (_walkFrame[slot] != frame) { _walkFrame[slot] = frame; _walkCount[slot] = 0; }
                if (_walkCount[slot] >= MaxPerSlot) return;
                _walkCount[slot]++;
                _walks++;
                Walk(slot, frame);
            }
            catch (Exception) { }
        }

        private static void Walk(int slot, int frame)
        {
            Assembly ours = typeof(ActionReaders).Assembly;
            for (int i = 1; i < MaxFrames; i++)
            {
                StackFrame f = new StackFrame(i, false);
                MethodBase m = f.GetMethod();
                if (m == null) return;
                Type t = m.DeclaringType;
                while (t != null && t.DeclaringType != null && t.Name.Length > 0 && t.Name[0] == '<') t = t.DeclaringType;   // an iterator / closure: its owner
                if (t == null || t.Assembly == ours) continue;
                string full = t.FullName ?? t.Name;
                if (full == "TheForest.Utils.Input" || full.StartsWith("HarmonyLib", StringComparison.Ordinal)) continue;
                if (full.StartsWith("UnityEngine", StringComparison.Ordinal) || full.StartsWith("System", StringComparison.Ordinal)) return;   // Input.LateUpdate's own reads
                string word;
                if (ActionFunctions.Lookup(full, m.Name, _names[slot], out word))
                {
                    if (word != null) { _label[slot] = word; _labelFrame[slot] = frame; }
                }
                else
                {
                    string key = full + "::" + m.Name + " for " + _names[slot];
                    if (_unnamed.Add(key)) _log.LogInfo("ActionReaders: unnamed reader " + key);
                }
                return;
            }
        }

        // --- the player's control FSM -------------------------------------------------------

        private static FieldInfo _scriptSetup, _pmControl;
        private static MethodInfo _stateGetter;
        private static object _fsm;
        private static Func<string> _state;
        private static bool _stateBroken;

        /// pmControl.ActiveStateName of the local player ("" when unreadable). No allocation.
        public static string ControlState()
        {
            if (_stateBroken) return "";
            try
            {
                if (_scriptSetup == null)
                {
                    Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
                    _scriptSetup = local != null ? local.GetField("ScriptSetup", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) : null;
                    if (_scriptSetup == null) { _stateBroken = true; return ""; }
                }
                object setup = _scriptSetup.GetValue(null);
                if (setup == null) return "";
                if (_pmControl == null)
                {
                    _pmControl = setup.GetType().GetField("pmControl", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (_pmControl == null) { _stateBroken = true; return ""; }
                }
                object fsm = _pmControl.GetValue(setup);
                if (fsm == null) return "";
                if (!ReferenceEquals(fsm, _fsm))
                {
                    if (_stateGetter == null)
                    {
                        PropertyInfo p = fsm.GetType().GetProperty("ActiveStateName", BindingFlags.Instance | BindingFlags.Public);
                        _stateGetter = p != null ? p.GetGetMethod() : null;
                        if (_stateGetter == null) { _stateBroken = true; return ""; }
                    }
                    _fsm = fsm;
                    _state = (Func<string>)Delegate.CreateDelegate(typeof(Func<string>), fsm, _stateGetter);
                }
                return _state() ?? "";
            }
            catch (Exception) { return ""; }
        }
    }
}
