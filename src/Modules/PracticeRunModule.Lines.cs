using BepInEx.Configuration;
using ForestOverlay.Core;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Run line options (Runs -> Line options; QA Discord 2026-09-26,
    // v0.24.191): the lines' opacity (sxczurass: at full they hid what was
    // behind them) and showing only the next few seconds of the comparison
    // line (author), so a long route is not one tangle. The colours already
    // differ: the comparison blue, the current run yellow, the ghost pink.
    //
    // One feature, its own settings (T-0253; decisions.md *Easy to learn*):
    // the run lines' options act on the run lines only and show only while
    // they are on; the ghost has its own switch and, with the replay, its
    // own opacity (Runs -> Ghost and replay). Both new settings start from
    // the old shared ones, so nothing a runner set changes.
    //
    // Sliders hold their value and write the config once it settles
    // (gotcha 60: a write saves the whole file).
    // ------------------------------------------------------------------
    public sealed partial class PracticeRunModule
    {
        private ConfigEntry<float> _lineOpacity, _lineAhead, _replayOpacity;
        private ConfigEntry<bool> _lineAheadOn, _keepFailedCfg, _ghostCfg;

        private float _lineOpacityNow = -1f, _lineAheadNow = -1f, _replayOpacityNow = -1f;   // < 0 = the config's value
        private float _lineWriteAt;
        private readonly GUIContent _lineOpacityText = new GUIContent("");
        private readonly GUIContent _lineAheadText = new GUIContent("");
        private readonly GUIContent _replayOpacityText = new GUIContent("");
        private float _lineOpacityShown = -1f, _lineAheadShown = -1f, _replayOpacityShown = -1f;

        private static readonly GUIContent TextGhost = new GUIContent(" Ghost during a run");
        private static readonly GUIContent TipGhost = new GUIContent(
            "Where the comparison run was at your run's time, in pink. The replay camera always shows it.");
        private static readonly GUIContent TipReplayOpacity = new GUIContent(
            "The ghost, the replay's buildings and markers, and the comparison line while you watch it.");
        private static readonly GUIContent TipLineOpacity = new GUIContent("Your run's line, the comparison's and the red one.");

        private float LineOpacity { get { return _lineOpacityNow >= 0f ? _lineOpacityNow : _lineOpacity.Value; } }
        private float LineAhead { get { return _lineAheadNow >= 0f ? _lineAheadNow : _lineAhead.Value; } }
        private float ReplayOpacity { get { return _replayOpacityNow >= 0f ? _replayOpacityNow : _replayOpacity.Value; } }

        private void InitLineOptions(ModuleContext ctx)
        {
            _lineOpacity = ctx.Config.Bind("Runs", "RunLineOpacity", 0.9f, "Run lines (yours, the comparison's, the red one), 0.1 (faint) to 1 (solid).");
            // Defaults from the settings they split off (T-0253): the ghost
            // was drawn with the run lines and at their opacity.
            _ghostCfg = ctx.Config.Bind("Runs", "Ghost", _showLinesCfg.Value,
                "Draw the comparison run's ghost during a run (the replay camera always shows it).");
            _replayOpacity = ctx.Config.Bind("Runs", "ReplayOpacity", _lineOpacity.Value,
                "The ghost, the replay's buildings and markers, and the comparison line while the replay camera plays, 0.1 (faint) to 1 (solid).");
            _lineAheadOn = ctx.Config.Bind("Runs", "RunLineAheadOnly", false,
                "Draw only the next RunLineAheadSeconds of the comparison line, from where its ghost is.");
            _lineAhead = ctx.Config.Bind("Runs", "RunLineAheadSeconds", 5f, "How far ahead the comparison line reaches with RunLineAheadOnly (1-60 s).");
            _keepFailedCfg = ctx.Config.Bind("Runs", "KeepFailedRunLine", true,
                "Keep the line of the last run that did not finish (a restart, abort or death), in red, until the next one fails.");
        }

        private void FlushLineOptions()
        {
            if (_lineWriteAt <= 0f || Time.unscaledTime < _lineWriteAt) return;
            _lineWriteAt = 0f;
            if (_lineOpacityNow >= 0f) { _lineOpacity.Value = _lineOpacityNow; _lineOpacityNow = -1f; }
            if (_lineAheadNow >= 0f) { _lineAhead.Value = _lineAheadNow; _lineAheadNow = -1f; }
            if (_replayOpacityNow >= 0f) { _replayOpacity.Value = _replayOpacityNow; _replayOpacityNow = -1f; }
        }

        /// The run lines' own options, indented under the Run lines switch
        /// and drawn only while it is on (they act on nothing else); returns
        /// the new y.
        private float DrawLineOptions(float x, float y, float w)
        {
            // Text rebuilt only when the value moves (OnGUI runs often).
            if (LineOpacity != _lineOpacityShown) { _lineOpacityShown = LineOpacity; _lineOpacityText.text = Mathf.RoundToInt(LineOpacity * 100f) + "%"; }
            if (LineAhead != _lineAheadShown) { _lineAheadShown = LineAhead; _lineAheadText.text = LineAhead.ToString("0") + " s"; }

            w -= x;
            float sliderW = Mathf.Max(80f, w - 190f);
            GUI.Label(new Rect(x, y, 120, 20), "Opacity");
            UiKit.Hint(new Rect(x, y, 120, 20), TipLineOpacity);
            float op = GUI.HorizontalSlider(new Rect(x + 124f, y + 5, sliderW, 16), LineOpacity, 0.1f, 1f);
            if (Mathf.Abs(op - LineOpacity) > 0.004f)
            {
                _lineOpacityNow = Mathf.Round(op * 100f) / 100f;
                _lineWriteAt = Time.unscaledTime + 0.5f;
            }
            GUI.Label(new Rect(x + 130f + sliderW, y, 60, 20), _lineOpacityText);
            y += 24f;

            bool failed = GUI.Toggle(new Rect(x, y, w, 20), _keepFailedCfg.Value, " Keep the last unfinished run's line (red)");
            if (failed != _keepFailedCfg.Value) _keepFailedCfg.Value = failed;
            y += 22f;

            bool ahead = GUI.Toggle(new Rect(x, y, w, 20), _lineAheadOn.Value, " Comparison line: only the next few seconds of it");
            if (ahead != _lineAheadOn.Value) _lineAheadOn.Value = ahead;
            y += 22f;
            if (_lineAheadOn.Value)
            {
                GUI.Label(new Rect(x, y, 120, 20), "Seconds ahead");
                float s = GUI.HorizontalSlider(new Rect(x + 124f, y + 5, sliderW, 16), LineAhead, 1f, 60f);
                if (Mathf.Abs(s - LineAhead) > 0.05f)
                {
                    _lineAheadNow = Mathf.Round(s);
                    _lineWriteAt = Time.unscaledTime + 0.5f;
                }
                GUI.Label(new Rect(x + 130f + sliderW, y, 60, 20), _lineAheadText);
                y += 24f;
            }
            return y + 4f;
        }

        /// Ghost and replay's look: the ghost's switch and shape, then the
        /// opacity the ghost and the replay share; returns the new y.
        private float DrawGhostOptions(float y, float w)
        {
            if (ReplayOpacity != _replayOpacityShown) { _replayOpacityShown = ReplayOpacity; _replayOpacityText.text = Mathf.RoundToInt(ReplayOpacity * 100f) + "%"; }

            Rect ghostR = new Rect(0, y, w, 22);
            bool ghost = GUI.Toggle(ghostR, _ghostCfg.Value, TextGhost);
            if (ghost != _ghostCfg.Value) _ghostCfg.Value = ghost;
            UiKit.Hint(ghostR, TipGhost);
            y += 24f;

            y = DrawGhostLook(y, w);

            float sliderW = Mathf.Max(80f, w - 190f);
            GUI.Label(new Rect(0, y, 120, 20), "Opacity");
            UiKit.Hint(new Rect(0, y, 120, 20), TipReplayOpacity);
            float op = GUI.HorizontalSlider(new Rect(124, y + 5, sliderW, 16), ReplayOpacity, 0.1f, 1f);
            if (Mathf.Abs(op - ReplayOpacity) > 0.004f)
            {
                _replayOpacityNow = Mathf.Round(op * 100f) / 100f;
                _lineWriteAt = Time.unscaledTime + 0.5f;
            }
            GUI.Label(new Rect(130f + sliderW, y, 60, 20), _replayOpacityText);
            return y + 26f;
        }
    }
}
