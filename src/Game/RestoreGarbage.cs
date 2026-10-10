using System;
using ForestOverlay.Data;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // What a Quick load allocates, step by step (T-0202: ~35 MB and a
    // forced GC a restore). Only while the allocation tracker counts
    // (Debug views, Game/AllocationTracker): each mark reads its
    // main-thread byte count - no cost otherwise. The restore line then
    // ends `garbage (main thread) N MB: <step> N, ...`, and the line a
    // few seconds later says the total since the start and the
    // collections in between.
    // ------------------------------------------------------------------
    public static class RestoreGarbage
    {
        private static readonly StepBytes Steps = new StepBytes();
        private static long _startBytes;
        private static int _gcStart;
        private static bool _on;

        private static bool On { get { return _on && AllocationTracker.Counting; } }

        public static void Begin()
        {
            _on = AllocationTracker.Counting;
            if (!_on) return;
            _startBytes = AllocationTracker.MainBytesEver;
            Steps.Begin(_startBytes);
            _gcStart = GC.CollectionCount(0);
        }

        public static void Mark(string step)
        {
            if (!On) return;
            Steps.Mark(step, AllocationTracker.MainBytesEver);
        }

        /// "garbage (main thread) N MB: ..., GC xN" or "".
        public static string Describe()
        {
            if (!On || !Steps.Started) return "";
            return "garbage (main thread) " + Steps.Describe() + ", GC x" + (GC.CollectionCount(0) - _gcStart);
        }

        /// This restore's start, kept by the caller for the line a few
        /// seconds later (the next restore may have begun by then).
        public struct Start
        {
            public bool On;
            public long Bytes;
            public int Gc;
        }

        public static Start Current()
        {
            Start s = new Start();
            s.On = On && Steps.Started;
            s.Bytes = _startBytes;
            s.Gc = _gcStart;
            return s;
        }

        /// Since `start`; "" when the tracker was or is off.
        public static string Since(Start start)
        {
            if (!start.On || !AllocationTracker.Counting) return "";
            return "garbage (main thread) since the restore started " +
                   StepBytes.Mb(AllocationTracker.MainBytesEver - start.Bytes) + " MB, GC x" + (GC.CollectionCount(0) - start.Gc);
        }
    }
}
