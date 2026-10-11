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
    // f = 1 (the F2 window, sizes in pixels) they are plain GUI calls.
    // The style's fontSize is changed around the call and put back (the
    // trend colour does the same with the text colour), so no style copies
    // and a palette switch still reaches them. Nothing allocates.
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

        private static int BaseSize(GUIStyle s)
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

        public static void Label(Rect r, GUIContent c, GUIStyle s)
        {
            float f = Scale();
            int real = RealSize(s, f);
            if (real == 0) { GUI.Label(r, c, s); return; }
            Matrix4x4 m = GUI.matrix;
            int old = s.fontSize;
            s.fontSize = real;
            Vector3 at = new Vector3(r.x, r.y, 0f);
            GUI.matrix = m * Matrix4x4.TRS(at, Quaternion.identity, new Vector3(1f / f, 1f / f, 1f)) * Matrix4x4.TRS(-at, Quaternion.identity, Vector3.one);
            GUI.Label(new Rect(r.x, r.y, r.width * f, r.height * f), c, s);
            GUI.matrix = m;
            s.fontSize = old;
        }

        public static Vector2 CalcSize(GUIStyle s, GUIContent c)
        {
            float f = Scale();
            int real = RealSize(s, f);
            if (real == 0) return s.CalcSize(c);
            int old = s.fontSize;
            s.fontSize = real;
            Vector2 v = s.CalcSize(c);
            s.fontSize = old;
            return v / f;
        }

        public static float CalcHeight(GUIStyle s, GUIContent c, float width)
        {
            float f = Scale();
            int real = RealSize(s, f);
            if (real == 0) return s.CalcHeight(c, width);
            int old = s.fontSize;
            s.fontSize = real;
            float h = s.CalcHeight(c, width * f);
            s.fontSize = old;
            return h / f;
        }
    }
}
