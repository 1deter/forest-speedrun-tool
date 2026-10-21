using System.Collections;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // More than one comparison line (T-0374; author, 2026-10-16: "yes, and
    // all of them" - PB, last run, best segments, site runs). Compare to
    // still picks THE comparison (delta, splits, ghost, the blue line); up
    // to ExtraPicks.Max more runs are drawn beside it, each in its own
    // colour (Game/DebugDraw RunLineBehaviour.ExtraColours) with its name
    // as a tag along the line, and aiming the crosshair at any line - the
    // comparison's too - names it and the time it was at that spot.
    //
    // Kept apart like the other runners' runs (Site.cs): extras are drawn
    // only; they never join _attempts, the table or the ghost. The choice
    // is one config string (Data/ExtraPicks: pb, last, best, runner:<id>).
    // A run that is already drawn (the comparison itself, or an earlier
    // extra) is drawn once and the options say so.
    //
    // The aim is worked out ten times a second from the line's own points
    // (Data/LineAim: nearest to the screen centre, in camera space, no
    // physics), the tag and plate redrawn each repaint from the cached
    // result - nothing allocates per frame.
    // ------------------------------------------------------------------
    public sealed partial class PracticeRunModule
    {
        private const int XN = ExtraPicks.Max;
        private const int XMain = XN;   // the comparison line's slot in the aim arrays
        private const int XOk = 0, XNone = 1, XDup = 2, XLoading = 3, XNotOnBoard = 4, XFailed = 5;

        private ConfigEntry<string> _extraCfg;
        private List<string> _xPicks = new List<string>();
        private string _xRaw;
        private readonly Attempt[] _xRun = new Attempt[XN];
        private readonly string[] _xName = new string[XN];
        private readonly int[] _xState = new int[XN];
        private readonly LineBuffer[] _xBuf = new LineBuffer[XN];
        private readonly GUIContent[] _xTag = NewContents(XN);
        private readonly float[] _xTagW = new float[XN];
        private float _xResolveAt;
        private bool _xDirty = true;
        private int _xSig = -1;
        private readonly GUIContent _xStatus = new GUIContent("");
        private string _xNote = "";
        private readonly GUIContent _xNoteText = new GUIContent("");

        // The best-segments run: rebuilt only when the attempts change.
        private Attempt _xBest;
        private int _xBestCount = -1;
        private Attempt _xBestLast;
        private string _xBestSeg;

        // The site runs offered as toggles, and the downloads for them.
        private readonly List<GUIContent> _xOtherText = new List<GUIContent>();
        private bool _xOthersDirty = true;
        private string _xBoardKey = "";
        private readonly List<long> _xFetching = new List<long>();
        private readonly List<long> _xFailed = new List<long>();

        // The aim (ten times a second) and what the repaint draws from it.
        private float _xAimAt;
        private readonly Vector3[] _xAimPos = new Vector3[XN + 1];
        private readonly float[] _xAimPx = new float[XN + 1];
        private readonly float[] _xAimT = new float[XN + 1];
        private readonly bool[] _xTagOn = new bool[XN + 1];
        private readonly bool[] _xTagBlocked = new bool[XN + 1];
        private int _xPicked = -1, _xPickKey = -1;
        private readonly GUIContent _xPickText = new GUIContent("");
        private float _xPickW = -1f;
        private GUIStyle _xStyle, _xShadow;
        private readonly float[] _xCx = new float[XN], _xTop = new float[XN], _xW = new float[XN], _xH = new float[XN];
        private readonly bool[] _xShown = new bool[XN];
        private readonly int[] _xSlot = new int[XN];
        private readonly float[] _xFade = new float[XN];

        private const float PickPixels = 14f;   // how near the crosshair a line must be, px at 1080p
        private static readonly Color MainLineColour = new Color(0.35f, 0.75f, 1f, 1f);

        private static readonly GUIContent TipMoreLines = new GUIContent(
            "Draw more runs as lines beside the comparison, each in its own colour with its name along it. Aim the crosshair at a line to see whose it is and the time there.");
        private static readonly GUIContent TextXPb = new GUIContent(" your best");
        private static readonly GUIContent TextXLast = new GUIContent(" last run");
        private static readonly GUIContent TextXBest = new GUIContent(" best segments");
        private static readonly GUIContent TipXPb = new GUIContent("Your personal best on this spot, as a line.");
        private static readonly GUIContent TipXLast = new GUIContent("Your last finished run on this spot, as a line.");
        private static readonly GUIContent TipXBest = new GUIContent("Every stretch between checkpoints taken from the run you did it fastest in, joined into one line.");
        private static readonly GUIContent TipXSite = new GUIContent("This runner's best run on the website, as a line. Needs the website's list (Check now, or Settings > Network).");

        private void InitExtraLines(ModuleContext ctx)
        {
            _extraCfg = ctx.Config.Bind("Runs", "RunLineExtras", "",
                "Extra comparison lines drawn beside the comparison: comma-separated pb, last, best (best segments), runner:<id> (a runner on the website's list). Up to 6.");
        }

        // --- which runs the extras are, worked out a few times a second ---

        private Attempt ExtraBestSegments(Attempt pb)
        {
            int n = _attempts.Count;
            Attempt last = n > 0 ? _attempts[n - 1] : null;
            if (n != _xBestCount || !ReferenceEquals(last, _xBestLast) || _xBestSeg != _loadedSegmentId)
            {
                _xBestCount = n; _xBestLast = last; _xBestSeg = _loadedSegmentId;
                _xBest = pb == null || pb.Splits == null ? null : BestSegmentsRun.Build(_attempts, pb.Splits.Length);
            }
            return _xBest;
        }

        private void ResolveExtras()
        {
            string raw = _extraCfg.Value;
            bool picksChanged = !string.Equals(raw, _xRaw);
            if (picksChanged) { _xRaw = raw; _xPicks = ExtraPicks.Parse(raw); }
            float now = Time.unscaledTime;
            if (!picksChanged && !_xDirty && now < _xResolveAt) return;
            _xResolveAt = now + 0.25f;
            _xDirty = false;
            if (!string.Equals(_xBoardKey, _boardKey)) { _xBoardKey = _boardKey; _xFailed.Clear(); _xFetching.Clear(); }

            Attempt pb = _xPicks.Count > 0 ? RunCompare.Best(_attempts) : null;
            int sig = 0;
            for (int i = 0; i < XN; i++)
            {
                if (i >= _xPicks.Count) { SetExtraRun(i, null, "", XOk); continue; }
                string tok = _xPicks[i];
                Attempt run = null;
                string name = "";
                int state = XOk;
                if (tok == ExtraPicks.Pb) { run = pb; name = "Personal best"; }
                else if (tok == ExtraPicks.Last) { run = _attempts.Count > 0 ? _attempts[_attempts.Count - 1] : null; name = "Last run"; }
                else if (tok == ExtraPicks.Best) { run = ExtraBestSegments(pb); name = "Best segments"; }
                else if (tok.StartsWith(ExtraPicks.RunnerPrefix))
                {
                    string id = tok.Substring(ExtraPicks.RunnerPrefix.Length);
                    BoardEntry e = null;
                    for (int j = 0; j < _others.Count; j++) if (_others[j].RunnerId == id) e = _others[j];
                    if (e == null) { state = XNotOnBoard; name = "Runner"; }
                    else
                    {
                        name = e.Name;
                        if (!_runnerRuns.TryGetValue(e.RunId, out run))
                        {
                            run = null;
                            if (_xFailed.IndexOf(e.RunId) >= 0) state = XFailed;
                            else
                            {
                                state = XLoading;
                                StartExtraFetch(e);
                            }
                        }
                    }
                }
                else name = tok;

                if (state == XOk && run == null) state = XNone;
                if (state == XOk)
                {
                    bool dup = ReferenceEquals(run, _reference);
                    for (int j = 0; j < i && !dup; j++) if (ReferenceEquals(_xRun[j], run)) dup = true;
                    if (dup) { state = XDup; run = null; }
                }
                SetExtraRun(i, run, name, state);
                sig = sig * 6 + state;
            }

            if (picksChanged || sig != _xSig)
            {
                _xSig = sig;
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < _xPicks.Count; i++)
                {
                    string why = _xState[i] == XNone ? "no run to draw yet"
                               : _xState[i] == XDup ? "drawn already (it is the comparison or another extra)"
                               : _xState[i] == XLoading ? "downloading its run..."
                               : _xState[i] == XNotOnBoard ? "not on this spot's list"
                               : _xState[i] == XFailed ? "its run could not be downloaded" : null;
                    if (why == null) continue;
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(_xName[i]).Append(": ").Append(why);
                }
                _xStatus.text = sb.ToString();
            }
        }

        private void SetExtraRun(int i, Attempt run, string name, int state)
        {
            _xState[i] = state;
            if (_xName[i] != name) { _xName[i] = name; _xTag[i].text = name; _xTagW[i] = -1f; }
            if (ReferenceEquals(_xRun[i], run)) return;
            _xRun[i] = run;
            if (_xBuf[i] == null) _xBuf[i] = new LineBuffer();
            _xBuf[i].Clear();
            if (run != null) _xBuf[i].Sync(run.Samples);
            _xPickKey = -1;
        }

        private void StartExtraFetch(BoardEntry e)
        {
            if (e.RunId <= 0 || string.IsNullOrEmpty(SiteUrl) || _xFetching.IndexOf(e.RunId) >= 0) return;
            _xFetching.Add(e.RunId);
            Ctx.Runner.StartCoroutine(FetchExtraRunRoutine(_boardKey, e));
        }

        private IEnumerator FetchExtraRunRoutine(string key, BoardEntry e)
        {
            long code = 0;
            string body = null, error = null;
            yield return Ctx.Runner.StartCoroutine(WebRequest.Send("GET", SiteBoard.RunFileUrl(SiteUrl, e.RunId), null, null, null, 30f,
                delegate(long c, string b, string err) { code = c; body = b; error = err; }));
            _xFetching.Remove(e.RunId);
            if (key != _boardKey) yield break;

            Attempt a = code == 200 && body != null ? AttemptFormat.Parse(body.Replace("\r\n", "\n").Split('\n')) : null;
            if (a == null || a.Samples.Count == 0)
            {
                string why = code == 0 ? (error ?? "no answer") : code == 200 ? "not a run file" : "answer " + code;
                _xFailed.Add(e.RunId);
                _xDirty = true;
                Ctx.Log.LogWarning("Site run " + e.RunId + " (" + e.Name + "): " + why + " (as an extra line).");
                yield break;
            }
            _runnerRuns[e.RunId] = a;
            _xDirty = true;
            Ctx.Log.LogInfo("Site run " + e.RunId + " (" + e.Name + ", " + Format(a.Duration) + "): " + a.Samples.Count +
                            " samples - an extra line for '" + key + "'.");
        }

        /// From UpdateLines, with the lines on screen: the extras' points to
        /// the behaviour. `show` false = none this frame.
        private void UpdateExtraLines(bool show)
        {
            ResolveExtras();
            _lines.ExtraN = show ? _xPicks.Count : 0;
            _lines.ExtraOpacity = _camOn ? ReplayOpacity : LineOpacity;
            for (int i = 0; i < XN; i++)
            {
                bool has = _xRun[i] != null && _xBuf[i] != null;
                _lines.ExtraLine[i] = has ? _xBuf[i].Points : null;
                _lines.ExtraCount[i] = has ? _xBuf[i].Count : 0;
            }
        }

        private void ClearExtraLines()
        {
            if (_lines != null) _lines.ExtraN = 0;
            _xPicked = -1;
        }

        // --- the tags and the crosshair pick ---

        private void AimUpdate(Camera cam)
        {
            Transform t = cam.transform;
            Vector3 eye = t.position, fwd = t.forward, right = t.right, up = t.up;
            float k = cam.pixelHeight / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
            float near = cam.nearClipPlane * 1.05f;
            float best = float.MaxValue;
            int pick = -1;
            for (int i = 0; i <= XN; i++)
            {
                _xTagOn[i] = false;
                Vector3[] pts; float[] times; int s, e;
                if (i < XN)
                {
                    if (i >= _lines.ExtraN || _xRun[i] == null || _xBuf[i] == null) continue;
                    pts = _xBuf[i].Points; times = _xBuf[i].Times; s = 0; e = _xBuf[i].Count;
                }
                else
                {
                    pts = _referenceLine.Points; times = _referenceLine.Times;
                    s = _lines.ReferenceStart; e = _lines.ReferenceCount;
                }
                if (e - s < 2) continue;
                int idx; float u, d; Vector3 p;
                if (!LineAim.Nearest(pts, s, e, eye, fwd, right, up, near, out idx, out u, out p, out d)) continue;
                float px = d * k;
                _xAimPos[i] = p;
                _xAimPx[i] = px;
                _xAimT[i] = times[idx] + (times[idx + 1] - times[idx]) * u;
                if (i < XN && px <= cam.pixelHeight * 0.45f)
                {
                    _xTagOn[i] = true;
                    _xTagBlocked[i] = _lines.Behind != RunLineBehaviour.BehindThrough && SightCheck.Blocked(eye, p);
                }
                if (px < best) { best = px; pick = i; }
            }
            float tol = PickPixels * cam.pixelHeight / 1080f;
            _xPicked = pick >= 0 && best <= tol && !Cursor.visible ? pick : -1;
        }

        /// From DrawScreen: a name tag on each extra line near the view's
        /// centre, and under the crosshair the name of the line aimed at.
        private void DrawExtraLineLabels()
        {
            if (_lines == null || !_lines.Show || _lines.ExtraN == 0) return;
            Event ev = Event.current;
            if (ev == null || ev.type != EventType.Repaint) return;
            Camera cam = DrawTarget.View();
            if (cam == null) return;
            if (_xStyle == null)
            {
                _xStyle = UiKit.Style(GUI.skin.label);
                _xStyle.alignment = TextAnchor.LowerLeft;
                _xStyle.fontSize = 13;
                _xStyle.wordWrap = false;
                _xStyle.clipping = TextClipping.Overflow;
                _xStyle.padding = new RectOffset(0, 0, 0, 0);
                _xShadow = UiKit.Style(_xStyle);
            }
            float now = Time.unscaledTime;
            if (now >= _xAimAt || now < _xAimAt - 1f) { _xAimAt = now + 0.1f; AimUpdate(cam); }

            float scale = Screen.height / 1080f;
            Vector3 eye = cam.transform.position;
            float lift = (LineWidth * 0.5f + 4f) * scale + 4f;
            float baseA = Mathf.Clamp01(_lines.ExtraOpacity);
            int n = 0;
            for (int i = 0; i < _lines.ExtraN && i < XN; i++)
            {
                if (!_xTagOn[i] || _xRun[i] == null) continue;
                if (_xTagBlocked[i] && _lines.Behind == RunLineBehaviour.BehindHidden) continue;
                float fade = CheckpointMarks.Fade((_xAimPos[i] - eye).magnitude, RunLineBehaviour.DrawDistance, RunLineBehaviour.FadeShare);
                if (fade <= 0.02f) continue;
                Vector3 sp = cam.WorldToScreenPoint(_xAimPos[i]);
                if (sp.z <= 0f || sp.x < -50f || sp.x > Screen.width + 50f || sp.y < -20f || sp.y > Screen.height + 20f) continue;
                if (_xTagW[i] < 0f) _xTagW[i] = _xStyle.CalcSize(_xTag[i]).x;
                _xSlot[n] = i;
                _xFade[n] = fade * (_xTagBlocked[i] ? RunLineBehaviour.FaintShare : 1f) * baseA;
                _xCx[n] = sp.x;
                _xW[n] = _xTagW[i] + ReplayLabels.Padding;
                _xH[n] = ReplayLabels.LineHeight;
                _xTop[n] = Screen.height - sp.y - lift - ReplayLabels.LineHeight;
                _xShown[n] = true;
                n++;
            }
            if (n > 0)
            {
                ReplayLabels.Stack(_xCx, _xTop, _xW, _xH, _xShown, n, ReplayLabels.Gap);
                for (int s = 0; s < n; s++)
                {
                    int i = _xSlot[s];
                    DrawExtraText(_xCx[s] - _xTagW[i] * 0.5f, _xTop[s], _xTagW[i], _xH[s], _xTag[i], RunLineBehaviour.ExtraColours[i % RunLineBehaviour.ExtraColours.Length], _xFade[s]);
                }
            }

            // Under the crosshair: whose line, and the time there.
            int pk = _xPicked;
            if (pk < 0) return;
            if (pk < XN && (pk >= _lines.ExtraN || _xRun[pk] == null)) return;
            int key = (pk + 1) * 1000000 + (int)(_xAimT[pk] * 10f);
            if (key != _xPickKey)
            {
                _xPickKey = key;
                _xPickText.text = (pk < XN ? _xName[pk] : ComparisonName()) + "  " + Format(_xAimT[pk]);
                _xPickW = -1f;
            }
            if (_xPickW < 0f) _xPickW = _xStyle.CalcSize(_xPickText).x;
            Color c = pk < XN ? RunLineBehaviour.ExtraColours[pk % RunLineBehaviour.ExtraColours.Length] : MainLineColour;
            DrawExtraText(Screen.width * 0.5f - _xPickW * 0.5f, Screen.height * 0.5f + 28f * scale, _xPickW, ReplayLabels.LineHeight, _xPickText, c, 1f);
        }

        private void DrawExtraText(float x, float top, float w, float h, GUIContent text, Color colour, float alpha)
        {
            colour.a = alpha;
            _xStyle.normal.textColor = colour;
            _xShadow.normal.textColor = new Color(0f, 0f, 0f, 0.85f * alpha);
            Rect r = new Rect(x, top, w + 40f, h);   // wider than the text: never clipped
            WinPx.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, _xShadow);
            WinPx.Label(r, text, _xStyle);
        }

        // --- Runs -> Run options -> Run lines: More lines ---

        private bool ExtraToggle(ref float cx, ref float y, float x0, float right, float wd, string token, GUIContent text, GUIContent tip)
        {
            bool on = ExtraPicks.IndexOf(_xPicks, token) >= 0;
            bool now = MarkFieldToggle(ref cx, ref y, x0, right, wd, on, text.text, tip);
            if (now == on) return false;
            List<string> next = ExtraPicks.Parse(_extraCfg.Value);
            if (!ExtraPicks.Set(next, token, now))
            {
                _xNote = "Six extra lines at most - switch one off first.";
                return false;
            }
            _xNote = "";
            string value = ExtraPicks.Serialize(next);
            _extraCfg.Value = value;
            Ctx.Log.LogInfo("Run lines: extra line " + token + " " + (now ? "on" : "off") + " (now '" + value + "').");
            return true;
        }

        private float DrawExtraOptions(float x, float y, float w)
        {
            // Make sure the picks are current (the section can be drawn before the first tick).
            if (!string.Equals(_extraCfg.Value, _xRaw)) { _xRaw = _extraCfg.Value; _xPicks = ExtraPicks.Parse(_xRaw); }
            if (_xOthersDirty)
            {
                _xOthersDirty = false;
                _xOtherText.Clear();
                for (int i = 0; i < _others.Count; i++)
                    _xOtherText.Add(new GUIContent(" " + _others[i].Name + "  " + Format(_others[i].Duration)));
            }

            WinPx.Label(new Rect(x, y, 120, 20), "More lines");
            UiKit.Hint(new Rect(x, y, 120, 20), TipMoreLines);
            float x0 = x + 124f, right = x + w, cx = x0;
            ExtraToggle(ref cx, ref y, x0, right, 100f, ExtraPicks.Pb, TextXPb, TipXPb);
            ExtraToggle(ref cx, ref y, x0, right, 96f, ExtraPicks.Last, TextXLast, TipXLast);
            ExtraToggle(ref cx, ref y, x0, right, 126f, ExtraPicks.Best, TextXBest, TipXBest);
            for (int i = 0; i < _others.Count && i < _xOtherText.Count; i++)
            {
                if (string.IsNullOrEmpty(_others[i].RunnerId)) continue;
                ExtraToggle(ref cx, ref y, x0, right, 34f + 8f * _xOtherText[i].text.Length, ExtraPicks.RunnerToken(_others[i].RunnerId), _xOtherText[i], TipXSite);
            }
            y += 24f;
            if (_xStatus.text.Length > 0) y += UiText.DrawDim(x0, y, right - x0, _xStatus) + 2f;
            if (_xNote.Length > 0)
            {
                if (_xNoteText.text != _xNote) _xNoteText.text = _xNote;
                y += UiText.Draw(x0, y, right - x0, _xNoteText) + 2f;
            }
            return y;
        }
    }
}
