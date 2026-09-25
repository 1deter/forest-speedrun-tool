using System.Collections.Generic;
using ForestOverlay.Core;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // The one window everything lives in.
    //
    // WHY
    // Every feature used to own a floating panel behind its own hotkey.
    // That does not scale: past a handful of features you are memorising
    // keys to find things, and on a keyboard without a numpad there are
    // not enough comfortable keys to go round. One window with tabs means
    // a single key to remember, and discovering a feature is reading a tab
    // strip rather than the docs.
    //
    // Per-feature hotkeys still exist, but they now OPEN THIS WINDOW on
    // that tab rather than toggling a separate panel - and they are
    // unbound by default, so the key list stays short until someone
    // deliberately binds one in Settings.
    //
    // Tabs are collected from the modules themselves, so adding a feature
    // still means writing one class and registering it; the window picks
    // it up with no edit here.
    // ------------------------------------------------------------------
    public sealed class MainWindowModule : OverlayModule
    {
        private const float TabStripHeight = 26f;
        private const float Pad = 8f;

        public override string Id { get { return "mainwindow"; } }
        public override string DisplayName { get { return "ForestOverlay"; } }
        public override bool HasPanel { get { return true; } }

        private Rect _windowRect;
        private bool _windowPlaced;
        private int _active;
        private Vector2 _stripScroll;

        private GUIStyle _tabStyle;
        private GUIStyle _activeTabStyle;

        private readonly List<GUIContent> _tabLabels = new List<GUIContent>();

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("panel.main", KeyCode.F2, "Open ForestOverlay window", TogglePanel);
        }

        /// True while the window is open on this module's tab.
        public bool IsShowing(OverlayModule module)
        {
            if (!PanelOpen) return false;
            List<OverlayModule> tabs = Host.Tabs();
            return _active < tabs.Count && ReferenceEquals(tabs[_active], module);
        }

        /// Opens the window focused on a given module's tab. Used by the
        /// optional per-feature hotkeys.
        public void OpenAt(OverlayModule module)
        {
            List<OverlayModule> tabs = Host.Tabs();

            for (int i = 0; i < tabs.Count; i++)
            {
                if (!ReferenceEquals(tabs[i], module)) continue;
                _active = i;
                break;
            }

            if (!PanelOpen) TogglePanel();
        }

        public override void DrawPanel(int windowId)
        {
            if (!_windowPlaced)
            {
                float w = Mathf.Min(760f, Screen.width - 80f);
                float h = Mathf.Min(620f, Screen.height - 120f);
                _windowRect = new Rect((Screen.width - w) * 0.5f, 70f, w, h);
                _windowPlaced = true;
            }

            // Keep at least the title bar on screen: a resolution change or
            // a drag past the edge would otherwise leave an open window
            // nobody can see.
            _windowRect.x = Mathf.Clamp(_windowRect.x, 40f - _windowRect.width, Screen.width - 40f);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Screen.height - 30f);

            _windowRect = GUI.Window(windowId, _windowRect, DrawContents,
                                     "ForestOverlay v" + OverlayPlugin.PluginVersion);
        }

        private void EnsureStyles()
        {
            if (_tabStyle != null) return;

            _tabStyle = new GUIStyle(GUI.skin.button);
            _tabStyle.padding = new RectOffset(10, 10, 2, 2);

            _activeTabStyle = new GUIStyle(_tabStyle);
            _activeTabStyle.fontStyle = FontStyle.Bold;
        }

        private void DrawContents(int id)
        {
            EnsureStyles();

            List<OverlayModule> tabs = Host.Tabs();
            if (tabs.Count == 0)
            {
                GUI.Label(new Rect(Pad, 30f, _windowRect.width - Pad * 2f, 40f),
                          "No tabs registered.");
                GUI.DragWindow(new Rect(0, 0, _windowRect.width, 22));
                return;
            }

            if (_active >= tabs.Count) _active = 0;

            DrawTabStrip(tabs);

            // The body is drawn inside a group so each module can lay out
            // from 0,0 and never has to know where the window is.
            Rect body = new Rect(Pad,
                                 26f + TabStripHeight + 4f,
                                 _windowRect.width - Pad * 2f,
                                 _windowRect.height - 26f - TabStripHeight - 12f);

            GUI.BeginGroup(body);
            tabs[_active].DrawTab(new Rect(0f, 0f, body.width, body.height));
            GUI.EndGroup();

            GUI.DragWindow(new Rect(0, 0, _windowRect.width, 22));
        }

        private void DrawTabStrip(List<OverlayModule> tabs)
        {
            // Labels are cached: this runs on every OnGUI pass and OnGUI
            // runs several times a frame.
            for (int i = 0; i < tabs.Count; i++)
            {
                if (i < _tabLabels.Count) _tabLabels[i].text = tabs[i].TabTitle;
                else _tabLabels.Add(new GUIContent(tabs[i].TabTitle));
            }

            Rect strip = new Rect(Pad, 26f, _windowRect.width - Pad * 2f, TabStripHeight);

            float total = 0f;
            for (int i = 0; i < tabs.Count; i++)
                total += _tabStyle.CalcSize(_tabLabels[i]).x + 4f;

            // Scrolls rather than wrapping, so the body keeps a predictable
            // height however many features exist.
            Rect content = new Rect(0, 0, Mathf.Max(total, strip.width), TabStripHeight - 2f);
            _stripScroll = GUI.BeginScrollView(strip, _stripScroll, content, false, false);

            float x = 0f;
            for (int i = 0; i < tabs.Count; i++)
            {
                float w = _tabStyle.CalcSize(_tabLabels[i]).x + 4f;

                if (GUI.Button(new Rect(x, 0f, w, TabStripHeight - 4f), _tabLabels[i],
                               i == _active ? _activeTabStyle : _tabStyle))
                {
                    _active = i;
                }

                x += w;
            }

            GUI.EndScrollView();
        }
    }
}
