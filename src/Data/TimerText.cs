using System;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The splits panel's timer (T-0336, LiveSplit's Timer component) as
    // characters: the panel draws it every frame, one cached GUIContent a
    // character, so a running clock costs no string at all. The text reads
    // as SplitTable.Time ("12.34", "1:02.34", "1:02:03.45"), always with a
    // '.' - LiveSplit's look, whatever the culture.
    //
    // Pure: linked into the tests.
    // ------------------------------------------------------------------
    public static class TimerText
    {
        /// Longest text Write makes (hours up to 999 at 3 decimals).
        public const int MaxLength = 16;

        /// `t` seconds at `decimals` (0-3) places into `buf` (MaxLength
        /// long); returns the length. Unknown or negative reads as 0.
        public static int Write(char[] buf, float t, int decimals)
        {
            if (float.IsNaN(t) || float.IsInfinity(t) || t < 0f) t = 0f;
            if (t > 3.0e6f) t = 3.0e6f;
            decimals = Math.Max(0, Math.Min(3, decimals));
            long scale = 1;
            for (int i = 0; i < decimals; i++) scale *= 10;
            long u = (long)Math.Round(t * (double)scale);
            long whole = u / scale, frac = u % scale;
            long h = whole / 3600, m = whole / 60 % 60, s = whole % 60;

            int at = 0;
            if (h > 0)
            {
                at = Int(buf, at, h, 1);
                buf[at++] = ':';
                at = Int(buf, at, m, 2);
                buf[at++] = ':';
                at = Int(buf, at, s, 2);
            }
            else if (m > 0)
            {
                at = Int(buf, at, m, 1);
                buf[at++] = ':';
                at = Int(buf, at, s, 2);
            }
            else at = Int(buf, at, s, 1);
            if (decimals > 0)
            {
                buf[at++] = '.';
                at = Int(buf, at, frac, decimals);
            }
            return at;
        }

        // `v` with at least `digits` digits at `at`; the end index.
        private static int Int(char[] buf, int at, long v, int digits)
        {
            int n = 1;
            for (long p = v; p >= 10; p /= 10) n++;
            if (n < digits) n = digits;
            for (int i = n - 1; i >= 0; i--)
            {
                buf[at + i] = (char)('0' + (int)(v % 10));
                v /= 10;
            }
            return at + n;
        }
    }
}
