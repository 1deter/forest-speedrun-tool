using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The plane wreck an in-place restore re-creates skips the crash
    // clearing the wreck already standing there has done (T-0148).
    //
    // WHY (bridge + IL, 2026-10-07, v0.24.251): a Quick load makes a new
    // wreck (PlaneCrashController.OnDeserialized -> setupCrashedPlane,
    // 0.3 s later -> a new Hull(Clone); the old one goes 1.5 s on,
    // SavestateBridge.ClearOldPlaneHulls). Its Hull carries CrashClearing,
    // whose Start runs OnCrash: FindObjectsOfType<LOD_Base> over the whole
    // world, Destroy (or burn) every tree LOD along the 70 m crash path,
    // and NeoGrassCutter.Cut's per-cell SetDetailLayer calls on the
    // terrain grass - 194 / 195 ms per call on the live wreck (bridge
    // `call ... CrashClearing.OnCrash`), and the game profiler's
    // CrashClearing.Start max 195 ms, once per restore: the second of the
    // two "Load timing: hitch" lines after every spot restart. The path
    // was cleared when the first wreck at that spot started (a load, or
    // the opening cutscene's own OnCrash): the trees' LODs are destroyed
    // and the grass is cut, for the rest of the scene.
    //
    // WHAT: a prefix on CrashClearing.Start. Every wreck that starts is
    // remembered; one starting within a metre of a live one is skipped
    // (one log line). A wreck at a new place (a restore from another save)
    // or the first one of a scene runs the game's clearing as before.
    // ------------------------------------------------------------------
    public static class WreckClearing
    {
        private const float SamePlace = 1f;

        private static ManualLogSource _log;
        private static Harmony _harmony;
        private static readonly List<Component> Started = new List<Component>();

        /// Wrecks whose clearing was skipped, this launch.
        public static int Skipped { get; private set; }

        public static void Install(ManualLogSource log, string harmonyId)
        {
            _log = log;
            try
            {
                Type t = GameBridge.FindGameType("CrashClearing");
                MethodInfo start = t != null ? t.GetMethod("Start", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) : null;
                if (start == null) { _log.LogWarning("WreckClearing: CrashClearing.Start not found - a restore's new plane wreck clears its crash path again."); return; }
                _harmony = new Harmony(harmonyId + ".wreckclearing");
                _harmony.Patch(start, prefix: new HarmonyMethod(typeof(WreckClearing).GetMethod("StartPrefix", BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (Exception ex)
            {
                _log.LogWarning("WreckClearing: " + ex.Message);
            }
        }

        public static void Uninstall()
        {
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception) { }
            Started.Clear();
        }

        private static bool StartPrefix(MonoBehaviour __instance)
        {
            try
            {
                if (__instance == null) return true;
                Vector3 at = __instance.transform.position;
                Component already = null;
                for (int i = Started.Count - 1; i >= 0; i--)
                {
                    Component c = Started[i];
                    if (c == null) { Started.RemoveAt(i); continue; }
                    if (ReferenceEquals(c, __instance)) return true;
                    if ((c.transform.position - at).sqrMagnitude < SamePlace * SamePlace) already = c;
                }
                Started.Add(__instance);
                if (already == null) return true;

                Skipped++;
                _log.LogInfo("Plane wreck: the restore's new wreck skipped the game's crash clearing - the wreck already at " +
                             Pos(at) + " cleared that path (trees and grass), ~0.2 s saved.");
                return false;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Plane wreck: clearing check failed (" + ex.Message + ") - the game's clearing runs.");
                return true;
            }
        }

        private static string Pos(Vector3 p)
        {
            return "(" + p.x.ToString("0") + ", " + p.y.ToString("0") + ", " + p.z.ToString("0") + ")";
        }
    }
}
