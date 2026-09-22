using System;
using System.IO;
using BepInEx;
using ForestOverlay.Core;
using ForestOverlay.Game;
using ForestOverlay.Modules;
using UnityEngine;

namespace ForestOverlay
{
    // ------------------------------------------------------------------
    // v0.5.0
    //
    //   * Restructured into modules. Plugin now only does lifecycle and
    //     composition; every feature is a self-contained OverlayModule
    //     and adding one is a class plus a line in BuildModules().
    //   * Cursor unlock actually works. v0.4.0 fought Cursor.lockState and
    //     lost, because TheForest.UI.VirtualCursor.LateUpdate re-locks it
    //     (which warps the pointer to screen centre) whenever
    //     TheForest.Utils.Input.IsMouseLocked is true. We now flip that
    //     flag instead, which is what the ESC menu does. See
    //     Core/CursorController.
    //   * Per-item inventory breakdown, with pinnable HUD counters.
    //   * Teleport library loaded from text files, so spots can be
    //     contributed without touching code.
    //   * Sticky PRACTICE marker whenever a state-altering tool is used.
    // ------------------------------------------------------------------

    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class OverlayPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.deter.forestoverlay";
        public const string PluginName = "ForestOverlay";
        public const string PluginVersion = "0.12.1";

        private const KeyCode ToggleHudKeyDefault = KeyCode.F5;

        private ModuleHost _host;
        private PlayerRef _player;
        private GameBridge _bridge;
        private InventoryReader _inventory;
        private PlayerStateReader _playerState;
        private PracticeState _practice;

        private GUIStyle _hudLabelStyle;
        private GUIStyle _warnStyle;

        // Built once. Concatenating the title inside OnGUI would allocate
        // on every pass, several times per frame.
        private static readonly GUIContent HudTitle =
            new GUIContent("Forest Overlay v" + PluginVersion);

        // ------------------------------------------------------------------
        private void Awake()
        {
            // Every lifecycle method is individually guarded. A throwing
            // Awake kills the plugin silently while BepInEx still reports
            // it as loaded - that has already cost this project a debugging
            // session once.
            try
            {
                Logger.LogInfo(PluginName + " v" + PluginVersion + " loading (net35 / Unity 5.6).");

                string configDir = Path.Combine(Paths.ConfigPath, PluginName);
                if (!Directory.Exists(configDir)) Directory.CreateDirectory(configDir);

                _bridge = new GameBridge(Logger);
                _player = new PlayerRef(Logger);
                _inventory = new InventoryReader(Logger);
                _playerState = new PlayerStateReader(Logger);
                _practice = new PracticeState();

                ModuleContext ctx = new ModuleContext();
                ctx.Log = Logger;
                ctx.Config = Config;
                ctx.Bridge = _bridge;
                ctx.Player = _player;
                ctx.Inventory = _inventory;
                ctx.PlayerState = _playerState;
                ctx.Practice = _practice;
                ctx.ConfigDirectory = configDir;
                ctx.Runner = this;
                ctx.PluginPath = System.Reflection.Assembly.GetExecutingAssembly().Location;

                _host = new ModuleHost(ctx);
                BuildModules(_host);
                _host.InitialiseAll();

                // Registered through the same table as every module key so
                // it is rebindable and shows up in the settings panel.
                // Two separate switches. "Hide HUD" meaning "hide one box"
                // was surprising: a runner clearing the screen for a
                // recording means all of it.
                _host.Hotkeys.Add("ui.toggleAll", ToggleHudKeyDefault,
                                  "Show / hide ALL overlay UI", ToggleAllUi);
                _host.Hotkeys.Add("ui.toggleInfo", KeyCode.None,
                                  "Show / hide the info box", ToggleInfoBox);

                Logger.LogInfo(_host.Count + " modules registered.");
                Logger.LogInfo("Keys: " + _host.Hotkeys.Describe());
            }
            catch (Exception ex)
            {
                Logger.LogError("Awake() threw - overlay will be inert: " + ex);
            }
        }

        // ------------------------------------------------------------------
        // The whole registration surface. Adding a feature is one line.
        // ------------------------------------------------------------------
        private static void BuildModules(ModuleHost host)
        {
            host.Register(new MainWindowModule());   // the shell every tab lives in
            host.Register(new UpdateModule());       // info-only
            host.Register(new SettingsModule());     // info-only
            host.Register(new RunInfoModule());      // info-only
            // TimerModule is deliberately NOT registered. Its manual
            // start/stop/split clashed with the practice run keys and has
            // no purpose until we decide how timing should work - the
            // direction is automatic splits driven by configuration, the
            // way the author's LiveSplit autosplitter does it. The file is
            // kept so that work has somewhere to land.
            host.Register(new InventoryModule());    // info-only
            host.Register(new DumpModule());         // info-only
            host.Register(new ExplorerModule());     // info-only
            host.Register(new DebugViewModule());     // view-only, but holds the player
            host.Register(new PracticeModule());     // PRACTICE ONLY
            host.Register(new PracticeRunModule());  // info-only (times what practice sets up)
        }

        // ------------------------------------------------------------------
        private void Update()
        {
            if (_host == null) return;

            try
            {
                _player.Tick();
                if (_player.Found)
                {
                    _bridge.ResolvePlayerController(_player.Transform);
                    _bridge.ResolveRotators(_player.Transform);
                }

                _host.Tick();
            }
            catch (Exception ex)
            {
                Logger.LogError("Update() threw: " + ex);
            }
        }

        private void ToggleAllUi()
        {
            _host.UiVisible = !_host.UiVisible;
        }

        private void ToggleInfoBox()
        {
            _host.HudVisible = !_host.HudVisible;
        }

        private void OnDestroy()
        {
            try { if (_host != null) _host.Shutdown(); }
            catch (Exception ex) { Logger.LogWarning("OnDestroy: " + ex.Message); }
        }

        // ------------------------------------------------------------------
        private void OnGUI()
        {
            if (_host == null) return;

            try
            {
                if (!_host.UiVisible) return;

                EnsureStyles();
                if (_host.HudVisible) DrawHud();
                _host.DrawPanels();
            }
            catch (Exception ex)
            {
                // Disable rather than throw every frame; an exception here
                // repeats several times per frame and floods the log.
                _host.HudVisible = false;
                Logger.LogError("OnGUI() threw, HUD disabled: " + ex);
            }
        }

        private void EnsureStyles()
        {
            if (_hudLabelStyle != null) return;

            _hudLabelStyle = new GUIStyle(GUI.skin.label);
            _hudLabelStyle.padding = new RectOffset(0, 0, 0, 0);

            _warnStyle = new GUIStyle(_hudLabelStyle);
            _warnStyle.fontStyle = FontStyle.Bold;
            _warnStyle.normal.textColor = new Color(1f, 0.55f, 0.2f);
        }

        private void DrawHud()
        {
            const int w = 330;
            const int lineHeight = 18;

            int lines = _host.Hud.Count;
            int h = 34 + (lines + 1) * lineHeight;

            GUI.Box(new Rect(10, 10, w, h), HudTitle);

            int y = 30;
            for (int i = 0; i < lines; i++)
            {
                GUI.Label(new Rect(20, y, w - 24, lineHeight), _host.Hud.At(i), _hudLabelStyle);
                y += lineHeight;
            }

            // Sticky and last, so it is the line the eye lands on. A run
            // recording must make it obvious that a practice tool was used.
            GUI.Label(new Rect(20, y, w - 24, lineHeight),
                      _practice.Label,
                      _practice.Used ? _warnStyle : _hudLabelStyle);
        }
    }
}
