using System;
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
    // made while on the rope keeps the player on it at the captured spot
    // (bridge), so the rope is entered before the restore runs.
    // ------------------------------------------------------------------
    public static class RopeClimb
    {
        private static FieldInfo _animControl;    // static LocalPlayer.AnimControl
        private static FieldInfo _specialActions; // static LocalPlayer.SpecialActions (GameObject)
        private static FieldInfo _onRope;         // playerAnimatorControl.onRope
        private static MethodInfo _exit;          // playerAnimatorControl.exitClimbMode()
        private static Type _ropeAction;          // PlayerClimbRopeAction
        private static FieldInfo _ropeRoot;       // PlayerClimbRopeAction._currentRopeRoot
        private static MethodInfo _enterTop;      // PlayerClimbRopeAction.enterClimbRopeTop(Transform)
        private static Type _climbTop;            // activateClimbTop
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

        /// Before a restore: the captured climb, or none. On the captured
        /// rope already = left alone; otherwise the current climb is ended
        /// and the captured rope entered from its top.
        public static string Prepare(string captured)
        {
            try
            {
                if (captured.Length == 0) return Leave();
                if (!Resolve()) return "rope: game types not found";
                if (OnRope() && Capture() == captured) return "";
                GameObject rope = GameObject.Find(captured);
                Component top = rope != null ? rope.GetComponentInChildren(_climbTop) : null;
                if (top == null) return "rope: '" + captured + "' not found";
                string left = Leave();
                Component action = Action();
                if (action == null) return "rope: no climb action on the player";
                _enterTop.Invoke(action, new object[] { top.transform });
                return (left.Length > 0 ? left + ", " : "") + "back on the rope (" + rope.name + ")";
            }
            catch (Exception ex) { return "rope: failed (" + (ex.InnerException ?? ex).Message + ")"; }
        }

        private static bool OnRope()
        {
            if (!Resolve()) return false;
            object anim = _animControl.GetValue(null) as UnityEngine.Object;
            return anim != null && (bool)_onRope.GetValue(anim);
        }

        private static Component Action()
        {
            GameObject go = _specialActions.GetValue(null) as GameObject;
            return go != null ? go.GetComponent(_ropeAction) : null;
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
            if (local == null || anim == null || _ropeAction == null || _climbTop == null) return false;
            _animControl = local.GetField("AnimControl", stat);
            _specialActions = local.GetField("SpecialActions", stat);
            _onRope = anim.GetField("onRope", inst);
            _exit = anim.GetMethod("exitClimbMode", inst, null, Type.EmptyTypes, null);
            _ropeRoot = _ropeAction.GetField("_currentRopeRoot", inst);
            MethodInfo enter = _ropeAction.GetMethod("enterClimbRopeTop", inst, null, new[] { typeof(Transform) }, null);
            if (_animControl == null || _specialActions == null || _onRope == null || _exit == null || _ropeRoot == null)
                return false;
            _enterTop = enter;
            return _enterTop != null;
        }
    }
}
