using System;
using System.Globalization;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The position part of SavestateFile.PickupKey ("x,y,z" to 0.1 m) as
    // a number: two positions give equal numbers exactly when they give
    // equal text. For lookups that run over hundreds of objects every
    // restore (Game/PanelKeeper: 490 cave panels) - the text costs three
    // number formats and a concat per object, ~0.9 MB of garbage a
    // restore (T-0202); the number costs nothing. The file keeps the text.
    // ------------------------------------------------------------------
    public static class PositionKey
    {
        private const long Offset = 1L << 20;   // +-104857.5 m in tenths
        private const long Mask = (1L << 21) - 1;

        public static long Of(float x, float y, float z)
        {
            return Pack(Tenths(x), Tenths(y), Tenths(z));
        }

        /// PickupKey's rounding: Math.Round(v, 1), then the tenths.
        private static long Tenths(double v)
        {
            return (long)Math.Round(Math.Round(v, 1) * 10.0);
        }

        private static long Pack(long x, long y, long z)
        {
            return (((x + Offset) & Mask) << 42) | (((y + Offset) & Mask) << 21) | ((z + Offset) & Mask);
        }

        /// "x,y,z" from `start` to the end of `s` (PickupKey's position
        /// part). False when it is not three numbers.
        public static bool TryParse(string s, int start, out long key)
        {
            key = 0;
            if (s == null || start < 0 || start > s.Length) return false;
            long x, y, z;
            int i = start;
            if (!TenthsAt(s, ref i, out x) || i >= s.Length || s[i++] != ',') return false;
            if (!TenthsAt(s, ref i, out y) || i >= s.Length || s[i++] != ',') return false;
            if (!TenthsAt(s, ref i, out z) || i != s.Length) return false;
            key = Pack(x, y, z);
            return true;
        }

        /// "<int>@x,y,z" (a `panels` entry): the int and the position's key.
        public static bool TryParseEntry(string e, out int value, out long key)
        {
            value = 0;
            key = 0;
            if (e == null) return false;
            int at = e.IndexOf('@');
            if (at <= 0) return false;
            int i = 0;
            bool neg = e[0] == '-';
            if (neg) i++;
            if (i >= at) return false;
            long v = 0;
            for (; i < at; i++)
            {
                char c = e[i];
                if (c < '0' || c > '9') return false;
                v = v * 10 + (c - '0');
                if (v > int.MaxValue) return false;
            }
            value = (int)(neg ? -v : v);
            return TryParse(e, at + 1, out key);
        }

        // A number as PickupKey writes it ("-442.2", "0.0"); anything else
        // (more decimals, an exponent) goes through double.Parse and the
        // same rounding.
        private static bool TenthsAt(string s, ref int i, out long tenths)
        {
            tenths = 0;
            int begin = i;
            int end = s.IndexOf(',', i);
            if (end < 0) end = s.Length;
            if (end == begin) return false;
            int j = begin;
            bool neg = s[j] == '-';
            if (neg) j++;
            long whole = 0;
            int digits = 0;
            while (j < end && s[j] >= '0' && s[j] <= '9') { whole = whole * 10 + (s[j] - '0'); j++; digits++; }
            if (digits > 0 && digits < 16 && j + 2 == end && s[j] == '.' && s[j + 1] >= '0' && s[j + 1] <= '9')
            {
                long t = whole * 10 + (s[j + 1] - '0');
                tenths = neg ? -t : t;
                i = end;
                return true;
            }
            double d;
            if (!double.TryParse(s.Substring(begin, end - begin), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return false;
            tenths = Tenths(d);
            i = end;
            return true;
        }
    }
}
