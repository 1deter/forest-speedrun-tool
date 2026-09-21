using System;
using BepInEx;
using UnityEngine;

namespace ForestOverlay
{
    // ------------------------------------------------------------------
    // v0.4.0
    //
    // Changes:
    //   * Time.timeScale freeze REMOVED - it did nothing, because The
    //     Forest re-asserts timeScale every frame. Replaced with the
    //     game's own FirstPersonCharacter.Locked / MovementLocked flags,
    //     which is what the game itself uses when opening its menus.
    //   * Cursor is now re-asserted in OnGUI as well as LateUpdate. OnGUI
    //     runs after LateUpdate, so this is the last word in the frame and
    //     should stop the on/off jitter.
    //   * First real game data on the HUD: inventory item count, read from
    //     TheForest.Items.Inventory.PlayerInventory via reflection.
    // ------------------------------------------------------------------

    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class OverlayPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.deter.forestoverlay";
        public const string PluginName = "ForestOverlay";
        public const string PluginVersion = "0.4.0";

        private const KeyCode ToggleOverlayKey = KeyCode.F5;
        private const KeyCode SavePositionKey = KeyCode.F6;
        private const KeyCode LoadPositionKey = KeyCode.F7;
        private const KeyCode StartStopTimerKey = KeyCode.F8;
        private const KeyCode ResetTimerKey = KeyCode.F9;
        private const KeyCode ToggleExplorerKey = KeyCode.F10;
        private const KeyCode WriteDumpsKey = KeyCode.F11;

        private bool _overlayVisible = true;
        private bool _explorerVisible;

        private bool _timerRunning;
        private float _timerElapsed;

        private bool _hasSavedPosition;
        private Vector3 _savedPosition;
        private Quaternion _savedRotation;
        private string _saveStatusMessage = "";

        private Transform _playerTransform;
        private Rigidbody _playerRigidbody;
        private CharacterController _playerController;
        private Vector3 _lastPosition;
        private Vector3 _computedVelocity;
        private string _playerSourceDescription = "searching...";
        private float _nextPlayerSearchTime;

        private bool _cursorOverridden;
        private CursorLockMode _prevLockState = CursorLockMode.Locked;
        private bool _prevCursorVisible;
        private bool _playerLockApplied;

        private string _hudTimerLine = "";
        private string _hudSpeedLine = "";
        private string _hudVelLine = "";
        private string _hudPlayerLine = "";
        private string _hudInventoryLine = "";
        private float _nextHudRefreshTime;

        private TypeExplorer _explorer;
        private GameBridge _bridge;
        private string _dumpStatus = "";

        private void Awake()
        {
            Logger.LogInfo(PluginName + " v" + PluginVersion + " loaded (net35 / Unity 5.6).");
            Logger.LogInfo("F5 hud | F6/F7 save-load pos | F8/F9 timer | F10 explorer | F11 dumps");

            _bridge = new GameBridge(Logger);

            try
            {
                _explorer = new TypeExplorer(Logger);
                _explorer.OnLockPlayerChanged = OnLockPlayerToggled;
                _explorer.Rescan();
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Type explorer init failed: " + ex);
            }
        }

        private void OnDestroy()
        {
            RestoreCursor();
            ReleasePlayerLock();
        }

        private void Update()
        {
            try
            {
                HandleHotkeys();
                TryAcquirePlayerReferences();
                UpdateVelocity();

                if (_bridge != null) _bridge.ResolveInventory();

                if (_timerRunning)
                    _timerElapsed += Time.unscaledDeltaTime;

                RefreshHudTextIfDue();
            }
            catch (Exception ex)
            {
                Logger.LogError("Update() threw: " + ex);
            }
        }

        private void LateUpdate()
        {
            try
            {
                if (_explorerVisible) ApplyCursorOverride();
                else if (_cursorOverridden) RestoreCursor();
            }
            catch (Exception ex)
            {
                Logger.LogError("LateUpdate() threw: " + ex);
            }
        }

