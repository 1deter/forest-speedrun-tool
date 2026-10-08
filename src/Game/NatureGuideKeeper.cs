using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The nature guide's ticks after a Quick load (author, 2026-10-01: a
    // Quick load did not restore the found entries).
    //
    // WHY (IL + bridge, v0.24.187): TickOffSystem keeps the live state in
    // each Entry._ticked and saves it as `_tickedEntries` (ids, written in
    // OnSerializing). An in-place LoadNow writes `_tickedEntries` back
    // (bridge: nulled, restored, read back), then OnDeserialized calls
    // Awake -> DelayedAwake, which applies the array only on the first run
    // (`_initialized`). So an entry ticked after the capture stayed ticked;
    // one ticked at capture and not live (another save's state) stayed off.
    // A Full load builds a fresh TickOffSystem and is right.
    //
    // A tick does what the entry's handler does: `_ticked`, the page's tick
    // mark on, then Entry.Clear - which unsubscribes and NULLS `_tickGo`.
    // So the mark of an entry ticked this session is only reachable through
    // a prefix on Clear that remembers it.
    //
    // WHAT: after a Quick load, each entry is set to what `_tickedEntries`
    // says: un-ticked = `_ticked` off, its mark hidden, `_tickGo` back,
    // Init() (subscribed again, so finding it ticks it); ticked = the
    // handler's steps without its TickedOffEntry message.
    // ------------------------------------------------------------------
    public static class NatureGuideKeeper
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static ManualLogSource _log;
        private static Harmony _harmony;
        // Entry -> its tick mark, as Clear found it. Keyed by reference.
        private static readonly Dictionary<object, GameObject> Marks = new Dictionary<object, GameObject>();

        private static Type _host;
        private static FieldInfo _entries, _tickedIds, _id, _ticked, _tickGo;
        private static MethodInfo _init, _clear;

        public static void Install(ManualLogSource log, string harmonyId)
        {
            _log = log;
            try
            {
                if (!Resolve()) { _log.LogWarning("NatureGuideKeeper: TickOffSystem's fields not found - a Quick load keeps the live nature guide ticks."); return; }
                _harmony = new Harmony(harmonyId + ".natureguide");
                _harmony.Patch(_clear, prefix: new HarmonyMethod(typeof(NatureGuideKeeper).GetMethod("ClearPrefix", BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (Exception ex) { _log.LogWarning("NatureGuideKeeper: " + ex.Message); }
        }

        public static void Uninstall()
        {
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception) { }
        }

        private static bool Resolve()
        {
            if (_host != null) return _entries != null;
            _host = GameBridge.FindGameType("TheForest.Player.TickOffSystem");
            if (_host == null) return false;
            _entries = _host.GetField("_entries", Flags);
            _tickedIds = _host.GetField("_tickedEntries", Flags);
            Type entry = _entries != null ? _entries.FieldType.GetElementType() : null;
            if (entry != null)
            {
                _id = entry.GetField("_id", Flags);
                _ticked = entry.GetField("_ticked", Flags);
                _tickGo = entry.GetField("_tickGo", Flags);
                _init = entry.GetMethod("Init", Flags, null, Type.EmptyTypes, null);
                _clear = entry.GetMethod("Clear", Flags, null, Type.EmptyTypes, null);
            }
            if (entry == null || _tickedIds == null || _id == null || _ticked == null || _tickGo == null || _init == null || _clear == null)
            {
                _entries = null;
                return false;
            }
            return true;
        }

        private static void ClearPrefix(object __instance)
        {
            try
            {
                GameObject go = _tickGo.GetValue(__instance) as GameObject;
                if (go != null) Marks[__instance] = go;
            }
            catch (Exception) { }
        }

        /// After a Quick load's LoadNow: the entries as the save says.
        /// Returns a note for the restore line ("" = nothing changed).
        public static string Restore()
        {
            if (_entries == null) return "";
            Component host = FindHost();
            if (host == null) return "";

            IList entries = _entries.GetValue(host) as IList;
            int[] saved = _tickedIds.GetValue(host) as int[];
            if (entries == null) return "";

            int on = 0, off = 0, noMark = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                object e = entries[i];
                if (e == null) continue;
                bool should = saved != null && Array.IndexOf(saved, (int)_id.GetValue(e)) >= 0;
                bool now = (bool)_ticked.GetValue(e);
                if (should == now) continue;

                GameObject mark = _tickGo.GetValue(e) as GameObject;
                if (mark == null) { GameObject m; if (Marks.TryGetValue(e, out m)) mark = m; }

                if (!should)
                {
                    _ticked.SetValue(e, false);
                    if (mark != null) { mark.SetActive(false); _tickGo.SetValue(e, mark); }
                    else noMark++;
                    _init.Invoke(e, null);   // finding it ticks it again
                    off++;
                }
                else
                {
                    _ticked.SetValue(e, true);
                    if (mark != null) mark.SetActive(true);
                    _clear.Invoke(e, null);  // as the game's handler, without its message
                    on++;
                }
            }
            if (on == 0 && off == 0) return "";
            return "nature guide: " + (off > 0 ? off + " entr" + (off == 1 ? "y" : "ies") + " found since un-ticked" : "") +
                   (off > 0 && on > 0 ? ", " : "") + (on > 0 ? on + " ticked as saved" : "") +
                   (noMark > 0 ? " (" + noMark + " tick mark(s) not found - still drawn in the book)" : "");
        }

        private static Component FindHost()
        {
            // player/ControllerObjects/SpecialItems/TickOff, active in play.
            // Once per restore - a scene walk, 20-25 ms: kept (T-0148).
            try { return SceneCache.One(_host); }
            catch (Exception) { return null; }
        }
    }
}
