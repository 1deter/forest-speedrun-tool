using System;
using System.Reflection;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Closes the survival book on a reset (runner sxczurass: restarting a
    // savestate with the book open left it in the hands).
    //
    // The book is not an equipped item, so the restore's StashHands never
    // sees it: the inventory stays in its Book view (the player locked,
    // the HUD hidden). AnimReset alone switched `bookHeld` off under it.
    //
    // WHAT (IL): Create.CloseBookForInventory - the game's fast close,
    // used when the inventory is opened from the book. CloseTheBook(true)
    // puts the view back to World at once (showEquipped: RestoreEquipement,
    // inventory enabled) and fastCloseBook plays the quick close on the
    // book model. Runs before AnimReset.Cancel, which would otherwise
    // clear `bookHeld` first.
    //
    // THE CAMERA (runner sxczurass, v0.24.91: after a reset with the book
    // open he could look sideways, not up or down). survivalBookController
    // .setOpenBook - an animation event as the book reaches its idle pose -
    // sets the camera rotator's rotationRange to (0, 0) (pitch; yaw is the
    // body's) and xOffset to -20. Only FinalCloseBook, played by the close
    // animation, puts them back. A reset in the ~0.5 s between setOpenBook
    // and the real book opening takes a close path that never plays it
    // (bridge, 2026-09-26: reset 0.6 s after opening -> (0, 0) for good;
    // 0.2 s and 1.2 s fine). So once the reset is done (Tick, with no
    // restore running), a book lock left with the book shut gets
    // FinalCloseBook's camera lines. xOffset -20 marks the book's lock:
    // only setOpenBook (and a cave cutscene) writes it.
    // ------------------------------------------------------------------
    public static class BookClose
    {
        private static FieldInfo _inventory;     // static LocalPlayer.Inventory
        private static FieldInfo _create;        // static LocalPlayer.Create
        private static PropertyInfo _view;       // PlayerInventory.CurrentView
        private static MethodInfo _close;        // Create.CloseBookForInventory()
        private static object _bookView;         // PlayerViews.Book
        private static bool _resolved;

        private const float CheckDelay = 1.5f;
        private const float BookXOffset = -20f;
        private static float _checkAt = -1f;
        private static FieldInfo _camRotator, _mainRotator, _fpCharacter, _camFollowHead, _animator;
        private static FieldInfo _range, _xOffset, _damping, _fixCam, _resetOriginal, _targetAngles, _followAngles, _minRange;
        private static bool _camResolved;

        /// Closes the book when it is open. Says what it did; "" when the
        /// book was not open.
        public static string IfOpen()
        {
            try
            {
                if (!Resolve()) return "";
                object inv = _inventory.GetValue(null);
                object create = _create.GetValue(null);
                if (inv == null || create == null) return "";
                if (!Equals(_view.GetValue(inv, null), _bookView)) return "";
                _close.Invoke(create, null);
                _checkAt = Time.realtimeSinceStartup + CheckDelay;
                return "closed the book";
            }
            catch (Exception ex)
            {
                return "closing the book failed: " + (ex.InnerException ?? ex).Message;
            }
        }

        /// Once a frame, while no restore runs: after a reset that closed
        /// the book, frees a pitch lock the book left. Says what it did;
        /// "" otherwise.
        public static string Tick()
        {
            if (_checkAt < 0f || Time.realtimeSinceStartup < _checkAt) return "";
            _checkAt = -1f;
            try
            {
                if (!Resolve() || !ResolveCamera()) return "";
                object inv = _inventory.GetValue(null);
                object rot = _camRotator.GetValue(null);
                object fp = _fpCharacter.GetValue(null);
                if (inv == null || rot == null || fp == null) return "";
                if (Equals(_view.GetValue(inv, null), _bookView)) return "";
                Vector2 range = (Vector2)_range.GetValue(rot);
                float xOffset = (float)_xOffset.GetValue(rot);
                if (range.x != 0f || xOffset != BookXOffset) return "";

                // FinalCloseBook's camera lines, in its order.
                _damping.SetValue(rot, 0f);
                Animator anim = _animator.GetValue(null) as Animator;
                if (anim != null) anim.SetBool("clampSpine", false);
                object main = _mainRotator.GetValue(null);
                if (main != null) _resetOriginal.SetValue(main, true);
                float min = (float)_minRange.GetValue(fp);
                _range.SetValue(rot, new Vector2(min, 0f));
                Component head = _camFollowHead.GetValue(null) as Component;
                if (head != null) head.transform.localEulerAngles = Vector3.zero;
                Vector3 t = (Vector3)_targetAngles.GetValue(rot);
                t.x += BookXOffset;
                _targetAngles.SetValue(rot, t);
                Vector3 f = (Vector3)_followAngles.GetValue(rot);
                f.x += BookXOffset;
                _followAngles.SetValue(rot, f);
                _xOffset.SetValue(rot, 0f);
                _fixCam.SetValue(rot, true);
                return "freed the camera's pitch the book had locked (range back to " + min.ToString("0") + ")";
            }
            catch (Exception ex)
            {
                return "freeing the book's camera lock failed: " + (ex.InnerException ?? ex).Message;
            }
        }

        private static bool ResolveCamera()
        {
            if (_camResolved) return _minRange != null;
            _camResolved = true;
            const BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
            Type rot = GameBridge.FindGameType("SimpleMouseRotator");
            Type fp = GameBridge.FindGameType("FirstPersonCharacter");
            if (local == null || rot == null || fp == null) return false;
            _camRotator = local.GetField("CamRotator", stat);
            _mainRotator = local.GetField("MainRotator", stat);
            _fpCharacter = local.GetField("FpCharacter", stat);
            _camFollowHead = local.GetField("CamFollowHead", stat);
            _animator = local.GetField("Animator", stat);
            _range = rot.GetField("rotationRange", inst);
            _xOffset = rot.GetField("xOffset", inst);
            _damping = rot.GetField("dampingOverride", inst);
            _fixCam = rot.GetField("fixCameraRotation", inst);
            _resetOriginal = rot.GetField("resetOriginalRotation", inst);
            _targetAngles = rot.GetField("targetAngles", inst);
            _followAngles = rot.GetField("followAngles", inst);
            FieldInfo min = fp.GetField("minCamRotationRange", inst);
            if (_camRotator == null || _mainRotator == null || _fpCharacter == null || _camFollowHead == null || _animator == null ||
                _range == null || _xOffset == null || _damping == null || _fixCam == null || _resetOriginal == null ||
                _targetAngles == null || _followAngles == null)
                return false;
            _minRange = min;
            return _minRange != null;
        }

        private static bool Resolve()
        {
            if (_resolved) return _close != null;
            _resolved = true;
            const BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
            Type inv = GameBridge.FindGameType("TheForest.Items.Inventory.PlayerInventory");
            Type create = GameBridge.FindGameType("TheForest.Buildings.Creation.Create");
            if (local == null || inv == null || create == null) return false;

            _inventory = local.GetField("Inventory", stat);
            _create = local.GetField("Create", stat);
            _view = inv.GetProperty("CurrentView", inst);
            Type views = inv.GetNestedType("PlayerViews", BindingFlags.Public | BindingFlags.NonPublic);
            if (views != null && Enum.IsDefined(views, "Book")) _bookView = Enum.Parse(views, "Book");
            MethodInfo close = create.GetMethod("CloseBookForInventory", inst, null, Type.EmptyTypes, null);

            if (_inventory == null || _create == null || _view == null || _bookView == null) return false;
            _close = close;
            return _close != null;
        }
    }
}
