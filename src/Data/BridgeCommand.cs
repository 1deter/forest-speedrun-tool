using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Parsing for the live test bridge (Modules/BridgeModule): command
    // lines, member paths and typed values. Pure, so it is tested; the
    // bridge's reflection lives in Game/ObjectProbe.
    //
    //   find "mutant_male_Dummy" 50 all
    //   get #-1234 PlayerStats._logs[0].name
    //   set player Transform.position 10,20.5,30
    // ------------------------------------------------------------------
    public static class BridgeCommand
    {
        /// Splits a line on spaces; "double quotes" keep spaces in one
        /// token. An empty list for a blank line or a # comment - a lone
        /// "#" is a comment, "#123" (a handle) is not.
        public static List<string> Tokenize(string line)
        {
            List<string> tokens = new List<string>();
            if (line == null) return tokens;

            string t = line.Trim();
            if (t.Length == 0 || t == "#" || t.StartsWith("# ") || t.StartsWith("//")) return tokens;

            StringBuilder cur = new StringBuilder();
            bool quoted = false;
            bool any = false;
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (c == '"') { quoted = !quoted; any = true; continue; }
                if (!quoted && (c == ' ' || c == '\t'))
                {
                    if (any) { tokens.Add(cur.ToString()); cur.Length = 0; any = false; }
                    continue;
                }
                cur.Append(c);
                any = true;
            }
            if (any) tokens.Add(cur.ToString());
            return tokens;
        }

        /// One step of a member path: a name and, when written as
        /// name[i], an index.
        public struct Step
        {
            public string Name;
            public int Index;   // -1: none
        }

        /// "a.b[2].c" -> a, b[2], c. False with the reason for a malformed
        /// path (empty step, bad index).
        public static bool TryParsePath(string path, List<Step> into, out string error)
        {
            into.Clear();
            error = null;
            if (string.IsNullOrEmpty(path)) { error = "empty path"; return false; }

            string[] parts = path.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                Step s;
                s.Index = -1;

                int open = p.IndexOf('[');
                if (open >= 0)
                {
                    if (!p.EndsWith("]")) { error = "'" + p + "': missing ]"; return false; }
                    string idx = p.Substring(open + 1, p.Length - open - 2);
                    int n;
                    if (!int.TryParse(idx, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < 0)
                    {
                        error = "'" + p + "': index must be a number from 0";
                        return false;
                    }
                    s.Index = n;
                    p = p.Substring(0, open);
                }

                if (p.Length == 0) { error = "empty step in '" + path + "'"; return false; }
                s.Name = p;
                into.Add(s);
            }
            return true;
        }

        public static bool TryParseFloat(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// "x,y,z" (spaces and parentheses allowed).
        public static bool TryParseVector3(string text, out Vector3 v)
        {
            v = Vector3.zero;
            float[] f;
            if (!TryParseFloats(text, 3, out f)) return false;
            v = new Vector3(f[0], f[1], f[2]);
            return true;
        }

        public static bool TryParseVector2(string text, out Vector2 v)
        {
            v = default(Vector2);
            float[] f;
            if (!TryParseFloats(text, 2, out f)) return false;
            v = new Vector2(f[0], f[1]);
            return true;
        }

        private static bool TryParseFloats(string text, int count, out float[] values)
        {
            values = null;
            if (text == null) return false;
            string[] parts = text.Trim().TrimStart('(').TrimEnd(')').Split(',');
            if (parts.Length != count) return false;

            float[] f = new float[count];
            for (int i = 0; i < count; i++)
                if (!TryParseFloat(parts[i].Trim(), out f[i])) return false;
            values = f;
            return true;
        }

        public static bool TryParseBool(string text, out bool value)
        {
            value = false;
            if (text == null) return false;
            switch (text.Trim().ToLowerInvariant())
            {
                case "true": case "1": case "on": case "yes": value = true; return true;
                case "false": case "0": case "off": case "no": value = false; return true;
            }
            return false;
        }

        /// Options written as key=value (e.g. max=500), removed from the
        /// token list so positional arguments stay in order.
        public static string TakeOption(List<string> tokens, string key)
        {
            string prefix = key + "=";
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    string v = tokens[i].Substring(prefix.Length);
                    tokens.RemoveAt(i);
                    return v;
                }
            }
            return null;
        }

        /// A bare flag word (e.g. "all"), removed when present.
        public static bool TakeFlag(List<string> tokens, string flag)
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                if (string.Equals(tokens[i], flag, StringComparison.OrdinalIgnoreCase))
                {
                    tokens.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// Text cut to `max` characters, one line - values are printed one
        /// per line and a newline inside would break the reply format.
        public static string OneLine(string text, int max)
        {
            if (text == null) return "";
            string s = text.Replace("\r", "").Replace('\n', ' ');
            return s.Length <= max ? s : s.Substring(0, max) + "...(" + s.Length + " chars)";
        }
    }
}
