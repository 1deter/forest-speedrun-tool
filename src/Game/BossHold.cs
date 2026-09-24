using System;
using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Holds Megan's transformation until Megan exists (runner maks: after a
    // load, walk about the boss room ~7-8 s first, or the player plays the
    // cutscene while Megan keeps swinging and the run ends stuck).
    //
    // WHY (IL + bridge, v0.24.24): activateGirlTransform.OnTriggerEnter
    // starts the cutscene (InitAnim -> SpecialActions "doGirlTransformRoutine"),
    // which drives Megan through the animator it takes from
    // Scene.SceneTracker.EndgameBoss when its own field is empty. EndgameBoss
    // is set by the boss's mutantAI.Start - seen live 7.0 s after a load
    // (null before). A savestate restore puts the player in (or next to)
    // the trigger at once.
    //
    // WHAT: for a minute after a savestate restore (Arm), a prefix skips the
    // trigger's enter while EndgameBoss is null and enters it again, with
    // the same collider, once Megan is there. Nothing changes outside that
    // minute: a plain load keeps the game's own behaviour. While it waits
    // the player is held where he entered (maks, v0.24.25: he could walk
    // off while Megan was missing).
    // ------------------------------------------------------------------
    public sealed class BossHold
    {
        private const float ArmedFor = 60f;
        private const float AfterBoss = 0.5f;

        private static ManualLogSource _log;
        private static MonoBehaviour _runner;
        private static FieldInfo _tracker;     // static Scene.SceneTracker
        private static FieldInfo _boss;        // sceneTracker.EndgameBoss
        private static MethodInfo _enter;      // activateGirlTransform.OnTriggerEnter
        private static float _armedAt = -1000f;
        private static bool _waiting;

        private Harmony _harmony;
        public string Status { get; private set; }

        public BossHold(ManualLogSource log, MonoBehaviour runner)
        {
            _log = log;
            _runner = runner;
            Status = "not installed";
        }

        public void Install(string harmonyId)
        {
            BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Type scene = GameBridge.FindGameType("TheForest.Utils.Scene");
            Type tracker = GameBridge.FindGameType("sceneTracker");
            Type trigger = GameBridge.FindGameType("activateGirlTransform");
            if (scene != null) _tracker = scene.GetField("SceneTracker", stat);
            if (tracker != null) _boss = tracker.GetField("EndgameBoss", inst);
            if (trigger != null) _enter = trigger.GetMethod("OnTriggerEnter", inst, null, new[] { typeof(Collider) }, null);
            if (_tracker == null || _boss == null || _enter == null)
            {
                Status = "types not found (tracker:" + (_tracker != null) + " boss:" + (_boss != null) + " trigger:" + (_enter != null) + ")";
                _log.LogWarning("BossHold: " + Status);
                return;
            }
            try
            {
                _harmony = new Harmony(harmonyId + ".bosshold");
                _harmony.Patch(_enter, new HarmonyMethod(typeof(BossHold).GetMethod("EnterPrefix", BindingFlags.Static | BindingFlags.NonPublic)));
                Status = "hooked";
            }
            catch (Exception ex) { Status = "Harmony: " + ex.Message; }
            _log.LogInfo("BossHold: " + Status + ".");
        }

        public void Uninstall()
        {
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception) { }
        }

        /// A savestate restore just ran (in place or with a load).
        public static void Arm()
        {
            _armedAt = Time.realtimeSinceStartup;
        }

        private static bool BossThere()
        {
            try
            {
                object t = _tracker.GetValue(null);
                if ((t as UnityEngine.Object) == null) return false;
                return (_boss.GetValue(t) as GameObject) != null;
            }
            catch (Exception) { return true; }
        }

        // false = skip the original. Never throws into the game.
        private static bool EnterPrefix(object __instance, Collider other)
        {
            try
            {
                if (Time.realtimeSinceStartup - _armedAt > ArmedFor) return true;
                if (other == null || !other.gameObject.CompareTag("Player")) return true;
                if (BossThere()) return true;
                if (!_waiting && _runner != null)
                {
                    _waiting = true;
                    _log.LogInfo("BossHold: Megan's transformation held - Megan not there yet (the game sets her up a few seconds after a load).");
                    _runner.StartCoroutine(EnterWhenReady(__instance as Component, other));
                }
                return false;
            }
            catch (Exception) { return true; }
        }

        private static void Pin(Rigidbody body, Vector3 at)
        {
            if (body == null) return;
            try
            {
                body.velocity = Vector3.zero;
                body.position = at;
                body.transform.position = at;
            }
            catch (Exception) { }
        }

        private static IEnumerator EnterWhenReady(Component trigger, Collider other)
        {
            float start = Time.realtimeSinceStartup;
            Rigidbody body = other != null ? other.attachedRigidbody : null;
            Vector3 at = body != null ? body.position : Vector3.zero;
            float ready = -1f;
            while (ready < 0f || Time.realtimeSinceStartup - ready < AfterBoss)
            {
                if (trigger == null || Time.realtimeSinceStartup - start > 30f)
                {
                    _waiting = false;
                    _log.LogWarning("BossHold: Megan did not appear within 30 s - the transformation was not started.");
                    yield break;
                }
                if (ready < 0f && BossThere()) ready = Time.realtimeSinceStartup;
                Pin(body, at);
                yield return null;
            }
            _waiting = false;
            if (trigger == null || other == null) yield break;
            _log.LogInfo("BossHold: Megan is there after " + (Time.realtimeSinceStartup - start).ToString("0.0") +
                         " s - starting the transformation.");
            try { _enter.Invoke(trigger, new object[] { other }); }
            catch (Exception ex) { _log.LogWarning("BossHold: starting the transformation failed: " + (ex.InnerException ?? ex).Message); }
        }
    }
}
