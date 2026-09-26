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
        public const string PluginVersion = "0.24.103";

        private const KeyCode ToggleHudKeyDefault = KeyCode.F5;

        private ModuleHost _host;
        private PlayerRef _player;
        private GameBridge _bridge;
        private InventoryReader _inventory;
        private PlayerStateReader _playerState;
        private PracticeState _practice;
        private readonly Notice _notice = new Notice();
        private GUIStyle _noticeStyle;
        private GameEvents _events;
        private LogKeeper _logs;

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

                // First, so even an inert plugin keeps its session's log.
                int keptLogs = Config.Bind("Diagnostics", "KeptLogs", 3,
                    "How many previous sessions' LogOutput.log to keep in config/ForestOverlay/logs " +
                    "(the game replaces LogOutput.log on every launch).").Value;
                _logs = new LogKeeper(Paths.BepInExRootPath, configDir, keptLogs, Logger);

                // Before any module loads, so they read the shipped lists.
                Data.ShippedData.Install(configDir, Logger);

                // Arms the next update: the patcher is what installs it.
                UpdaterInstaller.Install(Logger);
                UpdateChecker.TidyPluginFolder(System.Reflection.Assembly.GetExecutingAssembly().Location, Logger);

                _bridge = new GameBridge(Logger);
                _player = new PlayerRef(Logger);
                _inventory = new InventoryReader(Logger);
                _playerState = new PlayerStateReader(Logger);
                _practice = new PracticeState();

                // Read-only postfixes on the endgame cutscene methods.
                // Installed before modules so none can miss an event.
                _events = new GameEvents(Logger);
                _events.Install(PluginGuid);

                ModuleContext ctx = new ModuleContext();
                ctx.Log = Logger;
                ctx.Config = Config;
                ctx.Bridge = _bridge;
                ctx.Player = _player;
                ctx.Inventory = _inventory;
                ctx.PlayerState = _playerState;
                ctx.Practice = _practice;
                ctx.Notice = _notice;
                ctx.Events = _events;
                ctx.ConfigDirectory = configDir;
                ctx.Logs = _logs;
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
            host.Register(new CollectiblesModule()); // info-only (100% tracking)
            host.Register(new DumpModule());         // info-only
            host.Register(new ExplorerModule());     // info-only
            host.Register(new DebugViewModule());     // view-only, but holds the player
            host.Register(new PracticeModule());     // PRACTICE ONLY
            host.Register(new SavestateModule());    // PRACTICE ONLY, experimental (phase 0)
            host.Register(new PracticeRunModule());  // info-only (times what practice sets up)
            host.Register(new DeathModule());        // quick-load (normal runs) / revive (practice)
            host.Register(new QaModule());           // info-only (QA team: test list, marks, report)
            host.Register(new BridgeModule());       // PRACTICE ONLY, dev tool, off by default (live test bridge)
            host.Register(new CommunityModule());    // community spots / segments (downloads, never the runner's own file)
        }

        // ------------------------------------------------------------------
        private void Update()
        {
            if (_logs != null) _logs.Tick(Time.realtimeSinceStartup);
            if (_host == null) return;

            try
            {
                _player.Tick();

                // Before modules, so a split fires in the frame its
                // cutscene flag rose.
                if (_events != null) _events.Tick();
                if (_player.Found)
                {
                    _bridge.ResolvePlayerController(_player.Transform);
                    _bridge.ResolveRotators(_player.Transform);
                }

                _host.Tick();

                // Unity's GUI layout pass allocates every frame; only our
                // windows need it (Game/PerfPatches, fix 1). Set here,
                // before this frame's OnGUI.
                useGUILayout = !PerfPatches.OverlayLayout ||
                               (_host.UiVisible && (_host.AnyPanelOpen() || _notice.Active));
            }
            catch (Exception ex)
            {
                Logger.LogError("Update() threw: " + ex);
            }
        }

        private void ToggleAllUi()
        {
            _host.UiVisible = !_host.UiVisible;
            Logger.LogInfo(_host.UiVisible ? "UI shown (show / hide all key)."
                                           : "UI hidden (show / hide all key) - press it again to show.");
        }

        private void ToggleInfoBox()
        {
            _host.HudVisible = !_host.HudVisible;
        }

        private void OnApplicationQuit()
        {
            try { if (_logs != null) _logs.CopyNew(); }
            catch (Exception) { }
        }

        private void OnDestroy()
        {
            try { if (_host != null) _host.Shutdown(); }
            catch (Exception ex) { Logger.LogWarning("OnDestroy: " + ex.Message); }

            if (_events != null) _events.Uninstall();
        }

        // ------------------------------------------------------------------
        private void OnGUI()
        {
            if (_host == null) return;

            try
            {
                if (!_host.UiVisible) return;

                long allocStart = _host.Perf.BeginAlloc();
                bool exact = AllocationTracker.Counting;
                long bytes0 = exact ? AllocationTracker.MainBytes : 0;
                EnsureStyles();
                if (_host.HudVisible) DrawHud();
                _host.DrawPanels();
                if (_notice.Active) DrawNotice();
                _host.Perf.EndAlloc(allocStart);
                if (exact && AllocationTracker.Counting) _host.CountGuiAlloc(AllocationTracker.MainBytes - bytes0);
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

            // Wraps: a long line (an update message, a spot name in the
            // practice marker) was cut off at the box edge.
            _hudLabelStyle = new GUIStyle(GUI.skin.label);
            _hudLabelStyle.padding = new RectOffset(0, 0, 0, 0);
            _hudLabelStyle.wordWrap = true;

            _warnStyle = new GUIStyle(_hudLabelStyle);
            _warnStyle.fontStyle = FontStyle.Bold;
            _warnStyle.normal.textColor = new Color(1f, 0.55f, 0.2f);

            _noticeStyle = new GUIStyle(GUI.skin.box);
            _noticeStyle.fontSize = 14;
            _noticeStyle.wordWrap = true;
            _noticeStyle.alignment = TextAnchor.MiddleCenter;
            _noticeStyle.normal.textColor = new Color(1f, 0.75f, 0.4f);
            // Opaque: the skin's box is see-through, and over the window
            // its text read through the notice.
            Texture2D bg = new Texture2D(1, 1);
            bg.SetPixel(0, 0, new Color(0.08f, 0.08f, 0.08f, 0.95f));
            bg.Apply();
            _noticeStyle.normal.background = bg;
        }

        // Upper middle: clear of the HUD box (top left) and of the game's
        // own messages (bottom left), and where the eye is while playing.
        // A borderless window brought to the front: IMGUI draws every window
        // after all plain controls, so a plain box sat under the main window.
        private const int NoticeWindowId = 59_999;
        private GUI.WindowFunction _noticeWindowFn;

        private void DrawNotice()
        {
            const float w = 560f;
            float h = Mathf.Max(48f, _noticeStyle.CalcHeight(_notice.Content, w) + 16f);
            if (_noticeWindowFn == null) _noticeWindowFn = DrawNoticeWindow;
            GUI.Window(NoticeWindowId, new Rect((Screen.width - w) * 0.5f, Screen.height * 0.18f, w, h),
                _noticeWindowFn, GUIContent.none, GUIStyle.none);
            GUI.BringWindowToFront(NoticeWindowId);
        }

        private void DrawNoticeWindow(int id)
        {
            GUI.Box(new Rect(0f, 0f, 560f, Mathf.Max(48f, _noticeStyle.CalcHeight(_notice.Content, 560f) + 16f)),
                _notice.Content, _noticeStyle);
        }

        private void DrawHud()
        {
            const float w = 330f;
            const float lineHeight = 18f;
            const float textW = w - 24f;

            // Measured every pass (CalcHeight does not allocate) so the box
            // grows with a wrapped line instead of cutting it off.
            int lines = _host.Hud.Count;
            GUIStyle practiceStyle = _practice.Used ? _warnStyle : _hudLabelStyle;
            float textH = Mathf.Max(lineHeight, practiceStyle.CalcHeight(_practice.Label, textW));
            for (int i = 0; i < lines; i++)
                textH += Mathf.Max(lineHeight, _hudLabelStyle.CalcHeight(_host.Hud.At(i), textW));

            GUI.Box(new Rect(10, 10, w, 34f + textH), HudTitle);

            float y = 30f;
            for (int i = 0; i < lines; i++)
            {
                GUIContent line = _host.Hud.At(i);
                float h = Mathf.Max(lineHeight, _hudLabelStyle.CalcHeight(line, textW));
                GUI.Label(new Rect(20, y, textW, h), line, _hudLabelStyle);
                y += h;
            }

            // Sticky and last, so it is the line the eye lands on. A run
            // recording must make it obvious that a practice tool was used.
            GUI.Label(new Rect(20, y, textW, Mathf.Max(lineHeight, practiceStyle.CalcHeight(_practice.Label, textW))),
                      _practice.Label, practiceStyle);
        }
    }
}
