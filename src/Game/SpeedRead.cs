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
    // ------------------------------------------------------------------
    public static class SpeedRead
    {
        private static Func<object> _fp;
        private static Func<object, float> _run, _swim;
        private static bool _resolved, _ok;

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
            return _ok;
        }
    }
}
