using System;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Remembers the text made from a number, per slot, so a display that
    // refreshes ten times a second (the splits table while a run is on)
    // formats only the values that moved. The same value, decimals and
    // kind at a slot give back the string made last time - exactly what
    // SplitTable would make again, without the garbage.
    //
    // Pure: linked into the tests.
    // ------------------------------------------------------------------
    public sealed class TextMemo
    {
        public enum Kind { Time, Delta, TimeOrEmpty }

        private float[] _value = new float[0];
        private int[] _decimals = new int[0];
        private Kind[] _kind = new Kind[0];
        private string[] _text = new string[0];

        /// SplitTable.Time(v, decimals), remembered at `slot`.
        public string Time(int slot, float v, int decimals) { return Get(slot, v, decimals, Kind.Time); }

        /// SplitTable.Delta(v, decimals), remembered at `slot`.
        public string Delta(int slot, float v, int decimals) { return Get(slot, v, decimals, Kind.Delta); }

        /// "" for NaN, else SplitTable.Time(v, decimals) - the time save column.
        public string TimeOrEmpty(int slot, float v, int decimals) { return Get(slot, v, decimals, Kind.TimeOrEmpty); }

        /// Forgets every slot (the next read formats again).
        public void Clear()
        {
            for (int i = 0; i < _text.Length; i++) _text[i] = null;
        }

        private string Get(int slot, float v, int decimals, Kind kind)
        {
            if (slot < 0) return Make(v, decimals, kind);
            if (slot >= _text.Length) Grow(slot + 1);
            string t = _text[slot];
            if (t != null && _kind[slot] == kind && _decimals[slot] == decimals && SameValue(_value[slot], v)) return t;
            t = Make(v, decimals, kind);
            _text[slot] = t;
            _value[slot] = v;
            _decimals[slot] = decimals;
            _kind[slot] = kind;
            return t;
        }

        // Bit-exact, so NaN matches NaN and -0 is not 0 (it formats the same,
        // but nothing is assumed about the formatter).
        private static bool SameValue(float a, float b)
        {
            return BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b) || (float.IsNaN(a) && float.IsNaN(b));
        }

        private static string Make(float v, int decimals, Kind kind)
        {
            switch (kind)
            {
                case Kind.Delta: return SplitTable.Delta(v, decimals);
                case Kind.TimeOrEmpty: return float.IsNaN(v) ? "" : SplitTable.Time(v, decimals);
                default: return SplitTable.Time(v, decimals);
            }
        }

        private void Grow(int n)
        {
            int size = Math.Max(n, _text.Length * 2);
            Array.Resize(ref _value, size);
            Array.Resize(ref _decimals, size);
            Array.Resize(ref _kind, size);
            Array.Resize(ref _text, size);
        }
    }
}
