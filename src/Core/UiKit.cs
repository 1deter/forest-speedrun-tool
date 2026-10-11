using System;
using System.Collections.Generic;
using ForestOverlay.Data;
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
        // Yellow on black (decisions.md *Look: yellow on black*): the colours
        // of one Data/UiPalette variant, set by Apply. A switch repaints the
        // same textures in place (styles copied from the skin keep pointing at
        // them) and recolours every style made with Style(), so it shows at
        // once in every tab.
        public static Color PanelBg { get; private set; }
        public static Color CardBg { get; private set; }
        public static Color CardHover { get; private set; }
        public static Color Border { get; private set; }
        public static Color TextColour { get; private set; }
        public static Color DimColour { get; private set; }
        /// Descriptions (UiText.Dim): between TextColour and DimColour.
        public static Color SoftColour { get; private set; }
        /// The one accent: the loading screen's yellow, rgb(229, 197, 1).
        public static Color Accent { get; private set; }
        public static Color AccentDark { get; private set; }
        public static Color AccentHover { get; private set; }
        public static Color OnAccent { get; private set; }
        public static Color Warn { get; private set; }
        private static Color _buttonBorder, _buttonHoverBorder, _field, _widget, _widgetBorder, _tipBg, _tipBorder;

        public static UiPalette Palette { get; private set; }

        /// Every style copied through Style(): recoloured by a palette switch.
        private static readonly List<GUIStyle> Tracked = new List<GUIStyle>();

        static UiKit() { SetColours(UiPalette.Default); }

        private static Color Col(uint c) { return new Color(UiPalette.R(c), UiPalette.G(c), UiPalette.B(c), UiPalette.A(c)); }

        private static void SetColours(UiPalette p)
        {
            Palette = p;
            PanelBg = Col(p.Panel); CardBg = Col(p.Card); CardHover = Col(p.CardHover); Border = Col(p.Border);
            TextColour = Col(p.Text); DimColour = Col(p.Dim); SoftColour = Col(p.Soft);
            Accent = Col(p.Accent); AccentDark = Col(p.AccentDark); AccentHover = Col(p.AccentHover);
            OnAccent = Col(p.OnAccent); Warn = Col(p.Warn);
            _buttonBorder = Col(p.ButtonBorder); _buttonHoverBorder = Col(p.ButtonHoverBorder); _field = Col(p.Field);
            _widget = Col(p.Widget); _widgetBorder = Col(p.WidgetBorder); _tipBg = Col(p.Tip); _tipBorder = Col(p.TipBorder);
        }

        /// Switches the palette: before the first build it only sets the
        /// colours; after it, the textures are repainted in place and every
        /// style's text colour that was one of the old palette's text colours
        /// becomes the new one (UiPalette keeps them distinct, so the match is
        /// exact; a module's own colours - deltas, warnings - are left alone).
        public static void Apply(UiPalette p)
        {
            if (p == null || p == Palette) return;
            if (!_built || _skin == null) { SetColours(p); return; }
            Color[] from = TextTokens();
            SetColours(p);
            Color[] to = TextTokens();
            BuildTextures();
            GUIStyle[] own = { _skin.window, _skin.button, _skin.toggle, _skin.textField, _skin.textArea, _skin.label, _skin.box,
                               _tab, _tabActive, _header, _headerSummary, _card, _widgetCard, _outline, _handle, _tip,
                               _primary, _hint, _title };
            for (int i = 0; i < own.Length; i++) Recolour(own[i], from, to);
            for (int i = 0; i < Tracked.Count; i++) Recolour(Tracked[i], from, to);
        }

        /// A copy of `src` that follows a palette switch: use it for every
        /// style a module builds (instead of new GUIStyle(src)).
        public static GUIStyle Style(GUIStyle src)
        {
            GUIStyle s = new GUIStyle(src);
            Tracked.Add(s);
            return s;
        }

        private static Color[] TextTokens()
        {
            return new[] { TextColour, DimColour, SoftColour, Accent, AccentHover, OnAccent, Warn };
        }

        private static void Recolour(GUIStyle s, Color[] from, Color[] to)
        {
            if (s == null) return;
            Recolour(s.normal, from, to); Recolour(s.hover, from, to); Recolour(s.active, from, to); Recolour(s.focused, from, to);
            Recolour(s.onNormal, from, to); Recolour(s.onHover, from, to); Recolour(s.onActive, from, to); Recolour(s.onFocused, from, to);
        }

        private static void Recolour(GUIStyleState st, Color[] from, Color[] to)
        {
            Color c = st.textColor;
            for (int i = 0; i < from.Length; i++)
                if (c == from[i]) { st.textColor = to[i]; return; }
        }

        public const float Pad = 8f;
        private const int CheckSize = 16;
        private const float CheckLeft = 3f;
        public const float HeaderH = 24f;

        private static bool _built;
        private static GUISkin _skin;
        private static GUIStyle _tab, _tabActive, _header, _headerSummary, _card, _widgetCard,
                                _outline, _handle, _tip, _primary, _hint, _title;
        private static Texture2D _tAccentFlat;
        private static Texture2D _tPanel, _tCard, _tCardHover, _tWidget, _tButton, _tButtonHover, _tButtonOn,
                                 _tPrimary, _tPrimaryHover, _tField, _tCheckOff, _tCheckOn, _tCheckOffHover,
                                 _tCheckOnHover, _tOutline, _tHandle, _tTip, _tTab, _tTabActive;

        // --- textures ------------------------------------------------------------

        /// A rounded rectangle with an optional border, anti-aliased, for a
        /// 9-slice style (border = radius + 1). size: the texture's side.
        private static Texture2D Rounded(Texture2D reuse, int size, int radius, Color fill, Color border, int borderPx)
        {
            Texture2D t = Tex(reuse, size);
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

        /// `reuse` when it is already this size (a palette switch repaints it,
        /// so styles holding it follow), else a new texture.
        private static Texture2D Tex(Texture2D reuse, int size)
        {
            if (reuse != null && reuse.width == size) return reuse;
            Texture2D t = new Texture2D(size, size, TextureFormat.ARGB32, false, true);   // linear: as sRGB the game darkened them (yellow came out orange, #E28903)
            t.hideFlags = HideFlags.HideAndDontSave;
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            return t;
        }

        private static Texture2D Flat(Texture2D reuse, Color c)
        {
            Texture2D t = Tex(reuse, 1);
            t.SetPixel(0, 0, c);
            t.Apply(false, true);
            return t;
        }

        /// The checkbox: a 16x16 square texture, drawn unscaled and centred in
        /// its row by Toggle (T-0021: drawn by the toggle style it was stretched
        /// to the row's 20 / 22 px, so boxes came out wider than tall). An
        /// outline when off, a solid yellow square when on (author, 2026-10-11).
        private static Texture2D Check(Texture2D reuse, bool on, bool hover)
        {
            const int w = CheckSize, r = 3;
            Texture2D t = Tex(reuse, w);
            Color fill = on ? (hover ? AccentHover : Accent) : (hover ? new Color(1f, 1f, 1f, 0.06f) : new Color(0f, 0f, 0f, 0f));
            Color edge = on ? (hover ? AccentHover : Accent) : (hover ? TextColour : DimColour);
            float inner = w * 0.5f - r;
            Color[] px = new Color[w * w];
            for (int y = 0; y < w; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - w * 0.5f) - inner;
                    float qy = Mathf.Abs(y + 0.5f - w * 0.5f) - inner;
                    float sd = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f))
                               + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                    float cover = Mathf.Clamp01(0.5f - sd);
                    float fa = Mathf.Clamp01(0.5f - (sd + 1.5f));
                    Color c = Color.Lerp(edge, fill, fa);
                    c.a *= cover;
                    px[y * w + x] = c;
                }
            }
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }

        // --- build ---------------------------------------------------------------

        /// Paints every texture in the current colours, reusing the ones
        /// already made (Apply calls it again on a switch).
        private static void BuildTextures()
        {
            _tPanel = Rounded(_tPanel, 32, 8, PanelBg, Border, 1);
            _tCard = Rounded(_tCard, 24, 5, CardBg, CardBg, 0);
            _tCardHover = Rounded(_tCardHover, 24, 5, CardHover, CardHover, 0);
            _tWidget = Rounded(_tWidget, 24, 6, _widget, _widgetBorder, 1);
            _tButton = Rounded(_tButton, 24, 5, CardBg, _buttonBorder, 1);
            _tButtonHover = Rounded(_tButtonHover, 24, 5, CardHover, _buttonHoverBorder, 1);
            _tButtonOn = Rounded(_tButtonOn, 24, 5, AccentDark, Accent, 1);
            _tPrimary = Rounded(_tPrimary, 24, 5, Accent, Accent, 0);
            _tPrimaryHover = Rounded(_tPrimaryHover, 24, 5, AccentHover, Accent, 0);
            _tField = Rounded(_tField, 24, 4, _field, _buttonBorder, 1);
            _tCheckOff = Check(_tCheckOff, false, false);
            _tCheckOffHover = Check(_tCheckOffHover, false, true);
            _tCheckOn = Check(_tCheckOn, true, false);
            _tCheckOnHover = Check(_tCheckOnHover, true, true);
            _tOutline = Rounded(_tOutline, 24, 4, new Color(0f, 0f, 0f, 0f), Accent, 2);
            _tHandle = Rounded(_tHandle, 16, 4, Accent, Accent, 0);
            _tTip = Rounded(_tTip, 24, 5, _tipBg, _tipBorder, 1);
            _tTab = Flat(_tTab, new Color(0f, 0f, 0f, 0f));
            _tAccentFlat = Flat(_tAccentFlat, Accent);
            _tTabActive = Rounded(_tTabActive, 24, 5, CardBg, Accent, 1);
        }

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

            BuildTextures();

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
            // The label only, centred on the row: Toggle draws the box itself.
            StyleState(t.normal, null, TextColour);
            StyleState(t.hover, null, TextColour);
            StyleState(t.active, null, TextColour);
            StyleState(t.focused, null, TextColour);
            StyleState(t.onNormal, null, TextColour);
            StyleState(t.onHover, null, TextColour);
            StyleState(t.onActive, null, TextColour);
            StyleState(t.onFocused, null, TextColour);
            t.border = new RectOffset(0, 0, 0, 0);
            t.padding = new RectOffset((int)CheckLeft + CheckSize + 5, 0, 0, 0);
            t.overflow = new RectOffset(0, 0, 0, 0);
            t.alignment = TextAnchor.MiddleLeft;
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
            StyleState(_primary.normal, _tPrimary, OnAccent);
            StyleState(_primary.hover, _tPrimaryHover, OnAccent);
            StyleState(_primary.active, _tPrimaryHover, OnAccent);
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
        public static Texture2D AccentTexture { get { Ensure(); return _tAccentFlat; } }
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

        private static readonly GUIContent _toggleText = new GUIContent();

        /// A checkbox: every GUI.Toggle in the plugin goes through here (the
        /// skin's toggle style draws only the label). The box is a 16x16 square
        /// centred on the row whatever the row's height. Another style (a
        /// toggle drawn as a button) or the default skin passes straight through.
        public static bool Toggle(Rect r, bool value, string text)
        {
            _toggleText.text = text;
            return Toggle(r, value, _toggleText, GUI.skin.toggle);
        }

        public static bool Toggle(Rect r, bool value, GUIContent content)
        {
            return Toggle(r, value, content, GUI.skin.toggle);
        }

        public static bool Toggle(Rect r, bool value, string text, GUIStyle style)
        {
            _toggleText.text = text;
            return Toggle(r, value, _toggleText, style);
        }

        public static bool Toggle(Rect r, bool value, GUIContent content, GUIStyle style)
        {
            bool now = GUI.Toggle(r, value, content, style);
            if (!_built || _skin == null || GUI.skin != _skin || style != _skin.toggle) return now;
            Event e = Event.current;
            if (e == null || e.type != EventType.Repaint) return now;
            bool hover = GUI.enabled && r.Contains(e.mousePosition);
            Texture2D box = now ? (hover ? _tCheckOnHover : _tCheckOn) : (hover ? _tCheckOffHover : _tCheckOff);
            Color old = GUI.color;
            if (!GUI.enabled) GUI.color = new Color(old.r, old.g, old.b, old.a * 0.5f);
            GUI.DrawTexture(new Rect(r.x + CheckLeft, Mathf.Round(r.y + (r.height - CheckSize) * 0.5f), CheckSize, CheckSize), box);
            GUI.color = old;
            return now;
        }

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
