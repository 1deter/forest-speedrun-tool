using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // The splits panel is the run timer's (Modules/RunTimerModule.Splits,
    // T-0467): its table, stats, comparison, timer and Edit HUD rows. What
    // the practice tools give it, as its companion (Core/ITimerCompanion):
    // another runner's and a LiveSplit file's comparisons, the replay being
    // watched, the results panel, checkpoint practice's golds. They draw
    // it too, in the order the screen always had: their marks and labels,
    // the results panel, the splits panel, the replay bar.
    //
    // And the runner field of the Runs tab's Upload section (the name is
    // the timer's: it stamps the runs).
    // ------------------------------------------------------------------
    public sealed partial class PracticeRunModule : ITimerCompanion
    {
        // --- the timer's panel, as the practice tools read it -------------------------

        private SplitStats _stats { get { return _runTimer.Stats; } }
        // The rows' times (NaN = not reached): Data/RunTimer's Times.
        private float[] _times { get { return _timer.Times; } }
        private int SplitRows { get { return _runTimer.SplitRows; } }
        private float Opacity { get { return _runTimer.Opacity; } }

        /// The comparison (the timer's; another runner and LiveSplit are
        /// picked here).
        private RunComparison _referenceKind
        {
            get { return _runTimer.Comparison; }
            set { _runTimer.Comparison = value; }
        }

        private string ComparisonName() { return _runTimer.ComparisonName(); }
        private float[] ComparisonSplits() { return _runTimer.ComparisonSplits(); }

        // The panels' shared look (Core/SplitLook, the timer's): the results
        // panel, the replay bar, the line marks.
        private SplitLook _look { get { return _runTimer.Look; } }
        private GUIStyle _cellStyle { get { return _look.Cell; } }
        private GUIStyle _nameStyle { get { return _look.Name; } }
        private GUIStyle _titleStyle { get { return _look.Title; } }
        private GUIStyle _placeholderStyle { get { return _look.Placeholder; } }
        private GUIStyle[] _colourStyles { get { return _look.Colours; } }
        private GUIStyle _panelStyle { get { return _look.Panel; } }
        private static Color[] SplitPalette { get { return SplitLook.Palette; } }

        private const float EdgeGrab = 5f;

        // The results panel's name as an Edit HUD piece (snapping, T-0020).
        private const string ResultsPiece = "results";

        /// Edit HUD mode (Core/HudWidgets): the panels show even with nothing
        /// in them, with an outline and their resize edges.
        private bool HudEditing
        {
            get { return Host != null && Host.Hud.Widgets != null && Host.Hud.Widgets.Editing; }
        }

        private void EnsureSplitStyles()
        {
            _look.Ensure();
        }

        // The panels are drawn in the scaled screen (T-0318); the replay labels
        // sit at world positions in real screen pixels, so they stay outside.
        private UiScale HudUi { get { return Host.Hud.Settings.Scale(); } }

        public override void DrawScreen()
        {
            DrawReplayLabels();
            DrawLineMarkDots();
            DrawLineMarkLabels();
            DrawExtraLineLabels();
            Matrix4x4 outer = Host.Hud.Settings.BeginScaled();
            try
            {
                DrawResults(HudEditing);
                _runTimer.DrawSplitsPanel();   // the timer's, after the results panel as before
                DrawReplayBar();
            }
            finally { GUI.matrix = outer; }
        }

        // --- the timer's companion (Core/ITimerCompanion) --------------------------

        float[] ITimerCompanion.OtherSplits(RunComparison kind, float[] pb)
        {
            if (kind == RunComparison.Runner) return PickedRunner != null ? PickedRunner.Splits : pb;
            return LssSplits() ?? pb;
        }

        string ITimerCompanion.OtherName(RunComparison kind)
        {
            if (kind == RunComparison.Runner) return PickedRunner != null ? PickedRunner.Name : "Personal best";
            return LssComparisonName();
        }

        // After the runner's own four (the timer's): each other runner on the
        // website's board, then each comparison in the linked LiveSplit file.
        bool ITimerCompanion.NextOther(RunComparison kind)
        {
            if (kind == RunComparison.Runner)
            {
                if (_runnerPick + 1 < _others.Count) PickRunner(_runnerPick + 1);
                else if (LssAvailable) PickLss(0);
                else return false;
                return true;
            }
            if (kind == RunComparison.LiveSplit)
            {
                if (_lssPick + 1 < _lssCompareSplits.Count) { PickLss(_lssPick + 1); return true; }
                return false;
            }
            // From best segments.
            if (_others.Count > 0) { PickRunner(0); return true; }
            if (LssAvailable) { PickLss(0); return true; }
            return false;
        }

        void ITimerCompanion.ComparisonChanged() { SelectReference(); }

        bool ITimerCompanion.TabShowing { get { return TabShowing; } }

        Attempt ITimerCompanion.Watched { get { return WatchingComparison ? _reference : null; } }

        float ITimerCompanion.WatchClock { get { return _clock.T; } }

        bool ITimerCompanion.ResultsShowing { get { return ResultsShowing; } }

        void ITimerCompanion.ApplyGolds(SplitStats stats) { ApplyPracticeGolds(stats); }

        void ITimerCompanion.SplitsReset() { CloseResults(); }

        // --- the runner name (the Runs tab's Upload to the website) ------------------------

        // The runner field: one text box, pre-filled with the Steam name
        // (author, 2026-09-27: one field, the name already typed). Filled
        // from Tick when the field is first drawn; null = not filled yet.
        // The name itself is the timer's (RunTimerModule: it stamps the runs).
        private string _runnerEdit;
        private string _steamName;
        private bool _runnerFill;
        private readonly GUIContent _noSteamHint = new GUIContent(
            "Steam was not found - type the name your times should carry.");

        private string RunnerNameNow() { return _runTimer.RunnerName(); }
        private string RunnerIdNow() { return _runTimer.RunnerId(); }

        /// The runner field's text, read once it is first drawn (Tick).
        private void RefreshRunnerField()
        {
            if (!_runnerFill) return;
            _runnerFill = false;
            _steamName = RunnerIdentity.SteamName();
            string typed = _runTimer.SavedRunnerName;
            _runnerEdit = typed.Length > 0 ? typed : (_steamName ?? "");
        }

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
                    _runTimer.TypeRunnerName(t.Length == 0 || t == _steamName ? "" : t);
                }
            }
            y += 26f;
            if (_steamName == null && _runnerEdit != null && _runnerEdit.Trim().Length == 0) y += UiText.Draw(0, y, w, _noSteamHint);
            return y + 4f;
        }
    }
}
