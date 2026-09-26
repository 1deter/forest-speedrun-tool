using System;
using System.Reflection;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Crouched or standing, put back as captured (runner sxczurass: a
    // Quick / Full load taken standing left the player crouched).
    //
    // The save does not hold the stance. With the game's toggle crouch
    // (PlayerPreferences.UseCrouchToggle) it lives in
    // FirstPersonCharacter.crouch / crouching until the button is pressed
    // again, so it outlived every restore (bridge, 2026-09-26). With hold
    // crouch the button decides each frame and nothing is kept.
    //
    // WHAT (IL, FirstPersonCharacter.Update): standing up is the game's
    // own disableToggledCrouch (standingUp, crouch off, DisableCrouch
    // coroutine: capsule, animator, FSM bool, crouch layers). Crouching
    // is `crouch = true`: the next Update starts EnableCrouch when
    // grounded, and with hold crouch stands again unless the button is
    // held - as in a run.
    // ------------------------------------------------------------------
    public static class Stance
    {
        public const string Crouched = "crouched";
        public const string Standing = "standing";

        private static FieldInfo _fpCharacter;   // static LocalPlayer.FpCharacter
        private static FieldInfo _crouch;        // FirstPersonCharacter.crouch
        private static FieldInfo _crouching;     // FirstPersonCharacter.crouching
        private static MethodInfo _standUp;      // FirstPersonCharacter.disableToggledCrouch()
        private static bool _resolved;

        /// The stance now; "" when there is no player.
        public static string Capture()
        {
            try
            {
                object fp = Player();
                if (fp == null) return "";
                return IsCrouched(fp) ? Crouched : Standing;
            }
            catch (Exception) { return ""; }
        }

        /// Puts back the captured stance. Says what it did; "" when it
        /// already matched or the file has no stance (before v0.24.102).
        public static string Apply(string captured)
        {
            if (captured != Crouched && captured != Standing) return "";
            try
            {
                object fp = Player();
                if (fp == null) return "";
                bool now = IsCrouched(fp);
                if (captured == Standing && now)
                {
                    _standUp.Invoke(fp, null);
                    return "stood up (captured standing)";
                }
                if (captured == Crouched && !now)
                {
                    _crouch.SetValue(fp, true);
                    return "crouched (captured crouched)";
                }
                return "";
            }
            catch (Exception ex)
            {
                return "stance: failed (" + (ex.InnerException ?? ex).Message + ")";
            }
        }

        private static bool IsCrouched(object fp)
        {
            return (bool)_crouch.GetValue(fp) || (bool)_crouching.GetValue(fp);
        }

        private static object Player()
        {
            if (!Resolve()) return null;
            return _fpCharacter.GetValue(null);
        }

        private static bool Resolve()
        {
            if (_resolved) return _standUp != null;
            _resolved = true;
            const BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
            Type fp = GameBridge.FindGameType("FirstPersonCharacter");
            if (local == null || fp == null) return false;
            _fpCharacter = local.GetField("FpCharacter", stat);
            _crouch = fp.GetField("crouch", inst);
            _crouching = fp.GetField("crouching", inst);
            MethodInfo up = fp.GetMethod("disableToggledCrouch", inst, null, Type.EmptyTypes, null);
            if (_fpCharacter == null || _crouch == null || _crouching == null) return false;
            _standUp = up;
            return _standUp != null;
        }
    }
}
