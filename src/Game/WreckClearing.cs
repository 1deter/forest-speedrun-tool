using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using ForestOverlay.Data;
using HarmonyLib;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The plane wreck an in-place restore re-creates skips the grass cut
    // of its crash clearing - the wreck already standing there has cut
    // that grass (T-0148).
    //
    // WHY (bridge + IL, 2026-10-07, v0.24.251): a Quick load makes a new
    // wreck (PlaneCrashController.OnDeserialized -> setupCrashedPlane,
    // 0.3 s later -> a new Hull(Clone); the old one goes 1.5 s on,
    // SavestateBridge.ClearOldPlaneHulls). Its Hull carries CrashClearing,
    // whose `void Start()` runs OnCrash (game-notes *The plane wreck across
    // an in-place restore*): FindObjectsOfType<LOD_Base> over the world,
    // Destroy (or burn) every LOD_Base along the 70 m crash path, and
    // NeoGrassCutter.Cut at each of its 5 steps - OnCrash 161-195 ms a call,
    // the five Cut calls 27-30 ms each (~140 ms), so the LOD part ~25 ms.
    // That was the second of the two "Load timing: hitch" lines after every
    // spot restart.
    //
    // The grass cut repeats nothing: NeoGrassCutter only ever writes 0 (it
    // is the only SetDetailLayer caller in the game's code outside debug
    // keys; Grow only from the debug console), so the same place cut again
    // is unchanged. The LOD part does matter: pooled greeble plants and
    // rocks spawned on the path since the last clearing lose their LOD_Base
    // (415 -> 407 within 100 m on the first repeat after a teleport there).
    //
    // WHAT: a prefix on CrashClearing.Start. A wreck starting where a live
    // one stands (Data/WreckSites, tested) runs OnCrash's LOD part as the
    // game does - the same search, the game's own GetPosition steps, Burn /
    // Destroy - and skips the grass cut (one log line). The first wreck of
    // a scene and a wreck at a new place run the game's Start untouched.
    // ------------------------------------------------------------------
    public static class WreckClearing
    {
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static ManualLogSource _log;
        private static Harmony _harmony;
        private static readonly WreckSites<Component> Sites = new WreckSites<Component>(Alive, PositionOf);

        private static Type _lodBase;
        private static FieldInfo _radius, _length, _preferBurning;
        private static MethodInfo _stepCount, _getPosition, _burn;

        /// Wrecks whose grass cut was skipped, this launch.
        public static int Skipped { get; private set; }

        public static void Install(ManualLogSource log, string harmonyId)
        {
            _log = log;
            try
            {
                Type t = GameBridge.FindGameType("CrashClearing");
                _lodBase = GameBridge.FindGameType("LOD_Base");
                MethodInfo start = t != null ? t.GetMethod("Start", Inst, null, Type.EmptyTypes, null) : null;
                if (t != null)
                {
                    _radius = t.GetField("Radius", Inst);
                    _length = t.GetField("Length", Inst);
                    _preferBurning = t.GetField("PreferBurning", Inst);
                    _stepCount = t.GetMethod("GetStepCount", Inst, null, Type.EmptyTypes, null);
                    _getPosition = t.GetMethod("GetPosition", Inst, null, new[] { typeof(float) }, null);
                }
                _burn = _lodBase != null ? _lodBase.GetMethod("Burn", Inst, null, Type.EmptyTypes, null) : null;
                // `void Start()` (ilscan type): returning false skips it with no result to fill.
                if (start == null || start.ReturnType != typeof(void) || _lodBase == null || _radius == null || _length == null ||
                    _preferBurning == null || _stepCount == null || _getPosition == null || _getPosition.ReturnType != typeof(Vector3) ||
                    _burn == null || _burn.ReturnType != typeof(bool))
                {
                    _log.LogWarning("WreckClearing: CrashClearing / LOD_Base members not found - a restore's new plane wreck clears its crash path again.");
                    return;
                }
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
            Sites.Clear();
        }

        private static bool Alive(Component c) { return c != null; }
        private static Vector3 PositionOf(Component c) { return c.transform.position; }

        private static bool StartPrefix(MonoBehaviour __instance)
        {
            try
            {
                if (__instance == null) return true;
                Vector3 at = __instance.transform.position;
                if (!Sites.IsRepeat(__instance, at)) return true;

                int removed = ClearLods(__instance);
                Skipped++;
                _log.LogInfo("Plane wreck: the restore's new wreck cleared its crash path's plants as the game does (" + removed +
                             " LOD(s)) but skipped the grass cut - the wreck already at " + Pos(at) + " cut it.");
                return false;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Plane wreck: clearing check failed (" + (ex.InnerException ?? ex).Message + ") - the game's clearing runs.");
                return true;
            }
        }

        /// CrashClearing.OnCrash without NeoGrassCutter.Cut, line for line
        /// (decompiled 2026-10-07). Returns how many LOD_Base it destroyed.
        private static int ClearLods(MonoBehaviour cc)
        {
            float radius = (float)_radius.GetValue(cc);
            float length = (float)_length.GetValue(cc);
            bool preferBurning = (bool)_preferBurning.GetValue(cc);
            int stepCount = (int)_stepCount.Invoke(cc, null);

            UnityEngine.Object[] array = UnityEngine.Object.FindObjectsOfType(_lodBase);
            List<Component> list = new List<Component>();
            Vector3 position = cc.transform.position;
            float num = radius + length;
            num *= num;
            float num2 = radius * radius;
            for (int i = 0; i < array.Length; i++)
            {
                Component lod = array[i] as Component;
                if (lod != null && (lod.transform.position - position).sqrMagnitude < num) list.Add(lod);
            }

            int removed = 0;
            bool[] counted = new bool[list.Count];   // the game destroys one again at a later step: counted once
            object[] arg = new object[1];
            for (int j = 0; j < stepCount; j++)
            {
                arg[0] = (float)j / (float)stepCount;
                Vector3 position2 = (Vector3)_getPosition.Invoke(cc, arg);
                for (int k = 0; k < list.Count; k++)
                {
                    Component item = list[k];
                    if ((item.transform.position - position2).sqrMagnitude < num2 &&
                        (!preferBurning || !(bool)_burn.Invoke(item, null)))
                    {
                        UnityEngine.Object.Destroy(item);
                        if (!counted[k]) { counted[k] = true; removed++; }
                    }
                }
            }
            return removed;
        }

        private static string Pos(Vector3 p)
        {
            return "(" + p.x.ToString("0") + ", " + p.y.ToString("0") + ", " + p.z.ToString("0") + ")";
        }
    }
}
