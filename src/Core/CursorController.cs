using System;
using System.Reflection;
using BepInEx.Logging;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // Frees the mouse for our own windows.
    //
    // HOW THE OLD VERSION FAILED
    // v0.4.0 wrote Cursor.lockState/Cursor.visible directly, in LateUpdate
    // and again in OnGUI, hoping to get the last word in the frame. The
    // symptom was a cursor that was visible but pinned to screen centre
    // and flickering. The reason is in TheForest.UI.VirtualCursor.LateUpdate:
    //
    //     if (TheForest.Utils.Input.IsMouseLocked) {
    //         if (Cursor.lockState != Locked) Cursor.lockState = Locked;
    //         if (Cursor.visible)             Cursor.visible   = false;
    //         ...
    //     }
    //
    // Setting lockState = Locked warps the pointer to the centre of the
    // screen. Our OnGUI override ran afterwards and could restore
    // visibility, but it could not un-warp a cursor that had already been
    // recentred that frame - so the pointer was redrawn, every frame, in
    // the middle of the screen. No amount of ordering fixes that.
    //
    // WHAT THIS DOES INSTEAD
    // Flips the game's own master switch, TheForest.Utils.Input.IsMouseLocked,
    // which is exactly what the ESC menu does (and why the ESC menu has
    // always worked). With it false, VirtualCursor takes its other branch
    // and sets lockState = None / visible = true itself, every frame, for
    // us. We stop fighting the game and let it do the work.
    //
    // Input.LockMouse()/UnLockMouse() were both confirmed from IL to be
    // plain one-line flag setters with no other side effects.
    //
    // The flag is re-asserted from Update() rather than LateUpdate(),
    // because Unity runs every Update before any LateUpdate - that
    // guarantees VirtualCursor.LateUpdate observes our value regardless of
    // script execution order, which is otherwise undefined between two
    // MonoBehaviours.
    // ------------------------------------------------------------------
    public sealed class CursorController
    {
        private readonly ManualLogSource _log;

        private PropertyInfo _isMouseLockedProp;
        private MethodInfo _lockMouse;
        private MethodInfo _unlockMouse;
        private bool _resolved;

        private bool _active;
        private bool _hadLockedCursor = true;

        // True when we found the game's switch. When false we fall back to
        // writing Cursor.* directly, which is imperfect (see above) but is
        // better than a window nobody can click at all.
        public bool UsingGameFlag { get { return _isMouseLockedProp != null; } }

        public CursorController(ManualLogSource log)
        {
            _log = log;
        }

        private void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            Type t = GameBridge.FindGameType("TheForest.Utils.Input");
            if (t == null)
            {
                _log.LogWarning("TheForest.Utils.Input not found - falling back to direct Cursor writes.");
                return;
            }

            BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            _isMouseLockedProp = t.GetProperty("IsMouseLocked", flags);
            _lockMouse = t.GetMethod("LockMouse", flags, null, Type.EmptyTypes, null);
            _unlockMouse = t.GetMethod("UnLockMouse", flags, null, Type.EmptyTypes, null);

            _log.LogInfo("Cursor: IsMouseLocked:" + (_isMouseLockedProp != null) +
                         " LockMouse:" + (_lockMouse != null) +
                         " UnLockMouse:" + (_unlockMouse != null));
        }

        private bool ReadGameLocked()
        {
            if (_isMouseLockedProp == null) return true;
            try { return (bool)_isMouseLockedProp.GetValue(null, null); }
            catch (Exception) { return true; }
        }

        private void WriteGameLocked(bool locked)
        {
            try
            {
                MethodInfo m = locked ? _lockMouse : _unlockMouse;
                if (m != null) { m.Invoke(null, null); return; }

                if (_isMouseLockedProp != null && _isMouseLockedProp.CanWrite)
                    _isMouseLockedProp.SetValue(null, locked, null);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Could not set IsMouseLocked: " + ex.Message);
            }
        }

        // Called every frame from Update() while any panel wants the mouse.
        public void Acquire()
        {
            Resolve();

            if (!_active)
            {
                _hadLockedCursor = ReadGameLocked();
                _active = true;
            }

            if (_isMouseLockedProp != null)
            {
                // Cheap enough to re-assert unconditionally; this is what
                // survives the game re-locking on view changes.
                if (ReadGameLocked()) WriteGameLocked(false);
                return;
            }

            // Fallback only.
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
        }

        public void Release()
        {
            if (!_active) return;
            _active = false;

            if (_isMouseLockedProp != null)
            {
                // Restore whatever the game had before we interfered, so
                // closing a panel in a menu does not re-lock the pointer.
                if (_hadLockedCursor) WriteGameLocked(true);
                return;
            }

            Cursor.lockState = _hadLockedCursor ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !_hadLockedCursor;
        }
    }
}
