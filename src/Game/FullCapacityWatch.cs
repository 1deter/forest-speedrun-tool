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
    // Logs who made the game say "can't carry any more <item>".
    //
    // WHY: after a Full load the author saw "can't carry any more plane
    // axes" (2026-09-26), once; two Full loads of the same state through
    // the bridge did not show it. The message is HudGui.ToggleFullCapacityHud,
    // called only from PlayerInventory.AddItemNF when an add hits the
    // item's cap (IL). Candidate: RefreshHeld's put-away (StashEquipedWeapon
    // -> UnequipItemAtSlot -> AddItem back to the bag) racing the load's own
    // equip. Gotcha 25: ship the line that names the caller before a fix.
    //
    // WHAT: a read-only postfix; one line per item and second with the
    // call stack, never throws into the game.
    // ------------------------------------------------------------------
    public static class FullCapacityWatch
    {
        private static ManualLogSource _log;
        private static Harmony _harmony;
        private static int _lastId = -1;
        private static float _lastAt = -10f;

        public static void Install(ManualLogSource log, string harmonyId)
        {
            _log = log;
            try
            {
                Type hud = GameBridge.FindGameType("HudGui");
                MethodInfo m = hud != null ? hud.GetMethod("ToggleFullCapacityHud", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null) : null;
                if (m == null) { _log.LogWarning("FullCapacityWatch: HudGui.ToggleFullCapacityHud not found - \"can't carry any more\" is not logged."); return; }
                _harmony = new Harmony(harmonyId + ".fullcapacity");
                _harmony.Patch(m, postfix: new HarmonyMethod(typeof(FullCapacityWatch).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)));
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

        private static void Postfix(int itemId)
        {
            try
            {
                float now = Time.realtimeSinceStartup;
                if (itemId == _lastId && now - _lastAt < 1f) return;
                _lastId = itemId;
                _lastAt = now;

                StringBuilder sb = new StringBuilder("Inventory full: the game said \"can't carry any more\" of item ");
                sb.Append(itemId).Append(" - from ");
                StackFrame[] frames = new StackTrace(1, false).GetFrames();
                int shown = 0;
                for (int i = 0; frames != null && i < frames.Length && shown < 8; i++)
                {
                    MethodBase mb = frames[i].GetMethod();
                    if (mb == null || mb.DeclaringType == null) continue;
                    if (mb.DeclaringType.Namespace == "HarmonyLib") continue;
                    if (shown > 0) sb.Append(" <- ");
                    sb.Append(mb.DeclaringType.Name).Append('.').Append(mb.Name);
                    shown++;
                }
                _log.LogInfo(sb.ToString());
            }
            catch (Exception) { }
        }
    }
}
