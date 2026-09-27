using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // One attempt's .run text <-> an Attempt. Pure (was inside AttemptStore
    // until v0.24.146): the format is shared - a .foseg carries .run files
    // verbatim, and the website will read them - so write and parse are
    // tested together.
    //
    //   anchor|<label>
    //   recorded|<utc iso>
    //   duration|<seconds>
    //   route|<fingerprint>                 which version of the route
    //   splits|<t1>|<t2>|...                each checkpoint's time (v0.24.146)
    //   runner|<id>|<name>                  who ran it (v0.24.146)
    //   channels|Health|Stamina|Energy|...
    //   s|<t>|<x>|<y>|<z>|<speed>          position, 30 Hz
    //   v|<t>|<v0>|<v1>|...                state,    5 Hz
    //   i|<t>|<item>:<count>|...           item counts that changed (v0.24.161)
    //
    // Numbers use InvariantCulture: a comma-decimal machine would otherwise
    // silently reject them.
    // ------------------------------------------------------------------
    public static class AttemptFormat
    {
        private const char NL = (char)10;

        public static string Write(Attempt attempt)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("anchor|").Append(attempt.AnchorLabel).Append(NL);
            sb.Append("recorded|").Append(attempt.RecordedUtc.ToString("o")).Append(NL);
            sb.Append("duration|").Append(F(attempt.Duration)).Append(NL);

            // Which version of the route this was run on. Without it,
            // moving a start zone would leave old times silently
            // competing with new ones under the same segment id.
            if (!string.IsNullOrEmpty(attempt.Route))
                sb.Append("route|").Append(attempt.Route).Append(NL);

            if (attempt.Splits != null && attempt.Splits.Length > 0)
            {
                sb.Append("splits");
                for (int i = 0; i < attempt.Splits.Length; i++) sb.Append('|').Append(F(attempt.Splits[i]));
                sb.Append(NL);
            }

            if (!string.IsNullOrEmpty(attempt.RunnerId))
                sb.Append("runner|").Append(Clean(attempt.RunnerId)).Append('|').Append(Clean(attempt.RunnerName)).Append(NL);

            if (attempt.Channels != null && attempt.Channels.Length > 0)
            {
                sb.Append("channels");
                for (int i = 0; i < attempt.Channels.Length; i++)
                    sb.Append('|').Append(attempt.Channels[i]);
                sb.Append(NL);
            }

            for (int i = 0; i < attempt.Samples.Count; i++)
            {
                RunSample s = attempt.Samples[i];
                sb.Append("s|").Append(F(s.T)).Append('|')
                  .Append(F(s.P.x)).Append('|').Append(F(s.P.y)).Append('|').Append(F(s.P.z))
                  .Append('|').Append(F(s.Speed)).Append(NL);
            }

            for (int i = 0; i < attempt.States.Count; i++)
            {
                StateSample v = attempt.States[i];
                sb.Append("v|").Append(F(v.T));
                if (v.Values != null)
                    for (int c = 0; c < v.Values.Length; c++)
                        sb.Append('|').Append(F(v.Values[c]));
                sb.Append(NL);
            }

            // Changes at the same time share a line.
            for (int i = 0; i < attempt.Items.Count; )
            {
                float t = attempt.Items[i].T;
                sb.Append("i|").Append(F(t));
                for (; i < attempt.Items.Count && attempt.Items[i].T == t; i++)
                    sb.Append('|').Append(Clean(attempt.Items[i].Name)).Append(':')
                      .Append(attempt.Items[i].Count.ToString(CultureInfo.InvariantCulture));
                sb.Append(NL);
            }

            return sb.ToString();
        }

        /// Null when the text has no position samples (not an attempt).
        public static Attempt Parse(string[] lines)
        {
            Attempt a = new Attempt();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                string[] p = line.Split('|');

                if (p[0] == "anchor" && p.Length > 1) a.AnchorLabel = p[1];
                else if (p[0] == "recorded" && p.Length > 1)
                {
                    DateTime dt;
                    if (DateTime.TryParse(p[1], CultureInfo.InvariantCulture,
                                          DateTimeStyles.RoundtripKind, out dt))
                        a.RecordedUtc = dt;
                }
                else if (p[0] == "duration" && p.Length > 1) a.Duration = P(p[1]);
                else if (p[0] == "route" && p.Length > 1) a.Route = p[1];
                else if (p[0] == "splits")
                {
                    float[] splits = new float[p.Length - 1];
                    for (int c = 1; c < p.Length; c++) splits[c - 1] = P(p[c]);
                    a.Splits = splits;
                }
                else if (p[0] == "runner" && p.Length > 1)
                {
                    a.RunnerId = p[1];
                    a.RunnerName = p.Length > 2 ? p[2] : "";
                }
                else if (p[0] == "channels" && p.Length > 1)
                {
                    string[] names = new string[p.Length - 1];
                    for (int c = 1; c < p.Length; c++) names[c - 1] = p[c];
                    a.Channels = names;
                }
                else if (p[0] == "v" && p.Length >= 2)
                {
                    StateSample v;
                    v.T = P(p[1]);
                    v.Values = new float[p.Length - 2];
                    for (int c = 2; c < p.Length; c++) v.Values[c - 2] = P(p[c]);
                    a.States.Add(v);
                }
                else if (p[0] == "i" && p.Length >= 3)
                {
                    float t = P(p[1]);
                    for (int c = 2; c < p.Length; c++)
                    {
                        // The last colon: the count never has one, a name might.
                        int at = p[c].LastIndexOf(':');
                        int n;
                        if (at <= 0 || !int.TryParse(p[c].Substring(at + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) continue;
                        ItemChange ic;
                        ic.T = t;
                        ic.Name = p[c].Substring(0, at);
                        ic.Count = n;
                        a.Items.Add(ic);
                    }
                }
                else if (p[0] == "s" && p.Length >= 6)
                {
                    RunSample s;
                    s.T = P(p[1]);
                    s.P = new Vector3(P(p[2]), P(p[3]), P(p[4]));
                    s.Speed = P(p[5]);
                    a.Samples.Add(s);
                }
            }

            if (a.Samples.Count == 0) return null;
            a.Completed = true;
            return a;
        }

        /// A name or id as one field: no separators or line breaks.
        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                sb.Append(c == '|' || c < ' ' ? ' ' : c);
            }
            return sb.ToString().Trim();
        }

        private static string F(float v)
        {
            return v.ToString("F3", CultureInfo.InvariantCulture);
        }

        private static float P(string s)
        {
            float v;
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            return v;
        }
    }
}
