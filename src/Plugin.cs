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
        public const string PluginVersion = "0.5.1";

        private const KeyCode ToggleHudKey = KeyCode.F5;

        private ModuleHost _host;
        private PlayerRef _player;
        private GameBridge _bridge;
        private InventoryReader _inventory;
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
                _practice = new PracticeState();

                ModuleContext ctx = new ModuleContext();
                ctx.Log = Logger;
                ctx.Bridge = _bridge;
                ctx.Player = _player;
                ctx.Inventory = _inventory;
                ctx.Practice = _practice;
                ctx.ConfigDirectory = configDir;

                _host = new ModuleHost(ctx);
                BuildModules(_host);
                _host.InitialiseAll();

                Logger.LogInfo(_host.Count + " modules registered.");
                Logger.LogInfo("F5 hud | " + _host.Hotkeys.Describe());
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
            host.Register(new RunInfoModule());      // info-only
            host.Register(new TimerModule());        // info-only
            host.Register(new InventoryModule());    // info-only
            host.Register(new DumpModule());         // info-only
            host.Register(new ExplorerModule());     // info-only
            host.Register(new PracticeModule());     // PRACTICE ONLY
        }

        // ------------------------------------------------------------------
        private void Update()
        {
            if (_host == null) return;

            try
            {
                if (Input.GetKeyDown(ToggleHudKey)) _host.HudVisible = !_host.HudVisible;

                _player.Tick();
                if (_player.Found) _bridge.ResolvePlayerController(_player.Transform);

                _host.Tick();
            }
            catch (Exception ex)
            {
                Logger.LogError("Update() threw: " + ex);
            }
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
