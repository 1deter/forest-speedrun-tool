using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using ForestOverlay.Core;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Input overlay (T-0302; author, 2026-10-11): the keys and mouse buttons
    // you press, drawn on the game screen, modelled on girlglock/input-overlay.
    // It follows the HUD's show / hide (F5 hides it), like the splits panel.
    //
    // - Fully modular: every tile (a key, the mouse box, the wheel) is placed
    //   on its own, shown or hidden, sized on its own (Data/InputLayout). The
    //   default is W A S D, Shift, Space, Ctrl, M1, M2 and the mouse box.
    // - Physical keys (UnityEngine.Input.GetKey): what the fingers did,
    //   whatever the game's bindings. Unity gives digital keys, so there is
    //   no analogue fill - a press fills the tile in the accent colour with
    //   a short pop, as the reference does for a digital key.
    // - Nothing is drawn while a cursor shows (menus, inventory, the F2
    //   window) - except in Edit HUD, where the tiles can be placed and
    //   light up for trying.
    // - Set up where it is shown (Edit HUD > Panels), saved in the HUD
    //   profile (@Inputs.On, @Inputs.Layout).
    // - Sampled in Tick (one Input.GetKey per tile, no allocation), drawn on
    //   Repaint only from cached contents. Off = nothing sampled or drawn.
    //
    // Writes no game state: not practice-only, available in run mode.
    // ------------------------------------------------------------------
    public sealed class InputOverlayModule : OverlayModule
    {
        public override string Id { get { return "inputs"; } }
        public override string DisplayName { get { return "Input overlay"; } }

        private const float PopAmount = 0.1f, RiseTime = 0.1f, FallTime = 0.2f, MinLit = 0.05f;
        private const float TrailSeconds = 0.6f, MouseGain = 0.02f, TrailSpacing = 0.08f;
        private const int TrailMax = 48;

        private sealed class TileState
        {
            public InputTile Tile;
            public KeyCode Code;
            public bool Valid;
            public bool Special;     // the mouse box or the wheel
            public float Level;      // 0 idle .. 1 pressed (the animation)
            public float LitUntil;
            public bool Active;      // held (or moving, for the mouse box) this frame: the dynamic mode draws only these
            public GUIContent Label, Name;
            public string SnapId;
            public Rect Rect;        // as last drawn (virtual pixels)
        }

        private ConfigEntry<bool> _on;
        private ConfigEntry<bool> _onlyPressed;
        private bool _mouseMoved;
        private ConfigEntry<string> _layoutCfg;
        private InputLayout _layout = new InputLayout();
        private readonly List<TileState> _states = new List<TileState>();

        // The mouse box: where the mouse has "gone" (wrapping at the edges)
        // and its fading trail.
        private float _mx, _my;
        private readonly float[] _trailX = new float[TrailMax], _trailY = new float[TrailMax], _trailT = new float[TrailMax];
        private int _trailHead, _trailCount;
        private float _lastTx, _lastTy;
        private bool _axisBroken;
        private bool _axisTold;

        private static readonly KeyCode[] AllKeys = (KeyCode[])Enum.GetValues(typeof(KeyCode));

        // Edit HUD state.
        private bool _capturing;
        private int _drag = -1;
        private Vector2 _dragOffset;
        private int _removeAt = -1;
        private bool _resetArmed;
        private float _resetUntil;
        private readonly GUIContent _status = new GUIContent("");

        private GUIStyle _idle, _lit;

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);
            _on = ctx.Config.Bind("Inputs", "On", false, "Show the input overlay: the keys and mouse buttons you press, on the game screen.");
            _onlyPressed = ctx.Config.Bind("Inputs", "OnlyPressed", false, "Show a tile only while its key is held (exact, no fade). Edit HUD still shows every tile.");
            _layoutCfg = ctx.Config.Bind("Inputs", "Layout", DefaultLayout().Format(),
                "The input overlay's tiles: key, x, y, size, shown (1/0), separated by ';'. Set up in Edit HUD.");
            HudWidgets widgets = Host.Hud.Widgets;
            widgets.RegisterProfileEntry(_on, null);
            widgets.RegisterProfileEntry(_onlyPressed, null);
            widgets.RegisterProfileEntry(_layoutCfg, delegate (object v)
            {
                _layoutCfg.BoxedValue = v;
                Reload();
            }, ProfileUnit.InputTiles);
            Reload();
        }

        private InputLayout DefaultLayout()
        {
            UiScale ui = Host.Hud.Settings.Scale();
            return ui.HasReference ? InputLayout.Default(ui.RefW, ui.RefH) : InputLayout.Default();
        }

        private void Reload()
        {
            _layout = InputLayout.Parse(_layoutCfg.Value);
            _states.Clear();
            for (int i = 0; i < _layout.Tiles.Count; i++) _states.Add(MakeState(_layout.Tiles[i], i));
            _drag = -1;
        }

        private TileState MakeState(InputTile t, int index)
        {
            TileState s = new TileState();
            s.Tile = t;
            s.SnapId = "input" + index + t.Key;
            string label = InputLayout.LabelFor(t.Key);
            s.Label = new GUIContent(label);
            s.Name = new GUIContent(t.Key == InputLayout.MouseKey ? "Mouse box" : t.Key == InputLayout.WheelKey ? "Mouse wheel" : label);
            if (t.Key == InputLayout.MouseKey || t.Key == InputLayout.WheelKey) { s.Valid = true; s.Special = true; return s; }
            try
            {
                s.Code = (KeyCode)Enum.Parse(typeof(KeyCode), t.Key);
                s.Valid = true;
            }
            catch (Exception) { s.Valid = false; }   // a key a newer version knew: kept in the file, not drawn
            return s;
        }

        private bool Editing { get { return Host != null && Host.Hud.Widgets != null && Host.Hud.Widgets.Editing; } }

        // --- sampling (Tick) -------------------------------------------------------------

        // The old default sat on the game's bottom-left messages: a layout
        // never moved goes to the new one (author, 2026-10-15, T-0325). On
        // the first Tick, after the active HUD profile was put in (it holds
        // the layout too), so the profile is written with the new one.
        private bool _oldDefaultChecked;

        private void MoveOldDefault()
        {
            _oldDefaultChecked = true;
            UiScale ui = Host.Hud.Settings.Scale();
            if (!InputLayout.IsOldDefault(_layoutCfg.Value, ui.HasReference ? ui.RefW : 1920, ui.HasReference ? ui.RefH : 1080)) return;
            _layoutCfg.Value = DefaultLayout().Format();
            Reload();
            Ctx.Log.LogInfo("Inputs: the old default layout moved to bottom centre (clear of the game's messages).");
        }

        public override void Tick()
        {
            if (!_oldDefaultChecked) MoveOldDefault();
            if (_on == null || !_on.Value) { _capturing = false; return; }
            bool editing = Editing;
            if (!editing) _capturing = false;
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            bool live = editing || !Cursor.visible;
            if (_capturing) CaptureKey();

            float scroll = live ? Input.mouseScrollDelta.y : 0f;
            for (int i = 0; i < _states.Count; i++)
            {
                TileState s = _states[i];
                if (!s.Valid || !s.Tile.Shown) { s.Level = 0f; s.Active = false; continue; }
                bool down = false;
                if (live)
                {
                    if (!s.Special) down = Input.GetKey(s.Code);
                    else if (s.Tile.Key == InputLayout.WheelKey) down = scroll != 0f;
                }
                s.Active = down;
                if (_onlyPressed.Value && !editing) { s.LitUntil = 0f; s.Level = down ? 1f : 0f; continue; }   // frame-true: no latch, no animation
                if (down) s.LitUntil = now + MinLit;
                bool lit = down || now < s.LitUntil;
                if (lit) s.Level = Mathf.Min(1f, s.Level + dt / RiseTime);
                else s.Level = Mathf.Max(0f, s.Level - dt / FallTime);
            }
            _mouseMoved = false;
            if (HasShownMouseBox()) SampleMouse(live, now);
            if (_onlyPressed.Value && !editing)
                for (int i = 0; i < _states.Count; i++)
                    if (_states[i].Special && _states[i].Tile.Key == InputLayout.MouseKey) _states[i].Active = _mouseMoved;
        }

        private bool HasShownMouseBox()
        {
            for (int i = 0; i < _states.Count; i++)
                if (_states[i].Special && _states[i].Tile.Key == InputLayout.MouseKey && _states[i].Tile.Shown) return true;
            return false;
        }

        private void SampleMouse(bool live, float now)
        {
            if (!live || _axisBroken) return;
            float dx, dy;
            try
            {
                dx = Input.GetAxisRaw("Mouse X");
                dy = Input.GetAxisRaw("Mouse Y");
            }
            catch (Exception ex)
            {
                _axisBroken = true;
                if (!_axisTold)
                {
                    _axisTold = true;
                    Ctx.Log.LogWarning("Inputs: the mouse box cannot read the mouse (" + ex.Message + ") - it stays still.");
                }
                return;
            }
            if (dx == 0f && dy == 0f) return;
            _mouseMoved = true;
            _mx = Wrap(_mx + dx * MouseGain);
            _my = Wrap(_my - dy * MouseGain);   // up on the mouse is up on the screen
            // A point only once the mouse has gone a little way from the last one,
            // so a slow move leaves a trail instead of dots stacked under the head.
            float tx = _mx - _lastTx, ty = _my - _lastTy;
            if (tx * tx + ty * ty < TrailSpacing * TrailSpacing) return;
            _lastTx = _mx;
            _lastTy = _my;
            _trailX[_trailHead] = _mx;
            _trailY[_trailHead] = _my;
            _trailT[_trailHead] = now;
            _trailHead = (_trailHead + 1) % TrailMax;
            if (_trailCount < TrailMax) _trailCount++;
        }

        private static float Wrap(float v)
        {
            while (v > 1f) v -= 2f;
            while (v < -1f) v += 2f;
            return v;
        }

        /// "+ Key": the next key pressed becomes a tile.
        private void CaptureKey()
        {
            if (!Input.anyKeyDown) return;
            for (int i = 0; i < AllKeys.Length; i++)
            {
                KeyCode k = AllKeys[i];
                if (!Input.GetKeyDown(k)) continue;
                if (k == KeyCode.Mouse0 || k == KeyCode.Mouse1) continue;   // the clicks that run this window
                _capturing = false;
                if (k == KeyCode.Escape) { SetStatus("Cancelled."); return; }
                string name = k.ToString();
                if (name.StartsWith("Joystick", StringComparison.Ordinal)) { SetStatus("Gamepad buttons are not shown."); return; }
                AddTile(name);
                return;
            }
        }

        // --- changes (Edit HUD) ----------------------------------------------------------------

        private void SetStatus(string text)
        {
            _status.text = text;
        }

        private void Commit()
        {
            _layoutCfg.Value = _layout.Format();
        }

        private void AddTile(string key)
        {
            if (_layout.Find(key) != null) { SetStatus(InputLayout.LabelFor(key) + " is already there."); return; }
            UiScale ui = Host.Hud.Settings.Scale();
            // Bottom right, clear of the Edit HUD window, in the first free slot so new tiles do not stack
            // (author, 2026-10-15, T-0355).
            float nx, ny;
            _layout.FreeSlot(key, ui.RefW, ui.RefH, out nx, out ny);
            InputTile t = _layout.Add(key, nx, ny);
            if (t == null) { SetStatus("No room for another tile."); return; }
            _states.Add(MakeState(t, _states.Count));
            Commit();
            SetStatus("Added " + InputLayout.LabelFor(key) + " at the bottom right of the screen: drag it where you want it.");
            Ctx.Log.LogInfo("Inputs: tile " + key + " added.");
        }

        private void RemoveTile(int i)
        {
            string key = _states[i].Tile.Key;
            _layout.Remove(key);
            Commit();
            Reload();
            Ctx.Log.LogInfo("Inputs: tile " + key + " removed.");
        }

        private void SetShown(TileState s, bool shown)
        {
            if (s.Tile.Shown == shown) return;
            s.Tile.Shown = shown;
            Commit();
            Ctx.Log.LogInfo("Inputs: tile " + s.Tile.Key + (shown ? " shown." : " hidden."));
        }

        private void SetScale(TileState s, float scale)
        {
            float v = InputLayout.ClampScale(Mathf.Round(scale * 4f) / 4f);
            if (v == s.Tile.Scale) return;
            s.Tile.Scale = v;
            Commit();
            Ctx.Log.LogInfo("Inputs: tile " + s.Tile.Key + " scaled to " + v.ToString("0.##") + ".");
        }

        private void ResetLayout()
        {
            _layoutCfg.Value = DefaultLayout().Format();
            Reload();
            SetStatus("Back to the default layout.");
            Ctx.Log.LogInfo("Inputs: layout reset to the default.");
        }

        // --- drawing -----------------------------------------------------------------------------

        private void EnsureStyles()
        {
            if (_idle != null) return;
            {
                GUIStyle idle = UiKit.Style(GUI.skin.label);
                idle.alignment = TextAnchor.MiddleCenter;
                idle.fontSize = LabelBase;
                idle.fontStyle = FontStyle.Bold;
                idle.padding = new RectOffset(0, 0, 0, 0);
                idle.wordWrap = false;
                idle.clipping = TextClipping.Clip;
                idle.normal.textColor = UiKit.TextColour;
                GUIStyle lit = UiKit.Style(idle);
                lit.normal.textColor = UiKit.OnAccent;
                _idle = idle;
                _lit = lit;
            }
        }

        private const int LabelBase = 13;   // the label's size at scale 1

        // The label is one ratio to its tile at every size (T-0363).
        private void SizeLabels(float scale)
        {
            int size = Mathf.Max(6, Mathf.RoundToInt(LabelBase * scale));
            if (_idle.fontSize != size) { _idle.fontSize = size; _lit.fontSize = size; }
        }

        private Rect TileRect(TileState s, UiScale ui)
        {
            InputTile t = s.Tile;
            float w = InputLayout.TileW(t.Key, t.Scale), h = InputLayout.TileH(t.Key, t.Scale);
            float x = Mathf.Clamp(ui.PosX(t.X), 0f, Mathf.Max(0f, ui.VW - w));
            float y = Mathf.Clamp(ui.PosY(t.Y), 0f, Mathf.Max(0f, ui.VH - h));
            return new Rect(x, y, w, h);
        }

        public override void DrawScreen()
        {
            if (_on == null || !_on.Value || _states.Count == 0) return;
            bool editing = Editing;
            if (!editing && Cursor.visible) { if (_drag >= 0) _drag = -1; return; }
            if (!editing && _drag >= 0) _drag = -1;

            Matrix4x4 outer = Host.Hud.Settings.BeginScaled();
            try
            {
                UiKit.Ensure();
                EnsureStyles();
                UiScale ui = Host.Hud.Settings.Scale();
                Event e = Event.current;
                for (int i = 0; i < _states.Count; i++)
                {
                    TileState s = _states[i];
                    if (!s.Valid) continue;
                    if (_drag != i) s.Rect = TileRect(s, ui);
                }
                if (editing && e != null) EditEvents(ui, e);
                if (e == null || e.type != EventType.Repaint) return;

                float now = Time.unscaledTime;
                Color before = GUI.color;
                for (int i = 0; i < _states.Count; i++)
                {
                    TileState s = _states[i];
                    if (!s.Valid) continue;
                    bool hidden = !s.Tile.Shown;
                    if (hidden && !editing) continue;
                    if (!editing && _onlyPressed.Value && !s.Active) continue;   // vanish: nothing on screen while not pressed
                    DrawTile(s, hidden, editing, now, before);
                    if (editing) Host.Hud.Widgets.ReportPiece(s.SnapId, s.Rect);
                }
                GUI.color = before;
            }
            finally { GUI.matrix = outer; }
        }

        private void DrawTile(TileState s, bool hidden, bool editing, float now, Color before)
        {
            float a = hidden ? 0.35f : 1f;
            float level = s.Level;
            float pop = _onlyPressed.Value && !editing ? 0f : PopAmount * level;
            Rect r = s.Rect;
            r = new Rect(r.x - r.width * pop * 0.5f, r.y - r.height * pop * 0.5f, r.width * (1f + pop), r.height * (1f + pop));

            GUI.color = new Color(before.r, before.g, before.b, before.a * a);
            float bw = Thick(BorderPx * s.Tile.Scale);
            Flat(r, UiKit.WidgetEdge);
            Flat(Inset(r, bw), UiKit.WidgetFill);
            if (level > 0.01f)
            {
                GUI.color = new Color(before.r, before.g, before.b, before.a * a * level);
                Flat(r, UiKit.Accent);
            }

            if (s.Tile.Key == InputLayout.MouseKey) DrawMouseBox(r, s.Tile.Scale, now, before, a);
            else
            {
                SizeLabels(s.Tile.Scale);
                GUI.color = new Color(before.r, before.g, before.b, before.a * a * (1f - level));
                if (level < 0.99f) CrispText.Label(r, s.Label, _idle);
                if (level > 0.01f)
                {
                    GUI.color = new Color(before.r, before.g, before.b, before.a * a * level);
                    CrispText.Label(r, s.Label, _lit);
                }
            }
            if (editing)
            {
                GUI.color = new Color(before.r, before.g, before.b, before.a);
                WinPx.Box(s.Rect, GUIContent.none, UiKit.Outline);
            }
        }

        // Square-cornered, crisp drawing (T-0363): a flat colour on the white
        // texture, no rounded 9-slice edge to blur. Every size below is a
        // constant times the tile's scale, so tile, border, dot and line keep
        // one ratio at every size.
        private const float BorderPx = 1.5f, LineThickness = 2f, DotSize = 8f;

        private static Rect Inset(Rect r, float d)
        {
            return new Rect(r.x + d, r.y + d, Mathf.Max(0f, r.width - d * 2f), Mathf.Max(0f, r.height - d * 2f));
        }

        // Whole screen pixels under the HUD's scale (T-0363): a border is the
        // same width on every tile, never 1 px on one and 2 on the next.
        private static float Fx() { float f = GUI.matrix.m00; return f > 0.01f ? f : 1f; }

        private static float Thick(float v)
        {
            float f = Fx();
            return Mathf.Max(1f, Mathf.Round(v * f)) / f;
        }

        private static Rect SnapRect(Rect r)
        {
            float f = Fx();
            float x0 = Mathf.Round(r.x * f) / f, y0 = Mathf.Round(r.y * f) / f;
            float x1 = Mathf.Round(r.xMax * f) / f, y1 = Mathf.Round(r.yMax * f) / f;
            return new Rect(x0, y0, x1 - x0, y1 - y0);
        }

        private static void Flat(Rect r, Color c) { FlatRaw(SnapRect(r), c); }

        private static void FlatRaw(Rect r, Color c)
        {
            Color keep = GUI.color;
            GUI.color = new Color(c.r * keep.r, c.g * keep.g, c.b * keep.b, c.a * keep.a);
            WinPx.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = keep;
        }

        // The mouse path is one GL quad batch (T-0363): rotated GUI textures and
        // stamped squares both broke up or stacked their alpha in game. One quad
        // per segment, the age fade per vertex, so nothing overlaps along a
        // segment. Hidden/Internal-Colored is a stock shader (DebugDraw uses it).
        private static Material _lineMat;
        private static bool _lineMatTried;
        private bool _lineMatTold;

        private bool EnsureLineMaterial()
        {
            if (_lineMat != null) return true;
            if (!_lineMatTried)
            {
                _lineMatTried = true;
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                if (shader != null)
                {
                    _lineMat = new Material(shader);
                    _lineMat.hideFlags = HideFlags.HideAndDontSave;
                    _lineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    _lineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    _lineMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                    _lineMat.SetInt("_ZWrite", 0);
                    _lineMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                }
            }
            if (_lineMat == null && !_lineMatTold)
            {
                _lineMatTold = true;
                Ctx.Log.LogWarning("Inputs: the mouse path cannot be drawn (Hidden/Internal-Colored not found) - the mouse box shows the dot only.");
            }
            return _lineMat != null;
        }

        /// One segment as a quad of the given thickness, fading from colour c0 to c1.
        private static void Quad(float x0, float y0, float x1, float y1, float thick, Color c0, Color c1)
        {
            float dx = x1 - x0, dy = y1 - y0;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 0.01f) return;
            float nx = -dy / len * thick * 0.5f, ny = dx / len * thick * 0.5f;
            GL.Color(c0);
            Vertex(x0 + nx, y0 + ny);
            Vertex(x0 - nx, y0 - ny);
            GL.Color(c1);
            Vertex(x1 - nx, y1 - ny);
            Vertex(x1 + nx, y1 + ny);
        }

        /// A HUD point to GL's pixel space (LoadPixelMatrix: origin bottom-left), through the HUD's scale.
        private static void Vertex(float x, float y)
        {
            Vector3 s = GUI.matrix.MultiplyPoint3x4(new Vector3(x, y, 0f));
            GL.Vertex3(s.x, Screen.height - s.y, 0f);
        }

        private void DrawMouseBox(Rect r, float scale, float now, Color before, float a)
        {
            float half = r.width * 0.5f - 8f * scale;
            float cx = r.x + r.width * 0.5f, cy = r.y + r.height * 0.5f;
            float thick = Thick(LineThickness * scale);
            GUI.color = Color.white;   // colours below carry their own alpha
            float hx = cx + _mx * half, hy = cy + _my * half;
            float px = hx, py = hy, pAge = 0f;   // the line runs from the head back along the trail
            bool gl = _trailCount > 0 && Event.current.type == EventType.Repaint && EnsureLineMaterial();
            if (gl)
            {
                GL.PushMatrix();
                _lineMat.SetPass(0);
                GL.LoadPixelMatrix();   // screen pixels; Vertex applies the HUD's scale and flips y (as MapModule)
                GL.Begin(GL.QUADS);
            }
            for (int k = 0; k < _trailCount; k++)
            {
                int idx = (_trailHead - 1 - k + TrailMax * 2) % TrailMax;
                float age = now - _trailT[idx];
                if (age >= TrailSeconds) break;   // older entries are older still
                float tx = cx + _trailX[idx] * half, ty = cy + _trailY[idx] * half;
                // A jump across the box (the mouse wrapped at an edge) is not a line.
                bool wrapped = Mathf.Abs(tx - px) > half || Mathf.Abs(ty - py) > half;
                if (gl && !wrapped)
                {
                    float f0 = 1f - pAge / TrailSeconds, f1 = 1f - age / TrailSeconds;
                    float ba = 0.8f * before.a * a;
                    Quad(px, py, tx, ty, thick,
                        new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, ba * f0),
                        new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, ba * f1));
                }
                px = tx; py = ty; pAge = age;
            }
            if (gl)
            {
                GL.End();
                GL.PopMatrix();
            }
            float head = DotSize * scale;
            Flat(new Rect(hx - head * 0.5f, hy - head * 0.5f, head, head), new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, before.a * a));
        }

        // --- the mouse in Edit HUD: drag a tile, right-click to hide / show it ---------------------------

        private void EditEvents(UiScale ui, Event e)
        {
            MainWindowModule main = Host.Find<MainWindowModule>();
            Rect blocked = main != null ? Host.Hud.Settings.Virtual(main.ScreenRect) : new Rect();
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (_drag >= 0 || blocked.Contains(e.mousePosition)) return;
                    for (int i = _states.Count - 1; i >= 0; i--)
                    {
                        TileState s = _states[i];
                        if (!s.Valid || !s.Rect.Contains(e.mousePosition)) continue;
                        if (e.button == 1) { SetShown(s, !s.Tile.Shown); e.Use(); return; }
                        if (e.button != 0) return;
                        _drag = i;
                        _dragOffset = e.mousePosition - new Vector2(s.Rect.x, s.Rect.y);
                        e.Use();
                        return;
                    }
                    break;
                case EventType.MouseDrag:
                    if (_drag < 0 || _drag >= _states.Count) return;
                    TileState d = _states[_drag];
                    float x = Mathf.Clamp(e.mousePosition.x - _dragOffset.x, 0f, Mathf.Max(0f, ui.VW - d.Rect.width));
                    float y = Mathf.Clamp(e.mousePosition.y - _dragOffset.y, 0f, Mathf.Max(0f, ui.VH - d.Rect.height));
                    Vector2 at = Host.Hud.Widgets.SnapDrag(d.SnapId, new Rect(x, y, d.Rect.width, d.Rect.height));
                    d.Rect = new Rect(at.x, at.y, d.Rect.width, d.Rect.height);
                    d.Tile.X = ui.ToRefX(at.x);
                    d.Tile.Y = ui.ToRefY(at.y);
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (_drag < 0) return;
                    TileState m = _states[Mathf.Min(_drag, _states.Count - 1)];
                    _drag = -1;
                    Host.Hud.Widgets.EndSnap();
                    m.Tile.X = Mathf.Round(m.Tile.X);
                    m.Tile.Y = Mathf.Round(m.Tile.Y);
                    Commit();   // once per gesture
                    Ctx.Log.LogInfo("Inputs: tile " + m.Tile.Key + " moved to (" + Mathf.RoundToInt(m.Tile.X) + ", " + Mathf.RoundToInt(m.Tile.Y) + ").");
                    e.Use();
                    break;
            }
        }

        // --- the Edit HUD rows -----------------------------------------------------------------------

        private static readonly GUIContent InputsText = new GUIContent("Input overlay");
        private static readonly GUIContent About = new GUIContent(
            "Shows the keys and mouse buttons you press, on the game screen. Each tile is placed on its own: " +
            "drag it to move it, right-click it to hide or show it. Nothing shows while a cursor is on screen.");
        private static readonly GUIContent OnlyPressedText = new GUIContent("Only while pressed");
        private static readonly GUIContent OnlyPressedAbout = new GUIContent(
            "A tile is drawn only while its key is held, exactly as pressed, and not at all otherwise. This window still shows every tile so you can place them.");
        private static readonly GUIContent AddKeyText = new GUIContent("+ Key");
        private static readonly GUIContent AddKeyWaiting = new GUIContent("Press a key...");
        private static readonly GUIContent AddKeyTip = new GUIContent(
            "Press any key or mouse button 3-7 and it becomes a tile. Esc cancels. M1 and M2 are in the buttons below (a click here would be taken as the press).");
        private static readonly GUIContent AddMouseBoxText = new GUIContent("+ Mouse box");
        private static readonly GUIContent AddMouseBoxTip = new GUIContent("A box showing the mouse's movement as a fading trail (wraps at the edges).");
        private static readonly GUIContent AddWheelText = new GUIContent("+ Wheel");
        private static readonly GUIContent AddWheelTip = new GUIContent("A tile that lights when you scroll.");
        private static readonly GUIContent AddM1Text = new GUIContent("+ M1");
        private static readonly GUIContent AddM2Text = new GUIContent("+ M2");
        private static readonly GUIContent ResetText = new GUIContent("Default layout");
        private static readonly GUIContent ResetSureText = new GUIContent("Sure?");
        private static readonly GUIContent ResetTip = new GUIContent("WASD, Shift, Space, Ctrl, M1, M2 and the mouse box, back where they started.");
        private static readonly GUIContent RemoveText = new GUIContent("x");
        private static readonly GUIContent RemoveTip = new GUIContent("Remove this tile (hide it instead to keep its place).");
        private static readonly GUIContent SmallerText = new GUIContent("-");
        private static readonly GUIContent BiggerText = new GUIContent("+");
        private static readonly GUIContent BrokenText = new GUIContent(
            "The game gave no mouse movement, so the mouse box stays still.");
        private readonly GUIContent[] _scaleTexts = new GUIContent[(int)(InputLayout.MaxScale * 4f) + 1];
        private GUIStyle _centred;

        public override float DrawHudEditor(float y, float w)
        {
            const float indent = 20f;
            bool on = UiKit.Toggle(new Rect(0f, y, w, 22f), _on.Value, InputsText);
            if (on != _on.Value)
            {
                _on.Value = on;
                Ctx.Log.LogInfo("Inputs: " + (on ? "on" : "off") + ".");
            }
            y += 24f;
            y += UiText.Draw(indent, y, w - indent, About, UiKit.HintStyle) + 2f;
            if (!_on.Value) return y + 8f;

            bool only = UiKit.Toggle(new Rect(0f, y, w, 22f), _onlyPressed.Value, OnlyPressedText);
            if (only != _onlyPressed.Value)
            {
                _onlyPressed.Value = only;
                Ctx.Log.LogInfo("Inputs: only-pressed mode " + (only ? "on" : "off") + ".");
            }
            y += 24f;
            y += UiText.Draw(indent, y, w - indent, OnlyPressedAbout, UiKit.HintStyle) + 4f;

            if (_centred == null)
            {
                _centred = UiKit.Style(GUI.skin.label);
                _centred.alignment = TextAnchor.MiddleCenter;
            }
            _removeAt = -1;
            for (int i = 0; i < _states.Count; i++) y = DrawTileRow(i, y, w);
            if (_removeAt >= 0 && _removeAt < _states.Count) RemoveTile(_removeAt);   // after the rows: the list changes
            _removeAt = -1;

            // Add buttons, flowing left to right.
            float x = 0f;
            float keyW = Mathf.Max(90f, WinPx.CalcSize(GUI.skin.button, AddKeyWaiting).x + 8f, WinPx.CalcSize(GUI.skin.button, AddKeyText).x + 8f);
            Rect keyR = new Rect(x, y, keyW, 24f);
            if (UiKit.Toggle(keyR, _capturing, _capturing ? AddKeyWaiting : AddKeyText, GUI.skin.button) != _capturing)
            {
                _capturing = !_capturing;
                SetStatus(_capturing ? "Press the key to add. Esc cancels." : "");
            }
            UiKit.Hint(keyR, AddKeyTip);
            x += keyW + 6f;
            y = AddButton(ref x, y, w, AddM1Text, null, "Mouse0");
            y = AddButton(ref x, y, w, AddM2Text, null, "Mouse1");
            y = AddButton(ref x, y, w, AddWheelText, AddWheelTip, InputLayout.WheelKey);
            y = AddButton(ref x, y, w, AddMouseBoxText, AddMouseBoxTip, InputLayout.MouseKey);
            y += 28f;
            if (_status.text.Length > 0) y += UiText.Draw(0f, y, w, _status, UiKit.HintStyle) + 6f;

            bool armed = _resetArmed && Time.unscaledTime <= _resetUntil;
            Rect resetR = new Rect(0f, y, 120f, 24f);
            if (WinPx.Button(resetR, armed ? ResetSureText : ResetText))
            {
                if (armed) { _resetArmed = false; ResetLayout(); }
                else { _resetArmed = true; _resetUntil = Time.unscaledTime + 4f; }
            }
            UiKit.Hint(resetR, ResetTip);
            y += 30f;
            if (_axisBroken) y += UiText.Draw(0f, y, w, BrokenText, UiKit.HintStyle) + 2f;
            return y + 8f;
        }

        private float AddButton(ref float x, float y, float w, GUIContent text, GUIContent tip, string key)
        {
            float bw = Mathf.Max(80f, WinPx.CalcSize(GUI.skin.button, text).x + 8f);   // as wide as its words, disabled or not
            if (x + bw > w && x > 0f) { x = 0f; y += 28f; }
            bool present = _layout.Find(key) != null;
            Rect r = new Rect(x, y, bw, 24f);
            GUI.enabled = !present;
            if (WinPx.Button(r, text)) AddTile(key);
            GUI.enabled = true;
            if (tip != null) UiKit.Hint(r, tip);
            x += bw + 6f;
            return y;
        }

        // [shown] name ........ [-] 1x [+] [x]
        private float DrawTileRow(int i, float y, float w)
        {
            TileState s = _states[i];
            const float step = 26f, scaleW = 40f, gap = 4f;
            float h = 22f;
            float x = w - step;
            Rect removeR = new Rect(x, y, step, h);
            if (WinPx.Button(removeR, RemoveText)) _removeAt = i;
            UiKit.Hint(removeR, RemoveTip);
            x -= gap + step;
            if (WinPx.Button(new Rect(x, y, step, h), BiggerText)) SetScale(s, s.Tile.Scale + 0.25f);
            x -= scaleW;
            WinPx.Label(new Rect(x, y, scaleW, h), ScaleText(s.Tile.Scale), _centred);
            x -= step;
            if (WinPx.Button(new Rect(x, y, step, h), SmallerText)) SetScale(s, s.Tile.Scale - 0.25f);
            Rect nameR = new Rect(0f, y, Mathf.Max(40f, x - gap), h);
            bool shown = UiKit.Toggle(nameR, s.Tile.Shown, s.Name);
            if (shown != s.Tile.Shown) SetShown(s, shown);
            return y + 26f;
        }

        private GUIContent ScaleText(float scale)
        {
            int k = Mathf.Clamp(Mathf.RoundToInt(scale * 4f), 0, _scaleTexts.Length - 1);
            if (_scaleTexts[k] == null) _scaleTexts[k] = new GUIContent((k / 4f).ToString("0.##") + "x");
            return _scaleTexts[k];
        }
    }
}