        private void HandleHotkeys()
        {
            if (Input.GetKeyDown(ToggleOverlayKey))
                _overlayVisible = !_overlayVisible;

            if (Input.GetKeyDown(ToggleExplorerKey))
            {
                _explorerVisible = !_explorerVisible;
                Logger.LogInfo("Explorer -> " + _explorerVisible);

                if (_explorerVisible)
                {
                    if (_explorer != null && _explorer.LockPlayer) ApplyPlayerLock();
                }
                else
                {
                    RestoreCursor();
                    ReleasePlayerLock();
                }
            }

            if (Input.GetKeyDown(StartStopTimerKey)) _timerRunning = !_timerRunning;

            if (Input.GetKeyDown(ResetTimerKey))
            {
                _timerRunning = false;
                _timerElapsed = 0f;
            }

            if (Input.GetKeyDown(SavePositionKey)) SavePracticePosition();
            if (Input.GetKeyDown(LoadPositionKey)) LoadPracticePosition();
            if (Input.GetKeyDown(WriteDumpsKey)) WriteStandardDumps();
        }

        // ------------------------------------------------------------------
        // Cursor + player lock
        // ------------------------------------------------------------------
        private void ApplyCursorOverride()
        {
            if (!_cursorOverridden)
            {
                _prevLockState = Cursor.lockState;
                _prevCursorVisible = Cursor.visible;
                _cursorOverridden = true;
            }

            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
        }

        private void RestoreCursor()
        {
            if (!_cursorOverridden) return;
            Cursor.lockState = _prevLockState;
            Cursor.visible = _prevCursorVisible;
            _cursorOverridden = false;
        }

        private void ApplyPlayerLock()
        {
            if (_bridge == null || _playerLockApplied) return;
            _bridge.ResolvePlayerController(_playerTransform);
            _bridge.SetPlayerLocked(true);
            _playerLockApplied = true;
        }

        private void ReleasePlayerLock()
        {
            if (_bridge == null || !_playerLockApplied) return;
            _bridge.SetPlayerLocked(false);
            _playerLockApplied = false;
        }

        private void OnLockPlayerToggled(bool enabled)
        {
            if (enabled && _explorerVisible) ApplyPlayerLock();
            else ReleasePlayerLock();
        }

        // ------------------------------------------------------------------
        // Player
        // ------------------------------------------------------------------
        private void TryAcquirePlayerReferences()
        {
            if (_playerTransform != null) return;
            if (Time.unscaledTime < _nextPlayerSearchTime) return;
            _nextPlayerSearchTime = Time.unscaledTime + 0.5f;

            GameObject found = null;
            string source = null;

            try
            {
                found = GameObject.FindGameObjectWithTag("Player");
                if (found != null) source = "tag:Player";
            }
            catch (Exception) { }

            if (found == null && Camera.main != null)
            {
                found = Camera.main.transform.root.gameObject;
                source = "camRoot:" + found.name;
            }

            if (found == null)
            {
                CharacterController cc = FindObjectOfType(typeof(CharacterController)) as CharacterController;
                if (cc != null)
                {
                    found = cc.gameObject;
                    source = "charCtrl:" + found.name;
                }
            }

            if (found == null) return;

            _playerTransform = found.transform;
            _playerRigidbody = found.GetComponentInChildren<Rigidbody>();
            _playerController = found.GetComponentInChildren<CharacterController>();
            _lastPosition = _playerTransform.position;
            _playerSourceDescription = source;

            Logger.LogInfo("Player acquired via " + source);

            if (_bridge != null) _bridge.ResolvePlayerController(_playerTransform);
        }

        private void UpdateVelocity()
        {
            if (_playerTransform == null)
            {
                _computedVelocity = Vector3.zero;
                return;
            }

            if (_playerRigidbody != null) _computedVelocity = _playerRigidbody.velocity;
            else if (_playerController != null) _computedVelocity = _playerController.velocity;
            else
            {
                if (Time.deltaTime > 0f)
                    _computedVelocity = (_playerTransform.position - _lastPosition) / Time.deltaTime;
                _lastPosition = _playerTransform.position;
            }
        }

        // ------------------------------------------------------------------
        // Practice save/restore - position + rotation only, NOT a savestate.
        // ------------------------------------------------------------------
        private void SavePracticePosition()
        {
            if (_playerTransform == null)
            {
                _saveStatusMessage = "No player ref.";
                return;
            }

            _savedPosition = _playerTransform.position;
            _savedRotation = _playerTransform.rotation;
            _hasSavedPosition = true;
            _saveStatusMessage = "Saved " + DateTime.Now.ToString("HH:mm:ss");
        }

