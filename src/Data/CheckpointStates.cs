using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Checkpoint states ("saveloc", like KSF surf; author, QA Discord
    // 2026-09-26): with "Capture at checkpoints" on, each checkpoint a
    // practice run fires is captured as a Quick-load savestate, and
    // "Restart from checkpoint N" restores it and resumes the timed run
    // from there - for practising the late parts of a long segment
    // without running the whole thing.
    //
    // Files, beside the segment's start state in savestates/segments/:
    //   <safe id>.cp<N>.fosave   the savestate (N = 1 for the first checkpoint)
    //   <safe id>.cp<N>.meta     this class: the run's clock and splits then
    // The meta is written after the .fosave, so a pair with no meta is a
    // write that did not finish and is not offered.
    //
    // NEWEST WINS: each capture of checkpoint N replaces the last one,
    // whichever run it came from. A PB's states would mean holding every
    // run's captures until it is known to be a PB (a hitch's worth of
    // files per checkpoint per run), and the state you most want is the
    // one you just played into - the route as you run it now.
    //
    // A resumed run is practice: it never saves a full attempt (no PB, no
    // average, no upload, no PB chance). The segments it RUNS - every
    // split after the resume point - are real times from a real state on
    // this route, so they can be golds (best segments / sum of best):
    // practising a hard section is how a sum of best improves. Those are
    // kept in runs/<id>/checkpoint-segments.txt (PracticeSegment) and
    // merged into the splits table's golds (PracticeGolds).
    //
    // Pure, so it is unit tested (CheckpointStatesTests).
    // ------------------------------------------------------------------
    public sealed class CheckpointState
    {
        public const string Extension = ".meta";
        private const string Stamp = "yyyyMMdd'T'HHmmss'Z'";

        /// 0-based checkpoint index (the file says N = Index + 1).
        public int Index;
        /// Route fingerprint of the run that captured it.
        public string Route = "";
        public DateTime CapturedUtc;
        /// The run's clock when the level was serialized - a few frames
        /// after the split, so a resumed run counts those frames too.
        public float ResumeAt;
        /// The run's split times up to and including this checkpoint
        /// (Index + 1 entries).
        public float[] Splits = new float[0];
        /// Item counts at the run's start, for relative item triggers
        /// (`+3`): item id -> count.
        public readonly List<KeyValuePair<int, int>> Baseline = new List<KeyValuePair<int, int>>();

        public string Write()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("# ForestOverlay checkpoint state\n");
            sb.Append("checkpoint = ").Append((Index + 1).ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("route = ").Append(Route ?? "").Append('\n');
            sb.Append("captured = ").Append(CapturedUtc.ToString(Stamp, CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("resume = ").Append(ResumeAt.ToString("F3", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("splits = ");
            for (int i = 0; i < Splits.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Splits[i].ToString("F3", CultureInfo.InvariantCulture));
            }
            sb.Append('\n');
            sb.Append("baseline = ");
            for (int i = 0; i < Baseline.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Baseline[i].Key.ToString(CultureInfo.InvariantCulture)).Append(':')
                  .Append(Baseline[i].Value.ToString(CultureInfo.InvariantCulture));
            }
            sb.Append('\n');
            return sb.ToString();
        }

        /// null and why for text that is not one.
        public static CheckpointState Parse(string text, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(text)) { error = "empty"; return null; }

            CheckpointState s = new CheckpointState();
            bool hasIndex = false, hasResume = false, hasSplits = false, hasCaptured = false;
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "checkpoint":
                        int n;
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < 1)
                        { error = "bad checkpoint number '" + value + "'"; return null; }
                        s.Index = n - 1;
                        hasIndex = true;
                        break;
                    case "route":
                        s.Route = value;
                        break;
                    case "captured":
                        if (!DateTime.TryParseExact(value, Stamp, CultureInfo.InvariantCulture,
                                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out s.CapturedUtc))
                        { error = "bad capture time '" + value + "'"; return null; }
                        hasCaptured = true;
                        break;
                    case "resume":
                        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out s.ResumeAt))
                        { error = "bad resume time '" + value + "'"; return null; }
                        hasResume = true;
                        break;
                    case "splits":
                        if (value.Length > 0)
                        {
                            string[] parts = value.Split(',');
                            s.Splits = new float[parts.Length];
                            for (int k = 0; k < parts.Length; k++)
                                if (!float.TryParse(parts[k], NumberStyles.Float, CultureInfo.InvariantCulture, out s.Splits[k]))
                                { error = "bad split time '" + parts[k] + "'"; return null; }
                        }
                        hasSplits = true;
                        break;
                    case "baseline":
                        if (value.Length == 0) break;
                        string[] pairs = value.Split(',');
                        for (int k = 0; k < pairs.Length; k++)
                        {
                            int colon = pairs[k].IndexOf(':');
                            int id, count;
                            if (colon <= 0 ||
                                !int.TryParse(pairs[k].Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out id) ||
                                !int.TryParse(pairs[k].Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out count))
                            { error = "bad baseline item '" + pairs[k] + "'"; return null; }
                            s.Baseline.Add(new KeyValuePair<int, int>(id, count));
                        }
                        break;
                }
            }

            if (!hasIndex) { error = "no checkpoint number"; return null; }
            if (!hasResume) { error = "no resume time"; return null; }
            if (!hasSplits) { error = "no split times"; return null; }
            if (!hasCaptured) { error = "no capture time"; return null; }
            return s;
        }

        /// null when a run of `route` with `checkpoints` checkpoints can
        /// resume from it; else why not, in the runner's words.
        public string Check(string route, int checkpoints)
        {
            if (Index >= checkpoints)
                return "the segment has " + checkpoints + " checkpoint(s) now";
            if ((Route ?? "") != (route ?? ""))
                return "captured on an earlier version of this route (a zone or the start state changed)";
            if (Splits == null || Splits.Length != Index + 1)
                return "its split times are incomplete";
            for (int i = 0; i < Splits.Length; i++)
                if (float.IsNaN(Splits[i]) || Splits[i] < 0f || (i > 0 && Splits[i] < Splits[i - 1]))
                    return "its split times are out of order";
            if (float.IsNaN(ResumeAt) || ResumeAt < Splits[Index])
                return "its resume time is before its split";
            return null;
        }
    }

    public static class CheckpointFiles
    {
        /// "<safe id>.cp<N>" - add SavestateFile.Extension or
        /// CheckpointState.Extension.
        public static string BaseName(string safeId, int index)
        {
            return safeId + ".cp" + (index + 1).ToString(CultureInfo.InvariantCulture);
        }

        /// The 0-based checkpoint index a file name belongs to for this
        /// segment, or -1. `extension` with its dot (".fosave", ".meta").
        public static int IndexOf(string fileName, string safeId, string extension)
        {
            if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(safeId)) return -1;
            string prefix = safeId + ".cp";
            if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return -1;
            if (!fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return -1;
            string number = fileName.Substring(prefix.Length, fileName.Length - prefix.Length - extension.Length);
            if (number.Length == 0 || number.Length > 4) return -1;
            for (int i = 0; i < number.Length; i++)
                if (number[i] < '0' || number[i] > '9') return -1;
            int n = int.Parse(number, CultureInfo.InvariantCulture);
            if (n < 1 || number[0] == '0') return -1;
            return n - 1;
        }

        /// The checkpoint indices with both a savestate and a meta file
        /// among `fileNames` (names only, no folders), ascending.
        public static List<int> Complete(IList<string> fileNames, string safeId, string stateExtension)
        {
            List<int> states = new List<int>();
            List<int> metas = new List<int>();
            for (int i = 0; i < fileNames.Count; i++)
            {
                int s = IndexOf(fileNames[i], safeId, stateExtension);
                if (s >= 0 && !states.Contains(s)) states.Add(s);
                int m = IndexOf(fileNames[i], safeId, CheckpointState.Extension);
                if (m >= 0 && !metas.Contains(m)) metas.Add(m);
            }
            List<int> both = new List<int>();
            for (int i = 0; i < states.Count; i++)
                if (metas.Contains(states[i])) both.Add(states[i]);
            both.Sort();
            return both;
        }

        /// Which checkpoint the hotkey restarts from: the one restarted
        /// from last if it still has a state, else the latest one that has.
        /// -1 with none.
        public static int PickForHotkey(IList<int> usable, int last)
        {
            if (usable == null || usable.Count == 0) return -1;
            if (last >= 0 && usable.Contains(last)) return last;
            int best = -1;
            for (int i = 0; i < usable.Count; i++) if (usable[i] > best) best = usable[i];
            return best;
        }
    }

    /// One segment run live after a checkpoint resume (a gold candidate).
    public struct PracticeSegment
    {
        public string Route;
        public DateTime Utc;
        /// The splits table row: checkpoint index, or the checkpoint count
        /// for the end.
        public int Row;
        public float Seconds;

        private const string Stamp = "yyyyMMdd'T'HHmmss'Z'";

        /// One tab-separated line: utc, route, row, seconds.
        public string Write()
        {
            return Utc.ToString(Stamp, CultureInfo.InvariantCulture) + "\t" + (Route ?? "") + "\t" +
                   Row.ToString(CultureInfo.InvariantCulture) + "\t" +
                   Seconds.ToString("F3", CultureInfo.InvariantCulture);
        }

        public static bool TryParse(string line, out PracticeSegment p)
        {
            p = new PracticeSegment();
            if (string.IsNullOrEmpty(line) || line[0] == '#') return false;
            string[] f = line.TrimEnd('\r', '\n').Split('\t');
            if (f.Length < 4) return false;
            if (!DateTime.TryParseExact(f[0], Stamp, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out p.Utc)) return false;
            p.Route = f[1];
            if (!int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out p.Row) || p.Row < 0) return false;
            if (!float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out p.Seconds)) return false;
            if (float.IsNaN(p.Seconds) || p.Seconds <= 0f) return false;
            return true;
        }

        public static List<PracticeSegment> ParseAll(string text)
        {
            List<PracticeSegment> list = new List<PracticeSegment>();
            if (string.IsNullOrEmpty(text)) return list;
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                PracticeSegment p;
                if (TryParse(lines[i], out p)) list.Add(p);
            }
            return list;
        }
    }

    public static class PracticeGolds
    {
        /// The segment time of `row` in a run resumed after checkpoint
        /// `resumedFrom` (0-based), or NaN when that segment was not run
        /// live: its start is the resume point's split or a later one.
        /// `times` are the run's cumulative split times (NaN = not reached).
        public static float LiveSegment(float[] times, int resumedFrom, int row)
        {
            if (times == null || row <= resumedFrom || row <= 0 || row >= times.Length) return float.NaN;
            float seg = times[row] - times[row - 1];
            return seg > 0f ? seg : float.NaN;   // NaN in, NaN out
        }

        /// Lowers `st`'s best segments with the practice segments of
        /// `route`, and rebuilds the sum of best from them. The PB, last and
        /// average are full runs' and untouched. Returns how many golds came
        /// from practice.
        public static int Apply(SplitStats st, IList<PracticeSegment> segments, string route)
        {
            if (st == null || segments == null) return 0;
            int changed = 0;
            for (int i = 0; i < segments.Count; i++)
            {
                PracticeSegment p = segments[i];
                if ((p.Route ?? "") != (route ?? "")) continue;
                if (p.Row < 0 || p.Row >= st.Rows) continue;
                float best = st.BestSegments[p.Row];
                if (!float.IsNaN(best) && p.Seconds >= best) continue;   // a tie is not a new gold
                st.BestSegments[p.Row] = p.Seconds;
                st.BestSegmentSetUtc[p.Row] = p.Utc;
                changed++;
            }
            if (changed > 0)
            {
                st.BestSegmentSplits = SplitStats.Cumulative(st.BestSegments);
                st.SumOfBest = st.BestSegmentSplits[st.Rows - 1];
            }
            return changed;
        }
    }
}
