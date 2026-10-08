using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The survival book's to-do list after a Quick load (Quick load audit,
    // 2026-10-01).
    //
    // WHY (IL + bridge): SurvivalBookTodo's tasks (_son, _camp, _cave1 ...)
    // are saved whole, so an in-place LoadNow puts NEW task objects in its
    // fields. The game prepares tasks only in DelayedAwake - their page
    // entries (GOs), the status callback and the conditions' event
    // subscriptions - and only the first time (`_initialized`). Bridge,
    // after a Full load: `_son.GOs` set, `OnStatusChange` a delegate; after
    // one Quick load both null. So the list stopped updating until the
    // next Full load, and the replaced tasks stayed subscribed, unseen.
    //
    // WHAT: before LoadNow the live task objects are kept; after it, every
    // replaced one is Clear()ed as the game's OnDestroy does (unsubscribed),
    // `_initialized` goes back to false and the game's own DelayedAwake runs
    // again - for a loaded game it only prepares the tasks and re-enables
    // the component (its new-game messages wait on GameSetup.Init = new).
    // ------------------------------------------------------------------
    public static class TodoListKeeper
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static ManualLogSource _log;
        private static bool _resolved;
        private static Type _host;
        private static FieldInfo _initialized;
        private static MethodInfo _delayedAwake, _clear;
        private static readonly List<FieldInfo> Tasks = new List<FieldInfo>();

        private static MonoBehaviour _before;
        private static readonly List<object> Old = new List<object>();

        private static bool Resolve()
        {
            if (_resolved) return _delayedAwake != null;
            _resolved = true;
            _host = GameBridge.FindGameType("TheForest.Player.SurvivalBookTodo");
            Type condition = GameBridge.FindGameType("TheForest.TaskSystem.ACondition");
            if (_host == null || condition == null) return false;
            _initialized = _host.GetField("_initialized", Flags);
            _clear = condition.GetMethod("Clear", Flags, null, Type.EmptyTypes, null);
            MethodInfo awake = _host.GetMethod("DelayedAwake", Flags, null, Type.EmptyTypes, null);
            foreach (FieldInfo f in _host.GetFields(Flags))
                if (condition.IsAssignableFrom(f.FieldType)) Tasks.Add(f);
            if (_initialized == null || _clear == null || awake == null || Tasks.Count == 0) return false;
            _delayedAwake = awake;
            return true;
        }

        /// Before an in-place LoadNow: the live tasks.
        public static void BeforeRestore(ManualLogSource log)
        {
            _log = log;
            _before = null;
            Old.Clear();
            try
            {
                if (!Resolve()) return;
                _before = SceneCache.One(_host) as MonoBehaviour;   // a scene walk, kept (T-0148)
                if (_before == null) return;
                for (int i = 0; i < Tasks.Count; i++) Old.Add(Tasks[i].GetValue(_before));
            }
            catch (Exception ex) { if (_log != null) _log.LogWarning("TodoListKeeper: " + ex.Message); _before = null; }
        }

        /// After it: replaced tasks cleared, the new ones prepared. A note
        /// for the restore line ("" = nothing to do).
        public static string AfterRestore()
        {
            MonoBehaviour host = _before;
            _before = null;
            if (host == null || Old.Count != Tasks.Count) return "";

            int replaced = 0;
            for (int i = 0; i < Tasks.Count; i++)
            {
                object now = Tasks[i].GetValue(host);
                object was = Old[i];
                if (ReferenceEquals(now, was)) continue;
                replaced++;
                if (was != null)
                {
                    try { _clear.Invoke(was, null); } catch (Exception) { }
                }
            }
            Old.Clear();
            if (replaced == 0) return "";

            _initialized.SetValue(host, false);
            host.StartCoroutine((IEnumerator)_delayedAwake.Invoke(host, null));
            return "to-do list: " + replaced + " task(s) set up again";
        }
    }
}
