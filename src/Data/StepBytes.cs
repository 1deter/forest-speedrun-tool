using System.Collections.Generic;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Bytes allocated per named step of something long (a restore,
    // T-0202): the caller reads a running byte count at each step's end
    // and hands it in; this keeps the differences and says them in one
    // line, biggest steps first. A step seen twice adds up (a loop's
    // frames). Pure, so it is tested; Game/RestoreGarbage feeds it.
    // ------------------------------------------------------------------
    public sealed class StepBytes
    {
        private readonly List<string> _names = new List<string>();
        private readonly List<long> _bytes = new List<long>();
        private long _start;
        private long _last;

        public bool Started { get; private set; }

        /// Total bytes since Begin.
        public long Total { get { return _last - _start; } }

        public void Begin(long counter)
        {
            _names.Clear();
            _bytes.Clear();
            _start = counter;
            _last = counter;
            Started = true;
        }

        /// The bytes since the last mark go to `step`.
        public void Mark(string step, long counter)
        {
            if (!Started) return;
            long d = counter - _last;
            _last = counter;
            int i = _names.IndexOf(step);
            if (i >= 0) { _bytes[i] += d; return; }
            _names.Add(step);
            _bytes.Add(d);
        }

        public long BytesOf(string step)
        {
            int i = _names.IndexOf(step);
            return i >= 0 ? _bytes[i] : 0;
        }

        /// "12.3 MB: LoadNow 5.1, the loader's frames 4.0, ..." - steps
        /// under 0.05 MB are left out (named as "N small").
        public string Describe()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Mb(Total)).Append(" MB");
            List<int> order = new List<int>();
            for (int i = 0; i < _names.Count; i++) order.Add(i);
            order.Sort(delegate(int a, int b)
            {
                int c = _bytes[b].CompareTo(_bytes[a]);
                return c != 0 ? c : a.CompareTo(b);
            });
            int shown = 0, small = 0;
            for (int k = 0; k < order.Count; k++)
            {
                long b = _bytes[order[k]];
                if (b < 50 * 1024) { small++; continue; }
                sb.Append(shown == 0 ? ": " : ", ").Append(_names[order[k]]).Append(' ').Append(Mb(b));
                shown++;
            }
            if (small > 0) sb.Append(shown == 0 ? ": " : ", ").Append(small).Append(" small");
            return sb.ToString();
        }

        public static string Mb(long bytes)
        {
            double mb = bytes / (1024.0 * 1024.0);
            return mb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
