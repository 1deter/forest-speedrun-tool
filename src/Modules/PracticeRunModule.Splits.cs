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
        private enum Col { Delta, SplitTime, SegmentTime, SegmentDelta, BestSegment, TimeSave, Lrt }
        private const int ColCount = 7;
        private static readonly string[] ColTitles = { "Delta", "Split", "Segment", "Seg +/-", "Best seg", "Save", "LRT" };
        private static readonly string[] ColOptions =
        {
            "Delta", "Split time", "Segment time", "Segment delta", "Best segment", "Possible time save",
            "Load-removed split time (LRT)"
        };

        private enum Line { Previous, SumOfBest, BestPossible, Pace, Save, Pb, Attempts, PbChance, Playtime }
        private const int LineCount = 9;
        private static readonly string[] LineOptions =
        {
            "Previous segment", "Sum of best", "Best possible time", "Current pace", "Possible time save", "Personal best", "Attempts",
            "PB chance", "Total playtime"
        };

        private ConfigEntry<bool> _splitsPanel;
        private ConfigEntry<float> _panelX, _panelY, _panelWidth, _panelOpacity;
        private ConfigEntry<int> _panelRows, _timeDecimals, _deltaDecimals;
        private readonly ConfigEntry<bool>[] _cols = new ConfigEntry<bool>[ColCount];
        private readonly ConfigEntry<bool>[] _lines2 = new ConfigEntry<bool>[LineCount];
        private ConfigEntry<string> _runnerName, _localRunnerId;

        private SplitStats _stats;
        private float[] _times = new float[0];
        // The timer's load seconds when each row was reached (NaN = not
        // reached / unknown): the LRT column, the attempt's SplitLoads.
        private float[] _loadsAt = new float[0];
        private SplitRow[] _rowsData = new SplitRow[0];
        private SplitSummary _summary;
        private bool _splitsDirty = true;
        private float _nextSplitText;

        // PB chance and total playtime (v0.24.204, Data/RunHistory). The
        // unfinished runs of the loaded segment (every route), the finished
        // ones' time on every route, and PB chance's pool, rebuilt on start,
        // split, finish and reset as LiveSplit's component is.
        private List<UnfinishedAttempt> _unfinished = new List<UnfinishedAttempt>();
        private float _playedFinished;
        private Attempt _recordedUnfinished;
        private bool _pbChanceDirty = true;
        private readonly List<HistoryRun> _history = new List<HistoryRun>();
        private readonly System.Random _pbRandom = new System.Random();
        private float[] _notStarted = new float[0];
        private long _playtimeShown = -1;

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
        private readonly GUIContent _panelSizeText = new GUIContent("");
        private readonly GUIContent _opacityText = new GUIContent("");
        private readonly GUIContent _precisionText = new GUIContent("");
        private readonly GUIContent _dragHint = new GUIContent(
            "Drag the splits panel with the mouse to move it (while this window is open).");
        private readonly GUIContent _noSteamHint = new GUIContent(
            "Steam was not found - type the name your times should carry.");

        // Dragging the panel: only while the window is open (the cursor is
        // free then). The position is kept here while dragging and written
        // to the config once, on release - a config write per mouse event
        // would rewrite the file dozens of times a second.
        private bool _dragging;
        private Vector2 _dragOffset;
        private float _dragX, _dragY;

        // The runner field: one text box, pre-filled with the Steam name
        // (author, 2026-09-27: one field, the name already typed). Filled
        // from Tick when the options open; null = not filled yet.
        private string _runnerEdit;
        private string _steamName;

        // A config write saves the whole file (86 ms measured, bridge
        // 2026-09-27): the slider and the name field would hitch on every
        // step / keystroke. They hold their value here and write once,
        // half a second after the last change (FlushSplitSettings, Tick).
        private float _opacityNow = -1f;       // < 0 = the config's value
        private string _pendingName;           // null = nothing to write
        private bool _opacityPending;
        private float _writeAt;

        private float Opacity { get { return _opacityNow >= 0f ? _opacityNow : _panelOpacity.Value; } }

        private void FlushSplitSettings()
        {
            if ((!_opacityPending && _pendingName == null) || Time.unscaledTime < _writeAt) return;
            if (_opacityPending) { _panelOpacity.Value = _opacityNow; _opacityPending = false; }
            if (_pendingName != null) { _runnerName.Value = _pendingName; _pendingName = null; }
        }
        private readonly GUIContent _splitsHint = new GUIContent("");
        // The table's texts by what they were made from (RefreshSplits).
        private readonly TextMemo _cellText = new TextMemo();
        private readonly TextMemo _lineText = new TextMemo();
        private string _titleFor, _titleVs, _previousFor;
        private bool _previousLive;
        private int _attemptsLineStarted = -1, _attemptsLineCount = -1;
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
            _timeDecimals = c.Bind("Splits", "TimeDecimals", 2, "Decimal places for split times (0-3). Attempts always save milliseconds.");
            _deltaDecimals = c.Bind("Splits", "DeltaDecimals", 2, "Decimal places for deltas (0-3).");
            _panelOpacity = c.Bind("Splits", "PanelOpacity", 0.82f, "Opacity of the panel's background, 0 (none) to 1 (solid). The text stays solid.");
            bool[] colDefaults = { true, true, false, false, false, false, false };
            for (int i = 0; i < ColCount; i++)
            {
                _cols[i] = c.Bind("Splits", "Column" + (Col)i, colDefaults[i], "Splits column: " + ColOptions[i] + ".");
                _colTitles[i] = new GUIContent(ColTitles[i]);
                _colOptionText[i] = new GUIContent(" " + ColOptions[i]);
            }
            // PB chance and total playtime are LiveSplit add-ons, not in its
            // default layout: off until ticked.
            bool[] lineDefaults = { true, true, true, false, false, false, true, false, false };
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
            string typed = _pendingName ?? (_runnerName.Value != null ? _runnerName.Value.Trim() : "");
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
            // The loads before each checkpoint (Data/LoadTimes): only when
            // there were any - none listed reads as none before any.
            if (done.Loads > 0 && done.Splits.Length > 0 && _loadsAt.Length == done.Splits.Length + 1)
            {
                float[] at = new float[done.Splits.Length];
                for (int i = 0; i < at.Length; i++) at[i] = float.IsNaN(_loadsAt[i]) ? 0f : _loadsAt[i];
                done.SplitLoads = at;
            }
            done.RunnerId = RunnerIdNow();
            done.RunnerName = RunnerNameNow();
            Vector3 plane;
            float yaw;
            if (PlaneSite.TryRead(Ctx.Log, out plane, out yaw)) { done.HasPlane = true; done.Plane = plane; done.PlaneYaw = yaw; }
        }

        // --- state ------------------------------------------------------------

        private int SplitRows { get { return _segment == null ? 0 : _segment.Checkpoints.Count + 1; } }

        // Which segment + route the stats were built for.
        private string _statsKey = "";

        /// On arming (a restart, auto-restart, Go): the last run's times
        /// stay on screen until the next run's clock starts (author,
        /// 2026-09-27: "so you can skim through your times once you've
        /// finished a run"), as LiveSplit keeps them until a reset. Only a
        /// different segment or route clears them now.
        private void ArmSplits()
        {
            string key = _segment == null ? "" : _segment.Id + "|" + _armedRoute + "|" + SplitRows;
            if (_stats == null || key != _statsKey) ResetSplits();
            else _splitsDirty = true;
        }

        /// When a run starts (and on a new segment): stats from every
        /// attempt so far - golds are judged against the ones before this
        /// run - and no times yet.
        private void ResetSplits()
        {
            _statsKey = _segment == null ? "" : _segment.Id + "|" + _armedRoute + "|" + SplitRows;
            int rows = SplitRows;
            _stats = rows > 0 ? SplitStats.Build(_attempts, rows - 1) : null;
            ApplyPracticeGolds(_stats);   // golds from checkpoint practice
            if (_times.Length != rows) _times = new float[rows];
            for (int i = 0; i < rows; i++) _times[i] = float.NaN;
            if (_loadsAt.Length != rows) _loadsAt = new float[rows];
            for (int i = 0; i < rows; i++) _loadsAt[i] = float.NaN;
            if (_rowsData.Length != rows) _rowsData = new SplitRow[rows];
            _splitsDirty = true;
            _pbChanceDirty = true;
            CloseResults();   // the next run started, or another segment / route
        }

        private void RecordSplit(int row, float t)
        {
            if (_runMode != null) _runMode.TimerSplit(row, t);
            if (row >= 0 && row < _times.Length) _times[row] = t;
            if (row >= 0 && row < _loadsAt.Length) _loadsAt[row] = _recorder.LoadClock.LoadTime;
            _splitsDirty = true;
            _pbChanceDirty = true;
        }

        private void FinishSplits(Attempt done)
        {
            if (_times.Length > 0) _times[_times.Length - 1] = done.Duration;
            if (_loadsAt.Length > 0) _loadsAt[_loadsAt.Length - 1] = done.LoadTime;
            _splitsDirty = true;
            _pbChanceDirty = true;
        }

        // --- unfinished runs, PB chance, playtime ------------------------------

        /// A running attempt about to be dropped (abort, restart, death,
        /// another spot, practice off, the title screen): kept as an
        /// unfinished one - LiveSplit's reset - for PB chance and playtime.
        /// Once per attempt, whichever path gets there first.
        private void RecordUnfinished(string why)
        {
            Attempt cur = _recorder.Current;
            if (_recorder.State != RunRecorder.RunState.Running || cur == null || ReferenceEquals(cur, _recordedUnfinished)) return;
            if (Resumed) return;   // from a checkpoint: practice, not a run that started
            if (TasRun) return;    // a TAS replay's run is not the runner's
            _recordedUnfinished = cur;

            UnfinishedAttempt u = new UnfinishedAttempt();
            u.StartedUtc = cur.RecordedUtc;
            u.Route = cur.Route ?? "";
            u.Duration = _recorder.Elapsed;
            u.Splits = _splits.ToArray();
            _store.AddUnfinished(cur.AnchorLabel, u);
            if (cur.AnchorLabel == _loadedSegmentId) _unfinished.Add(u);
            _pbChanceDirty = true;
            _splitsDirty = true;
            Ctx.Log.LogInfo("Run '" + cur.AnchorLabel + "': unfinished after " + Format(u.Duration) + " (" + why + ", " +
                            u.Splits.Length + " split(s) reached) - kept for PB chance and playtime.");
        }

        /// On loading a segment's attempts: its unfinished runs and the
        /// time of this runner's finished ones, every route (time played is
        /// time played, whatever the zones were then).
        private void LoadPlaytime(string id, List<Attempt> all, string own)
        {
            _unfinished = _store.LoadUnfinished(id);
            _playedFinished = 0f;
            for (int i = 0; i < all.Count; i++)
                if (all[i].Completed && AttemptOwners.IsOwn(all[i].RunnerId, own)) _playedFinished += all[i].Duration;
            _pbChanceDirty = true;
            _playtimeShown = -1;
        }

        /// PB chance's attempts on the current route, oldest first.
        private void BuildHistory(int rows)
        {
            _history.Clear();
            for (int i = 0; i < _attempts.Count; i++)
            {
                if (!_attempts[i].Completed) continue;
                HistoryRun h;
                h.StartedUtc = _attempts[i].RecordedUtc;
                h.Times = SplitStats.SplitsOf(_attempts[i], rows);
                h.Finished = true;
                _history.Add(h);
            }
            for (int i = 0; i < _unfinished.Count; i++)
            {
                UnfinishedAttempt u = _unfinished[i];
                if (u.Route.Length > 0 && u.Route != _armedRoute) continue;
                HistoryRun h;
                h.StartedUtc = u.StartedUtc;
                h.Times = SplitStats.Filled(rows);
                for (int k = 0; k < u.Splits.Length && k < rows - 1; k++) h.Times[k] = u.Splits[k];
                h.Finished = false;
                _history.Add(h);
            }
            _history.Sort(ByStart);
        }

        private static int ByStart(HistoryRun a, HistoryRun b) { return a.StartedUtc.CompareTo(b.StartedUtc); }

        private void RefreshHistoryLines(int rows, bool running)
        {
            if (_lines2[(int)Line.PbChance].Value && _pbChanceDirty)
            {
                _pbChanceDirty = false;
                BuildHistory(rows);
                // Between runs a run that did not finish shows from the
                // start, as LiveSplit's does after a reset.
                bool finished = rows > 0 && !float.IsNaN(_times[rows - 1]);
                float[] times = _times;
                if (!running && !finished)
                {
                    if (_notStarted.Length != rows) _notStarted = SplitStats.Filled(rows);
                    times = _notStarted;
                }
                _lineValues[(int)Line.PbChance].text = PbChance.Compute(_history, times, _stats.Pb, _pbRandom);
            }
            if (_lines2[(int)Line.Playtime].Value)
            {
                float total = Playtime.Total(null, _unfinished, (running ? _recorder.Elapsed : 0f) + _playedFinished);
                long whole = (long)total;
                if (whole != _playtimeShown)
                {
                    _playtimeShown = whole;
                    _lineValues[(int)Line.Playtime].text = Playtime.Format(total);
                }
            }
        }

        private float[] ComparisonSplits()
        {
            if (_stats == null) return null;
            switch (_referenceKind)
            {
                case Reference.Last: return _stats.LastSplits;
                case Reference.Average: return _stats.AverageSplits;
                case Reference.BestSegments: return _stats.BestSegmentSplits;
                case Reference.Runner: return PickedRunner != null ? PickedRunner.Splits : _stats.PbSplits;
                case Reference.LiveSplit: return LssSplits() ?? _stats.PbSplits;
                default: return _stats.PbSplits;
            }
        }

        private string ComparisonName()
        {
            switch (_referenceKind)
            {
                case Reference.Runner: return PickedRunner != null ? PickedRunner.Name : "Personal best";
                case Reference.Last: return "Last run";
                case Reference.Average: return "Average";
                case Reference.BestSegments: return "Best segments";
                case Reference.LiveSplit: return LssComparisonName();
                default: return "Personal best";
            }
        }

        private void CycleComparison()
        {
            // Your own four, then each other runner on the website's board,
            // then each comparison in the linked LiveSplit file.
            if (_referenceKind == Reference.Runner)
            {
                if (_runnerPick + 1 < _others.Count) PickRunner(_runnerPick + 1);
                else if (LssAvailable) PickLss(0);
                else { _referenceKind = Reference.Best; SelectReference(); }
            }
            else if (_referenceKind == Reference.LiveSplit)
            {
                if (_lssPick + 1 < _lssCompareSplits.Count) PickLss(_lssPick + 1);
                else { _referenceKind = Reference.Best; SelectReference(); }
            }
            else if (_referenceKind == Reference.BestSegments && _others.Count > 0) PickRunner(0);
            else if (_referenceKind == Reference.BestSegments && LssAvailable) PickLss(0);
            else { _referenceKind = (Reference)(((int)_referenceKind + 1) % 4); SelectReference(); }
            _splitsDirty = true;
            if (!TabShowing) Ctx.Notice.Show("Compare to: " + ComparisonName(), 1.5f);
        }

        // --- text (Tick) ---------------------------------------------------------

        private void RefreshSplits()
        {
            FlushSplitSettings();
            bool running = _recorder.State == RunRecorder.RunState.Running;
            if (!_splitsDirty && !(running && Time.unscaledTime >= _nextSplitText)) return;
            if (!_splitsDirty && !TabShowing && !PanelShowing) return;
            _splitsDirty = false;
            _nextSplitText = Time.unscaledTime + 0.1f;

            if (TabShowing && _splitsOptionsOpen)
            {
                if (_runnerEdit == null)
                {
                    _steamName = RunnerIdentity.SteamName();
                    string typed = _runnerName.Value != null ? _runnerName.Value.Trim() : "";
                    _runnerEdit = typed.Length > 0 ? typed : (_steamName ?? "");
                }
                _panelSizeText.text = "width " + Mathf.RoundToInt(_panelWidth.Value) + " px, " + _panelRows.Value + " rows";
                _opacityText.text = Mathf.RoundToInt(Opacity * 100f) + "%";
                _precisionText.text = "times " + _timeDecimals.Value + ", deltas " + _deltaDecimals.Value + " decimal place(s)";
            }

            int rows = SplitRows;
            float[] compare = ComparisonSplits();
            if (_segment == null || _stats == null || compare == null || rows != _times.Length || rows != _stats.Rows)
            {
                _shownRows = 0;
                _compareTitle.text = "";
                _titleFor = null;   // set again when the table comes back
                _splitsHint.text = _segment == null ? "" : "Splits appear once the segment is armed.";
                return;
            }

            _summary = SplitTable.Fill(_stats, compare, _times, running, _recorder.Elapsed, _rowsData);
            string compareName = ComparisonName();
            if (!ReferenceEquals(_segment.Name, _titleFor) || !string.Equals(compareName, _titleVs))
            {
                _titleFor = _segment.Name;
                _titleVs = compareName;
                _compareTitle.text = _segment.Name + "  -  vs " + compareName;
            }
            _splitsHint.text = _stats.Completed == 0 ? "No finished attempts yet: the columns fill in as you run it."
                             : _stats.WithSplits == 0 && rows > 1 ? "Your earlier times were recorded before split times were saved - only their totals show."
                             : "";

            if (_rowColours.Length != rows) { _rowColours = new SplitColour[rows]; _rowCurrent = new bool[rows]; }
            while (_rowNames.Count < rows) _rowNames.Add(new GUIContent(""));
            while (_cells.Count < rows * ColCount) _cells.Add(new GUIContent(""));

            // While a run is on this is ten times a second: each cell's text
            // comes from the memo, formatted only when its value moved.
            for (int r = 0; r < rows; r++)
            {
                SplitRow d = _rowsData[r];
                string rowName = _segment.SplitName(r);
                if (!string.Equals(_rowNames[r].text, rowName)) _rowNames[r].text = rowName;
                _rowColours[r] = d.Colour;
                _rowCurrent[r] = d.Current;
                bool reached = !float.IsNaN(d.Time);
                // A row not reached shows the comparison's time in its split
                // and segment columns, as LiveSplit does.
                int td = _timeDecimals.Value, dd = _deltaDecimals.Value;
                int c0 = r * ColCount;
                _cells[c0 + (int)Col.Delta].text = _cellText.Delta(c0 + (int)Col.Delta, d.Delta, dd);
                _cells[c0 + (int)Col.SplitTime].text = _cellText.Time(c0 + (int)Col.SplitTime, reached ? d.Time : d.Compare, td);
                _cells[c0 + (int)Col.SegmentTime].text = _cellText.Time(c0 + (int)Col.SegmentTime, reached ? d.Segment : d.CompareSegment, td);
                _cells[c0 + (int)Col.SegmentDelta].text = _cellText.Delta(c0 + (int)Col.SegmentDelta, d.SegmentDelta, dd);
                _cells[c0 + (int)Col.BestSegment].text = _cellText.Time(c0 + (int)Col.BestSegment, d.BestSegment, td);
                _cells[c0 + (int)Col.TimeSave].text = _cellText.TimeOrEmpty(c0 + (int)Col.TimeSave, d.TimeSave, td);
                // This run's split time with its loads taken out; blank until reached.
                float lrt = reached && r < _loadsAt.Length ? LoadClock.Without(d.Time, _loadsAt[r]) : float.NaN;
                _cells[c0 + (int)Col.Lrt].text = _cellText.TimeOrEmpty(c0 + (int)Col.Lrt, lrt, td);
            }
            _shownRows = rows;

            int tdl = _timeDecimals.Value, ddl = _deltaDecimals.Value;
            string previous = _lineText.Delta(0, _summary.PreviousSegment, ddl);
            if (!ReferenceEquals(previous, _previousFor) || _summary.PreviousLive != _previousLive)
            {
                _previousFor = previous;
                _previousLive = _summary.PreviousLive;
                _lineValues[(int)Line.Previous].text = previous + (_summary.PreviousLive ? " (live)" : "");
            }
            _lineLabels[(int)Line.Previous].text = _summary.PreviousLive ? "Live segment" : "Previous segment";
            _lineValues[(int)Line.SumOfBest].text = _lineText.Time(1, _summary.SumOfBest, tdl);
            _lineValues[(int)Line.BestPossible].text = _lineText.Time(2, _summary.BestPossible, tdl);
            _lineValues[(int)Line.Pace].text = _lineText.Time(3, _summary.CurrentPace, tdl);
            _lineValues[(int)Line.Save].text = _lineText.Time(4, _summary.PossibleSave, tdl);
            _lineValues[(int)Line.Pb].text = _lineText.Time(5, _summary.Pb, tdl);
            // Started runs, as LiveSplit counts them (failed ones too); the
            // finished ones in brackets when they differ.
            int started = Mathf.Max(_started, _attempts.Count);
            if (started != _attemptsLineStarted || _attempts.Count != _attemptsLineCount)
            {
                _attemptsLineStarted = started;
                _attemptsLineCount = _attempts.Count;
                _lineValues[(int)Line.Attempts].text = started > _attempts.Count
                    ? started + " (" + _attempts.Count + " finished)" : _attempts.Count.ToString();
            }
            RefreshHistoryLines(rows, running);
        }

        // --- drawing ---------------------------------------------------------------

        private bool PanelShowing
        {
            get { return _splitsPanel != null && _splitsPanel.Value && Timing && _segment != null && _shownRows > 0; }
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

            // The kit's rounded dark card: the same look as the results panel
            // and the HUD widgets (docs/ui-redesign.md).
            _panelStyle = new GUIStyle(UiKit.WidgetCard);
        }

        public override void DrawScreen()
        {
            DrawReplayLabels();
            DrawResults();
            // The results panel has the splits in it and used to be drawn
            // over this panel (author, 2026-10-05): this one steps aside
            // until the next run starts.
            if (ResultsShowing || !PanelShowing) return;
            EnsureSplitStyles();

            float w = Mathf.Clamp(_panelWidth.Value, 160f, Screen.width);
            float h = PanelHeight(w);
            float px = _dragging ? _dragX : _panelX.Value;
            float py = _dragging ? _dragY : _panelY.Value;
            // PanelX < 0 means "against the right edge" - but only as a
            // saved setting: a drag past the left edge must stop at 0, not
            // read as that and jump right (author, v0.24.151, windowed).
            float x = px < 0f && !_dragging ? Screen.width - w - 8f : Mathf.Clamp(px, 0f, Mathf.Max(0f, Screen.width - w));
            float y = Mathf.Clamp(py, 0f, Mathf.Max(0f, Screen.height - h));
            Rect panel = new Rect(x, y, w, h);

            HandleDrag(panel);

            // Opacity on the background only: the text stays readable.
            Color before = GUI.color;
            GUI.color = new Color(before.r, before.g, before.b, Mathf.Clamp01(Opacity));
            GUI.Box(panel, GUIContent.none, _panelStyle);
            GUI.color = before;
            if (_dragging) GUI.Box(panel, GUIContent.none, UiKit.Outline);   // an outline while moving
            DrawSplitsTable(x + 6f, y + 4f, w - 12f, _panelRows.Value, true);
        }

        private void HandleDrag(Rect panel)
        {
            Event e = Event.current;
            if (e == null) return;
            MainWindowModule main = Host != null ? Host.Find<MainWindowModule>() : null;
            bool windowOpen = main != null && main.PanelOpen;

            if (!windowOpen)
            {
                if (_dragging) EndDrag();
                return;
            }

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button != 0 || !panel.Contains(e.mousePosition) || main.ScreenRect.Contains(e.mousePosition)) return;
                    _dragging = true;
                    _dragOffset = e.mousePosition - new Vector2(panel.x, panel.y);
                    _dragX = panel.x;
                    _dragY = panel.y;
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (!_dragging) return;
                    _dragX = Mathf.Max(0f, e.mousePosition.x - _dragOffset.x);
                    _dragY = Mathf.Max(0f, e.mousePosition.y - _dragOffset.y);
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (!_dragging) return;
                    EndDrag();
                    e.Use();
                    break;
            }
        }

        private void EndDrag()
        {
            _dragging = false;
            float w = Mathf.Clamp(_panelWidth.Value, 160f, Screen.width);
            _panelX.Value = Mathf.Clamp(_dragX, 0f, Mathf.Max(0f, Screen.width - w));
            _panelY.Value = Mathf.Max(0f, _dragY);
            Ctx.Log.LogInfo("Splits panel moved to (" + Mathf.RoundToInt(_panelX.Value) + ", " + Mathf.RoundToInt(_panelY.Value) + ").");
        }

        private int VisibleRows(int maxRows)
        {
            return maxRows <= 1 || _shownRows <= maxRows ? _shownRows : maxRows;
        }

        private float PanelHeight(float w)
        {
            int lines = 0;
            for (int i = 0; i < LineCount; i++) if (_lines2[i].Value) lines++;
            int cols = 0;
            for (int c = 0; c < ColCount; c++) if (_cols[c].Value) cols++;
            return 8f + 18f + (cols > 0 ? 18f : 0f) + VisibleRows(_panelRows.Value) * 18f + (lines > 0 ? 4f + lines * 18f : 0f);
        }

        /// The table at (x, y), `w` wide; returns its height. `header` adds
        /// the column titles (the tab has room for them).
        private float DrawSplitsTable(float x, float y, float w, int maxRows, bool header)
        {
            const float rowH = 18f;
            float colW = 50f + 6f * Mathf.Max(_timeDecimals.Value, _deltaDecimals.Value);   // 62 at 2 decimals
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
                // The whole row, right-aligned: a long value ("100% (Congrats!)")
                // is cut only when it meets its label, not at a fixed column.
                GUI.Label(new Rect(x, y, w, rowH), _lineValues[i], st);
                y += rowH;
            }
            return y - y0;
        }

        /// The Runs tab's splits section: the table, then its options.
        private static readonly GUIContent SplitsOptionsText = new GUIContent("Splits panel and table options");

        private float DrawSplitsSection(float y, float w)
        {
            EnsureSplitStyles();
            if (_shownRows > 0) y += DrawSplitsTable(0f, y, w, 0, true) + 4f;
            y += UiText.Draw(0, y, w, _splitsHint);

            bool optionsOpen = UiKit.Section(0f, ref y, w, "runs.splitsopts", SplitsOptionsText, null, null, false);
            if (optionsOpen != _splitsOptionsOpen)
            {
                _splitsOptionsOpen = optionsOpen;
                _runnerEdit = null;    // re-read the Steam name and the setting
                _splitsDirty = true;   // the runner and size lines are built on a refresh
            }
            if (!_splitsOptionsOpen) return y;

            bool panel = GUI.Toggle(new Rect(0, y, w, 20), _splitsPanel.Value, " Show the splits panel on screen (F5 hides all overlay UI)");
            if (panel != _splitsPanel.Value) _splitsPanel.Value = panel;
            y += 22f;
            bool results = GUI.Toggle(new Rect(0, y, w, 20), _resultsCfg.Value, " Show a results panel when a run finishes (drag its title while ESC / F2 shows the cursor)");
            if (results != _resultsCfg.Value) { _resultsCfg.Value = results; if (!results) CloseResults(); }
            y += 22f;

            y = FlowToggles(y, w, "Columns:", _cols, _colOptionText);
            y = FlowToggles(y, w, "Lines:", _lines2, _lineOptionText);

            y += UiText.Draw(0, y, w, _dragHint);
            GUI.Label(new Rect(0, y, 110, 20), "Panel position");
            if (GUI.Button(new Rect(114, y - 1, 130, 22), "Reset to top right")) { _panelX.Value = -1f; _panelY.Value = 140f; }
            y += 26f;
            GUI.Label(new Rect(0, y, 110, 20), "Background");
            float sliderW = Mathf.Max(80f, w - 184f);
            float op = GUI.HorizontalSlider(new Rect(114, y + 5, sliderW, 16), Opacity, 0f, 1f);
            if (Mathf.Abs(op - Opacity) > 0.004f)
            {
                _opacityNow = Mathf.Round(op * 100f) / 100f;
                _opacityPending = true;
                _writeAt = Time.unscaledTime + 0.5f;
                _splitsDirty = true;
            }
            GUI.Label(new Rect(120f + sliderW, y, 60, 20), _opacityText);
            y += 26f;
            GUI.Label(new Rect(0, y, 110, 20), "Precision");
            if (GUI.Button(new Rect(114, y - 1, 30, 22), "-")) { _timeDecimals.Value = Mathf.Max(0, _timeDecimals.Value - 1); _splitsDirty = true; }
            if (GUI.Button(new Rect(148, y - 1, 30, 22), "+")) { _timeDecimals.Value = Mathf.Min(3, _timeDecimals.Value + 1); _splitsDirty = true; }
            if (GUI.Button(new Rect(190, y - 1, 30, 22), "-")) { _deltaDecimals.Value = Mathf.Max(0, _deltaDecimals.Value - 1); _splitsDirty = true; }
            if (GUI.Button(new Rect(224, y - 1, 30, 22), "+")) { _deltaDecimals.Value = Mathf.Min(3, _deltaDecimals.Value + 1); _splitsDirty = true; }
            GUI.Label(new Rect(262, y, Mathf.Max(60f, w - 262f), 20), _precisionText);
            y += 26f;
            GUI.Label(new Rect(0, y, 110, 20), "Width / rows");
            if (GUI.Button(new Rect(114, y - 1, 30, 22), "-")) { _panelWidth.Value = Mathf.Max(160f, _panelWidth.Value - 20f); _splitsDirty = true; }
            if (GUI.Button(new Rect(148, y - 1, 30, 22), "+")) { _panelWidth.Value = Mathf.Min(900f, _panelWidth.Value + 20f); _splitsDirty = true; }
            if (GUI.Button(new Rect(190, y - 1, 30, 22), "-")) { _panelRows.Value = Mathf.Max(3, _panelRows.Value - 1); _splitsDirty = true; }
            if (GUI.Button(new Rect(224, y - 1, 30, 22), "+")) { _panelRows.Value = Mathf.Min(40, _panelRows.Value + 1); _splitsDirty = true; }
            GUI.Label(new Rect(262, y, Mathf.Max(60f, w - 262f), 20), _panelSizeText);
            y += 26f;

            GUI.Label(new Rect(0, y, 110, 20), "Runner name");
            if (_runnerEdit != null)
            {
                string typed = GUI.TextField(new Rect(114, y - 2, Mathf.Max(80f, w - 124f), 22), _runnerEdit);
                if (typed != _runnerEdit)
                {
                    _runnerEdit = typed;
                    // The Steam name (or nothing) is stored as "", so the
                    // name follows a Steam rename; anything else is kept.
                    string t = typed.Trim();
                    _pendingName = t.Length == 0 || t == _steamName ? "" : t;
                    _writeAt = Time.unscaledTime + 0.5f;
                }
            }
            y += 26f;
            if (_steamName == null && _runnerEdit != null && _runnerEdit.Trim().Length == 0) y += UiText.Draw(0, y, w, _noSteamHint);
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
                if (v != entries[i].Value) { entries[i].Value = v; _splitsDirty = true; _playtimeShown = -1; }
                x += tw;
            }
            return y + 24f;
        }
    }
}
