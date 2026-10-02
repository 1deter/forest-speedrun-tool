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
    // Sliders hold their value and write the config once it settles
    // (gotcha 60: a write saves the whole file).
    // ------------------------------------------------------------------
    public sealed partial class PracticeRunModule
    {
        private ConfigEntry<float> _lineOpacity, _lineAhead;
        private ConfigEntry<bool> _lineAheadOn, _keepFailedCfg;
        private bool _lineOptionsOpen;

        private float _lineOpacityNow = -1f, _lineAheadNow = -1f;   // < 0 = the config's value
        private float _lineWriteAt;
        private readonly GUIContent _lineOpacityText = new GUIContent("");
        private readonly GUIContent _lineAheadText = new GUIContent("");
        private float _lineOpacityShown = -1f, _lineAheadShown = -1f;

        private float LineOpacity { get { return _lineOpacityNow >= 0f ? _lineOpacityNow : _lineOpacity.Value; } }
        private float LineAhead { get { return _lineAheadNow >= 0f ? _lineAheadNow : _lineAhead.Value; } }

        private void InitLineOptions(ModuleContext ctx)
        {
            _lineOpacity = ctx.Config.Bind("Runs", "RunLineOpacity", 0.9f, "Run lines and ghost, 0.1 (faint) to 1 (solid).");
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
        }

        /// At the top of the Runs tab's scroll area when open; returns the new y.
        private float DrawLineOptions(float y, float w)
        {
            if (!_lineOptionsOpen) return y;

            // Text rebuilt only when the value moves (OnGUI runs often).
            if (LineOpacity != _lineOpacityShown) { _lineOpacityShown = LineOpacity; _lineOpacityText.text = Mathf.RoundToInt(LineOpacity * 100f) + "%"; }
            if (LineAhead != _lineAheadShown) { _lineAheadShown = LineAhead; _lineAheadText.text = LineAhead.ToString("0") + " s"; }

            float sliderW = Mathf.Max(80f, w - 190f);
            GUI.Label(new Rect(0, y, 120, 20), "Line opacity");
            float op = GUI.HorizontalSlider(new Rect(124, y + 5, sliderW, 16), LineOpacity, 0.1f, 1f);
            if (Mathf.Abs(op - LineOpacity) > 0.004f)
            {
                _lineOpacityNow = Mathf.Round(op * 100f) / 100f;
                _lineWriteAt = Time.unscaledTime + 0.5f;
            }
            GUI.Label(new Rect(130f + sliderW, y, 60, 20), _lineOpacityText);
            y += 24f;

            bool failed = GUI.Toggle(new Rect(0, y, w, 20), _keepFailedCfg.Value, " Keep the last unfinished run's line (red)");
            if (failed != _keepFailedCfg.Value) _keepFailedCfg.Value = failed;
            y += 22f;

            bool ahead = GUI.Toggle(new Rect(0, y, w, 20), _lineAheadOn.Value, " Comparison line: only the next few seconds of it");
            if (ahead != _lineAheadOn.Value) _lineAheadOn.Value = ahead;
            y += 22f;
            if (_lineAheadOn.Value)
            {
                GUI.Label(new Rect(0, y, 120, 20), "Seconds ahead");
                float s = GUI.HorizontalSlider(new Rect(124, y + 5, sliderW, 16), LineAhead, 1f, 60f);
                if (Mathf.Abs(s - LineAhead) > 0.05f)
                {
                    _lineAheadNow = Mathf.Round(s);
                    _lineWriteAt = Time.unscaledTime + 0.5f;
                }
                GUI.Label(new Rect(130f + sliderW, y, 60, 20), _lineAheadText);
                y += 24f;
            }
            return y + 6f;
        }
    }
}
