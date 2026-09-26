using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // "Can't carry any more <item>": logs who said it, and hides the one
    // our restores cause.
    //
    // WHY: after restores the author (Full load, plane axe) and runner
    // Cheesecake (Quick loads, plane axe and lighter, v0.24.97 report)
    // saw "can't carry any more ...". The message is
    // HudGui.ToggleFullCapacityHud, called only from
    // PlayerInventory.AddItemNF when an add hits the item's cap (IL). The
    // logged caller (v0.24.98 line) is PlayerInventory.OnDeserialized's
    // coroutine: 1.5 s after a load it calls Equip(id, true) for each item
    // saved in the hands and falls back to AddItem(id) when Equip refuses -
    // and Equip(id, true) refuses when the hands are locked (a put-away
    // still running, an action) or the item is already held (IL; bridge:
    // Equip 80 on a held axe returns false). A load from the title starts
    // with empty hands, so the game never gets there; a restore starts with
    // the runner's hands. The add at the cap changes nothing (AddItemNF:
    // the HUD call, then return false) and the item ends in the hands
    // either way (the game's equip or ReEquip / RefreshHeld).
    //
    // WHAT: a prefix. During a restore and 8 s after it, a message from
    // that coroutine is skipped with one log line; every other one is
    // shown and logged with its call stack, as before.
    // ------------------------------------------------------------------
    public static class FullCapacityWatch
    {
        private const float AfterRestore = 8f;      // the game's step is 1.5 s after the load, then a frame
        private const float RestoreTimeout = 120f;  // a restore that never reported back

        private static ManualLogSource _log;
        private static Harmony _harmony;
        private static int _lastId = -1;
        private static float _lastAt = -10f;
        private static float _restoreStarted = -1000f;
        private static float _restoreEnded = -1000f;
        private static bool _restoring;

        public static void Install(ManualLogSource log, string harmonyId)
        {
            _log = log;
            try
            {
                Type hud = GameBridge.FindGameType("HudGui");
                MethodInfo m = hud != null ? hud.GetMethod("ToggleFullCapacityHud", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null) : null;
                if (m == null) { _log.LogWarning("FullCapacityWatch: HudGui.ToggleFullCapacityHud not found - \"can't carry any more\" is not logged."); return; }
                _harmony = new Harmony(harmonyId + ".fullcapacity");
                _harmony.Patch(m, prefix: new HarmonyMethod(typeof(FullCapacityWatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (Exception ex)
            {
                _log.LogWarning("FullCapacityWatch: " + ex.Message);
            }
        }

        public static void Uninstall()
        {
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception) { }
        }

        /// A savestate restore (Quick or Full load) begins.
        public static void RestoreStarted()
        {
            _restoring = true;
            _restoreStarted = Time.realtimeSinceStartup;
        }

        /// It has finished (or failed); the window stays open a few seconds.
        public static void RestoreEnded()
        {
            _restoring = false;
            _restoreEnded = Time.realtimeSinceStartup;
        }

        private static bool InRestoreWindow(float now)
        {
            if (_restoring && now - _restoreStarted < RestoreTimeout) return true;
            return now - _restoreEnded < AfterRestore;
        }

        private static bool Prefix(int itemId)
        {
            try
            {
                float now = Time.realtimeSinceStartup;
                bool repeat = itemId == _lastId && now - _lastAt < 1f;
                _lastId = itemId;
                _lastAt = now;

                StackFrame[] frames = new StackTrace(1, false).GetFrames();
                bool fromLoadEquip = false;
                StringBuilder sb = new StringBuilder();
                int shown = 0;
                for (int i = 0; frames != null && i < frames.Length; i++)
                {
                    MethodBase mb = frames[i].GetMethod();
                    Type t = mb != null ? mb.DeclaringType : null;
                    if (t == null || t.Namespace == "HarmonyLib") continue;
                    if (t.Name.StartsWith("<OnDeserialized>") && t.DeclaringType != null && t.DeclaringType.Name == "PlayerInventory")
                        fromLoadEquip = true;
                    if (shown < 8)
                    {
                        if (shown > 0) sb.Append(" <- ");
                        sb.Append(t.Name).Append('.').Append(mb.Name);
                        shown++;
                    }
                }

                if (fromLoadEquip && InRestoreWindow(now))
                {
                    if (!repeat)
                        _log.LogInfo("Inventory full message hidden: item " + itemId + " - the game's re-equip after a restore " +
                                     "fell back to adding it, and it was already held or in the bag (nothing changed).");
                    return false;
                }
                if (!repeat)
                    _log.LogInfo("Inventory full: the game said \"can't carry any more\" of item " + itemId + " - from " + sb);
            }
            catch (Exception) { }
            return true;
        }
    }
}
