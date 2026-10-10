using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // The HUD customiser (docs/ui-redesign.md; in the spirit of Momentum
    // Mod's HUD customiser): every switchable info box line can be taken
    // out of the box and become a widget of its own - placed anywhere,
    // resized (font scale), label on / off - with a clean label + value
    // look. The layout is `config/ForestOverlay/hud-layout.txt`
    // (Data/HudLayout, tested); a line not in it stays in the box, so
    // nothing changes until the runner edits.
    //
    // EDIT MODE (Editing): the main window turns into a widget list
    // (DrawEditor) and the screen shows an outline and a corner handle on
    // every widget. Drag a widget to move it, its corner to resize it,
    // right-click to put it back in the box; drag a LINE out of the box to
    // make it a widget.
    //
    // - Live changes are in memory; the file is written once per gesture
    //   (on release) - a drag never writes per event (gotcha 60).
    // - Positions are clamped live to the screen (gotcha 61).
    // - Nothing allocates in OnGUI after the first pass per scale step:
    //   styles are cached by scale quarter, contents are kept.
    // - Honest labelling is not a widget: ON NOW, the practice marker and
    //   run mode's code stay in the box (HudLines.Locked).
    // ------------------------------------------------------------------
    public sealed class HudWidgets
    {
        private const float CardPad = 8f;
        private const float HandleSize = 12f;
        private const float ValueBase = 16f, LabelBase = 11f;
        // Widget values render at ONE font size and are scaled by GUI.matrix:
        // a font size per widget scale (up to 96 px bold) filled Unity's shared
        // dynamic-font texture, which then rebuilt every frame and letters
        // flickered out across the whole UI (author's video, 2026-10-05).
        private const int ValueRender = 32;

        private readonly string _path;
        private readonly ManualLogSource _log;
        private HudLayout _layout = new HudLayout();
        private bool[] _detached = new bool[HudLines.All.Length];
        private int[] _widgetLine = new int[0];

        /// Edit mode: the window shows the widget list, the screen the handles.
        public bool Editing;

        public HudWidgets(string configDirectory, ManualLogSource log)
        {
            _log = log;
            _path = string.IsNullOrEmpty(configDirectory) ? null : Path.Combine(configDirectory, "hud-layout.txt");
            Load();
        }

        public HudLayout Layout { get { return _layout; } }

        /// Whether line `lineIndex` of HudLines.All is its own widget now
        /// (the box skips it).
        public bool IsDetached(int lineIndex)
        {
            return lineIndex >= 0 && lineIndex < _detached.Length && _detached[lineIndex];
        }

        public bool AnyDetached
        {
            get { return _layout.Widgets.Count > 0; }
        }

        // --- file ---------------------------------------------------------------

        private void Load()
        {
            try
            {
                if (_path != null && File.Exists(_path)) _layout = HudLayout.Parse(File.ReadAllText(_path));
            }
            catch (Exception ex)
            {
                if (_log != null) _log.LogWarning("HUD layout not read (" + _path + "): " + ex.Message);
                _layout = new HudLayout();
            }
            Rebuild();
        }

        private void Save()
        {
            if (_path == null) return;
            try
            {
                string dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_path, _layout.Format());
            }
            catch (Exception ex)
            {
                if (_log != null) _log.LogWarning("HUD layout not saved: " + ex.Message);
            }
        }

        private void Rebuild()
        {
            for (int i = 0; i < _detached.Length; i++) _detached[i] = false;
            if (_widgetLine.Length != _layout.Widgets.Count) _widgetLine = new int[_layout.Widgets.Count];
            for (int i = 0; i < _layout.Widgets.Count; i++)
            {
                int line = HudLines.IndexOfKey(_layout.Widgets[i].Key);
                _widgetLine[i] = line;
                if (line >= 0) _detached[line] = true;
            }
        }

        // --- changes ----------------------------------------------------------------

        public void Detach(int lineIndex, float x, float y)
        {
            if (lineIndex < 0 || lineIndex >= HudLines.All.Length || HudLines.All[lineIndex].ConfigKey == null) return;
            _layout.Detach(HudLines.All[lineIndex].ConfigKey, x, y);
            Rebuild();
            Save();
            if (_log != null) _log.LogInfo("HUD widget '" + HudLines.All[lineIndex].Name + "' taken out of the box at (" + Mathf.RoundToInt(x) + ", " + Mathf.RoundToInt(y) + ").");
        }

        public void Attach(int lineIndex)
        {
            if (lineIndex < 0 || lineIndex >= HudLines.All.Length) return;
            if (_layout.Attach(HudLines.All[lineIndex].ConfigKey))
            {
                Rebuild();
                Save();
                if (_log != null) _log.LogInfo("HUD widget '" + HudLines.All[lineIndex].Name + "' put back in the box.");
            }
        }

        public void ResetLayout()
        {
            _layout = new HudLayout();
            Rebuild();
            Save();
            if (_log != null) _log.LogInfo("HUD layout reset: every widget back in the box.");
        }

        private HudWidgetLayout WidgetOf(int lineIndex)
        {
            return lineIndex < 0 ? null : _layout.Find(HudLines.All[lineIndex].ConfigKey);
        }

        // --- styles, by scale quarter ------------------------------------------------------

        private sealed class ScaleStyles
        {
            public GUIStyle Label, Value;
            public GUIStyle Shadow;
            public float ValueScale, ValueSize;
        }

        private readonly Dictionary<int, ScaleStyles> _styles = new Dictionary<int, ScaleStyles>();

        private ScaleStyles StylesFor(float scale)
        {
            int key = Mathf.RoundToInt(HudLayout.ClampScale(scale) * 4f);
            ScaleStyles s;
            if (_styles.TryGetValue(key, out s)) return s;
            float sc = key / 4f;
            s = new ScaleStyles();
            s.Label = new GUIStyle(GUI.skin.label);
            s.Label.fontSize = Mathf.RoundToInt(LabelBase);
            s.Label.normal.textColor = UiKit.DimColour;
            s.Label.padding = new RectOffset(0, 0, 0, 0);
            s.Label.wordWrap = false;
            s.Value = new GUIStyle(GUI.skin.label);
            s.ValueSize = Mathf.Max(9f, ValueBase * sc);
            s.ValueScale = s.ValueSize / ValueRender;
            s.Value.fontSize = ValueRender;
            s.Value.fontStyle = FontStyle.Bold;
            s.Value.normal.textColor = UiKit.TextColour;
            s.Value.padding = new RectOffset(0, 0, 0, 0);
            s.Value.wordWrap = false;
            s.Shadow = new GUIStyle(s.Value);
            s.Shadow.normal.textColor = new Color(0f, 0f, 0f, 0.75f);
            _styles[key] = s;
            return s;
        }

        private GUIContent[] _names;
        private static readonly GUIContent NotShowing = new GUIContent("not showing now");

        private GUIContent NameOf(int lineIndex)
        {
            if (_names == null)
            {
                _names = new GUIContent[HudLines.All.Length];
                for (int i = 0; i < _names.Length; i++) _names[i] = new GUIContent(HudLines.All[i].Name);
            }
            return _names[lineIndex];
        }

        // --- drawing the widgets ---------------------------------------------------------------

        private int _dragWidget = -1;      // index into the layout's widgets
        private Vector2 _dragOffset;
        private int _resizeWidget = -1;
        private float _resizeStartScale, _resizeStartW, _resizeStartMouseX;
        private int _pullLine = -1;        // a box line pressed, not yet dragged out
        private Vector2 _pullStart;

        /// From the info box's draw, in edit mode. Pulling a line out by
        /// dragging is off (author, 2026-10-05: lines popped out while the box
        /// was dragged - "over-engineering simplicity"); widgets are made with
        /// the editor list's "own" toggle. Kept as a no-op for the caller.
        public void BoxLineEvent(Rect lineRect, int lineIndex, Rect blocked)
        {
        }

        /// Draws every free widget; in edit mode also its outline and handle,
        /// and takes the mouse. `blocked`: where the F2 window is (clicks there are its).
        public void Draw(HudBuilder hud, Rect blocked)
        {
            if (!AnyDetached && !Editing) return;
            UiKit.Ensure();
            Event e = Event.current;

            // A pulled line becomes a widget once the mouse has moved off it.
            if (_pullLine >= 0 && e != null)
            {
                if (!Editing || e.type == EventType.MouseUp) _pullLine = -1;
                else if (e.type == EventType.MouseDrag && (e.mousePosition - _pullStart).sqrMagnitude > 36f)
                {
                    float x = Mathf.Max(0f, e.mousePosition.x - 16f), y = Mathf.Max(0f, e.mousePosition.y - 10f);
                    int line = _pullLine;
                    _pullLine = -1;
                    Detach(line, x, y);
                    _dragWidget = _layout.Widgets.Count - 1;
                    for (int i = 0; i < _layout.Widgets.Count; i++)
                        if (_widgetLine[i] == line) _dragWidget = i;
                    _dragOffset = new Vector2(16f, 10f);
                    e.Use();
                }
            }

            for (int i = 0; i < _layout.Widgets.Count; i++)
            {
                int line = _widgetLine[i];
                if (line < 0) continue;     // a key from a newer version: kept in the file, not drawn
                DrawWidget(i, line, hud, blocked, e);
            }
        }

        private void DrawWidget(int i, int line, HudBuilder hud, Rect blocked, Event e)
        {
            HudWidgetLayout w = _layout.Widgets[i];
            ScaleStyles st = StylesFor(w.Scale);

            // The slots this widget shows (a pinned-items widget has several).
            int slots = 0;
            for (int j = 0; j < hud.Count; j++) if (hud.LineIndex(j) == line) slots++;
            if (slots == 0 && !Editing) return;

            bool single = slots <= 1;
            float labelH = st.Label.fontSize + 5f, valueH = st.ValueSize + 7f;
            float width = 0f, height = 0f;
            if (slots == 0)
            {
                width = Mathf.Max(st.Label.CalcSize(NameOf(line)).x, st.Value.CalcSize(NotShowing).x * st.ValueScale);
                height = valueH;
            }
            else
            {
                for (int j = 0; j < hud.Count; j++)
                {
                    if (hud.LineIndex(j) != line) continue;
                    width = Mathf.Max(width, st.Value.CalcSize(hud.ValueAt(j)).x * st.ValueScale);
                    height += valueH;
                }
            }
            width += CardPad * 2f;
            height += CardPad * 2f - 2f;

            float x = Mathf.Clamp(w.X, 0f, Mathf.Max(0f, Screen.width - width));
            float y = Mathf.Clamp(w.Y, 0f, Mathf.Max(0f, Screen.height - height));
            Rect rect = new Rect(x, y, width, height);

            if (Editing && e != null) HandleEditEvents(i, line, rect, width, blocked, e);
            // A drag moved it during the handler: draw where it is now.
            if (_dragWidget == i || _resizeWidget == i)
            {
                x = Mathf.Clamp(w.X, 0f, Mathf.Max(0f, Screen.width - width));
                y = Mathf.Clamp(w.Y, 0f, Mathf.Max(0f, Screen.height - height));
                rect = new Rect(x, y, width, height);
            }

            // No card (author, 2026-10-05: transparent, like Momentum Mod) - a soft
            // shadow keeps the value readable over snow / bright lab walls.
            if (Editing) GUI.Box(rect, GUIContent.none, UiKit.WidgetCard);
            float cy = rect.y + CardPad - 1f;
            float cx = rect.x + CardPad;
            if (slots == 0)
            {
                
                GUI.Label(new Rect(cx, cy, width, valueH), NotShowing, st.Label);
            }
            else
            {
                for (int j = 0; j < hud.Count; j++)
                {
                    if (hud.LineIndex(j) != line) continue;
                    Matrix4x4 m = GUI.matrix;
                    GUIUtility.ScaleAroundPivot(new Vector2(st.ValueScale, st.ValueScale), new Vector2(cx, cy));
                    Rect vr = new Rect(cx, cy, width / st.ValueScale, valueH / st.ValueScale);
                    GUI.Label(new Rect(vr.x + 2f, vr.y + 2f, vr.width, vr.height), hud.ValueAt(j), st.Shadow);
                    GUI.Label(vr, hud.ValueAt(j), st.Value);
                    GUI.matrix = m;
                    cy += valueH;
                }
            }

            if (Editing)
            {
                GUI.Box(rect, GUIContent.none, UiKit.Outline);
                GUI.Box(new Rect(rect.xMax - HandleSize, rect.yMax - HandleSize, HandleSize, HandleSize), GUIContent.none, UiKit.Handle);
            }
        }

        private void HandleEditEvents(int i, int line, Rect rect, float width, Rect blocked, Event e)
        {
            HudWidgetLayout w = _layout.Widgets[i];
            Rect handle = new Rect(rect.xMax - HandleSize - 2f, rect.yMax - HandleSize - 2f, HandleSize + 4f, HandleSize + 4f);
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (_dragWidget >= 0 || _resizeWidget >= 0 || !rect.Contains(e.mousePosition) || blocked.Contains(e.mousePosition)) return;
                    if (e.button == 1)
                    {
                        Attach(line);
                        e.Use();
                        return;
                    }
                    if (e.button != 0) return;
                    if (handle.Contains(e.mousePosition))
                    {
                        _resizeWidget = i;
                        _resizeStartScale = w.Scale;
                        _resizeStartW = width;
                        _resizeStartMouseX = e.mousePosition.x;
                    }
                    else
                    {
                        _dragWidget = i;
                        _dragOffset = e.mousePosition - new Vector2(rect.x, rect.y);
                    }
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (_dragWidget == i)
                    {
                        w.X = Mathf.Clamp(e.mousePosition.x - _dragOffset.x, 0f, Mathf.Max(0f, Screen.width - width));
                        w.Y = Mathf.Clamp(e.mousePosition.y - _dragOffset.y, 0f, Mathf.Max(0f, Screen.height - rect.height));
                        e.Use();
                    }
                    else if (_resizeWidget == i)
                    {
                        w.Scale = HudLayout.ScaleFromDrag(_resizeStartScale, _resizeStartW,
                                                          _resizeStartW + (e.mousePosition.x - _resizeStartMouseX));
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (_dragWidget == i || _resizeWidget == i)
                    {
                        bool moved = _dragWidget == i;
                        _dragWidget = -1;
                        _resizeWidget = -1;
                        w.X = Mathf.Round(w.X);
                        w.Y = Mathf.Round(w.Y);
                        Save();   // once per gesture
                        if (_log != null)
                            _log.LogInfo("HUD widget '" + HudLines.All[line].Name + "' " + (moved ? "moved to (" + Mathf.RoundToInt(w.X) + ", " + Mathf.RoundToInt(w.Y) + ")" : "scaled to " + w.Scale.ToString("0.##")) + ".");
                        e.Use();
                    }
                    break;
            }
        }

        /// Ends any gesture in flight (the window closed mid-drag).
        public void StopEditing()
        {
            Editing = false;
            _dragWidget = -1;
            _resizeWidget = -1;
            _pullLine = -1;
        }

        // --- the editor (in the F2 window) ------------------------------------------------------

        private static readonly GUIContent EditHint = new GUIContent(
            "Drag a value out of the info box to make it a widget. Drag a widget or panel to move it, its corner or edges to resize; right-click a widget to put it back.");
        private static readonly GUIContent LockedNote = new GUIContent(
            "Always in the info box: ON NOW, the practice marker and run mode's code (a recording must show them).");
        private static readonly GUIContent DoneText = new GUIContent("Done");
        private static readonly GUIContent ResetText = new GUIContent("Reset layout");
        private static readonly GUIContent ResetTip = new GUIContent("Every widget back in the info box.");
        private static readonly GUIContent OwnText = new GUIContent("own");
        private static readonly GUIContent OwnTip = new GUIContent("Take this value out of the info box as a widget of its own.");
        private static readonly GUIContent LabelText = new GUIContent("label");
        private static readonly GUIContent LabelTip = new GUIContent("Show the name above the value.");
        private static readonly GUIContent SmallerText = new GUIContent("-");
        private static readonly GUIContent BiggerText = new GUIContent("+");
        private GUIContent[] _descriptions;
        private GUIContent[] _toggleNames;
        private readonly GUIContent[] _scaleTexts = new GUIContent[(int)(HudLayout.MaxScale * 4f) + 1];
        private Vector2 _scroll;
        private float _panelsH;
        private static readonly GUIContent PanelsTitle = new GUIContent("Panels");
        private static readonly GUIContent ValuesTitle = new GUIContent("Info box values");

        /// The widget list in the window's body; true when Done was pressed.
        public bool DrawEditor(Rect area, HudSettings settings, Rect windowRect, ModuleHost host)
        {
            if (_descriptions == null)
            {
                _descriptions = new GUIContent[HudLines.All.Length];
                _toggleNames = new GUIContent[HudLines.All.Length];
                for (int i = 0; i < _descriptions.Length; i++)
                {
                    _descriptions[i] = new GUIContent(HudLines.All[i].Description);
                    _toggleNames[i] = new GUIContent(HudLines.All[i].Name);
                }
            }

            bool done = false;
            float w = area.width;
            float y = 0f;
            y += UiText.Draw(0f, y, w, EditHint, UiKit.HintStyle) + 4f;
            if (UiKit.PrimaryButton(new Rect(0f, y, 100f, 26f), DoneText)) done = true;
            if (GUI.Button(new Rect(106f, y, 120f, 26f), ResetText)) ResetLayout();
            UiKit.Hint(new Rect(106f, y, 120f, 26f), ResetTip);
            y += 34f;

            const float rowH = 26f;
            int rows = 0;
            for (int i = 0; i < HudLines.All.Length; i++) if (HudLines.All[i].Switchable) rows++;
            float noteH = UiText.Height(w - 20f, LockedNote, UiKit.HintStyle);
            // The panels' rows (the modules') are as tall as their open
            // folds: last pass's height sizes the scroll view.
            float contentH = _panelsH + rows * rowH + noteH + 8f;
            float viewH = area.height - y;
            bool scrolls = contentH > viewH;
            float cw = scrolls ? w - 18f : w;
            _scroll = GUI.BeginScrollView(new Rect(0f, y, w, viewH), _scroll, new Rect(0f, 0f, cw, contentH));
            float ry = 0f;
            // The on-screen panels first (author, 2026-10-10: set up where
            // they are shown, and visible - not under 24 value rows).
            if (host != null)
            {
                GUI.Label(new Rect(0f, ry, cw, 22f), PanelsTitle, UiKit.Title);
                float after = host.DrawHudEditors(ry + 24f, cw);
                GUI.Label(new Rect(0f, after + 4f, cw, 22f), ValuesTitle, UiKit.Title);
                ry = after + 30f;
                _panelsH = ry;
            }
            for (int i = 0; i < HudLines.All.Length; i++)
            {
                HudLine l = HudLines.All[i];
                if (!l.Switchable) continue;
                bool shown = settings.Shows(i);
                Rect nameR = new Rect(0f, ry, cw - 236f, rowH - 4f);
                bool now = GUI.Toggle(nameR, shown, _toggleNames[i]);
                if (now != shown) settings.SetShows(i, now);
                UiKit.Hint(nameR, _descriptions[i]);

                HudWidgetLayout wl = WidgetOf(i);
                bool own = wl != null;
                float x = cw - 232f;
                bool ownNow = GUI.Toggle(new Rect(x, ry, 52f, rowH - 4f), own, OwnText);
                if (ownNow != own)
                {
                    if (ownNow)
                    {
                        // Beside the box, where the eye already is.
                        Detach(i, Mathf.Min(settings.X + HudLines.Width(settings.TextSize, Screen.width) + 12f, Screen.width - 120f),
                               settings.Y + 8f + Offset());
                    }
                    else Attach(i);
                    wl = WidgetOf(i);
                }
                UiKit.Hint(new Rect(x, ry, 52f, rowH - 4f), OwnTip);
                if (wl != null)
                {
                    float xs = x + 56f;
                    if (GUI.Button(new Rect(xs, ry, 24f, rowH - 4f), SmallerText)) SetScale(wl, wl.Scale - 0.25f);
                    GUI.Label(new Rect(xs + 26f, ry, 44f, rowH - 4f), ScaleText(wl.Scale));
                    if (GUI.Button(new Rect(xs + 70f, ry, 24f, rowH - 4f), BiggerText)) SetScale(wl, wl.Scale + 0.25f);
                }
                ry += rowH;
            }
            UiText.Draw(0f, ry + 4f, cw - 4f, LockedNote, UiKit.HintStyle);
            GUI.EndScrollView();
            return done;
        }

        // New widgets fan out so they do not land on each other.
        private float Offset()
        {
            return (_layout.Widgets.Count % 8) * 30f;
        }

        private void SetScale(HudWidgetLayout wl, float scale)
        {
            float s = HudLayout.ClampScale(scale);
            if (s == wl.Scale) return;
            wl.Scale = s;
            Save();
        }

        private GUIContent ScaleText(float scale)
        {
            int k = Mathf.Clamp(Mathf.RoundToInt(scale * 4f), 0, _scaleTexts.Length - 1);
            if (_scaleTexts[k] == null) _scaleTexts[k] = new GUIContent((k / 4f).ToString("0.##") + "x");
            return _scaleTexts[k];
        }
    }
}
