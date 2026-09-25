using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // One segment as a single file to share (`<id>.foseg`): its definition,
    // its start state and, optionally, recorded attempts - so a route
    // travels whole, and the website (forest.deter.cloud) reads one file
    // per route.
    //
    //   ForestOverlay segment 1
    //   exported = 2026-09-25 23:40:00
    //   plugin   = 0.24.71
    //
    //   [segment]
    //   id       = deter/route.plane-to-cave5       (SegmentFormat's block)
    //   ...
    //
    //   [startstate]
    //   ForestOverlay savestate 1                  (the .fosave, verbatim)
    //   ...
    //
    //   [attempt]
    //   anchor|deter/route.plane-to-cave5          (a .run file, verbatim;
    //   ...                                         one section each)
    //
    // WHY this shape (author, 2026-09-25: "do whatever's easiest"): every
    // section is a file format the plugin already writes and reads, copied
    // unchanged, so there is one parser per format and nothing new to get
    // wrong. A section starts at a line beginning with '[', which none of
    // those formats can produce (savestate header lines start with a key,
    // run lines with a tag, a segment block with a key).
    //
    // Pure so the round trip is tested; the file IO is PracticeModule's.
    // ------------------------------------------------------------------
    public sealed class SegmentBundle
    {
        public const string Magic = "ForestOverlay segment 1";
        public const string Extension = ".foseg";

        public string Exported = "";
        public string PluginVersion = "";
        public Segment Segment;

        /// The .fosave text; null when the segment has none.
        public string StartState;

        /// .run texts, one per attempt.
        public readonly List<string> Attempts = new List<string>();

        public string Write()
        {
            StringBuilder sb = new StringBuilder(256 + (StartState != null ? StartState.Length : 0));
            sb.Append(Magic).Append('\n');
            sb.Append("exported = ").Append(OneLine(Exported)).Append('\n');
            sb.Append("plugin   = ").Append(OneLine(PluginVersion)).Append('\n');

            // Opens with its own blank line and [segment] header.
            SegmentFormat.WriteSegment(sb, Segment, "\n");

            if (StartState != null) Section(sb, "startstate", StartState);
            for (int i = 0; i < Attempts.Count; i++) Section(sb, "attempt", Attempts[i]);
            return sb.ToString();
        }

        /// Null and `error` set when the text is not a bundle this version
        /// can read. Warnings from the segment block (a bad trigger line)
        /// are collected, never fatal.
        public static SegmentBundle Parse(string text, out string error, List<string> warnings)
        {
            error = null;
            if (string.IsNullOrEmpty(text)) { error = "empty file"; return null; }

            string[] lines = text.TrimStart('﻿').Replace("\r\n", "\n").Split('\n');
            if (lines[0].Trim() != Magic)
            {
                error = "not a ForestOverlay segment file";
                return null;
            }

            SegmentBundle b = new SegmentBundle();
            List<string> segmentLines = null;
            StringBuilder body = null;
            string section = "";

            for (int i = 1; i <= lines.Length; i++)
            {
                string line = i < lines.Length ? lines[i] : "[end]";
                bool header = line.Length > 0 && line[0] == '[';

                if (header)
                {
                    Close(b, section, body);
                    body = null;
                    section = line.Trim().TrimStart('[').TrimEnd(']').Trim().ToLowerInvariant();
                    if (section == "segment")
                    {
                        if (segmentLines != null) { error = "more than one [segment] section"; return null; }
                        segmentLines = new List<string>();
                        segmentLines.Add("[segment]");
                    }
                    else if (section == "startstate" || section == "attempt")
                        body = new StringBuilder();
                    continue;
                }

                if (section == "")
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();
                    if (key == "exported") b.Exported = value;
                    else if (key == "plugin") b.PluginVersion = value;
                }
                else if (section == "segment") segmentLines.Add(line);
                else if (body != null) body.Append(line).Append('\n');
            }

            if (segmentLines == null) { error = "no [segment] section"; return null; }
            List<Segment> found = SegmentFormat.ParseAll(segmentLines.ToArray(), delegate(int n, string why)
            {
                if (warnings != null) warnings.Add("segment line " + n + ": " + why);
            });
            if (found.Count != 1 || string.IsNullOrEmpty(found[0].Id)) { error = "the [segment] section has no id"; return null; }
            b.Segment = found[0];
            return b;
        }

        private static void Close(SegmentBundle b, string section, StringBuilder body)
        {
            if (body == null) return;
            string text = body.ToString().Trim('\n');
            if (text.Length == 0) return;
            text += "\n";
            if (section == "startstate") b.StartState = text;
            else if (section == "attempt") b.Attempts.Add(text);
        }

        private static void Section(StringBuilder sb, string name, string text)
        {
            sb.Append('\n').Append('[').Append(name).Append(']').Append('\n');
            string t = text.TrimStart('﻿').Replace("\r\n", "\n").Trim('\n');
            string[] lines = t.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                // Never a section header inside a section: indented, a
                // line reads back unchanged by every format here (they
                // trim), and the file stays readable.
                string line = lines[i];
                if (line.Length > 0 && line[0] == '[') line = " " + line;
                sb.Append(line).Append('\n');
            }
        }

        private static string OneLine(string s)
        {
            return (s ?? "").Replace('\r', ' ').Replace('\n', ' ');
        }

        /// The file name AttemptStore gives this attempt
        /// (`yyyyMMdd_HHmmss_<duration>.run`); null when the text has no
        /// readable recorded / duration line.
        public static string AttemptFileName(string runText)
        {
            if (string.IsNullOrEmpty(runText)) return null;
            DateTime recorded = DateTime.MinValue;
            float duration = -1f;
            bool haveRecorded = false;

            string[] lines = runText.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("recorded|"))
                    haveRecorded = DateTime.TryParse(line.Substring(9), CultureInfo.InvariantCulture,
                                                     DateTimeStyles.RoundtripKind, out recorded);
                else if (line.StartsWith("duration|"))
                    float.TryParse(line.Substring(9), NumberStyles.Float, CultureInfo.InvariantCulture, out duration);
                else if (line.StartsWith("s|") || line.StartsWith("v|")) break;
            }

            if (!haveRecorded || duration < 0f) return null;
            return recorded.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_" +
                   duration.ToString("F3", CultureInfo.InvariantCulture) + ".run";
        }
    }
}
