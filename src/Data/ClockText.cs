using System;
using System.Globalization;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The running clock as text, built in one go for the HUD (ten times a
    // second while a run is on) and the replay camera's time: the old way
    // was m.ToString("00") + ":" + s.ToString("00.000") + the delta's own
    // strings, about six allocations and ~250 bytes per refresh for one
    // line. This writes the digits into a reused buffer and makes the one
    // string.
    //
    // The same look as PracticeRunModule.Format ("mm:ss.mmm") and its
    // signed delta ("+1.23", two decimals), with the current culture's
    // decimal separator. The milliseconds are rounded from the double
    // value, so a clock can differ from the float formatter's digit by 1 ms
    // at an exact rounding boundary - a display of a running clock, never
    // a saved time (Format still makes those).
    //
    // Main thread only (one shared buffer). Pure: linked into the tests.
    // ------------------------------------------------------------------
    public static class ClockText
    {
        private static readonly char[] Buffer = new char[48];

        /// "01:05.250" for 65.25 s.
        public static string Clock(float seconds)
        {
            int n = WriteClock(0, seconds);
            return n < 0 ? Fallback(seconds) : new string(Buffer, 0, n);
        }

        /// "01:05.250   +0.45": the clock, three spaces, the signed delta.
        public static string ClockWithDelta(float seconds, float delta)
        {
            int n = WriteClock(0, seconds);
            if (n < 0) return Fallback(seconds) + "   " + Signed(delta);
            int m = WriteSigned(n, "   ", delta);
            return m < 0 ? new string(Buffer, 0, n) + "   " + Signed(delta) : new string(Buffer, 0, m);
        }

        /// "+1.23" / "-0.45".
        public static string Signed(float d)
        {
            return (d >= 0f ? "+" : "-") + Math.Abs(d).ToString("0.00");
        }

        // Digits at `at`; the end index, or -1 when the value is not a plain
        // finite non-negative number (the caller falls back to the formatter).
        private static int WriteClock(int at, float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f || seconds > 3.0e6f) return -1;
            int m = (int)(seconds / 60f);
            float s = seconds - m * 60f;
            long ms = (long)Math.Round(s * 1000.0, MidpointRounding.AwayFromZero);
            long whole = ms / 1000, frac = ms % 1000;

            at = Int(at, m, 2);
            Buffer[at++] = ':';
            at = Int(at, whole, 2);
            at = Separator(at);
            return Int(at, frac, 3);
        }

        private static int WriteSigned(int at, string prefix, float d)
        {
            float a = Math.Abs(d);
            if (float.IsNaN(a) || float.IsInfinity(a) || a > 1.0e6f) return -1;
            for (int i = 0; i < prefix.Length; i++) Buffer[at++] = prefix[i];
            Buffer[at++] = d >= 0f ? '+' : '-';
            long h = (long)Math.Round(a * 100.0, MidpointRounding.AwayFromZero);
            at = Int(at, h / 100, 1);
            at = Separator(at);
            return Int(at, h % 100, 2);
        }

        private static int Separator(int at)
        {
            string sep = NumberFormatInfo.CurrentInfo.NumberDecimalSeparator;
            if (sep == null || sep.Length == 0) sep = ".";
            for (int i = 0; i < sep.Length && at < Buffer.Length - 8; i++) Buffer[at++] = sep[i];
            return at;
        }

        // `v` (>= 0) in at least `width` digits, zero padded.
        private static int Int(int at, long v, int width)
        {
            int digits = 1;
            for (long t = v / 10; t > 0; t /= 10) digits++;
            int len = Math.Max(digits, width);
            for (int i = len - 1; i >= 0; i--)
            {
                Buffer[at + i] = (char)('0' + (int)(v % 10));
                v /= 10;
            }
            return at + len;
        }

        private static string Fallback(float seconds)
        {
            int m = (int)(seconds / 60f);
            float s = seconds - m * 60f;
            return m.ToString("00") + ":" + s.ToString("00.000");
        }
    }
}
