using BepInEx.Configuration;
using System.Collections.Generic;
using ForestOverlay.Core;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Run line options (Runs -> Run options, under Run lines; QA Discord 2026-09-26,
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
        private ConfigEntry<float> _lineOpacity, _lineAhead, _replayOpacity, _lineWidth;
        private ConfigEntry<bool> _lineAheadOn, _keepFailedCfg, _ghostCfg;
        private ConfigEntry<string> _lineBehindCfg;
        private ConfigEntry<bool> _lineBySpeedCfg;
        private ConfigEntry<string> _markFieldsCfg;   // what the checkpoint markers show (T-0388)
        private static readonly GUIContent TipLineColour = new GUIContent(
            "The comparison line's colour: one colour, or along its length by the speed it was run at (green fast, amber, red slow).");
        private readonly GUIContent _speedLegend = new GUIContent("");
        private float _legendMin = -1f, _legendMax = -1f;
        // The by-speed colour range of the comparison line (SpeedRamp.Range),
        // worked out when the line changes.
        private float _speedLo, _speedHi;
        private int _speedCount = -1;
        private object _speedSource;
        private static readonly string[] BehindNames = { "Hidden", "Faint", "Through" };
        private static readonly GUIContent TipLineBehind = new GUIContent(
            "What the run lines do behind plants, rocks and the ground: hidden by them, faint through them, or drawn fully through.");

        /// 0 hidden, 1 faint, 2 drawn through (RunLineBehaviour.Behind).
        private int LineBehind
        {
            get
            {
                string v = _lineBehindCfg == null ? null : _lineBehindCfg.Value;
                for (int i = 0; i < BehindNames.Length; i++)
                    if (string.Equals(v, BehindNames[i], System.StringComparison.OrdinalIgnoreCase)) return i;
                return 0;
            }
        }

        private float _lineOpacityNow = -1f, _lineAheadNow = -1f, _replayOpacityNow = -1f, _lineWidthNow = -1f;   // < 0 = the config's value
        private float _lineWriteAt;
        private readonly GUIContent _lineOpacityText = new GUIContent("");
        private readonly GUIContent _lineAheadText = new GUIContent("");
        private readonly GUIContent _replayOpacityText = new GUIContent("");
        private readonly GUIContent _lineWidthText = new GUIContent("");
        private float _lineOpacityShown = -1f, _lineAheadShown = -1f, _replayOpacityShown = -1f, _lineWidthShown = -1f;

        private static readonly GUIContent TextGhost = new GUIContent(" Ghost during a run");
        private static readonly GUIContent TipGhost = new GUIContent(
            "Where the comparison run was at your run's time, in pink. The replay camera always shows it.");
        private static readonly GUIContent TipReplayOpacity = new GUIContent(
            "The ghost, the replay's buildings and markers, and the comparison line while you watch it.");
        private static readonly GUIContent TipLineOpacity = new GUIContent("Your run's line, the comparison's and the red one.");
        private static readonly GUIContent TipLineWidth = new GUIContent("How thick the run lines and the ghost are drawn, in pixels on a 1080p screen.");

        private float LineOpacity { get { return _lineOpacityNow >= 0f ? _lineOpacityNow : _lineOpacity.Value; } }
        private float LineAhead { get { return _lineAheadNow >= 0f ? _lineAheadNow : _lineAhead.Value; } }
        private float ReplayOpacity { get { return _replayOpacityNow >= 0f ? _replayOpacityNow : _replayOpacity.Value; } }
        private float LineWidth { get { return Mathf.Clamp(_lineWidthNow >= 0f ? _lineWidthNow : _lineWidth.Value, 1f, 8f); } }

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
            _lineWidth = ctx.Config.Bind("Runs", "RunLineWidth", 3f,
                "Run lines' and the ghost's width in pixels on a 1080p screen (scaled with the screen's height), 1-8.");
            _lineBehindCfg = ctx.Config.Bind("Runs", "RunLineBehind", "Hidden",
                "Run lines behind plants, rocks and the ground: Hidden (depth-tested), Faint (the hidden parts at a third of the opacity) or Through (drawn fully through).");
            _lineBySpeedCfg = ctx.Config.Bind("Runs", "RunLineBySpeed", false,
                "Colour the comparison line along its length by the speed it was run at (green fast, amber, red slow) instead of one colour.");
            _markFieldsCfg = ctx.Config.Bind("Runs", "RunLineCheckpoints", MarkSpec.Default,
                "What the comparison line's checkpoint markers show: off, dot (dots only), or comma-separated label fields: time, delta, segment, speed, ch:<stat> (e.g. ch:Health), item:<item> (e.g. item:Soda).");
            // v0.24.316-333's default moves to the new one once (T-0400);
            // the marker keeps a later "time,delta" chosen by hand.
            ConfigEntry<bool> moved = ctx.Config.Bind("Runs", "RunLineCheckpointsMoved", false,
                "Set once the checkpoint labels' old default (time,delta) was moved to the new one; leave it.");
            string next = MarkSpec.MoveOldDefault(_markFieldsCfg.Value, moved.Value);
            if (!moved.Value) moved.Value = true;
            if (next != _markFieldsCfg.Value)
            {
                _markFieldsCfg.Value = next;
                ctx.Log.LogInfo("Run lines: checkpoint labels moved from the old default to " + next + ".");
            }
            _keepFailedCfg = ctx.Config.Bind("Runs", "KeepFailedRunLine", true,
                "Keep the line of the last run that did not finish (a restart, abort or death), in red, until the next one fails.");
        }

        // --- the checkpoint markers' choice (T-0388; Data/MarkLabels) ---
        // One choice for the comparison line; T-0374 (several lines) adds a
        // per-line override on top of it. Parsed when the config text
        // changes, so reads cost nothing per frame.
        private MarkSpec _markSpec = new MarkSpec();
        private string _markSpecRaw;
        private int _markSpecVer;
        private string _markKeepLabels = MarkSpec.Default;   // the fields to come back to from off / dots
        private string _markNoteText = "";
        private readonly GUIContent _markNote = new GUIContent("");
        private readonly GUIContent _markStatus = new GUIContent("");
        private Attempt _markStatusFor;
        private int _markStatusVer = -1, _markStatusCount = -1;
        private readonly List<string> _markChans = new List<string>();
        private readonly List<string> _markChanLabels = new List<string>();
        private Attempt _markChansFor;
        private int _markChansVer = -1;
        private readonly List<string> _markItems = new List<string>();
        private readonly List<string> _markItemLabels = new List<string>();
        private bool _markItemsOpen;
        private Vector2 _markItemsScroll;
        private float _markItemsH = 22f;            // the open list's height, last frame
        private const float MarkItemsMaxH = 3 * 22f; // at most three rows, then it scrolls
        private readonly GUIContent _markItemsFold = new GUIContent("");
        private static readonly GUIContent TipMarks = new GUIContent(
            "What the comparison line's checkpoint markers show: nothing, only the dots, or the dots with a label of the fields you pick.");
        private static readonly GUIContent TipMarkTime = new GUIContent("The comparison run's time at the checkpoint.");
        private static readonly GUIContent TipMarkDelta = new GUIContent("Your time against it, once your run is past the checkpoint (green ahead, red behind).");
        private static readonly GUIContent TipMarkSegment = new GUIContent("How long the comparison run took from the checkpoint before to this one.");
        private static readonly GUIContent TipMarkSpeed = new GUIContent("The comparison run's speed at the checkpoint.");
        private static readonly GUIContent TipMarkChannel = new GUIContent("The comparison run's value of this stat at the checkpoint.");
        private static readonly GUIContent TipMarkItem = new GUIContent("How many of this item the comparison run held at the checkpoint.");
        private static readonly GUIContent TipMarkItems = new GUIContent("Item counts for the label: every item the comparison run held.");

        private MarkSpec MarkSettings
        {
            get
            {
                string v = _markFieldsCfg == null ? null : _markFieldsCfg.Value;
                if (!string.Equals(v, _markSpecRaw))
                {
                    _markSpecRaw = v;
                    _markSpec = MarkSpec.Parse(v);
                    _markSpecVer++;
                    _markNoteText = "";
                }
                return _markSpec;
            }
        }

        private void SetMarkSpec(MarkSpec next)
        {
            MarkSpec was = MarkSettings;
            if (was.Labels) _markKeepLabels = was.Serialize();
            string text = next.Serialize();
            _markFieldsCfg.Value = text;
            Ctx.Log.LogInfo("Run lines: checkpoint markers set to " + text + ".");
        }

        private void SetMarkMode(MarkSpec.Kind mode)
        {
            string text = mode == MarkSpec.Kind.Labels ? _markKeepLabels : (mode == MarkSpec.Kind.Off ? "off" : "dot");
            SetMarkSpec(MarkSpec.Parse(text));
        }

        private void SetLineBehind(int i)
        {
            _lineBehindCfg.Value = BehindNames[i];
            Ctx.Log.LogInfo("Run lines: behind things set to " + BehindNames[i] + ".");
        }

        private void SetLineBySpeed(bool on)
        {
            _lineBySpeedCfg.Value = on;
            Ctx.Log.LogInfo("Run lines: comparison colour set to " + (on ? "by speed" : "one colour") + ".");
        }

        /// The ramp's legend: a bar of the buckets, slow to fast, with the
        /// comparison run's slowest and fastest speed under its ends.
        private float DrawSpeedLegend(float x, float y, float w)
        {
            float min = _speedLo, max = _speedHi;
            if (min != _legendMin || max != _legendMax)
            {
                _legendMin = min; _legendMax = max;
                _speedLegend.text = _referenceLine.Count < 2 ? "No comparison run yet."
                    : "slow " + min.ToString("0.0") + " m/s  -  fast " + max.ToString("0.0") + " m/s (the line's 5th to 95th percentile)";
            }
            if (_referenceLine.Count < 2) return y + UiText.Draw(x + 124f, y, Mathf.Max(60f, w - 124f), _speedLegend) + 4f;
            int n = ForestOverlay.Data.SpeedRamp.Buckets;
            float barW = Mathf.Clamp(w - 124f, 32f, 256f), cell = barW / n;
            Color keep = GUI.color;
            for (int i = 0; i < n; i++)
            {
                float r, g, b;
                ForestOverlay.Data.SpeedRamp.Colour(i, out r, out g, out b);
                GUI.color = new Color(r, g, b, keep.a);
                WinPx.DrawTexture(new Rect(x + 124f + i * cell, y + 2, cell + 0.5f, 10), Texture2D.whiteTexture);
            }
            GUI.color = keep;
            y += 14f;
            y += UiText.Draw(x + 124f, y, Mathf.Max(60f, w - 124f), _speedLegend);
            return y + 4f;
        }

        private void FlushLineOptions()
        {
            if (_lineWriteAt <= 0f || Time.unscaledTime < _lineWriteAt) return;
            _lineWriteAt = 0f;
            if (_lineOpacityNow >= 0f) { _lineOpacity.Value = _lineOpacityNow; _lineOpacityNow = -1f; }
            if (_lineAheadNow >= 0f) { _lineAhead.Value = _lineAheadNow; _lineAheadNow = -1f; }
            if (_replayOpacityNow >= 0f) { _replayOpacity.Value = _replayOpacityNow; _replayOpacityNow = -1f; }
            if (_lineWidthNow >= 0f) { _lineWidth.Value = _lineWidthNow; _lineWidthNow = -1f; }
        }

        /// The run lines' own options, indented under the Run lines switch
        /// and drawn only while it is on (they act on nothing else); returns
        /// the new y.
        private float DrawLineOptions(float x, float y, float w)
        {
            // Text rebuilt only when the value moves (OnGUI runs often).
            if (LineOpacity != _lineOpacityShown) { _lineOpacityShown = LineOpacity; _lineOpacityText.text = Mathf.RoundToInt(LineOpacity * 100f) + "%"; }
            if (LineAhead != _lineAheadShown) { _lineAheadShown = LineAhead; _lineAheadText.text = LineAhead.ToString("0") + " s"; }
            if (LineWidth != _lineWidthShown) { _lineWidthShown = LineWidth; _lineWidthText.text = LineWidth.ToString("0.#") + " px"; }

            w -= x;
            float sliderW = Mathf.Max(80f, w - 190f);
            WinPx.Label(new Rect(x, y, 120, 20), "Opacity");
            UiKit.Hint(new Rect(x, y, 120, 20), TipLineOpacity);
            float op = WinPx.HorizontalSlider(new Rect(x + 124f, y + 5, sliderW, 16), LineOpacity, 0.1f, 1f);
            if (Mathf.Abs(op - LineOpacity) > 0.004f)
            {
                _lineOpacityNow = Mathf.Round(op * 100f) / 100f;
                _lineWriteAt = Time.unscaledTime + 0.5f;
            }
            WinPx.Label(new Rect(x + 130f + sliderW, y, 60, 20), _lineOpacityText);
            y += 24f;

            WinPx.Label(new Rect(x, y, 120, 20), "Width");
            UiKit.Hint(new Rect(x, y, 120, 20), TipLineWidth);
            float wd = WinPx.HorizontalSlider(new Rect(x + 124f, y + 5, sliderW, 16), LineWidth, 1f, 8f);
            if (Mathf.Abs(wd - LineWidth) > 0.04f)
            {
                _lineWidthNow = Mathf.Round(wd * 2f) / 2f;
                _lineWriteAt = Time.unscaledTime + 0.5f;
            }
            WinPx.Label(new Rect(x + 130f + sliderW, y, 60, 20), _lineWidthText);
            y += 24f;

            WinPx.Label(new Rect(x, y, 120, 20), "Behind things");
            UiKit.Hint(new Rect(x, y, 120, 20), TipLineBehind);
            int behind = LineBehind;
            if (UiKit.Toggle(new Rect(x + 124f, y, 76, 20), behind == 0, " hidden") && behind != 0) SetLineBehind(0);
            if (UiKit.Toggle(new Rect(x + 204f, y, 66, 20), behind == 1, " faint") && behind != 1) SetLineBehind(1);
            if (UiKit.Toggle(new Rect(x + 274f, y, 130, 20), behind == 2, " drawn through") && behind != 2) SetLineBehind(2);
            y += 24f;

            WinPx.Label(new Rect(x, y, 120, 20), "Comparison colour");
            UiKit.Hint(new Rect(x, y, 120, 20), TipLineColour);
            bool bySpeed = _lineBySpeedCfg.Value;
            if (UiKit.Toggle(new Rect(x + 124f, y, 96, 20), !bySpeed, " one colour") && bySpeed) SetLineBySpeed(false);
            if (UiKit.Toggle(new Rect(x + 224f, y, 96, 20), bySpeed, " by speed") && !bySpeed) SetLineBySpeed(true);
            y += 24f;
            if (_lineBySpeedCfg.Value) y = DrawSpeedLegend(x, y, w);

            bool failed = UiKit.Toggle(new Rect(x, y, w, 20), _keepFailedCfg.Value, " Keep the last unfinished run's line (red)");
            if (failed != _keepFailedCfg.Value) _keepFailedCfg.Value = failed;
            y += 22f;

            bool ahead = UiKit.Toggle(new Rect(x, y, w, 20), _lineAheadOn.Value, " Comparison line: only the next few seconds of it");
            if (ahead != _lineAheadOn.Value) _lineAheadOn.Value = ahead;
            y += 22f;
            if (_lineAheadOn.Value)
            {
                WinPx.Label(new Rect(x, y, 120, 20), "Seconds ahead");
                float s = WinPx.HorizontalSlider(new Rect(x + 124f, y + 5, sliderW, 16), LineAhead, 1f, 60f);
                if (Mathf.Abs(s - LineAhead) > 0.05f)
                {
                    _lineAheadNow = Mathf.Round(s);
                    _lineWriteAt = Time.unscaledTime + 0.5f;
                }
                WinPx.Label(new Rect(x + 130f + sliderW, y, 60, 20), _lineAheadText);
                y += 24f;
            }
            y = DrawMarkOptions(x, y, w);
            return y + 4f;
        }

        /// Stats the labels can show: the short list (MarkSpec.Stats), then
        /// any chosen one outside it (so it can still be switched off);
        /// items: the comparison run's, then any chosen one it lacks.
        /// Rebuilt when the run or the choice changes.
        private void RefreshMarkChannels(MarkSpec sp)
        {
            if (ReferenceEquals(_markChansFor, _reference) && _markChansVer == _markSpecVer) return;
            _markChansFor = _reference;
            _markChansVer = _markSpecVer;
            _markChans.Clear();
            _markChanLabels.Clear();
            for (int i = 0; i < MarkSpec.Stats.Length; i++) _markChans.Add(MarkSpec.Stats[i]);
            for (int i = 0; i < sp.Channels.Count; i++)
            {
                bool have = false;
                for (int j = 0; j < _markChans.Count; j++)
                    if (string.Equals(_markChans[j], sp.Channels[i], System.StringComparison.OrdinalIgnoreCase)) have = true;
                if (!have) _markChans.Add(sp.Channels[i]);
            }
            for (int i = 0; i < _markChans.Count; i++) _markChanLabels.Add(" " + MarkSpec.StatName(_markChans[i]));

            MarkLabels.ItemNames(_reference, _markItems);
            for (int i = 0; i < sp.Items.Count; i++)
                if (_markItems.IndexOf(sp.Items[i]) < 0) _markItems.Add(sp.Items[i]);
            _markItemLabels.Clear();
            for (int i = 0; i < _markItems.Count; i++) _markItemLabels.Add(" " + _markItems[i]);
            _markItemsFold.text = " Items (" + sp.Items.Count + " of " + _markItems.Count + " on the label)";
        }

        /// A toggle in the wrapping fields row: moves to the next line when
        /// it does not fit (cx / y are advanced); returns its new state.
        private bool MarkFieldToggle(ref float cx, ref float y, float x0, float right, float wd, bool on, string text, GUIContent tip)
        {
            if (cx > x0 && cx + wd > right) { cx = x0; y += 22f; }
            Rect r = new Rect(cx, y, wd, 20);
            bool now = UiKit.Toggle(r, on, text);
            UiKit.Hint(r, tip);
            cx += wd + 4f;
            return now;
        }

        /// Runs -> Run options -> Checkpoints: off / dots only / dots and
        /// labels, then which fields the labels hold. Returns the new y.
        private float DrawMarkOptions(float x, float y, float w)
        {
            MarkSpec sp = MarkSettings;
            WinPx.Label(new Rect(x, y, 120, 20), "Checkpoints");
            UiKit.Hint(new Rect(x, y, 120, 20), TipMarks);
            MarkSpec.Kind mode = sp.Mode;
            if (UiKit.Toggle(new Rect(x + 124f, y, 56, 20), mode == MarkSpec.Kind.Off, " off") && mode != MarkSpec.Kind.Off) SetMarkMode(MarkSpec.Kind.Off);
            if (UiKit.Toggle(new Rect(x + 184f, y, 104, 20), mode == MarkSpec.Kind.Dots, " dots only") && mode != MarkSpec.Kind.Dots) SetMarkMode(MarkSpec.Kind.Dots);
            if (UiKit.Toggle(new Rect(x + 292f, y, 150, 20), mode == MarkSpec.Kind.Labels, " dots and labels") && mode != MarkSpec.Kind.Labels) SetMarkMode(MarkSpec.Kind.Labels);
            y += 24f;

            sp = MarkSettings;
            if (sp.Labels)
            {
                RefreshMarkChannels(sp);
                WinPx.Label(new Rect(x, y, 120, 20), "Label shows");
                float x0 = x + 124f, right = x + w, cx = x0;
                MarkSpec edit = null;
                bool v;
                v = MarkFieldToggle(ref cx, ref y, x0, right, 76f, sp.Time, " time", TipMarkTime);
                if (v != sp.Time) { edit = MarkSpec.Parse(sp.Serialize()); edit.Time = v; }
                v = MarkFieldToggle(ref cx, ref y, x0, right, 80f, sp.Delta, " delta", TipMarkDelta);
                if (v != sp.Delta) { edit = MarkSpec.Parse(sp.Serialize()); edit.Delta = v; }
                v = MarkFieldToggle(ref cx, ref y, x0, right, 96f, sp.Segment, " segment", TipMarkSegment);
                if (v != sp.Segment) { edit = MarkSpec.Parse(sp.Serialize()); edit.Segment = v; }
                v = MarkFieldToggle(ref cx, ref y, x0, right, 84f, sp.Speed, " speed", TipMarkSpeed);
                if (v != sp.Speed) { edit = MarkSpec.Parse(sp.Serialize()); edit.Speed = v; }
                for (int c = 0; c < _markChans.Count; c++)
                {
                    bool on = sp.HasChannel(_markChans[c]);
                    v = MarkFieldToggle(ref cx, ref y, x0, right, 34f + 8f * _markChans[c].Length, on, _markChanLabels[c], TipMarkChannel);
                    if (v != on) { edit = MarkSpec.Parse(sp.Serialize()); edit.SetChannel(_markChans[c], v); }
                }
                y += 24f;

                // Items: folded, a run can hold dozens.
                if (_markItems.Count > 0)
                {
                    Rect fr = new Rect(x0, y, 240, 20);
                    bool open = UiKit.Toggle(fr, _markItemsOpen, _markItemsFold);
                    UiKit.Hint(fr, TipMarkItems);
                    if (open != _markItemsOpen) _markItemsOpen = open;
                    y += 22f;
                    if (_markItemsOpen)
                    {
                        // Three rows at most so the rows below stay on screen.
                        float viewH = Mathf.Min(_markItemsH, MarkItemsMaxH);
                        Rect area = new Rect(x0, y, right - x0, viewH);
                        Rect content = new Rect(0, 0, right - x0 - 18f, _markItemsH);
                        _markItemsScroll = WinPx.BeginScrollView(area, _markItemsScroll, content);
                        float iy = 0f, icx = 0f;
                        for (int c = 0; c < _markItems.Count; c++)
                        {
                            bool on = sp.HasItem(_markItems[c]);
                            v = MarkFieldToggle(ref icx, ref iy, 0f, content.width, 34f + 8f * _markItems[c].Length, on, _markItemLabels[c], TipMarkItem);
                            if (v != on) { edit = MarkSpec.Parse(sp.Serialize()); edit.SetItem(_markItems[c], v); }
                        }
                        WinPx.EndScrollView();
                        _markItemsH = iy + 22f;
                        y += viewH + 4f;
                    }
                }
                if (edit != null)
                {
                    // At least one field: an empty label is "dots only".
                    if (edit.FieldCount == 0) _markNoteText = "Keep at least one field, or choose dots only.";
                    else { _markNoteText = ""; SetMarkSpec(edit); }
                }
            }

            // Why nothing (or less) shows, in words.
            int splits = _reference != null && _reference.Splits != null ? _reference.Splits.Length : 0;
            if (!ReferenceEquals(_markStatusFor, _reference) || _markStatusVer != _markSpecVer || _markStatusCount != _markCount)
            {
                _markStatusFor = _reference; _markStatusVer = _markSpecVer; _markStatusCount = _markCount;
                _markStatus.text = MarkLabels.Status(sp, _reference, _markCount, splits);
            }
            if (_markStatus.text.Length > 0) y += UiText.Draw(x, y, w, _markStatus) + 2f;
            if (_markNoteText.Length > 0)
            {
                if (_markNote.text != _markNoteText) _markNote.text = _markNoteText;
                y += UiText.Draw(x, y, w, _markNote) + 2f;
            }
            return y;
        }

        /// Ghost and replay's look: the ghost's switch and shape, then the
        /// opacity the ghost and the replay share; returns the new y.
        private float DrawGhostOptions(float y, float w)
        {
            if (ReplayOpacity != _replayOpacityShown) { _replayOpacityShown = ReplayOpacity; _replayOpacityText.text = Mathf.RoundToInt(ReplayOpacity * 100f) + "%"; }

            Rect ghostR = new Rect(0, y, w, 22);
            bool ghost = UiKit.Toggle(ghostR, _ghostCfg.Value, TextGhost);
            if (ghost != _ghostCfg.Value) _ghostCfg.Value = ghost;
            UiKit.Hint(ghostR, TipGhost);
            y += 24f;

            y = DrawGhostLook(y, w);

            float sliderW = Mathf.Max(80f, w - 190f);
            WinPx.Label(new Rect(0, y, 120, 20), "Opacity");
            UiKit.Hint(new Rect(0, y, 120, 20), TipReplayOpacity);
            float op = WinPx.HorizontalSlider(new Rect(124, y + 5, sliderW, 16), ReplayOpacity, 0.1f, 1f);
            if (Mathf.Abs(op - ReplayOpacity) > 0.004f)
            {
                _replayOpacityNow = Mathf.Round(op * 100f) / 100f;
                _lineWriteAt = Time.unscaledTime + 0.5f;
            }
            WinPx.Label(new Rect(130f + sliderW, y, 60, 20), _replayOpacityText);
            return y + 26f;
        }
    }
}
