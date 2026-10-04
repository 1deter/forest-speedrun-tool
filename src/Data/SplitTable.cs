using System;
using System.Collections.Generic;
using System.Globalization;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // LiveSplit-style splits for a timed segment (author, 2026-09-27: a
    // customisable splits view built from our own attempts first, an .lss
    // import after).
    //
    // A segment with n checkpoints has n + 1 rows: one per checkpoint, the
    // end last. Everything is cumulative seconds from the start (LiveSplit's
    // "split time"); a row's segment time is its split minus the previous
    // one. NaN means unknown - an attempt recorded before split times were
    // saved (v0.24.146) knows its end only, and a row it cannot place stays
    // blank rather than inventing a time.
    //
    // A comparison is a float[rows] of cumulative times. Our own ones come
    // from SplitStats; other runners' (a .foseg, community packs, the
    // website) are the same array, so they plug in without touching this.
    //
    // Pure: linked into the tests.
    // ------------------------------------------------------------------
    public enum SplitColour { None, AheadGaining, AheadLosing, BehindGaining, BehindLosing, Gold }

    public struct SplitRow
    {
        public float Compare;         // the comparison's split time here
        public float CompareSegment;  // the comparison's time for this row alone
        public float Time;            // this run's split time; NaN = not reached
        public float Segment;         // this run's time for this row alone
        public float Delta;           // Time - Compare; on the current row, live
        public float SegmentDelta;    // Segment - CompareSegment
        public float BestSegment;     // the gold for this row
        public float TimeSave;        // CompareSegment - BestSegment (>= 0)
        public bool Live;             // Delta is the running one (current row)
        public bool Current;          // the row being run
        public SplitColour Colour;
    }

    public struct SplitSummary
    {
        public float PreviousSegment;   // the last finished row's segment delta (live when losing on the current one)
        public bool PreviousLive;
        public float SumOfBest;
        public float BestPossible;      // done so far + the golds still to come
        public float CurrentPace;       // where this run ends at the comparison's pace
        public float PossibleSave;      // the current (or next) row's possible time save
        public float Pb;
    }

    public sealed class SplitStats
    {
        public int Rows;
        public float[] BestSegments;
        public float[] PbSplits;
        public float[] AverageSplits;
        public float[] LastSplits;
        public float[] BestSegmentSplits;
        public float Pb = float.NaN;
        public float SumOfBest = float.NaN;
        /// When the PB and each gold were set: the attempt's recorded time
        /// (its start, UTC); default (MinValue) = unknown. The first attempt
        /// to reach a time keeps it - a tie is not a new gold.
        public DateTime PbSetUtc;
        public DateTime[] BestSegmentSetUtc;
        public int Completed;
        /// Completed attempts that carry a time for every row.
        public int WithSplits;

        public static SplitStats Build(IList<Attempt> attempts, int checkpoints)
        {
            int rows = Math.Max(0, checkpoints) + 1;
            SplitStats st = new SplitStats();
            st.Rows = rows;
            st.BestSegments = Filled(rows);
            st.PbSplits = Filled(rows);
            st.LastSplits = Filled(rows);
            st.BestSegmentSetUtc = new DateTime[rows];

            float[] segSum = new float[rows];
            int[] segN = new int[rows];
            Attempt pb = null, last = null;

            for (int a = 0; a < attempts.Count; a++)
            {
                Attempt at = attempts[a];
                if (at == null || !at.Completed) continue;
                st.Completed++;
                last = at;
                if (pb == null || at.Duration < pb.Duration) pb = at;

                float[] cum = SplitsOf(at, rows);
                bool full = true;
                for (int i = 0; i < rows; i++)
                {
                    float seg = SegmentAt(cum, i);
                    if (float.IsNaN(seg)) { full = false; continue; }
                    segSum[i] += seg;
                    segN[i]++;
                    if (float.IsNaN(st.BestSegments[i]) || seg < st.BestSegments[i])
                    {
                        st.BestSegments[i] = seg;
                        st.BestSegmentSetUtc[i] = at.RecordedUtc;
                    }
                }
                if (full) st.WithSplits++;
            }

            if (pb != null) { st.PbSplits = SplitsOf(pb, rows); st.Pb = pb.Duration; st.PbSetUtc = pb.RecordedUtc; }
            if (last != null) st.LastSplits = SplitsOf(last, rows);

            float[] avgSeg = Filled(rows);
            for (int i = 0; i < rows; i++)
                if (segN[i] > 0) avgSeg[i] = segSum[i] / segN[i];
            st.AverageSplits = Cumulative(avgSeg);
            st.BestSegmentSplits = Cumulative(st.BestSegments);
            st.SumOfBest = st.BestSegmentSplits[rows - 1];
            return st;
        }

        /// An attempt's split times as rows: its checkpoints when it saved
        /// one per checkpoint, its duration as the end. Anything else = NaN.
        public static float[] SplitsOf(Attempt a, int rows)
        {
            float[] cum = Filled(rows);
            if (a == null) return cum;
            if (a.Splits != null && a.Splits.Length == rows - 1)
                for (int i = 0; i < rows - 1; i++) cum[i] = a.Splits[i];
            cum[rows - 1] = a.Duration;
            return cum;
        }

        public static float SegmentAt(float[] cum, int i)
        {
            float prev = i == 0 ? 0f : cum[i - 1];
            return cum[i] - prev;   // NaN in, NaN out
        }

        /// Running sum; NaN from the first unknown on.
        public static float[] Cumulative(float[] segments)
        {
            float[] cum = new float[segments.Length];
            float t = 0f;
            for (int i = 0; i < segments.Length; i++)
            {
                t += segments[i];
                cum[i] = t;
            }
            return cum;
        }

        public static float[] Filled(int n)
        {
            float[] f = new float[n];
            for (int i = 0; i < n; i++) f[i] = float.NaN;
            return f;
        }
    }

    public static class SplitTable
    {
        private const float Epsilon = 0.0005f;

        /// Fills `rows` (length stats.Rows) for a run whose split times so
        /// far are `times` (cumulative, NaN = not reached; the first NaN is
        /// the current row while `running`), `elapsed` seconds in.
        public static SplitSummary Fill(SplitStats stats, float[] compare, float[] times,
                                        bool running, float elapsed, SplitRow[] rows)
        {
            int n = stats.Rows;
            int current = -1;
            if (running)
                for (int i = 0; i < n; i++)
                    if (float.IsNaN(times[i])) { current = i; break; }

            SplitSummary sum = new SplitSummary();
            sum.SumOfBest = stats.SumOfBest;
            sum.Pb = stats.Pb;
            sum.PreviousSegment = float.NaN;
            sum.PossibleSave = float.NaN;
            int lastReached = -1;

            for (int i = 0; i < n; i++)
            {
                SplitRow r = new SplitRow();
                r.Compare = compare[i];
                r.CompareSegment = SplitStats.SegmentAt(compare, i);
                r.Time = times[i];
                r.Segment = SplitStats.SegmentAt(times, i);
                r.BestSegment = stats.BestSegments[i];
                r.TimeSave = Math.Max(0f, r.CompareSegment - r.BestSegment);   // NaN stays NaN
                if (float.IsNaN(r.CompareSegment) || float.IsNaN(r.BestSegment)) r.TimeSave = float.NaN;
                r.Delta = float.NaN;
                r.SegmentDelta = float.NaN;
                r.Current = i == current;

                if (!float.IsNaN(r.Time))
                {
                    lastReached = i;
                    r.Delta = r.Time - r.Compare;
                    r.SegmentDelta = r.Segment - r.CompareSegment;
                    r.Colour = ColourOf(r.Delta, r.SegmentDelta, r.Segment, r.BestSegment);
                    sum.PreviousSegment = r.SegmentDelta;
                }
                else if (r.Current)
                {
                    float prevTime = i == 0 ? 0f : times[i - 1];
                    float segSoFar = elapsed - prevTime;
                    float live = elapsed - r.Compare;
                    // LiveSplit's rule: the running delta shows once it is
                    // behind, or losing time on this row even while ahead.
                    if (live > 0f || segSoFar > r.CompareSegment)
                    {
                        r.Delta = live;
                        r.SegmentDelta = segSoFar - r.CompareSegment;
                        r.Live = true;
                        r.Colour = ColourOf(r.Delta, r.SegmentDelta, float.NaN, float.NaN);
                        if (r.SegmentDelta > 0f) { sum.PreviousSegment = r.SegmentDelta; sum.PreviousLive = true; }
                    }
                    sum.PossibleSave = r.TimeSave;
                }
                rows[i] = r;
            }

            if (!running && lastReached < n - 1 && lastReached + 1 < n) sum.PossibleSave = rows[lastReached + 1].TimeSave;

            // Best possible: what is done, then the golds still to come (the
            // current row at least its time so far).
            float done = lastReached < 0 ? 0f : times[lastReached];
            float best = done;
            for (int i = lastReached + 1; i < n; i++)
            {
                float gold = stats.BestSegments[i];
                if (i == current)
                {
                    float soFar = elapsed - done;
                    gold = float.IsNaN(gold) ? float.NaN : Math.Max(gold, soFar);
                }
                best += gold;
            }
            sum.BestPossible = best;

            // Current pace: the last split plus the comparison from there on;
            // a row already running longer than that pushes it out.
            float compDone = lastReached < 0 ? 0f : compare[lastReached];
            float pace = done + (compare[n - 1] - compDone);
            if (current >= 0 && rows[current].Live && rows[current].Delta > 0f)
                pace = Math.Max(pace, compare[n - 1] + rows[current].Delta);
            sum.CurrentPace = pace;
            return sum;
        }

        public static SplitColour ColourOf(float delta, float segmentDelta, float segment, float bestSegment)
        {
            if (float.IsNaN(delta)) return SplitColour.None;
            if (!float.IsNaN(segment) && !float.IsNaN(bestSegment) && segment < bestSegment - Epsilon) return SplitColour.Gold;
            bool ahead = delta < 0f;
            bool gaining = float.IsNaN(segmentDelta) ? ahead : segmentDelta < 0f;
            if (ahead) return gaining ? SplitColour.AheadGaining : SplitColour.AheadLosing;
            return gaining ? SplitColour.BehindGaining : SplitColour.BehindLosing;
        }

        // --- text -----------------------------------------------------------

        /// "12.34", "1:02.34", "1:02:03.45" at 2 decimals; "-" when unknown.
        public static string Time(float t) { return Time(t, 2); }

        /// A time with `decimals` (0-3) places - LiveSplit's accuracy
        /// setting. Rounded to that accuracy; files keep milliseconds.
        public static string Time(float t, int decimals)
        {
            if (float.IsNaN(t) || float.IsInfinity(t)) return "-";
            decimals = Math.Max(0, Math.Min(3, decimals));
            bool neg = t < 0f;
            if (neg) t = -t;
            long scale = Pow10(decimals);
            long u = (long)Math.Round(t * (double)scale);
            long whole = u / scale;
            long frac = u % scale;
            long h = whole / 3600, m = whole / 60 % 60, s = whole % 60;
            string text;
            if (h > 0) text = h + ":" + m.ToString("00") + ":" + s.ToString("00");
            else if (m > 0) text = m + ":" + s.ToString("00");
            else text = s.ToString(CultureInfo.InvariantCulture);
            if (decimals > 0) text += "." + frac.ToString(new string('0', decimals));
            return neg ? "-" + text : text;
        }

        /// "+1.23", "-0.45", "+1:02.30" at 2 decimals; "" when unknown.
        public static string Delta(float d) { return Delta(d, 2); }

        public static string Delta(float d, int decimals)
        {
            if (float.IsNaN(d) || float.IsInfinity(d)) return "";
            string sign = d < 0f ? "-" : "+";
            return sign + Time(Math.Abs(d), decimals);
        }

        private static long Pow10(int n)
        {
            long p = 1;
            for (int i = 0; i < n; i++) p *= 10;
            return p;
        }
    }
}
