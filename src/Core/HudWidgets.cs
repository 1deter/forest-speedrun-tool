using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using BepInEx.Logging;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // The HUD customiser (docs/ui-redesign.md, T-0018; in the spirit of
    // Momentum Mod's HUD customiser). Every ticked HUD value shows: by
    // default in the COLUMN at the HUD position (drawn by the plugin, the
    // pre-overhaul order, no backing), or FREE - placed anywhere and sized
    // on its own. Each value shows its value only, with the runner's own
    // text before / after it. The changes are part of the active HUD
    // profile (HudWidgets.Profiles, `config/ForestOverlay/hud/<name>.txt`;
    // Data/HudLayout + HudProfile, tested); an empty file is the default look.
    //
    // EDIT MODE (Editing): the main window turns into a value list
    // (DrawEditor) and the screen shows an outline on every value. Drag a
    // value out of the column to place it; drag a placed value to move it,
    // its corner to resize it; right-click (or its "to column" button) puts
    // it back. The column moves by its grip.
    //
    // - Live changes are in memory; the file is written once per gesture
    //   (on release) or per text edit (when its row closes / Done) - never
    //   per event or keystroke (gotcha 60).
    // - Positions are clamped live to the screen (gotcha 61).
    // - Nothing allocates in OnGUI after the first pass per scale step:
    //   styles are cached by scale quarter, contents are kept; the text
    //   around a value is joined on the HUD's 10 Hz tick (HudBuilder).
    // - Honest labelling is not a value: ON NOW and the practice marker
    //   always close the column (HudLines.Locked).
    // - A value that offers it can be coloured by its trend (T-0023; its
    //   Trend switch here, colours in Settings -> HUD): only the style's
    //   text colour changes around the draw, so the values' own font never
    //   rebuilds for it (gotcha 107).
    // ------------------------------------------------------------------
    public sealed partial class HudWidgets
    {
        private const float CardPad = 8f;
        private const float HandleSize = 12f;
        private const float ValueBase = 16f, LabelBase = 11f;
        // Widget values render at one of THREE font sizes - the smallest at or
        // above the value's size - and are scaled down by GUI.matrix, so a
        // large widget stays crisp (T-0022: one 32 px render scaled up to 4.5x
        // looked choppy, author 2026-10-10). A font size per widget scale (up
        // to 96 px bold) once filled Unity's shared dynamic-font texture, which
        // rebuilt every frame and letters flickered out across the whole UI
        // (author's video, 2026-10-05): the values draw with their own font
        // instance (own texture), and only these three sizes.
        private static readonly int[] ValueRenders = { 32, 64, 96 };
        private static Font _valueFont;
        private static bool _valueFontTried;

        // Every printable ASCII glyph, asked of the values' font at each size in
        // use on every repaint (T-0301). A dynamic font keeps only the glyphs
        // asked for lately: Speed going 0.00 -> moving digits -> 0.00 rebuilt
        // its whole texture at 96 px bold, a ~20 ms frame on each start and
        // stop of walking (author: a spike per WASD press). Asked every frame,
        // nothing is ever missing; the call costs nothing once they are in.
        private static readonly string KeptGlyphs = PrintableAscii();

        private static string PrintableAscii()
        {
            char[] c = new char[127 - 32];
            for (int i = 0; i < c.Length; i++) c[i] = (char)(32 + i);
            return new string(c);
        }

        private static int ValueRenderFor(float size)
        {
            for (int i = 0; i < ValueRenders.Length; i++)
                if (ValueRenders[i] >= size) return ValueRenders[i];
            return ValueRenders[ValueRenders.Length - 1];
        }

        /// Arial as its own dynamic font, so the values' large glyphs never share
        /// the UI's font texture; null (the skin's font) if the OS has no Arial.
        private Font ValueFont()
        {
            if (_valueFontTried) return _valueFont;
            _valueFontTried = true;
            try
            {
                _valueFont = Font.CreateDynamicFontFromOSFont("Arial", ValueRenders[0]);
                if (_valueFont != null) _valueFont.hideFlags = HideFlags.HideAndDontSave;
            }
            catch (Exception ex)
            {
                _valueFont = null;
                if (_log != null) _log.LogWarning("HUD widgets: no font of their own (" + ex.Message + ") - values share the UI's font.");
            }
            return _valueFont;
        }

        private readonly ManualLogSource _log;
        private HudLayout _layout = new HudLayout();
        private readonly bool[] _free = new bool[HudLines.All.Length];
        private readonly bool[] _docked = new bool[HudLines.All.Length];
        private readonly string[] _before = new string[HudLines.All.Length];
        private readonly string[] _after = new string[HudLines.All.Length];
        private readonly bool[] _trendOn = new bool[HudLines.All.Length];
        private int[] _widgetLine = new int[0];

        /// Edit mode: the window shows the value list, the screen the handles.
        public bool Editing;

        /// Bumped when any value's text changes, so HudBuilder rebuilds the shown text.
        public int TextVersion { get; private set; }

        public HudWidgets(ConfigFile config, HudSettings settings, string configDirectory, ManualLogSource log)
        {
            _log = log;
            _config = config;
            _settings = settings;
            _configDir = configDirectory;
            _dir = string.IsNullOrEmpty(configDirectory) ? null : Path.Combine(configDirectory, "hud");
            _activeCfg = config.Bind("HUD", "Profile", HudProfileNames.DefaultName,
                "The HUD profile in use: a file in config/ForestOverlay/hud (pick it in Edit HUD layout).");
            config.SettingChanged += OnSettingChanged;
            if (settings != null) settings.RegisterProfileEntries(this);
            Rebuild();
        }

        public HudLayout Layout { get { return _layout; } }

        /// Whether line `lineIndex` of HudLines.All is placed on its own (the
        /// column skips it).
        public bool IsFree(int lineIndex)
        {
            return lineIndex >= 0 && lineIndex < _free.Length && _free[lineIndex];
        }

        /// Whether line `lineIndex` is a row of the splits panel (T-0338).
        public bool IsDocked(int lineIndex)
        {
            return lineIndex >= 0 && lineIndex < _docked.Length && _docked[lineIndex];
        }

        /// Whether the column skips line `lineIndex`: placed on its own or
        /// docked in the splits panel.
        public bool LeavesColumn(int lineIndex)
        {
            return HudLayout.LeavesColumn(IsFree(lineIndex), IsDocked(lineIndex), PanelOnScreen);
        }

        /// The frame the splits panel last drew (it sets this): a docked
        /// value shows in the column while the panel is not on screen.
        public int DockHostFrame = -10;
        public bool PanelOnScreen { get { return DockHostFrame >= Time.frameCount - 1; } }

        /// The docked values' HudLines indexes, in the order they were docked.
        public List<int> DockedLines { get { return _dockedLines; } }
        private readonly List<int> _dockedLines = new List<int>();
        private readonly List<string> _dockedKeys = new List<string>();

        /// Whether line `lineIndex` is coloured by its trend (T-0023).
        public bool TrendOn(int lineIndex)
        {
            return lineIndex >= 0 && lineIndex < _trendOn.Length && _trendOn[lineIndex];
        }

        /// A value as shown: the runner's text around it. Called on the HUD
        /// tick when the value changes, never in OnGUI.
        public string Decorate(int lineIndex, string value)
        {
            if (lineIndex < 0 || lineIndex >= _before.Length) return value;
            return HudLayout.Decorate(_before[lineIndex], value, _after[lineIndex]);
        }

        // --- file ---------------------------------------------------------------

        /// The layout changed: the active profile's file is written now.
        private void Save()
        {
            SaveProfile();
        }

        private void Rebuild()
        {
            for (int i = 0; i < _free.Length; i++)
            {
                _free[i] = false;
                _docked[i] = false;
                _before[i] = "";
                _after[i] = "";
                _trendOn[i] = false;
            }
            if (_widgetLine.Length != _layout.Widgets.Count)
            {
                _widgetLine = new int[_layout.Widgets.Count];
                _rects = new Rect[_layout.Widgets.Count];
            }
            for (int i = 0; i < _layout.Widgets.Count; i++)
            {
                HudWidgetLayout w = _layout.Widgets[i];
                int line = HudLines.IndexOfKey(w.Key);
                _widgetLine[i] = line;
                if (line < 0) continue;     // a key from a newer version: kept in the file, not drawn
                _free[line] = w.Free;
                _docked[line] = w.Docked;
                _before[line] = w.Before;
                _after[line] = w.After;
                _trendOn[line] = w.Trend && HudLines.All[line].Trend;
            }
            _layout.DockedKeys(_dockedKeys);
            _dockedLines.Clear();
            for (int i = 0; i < _dockedKeys.Count; i++)
            {
                int line = HudLines.IndexOfKey(_dockedKeys[i]);
                if (line >= 0) _dockedLines.Add(line);
            }
            TextVersion++;
        }

        // --- changes ----------------------------------------------------------------

        public void Detach(int lineIndex, float x, float y)
        {
            Detach(lineIndex, x, y, 1f);
        }

        /// Placed on its own at (x, y) in reference pixels, `scale` big (the
        /// splits panel's popped-out timer, T-0336).
        public void Detach(int lineIndex, float x, float y, float scale)
        {
            if (lineIndex < 0 || lineIndex >= HudLines.All.Length || HudLines.All[lineIndex].ConfigKey == null) return;
            _layout.Detach(HudLines.All[lineIndex].ConfigKey, x, y).Scale = HudLayout.ClampScale(scale);
            Rebuild();
            Save();
            if (_log != null) _log.LogInfo("HUD widget '" + HudLines.All[lineIndex].Name + "' placed on its own at (" + Mathf.RoundToInt(x) + ", " + Mathf.RoundToInt(y) + ").");
        }

        /// The value becomes a row of the splits panel (T-0338).
        public void Dock(int lineIndex)
        {
            if (lineIndex < 0 || lineIndex >= HudLines.All.Length || HudLines.All[lineIndex].ConfigKey == null) return;
            if (!_layout.Dock(HudLines.All[lineIndex].ConfigKey)) return;
            Rebuild();
            Save();
            if (_log != null) _log.LogInfo("HUD widget '" + HudLines.All[lineIndex].Name + "' docked in the splits panel.");
        }

        public void Attach(int lineIndex)
        {
            if (lineIndex < 0 || lineIndex >= HudLines.All.Length) return;
            if (_layout.Attach(HudLines.All[lineIndex].ConfigKey))
            {
                Rebuild();
                Save();
                if (_log != null) _log.LogInfo("HUD widget '" + HudLines.All[lineIndex].Name + "' put back in the column.");
            }
        }

        public void SetTrend(int lineIndex, bool on)
        {
            if (lineIndex < 0 || lineIndex >= HudLines.All.Length || !HudLines.All[lineIndex].Trend) return;
            if (!_layout.SetTrend(HudLines.All[lineIndex].ConfigKey, on)) return;
            Rebuild();
            Save();
            if (_log != null) _log.LogInfo("HUD widget '" + HudLines.All[lineIndex].Name + "' trend colour " + (on ? "on" : "off") + ".");
        }

        private HudWidgetLayout WidgetOf(int lineIndex)
        {
            return lineIndex < 0 ? null : _layout.Find(HudLines.All[lineIndex].ConfigKey);
        }

        // --- styles, by scale quarter ------------------------------------------------------

        private sealed class ScaleStyles
        {
            public GUIStyle Label, Value;
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
            s.Label = UiKit.Style(GUI.skin.label);
            s.Label.fontSize = Mathf.RoundToInt(LabelBase);
            s.Label.normal.textColor = UiKit.DimColour;
            s.Label.padding = new RectOffset(0, 0, 0, 0);
            s.Label.wordWrap = false;
            s.Value = UiKit.Style(GUI.skin.label);
            s.ValueSize = Mathf.Max(9f, ValueBase * sc);
            int render = ValueRenderFor(s.ValueSize);
            s.ValueScale = s.ValueSize / render;
            Font own = ValueFont();
            if (own != null) s.Value.font = own;
            s.Value.fontSize = render;
            s.Value.fontStyle = FontStyle.Bold;
            s.Value.normal.textColor = UiKit.TextColour;
            s.Value.padding = new RectOffset(0, 0, 0, 0);
            s.Value.wordWrap = false;
            _styles[key] = s;
            return s;
        }

        private static readonly GUIContent NotShowing = new GUIContent("not showing now");

        // --- drawing the widgets ---------------------------------------------------------------

        private int _dragWidget = -1;      // index into the layout's widgets
        private Vector2 _dragOffset;
        private int _resizeWidget = -1;
        private float _resizeStartScale, _resizeStartW, _resizeStartMouseX;
        private int _pullLine = -1;        // a column value pressed, not yet dragged out
        private Vector2 _pullStart, _pullOffset;
        private Rect[] _rects = new Rect[0];   // each placed value's rect as last drawn

        /// The column as drawn this pass (the plugin sets it): a placed value
        /// dropped on it goes back in (author, 2026-10-10: sliding it back in
        /// is what a runner tries first).
        public Rect ColumnRect;

        /// A placed value is being dragged over the column (the plugin lights it up).
        public bool DropOnColumn { get; private set; }

        private bool OverColumn(Vector2 mouse)
        {
            Rect c = ColumnRect;
            return c.width > 0f && new Rect(c.x - 4f, c.y - 2f, c.width + 8f, c.height + 4f).Contains(mouse);
        }

        /// From the column's draw, in edit mode, for each value it shows: a
        /// press on it and a drag of a few pixels places it on its own
        /// (author, 2026-10-10: "drag a value out to place it anywhere").
        /// The column itself moves by its grip only, so moving it never
        /// pulls values out (the first try's fault, 2026-10-05).
        public void ColumnLineEvent(Rect lineRect, int lineIndex, Rect blocked)
        {
            Event e = Event.current;
            if (!Editing || e == null || e.type != EventType.MouseDown || e.button != 0) return;
            if (_dragWidget >= 0 || _resizeWidget >= 0 || _pullLine >= 0) return;
            if (lineIndex < 0 || HudLines.All[lineIndex].ConfigKey == null) return;
            if (!lineRect.Contains(e.mousePosition) || blocked.Contains(e.mousePosition)) return;
            // A placed value lying over the column takes the press (author,
            // 2026-10-10: the value under it popped out instead).
            for (int i = 0; i < _rects.Length; i++)
                if (_layout.Widgets[i].Free && _rects[i].Contains(e.mousePosition)) return;
            _pullLine = lineIndex;
            _pullStart = e.mousePosition;
            _pullOffset = e.mousePosition - new Vector2(lineRect.x, lineRect.y);
            e.Use();
        }

        /// Draws every placed value; in edit mode also its outline and handle,
        /// and takes the mouse. `blocked`: where the F2 window is (clicks there are its).
        public void Draw(HudBuilder hud, Rect blocked)
        {
            Event e = Event.current;

            // A pressed column value is placed on its own once the mouse has moved.
            if (_pullLine >= 0 && e != null)
            {
                if (!Editing || e.type == EventType.MouseUp) _pullLine = -1;
                else if (e.type == EventType.MouseDrag && (e.mousePosition - _pullStart).sqrMagnitude > 36f)
                {
                    int line = _pullLine;
                    _pullLine = -1;
                    Vector2 at = e.mousePosition - _pullOffset;
                    Detach(line, _settings.Ui.ToRefX(Mathf.Max(0f, at.x - CardPad)), _settings.Ui.ToRefY(Mathf.Max(0f, at.y - CardPad)));   // saved in reference pixels (T-0318)
                    for (int i = 0; i < _layout.Widgets.Count; i++)
                        if (_widgetLine[i] == line) _dragWidget = i;
                    _dragOffset = _pullOffset + new Vector2(CardPad, CardPad);
                    e.Use();
                }
            }

            bool any = false;
            for (int i = 0; i < _layout.Widgets.Count; i++)
            {
                int line = _widgetLine[i];
                if (line < 0 || !_layout.Widgets[i].Free) continue;
                if (!any) { UiKit.Ensure(); any = true; }
                DrawWidget(i, line, hud, blocked, e);
            }
            DrawGuides(e);
        }

        private void DrawWidget(int i, int line, HudBuilder hud, Rect blocked, Event e)
        {
            HudWidgetLayout w = _layout.Widgets[i];
            ScaleStyles st = StylesFor(w.Scale);
            if (e != null && e.type == EventType.Repaint && _valueFont != null && st.Value.font == _valueFont)
                _valueFont.RequestCharactersInTexture(KeptGlyphs, st.Value.fontSize, FontStyle.Bold);

            // The slots this widget shows (a pinned-items widget has several).
            int slots = 0;
            for (int j = 0; j < hud.Count; j++) if (hud.LineIndex(j) == line) slots++;
            if (slots == 0 && !Editing) return;

            _rects[i] = new Rect();
            float valueH = st.ValueSize + 7f;
            float width = 0f, height = 0f;
            if (slots == 0)
            {
                width = st.Label.CalcSize(NotShowing).x;
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

            UiScale ui = _settings.Ui;
            float x = Mathf.Clamp(ui.PosX(w.X), 0f, Mathf.Max(0f, ui.VW - width));
            float y = Mathf.Clamp(ui.PosY(w.Y), 0f, Mathf.Max(0f, ui.VH - height));
            Rect rect = new Rect(x, y, width, height);

            if (Editing && e != null)
            {
                HandleEditEvents(i, line, rect, width, blocked, e);
                // Put back in the column by the handler: gone from the list.
                if (i >= _layout.Widgets.Count || _layout.Widgets[i] != w) return;
            }
            // A drag moved it during the handler: draw where it is now.
            if (_dragWidget == i || _resizeWidget == i)
            {
                x = Mathf.Clamp(ui.PosX(w.X), 0f, Mathf.Max(0f, ui.VW - width));
                y = Mathf.Clamp(ui.PosY(w.Y), 0f, Mathf.Max(0f, ui.VH - height));
                rect = new Rect(x, y, width, height);
            }
            _rects[i] = rect;

            // No card (author, 2026-10-05: transparent, like Momentum Mod) and no
            // text shadow (author, 2026-10-11, T-0022: plain text for now).
            if (Editing) WinPx.Box(rect, GUIContent.none, UiKit.WidgetCard);
            float cy = rect.y + CardPad - 1f;
            float cx = rect.x + CardPad;
            if (slots == 0)
            {
                WinPx.Label(new Rect(cx, cy, width, valueH), NotShowing, st.Label);
            }
            else
            {
                for (int j = 0; j < hud.Count; j++)
                {
                    if (hud.LineIndex(j) != line) continue;
                    Matrix4x4 m = GUI.matrix;
                    GUIUtility.ScaleAroundPivot(new Vector2(st.ValueScale, st.ValueScale), new Vector2(cx, cy));
                    Rect vr = new Rect(cx, cy, width / st.ValueScale, valueH / st.ValueScale);
                    int trend = hud.TrendAt(j);
                    if (trend >= 0 && _settings != null)
                    {
                        // The colour only: the glyphs (and the font's texture) stay.
                        Color normal = st.Value.normal.textColor;
                        st.Value.normal.textColor = _settings.TrendColour((TrendState)trend);
                        WinPx.Label(vr, hud.ValueAt(j), st.Value);
                        st.Value.normal.textColor = normal;
                    }
                    else WinPx.Label(vr, hud.ValueAt(j), st.Value);
                    GUI.matrix = m;
                    cy += valueH;
                }
            }

            if (Editing)
            {
                WinPx.Box(rect, GUIContent.none, UiKit.Outline);
                WinPx.Box(new Rect(rect.xMax - HandleSize, rect.yMax - HandleSize, HandleSize, HandleSize), GUIContent.none, UiKit.Handle);
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
                        UiScale ui = _settings.Ui;
                        float dx = Mathf.Clamp(e.mousePosition.x - _dragOffset.x, 0f, Mathf.Max(0f, ui.VW - width));
                        float dy = Mathf.Clamp(e.mousePosition.y - _dragOffset.y, 0f, Mathf.Max(0f, ui.VH - rect.height));
                        Vector2 at = Snap(new Rect(dx, dy, width, rect.height), null, i);
                        w.X = ui.ToRefX(at.x);
                        w.Y = ui.ToRefY(at.y);
                        DropOnColumn = OverColumn(e.mousePosition);
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
                        DropOnColumn = false;
                        EndSnap();
                        if (moved && OverColumn(e.mousePosition))
                        {
                            Attach(line);   // dropped on the column: back in it
                            e.Use();
                            break;
                        }
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

        /// Ends any gesture in flight (the window closed mid-drag) and keeps
        /// a text being typed.
        public void StopEditing()
        {
            CloseText();
            Editing = false;
            _scannedForEdit = false;
            _renaming = false;
            _dragWidget = -1;
            _resizeWidget = -1;
            _pullLine = -1;
            DropOnColumn = false;
            EndSnap();
        }

        // --- the text around a value --------------------------------------------------------

        private int _textLine = -1;          // the row whose text fields are open
        private string _beforeEdit = "", _afterEdit = "";
        private bool _textDirty;

        private void OpenText(int lineIndex)
        {
            CloseText();
            _textLine = lineIndex;
            _beforeEdit = _before[lineIndex];
            _afterEdit = _after[lineIndex];
        }

        /// Writes the file once for a whole edit (not per keystroke).
        private void CloseText()
        {
            int line = _textLine;
            _textLine = -1;
            if (!_textDirty || line < 0) return;
            _textDirty = false;
            Save();
            if (_log != null)
                _log.LogInfo("HUD widget '" + HudLines.All[line].Name + "' text: before \"" + _before[line] + "\", after \"" + _after[line] + "\".");
        }

        private void ApplyText()
        {
            if (_textLine < 0) return;
            if (!_layout.SetText(HudLines.All[_textLine].ConfigKey, _beforeEdit, _afterEdit)) return;
            Rebuild();
            _textDirty = true;
        }

        // --- the editor (in the F2 window) ------------------------------------------------------

        private static readonly GUIContent EditHint = new GUIContent(
            "Drag a value out of the column to place it anywhere; drag the column's grip (the bar beside it) to move the column. " +
            "Drag a placed value to move it, its corner to resize it; right-click it to put it back. " +
            "Dragged pieces snap to the screen's centre and quarters and to each other; hold Alt to place freely.");
        private static readonly GUIContent LockedNote = new GUIContent(
            "Always shown under the column: ON NOW and the practice marker (a recording must show them).");
        private static readonly GUIContent DoneText = new GUIContent("Done");
        private static readonly GUIContent ResetText = new GUIContent("Reset profile");
        private static readonly GUIContent ResetSureText = new GUIContent("Sure?");
        private static readonly GUIContent ResetTip = new GUIContent(
            "This profile back to the default HUD: every value in the column with its label, the panels, Compact and Text size. " +
            "Click twice; there is no undo.");
        private static readonly GUIContent TextText = new GUIContent("Text");
        private static readonly GUIContent TextTip = new GUIContent("Your own text before and after the value, e.g. \"Speed \" or \" u/s\".");
        private static readonly GUIContent BeforeText = new GUIContent("Before");
        private static readonly GUIContent AfterText = new GUIContent("After");
        // Trend colour toggle (T-0023).
        private static readonly GUIContent TrendText = new GUIContent("Trend");
        private static readonly GUIContent TrendTip = new GUIContent(
            "Colour the value by its trend: gaining, losing or steady. The colours are in Settings -> HUD.");
        private static readonly GUIContent PanelText = new GUIContent("In panel");
        private static readonly GUIContent PanelTip = new GUIContent(
            "Make this value a row of the splits panel, under its lines, as a LiveSplit component. It shows there while the panel does.");
        private static readonly GUIContent UndockTip = new GUIContent("Take this value out of the splits panel, back into the column.");
        private static readonly GUIContent ColumnText = new GUIContent("To column");
        private static readonly GUIContent ColumnTip = new GUIContent("Put this value back in the column.");
        private static readonly GUIContent SmallerText = new GUIContent("-");
        private static readonly GUIContent BiggerText = new GUIContent("+");
        private GUIContent[] _descriptions;
        private GUIContent[] _toggleNames;
        private readonly GUIContent[] _scaleTexts = new GUIContent[(int)(HudLayout.MaxScale * 4f) + 1];
        private Vector2 _scroll;
        private float _panelsH;
        private float _idleH;   // the reasons under blank values (T-0322), last pass
        private static readonly GUIContent PanelsTitle = new GUIContent("Panels");
        private static readonly GUIContent ValuesTitle = new GUIContent("Values");

        private const float RowH = 26f, TextRowH = 28f, HeadH = 24f;
        private GUIStyle _headStyle;
        private object _headFor;

        /// The value list in the window's body; true when Done was pressed.
        public bool DrawEditor(Rect area, HudSettings settings, Rect windowRect, ModuleHost host)
        {
            if (_toggleNames == null)
            {
                _toggleNames = new GUIContent[HudLines.All.Length];
                for (int i = 0; i < _toggleNames.Length; i++)
                    _toggleNames[i] = new GUIContent(HudLines.All[i].Name);
            }
            // The tips name the runner's keys (T-0262).
            _descriptions = HudLineTips.Get(host != null ? host.Hotkeys : null);

            bool done = false;
            float w = area.width;
            float y = 0f;
            y += UiText.Draw(0f, y, w, EditHint, UiKit.HintStyle) + 4f;
            if (UiKit.PrimaryButton(new Rect(0f, y, 100f, 26f), DoneText)) done = true;
            bool resetArmed = Time.unscaledTime <= _resetArmedUntil;
            if (WinPx.Button(new Rect(106f, y, 120f, 26f), resetArmed ? ResetSureText : ResetText)) ResetProfile();
            UiKit.Hint(new Rect(106f, y, 120f, 26f), ResetTip);
            y += 30f;
            // One short line under Reset profile (T-0321): what it wipes while armed, then that it was done.
            if (resetArmed) y += UiText.Draw(106f, y, w - 106f, ResetArmedNote, UiKit.HintStyle) + 2f;
            else if (Time.unscaledTime <= _resetDoneUntil) y += UiText.Draw(106f, y, w - 106f, _resetDoneNote, UiKit.HintStyle) + 2f;
            y += 4f;

            int rows = 0;
            for (int i = 0; i < HudLines.All.Length; i++) if (!HudLines.All[i].Locked) rows++;
            float noteH = UiText.Height(w - 20f, LockedNote, UiKit.HintStyle);
            // The panels' rows (the modules') are as tall as their open
            // folds: last pass's height sizes the scroll view.
            int heads = 0;
            for (int i = 0; i < HudLines.All.Length; i++)
                if (!HudLines.All[i].Locked && HudLines.HeadingAt(i) != null) heads++;
            int placed = 0;   // a placed value's row has a second line (In panel)
            for (int i = 0; i < HudLines.All.Length; i++) if (!HudLines.All[i].Locked && IsFree(i)) placed++;
            float contentH = _panelsH + (rows + placed) * RowH + heads * HeadH + (_textLine >= 0 ? TextRowH : 0f) + _idleH + noteH + 8f;
            if (_headStyle == null || !ReferenceEquals(_headFor, UiKit.Palette))
            {
                _headFor = UiKit.Palette;
                _headStyle = UiKit.Style(GUI.skin.label);
                _headStyle.fontStyle = FontStyle.Bold;
                _headStyle.normal.textColor = UiKit.SoftColour;
            }
            float viewH = area.height - y;
            bool scrolls = contentH > viewH;
            float cw = scrolls ? w - 18f : w;
            _scroll = WinPx.BeginScrollView(new Rect(0f, y, w, viewH), _scroll, new Rect(0f, 0f, cw, contentH));
            float ry = DrawProfiles(0f, cw);
            // The on-screen panels first (author, 2026-10-10: set up where
            // they are shown, and visible - not under 24 value rows).
            if (host != null)
            {
                WinPx.Label(new Rect(0f, ry, cw, 22f), PanelsTitle, UiKit.Title);
                float after = host.DrawHudEditors(ry + 24f, cw);
                WinPx.Label(new Rect(0f, after + 4f, cw, 22f), ValuesTitle, UiKit.Title);
                ry = after + 30f;
            }
            _panelsH = ry;
            float idleH = 0f;
            for (int i = 0; i < HudLines.All.Length; i++)
            {
                HudLine l = HudLines.All[i];
                if (l.Locked) continue;
                // The same headings as Settings > HUD's list (T-0263).
                string heading = HudLines.HeadingAt(i);
                if (heading != null)
                {
                    WinPx.Label(new Rect(0f, ry + 2f, cw, 22f), heading, _headStyle);
                    ry += HeadH;
                }
                ry = DrawRow(i, ry, cw, settings);
                // Ticked but blank right now: why, under its row (T-0322).
                if (host != null && (l.External ? settings.ExternalShows : settings.Shows(i)))
                {
                    GUIContent idle = host.HudLineIdle(l.Module, l.Label);
                    if (idle != null)
                    {
                        float ih = UiText.Draw(20f, ry, cw - 24f, idle, UiKit.HintStyle) + 2f;
                        ry += ih;
                        idleH += ih;
                    }
                }
            }
            _idleH = idleH;   // sizes the next pass's scroll view
            UiText.Draw(0f, ry + 4f, cw - 4f, LockedNote, UiKit.HintStyle);
            WinPx.EndScrollView();
            return done;
        }

        private const float TextW = 56f, TrendW = 54f, Gap = 6f, StepW = 26f, ScaleW = 40f, ColumnW = 84f, PanelW = 66f;
        private GUIStyle _centred;

        // name toggle ... (placed:) [-] 1x [+] [To column] [Trend] [Text] -
        // right-aligned, Text always in the same place, Trend on the values
        // that offer it; then the text fields when open.
        private float DrawRow(int i, float ry, float cw, HudSettings settings)
        {
            if (_centred == null)
            {
                _centred = UiKit.Style(GUI.skin.label);
                _centred.alignment = TextAnchor.MiddleCenter;
            }
            float h = RowH - 4f;
            float x = cw - TextW;
            Rect textR = new Rect(x, ry, TextW, h);
            bool open = _textLine == i;
            if (UiKit.Toggle(textR, open, TextText, GUI.skin.button) != open)
            {
                if (open) CloseText();
                else OpenText(i);
            }
            UiKit.Hint(textR, TextTip);

            if (HudLines.All[i].Trend)
            {
                x -= Gap + TrendW;
                Rect trendR = new Rect(x, ry, TrendW, h);
                bool on = _trendOn[i];
                if (UiKit.Toggle(trendR, on, TrendText, GUI.skin.button) != on) SetTrend(i, !on);
                UiKit.Hint(trendR, TrendTip);
            }

            HudWidgetLayout wl = WidgetOf(i);
            // One button for the docking on this line: In panel, or To column.
            // A placed value's row is full: its In panel is on a line under it.
            if (wl == null || !wl.Free && !wl.Docked)
            {
                x -= Gap + PanelW;
                Rect dockR = new Rect(x, ry, PanelW, h);
                if (WinPx.Button(dockR, PanelText)) Dock(i);
                UiKit.Hint(dockR, PanelTip);
            }
            if (wl != null && wl.Docked)
            {
                x -= Gap + ColumnW;
                Rect undockR = new Rect(x, ry, ColumnW, h);
                if (WinPx.Button(undockR, ColumnText)) Attach(i);
                UiKit.Hint(undockR, UndockTip);
            }
            if (wl != null && wl.Free)
            {
                x -= Gap + ColumnW;
                Rect backR = new Rect(x, ry, ColumnW, h);
                if (WinPx.Button(backR, ColumnText)) Attach(i);
                UiKit.Hint(backR, ColumnTip);
                x -= Gap + StepW;
                if (WinPx.Button(new Rect(x, ry, StepW, h), BiggerText)) SetScale(wl, wl.Scale + 0.25f);
                x -= ScaleW;
                WinPx.Label(new Rect(x, ry, ScaleW, h), ScaleText(wl.Scale), _centred);
                x -= StepW;
                if (WinPx.Button(new Rect(x, ry, StepW, h), SmallerText)) SetScale(wl, wl.Scale - 0.25f);
            }

            // The 100% totals: its own module's switch (HudLines External).
            bool external = HudLines.All[i].External;
            bool shown = external ? settings.ExternalShows : settings.Shows(i);
            Rect nameR = new Rect(0f, ry, x - Gap, h);
            bool now = UiKit.Toggle(nameR, shown, _toggleNames[i]);
            if (now != shown)
            {
                if (external) settings.SetExternalShows(now);
                else settings.SetShows(i, now);
            }
            UiKit.Hint(nameR, _descriptions[i]);
            ry += RowH;

            if (wl != null && wl.Free)
            {
                Rect dockR = new Rect(cw - PanelW, ry - 4f, PanelW, h);
                if (WinPx.Button(dockR, PanelText)) Dock(i);
                UiKit.Hint(dockR, PanelTip);
                ry += RowH;
            }

            if (_textLine == i)
            {
                float half = (cw - 8f) * 0.5f;
                WinPx.Label(new Rect(8f, ry, 46f, h), BeforeText);
                string b = WinPx.TextField(new Rect(54f, ry, half - 54f, h), _beforeEdit, HudLayout.MaxText);
                WinPx.Label(new Rect(half + 8f, ry, 40f, h), AfterText);
                string a = WinPx.TextField(new Rect(half + 48f, ry, cw - half - 48f, h), _afterEdit, HudLayout.MaxText);
                if (!string.Equals(b, _beforeEdit) || !string.Equals(a, _afterEdit))
                {
                    _beforeEdit = b;
                    _afterEdit = a;
                    ApplyText();
                }
                ry += TextRowH;
            }
            return ry;
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
