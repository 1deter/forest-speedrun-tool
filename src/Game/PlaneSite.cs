using System;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Where this save's plane crashed: one of 12 sites
    // (PlaneCrashLocations.finalPositions), kept by the game on its
    // PlaneCrashController (`planePosition` / `planeRotation`, serialized
    // with the save). Recorded in each finished run (`plane|` line) so the
    // website's map shows that run's plane - author, 2026-09-27.
    //
    // Read once per finished run, from the game's own handle
    // TheForest.Utils.Scene.PlaneCrash (set by Scene.Awake and
    // PlaneCrashController.Start, cleared by Scene.OnDestroy). It was a
    // FindObjectOfType: 22-25 ms in ForestMain, most of every finish's
    // 'Slow tick: practicerun' (T-0150, measured over the bridge).
    // ------------------------------------------------------------------
    public static class PlaneSite
    {
        private static bool _warned;

        /// False when there is no crashed plane (title screen, multiplayer
        /// client, a game update renamed it).
        public static bool TryRead(ManualLogSource log, out Vector3 position, out float yaw)
        {
            position = Vector3.zero;
            yaw = 0f;
            try
            {
                if (GameBridge.FindGameType("PlaneCrashController") == null)
                {
                    if (!_warned && log != null) log.LogWarning("Plane site: PlaneCrashController not found - runs will not record the plane.");
                    _warned = true;
                    return false;
                }
                UnityEngine.Object c = GameBridge.ReadStaticField("TheForest.Utils.Scene", "PlaneCrash") as UnityEngine.Object;
                if (c == null) return false;
                object crashed = Field(c, "Crashed");
                if (!(crashed is bool) || !(bool)crashed) return false;
                object p = Field(c, "planePosition"), r = Field(c, "planeRotation");
                if (!(p is Vector3) || !(r is Quaternion)) return false;
                position = (Vector3)p;
                yaw = ((Quaternion)r).eulerAngles.y;
                return position != Vector3.zero;
            }
            catch (Exception e)
            {
                if (log != null) log.LogWarning("Plane site: " + e.Message);
                return false;
            }
        }

        private static object Field(object o, string name)
        {
            System.Reflection.FieldInfo f = o.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            return f != null ? f.GetValue(o) : null;
        }
    }
}
