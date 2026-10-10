using System;
using System.IO;
using BepInEx;
using ForestOverlay.Core;
using ForestOverlay.Data;
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
        public const string PluginVersion = "0.24.277";

        private const KeyCode ToggleHudKeyDefault = KeyCode.F5;

        // Wording: the author (T-0246, 2026-10-10).
        private const string ModApiRestartNotice =
            "ForestOverlay: ModAPI found. Restart the game once to turn the overlay on.";
        private const string ModApiFailedNotice =
            "ForestOverlay: could not fix ModAPI's game files, so the overlay is off. Please send us your log.";

        private ModuleHost _host;
        private PlayerRef _player;
        private GameBridge _bridge;
        private InventoryReader _inventory;
        private PlayerStateReader _playerState;
        private PracticeState _practice;
        private readonly Notice _notice = new Notice();
        private GUIStyle _noticeStyle;
        private GameEvents _events;
        private WorldEvents _worldEvents;
        private LogKeeper _logs;

        private GUIStyle _hudLabelStyle;
        private GUIStyle _warnStyle;
        private GUIStyle _hudBoxStyle;
        private int _hudStyleVersion = -1;

        // Built once. Concatenating the title inside OnGUI would allocate
        // on every pass, several times per frame.
        private static readonly GUIContent HudTitle =
            new GUIContent(HudLines.Title(PluginVersion, false));
        private static readonly GUIContent HudTitleCompact =
            new GUIContent(HudLines.Title(PluginVersion, true));
        private static readonly int HudTitleIndex = HudLines.IndexOfKey("ShowTitle");

        // Dragging the info box (Settings -> HUD): only while the window is
        // open (the cursor is free then). Kept here while dragging, clamped
        // live, written once on release (gotchas 60-61).
        private MainWindowModule _mainWindow;
        private bool _hudDragging;
        private Vector2 _hudDragOffset;
        private float _hudDragX, _hudDragY;

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
                Lifecycle.Log = Logger;

                string configDir = Path.Combine(Paths.ConfigPath, PluginName);
                if (!Directory.Exists(configDir)) Directory.CreateDirectory(configDir);

                // First, so even an inert plugin keeps its session's log.
                int keptLogs = Config.Bind("Diagnostics", "KeptLogs", 3,
                    "How many previous sessions' LogOutput.log to keep in config/ForestOverlay/logs " +
                    "(the game replaces LogOutput.log on every launch).").Value;
                _logs = new LogKeeper(Paths.BepInExRootPath, Paths.GameRootPath, configDir, keptLogs, Logger);
                UnityErrorLog.Start(Logger, _logs.Folder, _logs.SessionName + " v" + PluginVersion);

                // Before any module loads, so they read the shipped lists.
                Data.ShippedData.Install(configDir, Logger);

                // Arms the next update: the patcher is what installs it.
                UpdaterInstaller.Install(Logger);
                UpdateChecker.TidyPluginFolder(System.Reflection.Assembly.GetExecutingAssembly().Location, Logger);

                // ModAPI's Assembly-CSharp kills Mono under our patches until
                // the patcher repairs it; the patcher installed just above
                // runs from the next launch (T-0246).
                ModApi.Repair modApi = ModApi.State();
                if (modApi == ModApi.Repair.NeedsRestart)
                {
                    Logger.LogWarning("ModAPI: its Assembly-CSharp is not repaired this launch (the patcher was just installed) - overlay off until the game is restarted.");
                    _notice.Show(ModApiRestartNotice, 20f);
                    return;
                }
                if (modApi == ModApi.Repair.Failed)
                {
                    Logger.LogError("ModAPI: the patcher could not repair its Assembly-CSharp (see the 'ModAPI fix:' line above) - overlay off.");
                    _notice.Show(ModApiFailedNotice, 20f);
                    return;
                }

                _bridge = new GameBridge(Logger);
                LatePass.Log = line => Logger.LogInfo(line);   // log: Late pass
                _player = new PlayerRef(Logger);
                _inventory = new InventoryReader(Logger);
                _playerState = new PlayerStateReader(Logger);
                _practice = new PracticeState();

                // Read-only postfixes on the endgame cutscene methods.
                // Installed before modules so none can miss an event.
                _events = new GameEvents(Logger);
                _events.Install(PluginGuid);
                _worldEvents = new WorldEvents(Logger);

                ModuleContext ctx = new ModuleContext();
                ctx.Log = Logger;
                ctx.Config = Config;
                ctx.Bridge = _bridge;
                ctx.Player = _player;
                ctx.Inventory = _inventory;
                ctx.PlayerState = _playerState;
                ctx.Practice = _practice;
                ctx.Run = new RunMode(Logger, _notice, _practice);
                ctx.Notice = _notice;
                ctx.Events = _events;
                ctx.ConfigDirectory = configDir;
                ctx.Logs = _logs;
                ctx.Runner = this;
                ctx.PluginPath = System.Reflection.Assembly.GetExecutingAssembly().Location;

                _host = new ModuleHost(ctx);
                BuildModules(_host);
                _host.InitialiseAll();
                StallWatch.Start(Logger, _logs != null ? _logs.Folder : null, _host.ModuleNames());

                // Registered through the same table as every module key so
                // it is rebindable and shows up in the settings panel.
                // Two separate switches. "Hide HUD" meaning "hide one box"
                // was surprising: a runner clearing the screen for a
                // recording means all of it.
                _host.Hotkeys.Add("ui.toggleAll", ToggleHudKeyDefault,
                                  "Show / hide ALL overlay UI", ToggleAllUi);
                _host.Hotkeys.Add("ui.toggleInfo", KeyCode.None,
                                  "Show / hide the info box", ToggleInfoBox);

                Logger.LogInfo("Modules: " + _host.Count + " registered.");
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
            host.Register(new RunUploadModule());    // finished runs to forest.deter.cloud (drawn in the Runs tab)
            host.Register(new RunModeModule());      // run mode: a new game = a run, practice locked (drawn in the Runs tab)
            host.Register(new TasModule());          // PRACTICE ONLY, experimental: input record / replay (drawn in the Runs tab; last, so bridge indexes stay)
            host.Register(new MapModule());          // info-only view (its Go is Practice's); after TAS so bridge indexes stay
            host.Register(new PaintModule());        // practice mode only: paint where the crosshair hits (T-0219); last, so bridge indexes stay
        }

        // ------------------------------------------------------------------
        private void Update()
        {
            try
            {
                StallWatch.Beat(Time.frameCount);
                StallWatch.At(StallWatch.Hook.Update, -1);
                UnityErrorLog.Flush();
                if (_logs != null) _logs.Tick(Time.realtimeSinceStartup);
                if (_host == null) return;

                _player.Tick();

                // Before modules, so a split fires in the frame its
                // cutscene flag rose.
                if (_events != null) _events.Tick();
                if (_worldEvents != null) _worldEvents.Tick(_player);
                if (_player.Found)
                {
                    _bridge.ResolvePlayerController(_player.Transform);
                    _bridge.ResolveRotators(_player.Transform);
                }

                _host.Tick();
                _practice.Compact = _host.Hud.Compact;

                // Unity's GUI layout pass allocates every frame; only our
                // windows need it (Game/PerfPatches, fix 1). Set here,
                // before this frame's OnGUI.
                useGUILayout = !PerfPatches.OverlayLayout ||
                               (_host.UiVisible && (_host.AnyPanelOpen() || _notice.Active));
            }
            catch (Exception ex) { Lifecycle.Fail("OverlayPlugin.Update", ex); }
            finally { StallWatch.Leave(); }
        }

        private void ToggleAllUi()
        {
            _host.UiVisible = !_host.UiVisible;
            Logger.LogInfo("UI " + (_host.UiVisible ? "shown (show / hide all key)."
                                                  : "hidden (show / hide all key) - press it again to show."));
        }

        private void ToggleInfoBox()
        {
            _host.HudVisible = !_host.HudVisible;
        }

        // Unfocused with runInBackground off, Unity stops the frames: not a stall.
        private void OnApplicationFocus(bool focused)
        {
            try { StallWatch.SetPaused(!focused && !Application.runInBackground); }
            catch (Exception ex) { Lifecycle.Fail("OverlayPlugin.OnApplicationFocus", ex); }
        }

        private void OnApplicationQuit()
        {
            try { if (_logs != null) _logs.CopyNew(); }
            catch (Exception) { }
        }

        private void OnDestroy()
        {
            try
            {
                try { if (_host != null) _host.Shutdown(); }
                catch (Exception ex) { Logger.LogWarning("OnDestroy: " + ex.Message); }

                if (_events != null) _events.Uninstall();
                UnityErrorLog.Stop();
            }
            catch (Exception ex) { Lifecycle.Fail("OverlayPlugin.OnDestroy", ex); }
        }

        // ------------------------------------------------------------------
        private void OnGUI()
        {
            if (_host == null && !_notice.Active) return;

            try
            {
                // Inert: only a notice set before the modules (ModAPI).
                if (_host == null) { EnsureStyles(); DrawNotice(); return; }

                StallWatch.At(StallWatch.Hook.OnGui, -1);
                // Run mode's code stays on screen with the overlay hidden:
                // a recording needs it (docs/run-mode.md phase 2).
                if (!_host.UiVisible) { _host.DrawScreensAlways(); return; }

                long allocStart = _host.Perf.BeginAlloc();
                bool exact = AllocationTracker.Counting;
                long bytes0 = exact ? AllocationTracker.MainBytes : 0;
                EnsureStyles();
                if (_host.HudVisible)
                {
                    StallWatch.At(StallWatch.Hook.DrawHud, -1);
                    DrawHud();
                }
                _host.DrawScreens();
                _host.DrawScreensAlways();
                _host.DrawPanels();
                if (_notice.Active)
                {
                    StallWatch.At(StallWatch.Hook.Notice, -1);
                    DrawNotice();
                }
                _host.Perf.EndAlloc(allocStart);
                if (exact && AllocationTracker.Counting) _host.CountGuiAlloc(AllocationTracker.MainBytes - bytes0);
            }
            catch (Exception ex)
            {
                // Disable rather than throw every frame; an exception here
                // repeats several times per frame and floods the log.
                if (_host != null) _host.HudVisible = false;
                Logger.LogError("OnGUI() threw, HUD disabled: " + ex);
            }
            finally { StallWatch.Leave(); }
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

            _hudBoxStyle = new GUIStyle(GUI.skin.box);

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
            StallWatch.At(StallWatch.Hook.Notice, -1);
            GUI.Box(new Rect(0f, 0f, 560f, Mathf.Max(48f, _noticeStyle.CalcHeight(_notice.Content, 560f) + 16f)),
                _notice.Content, _noticeStyle);
            StallWatch.Leave();
        }

        // Text size from Settings -> HUD; 0 = the skin's own (the old look).
        private void ApplyHudStyle(HudSettings s)
        {
            _hudStyleVersion = s.Version;
            int px = s.TextSize;
            _hudLabelStyle.fontSize = px;
            _warnStyle.fontSize = px;
            _hudBoxStyle.fontSize = px;
        }

        private void DrawHud()
        {
            HudSettings s = _host.Hud.Settings;
            if (_hudStyleVersion != s.Version) ApplyHudStyle(s);

            int px = s.TextSize;
            float w = HudLines.Width(px, Screen.width);
            float lineHeight = HudLines.LineHeight(px);
            float textW = w - 24f;
            bool title = s.Shows(HudTitleIndex);
            float top = title ? (px <= 0 ? 20f : lineHeight + 2f) : 6f;

            // Measured every pass (CalcHeight does not allocate) so the box
            // grows with a wrapped line instead of cutting it off.
            int lines = _host.Hud.Count;
            GUIStyle practiceStyle = _practice.Warn ? _warnStyle : _hudLabelStyle;
            float markerH = Mathf.Max(lineHeight, practiceStyle.CalcHeight(_practice.Label, textW));
            float onH = _practice.AnyOn ? Mathf.Max(lineHeight, _warnStyle.CalcHeight(_practice.OnLabel, textW)) : 0f;
            float textH = markerH + onH;
            for (int i = 0; i < lines; i++)
                textH += Mathf.Max(lineHeight, _hudLabelStyle.CalcHeight(_host.Hud.At(i), textW));

            float boxH = top + textH + 14f;
            float bx = HudLines.Clamp(_hudDragging ? _hudDragX : s.X, w, Screen.width);
            float by = HudLines.Clamp(_hudDragging ? _hudDragY : s.Y, boxH, Screen.height);
            Rect box = new Rect(bx, by, w, boxH);
            HandleHudDrag(box, s);

            GUI.Box(box, title ? (s.Compact ? HudTitleCompact : HudTitle) : GUIContent.none, _hudBoxStyle);
            if (_hudDragging) GUI.Box(box, GUIContent.none);   // an outline while moving

            float x = bx + 10f;
            float y = by + top;
            for (int i = 0; i < lines; i++)
            {
                GUIContent line = _host.Hud.At(i);
                float h = Mathf.Max(lineHeight, _hudLabelStyle.CalcHeight(line, textW));
                GUI.Label(new Rect(x, y, textW, h), line, _hudLabelStyle);
                y += h;
            }

            // What changes the game now (no stagger, god mode, item caps...),
            // above the marker. Never switchable (honest labelling).
            if (_practice.AnyOn)
            {
                GUI.Label(new Rect(x, y, textW, onH), _practice.OnLabel, _warnStyle);
                y += onH;
            }

            // Sticky and last, so it is the line the eye lands on. A run
            // recording must make it obvious that a practice tool was used.
            // Never switchable either.
            GUI.Label(new Rect(x, y, textW, markerH), _practice.Label, practiceStyle);
        }

        private void HandleHudDrag(Rect box, HudSettings s)
        {
            Event e = Event.current;
            if (e == null) return;
            if (_mainWindow == null) _mainWindow = _host.Find<MainWindowModule>();
            bool windowOpen = _mainWindow != null && _mainWindow.PanelOpen;

            if (!windowOpen)
            {
                if (_hudDragging) EndHudDrag(box, s);
                return;
            }

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button != 0 || !box.Contains(e.mousePosition) || _mainWindow.ScreenRect.Contains(e.mousePosition)) return;
                    _hudDragging = true;
                    _hudDragOffset = e.mousePosition - new Vector2(box.x, box.y);
                    _hudDragX = box.x;
                    _hudDragY = box.y;
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (!_hudDragging) return;
                    _hudDragX = HudLines.Clamp(e.mousePosition.x - _hudDragOffset.x, box.width, Screen.width);
                    _hudDragY = HudLines.Clamp(e.mousePosition.y - _hudDragOffset.y, box.height, Screen.height);
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (!_hudDragging) return;
                    EndHudDrag(box, s);
                    e.Use();
                    break;
            }
        }

        private void EndHudDrag(Rect box, HudSettings s)
        {
            _hudDragging = false;
            s.SetPosition(Mathf.Round(HudLines.Clamp(_hudDragX, box.width, Screen.width)),
                          Mathf.Round(HudLines.Clamp(_hudDragY, box.height, Screen.height)));
            Logger.LogInfo("Info box moved to (" + Mathf.RoundToInt(s.X) + ", " + Mathf.RoundToInt(s.Y) + ").");
        }
    }
}
