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
    //
    // Cave entrances (IL, playerEnterCaveAction/<doCave>c__Iterator0
    // ::MoveNext): $PC 0 = the start (`enter`: in, not out; the player is
    // parented to `posGo` there); $PC 3 sends InACave; it returns false
    // when it lets go of the player (or hands over to the Timmy goodbye
    // cutscene, `timmyCutscene`). A call that started from $PC > 0 and
    // returned false is the let-go: where the player is then, against the
    // terrain, is Data/MoveDetector.CaveEntryEnded's whole question.
    //
    // Landings (IL, FirstPersonCharacter::HandleLanded): it hurts when
    // prevVelocity > 28 && !(shell ride or glider with prevVelocityXZ > 32)
    // && allowFallDamage && jumpingTimer > 0.75 && !jumpLand &&
    // !Clock.planecrash. A prefix reads all of it before the game acts, so
    // Data/MoveDetector.Landed sees what the game is about to judge.
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
        public static string CaveStatus = "not installed";

        /// Cave entries seen / let go of since startup ("did it see anything").
        public static int CaveEntries, CaveLetGo;

        private static FieldInfo _cavePc, _caveEnter, _cavePosGo, _caveThis, _caveTimmy;

        public static string LandStatus = "not installed";

        /// Landings seen since startup ("did it see anything").
        public static int Landings;

        private static FieldInfo _prevVel, _prevVelXZ, _allowFall, _jumpTimer, _jumpLand, _fpcRb;
        private static PropertyInfo _swimming;
        private static FieldInfo _animControl, _shellRide, _flyingGlider, _planeCrash;
        private static PropertyInfo _isInCaves;   // static LocalPlayer.IsInCaves
        private static bool _caveActive;
        private static Vector3 _caveFrom;
        private static float _caveAt;

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
                InstallCave(lp);
                InstallLand(lp);
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

        private static void InstallCave(Type lp)
        {
            try
            {
                Type act = GameBridge.FindGameType("playerEnterCaveAction");
                Type it = null;
                if (act != null)
                    foreach (Type n in act.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                        if (n.Name.StartsWith("<doCave>")) { it = n; break; }
                BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                MethodInfo move = it != null ? it.GetMethod("MoveNext", f) : null;
                _cavePc = it != null ? it.GetField("$PC", f) : null;
                _caveEnter = it != null ? it.GetField("enter", f) : null;
                _cavePosGo = it != null ? it.GetField("posGo", f) : null;
                _caveThis = it != null ? it.GetField("$this", f) : null;
                _caveTimmy = act != null ? act.GetField("timmyCutscene", f) : null;
                _isInCaves = lp != null ? lp.GetProperty("IsInCaves", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) : null;
                if (move == null || _cavePc == null || _caveEnter == null || _isInCaves == null)
                {
                    CaveStatus = "the cave entrance coroutine was not found - cave state force loads are not detected";
                    _log.LogWarning("MoveWatch: " + CaveStatus + ".");
                    return;
                }
                _harmony.Patch(move,
                    new HarmonyMethod(typeof(MoveWatch).GetMethod("CavePrefix", BindingFlags.Static | BindingFlags.NonPublic)),
                    new HarmonyMethod(typeof(MoveWatch).GetMethod("CavePostfix", BindingFlags.Static | BindingFlags.NonPublic)));
                CaveStatus = "watching cave entrances";
                _log.LogInfo("MoveWatch: " + CaveStatus + " (" + it.Name + ".MoveNext).");
            }
            catch (Exception ex)
            {
                CaveStatus = "failed: " + ex.Message;
                _log.LogWarning("MoveWatch: cave entrances " + CaveStatus);
            }
        }

        private static void InstallLand(Type lp)
        {
            try
            {
                Type fpc = GameBridge.FindGameType("FirstPersonCharacter");
                BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                BindingFlags s = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                MethodInfo landed = fpc != null ? fpc.GetMethod("HandleLanded", f, null, Type.EmptyTypes, null) : null;
                if (fpc != null)
                {
                    _prevVel = fpc.GetField("prevVelocity", f);
                    _prevVelXZ = fpc.GetField("prevVelocityXZ", f);
                    _allowFall = fpc.GetField("allowFallDamage", f);
                    _jumpTimer = fpc.GetField("jumpingTimer", f);
                    _jumpLand = fpc.GetField("jumpLand", f);
                    _swimming = fpc.GetProperty("swimming", f);
                    _fpcRb = fpc.GetField("rb", f);
                }
                _animControl = lp != null ? lp.GetField("AnimControl", s) : null;
                Type anim = GameBridge.FindGameType("playerAnimatorControl");
                _shellRide = anim != null ? anim.GetField("doShellRideMode", f) : null;
                _flyingGlider = anim != null ? anim.GetField("flyingGlider", f) : null;
                Type clock = GameBridge.FindGameType("Clock");
                _planeCrash = clock != null ? clock.GetField("planecrash", s) : null;
                if (landed == null || _prevVel == null || _allowFall == null || _jumpTimer == null || _jumpLand == null)
                {
                    LandStatus = "the game's landing was not found - fall damage cancels are not detected";
                    _log.LogWarning("MoveWatch: " + LandStatus + ".");
                    return;
                }
                _harmony.Patch(landed, new HarmonyMethod(typeof(MoveWatch).GetMethod("LandPrefix", BindingFlags.Static | BindingFlags.NonPublic)));
                LandStatus = "watching landings";
                _log.LogInfo("MoveWatch: " + LandStatus + " (FirstPersonCharacter.HandleLanded).");
            }
            catch (Exception ex)
            {
                LandStatus = "failed: " + ex.Message;
                _log.LogWarning("MoveWatch: landings " + LandStatus);
            }
        }

        private static void LandPrefix(object __instance)
        {
            try
            {
                Landings++;
                MoveDetector d = Detector;
                if (d == null) return;
                float judged = (float)_prevVel.GetValue(__instance);
                float air = (float)_jumpTimer.GetValue(__instance);
                bool gates = (bool)_allowFall.GetValue(__instance) && !(bool)_jumpLand.GetValue(__instance);
                if (_planeCrash != null && (bool)_planeCrash.GetValue(null)) gates = false;
                object ac = _animControl != null ? _animControl.GetValue(null) : null;
                if (ac != null && _prevVelXZ != null)
                {
                    bool ride = (_shellRide != null && (bool)_shellRide.GetValue(ac)) || (_flyingGlider != null && (bool)_flyingGlider.GetValue(ac));
                    if (ride && ((Vector3)_prevVelXZ.GetValue(__instance)).magnitude > 32f) gates = false;
                }
                bool swimming = _swimming != null && (bool)_swimming.GetValue(__instance, null);
                Transform t = _lpTransform != null ? _lpTransform.GetValue(null) as Transform : null;
                Rigidbody rb = _fpcRb != null ? _fpcRb.GetValue(__instance) as Rigidbody : null;
                float now = rb != null ? -rb.velocity.y : 0f;
                int before = d.Ready.Count;
                float fell = Mathf.Max(d.RecentFall, now);
                d.Landed(t != null ? t.position : Vector3.zero, judged, gates, air, swimming, now);
                if (d.Ready.Count > before || (fell > MoveDetector.FallSpeed && judged <= MoveDetector.FallSpeed))
                    _log.LogInfo("MoveWatch: a landing after " + air.ToString("0.00") + " s in the air: judged at " + judged.ToString("0.0") +
                                 " m/s, fell at " + fell.ToString("0.0") + " m/s, damage allowed " + gates + ", swimming " + swimming + ".");
            }
            catch (Exception) { }
        }

        private static void CavePrefix(object __instance, out int __state)
        {
            __state = -1;
            try { __state = (int)_cavePc.GetValue(__instance); }
            catch (Exception) { }
        }

        private static void CavePostfix(object __instance, bool __result, int __state)
        {
            try
            {
                Transform t = _lpTransform != null ? _lpTransform.GetValue(null) as Transform : null;
                if (__state == 0)
                {
                    _caveActive = (bool)_caveEnter.GetValue(__instance) && __result;
                    if (!_caveActive) return;
                    CaveEntries++;
                    GameObject at = _cavePosGo != null ? _cavePosGo.GetValue(__instance) as GameObject : null;
                    _caveFrom = at != null ? at.transform.position : (t != null ? t.position : Vector3.zero);
                    _caveAt = Time.time;
                    return;
                }
                if (__result || !_caveActive) return;
                _caveActive = false;
                CaveLetGo++;
                object self = _caveThis != null ? _caveThis.GetValue(__instance) : null;
                if (self != null && _caveTimmy != null && (bool)_caveTimmy.GetValue(self)) return;   // the goodbye cutscene takes over
                MoveDetector d = Detector;
                if (d == null || t == null) return;
                Vector3 p = t.position;
                Terrain terrain = Terrain.activeTerrain;
                float under = terrain != null ? terrain.SampleHeight(p) + terrain.GetPosition().y - p.y : float.NaN;
                bool inCaves = (bool)_isInCaves.GetValue(null, null);
                float from = Vector3.Distance(_caveFrom, p);
                _log.LogInfo("MoveWatch: a cave entrance let go of the player at (" + p.x.ToString("0.0") + ", " + p.y.ToString("0.0") + ", " +
                             p.z.ToString("0.0") + "), " + (terrain != null ? under.ToString("0.0") + " m under the terrain" : "no terrain") +
                             ", in cave state " + inCaves + ", " + from.ToString("0.0") + " m from where it took hold.");
                d.CaveEntryEnded(p, inCaves, terrain != null, under, from, Time.time - _caveAt);
            }
            catch (Exception) { }
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
