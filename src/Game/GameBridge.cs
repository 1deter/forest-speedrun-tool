using System;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Reflection bridge to The Forest's own types.
    //
    // Everything here is looked up by name at runtime rather than
    // referenced at compile time. That keeps the build free of game DLLs
    // (so CI works) and makes a game update degrade into a logged warning
    // instead of a crash.
    //
    // Inventory reflection lives in InventoryReader, cursor reflection in
    // Core/CursorController - each keeps its own game names together.
    //
    // Names below were confirmed from a live dump and from IL, not guessed:
    //   FirstPersonCharacter          - movement controller on "player"
    //     .Locked          (bool)     - read by SimpleMouseRotator.Update,
    //                                   FirstPersonCharacter.Update and
    //                                   .FixedUpdate, so it stops both
    //                                   movement AND camera look
    //     .MovementLocked  (bool)     - movement-only lock
    //     .LockView(bool) / .UnLockView() - the game's own pause-the-player
    //                                   pair; see SetPlayerLocked
    // ------------------------------------------------------------------
    public class GameBridge
    {
        private const float RetryInterval = 0.5f;

        private readonly ManualLogSource _log;

        private Component _fpc;
        private FieldInfo _lockedField;
        private FieldInfo _movementLockedField;
        private MethodInfo _lockView;
        private MethodInfo _unlockView;
        private int _lockViewArgCount;
        private float _nextResolveTime;
        private bool _loggedFailure;

        /// Human-readable resolution state, shown in the UI so a silent
        /// failure is visible instead of looking like a dead toggle.
        public string LockStatus { get; private set; }

        public bool PlayerLockAvailable
        {
            get { return Live() && (_lockView != null || _lockedField != null); }
        }

        public GameBridge(ManualLogSource log)
        {
            _log = log;
            LockStatus = "not resolved";
        }

        public void Reset()
        {
            _fpc = null;
            _lockedField = null;
            _movementLockedField = null;
            _lockView = null;
            _unlockView = null;
            _nextResolveTime = 0f;
            _loggedFailure = false;
            LockStatus = "not resolved";
        }

        // A destroyed UnityEngine.Object compares equal to null, so this
        // also catches a reference that went stale across a save load.
        private bool Live()
        {
            return _fpc != null;
        }

        // ------------------------------------------------------------------
        // Movement lock - uses the game's OWN flags.
        //
        // Time.timeScale is useless here: InventoryItemView.Update
        // re-asserts it every frame, so an external write always loses.
        //
        // RESOLUTION RETRIES. The previous version set a "resolved" flag
        // before scanning, so one failed scan disabled the lock for the
        // rest of the session - and because the cached component goes
        // stale when a save is loaded, the lock silently stopped working
        // and the explorer's toggle looked dead. Now it retries on a timer
        // and re-resolves whenever the reference dies.
        // ------------------------------------------------------------------
        public void ResolvePlayerController(Transform playerRoot)
        {
            if (Live()) return;
            if (playerRoot == null) return;
            if (Time.unscaledTime < _nextResolveTime) return;
            _nextResolveTime = Time.unscaledTime + RetryInterval;

            Component[] comps;
            try { comps = playerRoot.GetComponentsInChildren(typeof(Component), true); }
            catch (Exception ex)
            {
                LockStatus = "component scan failed";
                _log.LogWarning("Component scan failed: " + ex.Message);
                return;
            }

            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] == null) continue;

                Type t = comps[i].GetType();
                if (t.Name != "FirstPersonCharacter") continue;

                Bind(comps[i], t);
                return;
            }

            // Only log the miss once; this runs twice a second until the
            // player spawns and would otherwise flood the log.
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                _log.LogWarning("FirstPersonCharacter not found yet - will keep retrying.");
            }
            LockStatus = "searching...";
        }

        private void Bind(Component fpc, Type t)
        {
            _fpc = fpc;
            _loggedFailure = false;

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            _lockedField = t.GetField("Locked", flags);
            _movementLockedField = t.GetField("MovementLocked", flags);

            // Prefer the game's own pair. LockView additionally sleeps the
            // rigidbody, sets isKinematic and clears useGravity (so you do
            // not sag through the floor while held), clears CanJump, and
            // frees the mouse; UnLockView reverses all of it and wakes the
            // body. Poking Locked by hand does none of that.
            _lockView = FindMethod(t, "LockView", flags);
            _unlockView = t.GetMethod("UnLockView", flags, null, Type.EmptyTypes, null);

            _lockViewArgCount = _lockView != null ? _lockView.GetParameters().Length : 0;

            LockStatus = _lockView != null && _unlockView != null
                ? "LockView"
                : (_lockedField != null ? "Locked field" : "unavailable");

            _log.LogInfo("FirstPersonCharacter bound. LockView:" + (_lockView != null) +
                         "(" + _lockViewArgCount + " arg) UnLockView:" + (_unlockView != null) +
                         " Locked:" + (_lockedField != null) +
                         " MovementLocked:" + (_movementLockedField != null));
        }

        // LockView takes a bool in the build this was written against, but
        // tolerate a parameterless variant rather than losing the feature.
        private static MethodInfo FindMethod(Type t, string name, BindingFlags flags)
        {
            MethodInfo m = t.GetMethod(name, flags, null, new Type[] { typeof(bool) }, null);
            if (m != null) return m;
            return t.GetMethod(name, flags, null, Type.EmptyTypes, null);
        }

        public void SetPlayerLocked(bool locked)
        {
            if (!Live()) return;

            try
            {
                if (_lockView != null && _unlockView != null)
                {
                    if (locked)
                    {
                        object[] args = _lockViewArgCount == 1 ? new object[] { true } : null;
                        _lockView.Invoke(_fpc, args);
                    }
                    else
                    {
                        _unlockView.Invoke(_fpc, null);
                    }
                    return;
                }

                if (_lockedField != null) _lockedField.SetValue(_fpc, locked);
                if (_movementLockedField != null) _movementLockedField.SetValue(_fpc, locked);
            }
            catch (Exception ex)
            {
                LockStatus = "lock call failed";
                _log.LogWarning("Could not set player lock: " + ex.Message);
            }
        }

        public bool IsPlayerLocked()
        {
            if (!Live() || _lockedField == null) return false;
            try { return (bool)_lockedField.GetValue(_fpc); }
            catch (Exception) { return false; }
        }

        // ------------------------------------------------------------------
        public static Type FindGameType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            for (int a = 0; a < assemblies.Length; a++)
            {
                string name;
                try { name = assemblies[a].GetName().Name; }
                catch (Exception) { continue; }
                if (!name.StartsWith("Assembly-CSharp")) continue;

                try
                {
                    Type t = assemblies[a].GetType(fullName, false);
                    if (t != null) return t;
                }
                catch (Exception) { }
            }
            return null;
        }

        /// Reads a public static field from a game type, e.g.
        /// TheForest.Utils.LocalPlayer.Inventory. Statics like these are
        /// the canonical handles and survive a save load, unlike a cached
        /// FindObjectOfType result.
        public static object ReadStaticField(string typeName, string fieldName)
        {
            Type t = FindGameType(typeName);
            if (t == null) return null;

            FieldInfo f = t.GetField(fieldName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (f == null) return null;

            try { return f.GetValue(null); }
            catch (Exception) { return null; }
        }
    }
}
