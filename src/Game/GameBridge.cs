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
            _cameraRotator = null;
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
        // Look angles.
        //
        // SimpleMouseRotator does not read the transform - it RECOMPOSES
        // it every frame as originalRotation * Euler(-followAngles.x,
        // followAngles.y, 0), where followAngles damps towards
        // targetAngles (+ xOffset/yOffset). Writing transform.rotation
        // during a teleport never sticks.
        //
        // The two rotators store the view differently (confirmed from IL,
        // UpdateRotation):
        //
        //   BODY (yaw)     - zeroes originalRotation.x/.z every frame and
        //                    keeps .y, so yaw lives in originalRotation.
        //   CAMERA (pitch) - zeroes originalRotation.x/.y/.z every frame,
        //                    so pitch lives ONLY in targetAngles.x /
        //                    followAngles.x.
        //
        // So the two need different handling:
        //
        //   Yaw: set the body's rotation and raise resetOriginalRotation.
        //   CheckResetOriginalRotation adopts the current rotation as the
        //   new base and zeroes the angles - exactly right for the body.
        //   It is consumed inside UpdateRotation, which only runs while the
        //   player is NOT locked, so raising it during a locked teleport
        //   applies on the first unlocked frame.
        //
        //   Pitch: NEVER reset the camera rotator. Zeroing its angles is
        //   zeroing the pitch - that is what snapped the view level on
        //   every window close. Write targetAngles.x / followAngles.x and
        //   raise fixCameraRotation instead, which is what the game itself
        //   does when the survival book closes
        //   (survivalBookController.FinalCloseBook).
        // ------------------------------------------------------------------
        private Component[] _rotators;
        private Component _cameraRotator;
        private FieldInfo _resetOriginalRotation;
        private FieldInfo _isCameraRotator;
        private FieldInfo _targetAngles;
        private FieldInfo _followAngles;
        private FieldInfo _xOffset;
        private FieldInfo _fixCameraRotation;
        private Transform _pitchTransform;
        private bool _rotatorsResolved;
        private float _nextRotatorResolve;

        public void ResolveRotators(Transform playerRoot)
        {
            // Rotators die with the player on a save load; a destroyed
            // component compares equal to null, so re-resolve then rather
            // than keep rebasing (and reading pitch from) dead objects.
            if (_rotatorsResolved && _rotators.Length > 0 && _rotators[0] != null) return;
            if (playerRoot == null) return;
            if (Time.unscaledTime < _nextRotatorResolve) return;
            _nextRotatorResolve = Time.unscaledTime + RetryInterval;

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
            _targetAngles = rotatorType.GetField("targetAngles", flags);
            _followAngles = rotatorType.GetField("followAngles", flags);
            _xOffset = rotatorType.GetField("xOffset", flags);
            _fixCameraRotation = rotatorType.GetField("fixCameraRotation", flags);

            // Pitch lives on the camera rotator, yaw on the body one. They
            // are separate transforms, which is why saving the player's
            // rotation alone never captured where you were actually
            // looking vertically.
            _cameraRotator = null;
            _pitchTransform = null;
            if (_isCameraRotator != null)
            {
                for (int i = 0; i < _rotators.Length; i++)
                {
                    try
                    {
                        if (!(bool)_isCameraRotator.GetValue(_rotators[i])) continue;
                        _cameraRotator = _rotators[i];
                        _pitchTransform = _rotators[i].transform;
                        break;
                    }
                    catch (Exception) { }
                }
            }

            _log.LogInfo("SimpleMouseRotator x" + _rotators.Length +
                         " resetOriginalRotation:" + (_resetOriginalRotation != null) +
                         " camera:" + (_cameraRotator != null) +
                         " angles:" + (_targetAngles != null && _followAngles != null) +
                         " fixCameraRotation:" + (_fixCameraRotation != null));
        }

        /// Current view pitch in degrees, normalised to -180..180 so it can
        /// be stored and compared sensibly. Positive looks down (Unity's
        /// euler x). Returns 0 when unavailable.
        public float GetLookPitch()
        {
            if (_pitchTransform == null) return 0f;

            float x = _pitchTransform.localEulerAngles.x;
            return x > 180f ? x - 360f : x;
        }

        /// Point the player at a saved view direction.
        ///
        /// Yaw is the body, pitch is the camera - two different transforms,
        /// stored two different ways (see above).
        public void ApplyLook(Transform playerRoot, float yaw, float pitch)
        {
            if (playerRoot != null)
            {
                Vector3 e = playerRoot.eulerAngles;
                e.y = yaw;
                playerRoot.eulerAngles = e;
            }

            // The transform too, so the view is right while the player is
            // still held and the rotator is not running.
            if (_pitchTransform != null)
            {
                Vector3 c = _pitchTransform.localEulerAngles;
                c.x = pitch;
                _pitchTransform.localEulerAngles = c;
            }

            SetCameraPitch(pitch);
            RebaseLookAngles();
        }

        // Rotation is Euler(-followAngles.x, ...) and followAngles chases
        // targetAngles.x + xOffset, so pitch P means followAngles.x = -P
        // and targetAngles.x = -P - xOffset. fixCameraRotation makes the
        // next update snap followAngles instead of damping towards it.
        private void SetCameraPitch(float pitch)
        {
            if (_cameraRotator == null || _targetAngles == null || _followAngles == null) return;

            try
            {
                float offset = _xOffset != null ? (float)_xOffset.GetValue(_cameraRotator) : 0f;

                Vector3 target = (Vector3)_targetAngles.GetValue(_cameraRotator);
                target.x = -pitch - offset;
                _targetAngles.SetValue(_cameraRotator, target);

                Vector3 follow = (Vector3)_followAngles.GetValue(_cameraRotator);
                follow.x = -pitch;
                _followAngles.SetValue(_cameraRotator, follow);

                if (_fixCameraRotation != null) _fixCameraRotation.SetValue(_cameraRotator, true);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Could not set camera pitch: " + ex.Message);
            }
        }

        /// Tell the BODY rotator to rebase on the player's current yaw.
        /// Call after any teleport, and whenever the player lock is
        /// released. Deliberately skips the camera rotator - resetting it
        /// zeroes the pitch.
        public void RebaseLookAngles()
        {
            if (_rotators == null || _resetOriginalRotation == null) return;

            for (int i = 0; i < _rotators.Length; i++)
            {
                if (_rotators[i] == null) continue;
                if (ReferenceEquals(_rotators[i], _cameraRotator)) continue;
                try { _resetOriginalRotation.SetValue(_rotators[i], true); }
                catch (Exception) { }
            }
        }

        // ------------------------------------------------------------------
        // Cave state on teleport.
        //
        // Walking into a cave, CaveTriggers/CaveDoor send "InACave" to the
        // player: the lighting switches to cave mode (Clock.IsCave),
        // PlayerStats.SetInCave(true), and - the one that matters - the
        // player stops colliding with the terrain, because caves lie under
        // it. A teleport skips all of that, so arriving in a cave left you
        // under the terrain with the cave in its outdoor state: "lands in
        // nothing".
        //
        // The game's own teleport, LocalPlayer.Goto(Vector3), solves it
        // (IL): a target more than 6 m below the terrain surface (3 m if
        // already in a cave) is a cave; it calls GotoCave(inCave), which
        // sends InACave / NotInACave only when the state changes, then
        // zeroes velocity and moves. The same message is all a save load
        // uses to restore an in-cave player. Same rule, same call here.
        // ------------------------------------------------------------------
        private Type _localPlayerType;
        private Component _localPlayer;
        private MethodInfo _gotoCave;
        private PropertyInfo _isInCaves;
        private bool _caveResolved;

        /// Current cave state as the game sees it.
        public bool IsInCaves()
        {
            ResolveCave();
            try { return _isInCaves != null && (bool)_isInCaves.GetValue(null, null); }
            catch (Exception) { return false; }
        }

        /// Call BEFORE moving the player to `destination`. Returns what was
        /// done, for the status line.
        public string SyncCaveState(Vector3 destination)
        {
            ResolveCave();
            if (_gotoCave == null) return "cave switch unavailable";

            Terrain terrain = Terrain.activeTerrain;
            if (terrain == null) return "";

            bool inCaves = IsInCaves();
            bool destInCave = terrain.SampleHeight(destination) - destination.y > (inCaves ? 3f : 6f);
            if (destInCave == inCaves) return "";

            // LocalPlayer is a component; find it once, re-find only after a
            // save load has destroyed it (fake-null).
            if (_localPlayer == null)
            {
                try { _localPlayer = UnityEngine.Object.FindObjectOfType(_localPlayerType) as Component; }
                catch (Exception) { _localPlayer = null; }
            }
            if (_localPlayer == null) return "cave switch: LocalPlayer not found";

            try
            {
                _gotoCave.Invoke(_localPlayer, new object[] { destInCave });
                return destInCave ? "entered cave" : "left cave";
            }
            catch (Exception ex)
            {
                _log.LogWarning("GotoCave failed: " + ex.Message);
                return "cave switch failed";
            }
        }

        private void ResolveCave()
        {
            if (_caveResolved) return;
            _caveResolved = true;

            _localPlayerType = FindGameType("TheForest.Utils.LocalPlayer");
            if (_localPlayerType == null) return;

            BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            _gotoCave = _localPlayerType.GetMethod("GotoCave", any, null, new Type[] { typeof(bool) }, null);
            _isInCaves = _localPlayerType.GetProperty("IsInCaves", any);

            _log.LogInfo("Cave switch: GotoCave:" + (_gotoCave != null) + " IsInCaves:" + (_isInCaves != null));
        }

        public bool IsPlayerLocked()
        {
            if (!Live() || _lockedField == null) return false;
            try { return (bool)_lockedField.GetValue(_fpc); }
            catch (Exception) { return false; }
        }

        // ------------------------------------------------------------------
        // Lookups are cached. FindGameType walks every loaded assembly and
        // allocates an AssemblyName per assembly, and ReadStaticField is
        // called every frame (inventory, practice runs) - that was a steady
        // trickle of garbage for Mono's GC. Only hits are cached: a miss
        // may resolve once the game has finished loading.
        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>();
        private static readonly Dictionary<string, Dictionary<string, FieldInfo>> StaticFieldCache =
            new Dictionary<string, Dictionary<string, FieldInfo>>();

        public static Type FindGameType(string fullName)
        {
            Type cached;
            if (TypeCache.TryGetValue(fullName, out cached)) return cached;

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
                    if (t != null) { TypeCache[fullName] = t; return t; }
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
            // Two-level so a lookup allocates nothing (a joined key would).
            Dictionary<string, FieldInfo> fields;
            if (!StaticFieldCache.TryGetValue(typeName, out fields))
            {
                fields = new Dictionary<string, FieldInfo>();
                StaticFieldCache[typeName] = fields;
            }

            FieldInfo f;
            if (!fields.TryGetValue(fieldName, out f))
            {
                Type t = FindGameType(typeName);
                if (t == null) return null;

                f = t.GetField(fieldName,
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (f == null) return null;
                fields[fieldName] = f;
            }

            try { return f.GetValue(null); }
            catch (Exception) { return null; }
        }
    }
}
