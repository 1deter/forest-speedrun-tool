using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // A Full load from the title screen, the way the menu loads a slot.
    //
    // WHY: LoadSavedLevel called at the title screen loads the game scene
    // and hangs on LOADING (v0.24.211, bridge): Activation waits for
    // LocalPlayer.Rigidbody, and the player never appears. The menu's
    // loader (TitleSceneMain/Loading, LoadAsync.LoadLevelWithProgress)
    // first instantiates Resources "PreloadingPrefabs" and calls
    // LevelSerializer.InitPrefabList - the prefabs a save's objects (the
    // player among them) are built from - and only then Resume(), which
    // reads the slot file and calls LoadSavedLevel. In game those are
    // already there, so the in-game Full load works.
    //
    // WHAT: the title screen's own clicks (OnSinglePlayer, OnLoad,
    // OnSlotSelection), with Resume replaced while a load is pending: a
    // prefix calls LoadSavedLevel(the capture) instead of reading the
    // slot, and CanResume answers true (it checks the slot has a save).
    // Everything else is the menu's load. The load needs a slot (the
    // game's current one, Slot 1 when none was chosen yet); once in game
    // the slot is set back to none (0, as on a fresh launch; v0.24.215,
    // author): no slot was really loaded, and the game's save picker
    // (SaveSlotSelectionScreen.OnSlotSelection) skips its "overwrite?"
    // question for the loaded slot - with Slot 1 kept, a save there
    // overwrote it unasked. The save goes to the slot picked (bridge:
    // with 0, Slot 1 asks).
    // ------------------------------------------------------------------
    public static class TitleLoad
    {
        private const float Timeout = 120f;

        private static ManualLogSource _log;
        private static Harmony _harmony;
        private static MethodInfo _loadSavedLevel;
        private static string _pending;
        private static float _since;
        private static int _resumes;
        private static PropertyInfo _slotProp;
        private static MethodInfo _setSlot;

        public static void Install(ManualLogSource log, string harmonyId)
        {
            _log = log;
            try
            {
                BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                Type ls = GameBridge.FindGameType("LevelSerializer");
                MethodInfo resume = ls != null ? ls.GetMethod("Resume", stat, null, Type.EmptyTypes, null) : null;
                PropertyInfo canResume = ls != null ? ls.GetProperty("CanResume", stat) : null;
                _loadSavedLevel = ls != null ? ls.GetMethod("LoadSavedLevel", stat, null, new[] { typeof(string) }, null) : null;
                if (resume == null || canResume == null || canResume.GetGetMethod(true) == null || _loadSavedLevel == null)
                {
                    _log.LogWarning("TitleLoad: LevelSerializer.Resume / CanResume / LoadSavedLevel not found - no restores from the title screen.");
                    return;
                }
                _harmony = new Harmony(harmonyId + ".titleload");
                _harmony.Patch(resume, prefix: new HarmonyMethod(typeof(TitleLoad).GetMethod("ResumePrefix", stat)));
                _harmony.Patch(canResume.GetGetMethod(true), prefix: new HarmonyMethod(typeof(TitleLoad).GetMethod("CanResumePrefix", stat)));
            }
            catch (Exception ex)
            {
                _harmony = null;
                _log.LogWarning("TitleLoad: " + ex.Message);
            }
        }

        public static void Uninstall()
        {
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception) { }
        }

        private static bool Active
        {
            get
            {
                if (_pending == null) return false;
                if (Time.realtimeSinceStartup - _since <= Timeout) return true;
                _log.LogWarning("TitleLoad: the pending title-screen load timed out - Resume reads the slot again.");
                _pending = null;
                return false;
            }
        }

        /// Starts the menu's load with `data` in place of the slot's save.
        /// The caller has set Continue, the game mode and the difficulty.
        /// Null when started, else why not.
        public static string Start(string data)
        {
            if (_harmony == null) return "the title-screen load is not available (see the log)";
            BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Type t = GameBridge.FindGameType("TitleScreen");
            FieldInfo instance = t != null ? t.GetField("Instance", stat) : null;
            object title = instance != null ? instance.GetValue(null) : null;
            MethodInfo single = t != null ? t.GetMethod("OnSinglePlayer", inst, null, Type.EmptyTypes, null) : null;
            MethodInfo slotSel = t != null ? t.GetMethod("OnSlotSelection", inst, null, new[] { typeof(int) }, null) : null;
            if (title == null || single == null || slotSel == null) return "the title screen's load buttons were not found";

            Type setup = GameBridge.FindGameType("TheForest.Utils.GameSetup");
            _slotProp = setup != null ? setup.GetProperty("Slot", stat) : null;
            // The property's own setter: SetSlot clamps to 1-5, and "none"
            // is 0 - what a fresh launch has (bridge, 2026-10-02).
            _setSlot = _slotProp != null ? _slotProp.GetSetMethod(true) : null;
            int slot = 0;
            try { if (_slotProp != null) slot = Convert.ToInt32(_slotProp.GetValue(null, null)); }
            catch (Exception) { }
            if (slot <= 0) slot = 1;

            _pending = data;
            _since = Time.realtimeSinceStartup;
            _resumes = 0;
            try
            {
                // OnSinglePlayer: the player mode only (IL); Continue is set
                // by the caller, as OnLoad would.
                single.Invoke(title, null);
                slotSel.Invoke(title, new object[] { slot });
                _log.LogInfo("TitleLoad: the menu's load started (slot " + slot + "), the capture in place of the slot's save.");
                return null;
            }
            catch (Exception ex)
            {
                _pending = null;
                return "the title screen refused: " + (ex.InnerException ?? ex).Message;
            }
        }

        /// The load finished or failed: Resume reads slots again.
        public static void Clear()
        {
            if (_pending == null) return;
            _pending = null;
            string slot = "slot left as it was (GameSetup.Slot's setter not found)";
            try
            {
                if (_setSlot != null)
                {
                    _setSlot.Invoke(null, new[] { Enum.ToObject(_slotProp.PropertyType, 0) });
                    slot = "slot set to none - saving asks before overwriting any slot";
                }
            }
            catch (Exception ex) { slot = "slot not reset: " + (ex.InnerException ?? ex).Message; }
            _log.LogInfo("TitleLoad: done (" + _resumes + " Resume call(s) replaced; " + slot + ").");
        }

        private static bool ResumePrefix()
        {
            if (!Active) return true;
            _resumes++;
            try { _loadSavedLevel.Invoke(null, new object[] { _pending }); }
            catch (Exception ex) { _log.LogWarning("TitleLoad: LoadSavedLevel threw: " + (ex.InnerException ?? ex).Message); }
            return false;
        }

        private static bool CanResumePrefix(ref bool __result)
        {
            if (!Active) return true;
            __result = true;
            return false;
        }
    }
}
