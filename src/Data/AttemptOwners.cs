using System;
using System.Collections.Generic;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Whose attempt is it? Imported `.foseg` files (Practice -> Import)
    // bring other runners' attempts into the same runs/<segment>/ folder,
    // and until v0.24.190 they counted as the runner's own - PB, golds and
    // sum of best included. The rule (author, 2026-09-27): other runners'
    // attempts are comparisons only.
    //
    // An attempt is the runner's own when it carries their id, or no id at
    // all (recorded before v0.24.146 - nothing to tell it by). Every other
    // runner's best finished attempt on the route becomes a comparison
    // beside the website's (PracticeRunModule.Site), local and offline.
    //
    // Pure: linked into the tests.
    // ------------------------------------------------------------------
    public static class AttemptOwners
    {
        public static bool IsOwn(string runnerId, string ownId)
        {
            return string.IsNullOrEmpty(runnerId) || runnerId == ownId;
        }

        /// The runner id in a .run text's header, "" when it has none.
        public static string RunnerIdOf(string runText)
        {
            if (string.IsNullOrEmpty(runText)) return "";
            int at = 0;
            while (at < runText.Length)
            {
                int end = runText.IndexOf('\n', at);
                if (end < 0) end = runText.Length;
                string line = runText.Substring(at, end - at).TrimEnd('\r');
                if (line.StartsWith("runner|"))
                {
                    string[] p = line.Split('|');
                    return p.Length > 1 ? p[1] : "";
                }
                if (line.StartsWith("s|") || line.StartsWith("v|")) break;   // header over
                at = end + 1;
            }
            return "";
        }

        /// Each other runner's best finished attempt, fastest first, as
        /// board entries with negative RunIds (-1, -2 ...) - never a website
        /// run id. Splits: the attempt's checkpoint times then its total,
        /// `checkpoints + 1` values (NaN where the attempt has no split
        /// times). `runs` gets the attempts, index-aligned.
        public static List<BoardEntry> OthersBest(IList<Attempt> others, int checkpoints, List<Attempt> runs)
        {
            Dictionary<string, Attempt> best = new Dictionary<string, Attempt>();
            List<string> order = new List<string>();
            for (int i = 0; i < others.Count; i++)
            {
                Attempt a = others[i];
                if (a == null || !a.Completed || string.IsNullOrEmpty(a.RunnerId)) continue;
                Attempt had;
                if (!best.TryGetValue(a.RunnerId, out had)) { best[a.RunnerId] = a; order.Add(a.RunnerId); }
                else if (a.Duration < had.Duration) best[a.RunnerId] = a;
            }

            List<Attempt> sorted = new List<Attempt>();
            for (int i = 0; i < order.Count; i++) sorted.Add(best[order[i]]);
            sorted.Sort(delegate(Attempt x, Attempt y) { return x.Duration.CompareTo(y.Duration); });

            List<BoardEntry> list = new List<BoardEntry>();
            if (runs != null) runs.Clear();
            for (int i = 0; i < sorted.Count; i++)
            {
                Attempt a = sorted[i];
                BoardEntry e = new BoardEntry();
                e.RunId = -(i + 1);
                e.RunnerId = a.RunnerId;
                e.Name = string.IsNullOrEmpty(a.RunnerName) ? "a runner" : a.RunnerName;
                e.Duration = a.Duration;
                e.Splits = new float[checkpoints + 1];
                bool hasSplits = a.Splits != null && a.Splits.Length == checkpoints;
                for (int s = 0; s < checkpoints; s++) e.Splits[s] = hasSplits ? a.Splits[s] : float.NaN;
                e.Splits[checkpoints] = a.Duration;
                list.Add(e);
                if (runs != null) runs.Add(a);
            }
            return list;
        }

        /// The website's list with the local ones merged in: a runner on
        /// both keeps the faster entry. Local entries are marked by their
        /// negative RunId.
        public static List<BoardEntry> Merge(IList<BoardEntry> site, IList<BoardEntry> local)
        {
            List<BoardEntry> list = new List<BoardEntry>();
            if (site != null) list.AddRange(site);
            if (local == null) return list;
            for (int i = 0; i < local.Count; i++)
            {
                BoardEntry l = local[i];
                int at = -1;
                for (int j = 0; j < list.Count; j++) if (list[j].RunnerId == l.RunnerId) { at = j; break; }
                if (at < 0) list.Add(l);
                else if (l.Duration < list[at].Duration) list[at] = l;
            }
            list.Sort(delegate(BoardEntry x, BoardEntry y) { return x.Duration.CompareTo(y.Duration); });
            return list;
        }
    }
}
