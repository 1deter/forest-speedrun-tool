using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Persists attempts so a best time survives a restart, and so saved
    // run lines can be raced later.
    //
    // Same reasoning as LocationLibrary: a line-oriented text format,
    // because these files are meant to be shareable and diffable, and
    // net35 has no framework JSON. One file per attempt, grouped in a
    // folder per anchor, so a folder is a "track" and can be zipped and
    // sent to someone else as-is.
    //
    //   anchor|<label>
    //   recorded|<utc iso>
    //   duration|<seconds>
    //   route|<fingerprint>                 which version of the route
    //   channels|Health|Stamina|Energy|...
    //   s|<t>|<x>|<y>|<z>|<speed>          position, 30 Hz
    //   v|<t>|<v0>|<v1>|...                state,    5 Hz
    //
    // Coordinates use InvariantCulture for the same reason location files
    // do: a comma-decimal machine would otherwise silently reject them.
    // ------------------------------------------------------------------
    public sealed class AttemptStore
    {
        // Char code rather than a backslash-n escape: tooling that
        // rewrites this file has mangled those literals more than once.
        private static readonly char NL = (char)10;

        private readonly ManualLogSource _log;
        private readonly string _root;

        public string Root { get { return _root; } }

        public AttemptStore(ManualLogSource log, string configDirectory)
        {
            _log = log;
            _root = Path.Combine(configDirectory, "runs");
        }

        private string FolderFor(string anchorLabel)
        {
            return Path.Combine(_root, Sanitise(anchorLabel));
        }

        public bool Save(Attempt attempt)
        {
            if (attempt == null || attempt.Samples.Count == 0) return false;

            try
            {
                string dir = FolderFor(attempt.AnchorLabel);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string name = attempt.RecordedUtc.ToString("yyyyMMdd_HHmmss") + "_" +
                              attempt.Duration.ToString("F3", CultureInfo.InvariantCulture) + ".run";

                StringBuilder sb = new StringBuilder();
                sb.Append("anchor|").Append(attempt.AnchorLabel).Append('\n');
                sb.Append("recorded|").Append(attempt.RecordedUtc.ToString("o")).Append('\n');
                sb.Append("duration|").Append(F(attempt.Duration)).Append(NL);

                // Which version of the route this was run on. Without it,
                // moving a start zone would leave old times silently
                // competing with new ones under the same segment id.
                if (!string.IsNullOrEmpty(attempt.Route))
                    sb.Append("route|").Append(attempt.Route).Append(NL);

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
                      .Append('|').Append(F(s.Speed)).Append('\n');
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

                File.WriteAllText(Path.Combine(dir, name), sb.ToString(), Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Could not save attempt: " + ex.Message);
                return false;
            }
        }

        public List<Attempt> LoadAll(string anchorLabel)
        {
            List<Attempt> result = new List<Attempt>();

            try
            {
                string dir = FolderFor(anchorLabel);
                if (!Directory.Exists(dir)) return result;

                string[] files = Directory.GetFiles(dir, "*.run");
                for (int i = 0; i < files.Length; i++)
                {
                    Attempt a = Load(files[i]);
                    if (a != null) result.Add(a);
                }

                result.Sort(CompareByTime);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Could not list attempts: " + ex.Message);
            }

            return result;
        }

        /// How many saved attempts count for `route` - what changing the
        /// route would retire. Reads only each file's header, not its
        /// samples. No route line counts as current, as in LoadAll's caller.
        public int CountOnRoute(string anchorLabel, string route)
        {
            int n = 0;
            try
            {
                string dir = FolderFor(anchorLabel);
                if (!Directory.Exists(dir)) return 0;

                string[] files = Directory.GetFiles(dir, "*.run");
                for (int i = 0; i < files.Length; i++)
                {
                    string r = RouteOf(files[i]);
                    if (r.Length == 0 || r == route) n++;
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning("Could not count attempts: " + ex.Message);
            }
            return n;
        }

        private static string RouteOf(string path)
        {
            using (StreamReader r = new StreamReader(path, Encoding.UTF8))
            {
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    if (line.StartsWith("route|")) return line.Substring(6).Trim();
                    if (line.StartsWith("s|") || line.StartsWith("v|")) break;   // header over
                }
            }
            return "";
        }

        private static int CompareByTime(Attempt a, Attempt b)
        {
            return a.RecordedUtc.CompareTo(b.RecordedUtc);
        }

        private Attempt Load(string path)
        {
            try
            {
                string[] lines = File.ReadAllLines(path);
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
            catch (Exception ex)
            {
                _log.LogWarning("Could not read " + Path.GetFileName(path) + ": " + ex.Message);
                return null;
            }
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

        // Anchor labels become folder names, and they come from
        // user-authored location files, so they can contain anything.
        private static string Sanitise(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unnamed";

            char[] bad = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder(s.Length);

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool ok = true;
                for (int b = 0; b < bad.Length; b++)
                    if (c == bad[b]) { ok = false; break; }

                sb.Append(ok ? c : '_');
            }

            string cleaned = sb.ToString().Trim();
            return cleaned.Length == 0 ? "unnamed" : cleaned;
        }
    }
}
