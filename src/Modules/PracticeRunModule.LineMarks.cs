using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Checkpoint markers on the comparison line (T-0371; QA 2026-10-15,
    // from a surf run-line tool's dots): a dot with a dark ring where the
    // comparison run reached each split and its end, drawn with the line
    // (Game/DebugDraw RunLineBehaviour), and over it a small label with
    // that split's time - plus your delta once your run is past it. The
    // dots follow the line (shown with it, faded and culled by distance,
    // only the stretch in view with "only the next few seconds"); the
    // labels are drawn in DrawScreen from text cached when it changes,
    // nearest first, and ones that would overlap stack upwards
    // (Data/ReplayLabels.Stack). Times and decimals as the splits panel's.
    // ------------------------------------------------------------------
    public sealed partial class PracticeRunModule
    {
        private Attempt _markSource;
        private int _markCount;
        private readonly float[] _markT = new float[CheckpointMarks.Max];
        private readonly float[] _markDelta = new float[CheckpointMarks.Max];   // what each label's text was built with
        private readonly float[] _markSeg = new float[CheckpointMarks.Max];
        private readonly GUIContent[] _markText = NewContents(CheckpointMarks.Max);    // the split time, white
        private readonly GUIContent[] _markDText = NewContents(CheckpointMarks.Max);   // your delta, in the panel's colour
        private readonly SplitColour[] _markColour = new SplitColour[CheckpointMarks.Max];
        private readonly float[] _markTW = new float[CheckpointMarks.Max];            // measured widths, < 0 = measure
        private readonly float[] _markDW = new float[CheckpointMarks.Max];
        private int _markDecimals = -1;

        // Per repaint, nearest first (no allocation).
        private readonly int[] _markOrder = new int[CheckpointMarks.Max];
        private readonly float[] _markDist = new float[CheckpointMarks.Max];
        private readonly float[] _markFade = new float[CheckpointMarks.Max];
        private readonly float[] _markX = new float[CheckpointMarks.Max];
        private readonly float[] _markTop = new float[CheckpointMarks.Max];
        private readonly float[] _markW = new float[CheckpointMarks.Max];
        private readonly float[] _markH = new float[CheckpointMarks.Max];
        private readonly bool[] _markShown = new bool[CheckpointMarks.Max];
        private GUIStyle _markStyle, _markShadow;

        /// From UpdateLines: the markers' places (when the comparison
        /// changes), which show, and their texts (when a delta, the
        /// decimals or the ahead-only stretch changes).
        private void UpdateLineMarks()
        {
            if (!ReferenceEquals(_markSource, _reference))
            {
                _markSource = _reference;
                _markCount = 0;
                _markDecimals = -1;
                if (_reference != null)
                {
                    _markCount = CheckpointMarks.Times(_reference.Splits, _reference.Duration, _markT);
                    int hint = 0;
                    for (int i = 0; i < _markCount; i++)
                    {
                        Vector3 p;
                        // Not found = no place on the line: never shown.
                        if (!RunCompare.PositionAt(_reference.Samples, _reference.Duration, _markT[i], ref hint, out p))
                        {
                            _markT[i] = float.NaN;
                            hint = 0;
                        }
                        _lines.MarkerPos[i] = p;
                        _markDelta[i] = float.NaN;
                        _markSeg[i] = float.NaN;
                    }
                }
                _lines.MarkerCount = _markCount;
            }

            bool running = _recorder.State == RunRecorder.RunState.Running;
            bool windowed = !_camOn && _showLines && _lineAheadOn.Value;
            float from = running ? _recorder.Elapsed : 0f;
            int td = _timeDecimals.Value, dd = _deltaDecimals.Value;
            bool decimalsMoved = td * 10 + dd != _markDecimals;
            _markDecimals = td * 10 + dd;

            for (int i = 0; i < _markCount; i++)
            {
                _lines.MarkerOn[i] = CheckpointMarks.Shown(_markT[i], windowed, from, from + LineAhead);

                // Your delta, once your run is past this split (not while
                // the replay camera plays: nobody is running then).
                float delta = float.NaN, seg = float.NaN;
                int row = CheckpointMarks.Row(i, _markCount, _times.Length);
                if (!_camOn && row >= 0 && !float.IsNaN(_times[row]))
                {
                    delta = _times[row] - _markT[i];
                    // This stretch's delta, for the panel's gaining / losing
                    // shade (the first stretch is the delta itself).
                    int prev = i > 0 ? CheckpointMarks.Row(i - 1, _markCount, _times.Length) : -1;
                    if (prev >= 0 && !float.IsNaN(_times[prev]))
                        seg = (_times[row] - _times[prev]) - (_markT[i] - _markT[i - 1]);
                }

                bool same = Same(delta, _markDelta[i]) && Same(seg, _markSeg[i]);
                if (same && !decimalsMoved && _markText[i].text.Length > 0) continue;
                _markDelta[i] = delta;
                _markSeg[i] = seg;
                _markText[i].text = CheckpointMarks.Text(_markT[i], float.NaN, td, dd);
                _markDText[i].text = float.IsNaN(delta) ? "" : SplitTable.Delta(delta, dd);
                // The panel's own rule for a delta's colour.
                _markColour[i] = SplitTable.ColourOf(delta, seg, float.NaN, float.NaN);
                _markTW[i] = -1f;
            }
        }

        private static bool Same(float a, float b)
        {
            return (float.IsNaN(a) && float.IsNaN(b)) || a == b;
        }

        private void ClearLineMarks()
        {
            _markSource = null;
            _markCount = 0;
            if (_lines != null) _lines.MarkerCount = 0;
        }

        /// From DrawScreen: the labels over the dots. Repaint only; the
        /// text is cached in UpdateLineMarks.
        private void DrawLineMarkLabels()
        {
            if (_markCount == 0 || _lines == null || !_lines.Show) return;
            if (_lines.ReferenceCount - _lines.ReferenceStart < 2 || _lines.ReferenceOpacity <= 0.01f) return;
            Event e = Event.current;
            if (e == null || e.type != EventType.Repaint) return;
            Camera cam = DrawTarget.View();
            if (cam == null) return;
            if (_markStyle == null)
            {
                _markStyle = UiKit.Style(GUI.skin.label);
                _markStyle.alignment = TextAnchor.LowerLeft;
                _markStyle.fontSize = 13;
                _markStyle.wordWrap = false;
                _markStyle.clipping = TextClipping.Overflow;
                _markStyle.padding = new RectOffset(0, 0, 0, 0);
                _markShadow = UiKit.Style(_markStyle);
            }

            // Which are in view, nearest first (insertion into the order).
            Vector3 eye = cam.transform.position;
            float lift = (RunLineBehaviour.DotRadius + RunLineBehaviour.DotRing + 3f) * Screen.height / 1080f;
            int n = 0;
            for (int i = 0; i < _markCount; i++)
            {
                if (!_lines.MarkerOn[i]) continue;
                float dist = (_lines.MarkerPos[i] - eye).magnitude;
                float fade = CheckpointMarks.Fade(dist, RunLineBehaviour.DrawDistance, RunLineBehaviour.FadeShare);
                if (fade <= 0.02f) continue;
                Vector3 sp = cam.WorldToScreenPoint(_lines.MarkerPos[i]);
                if (sp.z <= 0f || sp.x < -100f || sp.x > Screen.width + 100f || sp.y < -50f || sp.y > Screen.height + 50f) continue;

                int at = n++;
                while (at > 0 && _markDist[at - 1] > dist)
                {
                    CopySlot(at, at - 1);
                    at--;
                }
                _markOrder[at] = i;
                _markDist[at] = dist;
                _markFade[at] = fade;
                _markX[at] = sp.x;
                _markH[at] = ReplayLabels.LineHeight;
                _markTop[at] = Screen.height - sp.y - lift - ReplayLabels.LineHeight;
                if (_markTW[i] < 0f)
                {
                    _markTW[i] = _markStyle.CalcSize(_markText[i]).x;
                    _markDW[i] = _markDText[i].text.Length > 0 ? _markStyle.CalcSize(_markDText[i]).x : 0f;
                }
                _markW[at] = _markTW[i] + (_markDW[i] > 0f ? DeltaGap + _markDW[i] : 0f) + ReplayLabels.Padding;
                _markShown[at] = true;
            }
            if (n == 0) return;

            ReplayLabels.Stack(_markX, _markTop, _markW, _markH, _markShown, n, ReplayLabels.Gap);
            for (int s = 0; s < n; s++)
            {
                int m = _markOrder[s];
                float a = _markFade[s] * Mathf.Clamp01(_lines.ReferenceOpacity);
                _markShadow.normal.textColor = new Color(0f, 0f, 0f, 0.85f * a);
                // The time, then your delta right after it in the panel's colour;
                // the pair centred on the dot, each piece wider than its text.
                float tw = _markTW[m], dw = _markDW[m];
                float x0 = _markX[s] - (tw + (dw > 0f ? DeltaGap + dw : 0f)) * 0.5f;
                DrawMarkPiece(x0, _markTop[s], tw, _markH[s], _markText[m], Color.white, a);
                if (dw > 0f)
                {
                    Color c = SplitPalette[(int)_markColour[m]];
                    DrawMarkPiece(x0 + tw + DeltaGap, _markTop[s], dw, _markH[s], _markDText[m], c, a);
                }
            }
        }

        private const float DeltaGap = 6f;

        private void DrawMarkPiece(float x, float top, float w, float h, GUIContent text, Color colour, float alpha)
        {
            colour.a = alpha;
            _markStyle.normal.textColor = colour;
            Rect r = new Rect(x, top, w + 40f, h);   // wider than the text: never clipped
            WinPx.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, _markShadow);
            WinPx.Label(r, text, _markStyle);
        }

        private void CopySlot(int to, int from)
        {
            _markOrder[to] = _markOrder[from];
            _markDist[to] = _markDist[from];
            _markFade[to] = _markFade[from];
            _markX[to] = _markX[from];
            _markTop[to] = _markTop[from];
            _markW[to] = _markW[from];
            _markH[to] = _markH[from];
            _markShown[to] = _markShown[from];
        }
    }
}
