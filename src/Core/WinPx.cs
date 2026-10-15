using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // Sharp text in the scaled F2 window (T-0360).
    //
    // The window is laid out in units of a 768-high screen (T-0324). It
    // used to draw under GUI.matrix x k, so IMGUI rasterised every glyph
    // at the 768p size and the matrix stretched it: soft text at 1080p and
    // above. CrispText's trick (font x k inside a local 1/k matrix) cannot
    // work in the window: IMGUI clips groups and scroll views in units
    // before the matrix, so enlarged text was cut at 1/k of its width
    // (gotcha 112; five escapes tried live, none works).
    //
    // So the window draws at matrix identity in screen pixels, and every
    // IMGUI call reachable from it goes through here: modules keep laying
    // out in window units, these convert at the boundary - rects x K (x and
    // y on whole pixels), the style's font size, padding, overflow, margin,
    // content offset and fixed size x K around the call and put back after
    // (no copies, no allocation), scroll positions both ways, the mouse
    // back into units. The 9-slice borders stay: they are texture pixels
    // too, and the generated textures are too small to slice wider.
    //
    // K is 1 outside the window (set by MainWindowModule around its
    // contents only), so for the HUD, the notice and every other piece
    // these are the plain GUI calls. scripts/lint.py keeps GUI's controls
    // out of src/Core and src/Modules: one missed call draws 1/K size in
    // the wrong place.
    // ------------------------------------------------------------------
    public static class WinPx
    {
        /// Window units to screen pixels; 1 outside the F2 window.
        public static float K = 1f;

        // --- geometry ------------------------------------------------------------

        /// A rect in window units as screen pixels: the corner on a whole
        /// pixel (sharp text and edges), the size exact (so a height measured
        /// by CalcHeight at this width is the height drawn).
        public static Rect Px(Rect r)
        {
            if (K == 1f) return r;
            return new Rect(UiScale.WindowPx(r.x, K), UiScale.WindowPx(r.y, K), r.width * K, r.height * K);
        }

        /// The mouse in window units (Event.current's, inside the window).
        public static Vector2 Mouse
        {
            get
            {
                Event e = Event.current;
                if (e == null) return Vector2.zero;
                return K == 1f ? e.mousePosition : e.mousePosition / K;
            }
        }

        /// The mouse move this event, in window units.
        public static Vector2 Delta
        {
            get
            {
                Event e = Event.current;
                if (e == null) return Vector2.zero;
                return K == 1f ? e.delta : e.delta / K;
            }
        }

        // --- style scaling ---------------------------------------------------------

        // The styles switched to pixels for the call in progress (a scroll view
        // switches its scrollbars too), with what to put back.
        private struct Saved
        {
            public GUIStyle S;
            public int Size, Pl, Pr, Pt, Pb, Ol, Or, Ot, Ob, Ml, Mr, Mt, Mb;
            public Vector2 Offset;
            public float Fw, Fh;
        }

        private static readonly Saved[] Stack = new Saved[16];
        private static int _depth;

        private static int ScalePx(int v)
        {
            return v == 0 ? 0 : Mathf.RoundToInt(v * K);
        }

        /// Switches a style to pixels until the matching Pop. A style already
        /// switched (the same one passed twice) is left as it is.
        private static void Push(GUIStyle s)
        {
            if (s == null || _depth >= Stack.Length) return;
            for (int i = 0; i < _depth; i++) if (ReferenceEquals(Stack[i].S, s)) return;

            Saved v = new Saved();
            v.S = s;
            v.Size = s.fontSize;
            int baseSize = CrispText.BaseSize(s);
            if (baseSize > 0) s.fontSize = UiScale.WindowFontPx(baseSize, K);

            RectOffset p = s.padding;
            v.Pl = p.left; v.Pr = p.right; v.Pt = p.top; v.Pb = p.bottom;
            p.left = ScalePx(v.Pl); p.right = ScalePx(v.Pr); p.top = ScalePx(v.Pt); p.bottom = ScalePx(v.Pb);
            RectOffset o = s.overflow;
            v.Ol = o.left; v.Or = o.right; v.Ot = o.top; v.Ob = o.bottom;
            o.left = ScalePx(v.Ol); o.right = ScalePx(v.Or); o.top = ScalePx(v.Ot); o.bottom = ScalePx(v.Ob);
            RectOffset m = s.margin;
            v.Ml = m.left; v.Mr = m.right; v.Mt = m.top; v.Mb = m.bottom;
            m.left = ScalePx(v.Ml); m.right = ScalePx(v.Mr); m.top = ScalePx(v.Mt); m.bottom = ScalePx(v.Mb);
            v.Offset = s.contentOffset;
            if (v.Offset.x != 0f || v.Offset.y != 0f) s.contentOffset = v.Offset * K;
            v.Fw = s.fixedWidth; v.Fh = s.fixedHeight;
            if (v.Fw != 0f) s.fixedWidth = v.Fw * K;
            if (v.Fh != 0f) s.fixedHeight = v.Fh * K;

            Stack[_depth++] = v;
        }

        /// Puts back every style switched since `depth` (Depth before the Pushes).
        private static void PopTo(int depth)
        {
            while (_depth > depth)
            {
                Saved v = Stack[--_depth];
                GUIStyle s = v.S;
                Stack[_depth].S = null;
                if (s == null) continue;
                s.fontSize = v.Size;
                RectOffset p = s.padding;
                p.left = v.Pl; p.right = v.Pr; p.top = v.Pt; p.bottom = v.Pb;
                RectOffset o = s.overflow;
                o.left = v.Ol; o.right = v.Or; o.top = v.Ot; o.bottom = v.Ob;
                RectOffset m = s.margin;
                m.left = v.Ml; m.right = v.Mr; m.top = v.Mt; m.bottom = v.Mb;
                if (v.Offset.x != 0f || v.Offset.y != 0f) s.contentOffset = v.Offset;
                if (v.Fw != 0f) s.fixedWidth = v.Fw;
                if (v.Fh != 0f) s.fixedHeight = v.Fh;
            }
        }

        /// Scales a copy's metrics from its source once (the window's own
        /// style, which IMGUI draws after the call that declares it).
        public static void ScaleInto(GUIStyle dst, GUIStyle src, float k)
        {
            if (dst == null || src == null) return;
            int baseSize = CrispText.BaseSize(src);
            dst.fontSize = baseSize > 0 ? UiScale.WindowFontPx(baseSize, k) : src.fontSize;
            Copy(dst.padding, src.padding, k);
            Copy(dst.overflow, src.overflow, k);
            dst.contentOffset = src.contentOffset * k;
        }

        private static void Copy(RectOffset dst, RectOffset src, float k)
        {
            dst.left = Mathf.RoundToInt(src.left * k); dst.right = Mathf.RoundToInt(src.right * k);
            dst.top = Mathf.RoundToInt(src.top * k); dst.bottom = Mathf.RoundToInt(src.bottom * k);
        }

        // --- controls ------------------------------------------------------------------

        public static void Label(Rect r, string text) { Label(r, text, GUI.skin.label); }
        public static void Label(Rect r, GUIContent c) { Label(r, c, GUI.skin.label); }
        public static void Label(Rect r, Texture t) { GUI.Label(Px(r), t); }

        public static void Label(Rect r, string text, GUIStyle s)
        {
            if (K == 1f) { GUI.Label(r, text, s); return; }
            int d = _depth;
            Push(s);
            try { GUI.Label(Px(r), text, s); }
            finally { PopTo(d); }
        }

        public static void Label(Rect r, GUIContent c, GUIStyle s)
        {
            if (K == 1f) { GUI.Label(r, c, s); return; }
            int d = _depth;
            Push(s);
            try { GUI.Label(Px(r), c, s); }
            finally { PopTo(d); }
        }

        public static bool Button(Rect r, string text) { return Button(r, text, GUI.skin.button); }
        public static bool Button(Rect r, GUIContent c) { return Button(r, c, GUI.skin.button); }
        public static bool Button(Rect r, Texture t) { return Button(r, t, GUI.skin.button); }

        public static bool Button(Rect r, string text, GUIStyle s)
        {
            if (K == 1f) return GUI.Button(r, text, s);
            int d = _depth;
            Push(s);
            try { return GUI.Button(Px(r), text, s); }
            finally { PopTo(d); }
        }

        public static bool Button(Rect r, GUIContent c, GUIStyle s)
        {
            if (K == 1f) return GUI.Button(r, c, s);
            int d = _depth;
            Push(s);
            try { return GUI.Button(Px(r), c, s); }
            finally { PopTo(d); }
        }

        public static bool Button(Rect r, Texture t, GUIStyle s)
        {
            if (K == 1f) return GUI.Button(r, t, s);
            int d = _depth;
            Push(s);
            try { return GUI.Button(Px(r), t, s); }
            finally { PopTo(d); }
        }

        public static void Box(Rect r, string text) { Box(r, text, GUI.skin.box); }
        public static void Box(Rect r, GUIContent c) { Box(r, c, GUI.skin.box); }

        public static void Box(Rect r, string text, GUIStyle s)
        {
            if (K == 1f) { GUI.Box(r, text, s); return; }
            int d = _depth;
            Push(s);
            try { GUI.Box(Px(r), text, s); }
            finally { PopTo(d); }
        }

        public static void Box(Rect r, GUIContent c, GUIStyle s)
        {
            if (K == 1f) { GUI.Box(r, c, s); return; }
            int d = _depth;
            Push(s);
            try { GUI.Box(Px(r), c, s); }
            finally { PopTo(d); }
        }

        public static bool Toggle(Rect r, bool value, string text) { return Toggle(r, value, text, GUI.skin.toggle); }
        public static bool Toggle(Rect r, bool value, GUIContent c) { return Toggle(r, value, c, GUI.skin.toggle); }

        public static bool Toggle(Rect r, bool value, string text, GUIStyle s)
        {
            if (K == 1f) return GUI.Toggle(r, value, text, s);
            int d = _depth;
            Push(s);
            try { return GUI.Toggle(Px(r), value, text, s); }
            finally { PopTo(d); }
        }

        public static bool Toggle(Rect r, bool value, GUIContent c, GUIStyle s)
        {
            if (K == 1f) return GUI.Toggle(r, value, c, s);
            int d = _depth;
            Push(s);
            try { return GUI.Toggle(Px(r), value, c, s); }
            finally { PopTo(d); }
        }

        public static string TextField(Rect r, string text) { return TextField(r, text, -1, GUI.skin.textField); }
        public static string TextField(Rect r, string text, int maxLength) { return TextField(r, text, maxLength, GUI.skin.textField); }
        public static string TextField(Rect r, string text, GUIStyle s) { return TextField(r, text, -1, s); }

        public static string TextField(Rect r, string text, int maxLength, GUIStyle s)
        {
            if (K == 1f) return GUI.TextField(r, text, maxLength, s);
            int d = _depth;
            Push(s);
            try { return GUI.TextField(Px(r), text, maxLength, s); }
            finally { PopTo(d); }
        }

        public static string TextArea(Rect r, string text) { return TextArea(r, text, -1, GUI.skin.textArea); }
        public static string TextArea(Rect r, string text, GUIStyle s) { return TextArea(r, text, -1, s); }

        public static string TextArea(Rect r, string text, int maxLength, GUIStyle s)
        {
            if (K == 1f) return GUI.TextArea(r, text, maxLength, s);
            int d = _depth;
            Push(s);
            try { return GUI.TextArea(Px(r), text, maxLength, s); }
            finally { PopTo(d); }
        }

        public static float HorizontalSlider(Rect r, float value, float left, float right)
        {
            return HorizontalSlider(r, value, left, right, GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
        }

        public static float HorizontalSlider(Rect r, float value, float left, float right, GUIStyle slider, GUIStyle thumb)
        {
            if (K == 1f) return GUI.HorizontalSlider(r, value, left, right, slider, thumb);
            int d = _depth;
            Push(slider);
            Push(thumb);
            try { return GUI.HorizontalSlider(Px(r), value, left, right, slider, thumb); }
            finally { PopTo(d); }
        }

        public static void DrawTexture(Rect r, Texture t) { GUI.DrawTexture(Px(r), t); }
        public static void DrawTexture(Rect r, Texture t, ScaleMode mode) { GUI.DrawTexture(Px(r), t, mode); }
        public static void DrawTexture(Rect r, Texture t, ScaleMode mode, bool alphaBlend) { GUI.DrawTexture(Px(r), t, mode, alphaBlend); }
        public static void DrawTexture(Rect r, Texture t, ScaleMode mode, bool alphaBlend, float aspect) { GUI.DrawTexture(Px(r), t, mode, alphaBlend, aspect); }

        public static void DrawTextureWithTexCoords(Rect r, Texture t, Rect texCoords)
        {
            GUI.DrawTextureWithTexCoords(Px(r), t, texCoords);
        }

        public static void BeginGroup(Rect r) { GUI.BeginGroup(Px(r)); }
        public static void EndGroup() { GUI.EndGroup(); }

        /// The scroll position in and out in window units; the scrollbars
        /// (the skin's) are switched to pixels while they are drawn.
        public static Vector2 BeginScrollView(Rect r, Vector2 scroll, Rect view)
        {
            return BeginScrollView(r, scroll, view, false, false);
        }

        public static Vector2 BeginScrollView(Rect r, Vector2 scroll, Rect view, bool alwaysH, bool alwaysV)
        {
            if (K == 1f) return GUI.BeginScrollView(r, scroll, view, alwaysH, alwaysV);
            GUISkin skin = GUI.skin;
            int d = _depth;
            Push(skin.scrollView);
            Push(skin.horizontalScrollbar);
            Push(skin.horizontalScrollbarThumb);
            Push(skin.horizontalScrollbarLeftButton);
            Push(skin.horizontalScrollbarRightButton);
            Push(skin.verticalScrollbar);
            Push(skin.verticalScrollbarThumb);
            Push(skin.verticalScrollbarUpButton);
            Push(skin.verticalScrollbarDownButton);
            try
            {
                Vector2 px = new Vector2(Mathf.Round(scroll.x * K), Mathf.Round(scroll.y * K));
                Vector2 v = GUI.BeginScrollView(Px(r), px, new Rect(view.x * K, view.y * K, view.width * K, view.height * K), alwaysH, alwaysV);
                // Unchanged: hand back the caller's own value (no drift through rounding).
                return v == px ? scroll : v / K;
            }
            finally { PopTo(d); }
        }

        /// The wheel scrolls as far on screen as it did at the window's
        /// size before (IMGUI moves 20 px a step, in the space it draws in).
        public static void EndScrollView()
        {
            Event e = Event.current;
            if (K == 1f || e == null || e.type != EventType.ScrollWheel) { GUI.EndScrollView(); return; }
            Vector2 delta = e.delta;
            e.delta = delta * K;
            try { GUI.EndScrollView(); }
            finally { if (e.type != EventType.Used) e.delta = delta; }
        }

        public static void DragWindow(Rect r) { GUI.DragWindow(Px(r)); }

        // --- measuring -------------------------------------------------------------------

        /// The size the text is drawn at, in window units.
        public static Vector2 CalcSize(GUIStyle s, GUIContent c)
        {
            if (K == 1f) return s.CalcSize(c);
            int d = _depth;
            Push(s);
            try { return s.CalcSize(c) / K; }
            finally { PopTo(d); }
        }

        /// The height the text takes wrapped at `width` (units), in units.
        public static float CalcHeight(GUIStyle s, GUIContent c, float width)
        {
            if (K == 1f) return s.CalcHeight(c, width);
            int d = _depth;
            Push(s);
            try { return s.CalcHeight(c, width * K) / K; }
            finally { PopTo(d); }
        }
    }
}
