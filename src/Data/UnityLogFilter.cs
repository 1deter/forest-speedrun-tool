using System;
using System.Collections.Generic;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Which of Unity's error messages reach our log (T-0276, Core/
    // UnityErrorLog). A broken object can log the same error every frame,
    // so:
    //
    //   - a message counts as the same when it differs only in its digits
    //     (texture ids, instance ids, coordinates);
    //   - the same message is written once, then at most once every
    //     RepeatSeconds, saying how many times it came in between;
    //   - at most PerMinute lines a minute in all; the next line written
    //     after the minute says how many were held back.
    //
    // Pure: the caller passes the time; not thread-safe (the caller locks).
    // ------------------------------------------------------------------
    public sealed class UnityLogFilter
    {
        public const double RepeatSeconds = 60.0;
        public const int PerMinute = 30;
        public const int MaxText = 400;
        private const int MaxKeys = 512;

        private sealed class Seen
        {
            public double LastWritten;
            public int Repeats;
        }

        private readonly Dictionary<string, Seen> _seen = new Dictionary<string, Seen>();
        private double _windowStart = double.MinValue;
        private int _inWindow;
        private int _heldBack;

        /// The line to write for this message, or null to drop it.
        /// type: Unity's LogType name (Error, Assert, Exception).
        /// stackTrace: the first line of it is added for an exception.
        public string Next(string type, string condition, string stackTrace, bool offMainThread, double now)
        {
            if (condition == null) condition = "";
            string key = Key(type, condition);

            Seen seen;
            bool known = _seen.TryGetValue(key, out seen);
            if (known && now - seen.LastWritten < RepeatSeconds)
            {
                seen.Repeats++;
                return null;
            }

            if (now - _windowStart >= 60.0)
            {
                _windowStart = now;
                _inWindow = 0;
            }
            if (_inWindow >= PerMinute)
            {
                _heldBack++;
                if (known) seen.Repeats++;
                return null;
            }
            _inWindow++;

            if (!known)
            {
                if (_seen.Count >= MaxKeys) _seen.Clear();
                seen = new Seen();
                _seen[key] = seen;
            }
            int repeats = seen.Repeats;
            seen.Repeats = 0;
            seen.LastWritten = now;

            StringBuilder sb = new StringBuilder();
            sb.Append("Unity: [").Append(type).Append("] ").Append(OneLine(condition, MaxText));
            if (type == "Exception")
            {
                string at = FirstLine(stackTrace);
                if (at.Length > 0) sb.Append(" | at ").Append(OneLine(at, 200));
            }
            if (offMainThread) sb.Append(" (off the main thread)");
            if (repeats > 0) sb.Append(" - ").Append(repeats).Append(repeats == 1 ? " more time" : " more times").Append(" since the last line");
            if (_heldBack > 0)
            {
                sb.Append(" - ").Append(_heldBack).Append(" other ").Append(_heldBack == 1 ? "line" : "lines")
                  .Append(" held back (over ").Append(PerMinute).Append(" a minute)");
                _heldBack = 0;
            }
            return sb.ToString();
        }

        /// The same message whatever its digits.
        public static string Key(string type, string condition)
        {
            StringBuilder sb = new StringBuilder(type.Length + 1 + Math.Min(condition.Length, 200));
            sb.Append(type).Append('|');
            bool inDigits = false;
            for (int i = 0; i < condition.Length && sb.Length < 220; i++)
            {
                char c = condition[i];
                if (c >= '0' && c <= '9')
                {
                    if (!inDigits) sb.Append('#');
                    inDigits = true;
                    continue;
                }
                inDigits = false;
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.TrimStart();
            int nl = s.IndexOfAny(new[] { '\r', '\n' });
            return nl < 0 ? s : s.Substring(0, nl);
        }

        // Newlines become " / " so one message stays one log line.
        private static string OneLine(string s, int max)
        {
            string t = s.Trim().Replace("\r\n", " / ").Replace('\n', ' ').Replace('\r', ' ');
            return t.Length > max ? t.Substring(0, max) + "..." : t;
        }
    }
}
