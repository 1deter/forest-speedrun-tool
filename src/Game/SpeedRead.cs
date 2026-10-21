using System;
using System.Reflection;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The player's run and swim speed, for run mode's speed watch.
    // LocalPlayer.FpCharacter (static) -> FirstPersonCharacter.runSpeed /
    // swimmingSpeed (floats; the debug console's _speedyrun writes 60 / 20
    // every frame, vanilla is 13.5 / 3.75: docs/game-notes.md *timeScale*).
    // The FpCharacter object is re-read each call (it changes on a load).
    // Also its movement constants walkSpeed / strafeSpeed / crouchSpeed /
    // jumpHeight / gravity (floats; game-notes *The other clocks and the
    // movement constants*, T-0414 F3).
    // ------------------------------------------------------------------
    public static class SpeedRead
    {
        private static Func<object> _fp;
        private static Func<object, float> _run, _swim;
        private static Func<object, float> _walk, _strafe, _crouch, _jump, _gravity;
        private static bool _resolved, _ok, _moveOk;

        /// True once the members were looked up and a game member is missing (the caller logs it once).
        public static bool Missing { get { return _resolved && !_ok; } }

        /// False when there is no player yet or a member is missing.
        public static bool TryRead(out float run, out float swim)
        {
            run = 0f; swim = 0f;
            try
            {
                if (!Resolve()) return false;
                object fp = _fp();
                if (fp == null) return false;
                run = _run(fp);
                swim = _swim(fp);
                return true;
            }
            catch (Exception) { return false; }
        }

        /// True once the members were looked up and a movement constant is missing (the caller logs it once).
        public static bool MovementMissing { get { return _resolved && !_moveOk; } }

        /// The movement constants; false when there is no player yet or a member is missing.
        public static bool TryReadMovement(out float walk, out float strafe, out float crouch, out float jump, out float gravity)
        {
            walk = 0f; strafe = 0f; crouch = 0f; jump = 0f; gravity = 0f;
            try
            {
                Resolve();
                if (!_moveOk) return false;
                object fp = _fp();
                if (fp == null) return false;
                walk = _walk(fp);
                strafe = _strafe(fp);
                crouch = _crouch(fp);
                jump = _jump(fp);
                gravity = _gravity(fp);
                return true;
            }
            catch (Exception) { return false; }
        }

        private static Func<object, float> Float(Type t, string name)
        {
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return f != null && f.FieldType == typeof(float) ? FastField.Instance<float>(f) : null;
        }

        private static bool Resolve()
        {
            if (_resolved) return _ok;
            const BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
            Type fp = GameBridge.FindGameType("FirstPersonCharacter");
            if (local == null || fp == null) return false;   // not loaded yet: try again later
            _resolved = true;
            FieldInfo f = local.GetField("FpCharacter", stat);
            FieldInfo r = fp.GetField("runSpeed", inst);
            FieldInfo s = fp.GetField("swimmingSpeed", inst);
            if (f != null && r != null && s != null && r.FieldType == typeof(float) && s.FieldType == typeof(float))
            {
                _fp = FastField.Static<object>(f);
                _run = FastField.Instance<float>(r);
                _swim = FastField.Instance<float>(s);
                _ok = _fp != null && _run != null && _swim != null;
            }
            if (_fp != null)
            {
                _walk = Float(fp, "walkSpeed");
                _strafe = Float(fp, "strafeSpeed");
                _crouch = Float(fp, "crouchSpeed");
                _jump = Float(fp, "jumpHeight");
                _gravity = Float(fp, "gravity");
                _moveOk = _walk != null && _strafe != null && _crouch != null && _jump != null && _gravity != null;
            }
            return _ok;
        }
    }
}
