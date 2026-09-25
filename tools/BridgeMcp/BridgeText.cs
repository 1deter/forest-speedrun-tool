using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ForestOverlay.BridgeMcp
{
    // ------------------------------------------------------------------
    // The text side of the bridge protocol (src/Modules/BridgeModule.cs):
    // quoting arguments for its tokenizer and reading its replies back.
    // Pure, so it is tested (linked into tests/ForestOverlay.Tests).
    //
    //   > #12 find _Dummy 100
    //   #-51234  mutant_male_Dummy(Clone)  (10.2, 30, 55.1)  4.3 m
    //   1 object(s), nearest first
    //   < #12 ok (3 ms)
    //   < #13 error: no player (0 ms)
    // ------------------------------------------------------------------
    public static class BridgeText
    {
        /// One reply block: a command and the lines it printed.
        public sealed class Block
        {
            public int Id;
            public string Command;
            public readonly List<string> Lines = new List<string>();
            public bool Closed;
            public string Error;     // null: ok (or not closed yet)
            public string Took;

            public bool Ok { get { return Closed && Error == null; } }
        }

        public sealed class Transcript
        {
            public readonly List<Block> Blocks = new List<Block>();
            /// Lines outside any block: echo output, the "bridge on" banner,
            /// anim watch samples between commands.
            public readonly List<string> Other = new List<string>();
            /// Blocks and other lines in the order they came (a Block or a string).
            public readonly List<object> Items = new List<object>();

            public int Errors
            {
                get
                {
                    int n = 0;
                    for (int i = 0; i < Blocks.Count; i++) if (Blocks[i].Error != null) n++;
                    return n;
                }
            }
        }

        private static readonly Regex Open = new Regex(@"^> #(\d+) (.*)$");
        private static readonly Regex CloseOk = new Regex(@"^< #(\d+) ok \(([^()]*)\)$");
        private static readonly Regex CloseError = new Regex(@"^< #(\d+) error: (.*) \(([^()]*)\)$");

        /// Splits out.txt text into blocks. The marker line (the batch's
        /// closing echo) is dropped wherever it appears.
        public static Transcript Parse(string raw, string marker)
        {
            Transcript t = new Transcript();
            if (string.IsNullOrEmpty(raw)) return t;

            Block open = null;
            string[] lines = raw.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0) continue;
                if (marker != null && line.Trim() == marker) continue;

                Match m = Open.Match(line);
                if (m.Success)
                {
                    open = new Block { Id = Int(m.Groups[1].Value), Command = m.Groups[2].Value };
                    t.Blocks.Add(open);
                    t.Items.Add(open);
                    continue;
                }

                m = CloseOk.Match(line);
                bool isError = false;
                if (!m.Success) { m = CloseError.Match(line); isError = m.Success; }
                if (m.Success)
                {
                    Block b = FindOpen(t, Int(m.Groups[1].Value));
                    if (b == null)
                    {
                        // Closing a command from an earlier batch (it waited
                        // past that call's timeout): keep the line, visibly.
                        t.Other.Add(line);
                        t.Items.Add(line);
                        continue;
                    }
                    b.Closed = true;
                    if (isError) { b.Error = m.Groups[2].Value; b.Took = m.Groups[3].Value; }
                    else b.Took = m.Groups[2].Value;
                    if (open == b) open = null;
                    continue;
                }

                if (open != null) open.Lines.Add(line);
                else { t.Other.Add(line); t.Items.Add(line); }
            }
            return t;
        }

        private static Block FindOpen(Transcript t, int id)
        {
            for (int i = t.Blocks.Count - 1; i >= 0; i--)
                if (t.Blocks[i].Id == id && !t.Blocks[i].Closed) return t.Blocks[i];
            return null;
        }

        private static int Int(string s)
        {
            int n;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0;
        }

        /// One argument for the bridge's tokenizer: spaces split tokens,
        /// double quotes group them, and there is no escape - so a quote
        /// inside becomes an apostrophe and a line break a space.
        public static string Arg(string s)
        {
            if (s == null) s = "";
            string clean = s.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('"', '\'');
            bool needs = clean.Length == 0 || clean.IndexOf(' ') >= 0 || clean.IndexOf('\t') >= 0;
            return needs ? "\"" + clean + "\"" : clean;
        }

        /// A whole command line as the caller wrote it: one line, trimmed.
        public static string Line(string s)
        {
            if (s == null) return "";
            return s.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        }

        /// A command from its parts, each quoted as needed.
        public static string Command(string cmd, params string[] args)
        {
            StringBuilder sb = new StringBuilder(cmd);
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == null) continue;
                sb.Append(' ').Append(Arg(args[i]));
            }
            return sb.ToString();
        }

        /// The value of a `get` reply line: `path = value  (Type)`. A
        /// string's quotes are removed. Null when the line is not one.
        public static string Value(string line)
        {
            if (line == null) return null;
            int eq = line.IndexOf(" = ", StringComparison.Ordinal);
            if (eq < 0) return null;
            string v = line.Substring(eq + 3);
            int type = v.LastIndexOf("  (", StringComparison.Ordinal);
            if (type >= 0 && v.EndsWith(")")) v = v.Substring(0, type);
            if (v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"') v = v.Substring(1, v.Length - 2);
            return v;
        }

        /// Seconds a batch may take: a base plus every `wait`, and a long
        /// allowance for each command that waits on the game (a Full load
        /// can take ~15 s, a slow one far more).
        public static double EstimateSeconds(IList<string> commands)
        {
            double s = 30;
            for (int i = 0; i < commands.Count; i++)
            {
                string c = Line(commands[i]);
                int sp = c.IndexOf(' ');
                string head = (sp < 0 ? c : c.Substring(0, sp)).ToLowerInvariant();
                string rest = sp < 0 ? "" : c.Substring(sp + 1).Trim();
                double n;
                switch (head)
                {
                    case "wait":
                        if (double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out n)) s += n;
                        break;
                    case "waitidle":
                    case "restart":
                    case "restore":
                    case "capture":
                        s += 150;
                        break;
                    case "shot":
                        s += 2;
                        break;
                }
            }
            return s;
        }

        /// The version in the newest "== bridge on, ForestOverlay vX, ..."
        /// banner of out.txt text, or null.
        public static string BannerVersion(string text)
        {
            if (text == null) return null;
            const string key = "== bridge on, ForestOverlay v";
            int at = text.LastIndexOf(key, StringComparison.Ordinal);
            if (at < 0) return null;
            int from = at + key.Length;
            int end = text.IndexOf(',', from);
            return end < 0 ? null : text.Substring(from, end - from);
        }
    }
}
