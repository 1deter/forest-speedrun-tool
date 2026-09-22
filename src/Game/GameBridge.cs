using System;
using System.Collections.Generic;
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
            _rotators = null;
            _rotatorsResolved = false;
            _resetOriginalRotation = null;
            _isCameraRotator = null;
            _pitchTransform = null;
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

        // ------------------------------------------------------------------
        // Look-angle rebase.
        //
        // SimpleMouseRotator does not read the transform - it RECOMPOSES
        // it every frame as originalRotation * Euler(targetAngles). So
        // writing transform.rotation during a teleport never sticks: the
        // moment input resumes the rotator rebuilds the old orientation
        // and the view snaps back to wherever you were looking when the
        // rotator last ran.
        //
        // Writing targetAngles by hand does not fix it either, because
        // they are relative to originalRotation, which is still stale.
        //
        // The game already has the right operation. CheckResetOriginalRotation:
        //
        //     if (resetOriginalRotation) {
        //         originalRotation = useRigidbody ? rb.rotation
        //                                         : transform.localRotation;
        //         targetAngles.x = targetAngles.y = 0;
        //         followAngles   = Vector2.zero;
        //         resetOriginalRotation = false;
        //     }
        //
        // - "adopt the current orientation as the new base". Setting that
        // one flag is all that is needed, and it lets the game decide what
        // that means for the pitch rotator versus the yaw one.
        //
        // It is consumed inside UpdateRotation, which only runs while the
        // player is NOT locked - so setting it during a locked teleport
        // applies on the first unlocked frame, which is exactly when the
        // snap used to happen.
        // ------------------------------------------------------------------
        private Component[] _rotators;
        private FieldInfo _resetOriginalRotation;
        private FieldInfo _isCameraRotator;
        private Transform _pitchTransform;
        private bool _rotatorsResolved;

        public void ResolveRotators(Transform playerRoot)
        {
            if (_rotatorsResolved || playerRoot == null) return;

            Component[] comps;
            try { comps = playerRoot.GetComponentsInChildren(typeof(Component), true); }
            catch (Exception) { return; }

            List<Component> found = new List<Component>();
            Type rotatorType = null;

            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] == null) continue;
                Type t = comps[i].GetType();
                if (t.Name != "SimpleMouseRotator") continue;
                rotatorType = t;
                found.Add(comps[i]);
            }

            if (rotatorType == null) return;

            _rotatorsResolved = true;
            _rotators = found.ToArray();

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _resetOriginalRotation = rotatorType.GetField("resetOriginalRotation", flags);
            _isCameraRotator = rotatorType.GetField("cameraRotator", flags);

            // Pitch lives on the camera rotator, yaw on the body one. They
            // are separate transforms, which is why saving the player's
            // rotation alone never captured where you were actually
            // looking vertically.
            _pitchTransform = null;
            if (_isCameraRotator != null)
            {
                for (int i = 0; i < _rotators.Length; i++)
                {
                    try
                    {
                        if (!(bool)_isCameraRotator.GetValue(_rotators[i])) continue;
                        _pitchTransform = _rotators[i].transform;
                        break;
                    }
                    catch (Exception) { }
                }
            }

            _log.LogInfo("SimpleMouseRotator x" + _rotators.Length +
                         " resetOriginalRotation:" + (_resetOriginalRotation != null) +
                         " pitchTransform:" + (_pitchTransform != null));
        }

        /// Current view pitch in degrees, normalised to -180..180 so it can
        /// be stored and compared sensibly. Returns 0 when unavailable.
        public float GetLookPitch()
        {
            if (_pitchTransform == null) return 0f;

            float x = _pitchTransform.localEulerAngles.x;
            return x > 180f ? x - 360f : x;
        }

        /// Point the player at a saved view direction, then rebase so the
        /// rotators adopt it instead of snapping back.
        ///
        /// Yaw is the body, pitch is the camera - two different transforms.
        /// Writing only the player rotation (which is all the anchor used
        /// to store) left pitch wherever it happened to be.
        public void ApplyLook(Transform playerRoot, float yaw, float pitch)
        {
            if (playerRoot != null)
            {
                Vector3 e = playerRoot.eulerAngles;
                e.y = yaw;
                playerRoot.eulerAngles = e;
            }

            if (_pitchTransform != null)
            {
                Vector3 c = _pitchTransform.localEulerAngles;
                c.x = pitch;
                _pitchTransform.localEulerAngles = c;
            }

            RebaseLookAngles();
        }

        /// Tell every rotator to rebase on the player's current orientation.
        /// Call after any teleport, and whenever the player lock is released.
        public void RebaseLookAngles()
        {
            if (_rotators == null || _resetOriginalRotation == null) return;

            for (int i = 0; i < _rotators.Length; i++)
            {
                if (_rotators[i] == null) continue;
                try { _resetOriginalRotation.SetValue(_rotators[i], true); }
                catch (Exception) { }
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
