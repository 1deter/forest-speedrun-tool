using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // What a segment's attempts add up to beyond the splits table: the
    // runs that never finished, LiveSplit's PB chance and its total
    // playtime (v0.24.204; Pick up here item 2).
    //
    // A finished attempt is a .run file. An unfinished one (aborted,
    // restarted, died, left the level) used to leave only a count
    // (started.txt, v0.24.196); since v0.24.204 it also leaves one line in
    // runs/<id>/unfinished.txt: when it started, its route, how long it
    // ran and the splits it reached. Those are LiveSplit's "resets": PB
    // chance needs where they stopped, total playtime how long they ran.
    //
    // Pure: linked into the tests.
    // ------------------------------------------------------------------
    public sealed class UnfinishedAttempt
    {
        public DateTime StartedUtc;
        public string Route = "";
        public float Duration;
        /// Cumulative split times reached, in order (checkpoints only).
        public float[] Splits = new float[0];

        private const string Stamp = "yyyyMMdd'T'HHmmss'Z'";

        /// One tab-separated line: utc, route, duration, splits (comma list).
        public string Write()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(StartedUtc.ToString(Stamp, CultureInfo.InvariantCulture)).Append('\t');
            sb.Append(Route ?? "").Append('\t');
            sb.Append(Duration.ToString("F3", CultureInfo.InvariantCulture)).Append('\t');
            for (int i = 0; i < Splits.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Splits[i].ToString("F3", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// null for a line that is not one (blank, damaged, a comment).
        public static UnfinishedAttempt Parse(string line)
        {
            if (string.IsNullOrEmpty(line) || line[0] == '#') return null;
            string[] f = line.TrimEnd('\r', '\n').Split('\t');
            if (f.Length < 3) return null;
            UnfinishedAttempt u = new UnfinishedAttempt();
            if (!DateTime.TryParseExact(f[0], Stamp, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out u.StartedUtc)) return null;
            u.Route = f[1];
            if (!float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out u.Duration) || u.Duration < 0f) return null;
            if (f.Length > 3 && f[3].Length > 0)
            {
                string[] s = f[3].Split(',');
                u.Splits = new float[s.Length];
                for (int i = 0; i < s.Length; i++)
                    if (!float.TryParse(s[i], NumberStyles.Float, CultureInfo.InvariantCulture, out u.Splits[i])) return null;
            }
            return u;
        }

        public static List<UnfinishedAttempt> ParseAll(string text)
        {
            List<UnfinishedAttempt> list = new List<UnfinishedAttempt>();
            if (string.IsNullOrEmpty(text)) return list;
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                UnfinishedAttempt u = Parse(lines[i]);
                if (u != null) list.Add(u);
            }
            return list;
        }
    }

    /// One attempt as PB chance sees it: cumulative times per row (NaN =
    /// not reached / unknown) and whether it finished.
    public struct HistoryRun
    {
        public DateTime StartedUtc;
        public float[] Times;
        public bool Finished;
    }

    // ------------------------------------------------------------------
    // LiveSplit's PB Chance component (SethBling/PBChance), rule for rule:
    //
    // - Recalculated on start, split and reset, not live.
    // - No PB yet: "100%".
    // - The pool: the most recent half of the attempts (its default "50%
    //   of attempts": attempts n - n*50/100 .. n, 1-based - so 6 of 10).
    //   Each attempt adds its positive segment times to their rows; one
    //   that did not finish adds a "reset" to the row after the last one
    //   it reached (row 0 when it reached none).
    // - Finished: below the PB "100% (Congrats!)", above it "0%", equal
    //   "- (Tied)".
    // - Otherwise 10,000 simulations from the time so far: each remaining
    //   row draws one entry from its pool; a reset fails the simulation; a
    //   row with an empty pool means "-". Success = a total under the PB.
    // - None succeeded: "< 0.01%" if the time so far plus the pool's best
    //   segments still beats or ties the PB, else "0%".
    //
    // Two differences, both where our data says less than LiveSplit's:
    // - A finished attempt saved before split times were (v0.24.146) has
    //   only its total; LiveSplit would read it as every checkpoint
    //   skipped. It is left out of the pool instead (unknown, not skipped).
    // - The "< 0.01%" check sums the remaining rows' best segments; the
    //   component also adds the row just finished (an off-by-one there).
    // ------------------------------------------------------------------
    public static class PbChance
    {
        public const int Simulations = 10000;
        public const int RecentPercent = 50;

        /// `history` oldest first; `times` this run's split times so far
        /// (NaN = not reached; all NaN before the clock starts); `pb` the
        /// PB before this run (NaN = none).
        public static string Compute(IList<HistoryRun> history, float[] times, float pb, Random rand)
        {
            if (float.IsNaN(pb) || pb <= 0f) return "100%";
            int rows = times.Length;
            if (rows == 0) return "-";

            // Where this run is: the first row not reached.
            int current = rows;
            for (int i = 0; i < rows; i++)
                if (float.IsNaN(times[i])) { current = i; break; }

            if (current >= rows)
            {
                float end = Ms(times[rows - 1]), best = Ms(pb);
                if (end < best) return "100% (Congrats!)";
                if (end > best) return "0%";
                return "- (Tied)";
            }

            float now = 0f;
            for (int i = current - 1; i >= 0; i--)
                if (!float.IsNaN(times[i])) { now = Math.Max(0f, times[i]); break; }

            // The pools: segment times, NaN = a reset.
            List<float>[] pools = BuildPools(history, rows);
            for (int r = current; r < rows; r++)
                if (pools[r].Count == 0) return "-";

            int success = 0;
            for (int s = 0; s < Simulations; s++)
            {
                float t = now;
                bool failed = false;
                for (int r = current; r < rows; r++)
                {
                    List<float> pool = pools[r];
                    float seg = pool[rand.Next(pool.Count)];
                    if (float.IsNaN(seg)) { failed = true; break; }
                    t += seg;
                }
                if (!failed && t < pb) success++;
            }

            if (success == 0)
            {
                float t = now;
                for (int r = current; r < rows; r++)
                {
                    float gold = float.NaN;
                    List<float> pool = pools[r];
                    for (int k = 0; k < pool.Count; k++)
                        if (!float.IsNaN(pool[k]) && (float.IsNaN(gold) || pool[k] < gold)) gold = pool[k];
                    if (!float.IsNaN(gold)) t += gold;
                }
                return t <= pb ? "< 0.01%" : "0%";
            }

            // success / 10000 as a percentage: at most two decimals.
            return (success / 100.0).ToString("0.##", CultureInfo.InvariantCulture) + "%";
        }

        /// Each row's pool from the recent attempts; NaN entries are resets.
        public static List<float>[] BuildPools(IList<HistoryRun> history, int rows)
        {
            List<float>[] pools = new List<float>[rows];
            for (int r = 0; r < rows; r++) pools[r] = new List<float>();
            int n = history.Count;
            int first = Math.Max(1, n - n * RecentPercent / 100);   // 1-based, as the component counts

            for (int a = first; a <= n; a++)
            {
                HistoryRun h = history[a - 1];
                if (h.Times == null || h.Times.Length != rows) continue;
                // A finished attempt that knows only its total (rows > 1):
                // its checkpoints are unknown, not skipped.
                if (h.Finished && rows > 1 && float.IsNaN(h.Times[0])) continue;

                int last = -1;
                float prev = 0f;
                for (int r = 0; r < rows; r++)
                {
                    float t = h.Times[r];
                    if (float.IsNaN(t)) continue;
                    // After a skipped row the time since the last one known
                    // goes here, as LiveSplit's history keeps it.
                    float seg = t - prev;
                    prev = t;
                    if (seg > 0f) { pools[r].Add(seg); last = r; }
                }
                if (!h.Finished && last < rows - 1) pools[last + 1].Add(float.NaN);
            }
            return pools;
        }

        private static float Ms(float t) { return (float)Math.Round(t * 1000.0) / 1000f; }
    }

    // ------------------------------------------------------------------
    // When a time was set, for the Runs tab (runner request: "when each
    // time was set"). An attempt's .run file has carried `recorded|<utc>`
    // (its start) from the first version, so nothing new is stored; this
    // only words it, in the runner's local time: "today 14:32",
    // "yesterday 09:05", "3 Oct 14:32", "3 Oct 2025 14:32". Both times are
    // LOCAL - the caller converts, so the words do not depend on the
    // machine's zone here.
    // ------------------------------------------------------------------
    public static class RunDates
    {
        private static readonly string[] Months =
        {
            "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
        };

        /// "" when the time is unknown (an attempt with no `recorded` line).
        public static string When(DateTime local, DateTime nowLocal)
        {
            if (local.Year < 2000) return "";
            string clock = local.Hour.ToString("00", CultureInfo.InvariantCulture) + ":" +
                           local.Minute.ToString("00", CultureInfo.InvariantCulture);
            DateTime day = local.Date, today = nowLocal.Date;
            if (day == today) return "today " + clock;
            if (day == today.AddDays(-1)) return "yesterday " + clock;
            string date = local.Day.ToString(CultureInfo.InvariantCulture) + " " + Months[local.Month - 1];
            if (local.Year != nowLocal.Year) date += " " + local.Year.ToString(CultureInfo.InvariantCulture);
            return date + " " + clock;
        }

        /// A UTC stamp (as attempts carry it) in local words; "" when unknown.
        public static string WhenUtc(DateTime utc, DateTime nowLocal)
        {
            if (utc.Year < 2000) return "";
            DateTime u = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return When(u.ToLocalTime(), nowLocal);
        }
    }

    public static class Playtime
    {
        /// LiveSplit's Total Playtime: every attempt's time, finished or
        /// not, plus the running one's.
        public static float Total(IList<Attempt> finished, IList<UnfinishedAttempt> unfinished, float running)
        {
            double t = 0;
            if (finished != null)
                for (int i = 0; i < finished.Count; i++)
                    if (finished[i] != null && finished[i].Completed && finished[i].Duration > 0f) t += finished[i].Duration;
            if (unfinished != null)
                for (int i = 0; i < unfinished.Count; i++)
                    if (unfinished[i] != null) t += unfinished[i].Duration;
            if (running > 0f) t += running;
            return (float)t;
        }

        /// LiveSplit's DaysTimeFormatter: "m:ss", "h:mm:ss", "2d 3:04:05".
        public static string Format(float seconds)
        {
            if (float.IsNaN(seconds) || seconds < 0f) seconds = 0f;
            long s = (long)Math.Floor(seconds);
            long days = s / 86400, h = s / 3600 % 24, m = s / 60 % 60, sec = s % 60;
            string text = s >= 3600
                ? h + ":" + m.ToString("00") + ":" + sec.ToString("00")
                : m + ":" + sec.ToString("00");
            return days > 0 ? days + "d " + text : text;
        }
    }
}
