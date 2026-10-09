using System;
using System.Collections.Generic;
using System.Reflection;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The game's own death, ended by a restore or a teleport the way a
    // load ends it (runner maks, T-0248: after a capture the next death
    // sent him to the menu; a restart while hanging in the cave left him
    // at the spot upside down with the rope on his hips).
    //
    // WHY (decompiled PlayerStats, proved over the bridge 2026-10-09):
    // a death is a chain of PlayerStats Invokes and coroutines -
    //   CheckDeath -> FallDownDead -> Invoke BlackScreen (4 s)
    //   BlackScreen -> dragAwayCutScene (first death outside a cave) or
    //                  Invoke KillPlayer
    //   KillPlayer  -> DeadTimes++; > 1: dead cam, Invoke GameOver (6 s,
    //                  the title screen); else the capture: WakeInCave ->
    //                  hangingInCaveCutScene (the rope `hangingPlayerRopeGo`
    //                  parented to the hips, upside down, no control)
    //   KillMeFast (drowning) and EndgameWakeUp (the boss fight) likewise.
    // A Full load builds a new player, so none of it survives. A restore
    // in place keeps the live PlayerStats: its pending Invokes fired after
    // the restore (GameOver: the menu), its coroutines carried on (the
    // drag-away captured the player anyway), and what they had changed
    // outside the save stayed (the rope, the pose, the cameras, the locks).
    // `DeadTimes` and `doneDragScene` are not in the save either: a load
    // gives 0 / false, a restore in place kept them, so the death after a
    // capture was a real one.
    //
    // WHAT: End() stops PlayerStats' coroutines and the chain's Invokes,
    // destroys the cutscene's objects, and puts back what the chain
    // switched off - the values the game's own wake-ups set
    // (WakeFromKnockOut, releaseFromHanging, dragAwayCutScene's end), the
    // camera mask and clip planes as they were when the death started.
    // Checked against a Full load of the same spot (a field diff of the
    // player's components over the bridge). It runs only when a death is
    // in progress; ForgetDeaths() runs on every restore in place.
    // ------------------------------------------------------------------
    public static class DeathSequence
    {
        private const BindingFlags Stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// End()'s answer when the death could not be ended in place - the
        /// restore then does a Full load instead (author, 2026-10-09).
        private const string FailPrefix = "death: ";

        private static readonly int BaseIdle = Animator.StringToHash("Base Layer.idle");

        // EndgameWakeUp's clip planes (decompiled), put back only at its end.
        private const float WakeFar = 600f, WakeNear = 0.1f;

        // The main camera when the death started: the drag-away changes the
        // culling mask, the boss-fight wake-up the clip planes, and each
        // puts them back only at its end. Used once, dropped on a load.
        private static bool _haveCam;
        private static int _cullingMask;
        private static float _near, _far;

        /// DeathHooks: the game's own death is about to play.
        public static void Started()
        {
            try
            {
                Camera cam = Static(LocalPlayer(), "MainCam") as Camera;
                if (cam == null) return;
                _cullingMask = cam.cullingMask;
                _near = cam.nearClipPlane;
                _far = cam.farClipPlane;
                _haveCam = true;
            }
            catch (Exception) { }
        }

        /// A load finished: a new player, nothing of an earlier death.
        public static void Forget()
        {
            _haveCam = false;
        }

        /// End() could not end the death in place.
        public static bool Failed(string note)
        {
            return note != null && note.StartsWith(FailPrefix);
        }

        /// Every restore in place: the deaths the save does not hold, as a
        /// load gives them. Says what changed; "" when nothing did.
        public static string ForgetDeaths()
        {
            try
            {
                object stats = Static(LocalPlayer(), "Stats");
                if (stats == null) return "";
                object times = Get(stats, "DeadTimes");
                Set(stats, "doneDragScene", false);
                if (!(times is int) || (int)times == 0) return "";
                Set(stats, "DeadTimes", 0);
                return "death count " + times + " -> 0 (as a load)";
            }
            catch (Exception ex) { return "death count: failed (" + (ex.InnerException ?? ex).Message + ")"; }
        }

        /// Before a restore or a teleport: ends the game's death in
        /// progress. Says what it ended; "" when none was running.
        public static string End()
        {
            try
            {
                Type local = LocalPlayer();
                MonoBehaviour stats = Static(local, "Stats") as MonoBehaviour;
                if (stats == null) return "";
                string why = Running(local, stats);
                if (why.Length == 0) return "";

                List<string> notes = new List<string>();
                stats.StopAllCoroutines();
                for (int i = 0; i < DeathProgress.Cancelled.Length; i++) stats.CancelInvoke(DeathProgress.Cancelled[i]);
                MonoBehaviour hit = Get(stats, "hitReaction") as MonoBehaviour;
                if (hit != null)
                {
                    hit.CancelInvoke("disableControllerFreeze");
                    Call(hit, "disableControllerFreeze");
                }

                int destroyed = DestroyCutsceneObjects(local, stats);
                if (destroyed > 0) notes.Add("removed " + destroyed + " cutscene object(s)");

                Set(stats, "Dead", false);
                Controls(local);
                Body(local);
                Cameras(local);
                Hud(local, stats);
                DeathHooks.ClearBlood();

                // Whatever still shows the death means a step did not take:
                // the caller loads instead.
                string still = Running(local, stats);
                if (still.Length > 0) return FailPrefix + "still running after the end (" + still + ")";
                return "ended the game's death (" + why + (notes.Count > 0 ? "; " + string.Join(", ", notes.ToArray()) : "") + ")";
            }
            catch (Exception ex) { return FailPrefix + "end failed (" + (ex.InnerException ?? ex).Message + ")"; }
        }

        // What shows a death in progress; "" when none (Data/DeathProgress).
        private static string Running(Type local, MonoBehaviour stats)
        {
            object anim = Static(local, "AnimControl");
            object inv = Static(local, "Inventory");
            object cams = Static(SceneType(), "Cams");
            string pending = null;
            for (int i = 0; i < DeathProgress.Signs.Length && pending == null; i++)
                if (stats.IsInvoking(DeathProgress.Signs[i])) pending = DeathProgress.Signs[i];
            return DeathProgress.Reason(
                IsTrue(Get(stats, "Dead")),
                anim != null && IsTrue(Get(anim, "upsideDown")),
                Alive(Get(stats, "mutant1")) || Alive(Get(stats, "mutant2")),
                inv != null && Convert.ToString(Get(inv, "CurrentView")) == "Death",
                pending,
                (cams != null && (Active(Get(cams, "DeadCam")) || Active(Get(cams, "CaveDeadCam")))) ||
                    Active(Static(local, "PlayerDeadCam")));
        }

        private static bool IsTrue(object o)
        {
            return o is bool && (bool)o;
        }

        // The drag-away's two cannibals and the hanging rope (on the hips).
        private static int DestroyCutsceneObjects(Type local, object stats)
        {
            int n = 0;
            string[] mutants = { "mutant1", "mutant2" };
            for (int i = 0; i < mutants.Length; i++)
            {
                GameObject m = Get(stats, mutants[i]) as GameObject;
                if (m == null) continue;
                UnityEngine.Object.Destroy(m);
                Set(stats, mutants[i], null);
                n++;
            }
            object setup = Static(local, "ScriptSetup");
            Transform hips = setup != null ? Get(setup, "hipsJnt") as Transform : null;
            if (hips != null)
                for (int i = hips.childCount - 1; i >= 0; i--)
                {
                    Transform c = hips.GetChild(i);
                    if (!c.name.StartsWith("hangingPlayerRopeGo")) continue;
                    UnityEngine.Object.Destroy(c.gameObject);
                    n++;
                }
            return n;
        }

        // Movement, look and hands, as the game's wake-ups give them back.
        private static void Controls(Type local)
        {
            object inv = Static(local, "Inventory");
            if (inv != null)
            {
                Enable(inv, true);
                PropertyInfo view = inv.GetType().GetProperty("CurrentView", Inst);
                if (view != null && view.PropertyType.IsEnum && Convert.ToString(view.GetValue(inv, null)) != "World")
                    view.SetValue(inv, Enum.Parse(view.PropertyType, "World"), null);
            }
            object fp = Static(local, "FpCharacter");
            if (fp != null)
            {
                Enable(fp, true);
                Call(fp, "UnLockView");
                Set(fp, "CanJump", true);
            }
            object main = Static(local, "MainRotator");
            if (main != null)
            {
                Set(main, "rotationRange", new Vector2(0f, 999f));
                Enable(main, true);
            }
            object camRot = Static(local, "CamRotator");
            object minRange = fp != null ? Get(fp, "minCamRotationRange") : null;
            if (camRot != null)
            {
                if (minRange is float) Set(camRot, "rotationRange", new Vector2((float)minRange, 0f));
                Enable(camRot, true);
            }
            object head = Static(local, "CamFollowHead");
            if (head != null)
            {
                Set(head, "followAnim", false);
                Set(head, "lockYCam", false);
                Component c = head as Component;
                if (c != null) c.transform.localEulerAngles = Vector3.zero;
            }
            GameObject greeble = Static(local, "GreebleRoot") as GameObject;
            if (greeble != null) greeble.SetActive(true);
            object create = Static(local, "Create");
            Component grabber = create != null ? Get(create, "Grabber") as Component : null;
            if (grabber != null) grabber.gameObject.SetActive(true);
        }

        // The animator and the body's physics: out of the death, drag-away
        // and hanging states, the body layer straight to its idle (the
        // hanging drop played otherwise, the camera in the neck).
        private static void Body(Type local)
        {
            object anim = Static(local, "AnimControl");
            if (anim != null)
            {
                Enable(anim, true);
                string[] flags = { "injured", "useRootMotion", "lockGravity", "upsideDown" };
                for (int i = 0; i < flags.Length; i++) Set(anim, flags[i], false);
                Enable(Get(anim, "forcePos"), true);
                // Root motion with lockGravity keeps the body kinematic
                // (playerAnimatorControl.Update); the game's releases clear
                // lockGravity a frame before root motion, which frees it.
                // Both off at once skip that frame: freed here as the climb
                // exit does (bridge: kinematic, no gravity, not grounded).
                Rigidbody rb = Get(anim, "controller") as Rigidbody;
                if (rb != null)
                {
                    rb.freezeRotation = true;
                    rb.useGravity = true;
                    rb.isKinematic = false;
                }
            }
            object setup = Static(local, "ScriptSetup");
            if (setup != null) Enable(Get(setup, "forceLocalPos"), true);

            Animator an = Static(local, "Animator") as Animator;
            if (an != null)
            {
                Component zero = an.GetComponent("ForceLocalPosZero");
                Enable(zero, true);
                an.SetBool("deathBool", false);
                an.SetBool("dragAwayBool", false);
                an.SetBool("hangingBool", false);
                an.SetInteger("hangingInt", 0);
                an.SetInteger("knockBackInt", 0);
                if (an.layerCount > 4)
                {
                    an.SetLayerWeight(1, 1f);
                    an.SetLayerWeight(2, 0f);
                    an.SetLayerWeight(4, 1f);
                }
                if (an.HasState(0, BaseIdle)) an.CrossFadeInFixedTime(BaseIdle, 0f, 0, 0f);
            }

            object pm = setup != null ? Get(setup, "pmControl") : null;
            if (pm != null)
            {
                object vars = Get(pm, "FsmVariables");
                object hanging = vars != null ? Call(vars, "GetFsmBool", "hangingUpsideDown") : null;
                if (hanging != null) Set(hanging, "Value", false);
                // Out of the FSM's death state, as WakeInCave does.
                if (Convert.ToString(Get(pm, "ActiveStateName")) != "waitForInput") Call(pm, "SendEvent", "toResetPlayer");
            }
        }

        private static void Cameras(Type local)
        {
            object cams = Static(SceneType(), "Cams");
            if (cams != null)
            {
                string[] names = { "SleepCam", "DeadCam", "CaveDeadCam" };
                for (int i = 0; i < names.Length; i++)
                {
                    GameObject go = Get(cams, names[i]) as GameObject;
                    if (go != null && go.activeSelf) go.SetActive(false);
                }
            }
            GameObject own = Static(local, "PlayerDeadCam") as GameObject;
            if (own != null && own.activeSelf) own.SetActive(false);

            // Only what the chain set: the drag-away's mask, the boss
            // wake-up's clip planes - back to the values at the death's start.
            Camera cam = Static(local, "MainCam") as Camera;
            object stats = Static(local, "Stats");
            if (cam != null && _haveCam)
            {
                object dragMask = stats != null ? Get(stats, "dragAwayCullingMask") : null;
                if (dragMask is LayerMask && cam.cullingMask == ((LayerMask)dragMask).value) cam.cullingMask = _cullingMask;
                if (cam.farClipPlane == WakeFar && cam.nearClipPlane == WakeNear)
                {
                    cam.nearClipPlane = _near;
                    cam.farClipPlane = _far;
                }
            }
            _haveCam = false;
        }

        private static void Hud(Type local, object stats)
        {
            object hud = Static(SceneType(), "HudGui");
            if (hud != null) Call(hud, "ShowHud", true);
            object scene = Get(stats, "sceneInfo");
            if (scene != null) Call(scene, "EnableMusic");
        }

        // ------------------------------------------------------------------

        private static Type _local, _scene;

        private static Type LocalPlayer()
        {
            if (_local == null) _local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
            return _local;
        }

        private static Type SceneType()
        {
            if (_scene == null) _scene = GameBridge.FindGameType("TheForest.Utils.Scene");
            return _scene;
        }

        private static bool Alive(object o)
        {
            UnityEngine.Object u = o as UnityEngine.Object;
            return u != null;
        }

        private static bool Active(object o)
        {
            GameObject go = o as GameObject;
            return go != null && go.activeSelf;
        }

        private static void Enable(object o, bool on)
        {
            Behaviour b = o as Behaviour;
            if (b != null && b.enabled != on) b.enabled = on;
        }

        private static object Static(Type t, string name)
        {
            if (t == null) return null;
            FieldInfo f = t.GetField(name, Stat);
            if (f != null) return f.GetValue(null);
            PropertyInfo p = t.GetProperty(name, Stat);
            return p != null ? p.GetValue(null, null) : null;
        }

        private static object Get(object o, string name)
        {
            Type t = o.GetType();
            FieldInfo f = t.GetField(name, Inst);
            if (f != null) return f.GetValue(o);
            PropertyInfo p = t.GetProperty(name, Inst);
            return p != null && p.GetIndexParameters().Length == 0 ? p.GetValue(o, null) : null;
        }

        private static void Set(object o, string name, object value)
        {
            Type t = o.GetType();
            FieldInfo f = t.GetField(name, Inst);
            if (f != null) { f.SetValue(o, value); return; }
            PropertyInfo p = t.GetProperty(name, Inst);
            if (p != null && p.CanWrite) p.SetValue(o, value, null);
        }

        private static object Call(object o, string name, params object[] args)
        {
            Type[] types = new Type[args.Length];
            for (int i = 0; i < args.Length; i++) types[i] = args[i].GetType();
            MethodInfo m = o.GetType().GetMethod(name, Inst, null, types, null);
            return m != null ? m.Invoke(o, args) : null;
        }
    }
}
