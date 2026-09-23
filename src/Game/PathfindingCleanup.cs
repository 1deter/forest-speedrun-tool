using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The load leak's first real culprit: the old world's pathfinder never
    // cleans up on a reload.
    //
    // AstarPath.OnDestroy (IL) begins
    //     if (AstarPath.active != this) return;
    // and everything after it is the cleanup: BlockUntilPathQueueBlocked,
    // FlushWorkItemsInternal, TerminateReceivers, DisableMultithreading,
    // PathProcessor.JoinThreads, ReturnPaths, AstarData.OnDestroy (the
    // graphs), then eleven static callbacks and `active` set to null.
    //
    // A reload of the game scene over itself (a quick-load, a load restore)
    // loads the new world before the old one is destroyed, so the NEW
    // AstarPath is already `active` (Awake -> SetUpReferences) when the old
    // one's OnDestroy runs: it returns at once, its path threads keep
    // running and its whole navigation graph stays alive. Through the title
    // screen there is no new instance, the cleanup runs - which is why a
    // menu trip gave the memory back (runner logs, 2026-09-23). The memory
    // census saw constant statics and constant Unity objects while the
    // heap grew ~120 MB a load: a thread's stack is a root no static walk
    // reaches.
    //
    // Fix: when the dying instance is not the active one, make it active for
    // the length of its own OnDestroy (GraphNode.Destroy also returns node
    // indices through AstarPath.active, so it must be the old one), then
    // put back `active` and every static callback the cleanup nulled - the
    // new world registered those already. A finalizer restores them even if
    // the cleanup throws, and passes the exception on unchanged.
    // ------------------------------------------------------------------
    public sealed class PathfindingCleanup
    {
        /// Set by the owning module from its config switch.
        public static bool Enabled = true;

        public static int Cleaned { get; private set; }

        private static ManualLogSource _log;
        private static FieldInfo _active;
        private static FieldInfo[] _saved;   // `active` and the static callbacks

        private Harmony _harmony;

        public string Status { get; private set; }

        public PathfindingCleanup(ManualLogSource log)
        {
            _log = log;
            Status = "not installed";
        }

        public void Install(string harmonyId)
        {
            try
            {
                Type astar = GameBridge.FindGameType("AstarPath");
                MethodInfo onDestroy = astar != null
                    ? astar.GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
                    : null;
                _active = astar != null ? astar.GetField("active", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) : null;
                if (onDestroy == null || _active == null)
                {
                    Status = "AstarPath.OnDestroy / active not found - not installed";
                    _log.LogWarning("PathfindingCleanup: " + Status + ".");
                    return;
                }

                // Every static the cleanup writes: `active` and the delegates.
                List<FieldInfo> saved = new List<FieldInfo>();
                FieldInfo[] statics = astar.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (int i = 0; i < statics.Length; i++)
                {
                    FieldInfo f = statics[i];
                    if (f.IsLiteral || f.IsInitOnly || f.Name.IndexOf('<') >= 0) continue;
                    if (f == _active || typeof(Delegate).IsAssignableFrom(f.FieldType)) saved.Add(f);
                }
                _saved = saved.ToArray();

                _harmony = new Harmony(harmonyId + ".pathfinding");
                _harmony.Patch(onDestroy,
                               prefix: new HarmonyMethod(typeof(PathfindingCleanup).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)),
                               finalizer: new HarmonyMethod(typeof(PathfindingCleanup).GetMethod("Finalizer", BindingFlags.Static | BindingFlags.NonPublic)));

                Status = "installed (" + _saved.Length + " statics kept)";
                _log.LogInfo("PathfindingCleanup: " + Status + ".");
            }
            catch (Exception ex)
            {
                Status = "failed: " + ex.Message;
                _log.LogWarning("PathfindingCleanup: install failed: " + ex);
            }
        }

        public void Uninstall()
        {
            if (_harmony != null) _harmony.UnpatchSelf();
        }

        // ------------------------------------------------------------------
        private static void Prefix(object __instance, out object[] __state)
        {
            __state = null;
            try
            {
                if (!Enabled || _saved == null) return;

                object active = _active.GetValue(null);
                if (ReferenceEquals(active, __instance)) return;   // the game's own path: nothing to do

                __state = new object[_saved.Length];
                for (int i = 0; i < _saved.Length; i++) __state[i] = _saved[i].GetValue(null);

                _active.SetValue(null, __instance);
                Cleaned++;
                _log.LogInfo("Pathfinding: the previous world's AstarPath was destroyed while the new one was active - " +
                             "running the cleanup the game skips (" + Cleaned + " this session).");
            }
            catch (Exception ex)
            {
                // Never break the game's own OnDestroy: undo and step aside.
                if (__state != null) Restore(__state);
                __state = null;
                _log.LogWarning("PathfindingCleanup: prefix failed: " + ex.Message);
            }
        }

        private static Exception Finalizer(Exception __exception, object[] __state)
        {
            if (__state != null) Restore(__state);
            if (__exception != null && __state != null)
                _log.LogWarning("PathfindingCleanup: the old pathfinder's cleanup threw: " + __exception.Message);
            return __exception;
        }

        private static void Restore(object[] state)
        {
            for (int i = 0; i < _saved.Length && i < state.Length; i++)
            {
                try { _saved[i].SetValue(null, state[i]); }
                catch (Exception) { }
            }
        }
    }
}
