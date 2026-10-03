using System;
using System.Reflection;
using BepInEx.Logging;
using ForestOverlay.Data;
using HarmonyLib;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Feeds Data/MoveDetector the explosion knockback's pushes (banned-move
    // detection; docs/run-mode.md *Banned moves: detection*). Read-only.
    //
    // WHAT (IL, playerHitReactions/<enableExplodeCamera>c__Iterator0
    // ::MoveNext): $PC 0 = the first call (the knockback starts); then
    //   phase 1 (returns with $PC 1): while timer < 0.5 { timer += dt;
    //     AddForce(-forward * 8, VelocityChange); yield }
    //   phase 2 (returns with $PC 2): while the layer-2 state is tagged
    //     explode { if (timer < 0.25) AddForce(the same); timer += dt; yield }
    //   then the end (returns false).
    // So a call that returns true pushed once, except in phase 2 with the
    // timer already at 0.25 (the check is before `timer += dt`, so the
    // timer it saw is timer - deltaTime). A push with Time.deltaTime 0 is a
    // push while game time is stopped: the bomb boost's whole mechanism.
    // A prefix keeps $PC from before the call (0 = the start).
    // ------------------------------------------------------------------
    public static class MoveWatch
    {
        private static ManualLogSource _log;
        private static Harmony _harmony;
        private static FieldInfo _pc, _timer;
        private static FieldInfo _lpTransform;   // static LocalPlayer.Transform

        /// Where the pushes go (null = nobody listens).
        public static MoveDetector Detector;

        /// Pushes seen since startup / while time was stopped (the "did it
        /// see anything" counts, gotcha 43).
        public static int Pushes, StoppedPushes, Knockbacks;

        public static string Status = "not installed";

        public static void Install(ManualLogSource log, string harmonyId)
        {
            _log = log;
            try
            {
                Type hit = GameBridge.FindGameType("playerHitReactions");
                Type it = null;
                if (hit != null)
                    foreach (Type n in hit.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                        if (n.Name.StartsWith("<enableExplodeCamera>")) { it = n; break; }
                MethodInfo move = it != null ? it.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null;
                BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                _pc = it != null ? it.GetField("$PC", f) : null;
                _timer = it != null ? it.GetField("<timer>__0", f) : null;
                if (move == null || _pc == null || _timer == null)
                {
                    Status = "the knockback coroutine was not found - bomb boosts are not detected";
                    _log.LogWarning("MoveWatch: " + Status + ".");
                    return;
                }
                Type lp = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
                if (lp != null) _lpTransform = lp.GetField("Transform", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                _harmony = new Harmony(harmonyId + ".movewatch");
                _harmony.Patch(move,
                    new HarmonyMethod(typeof(MoveWatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)),
                    new HarmonyMethod(typeof(MoveWatch).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)));
                Status = "watching the explosion knockback";
                _log.LogInfo("MoveWatch: " + Status + " (" + it.Name + ".MoveNext).");
            }
            catch (Exception ex)
            {
                Status = "failed: " + ex.Message;
                _log.LogWarning("MoveWatch: " + Status);
            }
        }

        public static void Uninstall()
        {
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception) { }
        }

        private static void Prefix(object __instance, out int __state)
        {
            __state = -1;
            try { __state = (int)_pc.GetValue(__instance); }
            catch (Exception) { }
        }

        private static void Postfix(object __instance, bool __result, int __state)
        {
            try
            {
                MoveDetector d = Detector;
                if (__state == 0)
                {
                    Knockbacks++;
                    if (d != null) d.KnockbackStarted();
                }
                if (!__result) return;
                int pc = (int)_pc.GetValue(__instance);
                float dt = Time.deltaTime;
                bool pushed = pc == 1 || (pc == 2 && (float)_timer.GetValue(__instance) - dt < 0.25f);
                if (!pushed) return;
                bool stopped = dt <= 0f;
                Pushes++;
                if (stopped) StoppedPushes++;
                if (d == null) return;
                Transform t = _lpTransform != null ? _lpTransform.GetValue(null) as Transform : null;
                d.KnockbackPush(stopped, stopped && MenuClose.PauseMenuOpen(), t != null, t != null ? t.position : Vector3.zero);
            }
            catch (Exception) { }
        }
    }
}
