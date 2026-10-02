using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // A run attempt's log and its hash chain (docs/run-mode.md, phase 2:
    // anti-splice codes). Pure: the plugin writes it (Modules/RunModeModule),
    // the site replays it (site/ForestSite/Attempts) - one file, linked in.
    //
    // The log is text, one record a line. Every line before `[report]` is
    // folded into a SHA-256 chain: head = SHA256(head + line), starting
    // from SHA256 of the format line. Once a second the game adds a `step`
    // (real time, the timer, the player's position in cm), and the code
    // on screen is the first 20 bits of the head after it, as four
    // characters (Crockford base32: no I, L, O, U).
    //
    // Why it stops splicing: every code depends on everything before it,
    // the site's nonce included, so a piece of video from another attempt
    // shows codes this log cannot produce; a log made after the fact fails
    // the checkpoints the site received during the run (the head after a
    // step, about once a minute, timed by the site's clock). Nothing here
    // is secret - the format is public; the server's nonce and its clock
    // are what a cheater cannot make up.
    //
    //   forest-attempt 1
    //   attempt|a-<16 hex>          made on the PC (the link exists offline)
    //   runner|<id>|<name>
    //   plugin|<version>
    //   category|<category>
    //   spot|<segment id>|<start state hash>     (or spot|-|-)
    //   seed|<32 hex>               local randomness, so codes differ at once
    //   started|<UTC, the PC's clock - informational>
    //   step|<n>|<real ms>|<timer ms or ->|<x cm>|<y cm>|<z cm>  (or -|-|-)
    //   nonce|<real ms>|<hex>       the site's, folded when it arrives
    //   split|<real ms>|<row>|<timer ms>
    //   flag|<real ms>|<why the attempt is not valid>
    //   end|<real ms>|<reason>|<final timer ms or ->
    //   [report]                    the run report, not folded (a claim)
    // ------------------------------------------------------------------
    public sealed class AttemptChain
    {
        public const string FormatLine = "forest-attempt 1";
        public const string ReportMarker = "[report]";
        public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        public const string NoCode = "----";

        private readonly SHA256 _sha = SHA256.Create();
        private readonly StringBuilder _text = new StringBuilder(4096);
        private byte[] _head;

        public AttemptChain()
        {
            _head = _sha.ComputeHash(Encoding.UTF8.GetBytes(FormatLine));
            _text.Append(FormatLine).Append('\n');
            Code = NoCode;
        }

        /// Steps so far (the last step's number).
        public int Steps { get; private set; }

        /// The code after the last step ("----" before the first).
        public string Code { get; private set; }

        /// Real time of the last record, ms since the attempt started.
        public long LastMs { get; private set; }

        public bool Ended { get; private set; }

        /// The head after the last line, lowercase hex.
        public string Head { get { return Hex(_head); } }

        /// The log so far (folded lines only).
        public string Text { get { return _text.ToString(); } }

        // --- writing -------------------------------------------------------

        public void Header(string attemptId, string runnerId, string runnerName, string plugin, string category,
                           string spotId, string spotState, string seed, DateTime startedUtc)
        {
            Add("attempt|" + Clean(attemptId));
            Add("runner|" + Clean(runnerId) + "|" + Clean(runnerName));
            Add("plugin|" + Clean(plugin));
            Add("category|" + Clean(category));
            Add("spot|" + Dash(spotId) + "|" + Dash(spotState));
            Add("seed|" + Clean(seed));
            Add("started|" + startedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        }

        /// One step: `timerMs` < 0 = no timer running; `hasPos` false = no
        /// player (a load). Returns the new code.
        public string Step(long realMs, long timerMs, bool hasPos, float x, float y, float z)
        {
            int n = Steps + 1;
            StringBuilder sb = new StringBuilder(64);
            sb.Append("step|").Append(n.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(realMs.ToString(CultureInfo.InvariantCulture)).Append('|').Append(Ms(timerMs)).Append('|');
            if (hasPos) sb.Append(Cm(x)).Append('|').Append(Cm(y)).Append('|').Append(Cm(z));
            else sb.Append("-|-|-");
            Add(sb.ToString());
            Steps = n;
            LastMs = realMs;
            Code = CodeOf(_head);
            return Code;
        }

        public void Nonce(long realMs, string nonce)
        {
            LastMs = realMs;
            Add("nonce|" + realMs.ToString(CultureInfo.InvariantCulture) + "|" + Clean(nonce));
        }

        public void Split(long realMs, int row, long timerMs)
        {
            LastMs = realMs;
            Add("split|" + realMs.ToString(CultureInfo.InvariantCulture) + "|" + row.ToString(CultureInfo.InvariantCulture) + "|" + Ms(timerMs));
        }

        public void Flag(long realMs, string why)
        {
            LastMs = realMs;
            Add("flag|" + realMs.ToString(CultureInfo.InvariantCulture) + "|" + Clean(why));
        }

        public void End(long realMs, string reason, long finalTimerMs)
        {
            if (Ended) return;
            LastMs = realMs;
            Add("end|" + realMs.ToString(CultureInfo.InvariantCulture) + "|" + Clean(reason) + "|" + Ms(finalTimerMs));
            Ended = true;
        }

        private void Add(string line)
        {
            _head = Fold(_sha, _head, line);
            _text.Append(line).Append('\n');
        }

        // --- the chain -------------------------------------------------------

        public static byte[] Fold(HashAlgorithm sha, byte[] head, string line)
        {
            byte[] l = Encoding.UTF8.GetBytes(line);
            byte[] buf = new byte[head.Length + l.Length];
            Buffer.BlockCopy(head, 0, buf, 0, head.Length);
            Buffer.BlockCopy(l, 0, buf, head.Length, l.Length);
            return sha.ComputeHash(buf);
        }

        /// The first 20 bits of a head as four characters.
        public static string CodeOf(byte[] head)
        {
            int bits = (head[0] << 12) | (head[1] << 4) | (head[2] >> 4);
            char[] c = new char[4];
            for (int i = 3; i >= 0; i--) { c[i] = Alphabet[bits & 31]; bits >>= 5; }
            return new string(c);
        }

        /// A code as typed by a person: upper case, I/L -> 1, O -> 0, spaces
        /// and dashes dropped (Crockford's reading rules). Null if it is not
        /// four valid characters.
        public static string NormaliseCode(string typed)
        {
            if (typed == null) return null;
            StringBuilder sb = new StringBuilder(4);
            for (int i = 0; i < typed.Length; i++)
            {
                char c = char.ToUpperInvariant(typed[i]);
                if (c == ' ' || c == '-') continue;
                if (c == 'I' || c == 'L') c = '1';
                else if (c == 'O') c = '0';
                if (Alphabet.IndexOf(c) < 0) return null;
                sb.Append(c);
            }
            return sb.Length == 4 ? sb.ToString() : null;
        }

        public static string Hex(byte[] b)
        {
            StringBuilder sb = new StringBuilder(b.Length * 2);
            for (int i = 0; i < b.Length; i++) sb.Append(b[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// Random hex from the OS's generator (the seed, attempt ids).
        public static string RandomHex(int bytes)
        {
            byte[] b = new byte[bytes];
            RandomNumberGenerator.Create().GetBytes(b);   // not IDisposable on net35
            return Hex(b);
        }

        public static string NewAttemptId() { return "a-" + RandomHex(8); }

        /// `a-` + 16 hex digits.
        public static bool IsAttemptId(string id)
        {
            if (id == null || id.Length != 18 || !id.StartsWith("a-", StringComparison.Ordinal)) return false;
            for (int i = 2; i < id.Length; i++)
                if (!Uri.IsHexDigit(id[i]) || char.IsUpper(id[i])) return false;
            return true;
        }

        private static string Ms(long ms) { return ms < 0 ? "-" : ms.ToString(CultureInfo.InvariantCulture); }
        private static string Cm(float v) { return ((long)Math.Round(v * 100.0)).ToString(CultureInfo.InvariantCulture); }
        private static string Dash(string s) { s = Clean(s); return s.Length == 0 ? "-" : s; }

        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++) sb.Append(s[i] == '|' || s[i] < ' ' ? ' ' : s[i]);
            return sb.ToString().Trim();
        }

        // --- replay (the site) ------------------------------------------------

        public sealed class StepInfo
        {
            public int N;
            public long RealMs;
            public long TimerMs;   // -1 = no timer
            public string Head;    // hex, after this step
            public string Code;
        }

        public sealed class Replay
        {
            public string Error;    // null = the log reads
            public string AttemptId = "", RunnerId = "", RunnerName = "", Plugin = "", Category = "";
            public string SpotId = "", SpotState = "", Seed = "", Started = "";
            public string Nonce;    // null = none folded
            public long NonceMs = -1;
            public readonly List<StepInfo> Steps = new List<StepInfo>();
            public readonly List<string> Flags = new List<string>();
            public int Splits;
            public bool Ended;
            public string EndReason = "";
            public long EndMs = -1, FinalTimerMs = -1;
            public string Report = "";
            public string FinalHead = "";

            /// The step with number n, or null.
            public StepInfo Step(int n) { return n >= 1 && n <= Steps.Count ? Steps[n - 1] : null; }

            /// Real time the log covers: its last record.
            public long LastMs;
        }

        /// Reads a log and recomputes its chain. Strict: an unknown or
        /// out-of-order line is an error, so a hand-edited log does not read
        /// as a short one.
        public static Replay Read(string text)
        {
            Replay r = new Replay();
            if (string.IsNullOrEmpty(text)) { r.Error = "empty"; return r; }
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines[0] != FormatLine) { r.Error = "not an attempt log (first line)"; return r; }

            using (SHA256 sha = SHA256.Create())
            {
                byte[] head = sha.ComputeHash(Encoding.UTF8.GetBytes(FormatLine));
                bool inSteps = false;
                long last = 0;
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line == ReportMarker)
                    {
                        r.Report = string.Join("\n", lines, i + 1, lines.Length - i - 1).TrimEnd('\n');
                        break;
                    }
                    if (line.Length == 0)
                    {
                        if (i == lines.Length - 1) break;
                        r.Error = "an empty line at " + (i + 1); return r;
                    }
                    if (r.Ended) { r.Error = "a line after end (line " + (i + 1) + ")"; return r; }

                    string[] p = line.Split('|');
                    string kind = p[0];
                    head = Fold(sha, head, line);
                    long ms;
                    switch (kind)
                    {
                        case "attempt": case "runner": case "plugin": case "category": case "spot": case "seed": case "started":
                            if (inSteps) { r.Error = kind + " after the first step (line " + (i + 1) + ")"; return r; }
                            if (!ReadHeader(r, kind, p)) { r.Error = "bad " + kind + " line"; return r; }
                            break;
                        case "step":
                        {
                            int n; long timer;
                            if (p.Length != 7 || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ||
                                !Long(p[2], out ms) || !OptLong(p[3], out timer) || !Pos(p[4]) || !Pos(p[5]) || !Pos(p[6]))
                            { r.Error = "bad step at line " + (i + 1); return r; }
                            if (n != r.Steps.Count + 1) { r.Error = "step " + n + " out of order (line " + (i + 1) + ")"; return r; }
                            if (ms < last) { r.Error = "time goes backwards at step " + n; return r; }
                            if (!inSteps && r.AttemptId.Length == 0) { r.Error = "no attempt line before the steps"; return r; }
                            inSteps = true;
                            last = ms;
                            StepInfo s = new StepInfo();
                            s.N = n; s.RealMs = ms; s.TimerMs = timer; s.Head = Hex(head); s.Code = CodeOf(head);
                            r.Steps.Add(s);
                            break;
                        }
                        case "nonce":
                            if (p.Length != 3 || !Long(p[1], out ms) || p[2].Length == 0) { r.Error = "bad nonce line"; return r; }
                            if (r.Nonce != null) { r.Error = "two nonce lines"; return r; }
                            if (ms < last) { r.Error = "time goes backwards at the nonce"; return r; }
                            last = ms; r.Nonce = p[2]; r.NonceMs = ms;
                            break;
                        case "split":
                        {
                            int row; long timer;
                            if (p.Length != 4 || !Long(p[1], out ms) || !int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out row) ||
                                !OptLong(p[3], out timer)) { r.Error = "bad split line"; return r; }
                            if (ms < last) { r.Error = "time goes backwards at a split"; return r; }
                            last = ms; r.Splits++;
                            break;
                        }
                        case "flag":
                            if (p.Length != 3 || !Long(p[1], out ms)) { r.Error = "bad flag line"; return r; }
                            if (ms < last) { r.Error = "time goes backwards at a flag"; return r; }
                            last = ms; r.Flags.Add(p[2]);
                            break;
                        case "end":
                        {
                            long timer;
                            if (p.Length != 4 || !Long(p[1], out ms) || !OptLong(p[3], out timer)) { r.Error = "bad end line"; return r; }
                            if (ms < last) { r.Error = "time goes backwards at the end"; return r; }
                            last = ms; r.Ended = true; r.EndMs = ms; r.EndReason = p[2]; r.FinalTimerMs = timer;
                            break;
                        }
                        default:
                            r.Error = "unknown line '" + kind + "' (line " + (i + 1) + ")";
                            return r;
                    }
                }
                r.LastMs = last;
                r.FinalHead = Hex(head);
            }
            if (r.AttemptId.Length == 0) r.Error = "no attempt line";
            return r;
        }

        private static bool ReadHeader(Replay r, string kind, string[] p)
        {
            switch (kind)
            {
                case "attempt": if (p.Length != 2 || r.AttemptId.Length > 0) return false; r.AttemptId = p[1]; return true;
                case "runner": if (p.Length != 3) return false; r.RunnerId = p[1]; r.RunnerName = p[2]; return true;
                case "plugin": if (p.Length != 2) return false; r.Plugin = p[1]; return true;
                case "category": if (p.Length != 2) return false; r.Category = p[1]; return true;
                case "spot": if (p.Length != 3) return false; r.SpotId = p[1] == "-" ? "" : p[1]; r.SpotState = p[2] == "-" ? "" : p[2]; return true;
                case "seed": if (p.Length != 2) return false; r.Seed = p[1]; return true;
                case "started": if (p.Length != 2) return false; r.Started = p[1]; return true;
            }
            return false;
        }

        private static bool Long(string s, out long v)
        {
            return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) && v >= 0;
        }

        private static bool OptLong(string s, out long v)
        {
            if (s == "-") { v = -1; return true; }
            return Long(s, out v);
        }

        private static bool Pos(string s)
        {
            long v;
            return s == "-" || long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        }
    }
}
