using System;
using System.Collections.Generic;
using UnityEngine;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // The shared look (docs/ui-redesign.md): one palette, one accent
    // colour, rounded panels drawn from textures generated ONCE, a cloned
    // GUISkin that every panel is drawn with, collapsible sections and a
    // hover tooltip.
    //
    // - Skin: Begin() / End() swap GUI.skin around panel drawing only, so
    //   every existing tab (all plain GUI.Button / Toggle / Label calls)
    //   inherits the look without a change, while the HUD, the notice and
    //   the game's own GUI keep the default skin.
    // - Nothing allocates after the first pass: textures, styles and the
    //   skin are built on first use inside OnGUI (GUI.skin needs it); a
    //   tooltip is a GUIContent the caller keeps.
    // - Only layout-free (Rect) controls are provided: that is how the
    //   whole project draws.
    // ------------------------------------------------------------------
    public static class UiKit
    {
        // --- palette ---------------------------------------------------------
        public static readonly Color PanelBg = new Color(0.090f, 0.098f, 0.122f, 0.96f);
        public static readonly Color CardBg = new Color(0.125f, 0.137f, 0.169f, 1f);
        public static readonly Color CardHover = new Color(0.165f, 0.180f, 0.220f, 1f);
        public static readonly Color Border = new Color(0.180f, 0.196f, 0.235f, 1f);
        public static readonly Color TextColour = new Color(0.90f, 0.91f, 0.92f, 1f);
        public static readonly Color DimColour = new Color(0.60f, 0.63f, 0.67f, 1f);
        /// The one accent: forest green.
        public static readonly Color Accent = new Color(0.30f, 0.78f, 0.56f, 1f);
        public static readonly Color AccentDark = new Color(0.16f, 0.42f, 0.31f, 1f);
        public static readonly Color Warn = new Color(1f, 0.55f, 0.2f, 1f);

        public const float Pad = 8f;
        public const float HeaderH = 24f;

        private static bool _built;
        private static GUISkin _skin;
        private static GUIStyle _tab, _tabActive, _header, _headerSummary, _card, _widgetCard,
                                _outline, _handle, _tip, _primary, _hint, _title;
        private static Texture2D _tPanel, _tCard, _tCardHover, _tWidget, _tButton, _tButtonHover, _tButtonOn,
                                 _tPrimary, _tPrimaryHover, _tField, _tCheckOff, _tCheckOn, _tCheckOffHover,
                                 _tCheckOnHover, _tOutline, _tHandle, _tTip, _tTab, _tTabActive;

        // --- textures ------------------------------------------------------------

        /// A rounded rectangle with an optional border, anti-aliased, for a
        /// 9-slice style (border = radius + 1). size: the texture's side.
        private static Texture2D Rounded(int size, int radius, Color fill, Color border, int borderPx)
        {
            Texture2D t = new Texture2D(size, size, TextureFormat.ARGB32, false);
            t.hideFlags = HideFlags.HideAndDontSave;
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            float half = size * 0.5f;
            float inner = half - radius;
            Color[] px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - half) - inner;
                    float qy = Mathf.Abs(y + 0.5f - half) - inner;
                    float sd = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f))
                               + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                    float cover = Mathf.Clamp01(0.5f - sd);
                    float fillAmt = borderPx > 0 ? Mathf.Clamp01(0.5f - (sd + borderPx)) : 1f;
                    Color c = Color.Lerp(border, fill, fillAmt);
                    c.a *= cover;
                    px[y * size + x] = c;
                }
            }
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }

        private static Texture2D Flat(Color c)
        {
            Texture2D t = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            t.hideFlags = HideFlags.HideAndDontSave;
            t.SetPixel(0, 0, c);
            t.Apply(false, true);
            return t;
        }

        /// A 24x24 checkbox drawn at the left of the texture (the toggle
        /// style's left border keeps it unstretched).
        private static Texture2D Check(bool on, bool hover)
        {
            const int w = 24;
            Texture2D t = new Texture2D(w, w, TextureFormat.ARGB32, false);
            t.hideFlags = HideFlags.HideAndDontSave;
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            Color clear = new Color(0f, 0f, 0f, 0f);
            Color[] px = new Color[w * w];
            for (int i = 0; i < px.Length; i++) px[i] = clear;

            Color fill = on ? (hover ? new Color(0.38f, 0.85f, 0.63f, 1f) : Accent) : (hover ? CardHover : CardBg);
            Color edge = on ? Accent : (hover ? DimColour : new Color(0.33f, 0.36f, 0.42f, 1f));
            const int box = 16, ox = 3, oy = 4, r = 4;
            float inner = box * 0.5f - r;
            for (int y = 0; y < box; y++)
            {
                for (int x = 0; x < box; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - box * 0.5f) - inner;
                    float qy = Mathf.Abs(y + 0.5f - box * 0.5f) - inner;
                    float sd = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f))
                               + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                    float cover = Mathf.Clamp01(0.5f - sd);
                    float fa = Mathf.Clamp01(0.5f - (sd + 1.2f));
                    Color c = Color.Lerp(edge, fill, fa);
                    c.a *= cover;
                    px[(y + oy) * w + x + ox] = c;
                }
            }
            if (on)
            {
                // A check mark: two thick strokes (texture y runs upwards).
                Stroke(px, w, ox + 4f, oy + 8f, ox + 7f, oy + 5f);
                Stroke(px, w, ox + 7f, oy + 5f, ox + 12f, oy + 11.5f);
            }
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }

        private static void Stroke(Color[] px, int w, float x0, float y0, float x1, float y1)
        {
            int steps = 24;
            for (int i = 0; i <= steps; i++)
            {
                float f = i / (float)steps;
                float cx = Mathf.Lerp(x0, x1, f), cy = Mathf.Lerp(y0, y1, f);
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int px0 = Mathf.RoundToInt(cx) + dx, py0 = Mathf.RoundToInt(cy) + dy;
                        if (px0 < 0 || py0 < 0 || px0 >= w || py0 >= w) continue;
                        float d = Mathf.Sqrt((px0 - cx) * (px0 - cx) + (py0 - cy) * (py0 - cy));
                        float a = Mathf.Clamp01(1.4f - d);
                        Color old = px[py0 * w + px0];
                        px[py0 * w + px0] = Color.Lerp(old, new Color(1f, 1f, 1f, 1f), a);
                    }
            }
        }

        // --- build ---------------------------------------------------------------

        private static void StyleState(GUIStyleState s, Texture2D bg, Color text)
        {
            s.background = bg;
            s.textColor = text;
        }

        /// Builds the textures, styles and skin once. Call inside OnGUI.
        public static void Ensure()
        {
            if (_built) return;
            _built = true;   // set first: a throw below must not rebuild (and leak) every pass

            _tPanel = Rounded(32, 8, PanelBg, Border, 1);
            _tCard = Rounded(24, 5, CardBg, CardBg, 0);
            _tCardHover = Rounded(24, 5, CardHover, CardHover, 0);
            _tWidget = Rounded(24, 6, new Color(0.07f, 0.075f, 0.09f, 0.84f), new Color(0.2f, 0.22f, 0.26f, 0.9f), 1);
            _tButton = Rounded(24, 5, CardBg, new Color(0.24f, 0.26f, 0.31f, 1f), 1);
            _tButtonHover = Rounded(24, 5, CardHover, new Color(0.34f, 0.37f, 0.43f, 1f), 1);
            _tButtonOn = Rounded(24, 5, AccentDark, Accent, 1);
            _tPrimary = Rounded(24, 5, Accent, Accent, 0);
            _tPrimaryHover = Rounded(24, 5, new Color(0.40f, 0.86f, 0.64f, 1f), Accent, 0);
            _tField = Rounded(24, 4, new Color(0.065f, 0.07f, 0.09f, 1f), new Color(0.24f, 0.26f, 0.31f, 1f), 1);
            _tCheckOff = Check(false, false);
            _tCheckOffHover = Check(false, true);
            _tCheckOn = Check(true, false);
            _tCheckOnHover = Check(true, true);
            _tOutline = Rounded(24, 4, new Color(0f, 0f, 0f, 0f), Accent, 2);
            _tHandle = Rounded(16, 4, Accent, Accent, 0);
            _tTip = Rounded(24, 5, new Color(0.04f, 0.045f, 0.055f, 0.98f), new Color(0.3f, 0.33f, 0.39f, 1f), 1);
            _tTab = Flat(new Color(0f, 0f, 0f, 0f));
            _tTabActive = Rounded(24, 5, CardBg, Accent, 1);

            GUISkin src = GUI.skin;
            _skin = UnityEngine.Object.Instantiate(src) as GUISkin;
            if (_skin == null) return;
            _skin.hideFlags = HideFlags.HideAndDontSave;

            // Window: rounded, title top-left.
            GUIStyle w = _skin.window;
            StyleState(w.normal, _tPanel, TextColour);
            StyleState(w.onNormal, _tPanel, TextColour);
            StyleState(w.hover, _tPanel, TextColour);
            StyleState(w.onHover, _tPanel, TextColour);
            StyleState(w.active, _tPanel, TextColour);
            StyleState(w.onActive, _tPanel, TextColour);
            StyleState(w.focused, _tPanel, TextColour);
            StyleState(w.onFocused, _tPanel, TextColour);
            w.border = new RectOffset(10, 10, 10, 10);
            w.padding = new RectOffset(10, 10, 28, 10);
            w.alignment = TextAnchor.UpperLeft;
            w.contentOffset = new Vector2(2f, -22f);   // the title draws inside padding.top (28): pull it into the header band, above the tab strip (y 26)
            w.fontStyle = FontStyle.Bold;
            w.fontSize = 13;

            // Button
            GUIStyle b = _skin.button;
            StyleState(b.normal, _tButton, TextColour);
            StyleState(b.hover, _tButtonHover, TextColour);
            StyleState(b.active, _tButtonOn, TextColour);
            StyleState(b.focused, _tButton, TextColour);
            StyleState(b.onNormal, _tButtonOn, TextColour);
            StyleState(b.onHover, _tButtonOn, TextColour);
            StyleState(b.onActive, _tButtonOn, TextColour);
            StyleState(b.onFocused, _tButtonOn, TextColour);
            b.border = new RectOffset(7, 7, 7, 7);
            b.padding = new RectOffset(10, 10, 3, 3);
            b.margin = new RectOffset(0, 0, 0, 0);
            b.alignment = TextAnchor.MiddleCenter;

            // Toggle: our checkbox
            GUIStyle t = _skin.toggle;
            StyleState(t.normal, _tCheckOff, TextColour);
            StyleState(t.hover, _tCheckOffHover, TextColour);
            StyleState(t.active, _tCheckOffHover, TextColour);
            StyleState(t.focused, _tCheckOff, TextColour);
            StyleState(t.onNormal, _tCheckOn, TextColour);
            StyleState(t.onHover, _tCheckOnHover, TextColour);
            StyleState(t.onActive, _tCheckOnHover, TextColour);
            StyleState(t.onFocused, _tCheckOn, TextColour);
            t.border = new RectOffset(22, 0, 0, 0);
            t.padding = new RectOffset(24, 0, 3, 0);
            t.overflow = new RectOffset(0, 0, 0, 0);
            t.imagePosition = ImagePosition.ImageLeft;

            // Text fields
            GUIStyle f = _skin.textField;
            StyleState(f.normal, _tField, TextColour);
            StyleState(f.hover, _tField, TextColour);
            StyleState(f.focused, _tField, TextColour);
            StyleState(f.active, _tField, TextColour);
            StyleState(f.onNormal, _tField, TextColour);
            StyleState(f.onHover, _tField, TextColour);
            StyleState(f.onFocused, _tField, TextColour);
            StyleState(f.onActive, _tField, TextColour);
            f.border = new RectOffset(6, 6, 6, 6);
            f.padding = new RectOffset(6, 6, 3, 3);
            GUIStyle a = _skin.textArea;
            StyleState(a.normal, _tField, TextColour);
            StyleState(a.hover, _tField, TextColour);
            StyleState(a.focused, _tField, TextColour);
            StyleState(a.active, _tField, TextColour);
            StyleState(a.onNormal, _tField, TextColour);
            StyleState(a.onHover, _tField, TextColour);
            StyleState(a.onFocused, _tField, TextColour);
            StyleState(a.onActive, _tField, TextColour);
            a.border = new RectOffset(6, 6, 6, 6);
            a.padding = new RectOffset(6, 6, 4, 4);

            // Labels and boxes
            _skin.label.normal.textColor = TextColour;
            StyleState(_skin.box.normal, _tCard, TextColour);
            _skin.box.border = new RectOffset(6, 6, 6, 6);

            // Our own named styles
            _tab = new GUIStyle(b);
            StyleState(_tab.normal, _tTab, DimColour);
            StyleState(_tab.hover, _tCardHover, TextColour);
            StyleState(_tab.active, _tCard, TextColour);
            StyleState(_tab.onNormal, _tTab, DimColour);
            _tab.padding = new RectOffset(11, 11, 2, 2);

            _tabActive = new GUIStyle(_tab);
            StyleState(_tabActive.normal, _tTabActive, Accent);
            StyleState(_tabActive.hover, _tTabActive, Accent);
            StyleState(_tabActive.active, _tTabActive, Accent);
            _tabActive.fontStyle = FontStyle.Bold;

            _header = new GUIStyle(b);
            StyleState(_header.normal, _tCard, TextColour);
            StyleState(_header.hover, _tCardHover, TextColour);
            StyleState(_header.active, _tCardHover, TextColour);
            _header.alignment = TextAnchor.MiddleLeft;
            _header.padding = new RectOffset(26, 8, 2, 2);
            _header.fontStyle = FontStyle.Bold;

            _headerSummary = new GUIStyle(_skin.label);
            _headerSummary.alignment = TextAnchor.MiddleRight;
            _headerSummary.normal.textColor = DimColour;
            _headerSummary.fontSize = 11;

            _card = new GUIStyle(_skin.box);
            StyleState(_card.normal, _tCard, TextColour);
            _card.border = new RectOffset(6, 6, 6, 6);

            _widgetCard = new GUIStyle(_skin.box);
            StyleState(_widgetCard.normal, _tWidget, TextColour);
            _widgetCard.border = new RectOffset(8, 8, 8, 8);

            _outline = new GUIStyle(_skin.box);
            StyleState(_outline.normal, _tOutline, TextColour);
            _outline.border = new RectOffset(6, 6, 6, 6);

            _handle = new GUIStyle(_skin.box);
            StyleState(_handle.normal, _tHandle, TextColour);
            _handle.border = new RectOffset(5, 5, 5, 5);

            _tip = new GUIStyle(_skin.box);
            StyleState(_tip.normal, _tTip, TextColour);
            _tip.border = new RectOffset(7, 7, 7, 7);
            _tip.padding = new RectOffset(8, 8, 5, 5);
            _tip.wordWrap = true;
            _tip.alignment = TextAnchor.UpperLeft;
            _tip.fontSize = 12;

            _primary = new GUIStyle(b);
            StyleState(_primary.normal, _tPrimary, new Color(0.04f, 0.1f, 0.07f, 1f));
            StyleState(_primary.hover, _tPrimaryHover, new Color(0.04f, 0.1f, 0.07f, 1f));
            StyleState(_primary.active, _tPrimaryHover, new Color(0.04f, 0.1f, 0.07f, 1f));
            _primary.fontStyle = FontStyle.Bold;

            _hint = new GUIStyle(_skin.label);
            _hint.normal.textColor = DimColour;
            _hint.fontSize = 11;
            _hint.wordWrap = true;

            _title = new GUIStyle(_skin.label);
            _title.fontStyle = FontStyle.Bold;
            _title.fontSize = 13;
        }

        // --- styles --------------------------------------------------------------
        public static GUISkin Skin { get { Ensure(); return _skin; } }
        public static GUIStyle Tab { get { Ensure(); return _tab; } }
        public static GUIStyle TabActive { get { Ensure(); return _tabActive; } }
        public static GUIStyle Card { get { Ensure(); return _card; } }
        public static GUIStyle WidgetCard { get { Ensure(); return _widgetCard; } }
        public static GUIStyle Outline { get { Ensure(); return _outline; } }
        public static GUIStyle Handle { get { Ensure(); return _handle; } }
        public static GUIStyle Primary { get { Ensure(); return _primary; } }
        public static GUIStyle HintStyle { get { Ensure(); return _hint; } }
        public static GUIStyle Title { get { Ensure(); return _title; } }

        /// Swaps in the overlay skin; pass the result to End. Never throws.
        public static GUISkin Begin()
        {
            GUISkin prev = GUI.skin;
            try { Ensure(); if (_skin != null) GUI.skin = _skin; }
            catch (Exception) { }
            return prev;
        }

        public static void End(GUISkin previous)
        {
            if (previous != null) GUI.skin = previous;
        }

        // --- controls --------------------------------------------------------------

        /// The main call to action.
        public static bool PrimaryButton(Rect r, GUIContent c)
        {
            return GUI.Button(r, c, Primary);
        }

        public static GUIContent C(string text)
        {
            return new GUIContent(text);
        }

        public static GUIContent C(string text, string tip)
        {
            return new GUIContent(text, tip);
        }

        // --- collapsible sections ---------------------------------------------------

        private static readonly Dictionary<string, bool> Open = new Dictionary<string, bool>();

        public static bool IsOpen(string id, bool defaultOpen)
        {
            bool v;
            return Open.TryGetValue(id, out v) ? v : defaultOpen;
        }

        public static void SetOpen(string id, bool open)
        {
            Open[id] = open;
        }

        /// A section header at (x, y): a click opens / closes it. Returns
        /// whether it is open; y moves past the header. `summary` (may be
        /// null) is a short dim state on the right ("off", "3 states") and
        /// `tip` (may be null) explains the section on hover.
        public static bool Section(float x, ref float y, float w, string id, GUIContent title,
                                   GUIContent summary, GUIContent tip, bool defaultOpen)
        {
            Ensure();
            bool open = IsOpen(id, defaultOpen);
            Rect r = new Rect(x, y, w, HeaderH);
            if (GUI.Button(r, title, _header)) { open = !open; Open[id] = open; }

            // The chevron: accent, drawn by hand (a text glyph may be missing from the font).
            DrawChevron(r.x + 10f, r.y + HeaderH * 0.5f, open);
            if (summary != null && !string.IsNullOrEmpty(summary.text))
                GUI.Label(new Rect(r.x + 8f, r.y, r.width - 16f, r.height), summary, _headerSummary);
            if (tip != null) Hint(r, tip);
            y += HeaderH + 4f;
            return open;
        }

        public static bool Section(float x, ref float y, float w, string id, GUIContent title, bool defaultOpen)
        {
            return Section(x, ref y, w, id, title, null, null, defaultOpen);
        }

        private static void DrawChevron(float cx, float cy, bool open)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            Color before = GUI.color;
            GUI.color = Accent;
            // Three stacked bars shrinking to a point: a triangle with no texture.
            if (open)
            {
                GUI.DrawTexture(new Rect(cx - 4f, cy - 2f, 8f, 2f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx - 3f, cy, 6f, 2f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx - 1f, cy + 2f, 2f, 2f), Texture2D.whiteTexture);
            }
            else
            {
                GUI.DrawTexture(new Rect(cx - 2f, cy - 4f, 2f, 8f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx, cy - 3f, 2f, 6f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx + 2f, cy - 1f, 2f, 2f), Texture2D.whiteTexture);
            }
            GUI.color = before;
        }

        // --- hover tooltip ----------------------------------------------------------

        private const float TipDelay = 0.35f;
        private const float TipMaxW = 280f;
        private static GUIContent _tipHover;      // hovered this pass
        private static Vector2 _tipScreen;
        private static GUIContent _tipShown;      // what the delay is timing
        private static float _tipSince;

        /// Registers a tooltip for `r`: shown near the mouse after a short
        /// delay by DrawTip, which the window calls last. Cheap; no
        /// allocation. Put the tip in a GUIContent the caller keeps.
        public static void Hint(Rect r, GUIContent tip)
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.Repaint || tip == null || string.IsNullOrEmpty(tip.text)) return;
            if (!r.Contains(e.mousePosition)) return;
            _tipHover = tip;
            _tipScreen = GUIUtility.GUIToScreenPoint(e.mousePosition);
        }

        /// Hover tip for a control that carries its own GUIContent.tooltip text.
        public static void Hint(Rect r, string tipText, GUIContent cache)
        {
            if (cache == null) return;
            if (!ReferenceEquals(cache.text, tipText)) cache.text = tipText;
            Hint(r, cache);
        }

        /// Draws the pending tip, if any, on top of everything in the
        /// current GUI space; `bounds` keeps it inside the window.
        public static void DrawTip(Rect bounds)
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.Repaint) return;
            GUIContent tip = _tipHover;
            _tipHover = null;
            if (tip == null) { _tipShown = null; return; }
            if (!ReferenceEquals(tip, _tipShown)) { _tipShown = tip; _tipSince = Time.unscaledTime; return; }
            if (Time.unscaledTime - _tipSince < TipDelay) return;

            Ensure();
            Vector2 local = GUIUtility.ScreenToGUIPoint(_tipScreen);
            float w = Mathf.Min(TipMaxW, bounds.width - 12f);
            float h = _tip.CalcHeight(tip, w);
            float x = local.x + 14f, y = local.y + 20f;
            if (x + w > bounds.xMax - 4f) x = bounds.xMax - 4f - w;
            if (y + h > bounds.yMax - 4f) y = local.y - h - 6f;
            if (x < bounds.x + 4f) x = bounds.x + 4f;
            if (y < bounds.y + 4f) y = bounds.y + 4f;
            GUI.Box(new Rect(x, y, w, h), tip, _tip);
        }
    }
}
