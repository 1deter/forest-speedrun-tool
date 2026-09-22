using System.Collections.Generic;
using ForestOverlay.Core;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // 100% tracking: the nature guide and the todo list. INFO-ONLY.
    //
    // Asked for by the 100% runners, in their own words: entries listed
    // individually but grouped by book page, plus a plain "23/40" counter
    // for the todo list. So the layout follows that rather than inventing
    // a different one.
    //
    // Flower and plant COORDINATES are deliberately not here. The author
    // judged that over the line for the category, and that call stands.
    // This shows what you have and have not found - the same information
    // the in-game book already gives you, without paging through it.
    // ------------------------------------------------------------------
    public sealed class CollectiblesModule : OverlayModule
    {
        private const float RefreshInterval = 1f;
        private const float RowHeight = 19f;

        public override string Id { get { return "collectibles"; } }
        public override string DisplayName { get { return "100%"; } }
        public override bool HasTab { get { return true; } }
        public override string TabTitle { get { return "100%"; } }
        public override int TabOrder { get { return 45; } }

        private SurvivalBookReader _book;
        private float _nextRefresh;

        private float _tabW;
        private float _tabH;
        private Vector2 _scroll;

        private bool _showFound = true;
        private bool _showMissing = true;
        private bool _pinSummary;

        private GUIStyle _rowStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _doneStyle;
        private GUIStyle _missingStyle;

        // Rows are cached; this list can be a few hundred entries and OnGUI
        // runs several times a frame.
        private readonly List<GUIContent> _labels = new List<GUIContent>();
        private readonly List<bool> _isHeader = new List<bool>();
        private readonly List<bool> _isDone = new List<bool>();

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);
            _book = new SurvivalBookReader(ctx.Log);
        }

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("tab.collectibles", KeyCode.None, "Open 100% tab", OpenMyTab);
        }

        public override void Tick()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshInterval;

            _book.Refresh();
            RebuildRows();
        }

        public override void ContributeHud(HudBuilder hud)
        {
            // Opt-in: most runs do not care, and the HUD is not free space.
            if (!_pinSummary) return;
            if (_book.TotalEntries == 0 && _book.Todo.Count == 0) return;

            hud.Pair("Guide", _book.TotalDone + "/" + _book.TotalEntries);
            hud.Pair("Tasks", _book.TodoDone + "/" + _book.Todo.Count);
        }

        // ------------------------------------------------------------------
        private void RebuildRows()
        {
            int n = 0;

            n = AddRow(n, "NATURE GUIDE   " + _book.TotalDone + "/" + _book.TotalEntries, true, false);

            for (int p = 0; p < _book.Pages.Count; p++)
            {
                BookPage page = _book.Pages[p];

                n = AddRow(n, "  " + page.Title + "   " + page.DoneCount + "/" + page.Entries.Count,
                           true, false);

                for (int e = 0; e < page.Entries.Count; e++)
                {
                    BookEntry entry = page.Entries[e];

                    if (entry.Done && !_showFound) continue;
                    if (!entry.Done && !_showMissing) continue;

                    string mark = entry.Done ? "found" : "not found";
                    if (!entry.Done && entry.UnlockLevel > 0) mark = "partial (" + entry.UnlockLevel + ")";

                    n = AddRow(n, "      " + entry.Name + "   -   " + mark, false, entry.Done);
                }
            }

            if (_book.Todo.Count > 0)
            {
                n = AddRow(n, "", true, false);
                n = AddRow(n, "TODO LIST   " + _book.TodoDone + "/" + _book.Todo.Count, true, false);

                for (int i = 0; i < _book.Todo.Count; i++)
                {
                    BookEntry task = _book.Todo[i];

                    if (task.Done && !_showFound) continue;
                    if (!task.Done && !_showMissing) continue;

                    n = AddRow(n, "      " + task.Name + "   -   " + (task.Done ? "done" : "to do"),
                               false, task.Done);
                }
            }

            // Trim without reallocating: extra cached rows are simply not
            // drawn.
            _rowCount = n;
        }

        private int _rowCount;

        private int AddRow(int index, string text, bool header, bool done)
        {
            if (index < _labels.Count)
            {
                _labels[index].text = text;
                _isHeader[index] = header;
                _isDone[index] = done;
            }
            else
            {
                _labels.Add(new GUIContent(text));
                _isHeader.Add(header);
                _isDone.Add(done);
            }

            return index + 1;
        }

        // ------------------------------------------------------------------
        public override void DrawTab(Rect area)
        {
            _tabW = area.width;
            _tabH = area.height;
            EnsureStyles();

            float w = _tabW;

            GUI.Label(new Rect(0, 2, w - 200, 20), _book.Status);

            bool pin = GUI.Toggle(new Rect(w - 190, 2, 190, 20), _pinSummary, " show totals on the HUD");
            if (pin != _pinSummary) _pinSummary = pin;

            bool found = GUI.Toggle(new Rect(0, 26, 110, 20), _showFound, " found");
            if (found != _showFound) { _showFound = found; RebuildRows(); }

            bool missing = GUI.Toggle(new Rect(116, 26, 120, 20), _showMissing, " not found");
            if (missing != _showMissing) { _showMissing = missing; RebuildRows(); }

            if (GUI.Button(new Rect(w - 90, 26, 90, 22), "Refresh"))
            {
                _book.Refresh();
                RebuildRows();
            }

            DrawList(new Rect(0, 54, w, _tabH - 58));
        }

        private void EnsureStyles()
        {
            if (_rowStyle != null) return;

            _rowStyle = new GUIStyle(GUI.skin.label);
            _rowStyle.alignment = TextAnchor.MiddleLeft;
            _rowStyle.padding = new RectOffset(2, 2, 0, 0);

            _headerStyle = new GUIStyle(_rowStyle);
            _headerStyle.fontStyle = FontStyle.Bold;

            // Muted green / amber rather than saturated, for the same
            // reason the zone colours were toned down.
            _doneStyle = new GUIStyle(_rowStyle);
            _doneStyle.normal.textColor = new Color(0.45f, 0.82f, 0.50f);

            _missingStyle = new GUIStyle(_rowStyle);
            _missingStyle.normal.textColor = new Color(0.92f, 0.72f, 0.32f);
        }

        private void DrawList(Rect area)
        {
            GUI.Box(area, GUIContent.none);

            Rect content = new Rect(0, 0, area.width - 20f, _rowCount * RowHeight + 4f);
            _scroll = GUI.BeginScrollView(area, _scroll, content);

            // Virtualised, same as every other long list here.
            for (int i = 0; i < _rowCount && i < _labels.Count; i++)
            {
                float y = 2f + i * RowHeight;
                if (y + RowHeight < _scroll.y || y > _scroll.y + area.height) continue;

                GUIStyle style = _isHeader[i] ? _headerStyle
                                              : (_isDone[i] ? _doneStyle : _missingStyle);

                GUI.Label(new Rect(4f, y, content.width - 8f, RowHeight), _labels[i], style);
            }

            GUI.EndScrollView();

            if (_rowCount == 0)
            {
                GUI.Label(new Rect(area.x + 8, area.y + 8, area.width - 16, 60),
                          "Nothing to show yet.\n\n" + _book.Status, _rowStyle);
            }
        }
    }
}
