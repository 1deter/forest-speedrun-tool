using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // Text that stays sharp in the scaled HUD (T-0333).
    //
    // The HUD pieces draw in a virtual screen through a scaled GUI.matrix
    // (T-0318). IMGUI rasterises a glyph at the style's font size and the
    // matrix then stretches the bitmap: at 2560x1440 with a 1366x768
    // reference (x1.88) the column, splits and results text was blurry,
    // while a placed value - rendered large and scaled DOWN (T-0022) - was
    // crisp (author, 2026-10-11).
    //
    // So under a scale f these draw at the real pixel size: the style's
    // font size times f, inside a local 1/f scale at the rect's corner, and
    // measure the same way so wrapping and heights match what is drawn. At
    // f = 1 (sizes in pixels) they are plain GUI calls; in the F2 window
    // (drawn at identity in pixels, T-0360) they are WinPx's.
    // The style's fontSize is changed around the call and put back (the
    // trend colour does the same with the text colour), so no style copies
    // and a palette switch still reaches them; the padding is scaled the
    // same way, so right-aligned cells keep their margin. The real size is
    // a whole pixel size, so text can be up to half a pixel per scale step
    // smaller or larger than the stretched look was. Nothing allocates.
    // ------------------------------------------------------------------
    public static class CrispText
    {
        /// The skin font's own size (a style with fontSize 0 uses it); read
        /// once in OnGUI, 0 if it could not be read (then text is drawn as before).
        public static int SkinFontSize = -1;

        /// The GUI scale in force: the matrix's x scale (the HUD has no rotation).
        private static float Scale()
        {
            float f = GUI.matrix.m00;
            return f > 0.99f && f < 1.01f ? 1f : f;
        }

        /// The size a style draws at: its own, its font's, else the skin font's.
        public static int BaseSize(GUIStyle s)
        {
            if (s.fontSize > 0) return s.fontSize;
            if (s.font != null) return s.font.fontSize;
            if (SkinFontSize < 0)
            {
                Font font = GUI.skin.font;
                SkinFontSize = font != null ? font.fontSize : 0;
            }
            return SkinFontSize;
        }

        /// The font size to draw at under scale f; 0 = draw as is.
        private static int RealSize(GUIStyle s, float f)
        {
            if (f == 1f || s == null) return 0;
            int size = BaseSize(s);
            return size > 0 ? Mathf.Max(1, Mathf.RoundToInt(size * f)) : 0;
        }

        // The style as it was, while it is switched to the real size.
        private static int _size, _pl, _pr, _pt, _pb;

        private static void Enlarge(GUIStyle s, int real, float f)
        {
            _size = s.fontSize;
            s.fontSize = real;
            RectOffset pad = s.padding;
            _pl = pad.left; _pr = pad.right; _pt = pad.top; _pb = pad.bottom;
            pad.left = Mathf.RoundToInt(_pl * f); pad.right = Mathf.RoundToInt(_pr * f);
            pad.top = Mathf.RoundToInt(_pt * f); pad.bottom = Mathf.RoundToInt(_pb * f);
        }

        private static void Restore(GUIStyle s)
        {
            s.fontSize = _size;
            RectOffset pad = s.padding;
            pad.left = _pl; pad.right = _pr; pad.top = _pt; pad.bottom = _pb;
        }

        // In the F2 window (WinPx.K != 1) these hand over to WinPx: its groups
        // clip before any matrix, so the enlarge-inside-1/f trick was cut at
        // 1/f of its width there (gotcha 112); the window draws in pixels.

        public static void Label(Rect r, GUIContent c, GUIStyle s)
        {
            if (WinPx.K != 1f) { WinPx.Label(r, c, s); return; }
            Halo(r, c, s, true);
            Draw(r, c, s);
        }

        /// The text only, no outline: for a pair that cross-fades (the input
        /// overlay's idle / lit caps) after one Outline for the pair.
        public static void LabelPlain(Rect r, GUIContent c, GUIStyle s)
        {
            if (WinPx.K != 1f) { WinPx.Label(r, c, s); return; }
            Draw(r, c, s);
        }

        /// The outline only (in the HUD; nothing in the F2 window), at the
        /// style's text alpha and GUI.color.
        public static void Outline(Rect r, GUIContent c, GUIStyle s)
        {
            if (WinPx.K == 1f) Halo(r, c, s, true);
        }

        /// A HUD value drawn as is (GUI.Label, no enlarge trick) with the same
        /// dark outline. The placed values draw under their own scale; the
        /// matrix scale is read, so the ring keeps the game's width.
        public static void LabelOutlined(Rect r, GUIContent c, GUIStyle s)
        {
            Halo(r, c, s, false);
            GUI.Label(r, c, s);
        }

        // The game's bottom-left messages are NGUI labels with effectStyle
        // Outline8, black, distance (1, 1) (bridge 2026-10-16, T-0335): the
        // text drawn eight times around itself in black, then once on top.
        // The HUD gets the same so it reads over sky and snow; the F2 window
        // (K != 1) has its own panels and is left alone. One NGUI unit is
        // Screen.height / 720 px (the HUD's own virtual unit is Screen.height /
        // 768 at the 1366x768 reference: about 6 % off, so the offset is
        // computed from the real pixels, not taken as 1 virtual unit);
        // Data/TextOutline holds the directions and that arithmetic. Only on
        // Repaint: the other GUI events draw nothing.
        private static void Halo(Rect r, GUIContent c, GUIStyle s, bool crisp)
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.Repaint) return;
            if (s == null || c == null || string.IsNullOrEmpty(c.text)) return;
            Color keep = s.normal.textColor;
            if (keep.a <= 0f) return;
            float dist = TextOutline.Distance(Screen.height, GUI.matrix.m00);
            s.normal.textColor = new Color(0f, 0f, 0f, keep.a);
            for (int i = 0; i < 8; i++)
            {
                Rect o = new Rect(r.x + TextOutline.Dx[i] * dist, r.y + TextOutline.Dy[i] * dist, r.width, r.height);
                if (crisp) Draw(o, c, s); else GUI.Label(o, c, s);
            }
            s.normal.textColor = keep;
        }

        private static void Draw(Rect r, GUIContent c, GUIStyle s)
        {
            float f = Scale();
            int real = RealSize(s, f);
            if (real == 0) { GUI.Label(r, c, s); return; }
            Matrix4x4 m = GUI.matrix;
            Enlarge(s, real, f);
            Vector3 at = new Vector3(r.x, r.y, 0f);
            GUI.matrix = m * Matrix4x4.TRS(at, Quaternion.identity, new Vector3(1f / f, 1f / f, 1f)) * Matrix4x4.TRS(-at, Quaternion.identity, Vector3.one);
            GUI.Label(new Rect(r.x, r.y, r.width * f, r.height * f), c, s);
            GUI.matrix = m;
            Restore(s);
        }

        public static Vector2 CalcSize(GUIStyle s, GUIContent c)
        {
            if (WinPx.K != 1f) return WinPx.CalcSize(s, c);
            float f = Scale();
            int real = RealSize(s, f);
            if (real == 0) return s.CalcSize(c);
            Enlarge(s, real, f);
            Vector2 v = s.CalcSize(c);
            Restore(s);
            return v / f;
        }

        public static float CalcHeight(GUIStyle s, GUIContent c, float width)
        {
            if (WinPx.K != 1f) return WinPx.CalcHeight(s, c, width);
            float f = Scale();
            int real = RealSize(s, f);
            if (real == 0) return s.CalcHeight(c, width);
            Enlarge(s, real, f);
            float h = s.CalcHeight(c, width * f);
            Restore(s);
            return h / f;
        }
    }
}
