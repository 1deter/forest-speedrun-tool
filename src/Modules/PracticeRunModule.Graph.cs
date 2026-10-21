using System.Collections.Generic;
using ForestOverlay.Core;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // The speed graph (T-0373; QA 2026-10-15): in the Runs tab, speed
    // against run time for the comparison run, the current run and the last
    // unfinished one, in their line colours. Hover reads every curve's speed
    // and the time there. Each pixel column keeps its min and max (no spike
    // falls between samples); a median-of-5 smooths the picture only
    // (Data/SpeedGraph, tested). Columns are cached and rebuilt a few times
    // a second, never per draw.
    // ------------------------------------------------------------------
    public sealed partial class PracticeRunModule
    {
        private const float GraphH = 120f, GraphRebuildEvery = 0.25f;
        // The line colours of Game/DebugDraw.cs (ReferenceColour, CurrentColour, FailedColour); keep in step.
        private static readonly Color GraphRef = new Color(0.35f, 0.75f, 1f, 1f);
        private static readonly Color GraphNow = new Color(1f, 0.95f, 0.35f, 1f);
        private static readonly Color GraphFailed = new Color(1f, 0.3f, 0.25f, 1f);
        private static readonly Color GraphBack = new Color(0f, 0f, 0f, 0.35f);
        private static readonly Color GraphGrid = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color GraphCursor = new Color(1f, 1f, 1f, 0.5f);

        // The last unfinished run's samples (the line keeps only points).
        private readonly List<RunSample> _failedSamples = new List<RunSample>();

        private sealed class GraphCurve
        {
            public float[] Lo = new float[0], Hi = new float[0], Smooth = new float[0];
            public bool Ok;
            public List<RunSample> Src;
            // What the columns were built from: rebuilt only when one changes.
            public int BuiltCols = -1;
            public float BuiltT = -1f, Peak;
            // The smoothed speeds (Median5) are made only when the data changes,
            // not when the time axis does. Version: a reused source list that
            // got new contents (the failed copy).
            public int Version, SmoothVersion = -1, SmoothCount = -1;
            public List<RunSample> SmoothSrc;
        }
        private readonly GraphCurve _gRef = new GraphCurve(), _gNow = new GraphCurve(), _gFailed = new GraphCurve();
        private float _graphNext, _graphT, _graphTop = 5f, _graphTopShown = 5f;
        private int _graphCols;
        private int _graphHoverCol = -1, _graphDataVer, _graphHoverVer = -1, _failedVersion;
        private readonly GUIContent _graphHoverText = new GUIContent("");
        private readonly GUIContent _graphTopText = new GUIContent("5");
        private static readonly GUIContent TextGraph = new GUIContent("Speed graph");
        private static readonly GUIContent TipGraph = new GUIContent("Speed against run time: the comparison run (blue), this run (yellow) and the last unfinished one (red). Hover to read them.");

        private void KeepFailedSamples()
        {
            _failedSamples.Clear();
            _failedSamples.AddRange(_recorder.Current.Samples);
            _failedVersion++;   // the list is reused: the graph's cache key
        }

        private float DrawGraphSection(float y, float w)
        {
            const float gx = 36f;
            float gw = Mathf.Floor(w - gx - 4f);
            if (gw < 60f) return y;
            int cols = (int)gw;

            List<RunSample> now = _recorder.State == RunRecorder.RunState.Running && _recorder.Current != null ? _recorder.Current.Samples : null;
            _gRef.Src = _reference != null ? _reference.Samples : null;
            _gNow.Src = now;
            _gFailed.Src = _keepFailedCfg.Value && _failedSamples.Count >= 2 ? _failedSamples : null;   // off = no red curve
            _gFailed.Version = _failedVersion;

            // The running curve grows: look for changes a few times a second;
            // a curve whose source, length, columns and time span are the same is left alone.
            if (cols != _graphCols || Time.unscaledTime >= _graphNext)
                RebuildGraph(cols);

            Rect g = new Rect(gx, y + 2f, gw, GraphH);
            bool any = _gRef.Ok || _gNow.Ok || _gFailed.Ok;

            if (Event.current.type == EventType.Repaint)
            {
                Texture2D t = Texture2D.whiteTexture;
                Color keep = GUI.color;
                GUI.color = GraphBack;
                WinPx.DrawTexture(g, t);
                GUI.color = GraphGrid;
                for (int i = 1; i < 4; i++)
                    WinPx.DrawTexture(new Rect(g.x, g.y + g.height * i / 4f, g.width, 1f), t);
                if (any)
                {
                    DrawCurve(_gRef, GraphRef, g, t);
                    DrawCurve(_gFailed, GraphFailed, g, t);
                    DrawCurve(_gNow, GraphNow, g, t);
                    if (_graphHoverCol >= 0)
                    {
                        GUI.color = GraphCursor;
                        WinPx.DrawTexture(new Rect(g.x + _graphHoverCol, g.y, 1f, g.height), t);
                    }
                }
                GUI.color = keep;
            }

            // Hover: the column under the mouse; the text is rebuilt only when it changes.
            int col = any && g.Contains(WinPx.Mouse) ? Mathf.Clamp((int)(WinPx.Mouse.x - g.x), 0, cols - 1) : -1;
            if (col != _graphHoverCol || (col >= 0 && _graphHoverVer != _graphDataVer))
            {
                _graphHoverCol = col;
                _graphHoverVer = _graphDataVer;
                _graphHoverText.text = col < 0 ? "" : HoverText(col, cols);   // built on a change only
            }

            WinPx.Label(new Rect(0f, g.y - 4f, gx - 2f, 18f), _graphTopText);
            WinPx.Label(new Rect(0f, g.yMax - 16f, gx - 2f, 18f), "0");
            y = g.yMax + 4f;

            LegendSwatch(y, 0f, GraphRef, _gRef.Ok); WinPx.Label(new Rect(14f, y, 90f, 20f), "comparison");
            LegendSwatch(y, 100f, GraphNow, _gNow.Ok); WinPx.Label(new Rect(114f, y, 80f, 20f), "this run");
            LegendSwatch(y, 190f, GraphFailed, _gFailed.Ok); WinPx.Label(new Rect(204f, y, 90f, 20f), "unfinished");
            y += 22f;

            if (!any)
                y += UiText.Draw(0f, y, w, "Nothing to draw yet: start a run, or finish one to compare against.");
            else if (_graphHoverCol >= 0)
                y += UiText.Draw(0f, y, w, _graphHoverText);
            else
                y += UiText.Draw(0f, y, w, "Hover the graph to read each curve (u/s) and the time.");
            return y + 4f;
        }

        private static void LegendSwatch(float y, float x, Color c, bool on)
        {
            if (Event.current.type == EventType.Repaint)
            {
                Color keep = GUI.color;
                c.a = on ? 1f : 0.3f;
                GUI.color = c;
                WinPx.DrawTexture(new Rect(x, y + 6f, 10f, 10f), Texture2D.whiteTexture);
                GUI.color = keep;
            }
        }

        private void RebuildGraph(int cols)
        {
            _graphCols = cols;
            _graphNext = Time.unscaledTime + GraphRebuildEvery;
            _graphT = Mathf.Max(LastT(_gRef.Src), Mathf.Max(LastT(_gNow.Src), LastT(_gFailed.Src)));
            bool changed = BuildCurve(_gRef, cols, _graphT);
            changed |= BuildCurve(_gNow, cols, _graphT);
            changed |= BuildCurve(_gFailed, cols, _graphT);
            if (changed) _graphDataVer++;
            float top = 0f;
            if (_gRef.Ok) top = Mathf.Max(top, _gRef.Peak);
            if (_gNow.Ok) top = Mathf.Max(top, _gNow.Peak);
            if (_gFailed.Ok) top = Mathf.Max(top, _gFailed.Peak);
            _graphTop = SpeedGraph.AxisTop(top);
            if (_graphTop != _graphTopShown) { _graphTopShown = _graphTop; _graphTopText.text = _graphTop.ToString("0"); }
        }

        private static float LastT(List<RunSample> s)
        {
            return s != null && s.Count > 0 ? s[s.Count - 1].T : 0f;
        }

        private static bool BuildCurve(GraphCurve c, int cols, float tMax)
        {
            int n = c.Src != null ? c.Src.Count : 0;
            bool dataSame = ReferenceEquals(c.Src, c.SmoothSrc) && n == c.SmoothCount && c.Version == c.SmoothVersion;
            if (dataSame && cols == c.BuiltCols && tMax == c.BuiltT) return false;
            if (!dataSame)
            {
                if (c.Smooth.Length < n) c.Smooth = new float[n + 256];
                if (n >= 2) SpeedGraph.Median5(c.Src, c.Smooth);
                c.SmoothSrc = c.Src; c.SmoothCount = n; c.SmoothVersion = c.Version;
            }
            c.BuiltCols = cols; c.BuiltT = tMax;
            if (c.Lo.Length < cols) { c.Lo = new float[cols]; c.Hi = new float[cols]; }
            c.Ok = SpeedGraph.Columns(c.Src, tMax, cols, c.Smooth, c.Lo, c.Hi);
            c.Peak = 0f;
            if (c.Ok)
                for (int i = 0; i < cols; i++) if (c.Hi[i] > c.Peak) c.Peak = c.Hi[i];
            return true;
        }

        private void DrawCurve(GraphCurve c, Color colour, Rect g, Texture2D t)
        {
            if (!c.Ok || c.Src == null || c.Src.Count == 0) return;
            GUI.color = colour;
            int last = SpeedGraph.ColumnOf(c.Src[c.Src.Count - 1].T, _graphT, _graphCols);
            for (int i = 0; i <= last && i < _graphCols; i++)
            {
                float yHi = g.yMax - g.height * Mathf.Clamp01(c.Hi[i] / _graphTop);
                float yLo = g.yMax - g.height * Mathf.Clamp01(c.Lo[i] / _graphTop);
                float h = yLo - yHi;
                if (h < 2f) { yHi -= (2f - h) * 0.5f; h = 2f; }   // a flat stretch still shows as a line
                WinPx.DrawTexture(new Rect(g.x + i, yHi, 1f, h), t);
            }
        }

        private string HoverText(int col, int cols)
        {
            float time = cols > 1 ? col * _graphT / (cols - 1) : 0f;
            string s = Format(time);
            float v;
            if (SpeedGraph.SpeedAt(_gRef.Src, time, out v)) s += "   comparison " + v.ToString("F1");
            if (SpeedGraph.SpeedAt(_gNow.Src, time, out v)) s += "   this run " + v.ToString("F1");
            if (SpeedGraph.SpeedAt(_gFailed.Src, time, out v)) s += "   unfinished " + v.ToString("F1");
            return s + "  u/s";
        }
    }
}