        private void LoadPracticePosition()
        {
            if (_playerTransform == null || !_hasSavedPosition)
            {
                _saveStatusMessage = "Nothing saved yet.";
                return;
            }

            if (_playerRigidbody != null)
            {
                _playerRigidbody.velocity = Vector3.zero;
                _playerRigidbody.position = _savedPosition;
                _playerRigidbody.rotation = _savedRotation;
            }
            else if (_playerController != null)
            {
                _playerController.enabled = false;
                _playerTransform.position = _savedPosition;
                _playerTransform.rotation = _savedRotation;
                _playerController.enabled = true;
            }
            else
            {
                _playerTransform.position = _savedPosition;
                _playerTransform.rotation = _savedRotation;
            }

            _saveStatusMessage = "Restored.";
        }

        private void WriteStandardDumps()
        {
            _dumpStatus = "dumping...";
            try
            {
                GameDumper.WriteTypeIndex(Logger);
                GameDumper.WriteSceneHierarchy(Logger);
                GameDumper.WritePlayerSnapshot(Logger, _playerTransform);
                _dumpStatus = "dumps -> " + GameDumper.DumpDirectory;
            }
            catch (Exception ex)
            {
                _dumpStatus = "dump failed - see log";
                Logger.LogError("Dump failed: " + ex);
            }
        }

        // ------------------------------------------------------------------
        // HUD
        // ------------------------------------------------------------------
        private void RefreshHudTextIfDue()
        {
            if (Time.unscaledTime < _nextHudRefreshTime) return;
            _nextHudRefreshTime = Time.unscaledTime + 0.1f;

            _hudTimerLine = "Timer  " + FormatTime(_timerElapsed) + (_timerRunning ? "  [RUN]" : "  [STOP]");
            _hudSpeedLine = "Speed  " + _computedVelocity.magnitude.ToString("F2") + " u/s";
            _hudVelLine = "Vel    " + _computedVelocity.x.ToString("F1") + ", " +
                                      _computedVelocity.y.ToString("F1") + ", " +
                                      _computedVelocity.z.ToString("F1");
            _hudPlayerLine = "Player " + (_playerTransform != null ? _playerSourceDescription : "searching...");

            int items = _bridge != null ? _bridge.GetPossessedItemCount() : -1;
            _hudInventoryLine = "Items  " + (items >= 0 ? items.ToString() : "(inventory not resolved)");
        }

        private void OnGUI()
        {
            try
            {
                // OnGUI runs after LateUpdate, so this is the last chance in the
                // frame to win the cursor fight against the game's own code.
                if (_explorerVisible) ApplyCursorOverride();

                if (_overlayVisible) DrawHudOverlay();
                if (_explorerVisible && _explorer != null) _explorer.Draw(60001);
            }
            catch (Exception ex)
            {
                _overlayVisible = false;
                _explorerVisible = false;
                Logger.LogError("OnGUI() threw, overlay disabled: " + ex);
            }
        }

        private void DrawHudOverlay()
        {
            const int w = 300;
            const int h = 168;
            GUI.Box(new Rect(10, 10, w, h), "Forest Overlay v" + PluginVersion);

            int y = 30;
            const int lh = 18;

            GUI.Label(new Rect(20, y, w - 24, lh), _hudTimerLine); y += lh;
            GUI.Label(new Rect(20, y, w - 24, lh), _hudSpeedLine); y += lh;
            GUI.Label(new Rect(20, y, w - 24, lh), _hudVelLine); y += lh;
            GUI.Label(new Rect(20, y, w - 24, lh), _hudInventoryLine); y += lh;
            GUI.Label(new Rect(20, y, w - 24, lh), _hudPlayerLine); y += lh;
            GUI.Label(new Rect(20, y, w - 24, lh), _saveStatusMessage); y += lh;
            GUI.Label(new Rect(20, y, w - 24, lh), _dumpStatus);
        }

        private static string FormatTime(float seconds)
        {
            TimeSpan ts = TimeSpan.FromSeconds(seconds);
            return ((int)ts.TotalMinutes).ToString("00") + ":" +
                   ts.Seconds.ToString("00") + "." +
                   ts.Milliseconds.ToString("000");
        }
    }
}
