using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // A rope climb (cave entrances like cave 4's), put back as captured
    // (runner maks: a start state taken on the cave 4 rope shot him into
    // the air on every restart).
    //
    // The save does not hold the climb. On a rope the player's body is
    // kinematic and ignores the rock around the hole; a restore after
    // leaving the rope put a free body at the captured spot, inside the
    // rock, and physics threw it out at ~48 m/s (bridge, 2026-09-26). A
    // teleport did not end a climb either (`onRope` stayed set).
    //
    // WHAT (IL): the climb starts with activateClimbTop.Update sending
    // `enterClimbRopeTop(<its transform>)` to LocalPlayer.SpecialActions
    // (PlayerClimbRopeAction), which records `_currentRopeRoot` (the
    // rope's parent). The whole exit is playerAnimatorControl.exitClimbMode
    // (body back to physics, FSM bools, then `resetClimbRope`). A restore
    // made while on the rope keeps the player on the rope (bridge), so the
    // rope is entered before the restore runs.
    //
    // Entered from the BOTTOM, the height put back after the grab (T-0065,
    // bridge 2026-10-18): enterClimbRopeTop's climb-over-the-edge animation
    // slides the body down by root motion and stickToRope then pins it to
    // the top (Cave 10: captured at y 35, held at 51; near the bottom it
    // slid into the ground and stuck there). enterClimbRope keeps the
    // body's y every frame; its grab lifts it ~0.8 m, so HoldHeight sets
    // the captured spot again once the climb idles. Built Climbing Ropes
    // all share the path `RopeBuilt(Clone)`: the rope is picked by path
    // AND nearest the captured spot.
    //
    // `onRope` is not only ropes (IL, T-0242): cliff climbs, the cave-mouth
    // crawl (playerEnterCaveAction.doCave) and the keypad door, artifact,
    // Goodbye Timmy and drag-away cutscenes set it too. A rope climb is
    // `onRope` with the rope action's `_currentRopeRoot` set (both enters
    // set it, resetClimbRope clears it).
    // ------------------------------------------------------------------
    public static class RopeClimb
    {
        private static FieldInfo _animControl;    // static LocalPlayer.AnimControl
        private static FieldInfo _specialActions; // static LocalPlayer.SpecialActions (GameObject)
        private static FieldInfo _onRope;         // playerAnimatorControl.onRope
        private static Func<object, bool> _onRopeGet;   // the same, read without boxing (every frame)
        private static Func<object, bool> _enteringCaveGet; // playerAnimatorControl.enteringACave (null if missing)
        private static MethodInfo _exit;          // playerAnimatorControl.exitClimbMode()
        private static Type _ropeAction;          // PlayerClimbRopeAction
        private static FieldInfo _ropeRoot;       // PlayerClimbRopeAction._currentRopeRoot
        private static MethodInfo _enterTop;      // PlayerClimbRopeAction.enterClimbRopeTop(Transform)
        private static MethodInfo _enterBottom;   // PlayerClimbRopeAction.enterClimbRope(Transform)
        private static FieldInfo _climbIdleHash, _climbHash, _doingClimb; // the action's tag hashes, doingClimb
        private static FieldInfo _lpTransform, _lpAnimator; // static LocalPlayer.Transform / Animator
        private static Type _climbTop;            // activateClimbTop
        private static Type _climbBottom;         // activateClimb
        private static bool _resolved;

        /// The rope the player is on, as a scene path; "" when not climbing.
        public static string Capture()
        {
            try
            {
                if (!OnRope()) return "";
                Component action = Action();
                Transform root = action != null ? _ropeRoot.GetValue(action) as Transform : null;
                return root != null ? PathOf(root) : "";
            }
            catch (Exception) { return ""; }
        }

        /// Ends a climb in progress with the game's own exit. Says so; ""
        /// when the player was not on a rope.
        public static string Leave()
        {
            try
            {
                if (!OnRope()) return "";
                _exit.Invoke(_animControl.GetValue(null), null);
                return "left the rope";
            }
            catch (Exception ex) { return "rope: leave failed (" + (ex.InnerException ?? ex).Message + ")"; }
        }

        /// Before a restore: the captured climb, or none. `at` = the
        /// captured spot. On the captured rope already = left alone;
        /// otherwise the current climb is ended and the captured rope
        /// entered from the bottom (HoldHeight puts the height back).
        public static string Prepare(string captured, Vector3 at)
        {
            try
            {
                if (captured.Length == 0) return Leave();
                if (!Resolve()) return "rope: game types not found";
                Transform root = Pick(captured, at);
                if (root == null) return "rope: '" + captured + "' not found";
                Component action = Action();
                if (action == null) return "rope: no climb action on the player";
                if (OnRope() && _ropeRoot.GetValue(action) as Transform == root) return "";
                string left = Leave();
                Component bottom = _climbBottom != null ? root.GetComponentInChildren(_climbBottom, true) : null;
                if (bottom != null && _enterBottom != null)
                    _enterBottom.Invoke(action, new object[] { bottom.transform });
                else
                {
                    Component top = root.GetComponentInChildren(_climbTop, true);
                    if (top == null) return "rope: '" + captured + "' has no trigger";
                    _enterTop.Invoke(action, new object[] { top.transform });
                }
                return (left.Length > 0 ? left + ", " : "") + "back on the rope (" + root.name + ")";
            }
            catch (Exception ex) { return "rope: failed (" + (ex.InnerException ?? ex).Message + ")"; }
        }

        /// After the restore: once the climb idles (the grab's root motion
        /// done, at most 3 s), the player back at the captured spot. A rope
        /// the restore re-created (dismantled since the capture) is entered
        /// here. `done` gets the log text ("" when there was no rope).
        public static IEnumerator HoldHeight(string captured, Vector3 at, Action<string> done)
        {
            if (string.IsNullOrEmpty(captured) || !Resolve()) { if (done != null) done(""); yield break; }
            string note = IsOnRope() ? "" : Prepare(captured, at);
            float until = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < until && IsOnRope() && !Idle()) yield return null;
            string result;
            try
            {
                Transform player = _lpTransform != null ? _lpTransform.GetValue(null) as Transform : null;
                if (!IsOnRope() || player == null) result = "rope: not on the rope after the restore";
                else
                {
                    player.position = at;
                    result = "on the rope at y " + at.y.ToString("F1") + (Idle() ? "" : " (still grabbing after 3 s)");
                }
            }
            catch (Exception ex) { result = "rope: putting the height back failed (" + (ex.InnerException ?? ex).Message + ")"; }
            if (done != null) done(note.Length > 0 ? note + ", " + result : result);
        }

        // The climb's own idle / climbing state (stickToRope's test), with
        // doingClimb set (0-2.6 s after the entry).
        private static bool Idle()
        {
            Animator animator = _lpAnimator != null ? _lpAnimator.GetValue(null) as Animator : null;
            Component action = Action();
            if (animator == null || action == null || _climbIdleHash == null || _climbHash == null) return true;
            if (animator.IsInTransition(0)) return false;
            int tag = animator.GetCurrentAnimatorStateInfo(0).tagHash;
            bool climbing = tag == (int)_climbIdleHash.GetValue(action) || tag == (int)_climbHash.GetValue(action);
            return climbing && (_doingClimb == null || (bool)_doingClimb.GetValue(action));
        }

        /// The rope root at `captured` nearest `at` (built ropes share one
        /// path); null when none.
        private static Transform Pick(string captured, Vector3 at)
        {
            UnityEngine.Object[] tops = UnityEngine.Object.FindObjectsOfType(_climbTop);
            Transform best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < tops.Length; i++)
            {
                Component c = tops[i] as Component;
                Transform root = c != null ? c.transform.parent : null;
                if (root == null || PathOf(root) != captured) continue;
                Vector3 d = c.transform.position - at;
                d.y = 0f;
                if (d.sqrMagnitude < bestD) { bestD = d.sqrMagnitude; best = root; }
            }
            if (best != null) return best;
            GameObject found = GameObject.Find(captured);
            return found != null ? found.transform : null;
        }

        /// On a rope now (WorldEvents' rope-grab / rope-leave) - not a cliff
        /// climb, a cave mouth or a cutscene, which set `onRope` too.
        public static bool IsOnRope()
        {
            try
            {
                if (!OnRope()) return false;
                Component action = Action();
                return action != null && _ropeRoot.GetValue(action) as Transform != null;
            }
            catch (Exception) { return false; }
        }

        /// The game's cave-mouth crawl / climb is running
        /// (playerEnterCaveAction.doCave sets `enteringACave` for its whole
        /// length, entering and leaving). It snaps the player to the mouth's
        /// enter / exit spot the frame it sets the cave (bridge: 9 m at
        /// cave 6), which is not a teleport. The rope enters set the flag
        /// too and only resetClimbRope clears it, so a rope climb is not a
        /// cave mouth (a restore onto a rope must stay a placement).
        public static bool IsEnteringCave()
        {
            try
            {
                if (!Resolve() || _enteringCaveGet == null) return false;
                object anim = _animControl.GetValue(null) as UnityEngine.Object;
                return anim != null && _enteringCaveGet(anim) && !IsOnRope();
            }
            catch (Exception) { return false; }
        }

        private static bool OnRope()
        {
            if (!Resolve()) return false;
            object anim = _animControl.GetValue(null) as UnityEngine.Object;
            return anim != null && _onRopeGet(anim);
        }

        private static Component Action()
        {
            GameObject go = _specialActions.GetValue(null) as GameObject;
            return go != null ? go.GetComponent(_ropeAction) : null;
        }

        private static FieldInfo IntField(FieldInfo f, Type type)
        {
            return f != null && f.FieldType == type ? f : null;
        }

        private static string PathOf(Transform t)
        {
            string s = t.name;
            for (Transform p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
            return s;
        }

        private static bool Resolve()
        {
            if (_resolved) return _enterTop != null;
            _resolved = true;
            const BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
            Type anim = GameBridge.FindGameType("playerAnimatorControl");
            _ropeAction = GameBridge.FindGameType("TheForest.Player.Actions.PlayerClimbRopeAction");
            _climbTop = GameBridge.FindGameType("activateClimbTop");
            _climbBottom = GameBridge.FindGameType("activateClimb");
            if (local == null || anim == null || _ropeAction == null || _climbTop == null) return false;
            _animControl = local.GetField("AnimControl", stat);
            _specialActions = local.GetField("SpecialActions", stat);
            _onRope = anim.GetField("onRope", inst);
            _exit = anim.GetMethod("exitClimbMode", inst, null, Type.EmptyTypes, null);
            _ropeRoot = _ropeAction.GetField("_currentRopeRoot", inst);
            MethodInfo enter = _ropeAction.GetMethod("enterClimbRopeTop", inst, null, new[] { typeof(Transform) }, null);
            if (_animControl == null || _specialActions == null || _onRope == null || _exit == null || _ropeRoot == null)
                return false;
            _onRopeGet = FastField.Instance<bool>(_onRope);
            FieldInfo entering = anim.GetField("enteringACave", inst);
            if (entering != null && entering.FieldType == typeof(bool)) _enteringCaveGet = FastField.Instance<bool>(entering);
            _enterTop = enter;
            _enterBottom = _ropeAction.GetMethod("enterClimbRope", inst, null, new[] { typeof(Transform) }, null);
            _climbIdleHash = IntField(_ropeAction.GetField("_climbIdleHash", inst), typeof(int));
            _climbHash = IntField(_ropeAction.GetField("_climbHash", inst), typeof(int));
            _doingClimb = IntField(_ropeAction.GetField("doingClimb", inst), typeof(bool));
            _lpTransform = local.GetField("Transform", stat);
            _lpAnimator = local.GetField("Animator", stat);
            return _enterTop != null;
        }
    }
}
