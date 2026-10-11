using System;
using System.Collections.Generic;
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
        public const string PluginVersion = "0.24.267";

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
        private WorldEvents _worldEvents;
        private LogKeeper _logs;

        private GUIStyle _hudLabelStyle;
        private GUIStyle _warnStyle;
        private int _hudStyleVersion = -1;

        // Dragging the HUD column (Settings -> HUD): only while the window is
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
                _logs = new LogKeeper(Paths.BepInExRootPath, configDir, keptLogs, Logger);

                // Before any module loads, so they read the shipped lists.
                Data.ShippedData.Install(configDir, Logger);

                // Arms the next update: the patcher is what installs it.
                UpdaterInstaller.Install(Logger);
                UpdateChecker.TidyPluginFolder(System.Reflection.Assembly.GetExecutingAssembly().Location, Logger);

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

                // Registered through the same table as every module key so
                // it is rebindable and shows up in the settings panel.
                // Two separate switches. "Hide HUD" meaning "hide one box"
                // was surprising: a runner clearing the screen for a
                // recording means all of it.
                _host.Hotkeys.Add("ui.toggleAll", ToggleHudKeyDefault,
                                  "Show / hide ALL overlay UI", ToggleAllUi);
                _host.Hotkeys.Add("ui.toggleInfo", KeyCode.None,
                                  "Show / hide the HUD values", ToggleInfoBox);

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
            host.Register(new DeveloperModule());    // the Developer tab: hosts other modules' developer folds; last, so bridge indexes stay
        }

        // ------------------------------------------------------------------
        private void Update()
        {
            try
            {
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
            }
            catch (Exception ex) { Lifecycle.Fail("OverlayPlugin.OnDestroy", ex); }
        }

        // ------------------------------------------------------------------
        private void OnGUI()
        {
            if (_host == null) return;

            try
            {
                // Run mode's code stays on screen with the overlay hidden:
                // a recording needs it (docs/run-mode.md phase 2).
                if (!_host.UiVisible) { _host.DrawScreensAlways(); return; }

                long allocStart = _host.Perf.BeginAlloc();
                bool exact = AllocationTracker.Counting;
                long bytes0 = exact ? AllocationTracker.MainBytes : 0;
                EnsureStyles();
                if (_host.HudVisible) DrawHud();
                _host.DrawScreens();
                _host.DrawScreensAlways();
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
            _hudLabelStyle = UiKit.Style(GUI.skin.label);
            _hudLabelStyle.padding = new RectOffset(0, 0, 0, 0);
            _hudLabelStyle.wordWrap = true;

            _warnStyle = UiKit.Style(_hudLabelStyle);
            _warnStyle.fontStyle = FontStyle.Bold;
            _warnStyle.normal.textColor = new Color(1f, 0.55f, 0.2f);

            // The toast's text; its card is UiKit's (opaque, rounded).
            _noticeStyle = UiKit.Style(GUI.skin.label);
            _noticeStyle.fontSize = 14;
            _noticeStyle.wordWrap = true;
            _noticeStyle.alignment = TextAnchor.MiddleLeft;
            _noticeStyle.padding = new RectOffset(0, 0, 0, 0);
            _noticeStyle.normal.textColor = UiKit.TextColour;
        }

        // Upper middle: clear of the HUD box (top left) and of the game's
        // own messages (bottom left), and where the eye is while playing.
        // A borderless window brought to the front: IMGUI draws every window
        // after all plain controls, so a plain box sat under the main window.
        private const int NoticeWindowId = 59_999;
        private GUI.WindowFunction _noticeWindowFn;

        // A toast (author, 2026-10-05): slides down from the top edge, a thin
        // bar runs out while it shows, slides back up at the end.
        private const float ToastSlide = 0.25f;
        private float _toastW, _toastH;

        private void DrawNotice()
        {
            float w = Mathf.Min(520f, Screen.width - 32f);
            float h = Mathf.Max(44f, _noticeStyle.CalcHeight(_notice.Content, w - 28f) + 22f);
            float t = _notice.Elapsed, d = _notice.Duration;
            float k = Mathf.Min(Mathf.Clamp01(t / ToastSlide), Mathf.Clamp01((d - t) / ToastSlide));
            k = k * k * (3f - 2f * k);
            _toastW = w;
            _toastH = h;
            if (_noticeWindowFn == null) _noticeWindowFn = DrawNoticeWindow;
            GUI.Window(NoticeWindowId, new Rect((Screen.width - w) * 0.5f, Mathf.Lerp(-h - 4f, 28f, k), w, h),
                _noticeWindowFn, GUIContent.none, GUIStyle.none);
            GUI.BringWindowToFront(NoticeWindowId);
        }

        private void DrawNoticeWindow(int id)
        {
            GUI.Box(new Rect(0f, 0f, _toastW, _toastH), GUIContent.none, UiKit.WidgetCard);
            GUI.Label(new Rect(14f, 8f, _toastW - 28f, _toastH - 18f), _notice.Content, _noticeStyle);
            float left = 1f - Mathf.Clamp01(_notice.Elapsed / Mathf.Max(0.01f, _notice.Duration));
            GUI.DrawTexture(new Rect(8f, _toastH - 6f, (_toastW - 16f) * left, 2f), UiKit.AccentTexture);
        }

        // Text size from Settings -> HUD; 0 = the skin's own (the old look).
        private void ApplyHudStyle(HudSettings s)
        {
            _hudStyleVersion = s.Version;
            int px = s.TextSize;
            _hudLabelStyle.fontSize = px;
            _warnStyle.fontSize = px;
        }

        // The HUD (T-0018): every ticked value, in the COLUMN at the HUD
        // position - the pre-overhaul info box's order, title first, value
        // only with the runner's own text around it, no backing (author,
        // 2026-10-10) - then what changes the game (ON NOW) and the practice
        // marker, always (honest labelling). A value placed on its own
        // (HudWidgets) leaves the column; a value not showing leaves no gap.
        private readonly List<float> _colH = new List<float>();
        private readonly List<float> _colW = new List<float>();
        private const float GripW = 10f;

        private void DrawHud()
        {
            HudSettings s = _host.Hud.Settings;
            if (_hudStyleVersion != s.Version) ApplyHudStyle(s);

            int px = s.TextSize;
            float wrapW = HudLines.Width(px, Screen.width) - 24f;
            float lineHeight = HudLines.LineHeight(px);
            HudBuilder hud = _host.Hud;
            HudWidgets widgets = hud.Widgets;
            bool editing = widgets != null && widgets.Editing;

            // Measured every pass (CalcSize / CalcHeight do not allocate) so
            // a long line wraps instead of being cut off.
            int lines = hud.Count;
            while (_colH.Count < lines) { _colH.Add(0f); _colW.Add(0f); }
            float colW = 0f, colH = 0f;
            for (int i = 0; i < lines; i++)
            {
                if (widgets != null && widgets.IsFree(hud.LineIndex(i))) continue;
                GUIContent c = hud.ValueAt(i);
                float cw = Mathf.Min(wrapW, _hudLabelStyle.CalcSize(c).x);
                float ch = Mathf.Max(lineHeight, _hudLabelStyle.CalcHeight(c, wrapW));
                _colW[i] = cw;
                _colH[i] = ch;
                colW = Mathf.Max(colW, cw);
                colH += ch;
            }
            GUIStyle practiceStyle = _practice.Warn ? _warnStyle : _hudLabelStyle;
            float onH = _practice.AnyOn ? Mathf.Max(lineHeight, _warnStyle.CalcHeight(_practice.OnLabel, wrapW)) : 0f;
            // Nothing while clean (author, 2026-10-10).
            bool marked = _practice.Label.text.Length > 0;
            float markH = marked ? Mathf.Max(lineHeight, practiceStyle.CalcHeight(_practice.Label, wrapW)) : 0f;
            if (onH > 0f) colW = Mathf.Max(colW, Mathf.Min(wrapW, _warnStyle.CalcSize(_practice.OnLabel).x));
            if (marked) colW = Mathf.Max(colW, Mathf.Min(wrapW, practiceStyle.CalcSize(_practice.Label).x));
            colH += onH + markH;
            // Nothing to show: still a spot to see and move while the window is open.
            if (_mainWindow != null && _mainWindow.PanelOpen)
            {
                colW = Mathf.Max(colW, 60f);
                colH = Mathf.Max(colH, lineHeight);
            }

            float x = HudLines.Clamp(_hudDragging ? _hudDragX : s.X, colW, Screen.width);
            float y = HudLines.Clamp(_hudDragging ? _hudDragY : s.Y, colH, Screen.height);
            Rect col = new Rect(x, y, colW, colH);

            if (_mainWindow == null) _mainWindow = _host.Find<MainWindowModule>();
            Rect blocked = _mainWindow != null ? _mainWindow.ScreenRect : new Rect();
            bool windowOpen = _mainWindow != null && _mainWindow.PanelOpen;

            // Edit mode: a press on a value places it on its own (HudWidgets);
            // the column moves by its grip only. Window open, not editing:
            // the whole column drags, as the box did.
            Rect grip = new Rect(col.xMax + 6f, y, GripW, colH);
            if (grip.xMax > Screen.width) grip.x = col.x - 6f - GripW;
            if (editing)
            {
                float ly = y;
                for (int i = 0; i < lines; i++)
                {
                    int li = hud.LineIndex(i);
                    if (widgets.IsFree(li)) continue;
                    widgets.ColumnLineEvent(new Rect(x, ly, _colW[i], _colH[i]), li, blocked);
                    ly += _colH[i];
                }
                HandleHudDrag(grip, col, s);
            }
            else HandleHudDrag(col, col, s);

            // A placed value dragged over the column: dropping it puts it back.
            if (editing && widgets.DropOnColumn) GUI.Box(new Rect(col.x - 4f, col.y - 2f, col.width + 8f, col.height + 4f), GUIContent.none, UiKit.WidgetCard);
            if (editing || windowOpen || _hudDragging)
                GUI.Box(new Rect(col.x - 4f, col.y - 2f, col.width + 8f, col.height + 4f), GUIContent.none, UiKit.Outline);
            if (editing) GUI.Box(grip, GUIContent.none, UiKit.Handle);

            float yy = y;
            for (int i = 0; i < lines; i++)
            {
                if (widgets != null && widgets.IsFree(hud.LineIndex(i))) continue;
                GUI.Label(new Rect(x, yy, wrapW, _colH[i]), hud.ValueAt(i), _hudLabelStyle);
                yy += _colH[i];
            }

            // What changes the game now (no stagger, god mode, item caps...),
            // above the marker. Never switchable (honest labelling).
            if (onH > 0f)
            {
                GUI.Label(new Rect(x, yy, wrapW, onH), _practice.OnLabel, _warnStyle);
                yy += onH;
            }

            // Sticky and last, so it is the line the eye lands on. A run
            // recording must make it obvious that a practice tool was used.
            // Never switchable either.
            if (marked) GUI.Label(new Rect(x, yy, wrapW, markH), _practice.Label, practiceStyle);

            // The values placed on their own (and, in edit mode, their handles);
            // a placed value dropped on the column goes back into it.
            if (widgets != null)
            {
                widgets.ColumnRect = col;
                widgets.Draw(hud, blocked);
            }
        }

        // `hit`: where a press starts the drag; `col`: the column, whose
        // size keeps it on screen.
        private void HandleHudDrag(Rect hit, Rect col, HudSettings s)
        {
            Event e = Event.current;
            if (e == null) return;
            if (_mainWindow == null) _mainWindow = _host.Find<MainWindowModule>();
            bool windowOpen = _mainWindow != null && _mainWindow.PanelOpen;

            if (!windowOpen)
            {
                if (_hudDragging) EndHudDrag(col, s);
                return;
            }

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button != 0 || !hit.Contains(e.mousePosition) || _mainWindow.ScreenRect.Contains(e.mousePosition)) return;
                    _hudDragging = true;
                    _hudDragOffset = e.mousePosition - new Vector2(col.x, col.y);
                    _hudDragX = col.x;
                    _hudDragY = col.y;
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (!_hudDragging) return;
                    _hudDragX = HudLines.Clamp(e.mousePosition.x - _hudDragOffset.x, col.width, Screen.width);
                    _hudDragY = HudLines.Clamp(e.mousePosition.y - _hudDragOffset.y, col.height, Screen.height);
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (!_hudDragging) return;
                    EndHudDrag(col, s);
                    e.Use();
                    break;
            }
        }

        private void EndHudDrag(Rect col, HudSettings s)
        {
            _hudDragging = false;
            s.SetPosition(Mathf.Round(HudLines.Clamp(_hudDragX, col.width, Screen.width)),
                          Mathf.Round(HudLines.Clamp(_hudDragY, col.height, Screen.height)));
            Logger.LogInfo("HUD column moved to (" + Mathf.RoundToInt(s.X) + ", " + Mathf.RoundToInt(s.Y) + ").");
        }
    }
}
