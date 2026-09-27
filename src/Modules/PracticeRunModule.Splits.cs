using System.Collections.Generic;
using BepInEx.Configuration;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // The splits table (v0.24.146; author, 2026-09-27): LiveSplit's view of
    // a timed segment, on the game screen and in the Runs tab, built from
    // this runner's attempts on the current route (Data/SplitTable).
    //
    // - One comparison drives everything: the Runs tab's "Compare to"
    //   (best, last, average, best segments) - the table, the delta, the
    //   ghost and the lines (best segments races the PB's line: it is no
    //   single attempt). Cycle it with the (unbound) comparison key.
    // - Every LiveSplit column and summary line, each toggleable (author:
    //   "all of the above, and anything missing from livesplit").
    // - Stats are built before a run and kept through it, so a gold is
    //   judged against the golds before this run, as LiveSplit does.
    // - Text is rebuilt in Tick (10 Hz), never in OnGUI.
    // ------------------------------------------------------------------
    public sealed partial class PracticeRunModule
    {
        private enum Col { Delta, SplitTime, SegmentTime, SegmentDelta, BestSegment, TimeSave }
        private const int ColCount = 6;
        private static readonly string[] ColTitles = { "Delta", "Split", "Segment", "Seg +/-", "Best seg", "Save" };
        private static readonly string[] ColOptions =
        {
            "Delta", "Split time", "Segment time", "Segment delta", "Best segment", "Possible time save"
        };

        private enum Line { Previous, SumOfBest, BestPossible, Pace, Save, Pb, Attempts }
        private const int LineCount = 7;
        private static readonly string[] LineOptions =
        {
            "Previous segment", "Sum of best", "Best possible time", "Current pace", "Possible time save", "Personal best", "Attempts"
        };

        private ConfigEntry<bool> _splitsPanel;
        private ConfigEntry<float> _panelX, _panelY, _panelWidth;
        private ConfigEntry<int> _panelRows;
        private readonly ConfigEntry<bool>[] _cols = new ConfigEntry<bool>[ColCount];
        private readonly ConfigEntry<bool>[] _lines2 = new ConfigEntry<bool>[LineCount];
        private ConfigEntry<string> _runnerName, _localRunnerId;

        private SplitStats _stats;
        private float[] _times = new float[0];
        private SplitRow[] _rowsData = new SplitRow[0];
        private SplitSummary _summary;
        private bool _splitsDirty = true;
        private float _nextSplitText;

        // Cached text: row r, column c -> _cells[r * ColCount + c].
        private readonly List<GUIContent> _rowNames = new List<GUIContent>();
        private readonly List<GUIContent> _cells = new List<GUIContent>();
        private SplitColour[] _rowColours = new SplitColour[0];
        private bool[] _rowCurrent = new bool[0];
        private readonly GUIContent[] _lineLabels = new GUIContent[LineCount];
        private readonly GUIContent[] _lineValues = new GUIContent[LineCount];
        private readonly GUIContent[] _colTitles = new GUIContent[ColCount];
        private readonly GUIContent[] _colOptionText = new GUIContent[ColCount];
        private readonly GUIContent[] _lineOptionText = new GUIContent[LineCount];
        private readonly GUIContent _compareTitle = new GUIContent("");
        private readonly GUIContent _runnerText = new GUIContent("");
        private readonly GUIContent _panelSizeText = new GUIContent("");
        private readonly GUIContent _splitsHint = new GUIContent("");
        private int _shownRows;
        private bool _splitsOptionsOpen;

        private GUIStyle _cellStyle, _nameStyle, _titleStyle;
        private GUIStyle[] _colourStyles;
        private GUIStyle _panelStyle;

        private void InitSplits(ModuleContext ctx)
        {
            ConfigFile c = ctx.Config;
            _splitsPanel = c.Bind("Splits", "Panel", true, "Show the splits panel on the game screen while a timed segment is current.");
            _panelX = c.Bind("Splits", "PanelX", -1f, "Panel position from the left, in pixels (-1 = against the right edge).");
            _panelY = c.Bind("Splits", "PanelY", 140f, "Panel position from the top, in pixels.");
            _panelWidth = c.Bind("Splits", "PanelWidth", 300f, "Panel width in pixels.");
            _panelRows = c.Bind("Splits", "PanelRows", 12, "Most split rows the panel shows at once (the end is always shown).");
            bool[] colDefaults = { true, true, false, false, false, false };
            for (int i = 0; i < ColCount; i++)
            {
                _cols[i] = c.Bind("Splits", "Column" + (Col)i, colDefaults[i], "Splits column: " + ColOptions[i] + ".");
                _colTitles[i] = new GUIContent(ColTitles[i]);
                _colOptionText[i] = new GUIContent(" " + ColOptions[i]);
            }
            bool[] lineDefaults = { true, true, true, false, false, false, true };
            for (int i = 0; i < LineCount; i++)
            {
                _lines2[i] = c.Bind("Splits", "Show" + (Line)i, lineDefaults[i], "Splits summary line: " + LineOptions[i] + ".");
                _lineLabels[i] = new GUIContent(LineOptions[i]);
                _lineValues[i] = new GUIContent("");
                _lineOptionText[i] = new GUIContent(" " + LineOptions[i]);
            }
            _runnerName = c.Bind("Runs", "RunnerName", "", "Your name on recorded attempts. Empty = your Steam name.");
            _localRunnerId = c.Bind("Runs", "LocalRunnerId", "", "Runner id used when Steam is not available (made once).");
        }

        // --- identity ---------------------------------------------------------

        private string RunnerNameNow()
        {
            string typed = _runnerName.Value != null ? _runnerName.Value.Trim() : "";
            if (typed.Length > 0) return typed;
            string steam = RunnerIdentity.SteamName();
            return steam ?? "";
        }

        private string RunnerIdNow()
        {
            string steam = RunnerIdentity.SteamRunnerId();
            if (steam != null) return steam;
            if (string.IsNullOrEmpty(_localRunnerId.Value)) _localRunnerId.Value = RunnerIdentity.RandomRunnerId();
            return _localRunnerId.Value;
        }

        private void StampAttempt(Attempt done)
        {
            if (_segment != null && _splits.Count == _segment.Checkpoints.Count) done.Splits = _splits.ToArray();
            done.RunnerId = RunnerIdNow();
            done.RunnerName = RunnerNameNow();
        }

        // --- state ------------------------------------------------------------

        private int SplitRows { get { return _segment == null ? 0 : _segment.Checkpoints.Count + 1; } }

        /// Before a run: stats from the attempts so far, no times yet.
        private void ResetSplits()
        {
            int rows = SplitRows;
            _stats = rows > 0 ? SplitStats.Build(_attempts, rows - 1) : null;
            if (_times.Length != rows) _times = new float[rows];
            for (int i = 0; i < rows; i++) _times[i] = float.NaN;
            if (_rowsData.Length != rows) _rowsData = new SplitRow[rows];
            _splitsDirty = true;
        }

        private void RecordSplit(int row, float t)
        {
            if (row >= 0 && row < _times.Length) _times[row] = t;
            _splitsDirty = true;
        }

        private void FinishSplits(float duration)
        {
            if (_times.Length > 0) _times[_times.Length - 1] = duration;
            _splitsDirty = true;
        }

        private float[] ComparisonSplits()
        {
            if (_stats == null) return null;
            switch (_referenceKind)
            {
                case Reference.Last: return _stats.LastSplits;
                case Reference.Average: return _stats.AverageSplits;
                case Reference.BestSegments: return _stats.BestSegmentSplits;
                default: return _stats.PbSplits;
            }
        }

        private static string ComparisonName(Reference r)
        {
            switch (r)
            {
                case Reference.Last: return "Last run";
                case Reference.Average: return "Average";
                case Reference.BestSegments: return "Best segments";
                default: return "Personal best";
            }
        }

        private void CycleComparison()
        {
            _referenceKind = (Reference)(((int)_referenceKind + 1) % 4);
            SelectReference();
            _splitsDirty = true;
            if (!TabShowing) Ctx.Notice.Show("Compare to: " + ComparisonName(_referenceKind), 1.5f);
        }

        // --- text (Tick) ---------------------------------------------------------

        private void RefreshSplits()
        {
            bool running = _recorder.State == RunRecorder.RunState.Running;
            if (!_splitsDirty && !(running && Time.unscaledTime >= _nextSplitText)) return;
            if (!_splitsDirty && !TabShowing && !PanelShowing) return;
            _splitsDirty = false;
            _nextSplitText = Time.unscaledTime + 0.1f;

            if (TabShowing && _splitsOptionsOpen)
            {
                string name = RunnerNameNow();
                _runnerText.text = "Runner: " + (name.Length > 0 ? name : "(no name - Steam not found; type one above)") +
                                   (_runnerName.Value.Trim().Length == 0 && name.Length > 0 ? "  (your Steam name)" : "");
                _panelSizeText.text = "width " + Mathf.RoundToInt(_panelWidth.Value) + " px, " + _panelRows.Value + " rows";
            }

            int rows = SplitRows;
            float[] compare = ComparisonSplits();
            if (_segment == null || _stats == null || compare == null || rows != _times.Length || rows != _stats.Rows)
            {
                _shownRows = 0;
                _compareTitle.text = "";
                _splitsHint.text = _segment == null ? "" : "Splits appear once the segment is armed.";
                return;
            }

            _summary = SplitTable.Fill(_stats, compare, _times, running, _recorder.Elapsed, _rowsData);
            _compareTitle.text = _segment.Name + "  -  vs " + ComparisonName(_referenceKind);
            _splitsHint.text = _stats.Completed == 0 ? "No finished attempts yet: the columns fill in as you run it."
                             : _stats.WithSplits == 0 && rows > 1 ? "Your earlier times were recorded before split times were saved - only their totals show."
                             : "";

            if (_rowColours.Length != rows) { _rowColours = new SplitColour[rows]; _rowCurrent = new bool[rows]; }
            while (_rowNames.Count < rows) _rowNames.Add(new GUIContent(""));
            while (_cells.Count < rows * ColCount) _cells.Add(new GUIContent(""));

            for (int r = 0; r < rows; r++)
            {
                SplitRow d = _rowsData[r];
                _rowNames[r].text = _segment.SplitName(r);
                _rowColours[r] = d.Colour;
                _rowCurrent[r] = d.Current;
                bool reached = !float.IsNaN(d.Time);
                // A row not reached shows the comparison's time in its split
                // and segment columns, as LiveSplit does.
                _cells[r * ColCount + (int)Col.Delta].text = SplitTable.Delta(d.Delta);
                _cells[r * ColCount + (int)Col.SplitTime].text = SplitTable.Time(reached ? d.Time : d.Compare);
                _cells[r * ColCount + (int)Col.SegmentTime].text = SplitTable.Time(reached ? d.Segment : d.CompareSegment);
                _cells[r * ColCount + (int)Col.SegmentDelta].text = SplitTable.Delta(d.SegmentDelta);
                _cells[r * ColCount + (int)Col.BestSegment].text = SplitTable.Time(d.BestSegment);
                _cells[r * ColCount + (int)Col.TimeSave].text = float.IsNaN(d.TimeSave) ? "" : SplitTable.Time(d.TimeSave);
            }
            _shownRows = rows;

            _lineValues[(int)Line.Previous].text = SplitTable.Delta(_summary.PreviousSegment) + (_summary.PreviousLive ? " (live)" : "");
            _lineLabels[(int)Line.Previous].text = _summary.PreviousLive ? "Live segment" : "Previous segment";
            _lineValues[(int)Line.SumOfBest].text = SplitTable.Time(_summary.SumOfBest);
            _lineValues[(int)Line.BestPossible].text = SplitTable.Time(_summary.BestPossible);
            _lineValues[(int)Line.Pace].text = SplitTable.Time(_summary.CurrentPace);
            _lineValues[(int)Line.Save].text = SplitTable.Time(_summary.PossibleSave);
            _lineValues[(int)Line.Pb].text = SplitTable.Time(_summary.Pb);
            _lineValues[(int)Line.Attempts].text = _stats.Completed.ToString();
        }

        // --- drawing ---------------------------------------------------------------

        private bool PanelShowing
        {
            get { return _splitsPanel != null && _splitsPanel.Value && Enabled && _segment != null && _shownRows > 0; }
        }

        private void EnsureSplitStyles()
        {
            if (_cellStyle != null) return;
            _cellStyle = new GUIStyle(GUI.skin.label);
            _cellStyle.alignment = TextAnchor.MiddleRight;
            _cellStyle.padding = new RectOffset(0, 2, 0, 0);
            _cellStyle.wordWrap = false;
            _cellStyle.clipping = TextClipping.Clip;
            _nameStyle = new GUIStyle(_cellStyle);
            _nameStyle.alignment = TextAnchor.MiddleLeft;
            _titleStyle = new GUIStyle(_nameStyle);
            _titleStyle.fontStyle = FontStyle.Bold;

            Color[] colours =
            {
                Color.white,
                new Color(0.25f, 0.85f, 0.35f),   // ahead, gaining
                new Color(0.6f, 0.95f, 0.6f),     // ahead, losing
                new Color(0.95f, 0.6f, 0.6f),     // behind, gaining
                new Color(0.9f, 0.25f, 0.25f),    // behind, losing
                new Color(1f, 0.8f, 0.2f),        // gold
            };
            _colourStyles = new GUIStyle[colours.Length];
            for (int i = 0; i < colours.Length; i++)
            {
                _colourStyles[i] = new GUIStyle(_cellStyle);
                _colourStyles[i].normal.textColor = colours[i];
            }

            _panelStyle = new GUIStyle(GUI.skin.box);
            Texture2D bg = new Texture2D(1, 1);
            bg.SetPixel(0, 0, new Color(0.06f, 0.06f, 0.06f, 0.82f));
            bg.Apply();
            _panelStyle.normal.background = bg;
        }

        public override void DrawScreen()
        {
            if (!PanelShowing) return;
            EnsureSplitStyles();

            float w = Mathf.Clamp(_panelWidth.Value, 160f, Screen.width);
            float h = PanelHeight(w);
            float x = _panelX.Value < 0f ? Screen.width - w - 8f : Mathf.Min(_panelX.Value, Screen.width - w);
            float y = Mathf.Clamp(_panelY.Value, 0f, Mathf.Max(0f, Screen.height - h));

            GUI.Box(new Rect(x, y, w, h), GUIContent.none, _panelStyle);
            DrawSplitsTable(x + 6f, y + 4f, w - 12f, _panelRows.Value, false);
        }

        private int VisibleRows(int maxRows)
        {
            return maxRows <= 1 || _shownRows <= maxRows ? _shownRows : maxRows;
        }

        private float PanelHeight(float w)
        {
            int lines = 0;
            for (int i = 0; i < LineCount; i++) if (_lines2[i].Value) lines++;
            return 8f + 18f + VisibleRows(_panelRows.Value) * 18f + (lines > 0 ? 4f + lines * 18f : 0f);
        }

        /// The table at (x, y), `w` wide; returns its height. `header` adds
        /// the column titles (the tab has room for them).
        private float DrawSplitsTable(float x, float y, float w, int maxRows, bool header)
        {
            const float rowH = 18f, colW = 62f;
            float y0 = y;
            int cols = 0;
            for (int c = 0; c < ColCount; c++) if (_cols[c].Value) cols++;
            float nameW = Mathf.Max(60f, w - cols * colW);

            GUI.Label(new Rect(x, y, w, rowH), _compareTitle, _titleStyle);
            y += rowH;

            if (header && cols > 0)
            {
                float cx = x + nameW;
                for (int c = 0; c < ColCount; c++)
                {
                    if (!_cols[c].Value) continue;
                    GUI.Label(new Rect(cx, y, colW, rowH), _colTitles[c], _cellStyle);
                    cx += colW;
                }
                y += rowH;
            }

            // Which rows: all, or a window around the current one with the
            // end always last.
            int visible = VisibleRows(maxRows);
            int start = 0;
            if (visible < _shownRows)
            {
                int focus = 0;
                for (int r = 0; r < _shownRows; r++) if (_rowCurrent[r] || !float.IsNaN(_times[r])) focus = r;
                start = Mathf.Clamp(focus - (visible - 2), 0, _shownRows - visible);
            }

            for (int k = 0; k < visible; k++)
            {
                int r = start + k;
                if (visible < _shownRows && k == visible - 1) r = _shownRows - 1;
                GUIStyle nameStyle = _rowCurrent[r] ? _titleStyle : _nameStyle;
                GUI.Label(new Rect(x, y, nameW, rowH), _rowNames[r], nameStyle);
                float cx = x + nameW;
                for (int c = 0; c < ColCount; c++)
                {
                    if (!_cols[c].Value) continue;
                    GUIStyle st = _cellStyle;
                    if (c == (int)Col.Delta || c == (int)Col.SegmentDelta) st = _colourStyles[(int)_rowColours[r]];
                    GUI.Label(new Rect(cx, y, colW, rowH), _cells[r * ColCount + c], st);
                    cx += colW;
                }
                y += rowH;
            }

            bool any = false;
            for (int i = 0; i < LineCount; i++)
            {
                if (!_lines2[i].Value) continue;
                if (!any) { y += 4f; any = true; }
                GUI.Label(new Rect(x, y, w - 90f, rowH), _lineLabels[i], _nameStyle);
                GUIStyle st = i == (int)Line.Previous ? _colourStyles[(int)SplitTable.ColourOf(_summary.PreviousSegment, _summary.PreviousSegment, float.NaN, float.NaN)] : _cellStyle;
                GUI.Label(new Rect(x + w - 90f, y, 90f, rowH), _lineValues[i], st);
                y += rowH;
            }
            return y - y0;
        }

        /// The Runs tab's splits section: the table, then its options.
        private float DrawSplitsSection(float y, float w)
        {
            EnsureSplitStyles();
            if (_shownRows > 0) y += DrawSplitsTable(0f, y, w, 0, true) + 4f;
            y += UiText.Draw(0, y, w, _splitsHint);

            if (GUI.Button(new Rect(0, y, 150, 22), _splitsOptionsOpen ? "Splits options  ^" : "Splits options  v"))
                _splitsOptionsOpen = !_splitsOptionsOpen;
            y += 26f;
            if (!_splitsOptionsOpen) return y;

            bool panel = GUI.Toggle(new Rect(0, y, w, 20), _splitsPanel.Value, " Show the splits panel on screen (F5 hides all overlay UI)");
            if (panel != _splitsPanel.Value) _splitsPanel.Value = panel;
            y += 22f;

            y = FlowToggles(y, w, "Columns:", _cols, _colOptionText);
            y = FlowToggles(y, w, "Lines:", _lines2, _lineOptionText);

            GUI.Label(new Rect(0, y, 110, 20), "Panel position");
            if (GUI.Button(new Rect(114, y - 1, 90, 22), "Top right")) { _panelX.Value = -1f; _panelY.Value = 140f; }
            if (GUI.Button(new Rect(208, y - 1, 90, 22), "Top left")) { _panelX.Value = 8f; _panelY.Value = 140f; }
            if (GUI.Button(new Rect(302, y - 1, 90, 22), "Lower right")) { _panelX.Value = -1f; _panelY.Value = Screen.height * 0.55f; }
            y += 26f;
            GUI.Label(new Rect(0, y, 110, 20), "Width / rows");
            if (GUI.Button(new Rect(114, y - 1, 30, 22), "-")) { _panelWidth.Value = Mathf.Max(160f, _panelWidth.Value - 20f); _splitsDirty = true; }
            if (GUI.Button(new Rect(148, y - 1, 30, 22), "+")) { _panelWidth.Value = Mathf.Min(900f, _panelWidth.Value + 20f); _splitsDirty = true; }
            if (GUI.Button(new Rect(190, y - 1, 30, 22), "-")) { _panelRows.Value = Mathf.Max(3, _panelRows.Value - 1); _splitsDirty = true; }
            if (GUI.Button(new Rect(224, y - 1, 30, 22), "+")) { _panelRows.Value = Mathf.Min(40, _panelRows.Value + 1); _splitsDirty = true; }
            GUI.Label(new Rect(262, y, Mathf.Max(60f, w - 262f), 20), _panelSizeText);
            y += 26f;

            GUI.Label(new Rect(0, y, 110, 20), "Runner name");
            string typed = GUI.TextField(new Rect(114, y - 2, Mathf.Max(80f, w - 124f), 22), _runnerName.Value ?? "");
            if (typed != _runnerName.Value) { _runnerName.Value = typed; _splitsDirty = true; }
            y += 26f;
            y += UiText.Draw(0, y, w, _runnerText);
            return y + 4f;
        }

        // Toggles laid out left to right, wrapping at the tab's width.
        private float FlowToggles(float y, float w, string title, ConfigEntry<bool>[] entries, GUIContent[] labels)
        {
            GUI.Label(new Rect(0, y, 70, 20), title);
            float x = 74f;
            for (int i = 0; i < entries.Length; i++)
            {
                const float tw = 150f;
                if (x + tw > w && x > 74f) { x = 74f; y += 22f; }
                bool v = GUI.Toggle(new Rect(x, y, tw, 20), entries[i].Value, labels[i]);
                if (v != entries[i].Value) { entries[i].Value = v; _splitsDirty = true; }
                x += tw;
            }
            return y + 24f;
        }
    }
}
