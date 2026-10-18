using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ForestOverlay.BridgeMcp
{
    // ------------------------------------------------------------------
    // T-0395: a GPU driver fault (nvlddmkm, "Restarting TDR occurred") hangs
    // the game's render, so a screenshot times out and a retry only piles
    // up. `status` and a timed-out screenshot name any such System-log event
    // since the game launched. Parse / Line are pure (tested on a real event
    // payload); Check asks wevtutil.
    // ------------------------------------------------------------------
    internal static class GpuFault
    {
        public sealed class Fault
        {
            public DateTime Utc;
            public int EventId;
            public string Text;
        }

        private static readonly Regex EventRx = new Regex("<Event[ >].*?</Event>", RegexOptions.Singleline);
        private static readonly Regex IdRx = new Regex("<EventID[^>]*>(\\d+)</EventID>");
        private static readonly Regex TimeRx = new Regex("<TimeCreated SystemTime=.([^'\"]+)");
        private static readonly Regex DataRx = new Regex("<Data>([^<]*)</Data>");

        /// The nvlddmkm events in `wevtutil qe /f:xml` output (any number of
        /// Event elements, concatenated).
        public static List<Fault> Parse(string xml)
        {
            List<Fault> list = new List<Fault>();
            if (string.IsNullOrEmpty(xml)) return list;
            foreach (Match m in EventRx.Matches(xml))
            {
                string e = m.Value;
                if (e.IndexOf("Provider Name='nvlddmkm'", StringComparison.OrdinalIgnoreCase) < 0 &&
                    e.IndexOf("Provider Name=\"nvlddmkm\"", StringComparison.OrdinalIgnoreCase) < 0) continue;
                Fault f = new Fault();
                Match id = IdRx.Match(e);
                if (id.Success) f.EventId = int.Parse(id.Groups[1].Value, CultureInfo.InvariantCulture);
                Match t = TimeRx.Match(e);
                if (t.Success) DateTime.TryParse(t.Groups[1].Value, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out f.Utc);
                // The readable data ("Restarting TDR occurred ..."); the device name and Binary blob are skipped.
                List<string> parts = new List<string>();
                foreach (Match d in DataRx.Matches(e))
                {
                    string s = System.Net.WebUtility.HtmlDecode(d.Groups[1].Value).Trim();
                    if (s.Length > 0 && !s.StartsWith("\\Device\\", StringComparison.Ordinal)) parts.Add(s);
                }
                f.Text = string.Join(" ", parts.ToArray());
                list.Add(f);
            }
            return list;
        }

        /// The one line for a tool reply; null when there is no fault.
        public static string Line(List<Fault> faults)
        {
            if (faults == null || faults.Count == 0) return null;
            Fault last = faults[faults.Count - 1];
            string what = string.IsNullOrEmpty(last.Text) ? "nvlddmkm event " + last.EventId : last.Text;
            return "GPU DRIVER FAULT: " + faults.Count + " nvlddmkm event(s) in the System log since the game launched (last " +
                   last.Utc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) + ": " + what +
                   "). The game's render is likely hung - stop driving it and close the game.";
        }

        /// Asks the System log for nvlddmkm events since `sinceUtc`; null
        /// when none, or when the log cannot be read.
        public static string Check(DateTime sinceUtc)
        {
            try
            {
                string stamp = sinceUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.000'Z'", CultureInfo.InvariantCulture);
                ProcessStartInfo psi = new ProcessStartInfo("wevtutil",
                    "qe System \"/q:*[System[Provider[@Name='nvlddmkm'] and TimeCreated[@SystemTime>='" + stamp + "']]]\" /f:xml /c:20")
                {
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };
                using (Process p = ChildProcess.Start(psi))
                {
                    if (p == null) return null;
                    string xml = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(8000)) { try { p.Kill(); } catch (Exception) { } return null; }
                    return Line(Parse(xml));
                }
            }
            catch (Exception) { return null; }
        }
    }
}
