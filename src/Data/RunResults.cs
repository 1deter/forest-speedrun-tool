using System;
using System.Collections.Generic;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The results screen after a run (maks's idea, docs/backlog.md *Run
    // mode and anti-splicing*: "a results screen after the run").
    //
    // Built once, when a timed run finishes, from the stats as they were
    // BEFORE the run (a gold is judged against the golds before it, as the
    // splits table does) and the run's own split times: the final time
    // against the previous PB, against the chosen comparison, each split
    // with its delta and what it saved or lost, the golds, and the best
    // possible time with this run's golds in it. Run mode adds its code,
    // report and receipt lines (RunModeOutcome).
    //
    // Pure: linked into the tests. Times go through SplitTable's text.
    // ------------------------------------------------------------------
    public struct ResultRow
    {
        public string Name;
        public string Time;          // split time
        public string Delta;         // vs the comparison
        public string Segment;       // this row alone
        public string SavedLost;     // "saved 0.40" / "lost 1.20" / "even" / ""
        public SplitColour Colour;   // the delta's colour (Gold for a gold)
        public bool Gold;
        public bool Saved;           // SavedLost is a saving
        public bool Lost;
    }

    public sealed class RunResult
    {
        public float Final = float.NaN;
        public float PreviousPb = float.NaN;
        public float CompareFinal = float.NaN;
        public float SumOfBestBefore = float.NaN;
        public float SumOfBestAfter = float.NaN;
        public bool NewPb;
        public bool FirstFinish;
        public bool Tied;
        public int Golds;

        public string Headline = "";      // "1:23.45"
        public string Verdict = "";       // "New personal best by 1.23 s! (was 1:24.68)"
        public string CompareLine = "";   // "vs Best segments: +2.34" ("" when it is the PB)
        public string GoldLine = "";      // "Golds: Cave, End" ("" with one row)
        public string BestPossible = "";  // "1:20.00" (sum of best, this run's golds in)
        public ResultRow[] Rows = new ResultRow[0];
    }

    /// Run mode's side of a finished attempt (Modules/RunModeModule fills it).
    public sealed class RunModeOutcome
    {
        public int Attempt;
        public string Label = "";        // the category (or the game)
        public string AttemptId = "";
        public string Code = "";         // the last code of the chain
        public bool Online;              // the site's start code arrived
        public bool SendOn;              // [Site] SendAttempts
        public bool AntiSplice = true;   // the category shows codes on screen
        public string Report = "";       // RunReport.Summary()
    }

    /// What became of an attempt's log on its way to the site.
    public enum AttemptUpload { Unknown, Waiting, Retrying, Sent, Refused, TokenBad }

    public static class RunResults
    {
        private const float Epsilon = 0.0005f;

        /// `before`: the stats from before this run; `compare`: the chosen
        /// comparison's split times (null / wrong length = none);
        /// `compareIsPb`: the comparison is the PB (its line would repeat
        /// the verdict); `times`: this run's split times, the end last.
        public static RunResult Build(SplitStats before, float[] compare, bool compareIsPb, string compareName,
                                      float[] times, IList<string> names, int timeDecimals, int deltaDecimals)
        {
            RunResult res = new RunResult();
            int n = before != null ? before.Rows : 0;
            if (n == 0 || times == null || times.Length != n) return res;
            if (compare == null || compare.Length != n) compare = SplitStats.Filled(n);

            float final = times[n - 1];
            res.Final = final;
            res.PreviousPb = before.Pb;
            res.CompareFinal = compare[n - 1];
            res.SumOfBestBefore = before.SumOfBest;
            res.SumOfBestAfter = SumOfBestAfter(before.BestSegments, times);
            res.Headline = SplitTable.Time(final, Math.Max(2, timeDecimals));

            res.FirstFinish = float.IsNaN(before.Pb);
            int cmp = res.FirstFinish ? -1 : Math.Sign(Ms(final) - Ms(before.Pb));
            res.NewPb = res.FirstFinish || cmp < 0;
            res.Tied = !res.FirstFinish && cmp == 0;
            res.Verdict = PbVerdict(final, before.Pb, deltaDecimals);
            res.CompareLine = compareIsPb ? "" : CompareText(compareName, final, compare[n - 1], deltaDecimals);
            res.BestPossible = SplitTable.Time(res.SumOfBestAfter, timeDecimals);

            SplitRow[] rows = new SplitRow[n];
            SplitTable.Fill(before, compare, times, false, final, rows);
            res.Rows = new ResultRow[n];
            List<string> golds = new List<string>();
            for (int i = 0; i < n; i++)
            {
                SplitRow d = rows[i];
                ResultRow r = new ResultRow();
                r.Name = names != null && i < names.Count && names[i] != null ? names[i] : (i == n - 1 ? "End" : "Checkpoint " + (i + 1));
                r.Time = SplitTable.Time(d.Time, timeDecimals);
                r.Delta = SplitTable.Delta(d.Delta, deltaDecimals);
                r.Segment = SplitTable.Time(d.Segment, timeDecimals);
                r.SavedLost = SavedLost(d.SegmentDelta, deltaDecimals);
                r.Saved = !float.IsNaN(d.SegmentDelta) && d.SegmentDelta < -Epsilon;
                r.Lost = !float.IsNaN(d.SegmentDelta) && d.SegmentDelta > Epsilon;
                r.Colour = d.Colour;
                r.Gold = d.Colour == SplitColour.Gold;
                if (r.Gold) golds.Add(r.Name);
                res.Rows[i] = r;
            }
            res.Golds = golds.Count;
            // One row: its gold is the PB, which the verdict already says.
            if (n > 1) res.GoldLine = golds.Count == 0 ? "No golds this run." : (golds.Count == 1 ? "Gold: " : golds.Count + " golds: ") + string.Join(", ", golds.ToArray()) + ".";
            return res;
        }

        /// "New personal best by 1.23 s! (was 1:24.68)", "First finished run -
        /// your personal best.", "Tied your personal best (1:24.68).",
        /// "1.23 s behind your personal best (1:24.68)." - judged to the
        /// millisecond, as the attempts are saved.
        public static string PbVerdict(float final, float previousPb, int decimals)
        {
            if (float.IsNaN(final)) return "";
            if (float.IsNaN(previousPb)) return "First finished run - your personal best.";
            float d = Ms(final) - Ms(previousPb);
            string was = SplitTable.Time(previousPb, Math.Max(decimals, 2));
            if (d < 0f) return "New personal best by " + By(-d, decimals) + "! (was " + was + ")";
            if (d == 0f) return "Tied your personal best (" + was + ").";
            return By(d, decimals) + " behind your personal best (" + was + ").";
        }

        /// "vs Best segments: +2.34", "vs Last run: -0.50"; says so when the
        /// comparison has no time.
        public static string CompareText(string name, float final, float compareFinal, int decimals)
        {
            string who = string.IsNullOrEmpty(name) ? "the comparison" : name;
            if (float.IsNaN(final)) return "";
            if (float.IsNaN(compareFinal)) return "vs " + who + ": no time to compare yet.";
            return "vs " + who + ": " + SplitTable.Delta(final - compareFinal, decimals);
        }

        /// A row's time against the comparison's: "saved 0.40", "lost 1.20",
        /// "even"; "" when either is unknown.
        public static string SavedLost(float segmentDelta, int decimals)
        {
            if (float.IsNaN(segmentDelta) || float.IsInfinity(segmentDelta)) return "";
            if (Math.Abs(segmentDelta) < Epsilon) return "even";
            return (segmentDelta < 0f ? "saved " : "lost ") + SplitTable.Time(Math.Abs(segmentDelta), decimals);
        }

        /// The sum of best segments with this run's in: each row's gold or
        /// this run's time there, whichever is lower. NaN when a row is
        /// unknown in both.
        public static float SumOfBestAfter(float[] bestBefore, float[] times)
        {
            if (bestBefore == null || times == null || bestBefore.Length != times.Length) return float.NaN;
            float sum = 0f;
            for (int i = 0; i < times.Length; i++)
            {
                float seg = SplitStats.SegmentAt(times, i);
                float gold = bestBefore[i];
                float best = float.IsNaN(gold) ? seg : float.IsNaN(seg) ? gold : Math.Min(gold, seg);
                if (float.IsNaN(best)) return float.NaN;
                sum += best;
            }
            return sum;
        }

        // "1.23 s" under a minute, "1:02.30" above.
        private static string By(float seconds, int decimals)
        {
            string t = SplitTable.Time(seconds, decimals);
            return seconds < 59.5f ? t + " s" : t;
        }

        private static float Ms(float t) { return (float)Math.Round(t * 1000.0) / 1000f; }

        // --- run mode -----------------------------------------------------------

        /// The site's verdict in plain words.
        public static string VerdictWords(string verdict)
        {
            switch (verdict)
            {
                case "green": return "green: checked online";
                case "amber": return "amber: parts checked by the video's codes only";
                case "red": return "red: see its page";
                case "refused": return "refused by the site (uploads/attempts/refused)";
                default: return verdict ?? "";
            }
        }

        /// Where the attempt's log is: `detail` is the verdict (Sent) or
        /// the reason (Retrying, Refused).
        public static string UploadWords(AttemptUpload state, string detail)
        {
            switch (state)
            {
                case AttemptUpload.Waiting: return "waiting to be sent";
                case AttemptUpload.Retrying: return "waiting - " + (string.IsNullOrEmpty(detail) ? "the site is not reachable" : detail);
                case AttemptUpload.Sent: return "sent - " + VerdictWords(detail);
                case AttemptUpload.Refused: return "refused by the site" + (string.IsNullOrEmpty(detail) ? "" : ": " + detail);
                case AttemptUpload.TokenBad: return "waiting - the site does not know this install's token (clear Token in the config)";
                default: return "";
            }
        }

        /// The results' run mode lines: the attempt and its last code, the
        /// report, the receipt. `upload` is UploadWords' text ("" = not
        /// heard of yet); the link is the caller's (it knows the site).
        public static List<string> RunModeLines(RunModeOutcome o, string upload, string link)
        {
            List<string> lines = new List<string>();
            if (o == null) return lines;
            lines.Add("Run mode attempt " + o.Attempt + (o.Label.Length > 0 ? " (" + o.Label + ")" : "") +
                      (o.Code.Length > 0 && o.Code != AttemptChain.NoCode ? " - last code " + o.Code : "") +
                      (o.Online ? ", online (the site has its start code)." : ", offline - checked by the video's codes only."));
            if (o.Report.Length > 0) lines.Add("Report: " + o.Report + ".");
            if (!o.SendOn)
                lines.Add("Receipt: not sent - sending attempts is off (the log is in run-reports).");
            else
            {
                lines.Add("Receipt: " + (string.IsNullOrEmpty(upload) ? UploadWords(AttemptUpload.Waiting, null) : upload) + ".");
                if (!string.IsNullOrEmpty(link)) lines.Add("Attempt page: " + link);
            }
            return lines;
        }
    }
}
