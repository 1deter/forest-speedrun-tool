using System;
using System.Reflection;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Cliff climb, sled, hang glider and zipline: modes that hold the body
    // the way a rope climb does (author, 2026-09-26: "other ride / climb
    // modes in savestates"; gotcha 47). A teleport or an in-place restore
    // moved the body and left the mode running - the next frames drove a
    // body that was somewhere else (gotcha 37: left alone is not stopped).
    // Wall climbs share the rope's `onRope` / exitClimbMode and are
    // RopeClimb's.
    //
    // WHAT (IL, v0.24.195): each mode ended with the game's own exit before
    // a Go / Restart / Quick load moves the player:
    //   cliff climb  AnimControl.cliffClimb        -> resetCliffClimb()
    //                (SpecialActions "exitClimbCliffGround")
    //   sled         AnimControl.doSledPushMode     -> resetPushSled()
    //                (SpecialActions "forceExitSled")
    //   glider       AnimControl.flyingGlider       -> PlayerHangGliderAction
    //                .StopFlyingGlider() (the glider stays in hand)
    //   zipline      playerZipLineAction._onZipLine -> ExitZipLine()
    // Not put back after a restore (a capture on one restores standing /
    // falling there; the capture's log line says so). Not seen live yet -
    // no zipline, sled or glider in the test slots: each step acts only
    // when its flag is set and logs what it did.
    // ------------------------------------------------------------------
    public static class RideModes
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static bool _resolved;
        private static FieldInfo _animControl, _specialActions;
        private static FieldInfo _cliff, _sled, _glider, _onZip;
        private static MethodInfo _resetCliff, _resetSled, _stopGlider, _exitZip;
        private static Type _gliderAction, _zipAction;

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
            Type anim = GameBridge.FindGameType("playerAnimatorControl");
            _gliderAction = GameBridge.FindGameType("PlayerHangGliderAction");
            _zipAction = GameBridge.FindGameType("playerZipLineAction");
            if (local != null)
            {
                _animControl = local.GetField("AnimControl", Any);
                _specialActions = local.GetField("SpecialActions", Any);
            }
            if (anim != null)
            {
                _cliff = anim.GetField("cliffClimb", Any);
                _sled = anim.GetField("doSledPushMode", Any);
                _glider = anim.GetField("flyingGlider", Any);
                _resetCliff = anim.GetMethod("resetCliffClimb", Any, null, Type.EmptyTypes, null);
                _resetSled = anim.GetMethod("resetPushSled", Any, null, Type.EmptyTypes, null);
            }
            if (_gliderAction != null) _stopGlider = _gliderAction.GetMethod("StopFlyingGlider", Any, null, Type.EmptyTypes, null);
            if (_zipAction != null)
            {
                _onZip = _zipAction.GetField("_onZipLine", Any);
                _exitZip = _zipAction.GetMethod("ExitZipLine", Any, null, Type.EmptyTypes, null);
            }
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

        /// Ends the mode in flight with the game's own exit. Says what it
        /// did; "" when the player was in none.
        public static string Leave()
        {
            try
            {
                Resolve();
                object anim = Anim();
                if (Flag(_cliff, anim) && _resetCliff != null) { _resetCliff.Invoke(anim, null); return "left the cliff climb"; }
                if (Flag(_sled, anim) && _resetSled != null) { _resetSled.Invoke(anim, null); return "let go of the sled"; }
                if (Flag(_glider, anim))
                {
                    Component g = Action(_gliderAction);
                    if (g != null && _stopGlider != null) { _stopGlider.Invoke(g, null); return "stopped gliding"; }
                }
                Component zip = Action(_zipAction);
                if (Flag(_onZip, zip) && _exitZip != null) { _exitZip.Invoke(zip, null); return "left the zipline"; }
            }
            catch (Exception ex) { return "ending a ride / climb failed: " + (ex.InnerException ?? ex).Message; }
            return "";
        }
    }
}
