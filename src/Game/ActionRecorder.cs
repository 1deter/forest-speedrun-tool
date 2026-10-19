using System.Collections.Generic;
using BepInEx.Logging;
using ForestOverlay.Data;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The recorder's input source (T-0344, build plan tasks/notes/T-0344.md):
    // which of the game's actions are held, read through GameActions.Held
    // (the action whatever key it is bound to, plus the bridge's injected
    // input and a TAS replay; no patch). GameActions binds only when asked,
    // so Names calls Ensure. Never through InputInject / TasInput's hooks:
    // those exist for the bridge / TAS and a second reader would fight a TAS
    // recording.
    //
    // The ends: every action but the look axes (Mouse X / Mouse Y - the
    // look is the run's `l|` track); an axis action with a negative default
    // binding as two ends (Data/InputActionTable.Ends). Read allocates
    // nothing: at most two Held calls per action a frame, only while a run
    // records.
    // ------------------------------------------------------------------
    public static class ActionRecorder
    {
        private static readonly string[] Skip = { "Mouse X", "Mouse Y" };
        private static readonly string[] None = new string[0];

        private static InputActionTable _for;
        private static string[] _names = new string[0];
        private static int[] _actions = new int[0], _signs = new int[0];

        /// The ends, or an empty array while the game's actions are not
        /// readable yet (asked again next frame).
        public static string[] Names(ManualLogSource log)
        {
            GameActions.Ensure(log);
            InputActionTable table = GameActions.Table;
            if (table == null || !GameActions.Ready) return None;
            if (!ReferenceEquals(table, _for))
            {
                List<int> a = new List<int>(), s = new List<int>();
                _names = table.Ends(Skip, a, s);
                _actions = a.ToArray();
                _signs = s.ToArray();
                _for = table;
                log.LogInfo("ActionRecorder: " + _names.Length + " action end(s) recorded per run.");
            }
            return _names;
        }

        /// Fills `held` (Names' length) for this frame; false when not ready.
        public static bool Read(bool[] held)
        {
            if (_for == null || !ReferenceEquals(GameActions.Table, _for) || held.Length != _actions.Length) return false;
            for (int i = 0; i < held.Length; i++) held[i] = GameActions.Held(_actions[i], _signs[i]);
            return true;
        }
    }
}
