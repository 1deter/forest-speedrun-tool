using ForestOverlay.Core;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // The replay bar (T-0369; QA 2026-10-15, from a replay viewer's
    // transport bar): while the replay camera plays, a card at the bottom
    // of the screen with no F2 window open. The replay time / duration,
    // play or paused, the speed and the view in a line; a timeline with
    // the playhead, a tall tick at each checkpoint the watched run reached
    // and a small one at each recorded interaction; the values the runner
    // picked (Data/ReplayValues, default the site's Basic) and, for a
    // recording, the keys held (Data/KeyTrack) - T-0344; buttons to play /
    // pause, step 1 s, change the speed and the view; a key hint. The
    // mouse is free while it shows (WantsMouse; the player is held anyway):
    // a click or drag on the timeline seeks. The keys keep working, Esc /
    // the key / the Runs tab button still stop it. Drawn in DrawScreen
    // inside the scaled HUD matrix, from text cached in RefreshCameraText.
    // ------------------------------------------------------------------
    public sealed partial class PracticeRunModule
    {
        private const float BarMaxW = 760f, BarPad = 8f, BarLine = 24f, TimelineH = 24f;

        private bool _barScrub;
        private float _barH = 130f;
        private readonly GUIContent _barInfo = new GUIContent("");
        // The watched run's chosen values at the playhead (T-0386, T-0344).
        private readonly GUIContent _barStats = new GUIContent("");
        private readonly ReplayValues.Cache _valuesCache = new ReplayValues.Cache();
        // A recording's keys held at the playhead (T-0344).
        private readonly GUIContent _barKeys = new GUIContent("");
        private readonly KeyTrack.Cache _keysCache = new KeyTrack.Cache();
        private static readonly GUIContent BarHint = new GUIContent(
            "Click or drag the timeline to seek. Keys: Space play / pause, Left / Right 1 s (Shift 5 s), " +
            "Up / Down speed, 1 2 3 4 or V view, R from the start, Esc stops. " +
            "Free view: W A S D fly, Q / E down / up, hold the right mouse button to look.");

        private static readonly Color BarTrack = new Color(1f, 1f, 1f, 0.16f);
        private static readonly Color BarPlayed = new Color(0.35f, 0.75f, 1f, 0.9f);
        private static readonly Color BarCheckpoint = new Color(1f, 0.85f, 0.3f, 1f);
        private static readonly Color BarEvent = new Color(1f, 1f, 1f, 0.5f);

        /// The bar shows (and claims the mouse) while the replay camera plays
        /// and no F2 window is open: the window owns the screen then.
        private bool BarShowing
        {
            get { return _camOn && Watched != null && Host != null && Host.UiVisible && !Host.AnyPanelOpen(); }
        }

        /// From DrawScreen, inside the scaled matrix.
        private void DrawReplayBar()
        {
            if (!BarShowing) { _barScrub = false; return; }
            Event e = Event.current;
            if (e == null) return;
            EnsureSplitStyles();
            UiScale ui = HudUi;

            float w = Mathf.Min(ui.VW - 16f, BarMaxW);
            float x = Mathf.Round((ui.VW - w) * 0.5f);
            float y = Mathf.Max(0f, Mathf.Round(ui.VH - _barH - 14f));
            float inner = w - BarPad * 2f;
            float left = x + BarPad;

            if (e.type == EventType.Repaint)
            {
                Color before = GUI.color;
                GUI.color = new Color(before.r, before.g, before.b, Mathf.Clamp(Opacity, 0.6f, 1f));
                WinPx.Box(new Rect(x, y, w, _barH), GUIContent.none, _panelStyle);
                GUI.color = before;
            }

            float cy = y + BarPad;
            cy += UiText.Draw(left, cy, inner, _barInfo);
            if (_barStats.text.Length > 0) cy += UiText.Draw(left, cy, inner, _barStats);
            if (_barKeys.text.Length > 0) cy += UiText.Draw(left, cy, inner, _barKeys);

            DrawTimeline(new Rect(left, cy + 2f, inner, TimelineH), e);
            cy += TimelineH + 8f;

            // Buttons: one row when it fits, else they wrap.
            float bx = left, right = left + inner;
            if (WinPx.Button(BarSlot(ref bx, ref cy, 84f, left, right), _clock.Paused ? "Play" : "Pause"))
            {
                _clock.TogglePause();
                Ctx.Log.LogInfo("Replay camera: bar " + (_clock.Paused ? "paused" : "playing") + " at " + _clock.T.ToString("0.00") + " s.");
            }
            if (WinPx.Button(BarSlot(ref bx, ref cy, 56f, left, right), "-1 s")) BarStep(-1f);
            if (WinPx.Button(BarSlot(ref bx, ref cy, 56f, left, right), "+1 s")) BarStep(1f);
            if (WinPx.Button(BarSlot(ref bx, ref cy, 68f, left, right), "slower")) { _clock.Slower(); BarSpeedLog(); }
            if (WinPx.Button(BarSlot(ref bx, ref cy, 68f, left, right), "faster")) { _clock.Faster(); BarSpeedLog(); }

            ReplayView v = _camView;
            if (UiKit.Toggle(BarSlot(ref bx, ref cy, 76f, left, right), v == ReplayView.Chase, " chase")) v = ReplayView.Chase;
            if (UiKit.Toggle(BarSlot(ref bx, ref cy, 112f, left, right), v == ReplayView.FirstPerson, " first person")) v = ReplayView.FirstPerson;
            if (UiKit.Toggle(BarSlot(ref bx, ref cy, 104f, left, right), v == ReplayView.Trajectory, " trajectory")) v = ReplayView.Trajectory;
            if (UiKit.Toggle(BarSlot(ref bx, ref cy, 64f, left, right), v == ReplayView.Free, " free")) v = ReplayView.Free;
            if (v != _camView) SetView(v);
            cy += BarLine + 2f;

            cy += UiText.DrawDim(left, cy, inner, BarHint);
            if (e.type == EventType.Repaint) _barH = cy + BarPad - y;
        }

        // The next control's rect, wrapping to a new row when it would pass `right`.
        private static Rect BarSlot(ref float bx, ref float cy, float bw, float left, float right)
        {
            if (bx > left && bx + bw > right) { bx = left; cy += BarLine + 2f; }
            Rect r = new Rect(bx, cy, bw, BarLine - 2f);
            bx += bw + 6f;
            return r;
        }

        private void BarStep(float seconds)
        {
            _clock.Seek(seconds);
            _camSnap = true;
            Ctx.Log.LogInfo("Replay camera: bar step " + seconds.ToString("+0;-0") + " s, at " + _clock.T.ToString("0.00") + " s.");
        }

        private void BarSpeedLog()
        {
            Ctx.Log.LogInfo("Replay camera: bar speed x" + _clock.Speed.ToString("0.##") + ".");
        }

        private void DrawTimeline(Rect r, Event e)
        {
            float duration = Mathf.Max(0.01f, _clock.Duration);
            Attempt src = Watched;

            // Input first, so a click lands where the playhead is drawn after.
            Rect hit = new Rect(r.x - 6f, r.y - 2f, r.width + 12f, r.height + 4f);
            if (e.type == EventType.MouseDown && e.button == 0 && hit.Contains(e.mousePosition))
            {
                _barScrub = true;
                BarSeekTo(e.mousePosition.x, r, duration);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _barScrub)
            {
                BarSeekTo(e.mousePosition.x, r, duration);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && _barScrub)
            {
                _barScrub = false;
                Ctx.Log.LogInfo("Replay camera: bar seek to " + _clock.T.ToString("0.00") + " s.");
                e.Use();
            }
            if (e.type != EventType.Repaint) return;

            Texture2D white = Texture2D.whiteTexture;
            Color before = GUI.color;
            float mid = r.y + r.height * 0.5f;
            GUI.color = BarTrack;
            WinPx.DrawTexture(new Rect(r.x, mid - 3f, r.width, 6f), white);

            float head = Mathf.Round(ReplayBar.PixelOf(_clock.T, duration, r.x, r.width));
            GUI.color = BarPlayed;
            WinPx.DrawTexture(new Rect(r.x, mid - 3f, Mathf.Max(0f, head - r.x), 6f), white);

            // Interactions: small ticks, one per pixel column.
            if (_replayMarkersCfg == null || _replayMarkersCfg.Value)
            {
                GUI.color = BarEvent;
                float lastCol = float.NaN;
                for (int i = 0; i < src.Events.Count; i++)
                {
                    float col = Mathf.Round(ReplayBar.PixelOf(src.Events[i].T, duration, r.x, r.width));
                    if (col == lastCol) continue;
                    lastCol = col;
                    WinPx.DrawTexture(new Rect(col, mid - 5f, 1f, 10f), white);
                }
            }

            // Checkpoints the watched run reached (its end is the timeline's end); a recording has none.
            float[] cps = _watchTake ? null : ReplayBar.TickTimes(src.Splits, SplitRows);
            if (cps != null)
            {
                GUI.color = BarCheckpoint;
                for (int i = 0; i < cps.Length; i++)
                {
                    float t = cps[i];
                    if (float.IsNaN(t) || t < 0f || t > duration) continue;
                    float col = Mathf.Round(ReplayBar.PixelOf(t, duration, r.x, r.width));
                    WinPx.DrawTexture(new Rect(col - 1f, r.y + 1f, 2f, r.height - 2f), white);
                }
            }

            GUI.color = Color.white;
            WinPx.DrawTexture(new Rect(head - 1f, r.y - 1f, 3f, r.height + 2f), white);
            GUI.color = before;
        }

        private void BarSeekTo(float mouseX, Rect r, float duration)
        {
            _clock.T = ReplayBar.TimeAt(mouseX, r.x, r.width, duration);
            _camSnap = true;
        }
    }
}
