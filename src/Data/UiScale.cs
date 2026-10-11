using System;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // How the placed HUD pieces follow the screen (T-0318, author
    // 2026-10-11). Every saved position and size (HUD column, free values,
    // splits panel, results panel, run code box) is in REFERENCE pixels:
    // the pixels of the screen the layout was made at. That is why an old
    // config needs no migration of its numbers - the first load simply
    // records the current screen as the reference (SetReference), and the
    // numbers mean exactly what they did.
    //
    // At another screen the pieces are drawn in a VIRTUAL screen (VW x VH):
    //   - sizes follow (default): the GUI is scaled by F = screenH / RefH,
    //     so the look is the same at every resolution; VW = screenW / F,
    //     VH = screenH / F (VH = RefH);
    //   - sizes in pixels (option): F = 1, VW x VH is the screen itself.
    // A position keeps its place relative to the frame either way:
    // PosX(r) = r * VW / RefW (and Y), ToRefX / ToRefY go back for saving.
    // A negative value is a sentinel (-1: centred / docked) and stays as is.
    // Pure and allocation free; linked into the tests.
    // ------------------------------------------------------------------
    public sealed class UiScale
    {
        public const float MinFactor = 0.25f, MaxFactor = 4f;

        public int RefW, RefH;
        /// True: sizes follow the screen height. False: sizes stay pixels.
        public bool SizesFollow = true;

        /// The GUI scale (1 when sizes are pixels or no reference yet).
        public float F = 1f;
        /// The screen the pieces are drawn in (screen / F).
        public float VW, VH;

        private int _sw, _sh, _refW, _refH;
        private bool _follow, _have;

        public bool HasReference { get { return RefW > 0 && RefH > 0; } }

        public void SetReference(int w, int h)
        {
            RefW = w;
            RefH = h;
            _have = false;
        }

        /// Recomputes for this frame's screen; true when the result changed
        /// (a resolution change, or the option flipped). Cheap enough for
        /// every OnGUI pass.
        public bool Update(int sw, int sh)
        {
            if (sw < 1) sw = 1;
            if (sh < 1) sh = 1;
            if (_have && sw == _sw && sh == _sh && _refW == RefW && _refH == RefH && _follow == SizesFollow) return false;
            _have = true;
            _sw = sw; _sh = sh; _refW = RefW; _refH = RefH; _follow = SizesFollow;
            F = HasReference && SizesFollow ? Clamp((float)sh / RefH) : 1f;
            VW = sw / F;
            VH = sh / F;
            return true;
        }

        private static float Clamp(float f)
        {
            return f < MinFactor ? MinFactor : f > MaxFactor ? MaxFactor : f;
        }

        /// A saved (reference) x in the virtual screen; negative stays.
        public float PosX(float r)
        {
            return r < 0f || !HasReference ? r : r * VW / RefW;
        }

        public float PosY(float r)
        {
            return r < 0f || !HasReference ? r : r * VH / RefH;
        }

        /// A virtual-screen x as the value to save.
        public float ToRefX(float v)
        {
            return !HasReference ? v : v * RefW / VW;
        }

        public float ToRefY(float v)
        {
            return !HasReference ? v : v * RefH / VH;
        }

        /// The one line logged when the screen changes: "1920x1080, GUI x1".
        public string Describe()
        {
            return _sw + "x" + _sh + " (reference " + RefW + "x" + RefH + "), " +
                   (SizesFollow ? "sizes follow the screen, GUI x" + Math.Round(F, 2).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                                : "sizes in pixels");
        }
    }
}
