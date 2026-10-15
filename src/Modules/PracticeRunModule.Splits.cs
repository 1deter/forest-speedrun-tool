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

        private ConfigEntry<bool> _splitsPanel, _timerInPanel;
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
        private int _opacityShown = -1, _sizeShownW = -1, _sizeShownRows = -1;
        private readonly GUIContent _noSteamHint = new GUIContent(
            "Steam was not found - type the name your times should carry.");

        // Dragging the panel: only in Edit HUD, T-0317 (the cursor is
        // free then). The position is kept here while dragging and written
        // to the config once, on release - a config write per mouse event
        // would rewrite the file dozens of times a second.
        private bool _dragging;
        private Vector2 _dragOffset;
        private float _dragX, _dragY;

        // Resizing in Edit HUD mode (HandleResize): 1 right edge, 2 rows,
        // 4 left edge, 3 / 6 a corner.
        // The live size is kept here and written once, on release.
        private int _resizeMode;
        private Vector2 _resizeStart;
        private float _resizeStartW, _resizeStartX, _resizeX, _resizeY, _liveW;
        private bool _resizeDocked;
        private int _resizeStartRows, _liveRows;

        // The runner field: one text box, pre-filled with the Steam name
        // (author, 2026-09-27: one field, the name already typed). Filled
        // from Tick when the field is first drawn; null = not filled yet.
        private string _runnerEdit;
        private string _steamName;
        private bool _runnerFill;

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
        // The table's texts by what they were made from (RefreshSplits).
        private readonly TextMemo _cellText = new TextMemo();
        private readonly TextMemo _lineText = new TextMemo();
        private string _titleFor, _titleVs, _previousFor;
        private int _titleFrom = -1;
        private bool _previousLive;
        private int _attemptsLineStarted = -1, _attemptsLineCount = -1;
        private int _shownRows;

        private GUIStyle _cellStyle, _nameStyle, _titleStyle, _placeholderStyle;
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
            _timerInPanel = c.Bind("Splits", "TimerInPanel", true,
                "The run timer inside the splits panel, as in LiveSplit (false: on its own - the HUD's Run timer value).");
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
            RegisterSplitsProfile();
            _runnerName = c.Bind("Runs", "RunnerName", "", "Your name on recorded attempts. Empty = your Steam name.");
            _localRunnerId = c.Bind("Runs", "LocalRunnerId", "", "Runner id used when Steam is not available (made once).");
        }

        // Everything Edit HUD sets up for the panel belongs to the HUD profile
        // (T-0019, author 2026-10-11): its switch, place, size, columns, lines,
        // decimals and background. The cached ones rebuild when a profile
        // switches them.
        private void RegisterSplitsProfile()
        {
            HudWidgets profiles = Host.Hud.Widgets;
            profiles.RegisterProfileEntry(_splitsPanel, null);
            profiles.RegisterProfileEntry(_timerInPanel, SplitsOptionApplier(_timerInPanel));
            profiles.RegisterProfileEntry(_panelX, null, ProfileUnit.X);
            profiles.RegisterProfileEntry(_panelY, null, ProfileUnit.Y);
            profiles.RegisterProfileEntry(_panelWidth, null, ProfileUnit.Size);
            profiles.RegisterProfileEntry(_panelRows, null);
            for (int i = 0; i < ColCount; i++) profiles.RegisterProfileEntry(_cols[i], SplitsOptionApplier(_cols[i]));
            for (int i = 0; i < LineCount; i++) profiles.RegisterProfileEntry(_lines2[i], SplitsOptionApplier(_lines2[i]));
            profiles.RegisterProfileEntry(_timeDecimals, SplitsOptionApplier(_timeDecimals));
            profiles.RegisterProfileEntry(_deltaDecimals, SplitsOptionApplier(_deltaDecimals));
            profiles.RegisterProfileEntry(_panelOpacity, delegate (object v)
            {
                _opacityPending = false;
                _opacityNow = -1f;
                _panelOpacity.Value = (float)v;
            });
        }

        private System.Action<object> SplitsOptionApplier(ConfigEntryBase entry)
        {
            return delegate (object v)
            {
                entry.BoxedValue = v;
                _splitsDirty = true;
                _playtimeShown = -1;
            };
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
            done.ModApi = Ctx.Practice != null && Ctx.Practice.ModApi;
            done.Mods = Ctx.Practice != null ? Ctx.Practice.Mods : "";
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

        /// The Edit HUD texts (background %, the size while resizing) and
        /// the runner field, built only when their value moved.
        private void RefreshEditTexts()
        {
            if (_runnerFill)
            {
                _runnerFill = false;
                _steamName = RunnerIdentity.SteamName();
                string typed = _runnerName.Value != null ? _runnerName.Value.Trim() : "";
                _runnerEdit = typed.Length > 0 ? typed : (_steamName ?? "");
            }
            if (!HudEditing) return;
            int op = Mathf.RoundToInt(Opacity * 100f);
            if (op != _opacityShown) { _opacityShown = op; _opacityText.text = op + "%"; }
            int pw = Mathf.RoundToInt(PanelWidthNow), pr = PanelRowsNow;
            if (pw != _sizeShownW || pr != _sizeShownRows)
            {
                _sizeShownW = pw;
                _sizeShownRows = pr;
                _panelSizeText.text = pw + " px wide, up to " + pr + " rows";
            }
        }

        private void RefreshSplits()
        {
            FlushSplitSettings();
            RefreshEditTexts();
            bool running = _recorder.State == RunRecorder.RunState.Running;
            if (!_splitsDirty && !(running && Time.unscaledTime >= _nextSplitText)) return;
            if (!_splitsDirty && !HudEditing && !PanelShowing) return;
            _splitsDirty = false;
            _nextSplitText = Time.unscaledTime + 0.1f;

            int rows = SplitRows;
            float[] compare = ComparisonSplits();
            if (_segment == null || _stats == null || compare == null || rows != _times.Length || rows != _stats.Rows)
            {
                _shownRows = 0;
                _compareTitle.text = "";
                _titleFor = null;   // set again when the table comes back
                return;
            }

            _summary = SplitTable.Fill(_stats, compare, _times, running, _recorder.Elapsed, _rowsData);
            string compareName = ComparisonName();
            // A run resumed from a checkpoint says so here (it was appended
            // to the HUD's clock before T-0336).
            int from = running && Resumed ? _resumedFrom : -1;
            if (!ReferenceEquals(_segment.Name, _titleFor) || !string.Equals(compareName, _titleVs) || from != _titleFrom)
            {
                _titleFor = _segment.Name;
                _titleVs = compareName;
                _titleFrom = from;
                _compareTitle.text = _segment.Name + "  -  vs " + compareName + (from >= 0 ? "  -  from checkpoint " + (from + 1) : "");
            }
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
            _timerColour = SplitTable.TimerColour(_rowsData, _times, running, _recorder.Elapsed);

            int tdl = _timeDecimals.Value, ddl = _deltaDecimals.Value;
            string previous = _lineText.Delta(0, _summary.PreviousSegment, ddl);
            if (!ReferenceEquals(previous, _previousFor) || _summary.PreviousLive != _previousLive)
            {
                _previousFor = previous;
                _previousLive = _summary.PreviousLive;
                // The label says "Live segment"; the number carries nothing (T-0336).
                _lineValues[(int)Line.Previous].text = previous;
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

        private const float RowH = 18f, TimerH = 34f;
        private const float MinPanelW = 160f, MaxPanelW = 900f;
        private const int MinPanelRows = 3, MaxPanelRows = 40;
        private const float EdgeGrab = 5f, CornerSize = 12f;

        // --- the timer (T-0336) ------------------------------------------------------------
        //
        // LiveSplit's Timer component, in the panel by default (author,
        // 2026-10-14: built around what LiveSplit users know) or popped out:
        // then it is the HUD's Run timer value, placed on its own. Drawn every
        // frame from TimerText's characters, one cached GUIContent each, so
        // the hundredths run smoothly and nothing allocates.

        /// The clock is drawn in the splits panel (else the HUD's Run timer shows it).
        private bool TimerInPanel
        {
            get { return _splitsPanel != null && _splitsPanel.Value && _timerInPanel.Value; }
        }

        private SplitColour _timerColour;
        private readonly char[] _timerChars = new char[TimerText.MaxLength];
        private static readonly GUIContent[] TimerGlyphs = MakeTimerGlyphs();
        private GUIStyle _timerStyle;
        private GUIStyle[] _timerColours;
        private float _timerMeasuredAt = -1f, _digitW, _colonW, _dotW;

        private static GUIContent[] MakeTimerGlyphs()
        {
            GUIContent[] g = new GUIContent[128];
            const string chars = "0123456789:.";
            for (int i = 0; i < chars.Length; i++) g[chars[i]] = new GUIContent(chars[i].ToString());
            return g;
        }

        /// What the timer shows: the running clock; between runs the time
        /// of the run whose splits the panel still shows (it finished), as
        /// LiveSplit keeps it until a reset; else 0.
        private float TimerSeconds(bool running)
        {
            if (running) return _recorder.Elapsed;
            int n = _times.Length;
            return n > 0 && !float.IsNaN(_times[n - 1]) ? _times[n - 1] : 0f;
        }

        /// The timer right-aligned in (x, y, w): digits in equal cells so
        /// they never shift while it runs.
        private void DrawTimer(float x, float y, float w)
        {
            if (_timerStyle == null)
            {
                // The results panel's headline size: no new size of the
                // shared font (gotcha 107).
                _timerStyle = UiKit.Style(_cellStyle);
                _timerStyle.fontSize = 26;
                _timerStyle.fontStyle = FontStyle.Bold;
                _timerStyle.alignment = TextAnchor.MiddleCenter;
                _timerStyle.padding = new RectOffset(0, 0, 0, 0);
                _timerColours = new GUIStyle[_colourStyles.Length];
                for (int i = 0; i < _timerColours.Length; i++)
                {
                    _timerColours[i] = UiKit.Style(_timerStyle);
                    _timerColours[i].normal.textColor = _colourStyles[i].normal.textColor;
                }
            }
            float f = GUI.matrix.m00;
            if (f != _timerMeasuredAt)
            {
                _timerMeasuredAt = f;
                _digitW = 0f;
                for (char c = '0'; c <= '9'; c++) _digitW = Mathf.Max(_digitW, CrispText.CalcSize(_timerStyle, TimerGlyphs[c]).x);
                _colonW = CrispText.CalcSize(_timerStyle, TimerGlyphs[':']).x;
                _dotW = CrispText.CalcSize(_timerStyle, TimerGlyphs['.']).x;
            }

            bool running = _recorder.State == RunRecorder.RunState.Running;
            int n = TimerText.Write(_timerChars, TimerSeconds(running), _timeDecimals.Value);
            float total = 0f;
            for (int i = 0; i < n; i++) total += CharW(_timerChars[i]);
            GUIStyle st = _timerColours[(int)_timerColour];
            float cx = x + Mathf.Max(0f, w - total);
            for (int i = 0; i < n; i++)
            {
                char c = _timerChars[i];
                float cw = CharW(c);
                CrispText.Label(new Rect(cx, y, cw, TimerH), TimerGlyphs[c], st);
                cx += cw;
            }
        }

        private float CharW(char c)
        {
            return c == ':' ? _colonW : c == '.' ? _dotW : _digitW;
        }

        private bool PanelShowing
        {
            get { return _splitsPanel != null && _splitsPanel.Value && Timing && _segment != null && _shownRows > 0; }
        }

        // The panels' names as Edit HUD pieces (snapping, T-0020).
        private const string SplitsPiece = "splits", ResultsPiece = "results";

        /// Edit HUD mode (Core/HudWidgets): the panels show even with nothing
        /// in them, with an outline and their resize edges.
        private bool HudEditing
        {
            get { return Host != null && Host.Hud.Widgets != null && Host.Hud.Widgets.Editing; }
        }

        // The size while a resize is under way, else the setting.
        private float PanelWidthNow { get { return _resizeMode != 0 ? _liveW : _panelWidth.Value; } }
        private int PanelRowsNow { get { return _resizeMode != 0 ? _liveRows : _panelRows.Value; } }

        private void EnsureSplitStyles()
        {
            if (_cellStyle != null) return;
            _cellStyle = UiKit.Style(GUI.skin.label);
            _cellStyle.alignment = TextAnchor.MiddleRight;
            _cellStyle.padding = new RectOffset(0, 2, 0, 0);
            _cellStyle.wordWrap = false;
            _cellStyle.clipping = TextClipping.Clip;
            _nameStyle = UiKit.Style(_cellStyle);
            _nameStyle.alignment = TextAnchor.MiddleLeft;
            _titleStyle = UiKit.Style(_nameStyle);
            _titleStyle.fontStyle = FontStyle.Bold;
            _placeholderStyle = UiKit.Style(GUI.skin.label);
            _placeholderStyle.wordWrap = true;
            _placeholderStyle.normal.textColor = UiKit.DimColour;

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
                _colourStyles[i] = UiKit.Style(_cellStyle);
                _colourStyles[i].normal.textColor = colours[i];
            }

            // The kit's rounded dark card: the same look as the results panel
            // and the HUD widgets (docs/ui-redesign.md).
            _panelStyle = UiKit.Style(UiKit.WidgetCard);
        }

        // The panels are drawn in the scaled screen (T-0318); the replay labels
        // sit at world positions in real screen pixels, so they stay outside.
        private UiScale HudUi { get { return Host.Hud.Settings.Scale(); } }

        public override void DrawScreen()
        {
            DrawReplayLabels();
            Matrix4x4 outer = Host.Hud.Settings.BeginScaled();
            try { DrawPanels(); }
            finally { GUI.matrix = outer; }
        }

        private void DrawPanels()
        {
            bool editing = HudEditing;
            DrawResults(editing);
            if (!editing && _resizeMode != 0) EndResize();   // the edit ended mid-gesture

            // The results panel has the splits in it and used to be drawn
            // over this panel (author, 2026-10-05): this one steps aside
            // until the next run starts - except while the runner lays the
            // HUD out, when both show.
            bool real = PanelShowing && (editing || !ResultsShowing);
            // Switched off, it is not drawn in edit mode either: the switch
            // in the Edit HUD list is how it comes back.
            if (!real && !(editing && _splitsPanel.Value))
            {
                if (_dragging) EndDrag();   // hidden mid-drag (the edit ended)
                return;
            }
            EnsureSplitStyles();
            // Docked HUD values show here while the panel does (T-0338).
            if (Host.Hud.Widgets != null) Host.Hud.Widgets.DockHostFrame = Time.frameCount;

            float w = Mathf.Clamp(PanelWidthNow, MinPanelW, HudUi.VW);
            int rows = PanelRowsNow;
            // In edit mode the outline is the most rows it will show, so the
            // bottom edge visibly moves while it is dragged.
            float h = PanelHeight(rows, editing);
            float x, y;
            if (_resizeMode != 0) { x = _resizeX; y = _resizeY; }
            else
            {
                float px = _dragging ? _dragX : HudUi.PosX(_panelX.Value);
                float py = _dragging ? _dragY : HudUi.PosY(_panelY.Value);
                // PanelX < 0 means "against the right edge" - but only as a
                // saved setting: a drag past the left edge must stop at 0, not
                // read as that and jump right (author, v0.24.151, windowed).
                x = px < 0f && !_dragging ? HudUi.VW - w - 8f : Mathf.Clamp(px, 0f, Mathf.Max(0f, HudUi.VW - w));
                y = Mathf.Clamp(py, 0f, Mathf.Max(0f, HudUi.VH - h));
            }
            Rect panel = new Rect(x, y, w, h);
            _panelDrawn = panel;
            if (editing) Host.Hud.Widgets.ReportPiece(SplitsPiece, panel);   // the others snap to it (T-0020)

            if (editing) HandleResize(panel);
            if (_resizeMode == 0) HandleDrag(panel);

            // Opacity on the background only: the text stays readable.
            Color before = GUI.color;
            GUI.color = new Color(before.r, before.g, before.b, Mathf.Clamp01(Opacity));
            WinPx.Box(panel, GUIContent.none, _panelStyle);
            GUI.color = before;
            if (real) DrawSplitsTable(x + 6f, y + 4f, w - 12f, rows, true);
            else
            {
                CrispText.Label(new Rect(x + 6f, y + 4f, w - 12f, RowH), SplitsPanelText, _titleStyle);
                UiText.Draw(x + 6f, y + 4f + RowH, w - 12f, SplitsPlaceholder, _placeholderStyle);
                // The docked rows sit at the bottom, where a run puts them.
                int dockedN = DockedRows();
                if (dockedN > 0) DrawDockedRows(x + 6f, panel.yMax - 4f - dockedN * RowH - 4f, w - 12f);
            }

            if (editing || _dragging) WinPx.Box(panel, GUIContent.none, UiKit.Outline);
            if (editing)
            {
                WinPx.Box(new Rect(panel.xMax - CornerSize, panel.yMax - CornerSize, CornerSize, CornerSize), GUIContent.none, UiKit.Handle);
                // The size while it changes, under the panel.
                if (_resizeMode != 0) CrispText.Label(new Rect(x, panel.yMax + 2f, w, RowH), _panelSizeText, _titleStyle);
            }
        }

        private void HandleDrag(Rect panel)
        {
            Event e = Event.current;
            if (e == null) return;
            MainWindowModule main = Host != null ? Host.Find<MainWindowModule>() : null;

            // Edit HUD only: the F2 window alone is not a drag mode (author, 2026-10-11, T-0317).
            if (!HudEditing || main == null)
            {
                if (_dragging) EndDrag();
                return;
            }

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button != 0 || !panel.Contains(e.mousePosition) || Host.Hud.Settings.Virtual(main.ScreenRect).Contains(e.mousePosition)) return;
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
                    if (HudEditing)
                    {
                        Vector2 at = Host.Hud.Widgets.SnapDrag(SplitsPiece, new Rect(_dragX, _dragY, panel.width, panel.height));
                        _dragX = at.x;
                        _dragY = at.y;
                    }
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
            if (HudEditing) Host.Hud.Widgets.EndSnap();
            float w = Mathf.Clamp(_panelWidth.Value, MinPanelW, HudUi.VW);
            _panelX.Value = HudUi.ToRefX(Mathf.Clamp(_dragX, 0f, Mathf.Max(0f, HudUi.VW - w)));   // saved in reference pixels
            _panelY.Value = HudUi.ToRefY(Mathf.Max(0f, _dragY));
            Ctx.Log.LogInfo("Splits panel moved to (" + Mathf.RoundToInt(_panelX.Value) + ", " + Mathf.RoundToInt(_panelY.Value) + ").");
        }

        /// Edit HUD mode: the right edge sets the width, the bottom edge the
        /// rows, the corner both (author, 2026-10-09: by dragging, as modern
        /// apps do - not +/- buttons). Written once, on release.
        private void HandleResize(Rect panel)
        {
            Event e = Event.current;
            if (e == null) return;
            Vector2 m = e.mousePosition;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button != 0 || _resizeMode != 0 || _dragging) return;
                    MainWindowModule main = Host != null ? Host.Find<MainWindowModule>() : null;
                    if (main != null && Host.Hud.Settings.Virtual(main.ScreenRect).Contains(m)) return;
                    bool corner = m.x >= panel.xMax - CornerSize && m.x <= panel.xMax + EdgeGrab &&
                                  m.y >= panel.yMax - CornerSize && m.y <= panel.yMax + EdgeGrab;
                    bool right = m.x >= panel.xMax - EdgeGrab && m.x <= panel.xMax + EdgeGrab && m.y >= panel.y && m.y <= panel.yMax;
                    bool left = m.x >= panel.x - EdgeGrab && m.x <= panel.x + EdgeGrab && m.y >= panel.y && m.y <= panel.yMax;
                    bool bottom = m.y >= panel.yMax - EdgeGrab && m.y <= panel.yMax + EdgeGrab && m.x >= panel.x && m.x <= panel.xMax;
                    // 1 right edge, 4 left edge (a panel against the right edge
                    // widens from there), 2 bottom; the corner is 1 + 2.
                    int mode = corner ? 3 : (right ? 1 : left ? 4 : 0) | (bottom ? 2 : 0);
                    if (mode == 0) return;
                    _resizeMode = mode;
                    _resizeStart = m;
                    _resizeX = _resizeStartX = panel.x;
                    _resizeY = panel.y;
                    _resizeDocked = _panelX.Value < 0f;
                    _liveW = _resizeStartW = panel.width;
                    _liveRows = _resizeStartRows = Mathf.Clamp(_panelRows.Value, MinPanelRows, MaxPanelRows);
                    _splitsDirty = true;
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (_resizeMode == 0) return;
                    if ((_resizeMode & 1) != 0)
                        _liveW = Mathf.Clamp(Mathf.Round(_resizeStartW + m.x - _resizeStart.x), MinPanelW,
                                             Mathf.Max(MinPanelW, Mathf.Min(MaxPanelW, HudUi.VW - _resizeX)));
                    if ((_resizeMode & 4) != 0)
                    {
                        // The right edge stays put.
                        float rightEdge = _resizeStartX + _resizeStartW;
                        _liveW = Mathf.Clamp(Mathf.Round(_resizeStartW - (m.x - _resizeStart.x)), MinPanelW,
                                             Mathf.Max(MinPanelW, Mathf.Min(MaxPanelW, rightEdge)));
                        _resizeX = rightEdge - _liveW;
                    }
                    if ((_resizeMode & 2) != 0)
                        _liveRows = Mathf.Clamp(_resizeStartRows + Mathf.RoundToInt((m.y - _resizeStart.y) / RowH), MinPanelRows, MaxPanelRows);
                    _splitsDirty = true;
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (_resizeMode == 0) return;
                    EndResize();
                    e.Use();
                    break;
            }
        }

        // One file write for the four values (each entry would save the
        // whole file: 86 ms, bridge 2026-09-27).
        private void EndResize()
        {
            ConfigFile c = Ctx.Config;
            bool saveEach = c.SaveOnConfigSet;
            c.SaveOnConfigSet = false;
            // A panel against the right edge stays there unless its right
            // edge was the one dragged.
            _panelX.Value = _resizeDocked && (_resizeMode & 1) == 0 ? -1f : Mathf.Round(HudUi.ToRefX(_resizeX));
            _panelY.Value = Mathf.Round(HudUi.ToRefY(_resizeY));
            _panelWidth.Value = _liveW;
            _panelRows.Value = _liveRows;
            c.SaveOnConfigSet = saveEach;
            c.Save();
            _resizeMode = 0;
            _splitsDirty = true;
            Ctx.Log.LogInfo("Splits panel resized to " + Mathf.RoundToInt(_liveW) + " px, " + _liveRows + " rows.");
        }

        private int VisibleRows(int maxRows)
        {
            return maxRows <= 1 || _shownRows <= maxRows ? _shownRows : maxRows;
        }

        /// `full`: as tall as `maxRows` rows (edit mode), not only the ones
        /// the current segment has.
        private float PanelHeight(int maxRows, bool full)
        {
            int lines = 0;
            for (int i = 0; i < LineCount; i++) if (_lines2[i].Value) lines++;
            int cols = 0;
            for (int c = 0; c < ColCount; c++) if (_cols[c].Value) cols++;
            int rows = full ? Mathf.Max(maxRows, VisibleRows(maxRows)) : VisibleRows(maxRows);
            int docked = DockedRows();
            return 8f + RowH + (cols > 0 ? RowH : 0f) + rows * RowH + (TimerInPanel ? TimerH : 0f) + (lines > 0 ? 4f + lines * RowH : 0f)
                   + (docked > 0 ? 4f + docked * RowH : 0f);
        }

        /// How many HUD values the runner docked in the panel (T-0338) that
        /// have something to show now.
        private int DockedRows()
        {
            HudBuilder hud = Host.Hud;
            HudWidgets widgets = hud.Widgets;
            if (widgets == null) return 0;
            int n = 0;
            for (int j = 0; j < hud.Count; j++) if (widgets.IsDocked(hud.LineIndex(j))) n++;
            return n;
        }

        /// The docked rows at (x, y), in the order they were docked; returns
        /// the height (with the 4 px gap above them).
        private float DrawDockedRows(float x, float y, float w)
        {
            HudBuilder hud = Host.Hud;
            HudWidgets widgets = hud.Widgets;
            if (widgets == null) return 0f;
            List<int> order = widgets.DockedLines;
            float y0 = y;
            for (int k = 0; k < order.Count; k++)
            {
                int li = order[k];
                for (int j = 0; j < hud.Count; j++)
                {
                    if (hud.LineIndex(j) != li) continue;
                    if (y == y0) y += 4f;
                    GUIContent label = hud.LabelAt(j);
                    GUIContent value = hud.ValueAt(j);
                    // The value keeps what it needs (never over the label); the
                    // label gets the rest, clipped (CalcSize does not allocate).
                    float valueW = Mathf.Min(w, CrispText.CalcSize(_cellStyle, value).x + 4f);
                    if (HudLines.All[li].Label != null && !string.IsNullOrEmpty(label.text) && w - valueW > 14f)
                        CrispText.Label(new Rect(x, y, w - valueW - 4f, RowH), label, _nameStyle);
                    int trend = hud.TrendAt(j);
                    if (trend >= 0)
                    {
                        // The colour only: the glyphs (and the font's texture) stay.
                        Color normal = _cellStyle.normal.textColor;
                        _cellStyle.normal.textColor = Host.Hud.Settings.TrendColour((TrendState)trend);
                        CrispText.Label(new Rect(x + w - valueW, y, valueW, RowH), value, _cellStyle);
                        _cellStyle.normal.textColor = normal;
                    }
                    else CrispText.Label(new Rect(x + w - valueW, y, valueW, RowH), value, _cellStyle);
                    y += RowH;
                }
            }
            return y - y0;
        }

        /// The table at (x, y), `w` wide; returns its height. `header` adds
        /// the column titles.
        private float DrawSplitsTable(float x, float y, float w, int maxRows, bool header)
        {
            float colW = 50f + 6f * Mathf.Max(_timeDecimals.Value, _deltaDecimals.Value);   // 62 at 2 decimals
            float y0 = y;
            int cols = 0;
            for (int c = 0; c < ColCount; c++) if (_cols[c].Value) cols++;
            float nameW = Mathf.Max(60f, w - cols * colW);

            CrispText.Label(new Rect(x, y, w, RowH), _compareTitle, _titleStyle);
            y += RowH;

            if (header && cols > 0)
            {
                float cx = x + nameW;
                for (int c = 0; c < ColCount; c++)
                {
                    if (!_cols[c].Value) continue;
                    CrispText.Label(new Rect(cx, y, colW, RowH), _colTitles[c], _cellStyle);
                    cx += colW;
                }
                y += RowH;
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
                // LiveSplit's current-split highlight: a bar across the row.
                if (_rowCurrent[r])
                {
                    Color before = GUI.color;
                    GUI.color = new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.4f);
                    WinPx.DrawTexture(new Rect(x - 3f, y, w + 6f, RowH), UiKit.AccentTexture);
                    GUI.color = before;
                }
                GUIStyle nameStyle = _rowCurrent[r] ? _titleStyle : _nameStyle;
                CrispText.Label(new Rect(x, y, nameW, RowH), _rowNames[r], nameStyle);
                float cx = x + nameW;
                for (int c = 0; c < ColCount; c++)
                {
                    if (!_cols[c].Value) continue;
                    GUIStyle st = _cellStyle;
                    if (c == (int)Col.Delta || c == (int)Col.SegmentDelta) st = _colourStyles[(int)_rowColours[r]];
                    CrispText.Label(new Rect(cx, y, colW, RowH), _cells[r * ColCount + c], st);
                    cx += colW;
                }
                y += RowH;
            }
            // Edit mode: the rows the panel has room for but this segment
            // does not fill stay empty, so the lines sit where they will.
            if (HudEditing && maxRows > visible) y += (maxRows - visible) * RowH;

            // LiveSplit's order: the splits, the timer, then the summary lines.
            if (TimerInPanel)
            {
                DrawTimer(x, y, w);
                y += TimerH;
            }

            bool any = false;
            for (int i = 0; i < LineCount; i++)
            {
                if (!_lines2[i].Value) continue;
                if (!any) { y += 4f; any = true; }
                CrispText.Label(new Rect(x, y, w - 90f, RowH), _lineLabels[i], _nameStyle);
                GUIStyle st = i == (int)Line.Previous ? _colourStyles[(int)SplitTable.ColourOf(_summary.PreviousSegment, _summary.PreviousSegment, float.NaN, float.NaN)] : _cellStyle;
                // The whole row, right-aligned: a long value ("100% (Congrats!)")
                // is cut only when it meets its label, not at a fixed column.
                CrispText.Label(new Rect(x, y, w, RowH), _lineValues[i], st);
                y += RowH;
            }

            // The HUD values docked here (T-0338): LiveSplit's extra components,
            // a name left and its value right, under the summary lines.
            y += DrawDockedRows(x, y, w);
            return y - y0;
        }

        // --- Edit HUD rows (T-0257) -----------------------------------------------------
        //
        // The splits and results panels are switched on and set up where
        // they are shown (author, 2026-10-10): rows in the Edit HUD list,
        // never a copy of the table in a tab. Each says what it is in a
        // line that is always on the page (a runner who does not know the
        // results panel would otherwise leave it off out of doubt).

        private static readonly GUIContent SplitsPanelText = new GUIContent("Splits panel");
        private static readonly GUIContent SplitsPanelAbout = new GUIContent(
            "Your splits against the comparison, as in LiveSplit, while a timed segment is on. " +
            "On screen: drag it to move, a side edge for the width, its bottom edge for the rows.");
        private static readonly GUIContent SplitsPlaceholder = new GUIContent(
            "Shows here while a timed segment is on. Drag the edges to size it.");
        private static readonly GUIContent SplitsOptionsTitle = new GUIContent("Columns, lines, decimals, background");
        private static readonly GUIContent TimerTitle = new GUIContent("Run timer");
        private static readonly GUIContent TimerInText = new GUIContent("In the panel");
        private static readonly GUIContent TimerOutText = new GUIContent("On its own");
        private static readonly GUIContent TimerTip = new GUIContent(
            "In the panel: under the splits, as in LiveSplit. On its own: the HUD's Run timer value, placed under the panel - drag it anywhere, size it by its corner.");
        private static readonly GUIContent ResultsPanelText = new GUIContent("Results panel");
        private static readonly GUIContent ResultsPanelAbout = new GUIContent(
            "Pops up when a timed run finishes: your time against your PB and the comparison, every split's delta, " +
            "your golds and PB chance. It closes when the next run starts. On screen: drag it to move, its right edge for the width.");
        private static readonly GUIContent ResLoadAlwaysText = new GUIContent(" Load-removed time after a run with no loads too");
        private static readonly GUIContent ResLoadAlwaysTip = new GUIContent(
            "A run with loads always shows its time without them; this adds the line after a load-free run. The LRT column is the splits' own.");
        private static readonly GUIContent TimeDecimalsText = new GUIContent("Time decimals");
        private static readonly GUIContent ResDecimalsText = new GUIContent("Decimals");
        private static readonly GUIContent ResDecimalsTip = new GUIContent(
            "The results panel's times and deltas, from the next finished run. The splits panel's decimals are its own.");
        private static readonly GUIContent DeltaDecimalsText = new GUIContent("Delta decimals");
        private static readonly GUIContent BackgroundText = new GUIContent("Background");
        private static readonly GUIContent ColumnsText = new GUIContent("Columns");
        private static readonly GUIContent LinesText = new GUIContent("Lines");
        private static readonly GUIContent[] DecimalChoices = { new GUIContent("0"), new GUIContent("1"), new GUIContent("2"), new GUIContent("3") };

        public override float DrawHudEditor(float y, float w)
        {
            const float indent = 20f;
            bool panel = UiKit.Toggle(new Rect(0f, y, w, 22f), _splitsPanel.Value, SplitsPanelText);
            if (panel != _splitsPanel.Value) _splitsPanel.Value = panel;
            y += 24f;
            y += UiText.Draw(indent, y, w - indent, SplitsPanelAbout, UiKit.HintStyle) + 2f;
            if (_splitsPanel.Value)
            {
                y = DrawTimerChoice(indent, y, w - indent);
                if (UiKit.Section(indent, ref y, w - indent, "hud.splitsopts", SplitsOptionsTitle, false))
                    y = DrawSplitsOptions(indent, y, w - indent);
            }
            y += 8f;

            bool results = UiKit.Toggle(new Rect(0f, y, w, 22f), _resultsCfg.Value, ResultsPanelText);
            if (results != _resultsCfg.Value) { _resultsCfg.Value = results; if (!results) CloseResults(); }
            y += 24f;
            y += UiText.Draw(indent, y, w - indent, ResultsPanelAbout, UiKit.HintStyle) + 2f;
            if (_resultsCfg.Value)
            {
                Rect loadR = new Rect(indent, y, w - indent, 22f);
                bool always = UiKit.Toggle(loadR, _resLoadAlways.Value, ResLoadAlwaysText);
                if (always != _resLoadAlways.Value) _resLoadAlways.Value = always;
                UiKit.Hint(loadR, ResLoadAlwaysTip);
                y += 24f;
                const float labelW = 110f;
                float barW = Mathf.Min(160f, w - indent - labelW - 4f);
                Rect decR = new Rect(indent + labelW, y, barW, 22f);
                WinPx.Label(new Rect(indent, y, labelW, 22f), ResDecimalsText);
                int rd = DecimalChoice(decR, _resDecimals.Value);
                if (rd != _resDecimals.Value) _resDecimals.Value = rd;
                UiKit.Hint(decR, ResDecimalsTip);
                y += 26f;
            }
            return y + 4f;
        }

        // One setting, two choices (decisions.md: options that contradict are
        // never both on): the timer is in the panel or on its own.
        private float DrawTimerChoice(float x, float y, float w)
        {
            const float labelW = 110f;
            WinPx.Label(new Rect(x, y, labelW, 22f), TimerTitle);
            float bw = Mathf.Max(60f, (w - labelW - 4f) * 0.5f);
            Rect inR = new Rect(x + labelW, y, bw, 22f), outR = new Rect(x + labelW + bw + 4f, y, bw, 22f);
            bool inPanel = _timerInPanel.Value;
            if (UiKit.Toggle(inR, inPanel, TimerInText, GUI.skin.button) && !inPanel) SetTimerInPanel(true);
            if (UiKit.Toggle(outR, !inPanel, TimerOutText, GUI.skin.button) && inPanel) SetTimerInPanel(false);
            UiKit.Hint(inR, TimerTip);
            UiKit.Hint(outR, TimerTip);
            return y + 28f;
        }

        private Rect _panelDrawn;   // the splits panel as last drawn (virtual pixels)

        /// Pops the timer out (the HUD's Run timer value, switched on and placed
        /// under the panel) or puts it back in the panel (the value back in the
        /// column, where it shows nothing while the panel has the timer).
        private void SetTimerInPanel(bool inPanel)
        {
            _timerInPanel.Value = inPanel;
            _splitsDirty = true;
            int line = HudLines.IndexOfKey("ShowRunTimer");
            HudWidgets widgets = Host.Hud.Widgets;
            HudSettings settings = Host.Hud.Settings;
            if (inPanel)
            {
                widgets.Attach(line);
                Ctx.Log.LogInfo("Splits panel: run timer in the panel.");
                return;
            }
            if (!settings.Shows(line)) settings.SetShows(line, true);
            if (!widgets.IsFree(line))
            {
                UiScale ui = HudUi;
                Rect p = _panelDrawn.width > 0f ? _panelDrawn : new Rect(ui.VW - 320f, 140f, 300f, 200f);
                float y = p.yMax + 6f;
                if (y > ui.VH - 60f) y = Mathf.Max(0f, p.y - 60f);
                widgets.Detach(line, ui.ToRefX(p.x), ui.ToRefY(y), 2f);
            }
            Ctx.Log.LogInfo("Splits panel: run timer on its own (the HUD's Run timer value).");
        }

        private float DrawSplitsOptions(float x, float y, float w)
        {
            y = FlowToggles(x, y, w, ColumnsText, _cols, _colOptionText);
            y = FlowToggles(x, y, w, LinesText, _lines2, _lineOptionText);

            const float labelW = 110f;
            float barW = Mathf.Min(160f, w - labelW - 4f);
            WinPx.Label(new Rect(x, y, labelW, 22f), TimeDecimalsText);
            int td = DecimalChoice(new Rect(x + labelW, y, barW, 22f), _timeDecimals.Value);
            if (td != _timeDecimals.Value) { _timeDecimals.Value = td; _splitsDirty = true; }
            y += 26f;
            WinPx.Label(new Rect(x, y, labelW, 22f), DeltaDecimalsText);
            int dd = DecimalChoice(new Rect(x + labelW, y, barW, 22f), _deltaDecimals.Value);
            if (dd != _deltaDecimals.Value) { _deltaDecimals.Value = dd; _splitsDirty = true; }
            y += 26f;

            WinPx.Label(new Rect(x, y, labelW, 22f), BackgroundText);
            float sliderW = Mathf.Max(60f, w - labelW - 50f);
            float op = WinPx.HorizontalSlider(new Rect(x + labelW, y + 6f, sliderW, 16f), Opacity, 0f, 1f);
            if (Mathf.Abs(op - Opacity) > 0.004f)
            {
                _opacityNow = Mathf.Round(op * 100f) / 100f;
                _opacityPending = true;
                _writeAt = Time.unscaledTime + 0.5f;
            }
            WinPx.Label(new Rect(x + labelW + sliderW + 6f, y, 44f, 22f), _opacityText);
            return y + 28f;
        }

        // 0-3 as four buttons, the current one pressed (GUI.Toolbar builds
        // style names as strings on every call).
        private static int DecimalChoice(Rect r, int value)
        {
            float bw = (r.width - 6f) / 4f;
            int picked = Mathf.Clamp(value, 0, 3);
            for (int i = 0; i < 4; i++)
            {
                bool on = UiKit.Toggle(new Rect(r.x + i * (bw + 2f), r.y, bw, r.height), i == picked, DecimalChoices[i], GUI.skin.button);
                if (on && i != picked) picked = i;
            }
            return picked;
        }

        // Toggles laid out left to right, each as wide as its words,
        // wrapping at the width (the Edit HUD list is narrow).
        private float FlowToggles(float x0, float y, float w, GUIContent title, ConfigEntry<bool>[] entries, GUIContent[] labels)
        {
            y += UiText.Draw(x0, y, w, title);
            float x = x0;
            GUIStyle toggle = GUI.skin.toggle;
            for (int i = 0; i < entries.Length; i++)
            {
                float tw = Mathf.Min(w, WinPx.CalcSize(toggle, labels[i]).x + 8f);
                if (x + tw > x0 + w && x > x0) { x = x0; y += 22f; }
                bool v = UiKit.Toggle(new Rect(x, y, tw, 20f), entries[i].Value, labels[i]);
                if (v != entries[i].Value) { entries[i].Value = v; _splitsDirty = true; _playtimeShown = -1; }
                x += tw + 6f;
            }
            return y + 26f;
        }

        // --- the runner name (the Runs tab's Upload to the website) ------------------------

        /// The name your attempts and uploads carry (T-0257: it lived in the
        /// splits options; the site is where it shows). Filled from Tick.
        private float DrawRunnerName(float y, float w)
        {
            WinPx.Label(new Rect(0, y, 110, 20), "Runner name");
            if (_runnerEdit == null) _runnerFill = true;
            else
            {
                string typed = WinPx.TextField(new Rect(114, y - 2, Mathf.Max(80f, w - 124f), 22), _runnerEdit);
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
    }
}
