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
    // Pathfinding graph updates: who queues them, how long they run, and
    // how long the old world's AstarPath holds up its teardown.
    //
    // WHY: a death reload out of the Megan fight froze 26-51 s. The main
    // thread waits in AstarPath.OnDestroy, which flushes the pathfinding
    // work in flight - and a Quick load starts graph updates that run
    // 16-35 s (game-notes "Pathfinding (A*) and the reload freeze"). A
    // Quick load re-creates the Astar object too (a new handle each
    // restore), so a repeat Quick load in that window waits the same way
    // (a 31.5 s restore, bridge, 2026-09-27). Which updates they are is
    // not known yet; the queue shows one at a time.
    //
    // WHAT: read-only. A prefix on every AstarPath.UpdateGraphs overload
    // logs the bounds and the caller; while updates are pending the tick
    // watches GraphUpdateProcessor.IsAnyGraphUpdateInProgress and logs how
    // long the burst ran; AstarPath.Awake / OnDestroy log, the latter with
    // how long it blocked.
    //
    // FOUND (v0.24.141 lines): the long update is ONE graph update of
    // 1533 x 81 x 1407 m from sceneTracker.doStructureBoundsNavRemove.
    // A Quick load re-creates the Astar object and every structure; the
    // structures' gridObjectBlocker.Start sees Scene.FinishGameLoad and
    // registers without doingOnGameStartCheck, so each takes the
    // one-at-a-time route, which merges every structure waiting at that
    // moment into one box - across the map. A load takes the other route
    // (loadGameNavSetup: doingOnGameStartCheck = true ->
    // doGlobalStructureBoundsNavRemove), which groups structures within
    // 100 m and cuts each group (0.2 s for Slot 2 at a title load).
    //
    // FIX: a prefix on gridObjectBlocker.Start. During an in-place restore
    // and 2 s after, a blocker takes Start's load branch (findRootTr in
    // 0.1 s + loadGameNavSetup) - the cut a Full load makes.
    // ------------------------------------------------------------------
    public static class PathfindingWatch
    {
        private const int MaxQueuedLinesPerBurst = 40;

        private static ManualLogSource _log;
        private static Harmony _harmony;
        private static FieldInfo _active;           // static AstarPath.active
        private static FieldInfo _graphUpdates;     // AstarPath.graphUpdates
        private static PropertyInfo _inProgress;    // GraphUpdateProcessor.IsAnyGraphUpdateInProgress
        private static FieldInfo _guoBounds;        // GraphUpdateObject.bounds
        private static FieldInfo _guoPhysics;       // GraphUpdateObject.updatePhysics

        [ThreadStatic] private static int _depth;
        private static bool _watching;
        private static bool _seenRunning;
        private static float _burstStart;
        private static int _burstQueued;
        private static int _burstUnlogged;
        private const float AfterRestore = 2f;
        private const float RestoreTimeout = 120f;
        private static bool _restoring;
        private static float _restoreStarted = -1000f;
        private static float _restoreEnded = -1000f;
        private static int _loadRouted;
        private static MethodInfo _loadGameNavSetup;
        private static float _destroyStart;
        private static bool _destroyWasBusy;

        public static void Install(ManualLogSource log, string harmonyId)
        {
            _log = log;
            try
            {
                Type astar = GameBridge.FindGameType("AstarPath");
                if (astar == null) { _log.LogWarning("PathfindingWatch: AstarPath not found - graph updates are not logged."); return; }
                const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                _active = astar.GetField("active", all);
                _graphUpdates = astar.GetField("graphUpdates", all);
                if (_graphUpdates != null)
                    _inProgress = _graphUpdates.FieldType.GetProperty("IsAnyGraphUpdateInProgress", all);
                Type guo = GameBridge.FindGameType("Pathfinding.GraphUpdateObject");
                if (guo != null)
                {
                    _guoBounds = guo.GetField("bounds", all);
                    _guoPhysics = guo.GetField("updatePhysics", all);
                }

                _harmony = new Harmony(harmonyId + ".pathfinding");
                HarmonyMethod queued = new HarmonyMethod(typeof(PathfindingWatch).GetMethod("QueuedPrefix", BindingFlags.Static | BindingFlags.NonPublic));
                HarmonyMethod queuedEnd = new HarmonyMethod(typeof(PathfindingWatch).GetMethod("QueuedFinalizer", BindingFlags.Static | BindingFlags.NonPublic));
                int patched = 0;
                foreach (MethodInfo m in astar.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.Name != "UpdateGraphs") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length == 0) continue;
                    Type p0 = ps[0].ParameterType;
                    if (p0 != typeof(Bounds) && (guo == null || p0 != guo)) continue;
                    _harmony.Patch(m, prefix: queued, finalizer: queuedEnd);
                    patched++;
                }
                Type blocker = GameBridge.FindGameType("gridObjectBlocker");
                MethodInfo start = blocker != null ? blocker.GetMethod("Start", all, null, Type.EmptyTypes, null) : null;
                _loadGameNavSetup = blocker != null ? blocker.GetMethod("loadGameNavSetup", all, null, Type.EmptyTypes, null) : null;
                if (start != null && _loadGameNavSetup != null)
                    _harmony.Patch(start, prefix: new HarmonyMethod(typeof(PathfindingWatch).GetMethod("BlockerStartPrefix", BindingFlags.Static | BindingFlags.NonPublic)));
                else
                    _log.LogWarning("PathfindingWatch: gridObjectBlocker.Start / loadGameNavSetup not found - a Quick load's structure nav cut stays the slow one.");
                MethodInfo awake = astar.GetMethod("Awake", all, null, Type.EmptyTypes, null);
                if (awake != null)
                    _harmony.Patch(awake, postfix: new HarmonyMethod(typeof(PathfindingWatch).GetMethod("AwakePostfix", BindingFlags.Static | BindingFlags.NonPublic)));
                MethodInfo destroy = astar.GetMethod("OnDestroy", all, null, Type.EmptyTypes, null);
                if (destroy != null)
                    _harmony.Patch(destroy,
                        prefix: new HarmonyMethod(typeof(PathfindingWatch).GetMethod("DestroyPrefix", BindingFlags.Static | BindingFlags.NonPublic)),
                        postfix: new HarmonyMethod(typeof(PathfindingWatch).GetMethod("DestroyPostfix", BindingFlags.Static | BindingFlags.NonPublic)));
                _log.LogInfo("PathfindingWatch: watching " + patched + " UpdateGraphs overload(s)" +
                             (awake != null ? ", Awake" : "") + (destroy != null ? ", OnDestroy" : "") +
                             (_inProgress == null ? " (no in-progress flag - durations not logged)" : "") + ".");
            }
            catch (Exception ex)
            {
                _log.LogWarning("PathfindingWatch: " + ex.Message);
            }
        }

        public static void Uninstall()
        {
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception) { }
        }

        /// An in-place restore begins.
        public static void RestoreStarted()
        {
            _restoring = true;
            _restoreStarted = Time.realtimeSinceStartup;
            _loadRouted = 0;
        }

        /// It has finished (or failed); the window stays open a moment -
        /// re-created objects start on the next frame.
        public static void RestoreEnded()
        {
            _restoring = false;
            _restoreEnded = Time.realtimeSinceStartup;
        }

        private static bool InRestoreWindow()
        {
            float now = Time.realtimeSinceStartup;
            if (_restoring && now - _restoreStarted < RestoreTimeout) return true;
            return now - _restoreEnded < AfterRestore;
        }

        /// Start's load branch for a blocker re-created by a restore.
        private static bool BlockerStartPrefix(MonoBehaviour __instance)
        {
            try
            {
                if (!InRestoreWindow() || __instance == null) return true;
                __instance.Invoke("findRootTr", 0.1f);
                _loadGameNavSetup.Invoke(__instance, null);
                if (_loadRouted++ == 0)
                    _log.LogInfo("Pathfinding: the restore's structures cut the navmesh the way a load does (grouped by place), " +
                                 "not in one box across the map - first: " + __instance.transform.root.name + ".");
                return false;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Pathfinding: load-style nav cut failed (" + (ex.InnerException ?? ex).Message + ") - the game's own Start runs.");
                return true;
            }
        }

        /// Every frame from the savestate module: only reads while a burst
        /// of updates is pending, and logs once when it has run out.
        public static void Tick()
        {
            if (!_watching) return;
            try
            {
                bool busy = IsBusy(null);
                float now = Time.realtimeSinceStartup;
                if (busy) { _seenRunning = true; return; }
                // One frame between two updates is not the end: wait a
                // little before calling the burst over.
                if (now - _burstStart < 0.5f && !_seenRunning) return;
                _watching = false;
                _log.LogInfo("Pathfinding: graph updates done " + (now - _burstStart).ToString("0.0") + " s after the first of " +
                             _burstQueued + " queued" + (_burstUnlogged > 0 ? " (" + _burstUnlogged + " not listed)" : "") + ".");
            }
            catch (Exception) { _watching = false; }
        }

        private static bool IsBusy(object astar)
        {
            if (_inProgress == null || _graphUpdates == null) return false;
            if (astar == null && _active != null) astar = _active.GetValue(null);
            if (astar == null) return false;
            object gu = _graphUpdates.GetValue(astar);
            return gu != null && (bool)_inProgress.GetValue(gu, null);
        }

        private static void QueuedPrefix(object[] __args)
        {
            try
            {
                // The overloads forward to each other (and a delayed one
                // arrives from AstarPath's own coroutine): log the outer call.
                if (++_depth > 1) return;
                StackFrame[] frames = new StackTrace(1, false).GetFrames();
                if (FromAstarCoroutine(frames)) return;
                float now = Time.realtimeSinceStartup;
                if (!_watching)
                {
                    _watching = true;
                    _seenRunning = false;
                    _burstStart = now;
                    _burstQueued = 0;
                    _burstUnlogged = 0;
                }
                _burstQueued++;
                if (_burstQueued > MaxQueuedLinesPerBurst) { _burstUnlogged++; return; }

                object a0 = __args != null && __args.Length > 0 ? __args[0] : null;
                Bounds b = new Bounds();
                string physics = "";
                if (a0 is Bounds) b = (Bounds)a0;
                else if (a0 != null && _guoBounds != null)
                {
                    b = (Bounds)_guoBounds.GetValue(a0);
                    if (_guoPhysics != null) physics = (bool)_guoPhysics.GetValue(a0) ? ", physics" : ", no physics";
                }
                _log.LogInfo("Pathfinding: graph update queued (+" + (now - _burstStart).ToString("0.00") + " s) at " +
                             V(b.center) + " size " + V(b.size) + physics + ", from " + Caller(frames) + ".");
            }
            catch (Exception) { }
        }

        private static void AwakePostfix(object __instance)
        {
            try
            {
                UnityEngine.Object o = __instance as UnityEngine.Object;
                _log.LogInfo("Pathfinding: AstarPath #" + (o != null ? o.GetInstanceID().ToString() : "?") + " awake.");
            }
            catch (Exception) { }
        }

        private static void DestroyPrefix(object __instance)
        {
            _destroyStart = Time.realtimeSinceStartup;
            try { _destroyWasBusy = IsBusy(__instance); }
            catch (Exception) { _destroyWasBusy = false; }
        }

        private static void DestroyPostfix(object __instance)
        {
            try
            {
                float took = Time.realtimeSinceStartup - _destroyStart;
                UnityEngine.Object o = __instance as UnityEngine.Object;
                _log.LogInfo("Pathfinding: AstarPath #" + (o != null ? o.GetInstanceID().ToString() : "?") + " destroyed in " +
                             (took * 1000f).ToString("0") + " ms" + (_destroyWasBusy ? " (a graph update was running - it waited for it)" : "") + ".");
            }
            catch (Exception) { }
        }

        private static string V(Vector3 v)
        {
            return "(" + v.x.ToString("0.#") + ", " + v.y.ToString("0.#") + ", " + v.z.ToString("0.#") + ")";
        }

        private static bool IsSkipped(Type t)
        {
            return t == null || t.Namespace == "HarmonyLib" || t.Name == "PathfindingWatch";
        }

        private static void QueuedFinalizer()
        {
            if (_depth > 0) _depth--;
        }

        /// A delayed update: AstarPath's UpdateGraphsInteral coroutine
        /// calling in (the original call was logged when it was made).
        private static bool FromAstarCoroutine(StackFrame[] frames)
        {
            for (int i = 0; frames != null && i < frames.Length; i++)
            {
                MethodBase mb = frames[i].GetMethod();
                Type t = mb != null ? mb.DeclaringType : null;
                if (IsSkipped(t) || t.Name == "AstarPath") continue;
                return t.DeclaringType != null && t.DeclaringType.Name == "AstarPath";
            }
            return false;
        }

        private static string Caller(StackFrame[] frames)
        {
            StringBuilder sb = new StringBuilder();
            int shown = 0;
            for (int i = 0; frames != null && i < frames.Length && shown < 4; i++)
            {
                MethodBase mb = frames[i].GetMethod();
                Type t = mb != null ? mb.DeclaringType : null;
                if (IsSkipped(t) || t.Name == "AstarPath") continue;
                if (shown > 0) sb.Append(" <- ");
                sb.Append(t.DeclaringType != null && t.Name.StartsWith("<") ? t.DeclaringType.Name + "." + t.Name : t.Name).Append('.').Append(mb.Name);
                shown++;
            }
            return shown > 0 ? sb.ToString() : "?";
        }
    }
}
