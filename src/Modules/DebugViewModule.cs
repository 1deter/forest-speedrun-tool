using System.Collections.Generic;
using BepInEx.Configuration;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Debug views for theory testing: freecam, collider and trigger
    // volumes, global wireframe.
    //
    // None of these are provided by the game. TheForest.DebugConsole has
    // 256 commands but nothing for wireframe, triggers or a detached
    // camera (_follow follows a target, it does not detach), so these are
    // drawn and driven by the plugin - see Game/DebugDraw.cs.
    //
    // Freecam moves the VIEW, not the player, so it does not write game
    // state. It still holds the player still while active, which does, so
    // it is reported as practice.
    //
    // HOLDING THE PLAYER. Freecam used to lock the player itself, and the
    // host released that lock the moment the window closed - so the body
    // walked off with the freecam's WASD. It now declares HoldsPlayer and
    // the host keeps the lock (and blocks game input) for as long as
    // freecam is on, window or no window.
    //
    // Volume drawing centres on the freecam camera while it is on: the
    // point of flying the camera somewhere is to look at what is there.
    //
    // The game profiler (Game/GameProfiler) lives here too: a debug switch,
    // off at every launch; `Diagnostics.GameProfilerExtra` adds methods.
    // So does the allocation tracker (Game/AllocationTracker): exact
    // allocation by type, and by method while the profiler runs;
    // `Diagnostics.AllocationTrackerAtStartup` installs it at launch so
    // plain objects are seen too.
    // ------------------------------------------------------------------
    public sealed class DebugViewModule : OverlayModule
    {
        private const float Row = 24f;
        private const float MinSizeLimit = 1f;
        private const float MaxSizeLimit = 200f;
        private const float SaveDelay = 1f;

        public override string Id { get { return "debugview"; } }
        public override string DisplayName { get { return "Debug views"; } }
        public override bool HasTab { get { return true; } }
        public override string TabTitle { get { return "Debug views"; } }
        public override int TabOrder { get { return 50; } }

        public override bool HoldsPlayer { get { return _freeCamOn; } }

        private GameObject _host;
        private DebugDrawBehaviour _draw;
        private WireframeBehaviour _wireframe;
        private FreeCamBehaviour _freeCam;

        private bool _freeCamOn;
        private bool _wireOn;
        private float _radius = 30f;
        private string _status = "";

        // Filters, persisted. Written on a short delay rather than on every
        // slider step - a ConfigEntry write saves the whole file.
        private ConfigEntry<bool> _limitSizeCfg;
        private ConfigEntry<float> _maxSizeCfg;
        private ConfigEntry<string> _excludeCfg;
        private bool _limitSize;
        private float _maxSize;
        private string _excludeText;
        private float _saveAt = -1f;

        // Labels are built in Tick; OnGUI runs several times a frame.
        private string _radiusLabel = "";
        private string _sizeLabel = "";
        private string _hiddenLabel = "";
        private readonly string[] _largestLabels = new string[5];
        private readonly string[] _largestNames = new string[5];
        private int _largestCount;
        private float _nextLabelBuild;
        private int _builtRadius = -1;
        private int _builtSize = -1;

        private GameProfiler _profiler;
        private ConfigEntry<string> _profilerExtraCfg;
        private ConfigEntry<bool> _allocAtStartupCfg;
        private PerfPatches _perf;
        private LoadTiming _loadTiming;
        private const float AllocInterval = 30f;
        private float _allocWindowStart;
        private string _allocReport = "";

        private Vector2 _scroll;
        private float _contentHeight = 600f;

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);

            // One hidden host object carries the behaviours. DontDestroyOnLoad
            // so a level change does not silently drop them.
            _host = new GameObject("ForestOverlay_Debug");
            _host.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(_host);

            _draw = _host.AddComponent<DebugDrawBehaviour>();
            _freeCam = _host.AddComponent<FreeCamBehaviour>();

            _limitSizeCfg = Ctx.Config.Bind("DebugViews", "LimitVolumeSize", true,
                "Hide collider/trigger volumes whose largest side exceeds MaxVolumeSize.");
            _maxSizeCfg = Ctx.Config.Bind("DebugViews", "MaxVolumeSize", 40f,
                "Largest volume drawn, in metres, when LimitVolumeSize is on.");
            _excludeCfg = Ctx.Config.Bind("DebugViews", "ExcludeNames", "",
                "Comma-separated name fragments; volumes whose GameObject name contains one are not drawn.");

            _limitSize = _limitSizeCfg.Value;
            _maxSize = Mathf.Clamp(_maxSizeCfg.Value, MinSizeLimit, MaxSizeLimit);
            _excludeText = _excludeCfg.Value ?? "";
            ApplyFilters();

            _profilerExtraCfg = Ctx.Config.Bind("Diagnostics", "GameProfilerExtra", "",
                "Extra game methods the game profiler (Debug views) times: \"Type::Method\" or \"Type::*\", comma-separated. " +
                "Read when the profiler is switched on.");
            _profiler = new GameProfiler(Ctx.Log, OverlayPlugin.PluginGuid);

            _allocAtStartupCfg = Ctx.Config.Bind("Diagnostics", "AllocationTrackerAtStartup", false,
                "Install the allocation tracker (Debug views) when the game starts, so every allocation is seen - " +
                "a small cost on each allocation for the whole session. Off: it installs when first switched on and " +
                "misses plain objects from code the game already ran.");
            if (_allocAtStartupCfg.Value) AllocationTracker.Install(Ctx.Log, true);

            _perf = new PerfPatches(Ctx.Log, Ctx.Config, OverlayPlugin.PluginGuid);
            _loadTiming = new LoadTiming(Ctx.Log, OverlayPlugin.PluginGuid);
            _loadTiming.Install();
        }

        /// A performance patch on / off (Debug views; the bridge calls this).
        public void TogglePerfPatch(int i)
        {
            if (_perf != null && i >= 0 && i < _perf.Count) _perf.Toggle(i);
        }

        /// The allocation tracker on / off (Debug views; the bridge calls
        /// this). Off logs a last report.
        public void ToggleAllocations()
        {
            if (AllocationTracker.Counting)
            {
                LogAllocations();
                AllocationTracker.Stop();
                return;
            }
            AllocationTracker.Start(Ctx.Log);
            _allocWindowStart = Time.unscaledTime;
        }

        private void LogAllocations()
        {
            List<string> lines = AllocationTracker.Report();
            string ours = Host != null ? Host.TakeAllocReport(Time.unscaledTime - _allocWindowStart) : "";
            if (ours.Length > 0) lines.Add(ours);
            for (int i = 0; i < lines.Count; i++) Ctx.Log.LogInfo(i == 0 ? lines[i] : "  " + lines[i]);
            _allocReport = string.Join("\n", lines.ToArray());
        }

        /// The game profiler on / off (Debug views; the bridge calls this).
        public void ToggleProfiler()
        {
            if (_profiler.Active) _profiler.Stop();
            else _profiler.Start(_profilerExtraCfg.Value);
        }

        public override void RegisterHotkeys(HotkeyMap map)
        {
            // F1 is deliberately left unbound - it opens the game's own
            // developer console when that is enabled, and a runner who has
            // not rebound yet would get both.
            map.Add("tab.debugview", KeyCode.None, "Open Debug views tab", OpenMyTab);
            map.Add("debug.freecam", KeyCode.KeypadMultiply, "Toggle freecam", ToggleFreeCam);
        }

        public override void Tick()
        {
            // Freecam ends itself when the game tears its camera down (a
            // level load); stop holding the player when it does.
            if (_freeCamOn && !_freeCam.Active)
            {
                _freeCamOn = false;
                _status = "freecam ended (camera changed)";
            }

            // With the window open the mouse is on buttons and the keys are
            // typing into fields; the view should not fly around meanwhile.
            _freeCam.InputEnabled = Host == null || !Host.AnyPanelOpen();

            if (_draw != null)
            {
                _draw.Origin = _freeCam.Active ? _freeCam.Camera.transform : Ctx.Player.Transform;
                _draw.Radius = _radius;
            }

            // The wireframe hook has to live on whichever camera is
            // actually rendering, and that changes when freecam starts.
            if (_wireOn) AttachWireframe();

            if (_saveAt >= 0f && Time.unscaledTime >= _saveAt) SaveFilters();

            _profiler.Tick();
            _loadTiming.Tick();
            if (AllocationTracker.Counting && Time.unscaledTime - _allocWindowStart >= AllocInterval)
            {
                LogAllocations();
                AllocationTracker.Start(Ctx.Log);
                _allocWindowStart = Time.unscaledTime;
            }

            BuildLabels();
        }

        // ------------------------------------------------------------------
        private void ToggleFreeCam()
        {
            if (_freeCam == null) return;

            if (_freeCamOn)
            {
                _freeCam.End();
                _freeCamOn = false;
                _status = "freecam off";
            }
            else
            {
                Camera cam = Camera.main;
                if (cam == null) { _status = "no main camera"; return; }

                _freeCam.Begin(cam);
                _freeCamOn = true;

                // The host holds the player (HoldsPlayer), which writes
                // game state.
                Ctx.Practice.Mark("freecam");
                _status = "freecam on - close the window to fly";
            }
        }

        private Camera RenderingCamera()
        {
            return _freeCam.Active ? _freeCam.Camera : Camera.main;
        }

        private void AttachWireframe()
        {
            Camera cam = RenderingCamera();
            if (cam == null) return;

            if (_wireframe != null && _wireframe.gameObject == cam.gameObject)
            {
                _wireframe.Enabled = _wireOn;
                return;
            }

            if (_wireframe != null) _wireframe.Enabled = false;

            _wireframe = cam.gameObject.GetComponent<WireframeBehaviour>();
            if (_wireframe == null) _wireframe = cam.gameObject.AddComponent<WireframeBehaviour>();
            _wireframe.Enabled = _wireOn;
        }

        private void SetWireframe(bool on)
        {
            _wireOn = on;
            if (on) AttachWireframe();
            else if (_wireframe != null) _wireframe.Enabled = false;
        }

        // ------------------------------------------------------------------
        private void ApplyFilters()
        {
            _draw.MaxSize = _limitSize ? _maxSize : 0f;
            _draw.Exclude = VolumeFilter.ParseExclude(_excludeText);
            _draw.RefreshSoon();
        }

        private void FiltersChanged()
        {
            ApplyFilters();
            _saveAt = Time.unscaledTime + SaveDelay;
            _nextLabelBuild = 0f;
        }

        private void SaveFilters()
        {
            _saveAt = -1f;
            _limitSizeCfg.Value = _limitSize;
            _maxSizeCfg.Value = _maxSize;
            _excludeCfg.Value = _excludeText;
        }

        private void BuildLabels()
        {
            int r = Mathf.RoundToInt(_radius);
            if (r != _builtRadius)
            {
                _builtRadius = r;
                _radiusLabel = "Radius " + r + " m" + (_freeCamOn ? " around the freecam" : " around you");
            }

            int s = Mathf.RoundToInt(_maxSize);
            if (s != _builtSize)
            {
                _builtSize = s;
                _sizeLabel = " Hide volumes larger than " + s + " m";
            }

            if (Time.unscaledTime < _nextLabelBuild) return;
            _nextLabelBuild = Time.unscaledTime + 0.5f;

            // Rebuilt on the draw's own refresh rate; the radius label also
            // depends on freecam, so refresh it here too.
            _builtRadius = -1;

            if (!_draw.ShowColliders && !_draw.ShowTriggers)
            {
                _hiddenLabel = "";
                _largestCount = 0;
                return;
            }

            _hiddenLabel = "Hidden: " + _draw.HiddenBySize + " by size, " +
                           _draw.HiddenByName + " by name";

            LargestList l = _draw.Largest;
            _largestCount = Mathf.Min(l.Count, _largestLabels.Length);
            for (int i = 0; i < _largestCount; i++)
            {
                _largestNames[i] = l.Names[i];
                _largestLabels[i] = l.Names[i] + "  (" + l.Sizes[i].ToString("F0") + " m)";
            }
        }

        // ------------------------------------------------------------------
        public override void ContributeHud(HudBuilder hud)
        {
            if (_freeCamOn) hud.Pair("Cam", "FREECAM");
        }

        public override void DrawTab(Rect area)
        {
            float w = area.width - 20f;
            Rect content = new Rect(0, 0, w, _contentHeight);
            _scroll = GUI.BeginScrollView(area, _scroll, content);

            float y = 4f;

            // --- views ------------------------------------------------------
            bool freecam = GUI.Toggle(new Rect(12, y, w - 24, 22), _freeCamOn, " Freecam");
            if (freecam != _freeCamOn) ToggleFreeCam();
            y += Row;

            bool wire = GUI.Toggle(new Rect(12, y, w - 24, 22), _wireOn, " Wireframe");
            if (wire != _wireOn) SetWireframe(wire);
            y += Row;

            bool cols = GUI.Toggle(new Rect(12, y, w - 24, 22), _draw.ShowColliders, " Colliders (green)");
            if (cols != _draw.ShowColliders) { _draw.ShowColliders = cols; _draw.RefreshSoon(); }
            y += Row;

            bool trigs = GUI.Toggle(new Rect(12, y, w - 24, 22), _draw.ShowTriggers, " Triggers (orange)");
            if (trigs != _draw.ShowTriggers) { _draw.ShowTriggers = trigs; _draw.RefreshSoon(); }
            y += Row + 6f;

            GUI.Label(new Rect(12, y, w - 24, 20), _radiusLabel);
            y += 20f;
            _radius = GUI.HorizontalSlider(new Rect(12, y + 4, w - 24, 20), _radius, 5f, 120f);
            y += Row + 6f;

            // --- filters ----------------------------------------------------
            bool limit = GUI.Toggle(new Rect(12, y, w - 24, 22), _limitSize, _sizeLabel);
            if (limit != _limitSize) { _limitSize = limit; FiltersChanged(); }
            y += Row;

            if (_limitSize)
            {
                float size = GUI.HorizontalSlider(new Rect(12, y + 4, w - 24, 20), _maxSize,
                                                  MinSizeLimit, MaxSizeLimit);
                if (!Mathf.Approximately(size, _maxSize)) { _maxSize = size; FiltersChanged(); }
                y += Row;
            }

            GUI.Label(new Rect(12, y, w - 24, 20), "Hide names containing (comma-separated):");
            y += 20f;
            string text = GUI.TextField(new Rect(12, y, w - 24, 22), _excludeText);
            if (text != _excludeText) { _excludeText = text; FiltersChanged(); }
            y += Row + 2f;

            if (_hiddenLabel.Length > 0)
            {
                GUI.Label(new Rect(12, y, w - 24, 20), _hiddenLabel);
                y += 22f;
            }

            if (_largestCount > 0)
            {
                GUI.Label(new Rect(12, y, w - 24, 20), "Largest drawn - Hide adds the name to the list:");
                y += 22f;

                for (int i = 0; i < _largestCount; i++)
                {
                    if (GUI.Button(new Rect(12, y, 56, 20), "Hide"))
                    {
                        _excludeText = VolumeFilter.AddToExclude(_excludeText, _largestNames[i]);
                        FiltersChanged();
                    }
                    GUI.Label(new Rect(74, y, w - 86, 20), _largestLabels[i]);
                    y += 22f;
                }
            }
            y += 8f;

            // --- game profiler ---------------------------------------------
            bool prof = GUI.Toggle(new Rect(12, y, w - 24, 22), _profiler.Active,
                                   " Game profiler (the game's slowest scripts, in the log every 30 s)");
            if (prof != _profiler.Active) ToggleProfiler();
            y += Row;
            y += UiText.Draw(12, y, w - 24, _profiler.Status);
            y += UiText.Draw(12, y, w - 24, _profiler.LastReport) + 8f;

            bool alloc = GUI.Toggle(new Rect(12, y, w - 24, 22), AllocationTracker.Counting,
                                    " Allocation tracker (what the game allocates, by type; by method with the profiler)");
            if (alloc != AllocationTracker.Counting) ToggleAllocations();
            y += Row;
            y += UiText.Draw(12, y, w - 24, AllocationTracker.Status);
            y += UiText.Draw(12, y, w - 24, _allocReport) + 8f;

            // --- performance patches -----------------------------------------
            y += UiText.Draw(12, y, w - 24, "Performance patches - less garbage for the game to collect (fewer hitches); " +
                                            "each one keeps what the game does. Untick one to get the game's own code back.");
            for (int i = 0; i < _perf.Count; i++)
            {
                bool on = GUI.Toggle(new Rect(12, y, w - 24, 22), _perf.IsOn(i), _perf.Label(i));
                if (on != _perf.IsOn(i)) _perf.Toggle(i);
                y += Row;
                if (_perf.Status(i) != (_perf.IsOn(i) ? "on" : "off"))
                    y += UiText.Draw(30, y, w - 42, _perf.Status(i));
            }
            y += 8f;

            // --- notes ------------------------------------------------------
            y += UiText.Draw(12, y, w - 24, _status);
            y += UiText.Draw(12, y, w - 24, "Freecam: WASD move, Q/E down/up, Shift fast, Ctrl slow.");
            y += UiText.Draw(12, y, w - 24, "The body is held still while it is on.");
            y += UiText.Draw(12, y, w - 24, "Volumes are world-space bounds, not exact mesh shapes.") + 4f;

            _contentHeight = y;
            GUI.EndScrollView();
        }

        public override void Shutdown()
        {
            if (_saveAt >= 0f) SaveFilters();
            if (_freeCam != null) _freeCam.End();
            _freeCamOn = false;
            if (_wireframe != null) _wireframe.Enabled = false;
            if (_host != null) Object.Destroy(_host);
            if (_profiler != null) _profiler.Uninstall();
            if (_perf != null) _perf.Shutdown();
            if (_loadTiming != null) _loadTiming.Uninstall();
        }
    }
}
