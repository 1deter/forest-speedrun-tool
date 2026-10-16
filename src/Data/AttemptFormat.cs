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
    //   loads|<n>|<seconds>|<s1>|<s2>|...   load-removed time: the loads the
    //                                       timer counted, their seconds, and
    //                                       their seconds before each
    //                                       checkpoint (Data/LoadTimes; none
    //                                       listed = none before any).
    //                                       No line = an attempt from before
    //                                       loads were tracked (no loads).
    //   runner|<id>|<name>                  who ran it (v0.24.146; older
    //                                       ones get it on export / upload)
    //   plane|<x>|<y>|<z>|<yaw>             the save's plane crash site (v0.24.163)
    //   flags|<flag>|...                    labels on the run; today only
    //                                       "ModAPI Detected" (T-0295), the
    //                                       board marks the run. No line = none.
    //   channels|Health|Stamina|Energy|...
    //   s|<t>|<x>|<y>|<z>|<speed>          position, 30 Hz
    //   v|<t>|<v0>|<v1>|...                state,    5 Hz
    //   i|<t>|<item>:<count>|...           item counts that changed (v0.24.161)
    //   e|<t>|<kind>|<x>|<y>|<z>|<detail>  what the runner did (replays;
    //                                       kinds from Data/RunAudit)
    //   b|<t>|<state>|<kind>|<x>|<y>|<z>|<rx>|<ry>|<rz>|<cx>|<cy>|<cz>|<sx>|<sy>|<sz>
    //                                       a structure placed / built: its
    //                                       place, rotation and local box
    //   l|<t>|<yaw>|<pitch>|<eye>          where the runner looked, 30 Hz
    //                                       (the replay camera; eye = the
    //                                       camera's height above s's point)
    //   anim|<layers>|<float name>|...      the arms (T-0387; Data/AnimTrack): header
    //   ak|<t>|<layer>|<hash>|<time>|<rate>|<weight>|<wrate>[|<next>|<ntime>|<nrate>|<p>|<prate>]
    //                                       a layer's state from t on (changes only:
    //                                       the playback advances it by the rates);
    //                                       next.. = a crossfade; "ak|<t>|<layer>|-" = off
    //   af|<t>|<index>=<value>:<rate>|...   float parameters from t on
    //   ay|<t>|<yaw>|<rate>                 the body's yaw from t on
    //   ah|<t>|<id>|<id>|...                the equipment slot ids when they changed
    //
    // Older readers skip lines they do not know, so e / b / l lines never
    // break an older plugin or the site.
    //
    // Numbers use InvariantCulture: a comma-decimal machine would otherwise
    // silently reject them.
    // ------------------------------------------------------------------
    public static class AttemptFormat
    {
        private const char NL = (char)10;

        /// The one flag a .run carries today (the same words as the run mode report's).
        public const string ModApiFlag = "ModAPI Detected";

        /// T-0308: other mods; the same prefix as Data/ExternalMods (this file
        /// is linked alone by the site).
        public const string ModsFlagPrefix = "Mods Detected: ";

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

            // Load-removed time: written for every attempt that tracked
            // loads, none included, so "no loads" reads apart from "older".
            if (attempt.HasLoads)
            {
                sb.Append("loads|").Append(Math.Max(0, attempt.Loads).ToString(CultureInfo.InvariantCulture))
                  .Append('|').Append(F(attempt.LoadTime));
                if (attempt.Loads > 0 && attempt.SplitLoads != null)
                    for (int i = 0; i < attempt.SplitLoads.Length; i++) sb.Append('|').Append(F(attempt.SplitLoads[i]));
                sb.Append(NL);
            }

            if (!string.IsNullOrEmpty(attempt.RunnerId))
                sb.Append("runner|").Append(Clean(attempt.RunnerId)).Append('|').Append(Clean(attempt.RunnerName)).Append(NL);

            bool mods = !string.IsNullOrEmpty(attempt.Mods);
            if (attempt.ModApi || mods)
            {
                sb.Append("flags");
                if (attempt.ModApi) sb.Append('|').Append(ModApiFlag);
                if (mods) sb.Append('|').Append(ModsFlagPrefix).Append(Clean(attempt.Mods));
                sb.Append(NL);
            }

            if (attempt.HasPlane)
                sb.Append("plane|").Append(F(attempt.Plane.x)).Append('|').Append(F(attempt.Plane.y)).Append('|')
                  .Append(F(attempt.Plane.z)).Append('|').Append(F(attempt.PlaneYaw)).Append(NL);

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

            for (int i = 0; i < attempt.Events.Count; i++)
            {
                RunEvent e = attempt.Events[i];
                sb.Append("e|").Append(F(e.T)).Append('|').Append(Clean(e.Kind)).Append('|')
                  .Append(F(e.P.x)).Append('|').Append(F(e.P.y)).Append('|').Append(F(e.P.z)).Append('|')
                  .Append(Clean(e.Detail)).Append(NL);
            }

            for (int i = 0; i < attempt.Buildings.Count; i++)
            {
                RunBuilding b = attempt.Buildings[i];
                sb.Append("b|").Append(F(b.T)).Append('|').Append(Clean(b.State)).Append('|').Append(Clean(b.Kind));
                V(sb, b.P); V(sb, b.Euler); V(sb, b.Center); V(sb, b.Size);
                sb.Append(NL);
            }

            // Two decimals: a hundredth of a degree / centimetre is plenty,
            // and this is a line per position sample.
            for (int i = 0; i < attempt.Looks.Count; i++)
            {
                LookSample l = attempt.Looks[i];
                sb.Append("l|").Append(F(l.T)).Append('|').Append(F2(l.Yaw)).Append('|')
                  .Append(F2(l.Pitch)).Append('|').Append(F2(l.Eye)).Append(NL);
            }

            AnimTrack an = attempt.Anim;
            if (an != null && an.HasPose)
            {
                sb.Append("anim|").Append(an.Layers.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < an.Floats.Length; i++) sb.Append('|').Append(Clean(an.Floats[i]));
                sb.Append(NL);
                for (int i = 0; i < an.LayerKeys.Count; i++)
                {
                    AnimLayerKey k = an.LayerKeys[i];
                    AnimLayerState st = k.S;
                    sb.Append("ak|").Append(F(k.T)).Append('|').Append(k.Layer.ToString(CultureInfo.InvariantCulture)).Append('|');
                    if (st.Hash == 0) { sb.Append('-').Append(NL); continue; }
                    sb.Append(st.Hash.ToString(CultureInfo.InvariantCulture)).Append('|').Append(F(st.Time)).Append('|')
                      .Append(F4(st.Rate)).Append('|').Append(F(st.Weight)).Append('|').Append(F(st.WeightRate));
                    if (st.Next != 0)
                        sb.Append('|').Append(st.Next.ToString(CultureInfo.InvariantCulture)).Append('|').Append(F(st.NextTime))
                          .Append('|').Append(F4(st.NextRate)).Append('|').Append(F(st.P)).Append('|').Append(F(st.PRate));
                    sb.Append(NL);
                }
                for (int i = 0; i < an.FloatKeys.Count; )
                {
                    float t = an.FloatKeys[i].T;
                    sb.Append("af|").Append(F(t));
                    for (; i < an.FloatKeys.Count && an.FloatKeys[i].T == t; i++)
                        sb.Append('|').Append(an.FloatKeys[i].Index.ToString(CultureInfo.InvariantCulture))
                          .Append('=').Append(F(an.FloatKeys[i].Value)).Append(':').Append(F(an.FloatKeys[i].Rate));
                    sb.Append(NL);
                }
                for (int i = 0; i < an.YawKeys.Count; i++)
                    sb.Append("ay|").Append(F(an.YawKeys[i].T)).Append('|').Append(F1(an.YawKeys[i].Yaw))
                      .Append('|').Append(F1(an.YawKeys[i].Rate)).Append(NL);
                for (int i = 0; i < an.HeldChanges.Count; i++)
                {
                    sb.Append("ah|").Append(F(an.HeldChanges[i].T));
                    for (int j = 0; j < an.HeldChanges[i].Ids.Length; j++)
                        sb.Append('|').Append(an.HeldChanges[i].Ids[j].ToString(CultureInfo.InvariantCulture));
                    sb.Append(NL);
                }
            }

            return sb.ToString();
        }

        private static void V(StringBuilder sb, Vector3 v)
        {
            sb.Append('|').Append(F(v.x)).Append('|').Append(F(v.y)).Append('|').Append(F(v.z));
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
                else if (p[0] == "loads" && p.Length >= 3)
                {
                    int n;
                    if (int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n >= 0)
                    {
                        a.HasLoads = true;
                        a.Loads = n;
                        a.LoadTime = Math.Max(0f, P(p[2]));
                        float[] at = new float[p.Length - 3];
                        for (int c = 3; c < p.Length; c++) at[c - 3] = Math.Max(0f, P(p[c]));
                        a.SplitLoads = at;
                    }
                }
                else if (p[0] == "runner" && p.Length > 1)
                {
                    a.RunnerId = p[1];
                    a.RunnerName = p.Length > 2 ? p[2] : "";
                }
                else if (p[0] == "flags")
                {
                    for (int c = 1; c < p.Length; c++)
                    {
                        if (p[c] == ModApiFlag) a.ModApi = true;
                        else if (p[c].StartsWith(ModsFlagPrefix, StringComparison.Ordinal)) a.Mods = p[c].Substring(ModsFlagPrefix.Length);
                    }
                }
                else if (p[0] == "plane" && p.Length >= 5)
                {
                    a.HasPlane = true;
                    a.Plane = new Vector3(P(p[1]), P(p[2]), P(p[3]));
                    a.PlaneYaw = P(p[4]);
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
                else if (p[0] == "e" && p.Length >= 6)
                {
                    RunEvent e;
                    e.T = P(p[1]);
                    e.Kind = p[2];
                    e.P = new Vector3(P(p[3]), P(p[4]), P(p[5]));
                    e.Detail = p.Length > 6 ? p[6] : "";
                    if (e.Kind.Length > 0) a.Events.Add(e);
                }
                else if (p[0] == "b" && p.Length >= 16)
                {
                    RunBuilding b;
                    b.T = P(p[1]);
                    b.State = p[2];
                    b.Kind = p[3];
                    b.P = new Vector3(P(p[4]), P(p[5]), P(p[6]));
                    b.Euler = new Vector3(P(p[7]), P(p[8]), P(p[9]));
                    b.Center = new Vector3(P(p[10]), P(p[11]), P(p[12]));
                    b.Size = new Vector3(P(p[13]), P(p[14]), P(p[15]));
                    a.Buildings.Add(b);
                }
                else if (p[0] == "l" && p.Length >= 5)
                {
                    LookSample l;
                    l.T = P(p[1]);
                    l.Yaw = P(p[2]);
                    l.Pitch = P(p[3]);
                    l.Eye = P(p[4]);
                    a.Looks.Add(l);
                }
                else if (p[0] == "anim" && p.Length >= 2)
                {
                    int layers;
                    if (int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out layers) && layers > 0 && layers < 64)
                    {
                        string[] names = new string[p.Length - 2];
                        for (int c = 2; c < p.Length; c++) names[c - 2] = p[c];
                        a.Anim = new AnimTrack(layers, names);
                    }
                }
                else if (p[0] == "ak" && p.Length >= 4 && a.Anim != null)
                {
                    int layer;
                    if (int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out layer) && layer >= 0 && layer < a.Anim.Layers)
                    {
                        AnimLayerKey k = new AnimLayerKey();
                        k.T = P(p[1]);
                        k.Layer = layer;
                        int h;
                        if (p[3] != "-" && p.Length >= 8 && int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out h))
                        {
                            k.S.Hash = h;
                            k.S.Time = P(p[4]); k.S.Rate = P(p[5]); k.S.Weight = P(p[6]); k.S.WeightRate = P(p[7]);
                            int nx;
                            if (p.Length >= 13 && int.TryParse(p[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out nx))
                            {
                                k.S.Next = nx;
                                k.S.NextTime = P(p[9]); k.S.NextRate = P(p[10]); k.S.P = P(p[11]); k.S.PRate = P(p[12]);
                            }
                        }
                        a.Anim.AddLayerKey(k);
                    }
                }
                else if (p[0] == "af" && p.Length >= 3 && a.Anim != null)
                {
                    float t = P(p[1]);
                    for (int c = 2; c < p.Length; c++)
                    {
                        int eq = p[c].IndexOf('=');
                        int idx;
                        if (eq <= 0 || !int.TryParse(p[c].Substring(0, eq), NumberStyles.Integer, CultureInfo.InvariantCulture, out idx)) continue;
                        if (idx < 0 || idx >= a.Anim.Floats.Length) continue;
                        string v = p[c].Substring(eq + 1);
                        int colon = v.IndexOf(':');
                        AnimFloatKey fk;
                        fk.T = t; fk.Index = idx;
                        fk.Value = P(colon < 0 ? v : v.Substring(0, colon));
                        fk.Rate = colon < 0 ? 0f : P(v.Substring(colon + 1));
                        a.Anim.AddFloatKey(fk);
                    }
                }
                else if (p[0] == "ay" && p.Length >= 3 && a.Anim != null)
                {
                    AnimYawKey y;
                    y.T = P(p[1]); y.Yaw = P(p[2]); y.Rate = p.Length > 3 ? P(p[3]) : 0f;
                    a.Anim.AddYawKey(y);
                }
                else if (p[0] == "ah" && p.Length >= 2 && a.Anim != null)
                {
                    int[] ids = new int[p.Length - 2];
                    for (int c = 2; c < p.Length; c++)
                    {
                        int id;
                        int.TryParse(p[c], NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
                        ids[c - 2] = id;
                    }
                    a.Anim.AddHeld(P(p[1]), ids);
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

        /// The .run text with a runner line: unchanged when it has one
        /// already (anywhere - Parse reads any order) or `id` is empty,
        /// else `runner|<id>|<name>` goes into the header, before the
        /// first channels / sample line, in the text's own line endings.
        /// Attempts recorded before v0.24.146 have no runner line; by
        /// AttemptOwners' rule they are their holder's own, so an export
        /// or upload names that runner (they read as the importer's own
        /// otherwise). Never the plain Steam id: `id` is the hashed one.
        public static string WithRunner(string runText, string id, string name)
        {
            string cleanId = Clean(id);
            if (string.IsNullOrEmpty(runText) || cleanId.Length == 0) return runText;

            int insert = -1;
            int at = 0;
            while (at < runText.Length)
            {
                int end = runText.IndexOf('\n', at);
                int next = end < 0 ? runText.Length : end + 1;
                int s = at;
                while (s < next && (runText[s] == ' ' || runText[s] == '\t' || runText[s] == '\uFEFF')) s++;
                if (StartsAt(runText, s, "runner|")) return runText;
                if (insert < 0 && (StartsAt(runText, s, "channels") || StartsAt(runText, s, "s|") ||
                                   StartsAt(runText, s, "v|") || StartsAt(runText, s, "i|") ||
                                   StartsAt(runText, s, "e|") || StartsAt(runText, s, "b|") ||
                                   StartsAt(runText, s, "l|") || StartsAt(runText, s, "anim") ||
                                   StartsAt(runText, s, "ak|") || StartsAt(runText, s, "af|") ||
                                   StartsAt(runText, s, "ay|") || StartsAt(runText, s, "ah|")))
                    insert = at;
                at = next;
            }

            string nl = runText.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            string line = "runner|" + cleanId + "|" + Clean(name) + nl;
            if (insert >= 0) return runText.Substring(0, insert) + line + runText.Substring(insert);
            return runText.EndsWith("\n") ? runText + line : runText + nl + line;
        }

        private static bool StartsAt(string text, int at, string prefix)
        {
            return at + prefix.Length <= text.Length && string.CompareOrdinal(text, at, prefix, 0, prefix.Length) == 0;
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

        private static string F4(float v)
        {
            return v.ToString("F4", CultureInfo.InvariantCulture);
        }

        private static string F1(float v)
        {
            return v.ToString("F1", CultureInfo.InvariantCulture);
        }

        private static string F2(float v)
        {
            return v.ToString("F2", CultureInfo.InvariantCulture);
        }

        private static float P(string s)
        {
            float v;
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            return v;
        }
    }
}
