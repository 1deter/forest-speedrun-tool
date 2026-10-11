using System;

namespace ForestOverlay.Data
{
    /// What a profile number measures, so it can follow a change of the
    /// reference screen (T-0327).
    public enum ProfileUnit { None, X, Y, Size }

    // ------------------------------------------------------------------
    // A profile file is shared between screens. Its positions and sizes are
    // in reference pixels (Data/UiScale), so the file records the reference
    // it was saved at (`@Reference.Width`, `@Reference.Height`) and loading
    // it on another reference converts: an x by the widths' ratio, a y and a
    // size by the heights' ratio (sizes follow the screen's height, as
    // UiScale.F). A negative value is a sentinel (-1 centred / docked) and
    // stays. A file without the keys is not converted. Pure; linked into the tests.
    // ------------------------------------------------------------------
    public static class ProfileUnits
    {
        public const string WidthKey = "Reference.Width", HeightKey = "Reference.Height";

        /// True when a stored reference (fw x fh) needs converting to (tw x th).
        public static bool Differs(int fw, int fh, int tw, int th)
        {
            return fw > 0 && fh > 0 && tw > 0 && th > 0 && (fw != tw || fh != th);
        }

        public static float Convert(float v, ProfileUnit unit, int fw, int fh, int tw, int th)
        {
            if (unit == ProfileUnit.None || v < 0f || !Differs(fw, fh, tw, th)) return v;
            double r = unit == ProfileUnit.X ? (double)tw / fw : (double)th / fh;
            return (float)Math.Round(v * r);
        }

        /// Reads a stored reference number; 0 when absent or not a number.
        public static int ParseDim(string s)
        {
            int n;
            return s != null && int.TryParse(s.Trim(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out n) && n > 0 ? n : 0;
        }
    }
}
