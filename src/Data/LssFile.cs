using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // A LiveSplit splits file (.lss) read into plain data, so a runner's
    // LiveSplit PB and golds can be offered as comparisons (Next up 9).
    // Comparisons only - never the runner's own PB / golds here (author,
    // 2026-09-27: other sources compare, they do not count).
    //
    //   <Run version="1.7.0">
    //     <GameName/> <CategoryName/> <Offset/> <AttemptCount/>
    //     <AttemptHistory> <Attempt id=".." started=".." ended="..">
    //                        <RealTime/> <GameTime/> </Attempt> ...
    //     <Segments> <Segment>
    //       <Name/>
    //       <SplitTimes> <SplitTime name="Personal Best"> <RealTime/> <GameTime/> ...
    //       <BestSegmentTime> <RealTime/> <GameTime/>
    //       <SegmentHistory> <Time id=".."> <RealTime/> <GameTime/> ...
    //
    // Times are TimeSpan text, "[-][d.]hh:mm:ss[.fffffff]". Split times are
    // cumulative from the timer's zero; history and golds are per segment.
    // An empty element is a skipped split (the next segment's history time
    // then covers both). Files older than LiveSplit 1.6 put the time as the
    // element's own text - read as real time.
    //
    // The timing method is a LiveSplit setting, not in the file:
    // PreferredTiming guesses from what the PB holds; the UI lets the runner
    // switch. A layout (.lsl) holds display settings only (columns,
    // accuracy) - nothing a comparison needs, so it is refused by name.
    //
    // System.Xml's XmlDocument only (no System.Xml.Linq, no LINQ). Pure:
    // linked into the tests.
    // ------------------------------------------------------------------
    public enum LssTiming { RealTime, GameTime }

    /// One time in both methods; NaN = not there.
    public struct LssTime
    {
        public double Real;
        public double Game;

        public static readonly LssTime None = new LssTime { Real = double.NaN, Game = double.NaN };

        public double Get(LssTiming t) { return t == LssTiming.GameTime ? Game : Real; }
        public bool IsEmpty { get { return double.IsNaN(Real) && double.IsNaN(Game); } }
    }

    public sealed class LssAttempt
    {
        public int Id;
        public string Started = "";   // LiveSplit's own text, kept as is
        public string Ended = "";
        public LssTime Time = LssTime.None;   // the final time; empty = reset
        public bool Finished { get { return !Time.IsEmpty; } }
    }

    public sealed class LssSegment
    {
        public string Name = "";
        /// Cumulative split time per comparison name ("Personal Best", a
        /// runner's own like "WR"). Missing or empty = LssTime.None.
        public readonly Dictionary<string, LssTime> SplitTimes = new Dictionary<string, LssTime>();
        public LssTime BestSegment = LssTime.None;
        /// Segment time per attempt id; an entry with no time = the split
        /// was skipped in that attempt. An attempt reset before this
        /// segment has no entry.
        public readonly Dictionary<int, LssTime> History = new Dictionary<int, LssTime>();

        public LssTime SplitTime(string comparison)
        {
            LssTime t;
            return SplitTimes.TryGetValue(comparison, out t) ? t : LssTime.None;
        }
    }

    public sealed class LssRun
    {
        public const string PersonalBest = "Personal Best";

        public string Version = "";
        public string GameName = "";
        public string CategoryName = "";
        public double Offset;
        public int AttemptCount;
        public readonly List<LssAttempt> Attempts = new List<LssAttempt>();
        public readonly List<LssSegment> Segments = new List<LssSegment>();
        /// Comparison names in the file, in first-seen order.
        public readonly List<string> Comparisons = new List<string>();

        public string[] SegmentNames()
        {
            string[] n = new string[Segments.Count];
            for (int i = 0; i < n.Length; i++) n[i] = Segments[i].Name;
            return n;
        }

        /// A comparison's cumulative split times, one per LiveSplit segment.
        public double[] SplitTimes(string comparison, LssTiming timing)
        {
            double[] t = new double[Segments.Count];
            for (int i = 0; i < t.Length; i++) t[i] = Segments[i].SplitTime(comparison).Get(timing);
            return t;
        }

        /// The golds, one per LiveSplit segment.
        public double[] BestSegments(LssTiming timing)
        {
            double[] t = new double[Segments.Count];
            for (int i = 0; i < t.Length; i++) t[i] = Segments[i].BestSegment.Get(timing);
            return t;
        }

        /// How many PB splits carry a time in this method.
        public int CountTimes(LssTiming timing)
        {
            int n = 0;
            for (int i = 0; i < Segments.Count; i++)
                if (!double.IsNaN(Segments[i].SplitTime(PersonalBest).Get(timing))) n++;
            return n;
        }

        /// Real time unless the PB has game times only - the Forest
        /// autosplitter removes no loads, so runners time real time.
        public LssTiming PreferredTiming()
        {
            return CountTimes(LssTiming.RealTime) == 0 && CountTimes(LssTiming.GameTime) > 0
                ? LssTiming.GameTime : LssTiming.RealTime;
        }

        /// The best time for LiveSplit segments (after, last] run back to
        /// back (after = -1: from the start). One segment: its gold. More:
        /// the best whole range any attempt in the history ran - the golds
        /// summed can come from different attempts and are only a floor -
        /// or that floor when the history has none. NaN when unknown.
        public double BestRange(int after, int last, LssTiming timing)
        {
            if (last < 0 || last >= Segments.Count || after >= last || after < -1) return double.NaN;
            if (last - after == 1)
            {
                double g = Segments[last].BestSegment.Get(timing);
                return double.IsNaN(g) ? HistoryBest(after, last, timing) : g;
            }
            double best = HistoryBest(after, last, timing);
            if (!double.IsNaN(best)) return best;
            double sum = 0;
            for (int i = after + 1; i <= last; i++) sum += Segments[i].BestSegment.Get(timing);
            return sum;   // NaN if any gold is missing
        }

        // An attempt counts for a range when it split at both ends: the
        // segment before the range (or the start) and the range's last.
        // Skipped splits inside roll into the next time, so the sum of the
        // times present is the range's time.
        private double HistoryBest(int after, int last, LssTiming timing)
        {
            double best = double.NaN;
            foreach (KeyValuePair<int, LssTime> kv in Segments[last].History)
            {
                int id = kv.Key;
                if (double.IsNaN(kv.Value.Get(timing))) continue;
                if (after >= 0)
                {
                    LssTime before;
                    if (!Segments[after].History.TryGetValue(id, out before)) continue;
                    if (double.IsNaN(before.Get(timing))) continue;
                }
                double sum = 0;
                bool ok = true;
                for (int i = after + 1; i <= last; i++)
                {
                    LssTime t;
                    if (!Segments[i].History.TryGetValue(id, out t)) { ok = false; break; }
                    double v = t.Get(timing);
                    if (!double.IsNaN(v)) sum += v;
                }
                if (ok && (double.IsNaN(best) || sum < best)) best = sum;
            }
            return best;
        }
    }

    public static class LssFile
    {
        /// The run, or null with `error` saying why in words a runner can
        /// act on.
        public static LssRun Parse(string text, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
            {
                error = "The file is empty.";
                return null;
            }

            XmlDocument doc = new XmlDocument();
            doc.XmlResolver = null;   // never fetch anything a file names
            try { doc.LoadXml(text); }
            catch (XmlException e)
            {
                error = "Not a LiveSplit splits file (not valid XML: " + e.Message + ")";
                return null;
            }

            XmlElement root = doc.DocumentElement;
            if (root == null || root.Name != "Run")
            {
                string name = root == null ? "nothing" : root.Name;
                error = name == "Layout"
                    ? "This is a LiveSplit layout (.lsl), not a splits file - open the .lss."
                    : "Not a LiveSplit splits file (it starts with <" + name + ">, expected <Run>).";
                return null;
            }

            try { return Read(root); }
            catch (FormatException e)
            {
                error = e.Message;
                return null;
            }
        }

        private static LssRun Read(XmlElement root)
        {
            LssRun run = new LssRun();
            run.Version = root.GetAttribute("version");
            run.GameName = Text(Child(root, "GameName"));
            run.CategoryName = Text(Child(root, "CategoryName"));

            string offset = Text(Child(root, "Offset"));
            if (offset.Length > 0) run.Offset = ParseTimeOrThrow(offset, "the offset");

            int count;
            if (int.TryParse(Text(Child(root, "AttemptCount")), NumberStyles.Integer, CultureInfo.InvariantCulture, out count))
                run.AttemptCount = count;

            XmlElement history = Child(root, "AttemptHistory");
            if (history != null)
                foreach (XmlNode n in history.ChildNodes)
                {
                    XmlElement e = n as XmlElement;
                    if (e == null || e.Name != "Attempt") continue;
                    LssAttempt a = new LssAttempt();
                    a.Id = IdOf(e, "an attempt");
                    a.Started = e.GetAttribute("started");
                    a.Ended = e.GetAttribute("ended");
                    a.Time = ReadTime(e, "attempt " + a.Id);
                    run.Attempts.Add(a);
                }

            XmlElement segments = Child(root, "Segments");
            if (segments != null)
                foreach (XmlNode n in segments.ChildNodes)
                {
                    XmlElement e = n as XmlElement;
                    if (e == null || e.Name != "Segment") continue;
                    run.Segments.Add(ReadSegment(e, run));
                }

            if (run.Segments.Count == 0)
                throw new FormatException("The splits file has no segments.");
            return run;
        }

        private static LssSegment ReadSegment(XmlElement e, LssRun run)
        {
            LssSegment s = new LssSegment();
            s.Name = Text(Child(e, "Name"));
            string where = "segment '" + s.Name + "'";

            XmlElement splits = Child(e, "SplitTimes");
            if (splits != null)
                foreach (XmlNode n in splits.ChildNodes)
                {
                    XmlElement st = n as XmlElement;
                    if (st == null || st.Name != "SplitTime") continue;
                    string name = st.GetAttribute("name");
                    if (name.Length == 0) continue;
                    s.SplitTimes[name] = ReadTime(st, where + " (" + name + ")");
                    if (!run.Comparisons.Contains(name)) run.Comparisons.Add(name);
                }

            // Before LiveSplit 1.6 the PB was its own element.
            XmlElement oldPb = Child(e, "PersonalBestSplitTime");
            if (oldPb != null && !s.SplitTimes.ContainsKey(LssRun.PersonalBest))
            {
                s.SplitTimes[LssRun.PersonalBest] = ReadTime(oldPb, where + " (Personal Best)");
                if (!run.Comparisons.Contains(LssRun.PersonalBest)) run.Comparisons.Add(LssRun.PersonalBest);
            }

            XmlElement best = Child(e, "BestSegmentTime");
            if (best != null) s.BestSegment = ReadTime(best, where + " (best segment)");

            XmlElement history = Child(e, "SegmentHistory");
            if (history != null)
                foreach (XmlNode n in history.ChildNodes)
                {
                    XmlElement t = n as XmlElement;
                    if (t == null || t.Name != "Time") continue;
                    int id = IdOf(t, where + "'s history");
                    s.History[id] = ReadTime(t, where + " (history " + id + ")");
                }
            return s;
        }

        /// <RealTime> / <GameTime> children, or (old files) the element's
        /// own text as real time.
        private static LssTime ReadTime(XmlElement e, string where)
        {
            LssTime t = LssTime.None;
            XmlElement real = Child(e, "RealTime");
            XmlElement game = Child(e, "GameTime");
            if (real != null || game != null)
            {
                string r = Text(real), g = Text(game);
                if (r.Length > 0) t.Real = ParseTimeOrThrow(r, where);
                if (g.Length > 0) t.Game = ParseTimeOrThrow(g, where);
                return t;
            }
            bool hasChildElement = false;
            foreach (XmlNode n in e.ChildNodes)
                if (n is XmlElement) { hasChildElement = true; break; }
            string own = hasChildElement ? "" : e.InnerText.Trim();
            if (own.Length > 0) t.Real = ParseTimeOrThrow(own, where);
            return t;
        }

        private static int IdOf(XmlElement e, string where)
        {
            int id;
            string s = e.GetAttribute("id");
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                throw new FormatException("Bad attempt id '" + s + "' in " + where + ".");
            return id;
        }

        private static double ParseTimeOrThrow(string s, string where)
        {
            double v;
            if (!TryParseTime(s, out v))
                throw new FormatException("Bad time '" + s + "' in " + where + ".");
            return v;
        }

        /// "[-][d.]hh:mm:ss[.fffffff]" (TimeSpan text; "mm:ss" and plain
        /// seconds too) to seconds. Never culture-dependent.
        public static bool TryParseTime(string s, out double seconds)
        {
            seconds = double.NaN;
            if (s == null) return false;
            s = s.Trim();
            if (s.Length == 0) return false;
            bool neg = false;
            if (s[0] == '-') { neg = true; s = s.Substring(1); }

            string[] p = s.Split(':');
            if (p.Length > 3) return false;
            double total = 0;
            for (int i = 0; i < p.Length; i++)
            {
                string part = p[i];
                bool first = i == 0, last = i == p.Length - 1;
                double v;
                if (last)
                {
                    // seconds: digits, optional fraction
                    if (!IsDecimal(part)) return false;
                    v = double.Parse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
                    if (p.Length > 1 && v >= 60) return false;
                }
                else if (first && p.Length == 3 && part.IndexOf('.') >= 0)
                {
                    // days.hours
                    int dot = part.IndexOf('.');
                    string d = part.Substring(0, dot), h = part.Substring(dot + 1);
                    if (!IsDigits(d) || !IsDigits(h)) return false;
                    v = long.Parse(d, CultureInfo.InvariantCulture) * 24.0 + long.Parse(h, CultureInfo.InvariantCulture);
                }
                else
                {
                    if (!IsDigits(part)) return false;
                    v = long.Parse(part, CultureInfo.InvariantCulture);
                    if (!first && v >= 60) return false;
                }
                total = total * 60 + v;
            }
            seconds = neg ? -total : total;
            return true;
        }

        private static bool IsDigits(string s)
        {
            if (s.Length == 0) return false;
            for (int i = 0; i < s.Length; i++) if (s[i] < '0' || s[i] > '9') return false;
            return true;
        }

        private static bool IsDecimal(string s)
        {
            int dot = s.IndexOf('.');
            if (dot < 0) return IsDigits(s);
            return IsDigits(s.Substring(0, dot)) && IsDigits(s.Substring(dot + 1));
        }

        private static XmlElement Child(XmlElement e, string name)
        {
            if (e == null) return null;
            foreach (XmlNode n in e.ChildNodes)
            {
                XmlElement c = n as XmlElement;
                if (c != null && c.Name == name) return c;
            }
            return null;
        }

        private static string Text(XmlElement e)
        {
            return e == null ? "" : e.InnerText.Trim();
        }
    }

    // ------------------------------------------------------------------
    // LiveSplit segments <-> a Segment's splits-table rows, by name.
    //
    // Names compare with case, spaces and punctuation ignored ("Vault
    // Door" = "vault-door" = "VAULT_DOOR"), after LiveSplit's subsplit
    // marks are dropped ("-Cave 5" and "{Caves} Cave 5" are "Cave 5").
    // The endgame events also answer to the autosplitter's labels
    // (1deter/auto-splitters, The Forest.ASL README: "Finding Timmy
    // (Artifact)", "Gold Keycard (Red Elevator)" ...).
    //
    // Rows match in order: each takes the first LiveSplit segment after
    // the previous row's, so repeated names (two "Stick" splits) pair up
    // in turn and the times stay cumulative. A row whose name only exists
    // earlier is reported unmatched.
    //
    // The segment's clock starts where the LiveSplit segment before row
    // 0's match ended (StartAfter; -1 = LiveSplit's own start): a practice
    // segment covering the middle of a run compares against that part.
    // ------------------------------------------------------------------
    public sealed class LssMatch
    {
        /// Row -> LiveSplit segment index; -1 = no match.
        public int[] Map = new int[0];
        /// The LiveSplit segment our start follows; -1 = LiveSplit's start.
        public int StartAfter = -1;
        public readonly List<string> UnmatchedRows = new List<string>();
        public readonly List<string> UnmatchedSplits = new List<string>();

        public bool Complete { get { return UnmatchedRows.Count == 0 && Map.Length > 0; } }

        /// Our endgame event names and the labels runners use for them.
        private static readonly string[][] Aliases =
        {
            new[] { "vault-door", "Vault Door" },
            new[] { "timmy-pickup", "Finding Timmy (Artifact)", "Finding Timmy", "Timmy" },
            new[] { "megan-transform", "Approaching Megan" },
            new[] { "megan-to-machine", "Putting Megan in Artifact", "Megan in Artifact" },
            new[] { "gold-door", "Gold Keycard (Automatic Door)", "Gold Door", "Automatic Door" },
            new[] { "red-elevator", "Gold Keycard (Red Elevator)", "Red Elevator" },
            new[] { "game-end", "Game End" },
        };

        /// `rows` are the segment's split names (Segment.SplitName, the end
        /// last). `endToLast`: an end row no name matches takes LiveSplit's
        /// last split - for a segment that ends where the run does.
        public static LssMatch Match(string[] rows, string[] splits, bool endToLast)
        {
            LssMatch m = new LssMatch();
            m.Map = new int[rows.Length];
            string[] keys = new string[splits.Length];
            for (int j = 0; j < splits.Length; j++) keys[j] = Key(splits[j]);
            bool[] used = new bool[splits.Length];

            int prev = -1;
            for (int i = 0; i < rows.Length; i++)
            {
                m.Map[i] = -1;
                string key = Key(rows[i]);
                for (int j = prev + 1; j < splits.Length; j++)
                    if (Same(key, keys[j])) { m.Map[i] = j; break; }
                if (m.Map[i] < 0 && endToLast && i == rows.Length - 1 && splits.Length - 1 > prev)
                    m.Map[i] = splits.Length - 1;
                if (m.Map[i] >= 0) { prev = m.Map[i]; used[prev] = true; }
                else m.UnmatchedRows.Add(rows[i]);
            }

            if (rows.Length > 0 && m.Map[0] >= 0) m.StartAfter = m.Map[0] - 1;
            for (int j = 0; j < splits.Length; j++)
                if (!used[j] && j > m.StartAfter) m.UnmatchedSplits.Add(splits[j]);
            return m;
        }

        /// A match the runner set by hand: `map` is row -> LiveSplit segment
        /// (-1 = none), any values; the unmatched lists and StartAfter are
        /// worked out as Match does. Out-of-range entries read as -1.
        public static LssMatch FromMap(int[] map, string[] rows, string[] splits)
        {
            LssMatch m = new LssMatch();
            m.Map = new int[rows.Length];
            bool[] used = new bool[splits.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                int j = map != null && i < map.Length ? map[i] : -1;
                if (j < -1 || j >= splits.Length) j = -1;
                m.Map[i] = j;
                if (j >= 0) used[j] = true;
                else m.UnmatchedRows.Add(rows[i]);
            }
            if (rows.Length > 0 && m.Map[0] >= 0) m.StartAfter = m.Map[0] - 1;
            for (int j = 0; j < splits.Length; j++)
                if (!used[j] && j > m.StartAfter) m.UnmatchedSplits.Add(splits[j]);
            return m;
        }

        /// True when every matched row comes after the one before it - the
        /// order golds and segment times assume.
        public bool InOrder
        {
            get
            {
                int prev = StartAfter;
                for (int i = 0; i < Map.Length; i++)
                {
                    if (Map[i] < 0) continue;
                    if (Map[i] <= prev) return false;
                    prev = Map[i];
                }
                return true;
            }
        }

        /// A comparison's cumulative LiveSplit times as our rows' split
        /// times, from our start. NaN for an unmatched row, and for all of
        /// them when the time our start stands on is unknown.
        public float[] Project(double[] cumulative)
        {
            float[] rows = SplitStats.Filled(Map.Length);
            double zero = 0;
            if (StartAfter >= 0)
            {
                zero = StartAfter < cumulative.Length ? cumulative[StartAfter] : double.NaN;
                if (double.IsNaN(zero)) return rows;
            }
            for (int i = 0; i < Map.Length; i++)
                if (Map[i] >= 0 && Map[i] < cumulative.Length) rows[i] = (float)(cumulative[Map[i]] - zero);
            return rows;
        }

        /// Our rows' golds: each row spans the LiveSplit segments since the
        /// previous row's match (LssRun.BestRange). NaN when the row or the
        /// one before it is unmatched.
        public float[] Golds(LssRun run, LssTiming timing)
        {
            float[] g = SplitStats.Filled(Map.Length);
            for (int i = 0; i < Map.Length; i++)
            {
                int after = i == 0 ? StartAfter : Map[i - 1];
                if (Map[i] < 0 || (i > 0 && after < 0)) continue;
                g[i] = (float)run.BestRange(after, Map[i], timing);
            }
            return g;
        }

        /// The comparison key: subsplit marks off, then letters and digits
        /// only, lower case.
        public static string Key(string name)
        {
            if (name == null) return "";
            string s = name.Trim();
            if (s.StartsWith("-")) s = s.Substring(1);
            if (s.StartsWith("{"))
            {
                int close = s.IndexOf('}');
                if (close > 0) s = s.Substring(close + 1);
            }
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
                if (char.IsLetterOrDigit(s[i])) sb.Append(char.ToLowerInvariant(s[i]));
            return sb.ToString();
        }

        private static bool Same(string a, string b)
        {
            if (a.Length == 0 || b.Length == 0) return false;
            if (a == b) return true;
            for (int g = 0; g < Aliases.Length; g++)
            {
                bool hasA = false, hasB = false;
                for (int k = 0; k < Aliases[g].Length; k++)
                {
                    string alias = Key(Aliases[g][k]);
                    if (alias == a) hasA = true;
                    if (alias == b) hasB = true;
                }
                if (hasA && hasB) return true;
            }
            return false;
        }
    }

    /// A segment's link to a LiveSplit file, one line per segment in
    /// config/ForestOverlay/livesplit/links.txt (the runner's own, never
    /// shared - a segment file names no LiveSplit file):
    ///   <segment id> TAB <file name> TAB real|game TAB <map: 2,3,-1 or empty = by name>
    public sealed class LssLink
    {
        public string SegmentId = "";
        public string File = "";
        public LssTiming Timing = LssTiming.RealTime;
        /// Hand-set row -> split map; null = matched by name.
        public int[] Map;

        public static List<LssLink> ParseAll(string text)
        {
            List<LssLink> all = new List<LssLink>();
            if (string.IsNullOrEmpty(text)) return all;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0 || line[0] == '#') continue;
                string[] f = line.Split('\t');
                if (f.Length < 2 || f[0].Trim().Length == 0 || f[1].Trim().Length == 0) continue;
                LssLink l = new LssLink();
                l.SegmentId = f[0].Trim();
                l.File = f[1].Trim();
                if (f.Length > 2 && f[2].Trim() == "game") l.Timing = LssTiming.GameTime;
                if (f.Length > 3 && f[3].Trim().Length > 0)
                {
                    string[] parts = f[3].Split(',');
                    int[] map = new int[parts.Length];
                    bool ok = true;
                    for (int k = 0; k < parts.Length; k++)
                        if (!int.TryParse(parts[k].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out map[k])) ok = false;
                    if (ok) l.Map = map;
                }
                all.Add(l);
            }
            return all;
        }

        public static string FormatAll(List<LssLink> links)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("# LiveSplit files linked to segments (ForestOverlay). segment id, file, real|game, row map\n");
            for (int i = 0; i < links.Count; i++)
            {
                LssLink l = links[i];
                sb.Append(l.SegmentId).Append('\t').Append(l.File).Append('\t')
                  .Append(l.Timing == LssTiming.GameTime ? "game" : "real").Append('\t');
                if (l.Map != null)
                    for (int k = 0; k < l.Map.Length; k++)
                    {
                        if (k > 0) sb.Append(',');
                        sb.Append(l.Map[k].ToString(CultureInfo.InvariantCulture));
                    }
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }

    /// What a LiveSplit file gives one segment: the arrays SplitTable.Fill
    /// takes as `compare` (cumulative per row, from our start) and the
    /// golds per row. The UI offers PbSplits as "LiveSplit PB" and
    /// GoldSplits as "LiveSplit golds" under Runs -> Compare to.
    public sealed class LssComparison
    {
        public LssTiming Timing;
        public LssMatch Match;
        public float[] PbSplits;
        public float[] Golds;
        public float[] GoldSplits;
        public float Pb = float.NaN;
        public float SumOfBest = float.NaN;

        public static LssComparison Build(LssRun run, LssMatch match, LssTiming timing)
        {
            LssComparison c = new LssComparison();
            c.Timing = timing;
            c.Match = match;
            c.PbSplits = match.Project(run.SplitTimes(LssRun.PersonalBest, timing));
            c.Golds = match.Golds(run, timing);
            c.GoldSplits = SplitStats.Cumulative(c.Golds);
            if (c.PbSplits.Length > 0)
            {
                c.Pb = c.PbSplits[c.PbSplits.Length - 1];
                c.SumOfBest = c.GoldSplits[c.GoldSplits.Length - 1];
            }
            return c;
        }

        /// Any other comparison the file holds (a runner's "WR" ...).
        public static float[] Other(LssRun run, LssMatch match, string comparison, LssTiming timing)
        {
            return match.Project(run.SplitTimes(comparison, timing));
        }
    }
}
