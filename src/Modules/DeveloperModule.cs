using ForestOverlay.Core;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // The Developer tab: what a runner never needs - the test bridge,
    // benchmarks, experimental features they would not risk a run on, the
    // memory census and the dumps (T-0226; author, 2026-10-08). Last in the
    // tab strip, so the runner's tabs show only runner features.
    //
    // It owns no feature: each fold is drawn by the module that owns it
    // (same config keys as before the move), so nobody's settings change.
    // ------------------------------------------------------------------
    public sealed class DeveloperModule : OverlayModule
    {
        public override string Id { get { return "developer"; } }
        public override string DisplayName { get { return "Developer"; } }
        public override bool HasTab { get { return true; } }
        public override string TabTitle { get { return "Developer"; } }
        public override int TabOrder { get { return 90; } }

        private static readonly GUIContent Intro = new GUIContent(
            "Developer and experimental tools. A run never needs anything here.");
        private static readonly GUIContent TextBridge = new GUIContent("Test bridge");
        private static readonly GUIContent TipBridge = new GUIContent(
            "Runs commands from bridge/in.txt - how the tool is tested. Off by default.");
        private static readonly GUIContent TextBench = new GUIContent("Benchmarks");
        private static readonly GUIContent TipBench = new GUIContent(
            "The game profiler, the frame test and the allocation tracker: numbers in the log for a performance report.");
        private static readonly GUIContent TextExperimental = new GUIContent("Experimental");
        private static readonly GUIContent TipExperimental = new GUIContent(
            "Off by default; each one changes what the game does. The trajectory preview and the gameplay-altering performance switches.");
        private static readonly GUIContent TextTas = new GUIContent("TAS (experimental)");
        private static readonly GUIContent TextCensus = new GUIContent("Memory census");
        private static readonly GUIContent TipCensus = new GUIContent(
            "What the game keeps in memory, in the log - after every load or now.");
        private static readonly GUIContent TextDumps = new GUIContent("Dumps");
        private static readonly GUIContent TipDumps = new GUIContent(
            "Every item id and name the game knows (and the nature guide in a loaded save), written to files for checklists.");

        private BridgeModule _bridge;
        private DebugViewModule _views;
        private TasModule _tas;
        private SavestateModule _savestates;
        private CollectiblesModule _collectibles;
        private bool _found;

        private Vector2 _scroll;
        private float _pageH = 600f;

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("tab.developer", KeyCode.None, "Open Developer tab", OpenMyTab);
        }

        public override void DrawTab(Rect area)
        {
            if (!_found)
            {
                _bridge = Host.Find<BridgeModule>();
                _views = Host.Find<DebugViewModule>();
                _tas = Host.Find<TasModule>();
                _savestates = Host.Find<SavestateModule>();
                _collectibles = Host.Find<CollectiblesModule>();
                _found = true;
            }

            bool scrolls = _pageH > area.height;
            float w = scrolls ? area.width - 20f : area.width;
            _scroll = GUI.BeginScrollView(area, _scroll, new Rect(0, 0, w, Mathf.Max(_pageH, area.height)));

            float y = 4f;
            y += UiText.DrawDim(0, y, w, Intro) + 6f;

            if (_bridge != null && UiKit.Section(0f, ref y, w, "dev.bridge", TextBridge, null, TipBridge, false))
            {
                bool on = GUI.Toggle(new Rect(12, y, w - 24, 22), _bridge.Enabled,
                                     " Test bridge: run commands from bridge/in.txt");
                if (on != _bridge.Enabled) _bridge.Enabled = on;
                y += 24f;
                y += UiText.DrawDim(12, y, w - 24, _bridge.StatusText) + 6f;
            }

            if (_views != null && UiKit.Section(0f, ref y, w, "dev.bench", TextBench, null, TipBench, false))
                y = _views.DrawBenchmarks(y, w);

            if (_views != null && UiKit.Section(0f, ref y, w, "dev.experimental", TextExperimental, null, TipExperimental, false))
                y = _views.DrawExperimental(y, w);

            if (_tas != null && UiKit.Section(0f, ref y, w, "dev.tas", TextTas, false))
                y = _tas.DrawSection(y, w) + 4f;

            if (_savestates != null && UiKit.Section(0f, ref y, w, "dev.census", TextCensus, null, TipCensus, false))
                y = _savestates.DrawCensus(12f, y, w - 24f) + 6f;

            if (_collectibles != null && UiKit.Section(0f, ref y, w, "dev.dumps", TextDumps, null, TipDumps, false))
                y = _collectibles.DrawDumps(12f, y, w - 24f) + 6f;

            _pageH = y + 4f;
            GUI.EndScrollView();
        }
    }
}
