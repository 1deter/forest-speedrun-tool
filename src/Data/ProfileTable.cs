using System;
using System.Collections.Generic;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The counters behind the game profiler (Game/GameProfiler) and its
    // report: per hooked method, calls, time and heap growth; every 30 s
    // the top methods by time and by allocation.
    //
    // Pure and allocation-free on the hot path (Record): the arrays grow
    // only when a method is added. Report builds the text, twice a minute.
    //
    // Heap figures are GC.GetTotalMemory deltas: Boehm's used size moves
    // in heap blocks, and allocation into the free space of a partly used
    // block does not show at all, so they undercount - read them as a
    // ranking, not as bytes. A delta that went down means a collection ran
    // inside the call; it counts as a GC hit for that method, not as
    // allocation, and its time (the pause) stays in the method's max.
    // ------------------------------------------------------------------
    public sealed class ProfileTable
    {
        private string[] _names = new string[64];
        private long[] _ticks = new long[64];
        private long[] _maxTicks = new long[64];
        private int[] _calls = new int[64];
        private long[] _bytes = new long[64];
        private int[] _gcHits = new int[64];
        private int _count;

        public int Count { get { return _count; } }

        public string Name(int slot) { return _names[slot]; }

        /// A new slot for a method; returns its index.
        public int Add(string name)
        {
            if (_count == _names.Length)
            {
                int n = _names.Length * 2;
                Array.Resize(ref _names, n);
                Array.Resize(ref _ticks, n);
                Array.Resize(ref _maxTicks, n);
                Array.Resize(ref _calls, n);
                Array.Resize(ref _bytes, n);
                Array.Resize(ref _gcHits, n);
            }
            _names[_count] = name;
            return _count++;
        }

        /// One call: its duration in Stopwatch ticks and the heap's change.
        public void Record(int slot, long ticks, long heapDelta)
        {
            if (slot < 0 || slot >= _count) return;
            _calls[slot]++;
            _ticks[slot] += ticks;
            if (ticks > _maxTicks[slot]) _maxTicks[slot] = ticks;
            if (heapDelta > 0) _bytes[slot] += heapDelta;
            else if (heapDelta < 0) _gcHits[slot]++;
        }

        public void Reset()
        {
            for (int i = 0; i < _count; i++)
            {
                _ticks[i] = 0;
                _maxTicks[i] = 0;
                _calls[i] = 0;
                _bytes[i] = 0;
                _gcHits[i] = 0;
            }
        }

        public long TotalTicks()
        {
            long t = 0;
            for (int i = 0; i < _count; i++) t += _ticks[i];
            return t;
        }

        public long TotalCalls()
        {
            long c = 0;
            for (int i = 0; i < _count; i++) c += _calls[i];
            return c;
        }

        public long TotalBytes()
        {
            long b = 0;
            for (int i = 0; i < _count; i++) b += _bytes[i];
            return b;
        }

        /// The report: a header, then the top methods by time per frame, by
        /// heap growth per second, and where collections ran. Empty when
        /// nothing was recorded.
        ///   Game profile (30 s, 5400 frames, 312 methods): hooked 2.41 ms/frame, 5321 calls/frame, heap +380 KB/s
        ///   time: mutantAI.Update 0.41 ms/f (x12/f, max 3.2 ms), ...
        ///   alloc: X.Update 120 KB/s (x1/f), ...
        ///   GC in: Y.LateUpdate x2
        public List<string> Report(float seconds, int frames, long tickFrequency, int top)
        {
            List<string> lines = new List<string>();
            if (frames <= 0 || seconds <= 0f || tickFrequency <= 0) return lines;
            long calls = TotalCalls();
            if (calls == 0) return lines;

            double msPerTick = 1000.0 / tickFrequency;
            lines.Add("Game profile (" + seconds.ToString("0") + " s, " + frames + " frames, " + _count + " methods): hooked " +
                      (TotalTicks() * msPerTick / frames).ToString("0.00") + " ms/frame, " +
                      (calls / (double)frames).ToString("0") + " calls/frame, heap +" +
                      (TotalBytes() / 1024.0 / seconds).ToString("0") + " KB/s");

            StringBuilder sb = new StringBuilder("time: ");
            int[] order = Ranked(_ticks, top);
            for (int k = 0; k < order.Length; k++)
            {
                int i = order[k];
                if (k > 0) sb.Append(", ");
                sb.Append(_names[i]).Append(' ')
                  .Append((_ticks[i] * msPerTick / frames).ToString("0.00")).Append(" ms/f (x")
                  .Append(PerFrame(_calls[i], frames)).Append("/f, max ")
                  .Append((_maxTicks[i] * msPerTick).ToString("0.0")).Append(" ms)");
            }
            if (order.Length > 0) lines.Add(sb.ToString());

            order = Ranked(_bytes, top);
            if (order.Length > 0)
            {
                sb = new StringBuilder("alloc: ");
                for (int k = 0; k < order.Length; k++)
                {
                    int i = order[k];
                    if (k > 0) sb.Append(", ");
                    sb.Append(_names[i]).Append(' ')
                      .Append((_bytes[i] / 1024.0 / seconds).ToString("0.0")).Append(" KB/s (x")
                      .Append(PerFrame(_calls[i], frames)).Append("/f)");
                }
                lines.Add(sb.ToString());
            }

            long[] hits = new long[_count];
            for (int i = 0; i < _count; i++) hits[i] = _gcHits[i];
            order = Ranked(hits, top);
            if (order.Length > 0)
            {
                sb = new StringBuilder("GC in: ");
                for (int k = 0; k < order.Length; k++)
                {
                    int i = order[k];
                    if (k > 0) sb.Append(", ");
                    sb.Append(_names[i]).Append(" x").Append(_gcHits[i])
                      .Append(" (max ").Append((_maxTicks[i] * msPerTick).ToString("0")).Append(" ms)");
                }
                lines.Add(sb.ToString());
            }
            return lines;
        }

        /// Extra methods to hook, from the config: "Type::Method" (a type's
        /// full name, or its short name), "Type::*" (every method it
        /// declares) or "*::Method" (that method on every game type),
        /// separated by commas, semicolons or new lines.
        /// Returns [type, method] pairs; malformed entries are skipped.
        public static List<string[]> ParseExtra(string text)
        {
            List<string[]> list = new List<string[]>();
            if (string.IsNullOrEmpty(text)) return list;
            string[] parts = text.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                int at = p.LastIndexOf("::", StringComparison.Ordinal);
                if (at <= 0 || at + 2 >= p.Length) continue;
                string type = p.Substring(0, at).Trim();
                string method = p.Substring(at + 2).Trim();
                if (type.Length == 0 || method.Length == 0) continue;
                list.Add(new[] { type, method });
            }
            return list;
        }

        private static string PerFrame(int calls, int frames)
        {
            double f = calls / (double)frames;
            return f >= 10 ? f.ToString("0") : f.ToString("0.#");
        }

        /// Indices of the `top` largest non-zero values, largest first;
        /// ties keep slot order.
        private int[] Ranked(long[] values, int top)
        {
            List<int> idx = new List<int>();
            for (int i = 0; i < _count; i++) if (values[i] > 0) idx.Add(i);
            idx.Sort(delegate(int a, int b)
            {
                int c = values[b].CompareTo(values[a]);
                return c != 0 ? c : a.CompareTo(b);
            });
            if (idx.Count > top) idx.RemoveRange(top, idx.Count - top);
            return idx.ToArray();
        }
    }
}
