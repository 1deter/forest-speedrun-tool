using System;
using System.Reflection;
using BepInEx.Logging;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Stops the game from acting on input while our window is open.
    //
    // The player lock (FirstPersonCharacter.LockView) stops movement and
    // look, but not actions: clicking a button in the overlay while holding
    // the plane axe still swung it.
    //
    // Every game overlay solves this the same way, and it is a flag rather
    // than a per-frame fight. TheForest.Utils.Input keeps
    //
    //     static Dictionary<InputState,bool> States
    //
    // and SetState(state, on) stores one entry, then ForceRefreshState
    // picks the highest-priority active state and makes its Rewired map
    // the only one enabled. The pause menu (HudGui.TogglePauseMenu) holds
    // InputState.Menu; the dev console holds Chat. Confirmed from IL: the
    // only readers of the states are SetState, ForceRefreshState and two
    // VR display helpers, so holding Menu switches the key map and nothing
    // else.
    //
    // SetState is a no-op when the value is unchanged, but it Debug.Logs
    // every change - so it is only called on a transition, never per frame.
    //
    // The game clears Menu itself when the pause menu closes, and a save
    // load resets states, so Hold() re-checks each frame and re-asserts
    // only when it has been lost.
    // ------------------------------------------------------------------
    public sealed class GameInput
    {
        private readonly ManualLogSource _log;

        private MethodInfo _setState;
        private MethodInfo _getState;
        private object[] _getArgs;
        private object[] _setOn;
        private object[] _setOff;
        private bool _resolved;

        // True only while the Menu state is on because WE turned it on.
        // If it was already on (pause menu, title screen) it is not ours
        // to clear.
        private bool _owned;
        private bool _holding;

        /// Shown in Settings, so a block that silently failed to resolve
        /// is visible instead of looking like it works.
        public string Status { get; private set; }

        public bool Available { get { return _setState != null && _getState != null; } }

        public GameInput(ManualLogSource log)
        {
            _log = log;
            Status = "not resolved yet";
        }

        private void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            Type input = GameBridge.FindGameType("TheForest.Utils.Input");
            Type state = GameBridge.FindGameType("TheForest.Utils.InputState");
            if (input == null || state == null)
            {
                Status = "unavailable (TheForest.Utils.Input not found)";
                _log.LogWarning("GameInput: " + Status);
                return;
            }

            BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            _setState = input.GetMethod("SetState", flags, null, new Type[] { state, typeof(bool) }, null);
            _getState = input.GetMethod("GetState", flags, null, new Type[] { state }, null);

            object menu;
            try { menu = Enum.Parse(state, "Menu"); }
            catch (Exception) { menu = null; }

            if (_setState == null || _getState == null || menu == null)
            {
                _setState = null;
                _getState = null;
                Status = "unavailable (SetState/GetState/Menu not found)";
                _log.LogWarning("GameInput: " + Status);
                return;
            }

            // Built once; Hold() runs every frame.
            _getArgs = new object[] { menu };
            _setOn = new object[] { menu, true };
            _setOff = new object[] { menu, false };

            Status = "ready";
            _log.LogInfo("GameInput: Input.SetState(Menu) bound.");
        }

        private bool MenuOn()
        {
            try { return (bool)_getState.Invoke(null, _getArgs); }
            catch (Exception) { return true; }
        }

        private void SetMenu(object[] args)
        {
            try { _setState.Invoke(null, args); }
            catch (Exception ex)
            {
                Status = "SetState failed: " + ex.Message;
                _log.LogWarning("GameInput: " + Status);
            }
        }

        /// Call every frame while game input should be blocked.
        public void Hold()
        {
            Resolve();
            if (!Available) return;

            if (!_holding)
            {
                _holding = true;
                Status = "blocked (Menu key map)";
            }

            if (MenuOn()) return;

            SetMenu(_setOn);
            _owned = true;
        }

        /// Call when blocking is no longer wanted. Cheap when not holding.
        public void Release()
        {
            if (!_holding) return;
            _holding = false;
            Status = "ready";

            if (!_owned) return;
            _owned = false;
            SetMenu(_setOff);
        }
    }
}
