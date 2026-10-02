using System;
using System.Collections;
using System.Reflection;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Cliff climb, sled, hang glider and zipline: modes that hold the body
    // the way a rope climb does (author, 2026-09-26: "other ride / climb
    // modes in savestates"; gotcha 47). Ended before a Go / Restart / Quick
    // load moves the player (v0.24.195; gotcha 37: left alone is not
    // stopped), and since v0.24.201 put back after a restore of a capture
    // taken on one (the `ride` header, Data/RideState), as RopeClimb does
    // for cave ropes. Wall climbs share the rope's `onRope` / exitClimbMode
    // and are RopeClimb's.
    //
    // WHAT (IL + bridge, v0.24.201, all four built in a Creative slot):
    //   zipline  playerZipLineAction._onZipLine; EnterZipLine(the line's
    //            EnterTrigger) puts the player at the trigger and starts
    //            StickToZipLine, which waits for the grab animation, then
    //            sets the body to `_onRopeAttachPos` once and slides along
    //            the trigger's forward. Put back: enter, attach point = the
    //            captured spot, the animator straight to the hanging state,
    //            the captured velocity once the slide runs. ExitZipLine at
    //            over 10 m/s pushes on for a second (PreserveExitVelocity):
    //            Leave stops the body first.
    //   sled     AnimControl.doSledPushMode; pushing parents the sled under
    //            the player (connectRigidBody: local (-1.32, -1.3, 3.155),
    //            its Rigidbody destroyed), so the save holds that local
    //            position: a capture while pushing restored the sled near
    //            the world origin, with no Rigidbody. Put back: the sled to
    //            its captured place, a Rigidbody as exitPushSled adds, then
    //            activateSledPush.enableSled (the game's grab).
    //   glider   AnimControl.holdingGlider / flyingGlider; the glider in the
    //            hands is not saved - the game's save (and the capture,
    //            SavestateBridge) drops it into the world first. Put back:
    //            that dropped glider picked up (activateHangGlider.
    //            SendPickupGlider), FlyWithGlider, the captured velocity.
    //   cliff    AnimControl.cliffClimb; the climbing axe's scan sends
    //            setEnterClimbPos(hit) + enterClimbCliff(axe) and sets
    //            AnimControl.cliffEnterNormal / cliffEnterPos. Put back the
    //            same way, once the axe is back in the hands.
    // ------------------------------------------------------------------
    public static class RideModes
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // connectRigidBody's local place for the sled under the player.
        private static readonly Vector3 SledPushLocal = new Vector3(-1.32f, -1.3f, 3.155f);

        private static bool _resolved;
        private static FieldInfo _animControl, _specialActions, _rigidbody, _animator;
        private static FieldInfo _cliff, _sled, _glider, _holding, _cliffNormal, _cliffPos;
        private static FieldInfo _onZip, _zipLine, _zipAttach, _zipFixed, _zipIdle;
        private static FieldInfo _sledRoot, _wasFlying;
        private static MethodInfo _resetCliff, _resetSled, _stopGlider, _dropGlider, _flyGlider, _exitZip, _enterZip;
        private static MethodInfo _enableSled, _pickupGlider, _setClimbPos, _enterCliff;
        private static Type _gliderAction, _zipAction, _sledAction, _cliffAction;
        private static Type _zipTrigger, _sledTrigger, _gliderPickup, _cliffAxe;

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
            Type anim = GameBridge.FindGameType("playerAnimatorControl");
            _gliderAction = GameBridge.FindGameType("PlayerHangGliderAction");
            _zipAction = GameBridge.FindGameType("playerZipLineAction");
            _sledAction = GameBridge.FindGameType("TheForest.Player.Actions.PlayerPushSledAction");
            _cliffAction = GameBridge.FindGameType("TheForest.Player.Actions.PlayerClimbCliffAction");
            _zipTrigger = GameBridge.FindGameType("activateZipLine");
            _sledTrigger = GameBridge.FindGameType("activateSledPush");
            _gliderPickup = GameBridge.FindGameType("activateHangGlider");
            _cliffAxe = GameBridge.FindGameType("activateCliffClimb");
            if (local != null)
            {
                _animControl = local.GetField("AnimControl", Any);
                _specialActions = local.GetField("SpecialActions", Any);
                _rigidbody = local.GetField("Rigidbody", Any);
                _animator = local.GetField("Animator", Any);
            }
            if (anim != null)
            {
                _cliff = anim.GetField("cliffClimb", Any);
                _sled = anim.GetField("doSledPushMode", Any);
                _glider = anim.GetField("flyingGlider", Any);
                _holding = anim.GetField("holdingGlider", Any);
                _cliffNormal = anim.GetField("cliffEnterNormal", Any);
                _cliffPos = anim.GetField("cliffEnterPos", Any);
                _resetCliff = anim.GetMethod("resetCliffClimb", Any, null, Type.EmptyTypes, null);
                _resetSled = anim.GetMethod("resetPushSled", Any, null, Type.EmptyTypes, null);
            }
            if (_gliderAction != null)
            {
                _stopGlider = _gliderAction.GetMethod("StopFlyingGlider", Any, null, Type.EmptyTypes, null);
                _dropGlider = _gliderAction.GetMethod("DropGlider", Any, null, new[] { typeof(bool) }, null);
                _flyGlider = _gliderAction.GetMethod("FlyWithGlider", Any, null, Type.EmptyTypes, null);
                _wasFlying = _gliderAction.GetField("wasFlying", Any);
            }
            if (_zipAction != null)
            {
                _onZip = _zipAction.GetField("_onZipLine", Any);
                _zipLine = _zipAction.GetField("_currentZipLine", Any);
                _zipAttach = _zipAction.GetField("_onRopeAttachPos", Any);
                _zipFixed = _zipAction.GetField("_fixPlayerPosition", Any);
                _zipIdle = _zipAction.GetField("_zipIdleHash", Any);
                _exitZip = _zipAction.GetMethod("ExitZipLine", Any, null, Type.EmptyTypes, null);
                _enterZip = _zipAction.GetMethod("EnterZipLine", Any, null, new[] { typeof(Transform) }, null);
            }
            if (_sledAction != null) _sledRoot = _sledAction.GetField("currentSledRoot", Any);
            if (_cliffAction != null)
            {
                _setClimbPos = _cliffAction.GetMethod("setEnterClimbPos", Any, null, new[] { typeof(Vector3) }, null);
                _enterCliff = _cliffAction.GetMethod("enterClimbCliff", Any, null, new[] { typeof(Transform) }, null);
            }
            if (_sledTrigger != null) _enableSled = _sledTrigger.GetMethod("enableSled", Any, null, Type.EmptyTypes, null);
            if (_gliderPickup != null) _pickupGlider = _gliderPickup.GetMethod("SendPickupGlider", Any, null, Type.EmptyTypes, null);
        }

        private static object Anim()
        {
            object a = _animControl != null ? _animControl.GetValue(null) : null;
            return (a as UnityEngine.Object) != null ? a : null;
        }

        private static Component Action(Type t)
        {
            if (t == null || _specialActions == null) return null;
            GameObject go = _specialActions.GetValue(null) as GameObject;
            if (go == null) return null;
            Component[] found = go.GetComponentsInChildren(t, true);
            return found != null && found.Length > 0 ? found[0] : null;
        }

        private static Rigidbody Body()
        {
            return _rigidbody != null ? _rigidbody.GetValue(null) as Rigidbody : null;
        }

        private static bool Flag(FieldInfo f, object o)
        {
            try { return f != null && o != null && (bool)f.GetValue(o); }
            catch (Exception) { return false; }
        }

        /// The mode the player is in now: "cliff climb", "sled", "glider",
        /// "zipline", or "".
        public static string Current()
        {
            try
            {
                Resolve();
                object anim = Anim();
                if (Flag(_cliff, anim)) return "cliff climb";
                if (Flag(_sled, anim)) return "sled";
                if (Flag(_glider, anim)) return "glider";
                if (Flag(_onZip, Action(_zipAction))) return "zipline";
            }
            catch (Exception) { }
            return "";
        }

        /// The ride now, as a savestate's `ride` value; "" for none. Read
        /// before the capture starts (its frames move the player on).
        public static string Capture(Vector3 at)
        {
            try
            {
                Resolve();
                object anim = Anim();
                Rigidbody body = Body();
                Vector3 velocity = body != null ? body.velocity : Vector3.zero;
                if (Flag(_cliff, anim))
                {
                    Vector3 normal = _cliffNormal != null ? (Vector3)_cliffNormal.GetValue(anim) : Vector3.zero;
                    return new RideState(RideState.Cliff, at, normal).Write();
                }
                if (Flag(_sled, anim))
                {
                    Component sled = Action(_sledAction);
                    Transform root = sled != null && _sledRoot != null ? _sledRoot.GetValue(sled) as Transform : null;
                    if (root != null) return new RideState(RideState.Sled, root.position, root.eulerAngles).Write();
                }
                if (Flag(_holding, anim))
                    return new RideState(Flag(_glider, anim) ? RideState.Glider : RideState.GliderHeld, at, velocity).Write();
                Component zip = Action(_zipAction);
                if (Flag(_onZip, zip))
                {
                    Transform line = _zipLine != null ? _zipLine.GetValue(zip) as Transform : null;
                    BoxCollider box = line != null ? line.GetComponent<BoxCollider>() : null;
                    if (box != null) return new RideState(RideState.Zipline, box.bounds.center, velocity).Write();
                }
            }
            catch (Exception) { }
            return "";
        }

        /// Ends the mode in flight with the game's own exit. Says what it
        /// did; "" when the player was in none. `dropGlider` (before a
        /// restore): a glider in the hands goes into the world, as the
        /// game's save does - the restore then gives back the capture's.
        public static string Leave(bool dropGlider = false)
        {
            try
            {
                Resolve();
                object anim = Anim();
                if (Flag(_cliff, anim) && _resetCliff != null) { _resetCliff.Invoke(anim, null); return "left the cliff climb"; }
                if (Flag(_sled, anim) && _resetSled != null) { _resetSled.Invoke(anim, null); return "let go of the sled"; }
                if (Flag(_holding, anim))
                {
                    Component g = Action(_gliderAction);
                    if (g != null && dropGlider && _dropGlider != null)
                    {
                        bool flying = Flag(_glider, anim);
                        _dropGlider.Invoke(g, new object[] { false });
                        return flying ? "stopped gliding, glider dropped" : "glider dropped";
                    }
                    if (g != null && Flag(_glider, anim) && _stopGlider != null) { _stopGlider.Invoke(g, null); return "stopped gliding"; }
                }
                Component zip = Action(_zipAction);
                if (Flag(_onZip, zip) && _exitZip != null)
                {
                    // Over 10 m/s the exit pushes on for a second.
                    Rigidbody body = Body();
                    if (body != null) body.velocity = Vector3.zero;
                    _exitZip.Invoke(zip, null);
                    return "left the zipline";
                }
            }
            catch (Exception ex) { return "ending a ride / climb failed: " + (ex.InnerException ?? ex).Message; }
            return "";
        }

        // ------------------------------------------------------------------
        // The capture: the game's save drops a held glider into the world
        // (SavestateBridge.Capture does the same). Dropped and taken back on
        // the frame of the serialization, so a capture in flight flies on.

        public sealed class HeldGlider
        {
            public bool Flying;
            public Vector3 Velocity;
            public Vector3 At;
        }

        /// Drops a held glider for the save; null when none is held.
        public static HeldGlider DropForSave()
        {
            try
            {
                Resolve();
                object anim = Anim();
                if (!Flag(_holding, anim)) return null;
                Component g = Action(_gliderAction);
                if (g == null || _dropGlider == null) return null;
                Rigidbody body = Body();
                HeldGlider held = new HeldGlider();
                held.Flying = Flag(_glider, anim);
                held.Velocity = body != null ? body.velocity : Vector3.zero;
                held.At = body != null ? body.position : Vector3.zero;
                _dropGlider.Invoke(g, new object[] { false });
                return held;
            }
            catch (Exception) { return null; }
        }

        /// Takes the glider dropped for the save back. Says what it did.
        public static string TakeBack(HeldGlider held)
        {
            if (held == null) return "";
            return Glider(held.At, held.Velocity, held.Flying);
        }

        // ------------------------------------------------------------------
        // PUT BACK after a restore.

        /// Puts the captured ride back; `at` is the captured spot. A
        /// coroutine (a zipline's speed waits for its slide to start, a
        /// sled's grab takes a second); `done` gets the log text.
        public static IEnumerator PutBack(string ride, Vector3 at, Action<string> done)
        {
            RideState s;
            if (!RideState.TryParse(ride, out s)) { if (done != null) done(""); yield break; }
            string note;
            try
            {
                Resolve();
                switch (s.Kind)
                {
                    case RideState.Zipline: note = Zipline(s, at); break;
                    case RideState.Sled: note = Sled(s); break;
                    case RideState.Glider: note = Glider(at, s.B, true); break;
                    case RideState.GliderHeld: note = Glider(at, s.B, false); break;
                    case RideState.Cliff: note = Cliff(s, at); break;
                    default: note = "ride '" + s.Kind + "' unknown to this version"; break;
                }
            }
            catch (Exception ex) { note = s.Kind + ": failed (" + (ex.InnerException ?? ex).Message + ")"; }

            // The zipline's slide starts once the grab animation is done;
            // until then it holds the body still.
            if (s.Kind == RideState.Zipline && note.StartsWith("back on the zipline"))
            {
                Component zip = Action(_zipAction);
                float until = Time.realtimeSinceStartup + 2f;
                while (zip != null && Flag(_onZip, zip) && !Flag(_zipFixed, zip) && Time.realtimeSinceStartup < until)
                    yield return null;
                yield return new WaitForFixedUpdate();
                Rigidbody body = Body();
                if (zip != null && Flag(_onZip, zip) && body != null)
                {
                    body.velocity = s.B;
                    note += ", at " + s.B.magnitude.ToString("F1") + " m/s";
                }
                else note += " - but it let go before the slide (" + (zip != null && Flag(_onZip, zip) ? "still grabbing" : "off the line") + ")";
            }
            if (done != null) done(note);
        }

        private static string Zipline(RideState s, Vector3 at)
        {
            Component zip = Action(_zipAction);
            if (zip == null || _enterZip == null || _zipAttach == null) return "zipline: game types not found";
            Transform line = Nearest(_zipTrigger, s.A, 1.5f, true);
            if (line == null) return "zipline: no line at " + Vec(s.A);
            if (Flag(_onZip, zip)) Leave();
            _enterZip.Invoke(zip, new object[] { line });
            // StickToZipLine ran to its first yield inside EnterZipLine: it
            // has put the body at the trigger and noted that spot as the
            // attach point. The captured spot instead (on the line).
            _zipAttach.SetValue(zip, at);
            GameObject player = Body() != null ? Body().gameObject : null;
            if (player != null) player.transform.position = at;
            // Straight to hanging: the grab animation is the start of a ride.
            Animator animator = _animator != null ? _animator.GetValue(null) as Animator : null;
            if (animator != null && _zipIdle != null) animator.Play((int)_zipIdle.GetValue(zip), 0, 0f);
            return "back on the zipline (" + Vec(s.A) + ")";
        }

        private static string Sled(RideState s)
        {
            if (_enableSled == null) return "sled: game types not found";
            // The sled nearest its captured place, or one a capture while
            // pushing put at the push's local offset (near the origin).
            Transform trigger = NearestRoot(_sledTrigger, s.A, 4f);
            if (trigger == null) trigger = NearestRoot(_sledTrigger, SledPushLocal, 1f);
            if (trigger == null) return "sled: none at " + Vec(s.A);
            Transform root = trigger.root;
            root.position = s.A;
            root.eulerAngles = s.B;
            // What exitPushSled gives a sled let go of; enableSled needs it.
            Rigidbody rb = root.GetComponent<Rigidbody>();
            if (rb == null) { rb = root.gameObject.AddComponent<Rigidbody>(); if (rb != null) rb.mass = 110f; }
            root.gameObject.layer = 28;
            Component push = trigger.GetComponent(_sledTrigger);
            object anim = Anim();
            if (Flag(_sled, anim) && _resetSled != null) _resetSled.Invoke(anim, null);
            _enableSled.Invoke(push, null);
            return "back on the sled (" + Vec(s.A) + ")";
        }

        private static string Glider(Vector3 at, Vector3 velocity, bool fly)
        {
            object anim = Anim();
            Component g = Action(_gliderAction);
            if (g == null || _pickupGlider == null) return "glider: game types not found";
            if (!Flag(_holding, anim))
            {
                // The capture reads `at` first and drops the glider on the
                // frame it serializes, a second of flight later (~10-15 m
                // on): the nearest within 40 m.
                Transform pickup = Nearest(_gliderPickup, at, 40f, false);
                if (pickup == null) return "glider: no dropped glider near " + Vec(at);
                _pickupGlider.Invoke(pickup.GetComponent(_gliderPickup), null);
            }
            if (!fly) return "glider back in the hands";
            if (!Flag(_glider, anim) && _flyGlider != null) _flyGlider.Invoke(g, null);
            if (_wasFlying != null) _wasFlying.SetValue(g, true);
            Rigidbody body = Body();
            if (body != null) body.velocity = velocity;
            return "gliding again at " + velocity.magnitude.ToString("F1") + " m/s";
        }

        private static string Cliff(RideState s, Vector3 at)
        {
            Component climb = Action(_cliffAction);
            if (climb == null || _setClimbPos == null || _enterCliff == null) return "cliff: game types not found";
            // The climbing axe's own component, under the player's hands.
            GameObject player = Body() != null ? Body().gameObject : null;
            Component axe = player != null && _cliffAxe != null ? player.GetComponentInChildren(_cliffAxe, true) : null;
            if (axe == null) return "cliff: no climbing axe on the player";
            object anim = Anim();
            if (Flag(_cliff, anim) && _resetCliff != null) _resetCliff.Invoke(anim, null);
            // enterClimbCliff puts the body at the entry point, one step
            // back along its facing: the entry = the captured spot + that.
            Vector3 entry = at + player.transform.forward;
            _setClimbPos.Invoke(climb, new object[] { entry });
            _enterCliff.Invoke(climb, new object[] { axe.transform });
            if (_cliffNormal != null) _cliffNormal.SetValue(anim, s.B);
            if (_cliffPos != null) _cliffPos.SetValue(anim, entry);
            return Flag(_cliff, anim) ? "back on the cliff" : "cliff: the climb did not start";
        }

        /// The component of `type` whose trigger centre (box, else its
        /// transform) is nearest `at`, within `radius`.
        private static Transform Nearest(Type type, Vector3 at, float radius, bool boxCentre)
        {
            if (type == null) return null;
            UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(type);
            Transform best = null;
            float bestD = radius;
            for (int i = 0; i < all.Length; i++)
            {
                Component c = all[i] as Component;
                if (c == null) continue;
                Vector3 p = c.transform.position;
                if (boxCentre)
                {
                    BoxCollider box = c.GetComponent<BoxCollider>();
                    if (box != null) p = box.bounds.center;
                }
                float d = Vector3.Distance(p, at);
                if (d <= bestD) { bestD = d; best = c.transform; }
            }
            return best;
        }

        /// As Nearest, by the object's root position.
        private static Transform NearestRoot(Type type, Vector3 at, float radius)
        {
            if (type == null) return null;
            UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(type);
            Transform best = null;
            float bestD = radius;
            for (int i = 0; i < all.Length; i++)
            {
                Component c = all[i] as Component;
                if (c == null) continue;
                float d = Vector3.Distance(c.transform.root.position, at);
                if (d <= bestD) { bestD = d; best = c.transform; }
            }
            return best;
        }

        private static string Vec(Vector3 v)
        {
            return v.x.ToString("F1") + ", " + v.y.ToString("F1") + ", " + v.z.ToString("F1");
        }
    }
}
