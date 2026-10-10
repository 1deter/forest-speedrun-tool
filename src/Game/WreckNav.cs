using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using ForestOverlay.Data;
using HarmonyLib;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // 20. The re-created plane wreck's navmesh updates (T-0278).
    //
    // WHY (IL + bridge, T-0202): every in-place restore makes a new wreck
    // (PlaneCrashController.loadCrashPlane) where the old one stands; the
    // overlay removes the old one 1.5 s on (SavestateBridge.
    // ClearOldPlaneHulls). Two graph updates over the same 73 x 44 x 66 m
    // box follow, each ~180 ms on the main thread (AstarPath.
    // FlushWorkItems / PerformBlockingActions) and ~7.7 MB of garbage
    // (RecastMeshGatherer copying the cutters' meshes):
    // - the new wreck's cut: gridObjectBlocker.doNavCut makes its
    //   navCubeCutter, then sceneTracker.doGlobalStructureBoundsNavRemove
    //   (a load's route) batches every structure waiting, recalculates
    //   each group's box, then mutantController.calculateMainNavArea
    //   (reads the navmesh only);
    // - the old wreck's removal: setupNavRemoveRoot.OnDestroy ->
    //   sceneTracker.startDummyNavRemove -> 7 s on, one update.
    // A recast update rebuilds the box from what stands in it, and the old
    // and new wreck stand at the same pose with the same cutter: both
    // recompute the navmesh already there (NodeHash the same with 1 or 4
    // cutters, game-notes *Performance*).
    //
    // WHAT: prefixes on the three. A wreck (a root holding CrashClearing,
    // named as the live one) is skipped by Data/WreckCuts' rule: a new cut
    // only over a twin whose own cut is in, a removal only of a wreck the
    // overlay removes while a cut twin stays. The game's coroutine for
    // anything else runs untouched. Behind PerfPatches'
    // RestoreSkipSameWreckNav (on).
    // ------------------------------------------------------------------
    public static class WreckNav
    {
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static ManualLogSource _log;
        private static Harmony _harmony;
        private static readonly WreckCuts<GameObject> Cuts = new WreckCuts<GameObject>(Alive, Position, Forward, Up);
        private static readonly Func<GameObject, bool> PendingFn = Pending;

        private static MethodInfo _global, _single, _dummy;
        private static FieldInfo _globalList;          // sceneTracker.globalNavStructures
        private static FieldInfo _planeCrash, _spawnedHull;
        private static Type _crashClearing;
        private static IList _pendingList;             // the list of the call being answered

        /// Updates skipped this launch (the bridge reads them).
        public static int SkippedCuts { get; private set; }
        public static int SkippedRemovals { get; private set; }

        /// "" = applied, else why not.
        public static string Apply(Harmony harmony, ManualLogSource log)
        {
            _log = log;
            Type tracker = GameBridge.FindGameType("sceneTracker");
            if (tracker == null) return "sceneTracker not found";
            _global = tracker.GetMethod("doGlobalStructureBoundsNavRemove", Inst, null, new[] { typeof(Transform), typeof(Bounds) }, null);
            _single = tracker.GetMethod("doStructureBoundsNavRemove", Inst, null, new[] { typeof(Transform), typeof(Bounds), typeof(float) }, null);
            _dummy = tracker.GetMethod("startDummyNavRemove", Inst, null, new[] { typeof(GameObject), typeof(Vector3), typeof(Bounds) }, null);
            _globalList = tracker.GetField("globalNavStructures", Inst);
            if (_global == null || _single == null || _dummy == null || _globalList == null ||
                _global.ReturnType != typeof(IEnumerator) || _single.ReturnType != typeof(IEnumerator) || _dummy.ReturnType != typeof(IEnumerator))
                return "sceneTracker's nav cut / removal methods not found";
            Type scene = GameBridge.FindGameType("TheForest.Utils.Scene");
            Type plane = GameBridge.FindGameType("PlaneCrashController");
            _planeCrash = scene != null ? scene.GetField("PlaneCrash", Stat) : null;
            _spawnedHull = plane != null ? plane.GetField("spawnedHullPrefab", Inst) : null;
            _crashClearing = GameBridge.FindGameType("CrashClearing");
            if (_planeCrash == null || _spawnedHull == null || _spawnedHull.FieldType != typeof(GameObject) || _crashClearing == null)
                return "the plane wreck's members not found";

            _harmony = harmony;
            _harmony.Patch(_global, prefix: Prefix("GlobalPrefix"));
            _harmony.Patch(_single, prefix: Prefix("SinglePrefix"));
            _harmony.Patch(_dummy, prefix: Prefix("DummyPrefix"));
            return "";
        }

        public static void Remove()
        {
            if (_harmony == null) return;
            if (_global != null) _harmony.Unpatch(_global, HarmonyPatchType.Prefix, _harmony.Id);
            if (_single != null) _harmony.Unpatch(_single, HarmonyPatchType.Prefix, _harmony.Id);
            if (_dummy != null) _harmony.Unpatch(_dummy, HarmonyPatchType.Prefix, _harmony.Id);
            Cuts.Clear();
            _harmony = null;
        }

        private static HarmonyMethod Prefix(string name)
        {
            HarmonyMethod m = new HarmonyMethod(typeof(WreckNav).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));
            m.priority = Priority.First;   // before NavRemovalOwnArea's on startDummyNavRemove
            return m;
        }

        /// SavestateBridge.ClearOldPlaneHulls, before it destroys `hull`.
        public static void Removing(GameObject hull)
        {
            if (_harmony != null && hull != null) Cuts.Removing(hull);
        }

        /// NavRemovalOwnArea's prefix asks too, so the answer holds
        /// whichever prefix runs first.
        /// `go` is what setupNavRemoveRoot.OnDestroy passes (the wreck or
        /// a part of it); returns the twin that keeps the cut, or null.
        public static GameObject SkipsRemoval(GameObject go)
        {
            if (_harmony == null || go == null) return null;
            return Cuts.Removal(go.transform.root.gameObject);
        }

        private static bool GlobalPrefix(object __instance, Transform __0, ref IEnumerator __result)
        {
            try
            {
                GameObject root = Wreck(__0);
                if (root == null) return true;
                _pendingList = _globalList.GetValue(__instance) as IList;
                GameObject twin = Cuts.NewCut(root, PendingFn);
                _pendingList = null;
                if (twin == null) return true;
                SkippedCuts++;
                _log.LogInfo("Performance: plane wreck - the restore's new wreck skipped its navmesh update; the wreck at " +
                             Pos(twin.transform.position) + " already cut the same place.");
                __result = Nothing();
                return false;
            }
            catch (Exception ex)
            {
                _pendingList = null;
                _log.LogWarning("Performance: plane wreck - nav cut check failed (" + (ex.InnerException ?? ex).Message + "); the game's update runs.");
                return true;
            }
        }

        private static void SinglePrefix(Transform __0)
        {
            try
            {
                GameObject root = Wreck(__0);
                if (root != null) Cuts.OtherCut(root);
            }
            catch (Exception) { }
        }

        private static bool DummyPrefix(GameObject __0, ref IEnumerator __result)
        {
            try
            {
                GameObject twin = SkipsRemoval(__0);
                if (twin == null) return true;
                SkippedRemovals++;
                _log.LogInfo("Performance: plane wreck - the old wreck's removal skipped its navmesh update; the new wreck at " +
                             Pos(twin.transform.position) + " keeps the same cut.");
                __result = Nothing();
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }

        // The root `t` belongs to when it is a plane wreck: named as the
        // live one and holding CrashClearing. Else null.
        private static GameObject Wreck(Transform t)
        {
            if (t == null) return null;
            GameObject root = t.root.gameObject;
            object ctrl = _planeCrash.GetValue(null);
            GameObject live = ctrl != null ? _spawnedHull.GetValue(ctrl) as GameObject : null;
            if (live == null || root.name != live.name) return null;
            return root.GetComponentInChildren(_crashClearing, true) != null ? root : null;
        }

        private static bool Pending(GameObject wreck)
        {
            return _pendingList == null || _pendingList.Contains(wreck);
        }

        private static IEnumerator Nothing()
        {
            yield break;
        }

        private static bool Alive(GameObject go) { return go != null; }
        private static Vector3 Position(GameObject go) { return go.transform.position; }
        private static Vector3 Forward(GameObject go) { return go.transform.forward; }
        private static Vector3 Up(GameObject go) { return go.transform.up; }

        private static string Pos(Vector3 p)
        {
            return "(" + p.x.ToString("0") + ", " + p.y.ToString("0") + ", " + p.z.ToString("0") + ")";
        }
    }
}
