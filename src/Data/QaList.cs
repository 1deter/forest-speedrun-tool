using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // QA test lists and a tester's answers (QA tab, v0.24.56).
    //
    // A list is a text file shipped in the DLL (qa/*.txt), numbered the
    // way it was sent to the testers, so answers read the same in game,
    // in chat and in the report:
    //
    //   # comment
    //   id = qa-2026-09-25
    //   title = ForestOverlay v0.24.43 - QA list
    //   intro = A line shown above the items (repeatable)
    //   section = Resetting mid-swing
    //   1) Swing, then press F7 halfway through. ...
    //   seen = ended attack state
    //
    // `seen` belongs to the item above it (repeatable): when a log line
    // containing that text appears, the item shows the line - evidence to
    // look at, not a pass. Pass / Fail stays the tester's call.
    //
    // Answers live in qa/answers/<id>.txt:
    //
    //   1 = pass | fail | skip
    //   1 note = text
    //   1 seen = 17:21:05 <log line>
    //
    // Pure, tested.
    // ------------------------------------------------------------------
    public enum QaResult { None, Pass, Fail, Skip }

    public sealed class QaItem
    {
        public int Number;
        public string Text;
        public string Section;
        public readonly List<string> Seen = new List<string>();
    }

    public sealed class QaAnswer
    {
        public QaResult Result;
        public string Note = "";
        /// First matching log line, with its time.
        public string Seen = "";

        public bool IsEmpty
        {
            get { return Result == QaResult.None && Note.Length == 0 && Seen.Length == 0; }
        }
    }

    public sealed class QaList
    {
        public string Id = "";
        public string Title = "";
        public readonly List<string> Intro = new List<string>();
        public readonly List<QaItem> Items = new List<QaItem>();
        /// Lines that could not be read, for the tab to show.
        public readonly List<string> Problems = new List<string>();

        public QaItem ByNumber(int n)
        {
            for (int i = 0; i < Items.Count; i++)
                if (Items[i].Number == n) return Items[i];
            return null;
        }

        // --------------------------------------------------------------
        public static QaList Parse(string text)
        {
            QaList list = new QaList();
            string section = "";
            string[] lines = SplitLines(text);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                int number;
                string rest;
                if (TryItem(line, out number, out rest))
                {
                    if (list.ByNumber(number) != null)
                    {
                        list.Problems.Add("line " + (i + 1) + ": item " + number + " twice");
                        continue;
                    }
                    QaItem item = new QaItem();
                    item.Number = number;
                    item.Text = rest;
                    item.Section = section;
                    list.Items.Add(item);
                    continue;
                }

                string key, value;
                if (!TryPair(line, out key, out value))
                {
                    list.Problems.Add("line " + (i + 1) + ": not understood");
                    continue;
                }

                switch (key)
                {
                    case "id": list.Id = value; break;
                    case "title": list.Title = value; break;
                    case "intro": list.Intro.Add(value); break;
                    case "section": section = value; break;
                    case "seen":
                        if (list.Items.Count == 0 || value.Length == 0)
                            list.Problems.Add("line " + (i + 1) + ": 'seen' needs an item above it and some text");
                        else
                            list.Items[list.Items.Count - 1].Seen.Add(value);
                        break;
                    default:
                        list.Problems.Add("line " + (i + 1) + ": unknown key '" + key + "'");
                        break;
                }
            }

            if (list.Id.Length == 0) list.Problems.Add("no id");
            return list;
        }

        /// "12) text" -> 12, "text".
        private static bool TryItem(string line, out int number, out string rest)
        {
            number = 0;
            rest = null;
            int close = line.IndexOf(')');
            if (close <= 0 || close > 4) return false;
            for (int i = 0; i < close; i++)
                if (line[i] < '0' || line[i] > '9') return false;
            number = int.Parse(line.Substring(0, close), CultureInfo.InvariantCulture);
            rest = line.Substring(close + 1).Trim();
            return true;
        }

        private static bool TryPair(string line, out string key, out string value)
        {
            key = value = null;
            int eq = line.IndexOf('=');
            if (eq <= 0) return false;
            key = line.Substring(0, eq).Trim().ToLowerInvariant();
            value = line.Substring(eq + 1).Trim();
            return true;
        }

        private static string[] SplitLines(string text)
        {
            return (text ?? "").Replace("\r\n", "\n").Split('\n');
        }

        // --------------------------------------------------------------
        /// The first `seen` text found in a log line, or null.
        public static bool Matches(QaItem item, string logLine)
        {
            if (logLine == null) return false;
            for (int i = 0; i < item.Seen.Count; i++)
                if (logLine.IndexOf(item.Seen[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // --------------------------------------------------------------
        public static Dictionary<int, QaAnswer> ParseAnswers(string text)
        {
            Dictionary<int, QaAnswer> answers = new Dictionary<int, QaAnswer>();
            string[] lines = SplitLines(text);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                string key, value;
                if (!TryPair(line, out key, out value)) continue;

                string field = "";
                int space = key.IndexOf(' ');
                if (space > 0)
                {
                    field = key.Substring(space + 1).Trim();
                    key = key.Substring(0, space);
                }

                int n;
                if (!TryNumber(key, out n)) continue;

                QaAnswer a;
                if (!answers.TryGetValue(n, out a)) { a = new QaAnswer(); answers[n] = a; }

                if (field.Length == 0) a.Result = ParseResult(value);
                else if (field == "note") a.Note = value;
                else if (field == "seen") a.Seen = value;
            }
            return answers;
        }

        public static string FormatAnswers(QaList list, Dictionary<int, QaAnswer> answers)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("# ForestOverlay QA answers for ").Append(list.Id).Append('\n');
            List<int> keys = new List<int>(answers.Keys);
            keys.Sort();
            for (int i = 0; i < keys.Count; i++)
            {
                QaAnswer a = answers[keys[i]];
                if (a.IsEmpty) continue;
                string n = keys[i].ToString(CultureInfo.InvariantCulture);
                if (a.Result != QaResult.None) sb.Append(n).Append(" = ").Append(ResultWord(a.Result)).Append('\n');
                if (a.Note.Length > 0) sb.Append(n).Append(" note = ").Append(OneLine(a.Note)).Append('\n');
                if (a.Seen.Length > 0) sb.Append(n).Append(" seen = ").Append(OneLine(a.Seen)).Append('\n');
            }
            return sb.ToString();
        }

        /// The readable part of a report: every item, its answer, note and
        /// the log line seen for it, numbered as sent.
        public static string FormatReport(QaList list, Dictionary<int, QaAnswer> answers, string tester, string version, string when)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(list.Title.Length > 0 ? list.Title : list.Id).Append('\n');
            sb.Append("Tester: ").Append(string.IsNullOrEmpty(tester) ? "(no name set)" : tester).Append('\n');
            sb.Append("Plugin: v").Append(version).Append('\n');
            sb.Append("Written: ").Append(when).Append('\n');

            int pass = 0, fail = 0, skip = 0, open = 0;
            for (int i = 0; i < list.Items.Count; i++)
            {
                QaAnswer a;
                answers.TryGetValue(list.Items[i].Number, out a);
                QaResult r = a != null ? a.Result : QaResult.None;
                if (r == QaResult.Pass) pass++;
                else if (r == QaResult.Fail) fail++;
                else if (r == QaResult.Skip) skip++;
                else open++;
            }
            sb.Append("Pass ").Append(pass).Append(", fail ").Append(fail).Append(", skipped ").Append(skip)
              .Append(", not answered ").Append(open).Append("\n\n");

            string section = null;
            for (int i = 0; i < list.Items.Count; i++)
            {
                QaItem item = list.Items[i];
                if (item.Section != section)
                {
                    section = item.Section;
                    if (section.Length > 0) sb.Append(section).Append('\n');
                }
                QaAnswer a;
                answers.TryGetValue(item.Number, out a);
                sb.Append(item.Number).Append(") [")
                  .Append(a != null && a.Result != QaResult.None ? ResultWord(a.Result) : "-")
                  .Append("] ").Append(item.Text).Append('\n');
                if (a != null && a.Note.Length > 0) sb.Append("   note: ").Append(a.Note).Append('\n');
                if (a != null && a.Seen.Length > 0) sb.Append("   log: ").Append(a.Seen).Append('\n');
            }
            return sb.ToString();
        }

        public static string ResultWord(QaResult r)
        {
            switch (r)
            {
                case QaResult.Pass: return "pass";
                case QaResult.Fail: return "fail";
                case QaResult.Skip: return "skip";
                default: return "";
            }
        }

        private static QaResult ParseResult(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "pass": return QaResult.Pass;
                case "fail": return QaResult.Fail;
                case "skip": return QaResult.Skip;
                default: return QaResult.None;
            }
        }

        private static bool TryNumber(string s, out int n)
        {
            n = 0;
            if (s.Length == 0 || s.Length > 4) return false;
            for (int i = 0; i < s.Length; i++)
                if (s[i] < '0' || s[i] > '9') return false;
            n = int.Parse(s, CultureInfo.InvariantCulture);
            return true;
        }

        private static string OneLine(string s)
        {
            return s.Replace("\r", " ").Replace("\n", " ");
        }
    }
}
