using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Segment block text <-> objects.
    //
    // Pure, and separate from SegmentLibrary for the same reason
    // TriggerParser is: the library does file IO and BepInEx logging and
    // cannot be linked into the tests - but THIS is the code that can
    // silently corrupt a shared route file. The editor writes segments
    // back out as text, so write and parse have to agree exactly, and
    // that agreement is worth a test rather than a hope.
    // ------------------------------------------------------------------
    public static class SegmentFormat
    {
        /// Applies one `key = value` line. Returns null on success, or a
        /// human-readable reason so the caller decides how to report it.
        public static string ApplyKey(Segment s, string key, string value)
        {
            switch (key)
            {
                case "id": s.Id = value; return null;
                case "name": s.Name = value; return null;
                case "category": s.Category = value.Length > 0 ? value : "Segments"; return null;
                case "notes": s.Notes = value; return null;

                case "spawn":
                    {
                        string[] p = TriggerParser.Split(value);
                        float x, y, z;
                        if (p.Length < 3 || !TriggerParser.F(p[0], out x) ||
                            !TriggerParser.F(p[1], out y) || !TriggerParser.F(p[2], out z))
                            return "bad spawn: " + value;

                        s.SpawnPosition = new Vector3(x, y, z);
                        s.HasSpawn = true;
                        if (p.Length > 3) TriggerParser.F(p[3], out s.SpawnYaw);
                        if (p.Length > 4) TriggerParser.F(p[4], out s.SpawnPitch);
                        return null;
                    }

                case "start":
                    return TriggerParser.Parse(value, out s.Start) ? null : "bad start: " + value;

                case "end":
                    return TriggerParser.Parse(value, out s.End) ? null : "bad end: " + value;

                case "check":
                case "checkpoint":
                    {
                        Trigger t;
                        if (!TriggerParser.Parse(value, out t)) return "bad checkpoint: " + value;
                        s.Checkpoints.Add(t);
                        return null;
                    }

                default:
                    return "unknown key: " + key;
            }
        }

        public static void WriteSegment(StringBuilder sb, Segment s, string nl)
        {
            sb.Append(nl).Append("[segment]").Append(nl);
            sb.Append("id       = ").Append(s.Id).Append(nl);
            sb.Append("name     = ").Append(s.Name).Append(nl);
            sb.Append("category = ").Append(s.Category).Append(nl);

            if (s.HasSpawn)
            {
                sb.Append("spawn    = ")
                  .Append(TriggerParser.Num(s.SpawnPosition.x)).Append(' ')
                  .Append(TriggerParser.Num(s.SpawnPosition.y)).Append(' ')
                  .Append(TriggerParser.Num(s.SpawnPosition.z)).Append(' ')
                  .Append(TriggerParser.Num(s.SpawnYaw)).Append(' ')
                  .Append(TriggerParser.Num(s.SpawnPitch)).Append(nl);
            }

            // Unset triggers are OMITTED, not written.
            //
            // TriggerParser.Write falls through to "manual" for an unset
            // trigger, so emitting them unconditionally turned every
            // spawn-only entry into a timed segment with manual start and
            // end the moment it was saved and reloaded. A spot must stay a
            // spot across a round trip.
            if (s.Start.IsSet) sb.Append("start    = ").Append(TriggerParser.Write(s.Start)).Append(nl);

            for (int i = 0; i < s.Checkpoints.Count; i++)
            {
                if (!s.Checkpoints[i].IsSet) continue;
                sb.Append("check    = ").Append(TriggerParser.Write(s.Checkpoints[i])).Append(nl);
            }

            if (s.End.IsSet) sb.Append("end      = ").Append(TriggerParser.Write(s.End)).Append(nl);

            if (!string.IsNullOrEmpty(s.Notes)) sb.Append("notes    = ").Append(s.Notes).Append(nl);
        }

        /// Parses whole-file text into segments. Malformed lines are
        /// reported through `onWarning` rather than thrown, so one bad line
        /// cannot lose an entire shared route set.
        public static List<Segment> ParseAll(string[] lines, Action<int, string> onWarning)
        {
            List<Segment> found = new List<Segment>();
            Segment current = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                if (line[0] == '[')
                {
                    if (current != null) found.Add(current);
                    current = new Segment();
                    continue;
                }

                if (current == null) continue;   // stray line before any header

                int eq = line.IndexOf('=');
                if (eq < 0)
                {
                    if (onWarning != null) onWarning(i + 1, "ignored (no separator): " + line);
                    continue;
                }

                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string value = line.Substring(eq + 1).Trim();

                string error = ApplyKey(current, key, value);
                if (error != null && onWarning != null) onWarning(i + 1, error);
            }

            if (current != null) found.Add(current);
            return found;
        }
    }
}
