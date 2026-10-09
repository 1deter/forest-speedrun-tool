using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // A savestate capture leaves the loaded save slot alone (T-0214: the
    // e2e smoke found Slot 1's `info` rewritten).
    //
    // WHY (IL + bridge, 2026-10-10): LevelSerializer.SerializeLevel sends
    // OnSerializing to every object, and GameStats.OnSerializing writes
    // its Stats (the load screen's day / kills / trees, read back only by
    // LoadSaveSlotInfo.LoadStats and the debug console) to
    // GetLocalSlotPath() + "info" and to Steam Cloud. One bridge `capture`
    // rewrote Slot1/info and nothing else in the slot.
    //
    // WHAT: a prefix skips GameStats.OnSerializing while our capture is
    // serializing; the game's own saves still write it.
    // ------------------------------------------------------------------
    public static class SlotInfoGuard
    {
        private static ManualLogSource _log;
        private static Harmony _harmony;

        /// True while SavestateBridge's SerializeLevel runs.
        public static bool Capturing;

        public static void Install(ManualLogSource log, string harmonyId)
        {
            _log = log;
            try
            {
                Type stats = GameBridge.FindGameType("TheForest.Utils.GameStats");
                MethodInfo target = stats != null
                    ? stats.GetMethod("OnSerializing", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
                    : null;
                if (target == null) { _log.LogWarning("SlotInfoGuard: GameStats.OnSerializing not found - a capture rewrites the slot's info file."); return; }
                _harmony = new Harmony(harmonyId + ".slotinfo");
                _harmony.Patch(target, prefix: new HarmonyMethod(typeof(SlotInfoGuard).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (Exception ex) { _log.LogWarning("SlotInfoGuard: " + ex.Message); }
        }

        public static void Uninstall()
        {
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception) { }
        }

        private static bool Prefix()
        {
            return !Capturing;
        }
    }
}
