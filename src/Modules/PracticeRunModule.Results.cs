using System.Collections.Generic;
using BepInEx.Configuration;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // The results panel after a run (maks's idea; docs/backlog.md *Run mode
    // and anti-splicing*). When a timed run finishes - a practice segment
    // or a run mode attempt - a panel shows the final time against the
    // previous PB and the chosen comparison, every split with its delta,
    // what it saved or lost and its gold, the best possible time with this
    // run's golds in, PB chance for the next run, the attempt count and
    // total playtime; in run mode also the last code, the report and the
    // receipt (its upload state follows the outbox) with the attempt page.
    //
    // - Built once at the finish (Data/RunResults); only the receipt line
    //   is rebuilt, in Tick, when the upload's state moves on.
    // - Closes with Close, its key (unbound), or when the next run's clock
    //   starts (ResetSplits); F5 hides it with the rest of the UI.
    // - Buttons only while the cursor is the runner's (the F2 window or the
    //   pause menu): with the game's locked cursor a click lands in the
    //   middle of the screen and would hit them. Dragged by its title then.
    // - [Splits] ResultsPanel turns it off.
    // ------------------------------------------------------------------
    public sealed partial class PracticeRunModule
    {
        private const int ResCols = 5;   // name, time, delta, segment, saved / lost
        private const int ResMaxRows = 16;
        private const float ResRowH = 18f;

        private ConfigEntry<bool> _resultsCfg;
        private ConfigEntry<float> _resX, _resY, _resWidth;

        private bool _resultsOpen;
        private RunResult _result;
        private RunModeOutcome _resOutcome;
        private int _resUploadVersion = -1;
        private bool _resClickable;
        private float _nextResCheck;
        private int _resRows;

        private readonly GUIContent _resTitle = new GUIContent("");
        private readonly GUIContent _resHeadline = new GUIContent("");
        private readonly GUIContent _resVerdict = new GUIContent("");
        private readonly GUIContent _resCompare = new GUIContent("");
        private readonly GUIContent _resGolds = new GUIContent("");
        private readonly GUIContent _resLoads = new GUIContent("");
        private readonly GUIContent _resMore = new GUIContent("");
        private readonly GUIContent _resRunMode = new GUIContent("");
        private readonly GUIContent _resHint = new GUIContent(
            "ESC (pause menu) or F2 for the buttons. Closes when the next run starts.");
        private readonly GUIContent _resRunModeHint = new GUIContent(
            "ESC (pause menu) or F2 for the buttons. Closes when the next run starts; Restart on the run spot starts the next attempt.");
        private static readonly string[] ResLineLabels = { "Best possible time", "PB chance (next run)", "Attempts", "Total playtime" };
        private readonly GUIContent[] _resLineLabels = new GUIContent[ResLineLabels.Length];
        private readonly GUIContent[] _resLineValues = new GUIContent[ResLineLabels.Length];
        private static readonly GUIContent[] ResColTitles =
        {
            new GUIContent("Split"), new GUIContent("Time"), new GUIContent("Delta"), new GUIContent("Segment"), new GUIContent("Saved / lost")
        };
        private readonly List<GUIContent> _resCells = new List<GUIContent>();
        private SplitColour[] _resDeltaColour = new SplitColour[0];
        private int[] _resSavedLost = new int[0];   // -1 saved, 1 lost, 0 neither
        private bool[] _resGold = new bool[0];
        private string _resLink = "";

        private GUIStyle _resHeadStyle, _resHeadGold, _resGoldWrap;

        // Dragging by the title, as the splits panel is dragged.
        private bool _resDragging;
        private Vector2 _resDragOffset;
        private float _resDragX, _resDragY;

        private void InitResults(ModuleContext ctx)
        {
            ConfigFile c = ctx.Config;
            _resultsCfg = c.Bind("Splits", "ResultsPanel", true, "Show a results panel when a timed run finishes (practice or run mode).");
            _resX = c.Bind("Splits", "ResultsX", -1f, "Results panel position from the left, in pixels (-1 = centred).");
            _resY = c.Bind("Splits", "ResultsY", 120f, "Results panel position from the top, in pixels.");
            _resWidth = c.Bind("Splits", "ResultsWidth", 480f, "Results panel width in pixels.");
            for (int i = 0; i < ResLineLabels.Length; i++)
            {
                _resLineLabels[i] = new GUIContent(ResLineLabels[i]);
                _resLineValues[i] = new GUIContent("");
            }
        }

        private void CloseResults()
        {
            _resultsOpen = false;
            _resDragging = false;
        }

        private void CloseResultsKey()
        {
            if (_resultsOpen) CloseResults();
        }

        private bool ResultsShowing { get { return _resultsOpen && Timing && _segment != null && _result != null; } }

        // --- built once, at the finish -------------------------------------------

        /// After FinishRun has saved the attempt and filled the last split:
        /// `_stats` are still the ones from before this run.
        private void ShowResults(Attempt done, RunModeOutcome outcome)
        {
            if (_resultsCfg == null || !_resultsCfg.Value || _segment == null || _stats == null) return;
            int rows = SplitRows;
            if (rows == 0 || _times.Length != rows || _stats.Rows != rows) return;

            string[] names = new string[rows];
            for (int r = 0; r < rows; r++) names[r] = _segment.SplitName(r);
            float[] compare = ComparisonSplits();
            bool compareIsPb = ReferenceEquals(compare, _stats.PbSplits);
            // One precision for the whole panel (author, 2026-10-05: an 11.331 PB
            // "by 0.03 (was 11.37)" read wrong): the finer of the two settings.
            int p = Mathf.Max(_timeDecimals.Value, _deltaDecimals.Value);
            _result = RunResults.Build(_stats, compare, compareIsPb, ComparisonName(), _times, names, p, p);
            _resOutcome = outcome;

            _resTitle.text = _segment.Name + "  -  results";
            _resHeadline.text = _result.Headline + (_result.NewPb ? "   NEW PB" : _result.Tied ? "   TIED PB" : "");
            _resVerdict.text = _result.Verdict;
            _resCompare.text = _result.CompareLine;
            _resGolds.text = _result.GoldLine;
            _result.LoadLine = RunResults.LoadLine(done.Duration, done.Loads, done.LoadTime, p, _cols[(int)Col.Lrt].Value);
            _resLoads.text = _result.LoadLine;

            // The table's cells: copied once, drawn every frame.
            _resRows = rows;
            while (_resCells.Count < rows * ResCols) _resCells.Add(new GUIContent(""));
            if (_resDeltaColour.Length != rows)
            {
                _resDeltaColour = new SplitColour[rows];
                _resSavedLost = new int[rows];
                _resGold = new bool[rows];
            }
            for (int r = 0; r < rows; r++)
            {
                ResultRow row = _result.Rows[r];
                _resCells[r * ResCols + 0].text = row.Name + (row.Gold ? "  (gold)" : "");
                _resCells[r * ResCols + 1].text = row.Time;
                _resCells[r * ResCols + 2].text = row.Delta;
                _resCells[r * ResCols + 3].text = row.Segment;
                _resCells[r * ResCols + 4].text = row.SavedLost;
                _resDeltaColour[r] = row.Colour;
                _resSavedLost[r] = row.Saved ? -1 : row.Lost ? 1 : 0;
                _resGold[r] = row.Gold;
            }
            int hidden = rows > ResMaxRows ? rows - ResMaxRows : 0;
            _resMore.text = hidden > 0 ? hidden + " more row(s) - the Runs tab's splits table has them all." : "";

            // PB chance for the NEXT run (this one's is decided): this run in
            // the history, its time the PB if it beat it.
            BuildHistory(rows);
            float pb = float.IsNaN(_stats.Pb) ? done.Duration : Mathf.Min(_stats.Pb, done.Duration);
            _resLineValues[0].text = _result.BestPossible;
            _resLineValues[1].text = PbChance.Compute(_history, SplitStats.Filled(rows), pb, _pbRandom);
            _resLineValues[2].text = AttemptsText();
            _resLineValues[3].text = Playtime.Format(Playtime.Total(null, _unfinished, _playedFinished));
            _pbChanceDirty = true;   // the splits panel rebuilds its own

            _resLink = "";
            _resUploadVersion = -1;
            RefreshResultsRunMode();
            _resultsOpen = true;
            Ctx.Log.LogInfo("Results panel: " + _result.Headline + " - " + _result.Verdict +
                            (_result.CompareLine.Length > 0 ? " " + _result.CompareLine : "") +
                            (_result.Golds > 0 ? " " + _result.Golds + " gold(s)." : "") +
                            (_result.LoadLine.Length > 0 ? " " + _result.LoadLine : "") +
                            (outcome != null ? " Run mode attempt " + outcome.Attempt + " (" + outcome.AttemptId + ")." : ""));
        }

        // --- Tick ---------------------------------------------------------------------

        private void RefreshResults()
        {
            if (!_resultsOpen) return;
            RefreshResultsRunMode();
            if (Time.unscaledTime < _nextResCheck) return;
            _nextResCheck = Time.unscaledTime + 0.1f;
            // The cursor is the runner's: the window, or the pause menu.
            MainWindowModule main = Host != null ? Host.Find<MainWindowModule>() : null;
            _resClickable = (main != null && main.PanelOpen) || MenuClose.PauseMenuOpen();
        }

        // The receipt line follows the outbox (waiting, sent, refused).
        private void RefreshResultsRunMode()
        {
            if (_resOutcome == null) { _resRunMode.text = ""; return; }
            int version = _upload != null ? _upload.AttemptWordsVersion : 0;
            if (version == _resUploadVersion) return;
            _resUploadVersion = version;
            string words = _upload != null ? _upload.AttemptUploadWords(_resOutcome.AttemptId) : "";
            _resLink = _upload != null && _resOutcome.SendOn && _resOutcome.AttemptId.Length > 0 ? _upload.AttemptLink(_resOutcome.AttemptId) : "";
            _resRunMode.text = string.Join("\n", RunResults.RunModeLines(_resOutcome, words, _resLink).ToArray());
        }

        // --- drawing ---------------------------------------------------------------

        private void EnsureResultStyles()
        {
            if (_resHeadStyle != null) return;
            _resHeadStyle = new GUIStyle(GUI.skin.label);
            _resHeadStyle.fontSize = 26;
            _resHeadStyle.fontStyle = FontStyle.Bold;
            _resHeadStyle.alignment = TextAnchor.MiddleLeft;
            _resHeadStyle.wordWrap = false;
            _resHeadStyle.clipping = TextClipping.Clip;
            _resHeadGold = new GUIStyle(_resHeadStyle);
            _resHeadGold.normal.textColor = new Color(1f, 0.8f, 0.2f);
            _resGoldWrap = new GUIStyle(UiText.Plain);
            _resGoldWrap.normal.textColor = new Color(1f, 0.8f, 0.2f);
        }

        private float ResColW { get { return 52f + 6f * Mathf.Max(_timeDecimals.Value, _deltaDecimals.Value); } }

        /// Lays the panel out at (x, y), `w` wide, and returns its height;
        /// draws only when `draw` (the first pass measures for the background).
        private float ResultsBody(float x, float y, float w, bool draw, bool overWindow)
        {
            float y0 = y;
            if (draw) GUI.Label(new Rect(x, y, w, ResRowH), _resTitle, _titleStyle);
            y += ResRowH + 2f;
            if (draw) GUI.Label(new Rect(x, y, w, 34f), _resHeadline, _result.NewPb ? _resHeadGold : _resHeadStyle);
            y += 36f;

            GUIStyle verdictStyle = _result.NewPb ? _resGoldWrap : UiText.Plain;
            y += draw ? UiText.Draw(x, y, w, _resVerdict, verdictStyle) : UiText.Height(w, _resVerdict, verdictStyle);
            y += draw ? UiText.Draw(x, y, w, _resCompare) : UiText.Height(w, _resCompare, UiText.Plain);
            y += draw ? UiText.Draw(x, y, w, _resGolds) : UiText.Height(w, _resGolds, UiText.Plain);
            if (_resLoads.text.Length > 0)
                y += draw ? UiText.Draw(x, y, w, _resLoads) : UiText.Height(w, _resLoads, UiText.Plain);
            y += 4f;

            // The splits: name, time, delta, segment, saved / lost.
            float colW = ResColW, saveW = colW + 34f;
            float nameW = Mathf.Max(60f, w - 3f * colW - saveW);
            if (draw)
            {
                GUI.Label(new Rect(x, y, nameW, ResRowH), ResColTitles[0], _titleStyle);
                float cx = x + nameW;
                for (int c = 1; c < ResCols; c++)
                {
                    float cw = c == 4 ? saveW : colW;
                    GUI.Label(new Rect(cx, y, cw, ResRowH), ResColTitles[c], _cellStyle);
                    cx += cw;
                }
            }
            y += ResRowH;
            int visible = Mathf.Min(_resRows, ResMaxRows);
            for (int k = 0; k < visible; k++)
            {
                // The first rows, the end always last.
                int r = _resRows > ResMaxRows && k == visible - 1 ? _resRows - 1 : k;
                if (draw)
                {
                    GUI.Label(new Rect(x, y, nameW, ResRowH), _resCells[r * ResCols], _resGold[r] ? GoldName : _nameStyle);
                    float cx = x + nameW;
                    for (int c = 1; c < ResCols; c++)
                    {
                        float cw = c == 4 ? saveW : colW;
                        GUIStyle st = _cellStyle;
                        if (c == 2) st = _colourStyles[(int)_resDeltaColour[r]];
                        else if (c == 3 && _resGold[r]) st = _colourStyles[(int)SplitColour.Gold];
                        else if (c == 4 && _resSavedLost[r] != 0)
                            st = _colourStyles[_resSavedLost[r] < 0 ? (int)SplitColour.AheadGaining : (int)SplitColour.BehindLosing];
                        GUI.Label(new Rect(cx, y, cw, ResRowH), _resCells[r * ResCols + c], st);
                        cx += cw;
                    }
                }
                y += ResRowH;
            }
            y += draw ? UiText.DrawDim(x, y, w, _resMore) : UiText.Height(w, _resMore, UiText.Dim);
            y += 4f;

            for (int i = 0; i < _resLineLabels.Length; i++)
            {
                if (draw)
                {
                    GUI.Label(new Rect(x, y, w - 90f, ResRowH), _resLineLabels[i], _nameStyle);
                    // The whole row, right-aligned: a long value is cut only
                    // where it meets its label.
                    GUI.Label(new Rect(x, y, w, ResRowH), _resLineValues[i], _cellStyle);
                }
                y += ResRowH;
            }

            if (_resRunMode.text.Length > 0)
            {
                y += 4f;
                y += draw ? UiText.Draw(x, y, w, _resRunMode) : UiText.Height(w, _resRunMode, UiText.Plain);
            }

            y += 4f;
            if (_resClickable)
            {
                if (draw)
                {
                    bool before = GUI.enabled;
                    // A click meant for the F2 window over the panel is the window's.
                    GUI.enabled = before && !overWindow;
                    if (GUI.Button(new Rect(x, y, 110f, 24f), "Restart")) Restart();
                    if (GUI.Button(new Rect(x + 116f, y, 80f, 24f), "Close")) CloseResults();
                    if (_resLink.Length > 0 && GUI.Button(new Rect(x + 202f, y, 110f, 24f), "Copy link"))
                    {
                        GUIUtility.systemCopyBuffer = _resLink;
                        Ctx.Notice.Show("Copied the attempt's link.", 3f);
                    }
                    GUI.enabled = before;
                }
                y += 28f;
            }
            else
            {
                GUIContent hint = _resOutcome != null ? _resRunModeHint : _resHint;
                y += draw ? UiText.DrawDim(x, y, w, hint) : UiText.Height(w, hint, UiText.Dim);
            }
            return y - y0;
        }

        private GUIStyle _goldName;

        private GUIStyle GoldName
        {
            get
            {
                if (_goldName == null)
                {
                    _goldName = new GUIStyle(_nameStyle);
                    _goldName.normal.textColor = new Color(1f, 0.8f, 0.2f);
                }
                return _goldName;
            }
        }

        private void DrawResults()
        {
            if (!ResultsShowing) return;
            EnsureSplitStyles();
            EnsureResultStyles();

            float w = Mathf.Clamp(_resWidth.Value, 300f, Mathf.Max(300f, Screen.width - 16f));
            float inner = w - 16f;
            float h = ResultsBody(0f, 0f, inner, false, false) + 12f;
            float px = _resDragging ? _resDragX : _resX.Value;
            float py = _resDragging ? _resDragY : _resY.Value;
            float x = px < 0f && !_resDragging ? (Screen.width - w) * 0.5f : Mathf.Clamp(px, 0f, Mathf.Max(0f, Screen.width - w));
            float y = Mathf.Clamp(py, 0f, Mathf.Max(0f, Screen.height - h));
            Rect panel = new Rect(x, y, w, h);

            MainWindowModule main = Host != null ? Host.Find<MainWindowModule>() : null;
            Event e = Event.current;
            bool overWindow = main != null && e != null && main.ScreenRect.Contains(e.mousePosition);
            HandleResultsDrag(new Rect(x, y, w, ResRowH + 6f), overWindow);

            Color before = GUI.color;
            GUI.color = new Color(before.r, before.g, before.b, Mathf.Clamp01(Mathf.Max(Opacity, 0.85f)));
            GUI.Box(panel, GUIContent.none, _panelStyle);
            GUI.color = before;
            if (_resDragging) GUI.Box(panel, GUIContent.none, UiKit.Outline);
            ResultsBody(x + 8f, y + 6f, inner, true, overWindow);
        }

        private void HandleResultsDrag(Rect title, bool overWindow)
        {
            Event e = Event.current;
            if (e == null) return;
            if (!_resClickable)
            {
                if (_resDragging) EndResultsDrag();
                return;
            }
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button != 0 || overWindow || !title.Contains(e.mousePosition)) return;
                    _resDragging = true;
                    _resDragOffset = e.mousePosition - new Vector2(title.x, title.y);
                    _resDragX = title.x;
                    _resDragY = title.y;
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (!_resDragging) return;
                    _resDragX = Mathf.Max(0f, e.mousePosition.x - _resDragOffset.x);
                    _resDragY = Mathf.Max(0f, e.mousePosition.y - _resDragOffset.y);
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (!_resDragging) return;
                    EndResultsDrag();
                    e.Use();
                    break;
            }
        }

        // Written once, on release (a config write saves the whole file).
        private void EndResultsDrag()
        {
            _resDragging = false;
            float w = Mathf.Clamp(_resWidth.Value, 300f, Mathf.Max(300f, Screen.width - 16f));
            _resX.Value = Mathf.Clamp(_resDragX, 0f, Mathf.Max(0f, Screen.width - w));
            _resY.Value = Mathf.Max(0f, _resDragY);
            Ctx.Log.LogInfo("Results panel moved to (" + Mathf.RoundToInt(_resX.Value) + ", " + Mathf.RoundToInt(_resY.Value) + ").");
        }
    }
}
