using System.Collections.Generic;
using System.Globalization;

namespace ForestOverlay.Data
{
    /// The recorded player stats at a replay time (T-0386): health,
    /// stamina and speed from the run's 5 Hz state track and its position
    /// samples. Step values (the last snapshot at or before t), as
    /// Attempt.TryStateAt. Pure; the replay bar calls it from its throttled
    /// text refresh, never from OnGUI.
    public static class ReplayStats
    {
        /// The horizontal speed at t: the last position sample at or before t.
        public static bool SpeedAt(IList<RunSample> samples, float t, out float speed)
        {
            speed = 0f;
            if (samples == null || samples.Count == 0) return false;
            if (samples[0].T > t) { speed = samples[0].Speed; return true; }
            int lo = 0, hi = samples.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (samples[mid].T <= t) lo = mid; else hi = mid - 1;
            }
            speed = samples[lo].Speed;
            return true;
        }

        /// "Health 80   Stamina 100   Speed 5.2 m/s" at t. A run with no
        /// state track says so ("Health and stamina: not recorded in this
        /// run") before its speed; before the first snapshot only the speed
        /// shows. "" for no run or a run with nothing at all.
        public static string Line(Attempt run, float t)
        {
            if (run == null) return "";
            string s = "";
            float v;
            if (run.States.Count == 0) s = NoState;
            else
            {
                if (run.TryStateAt(run.ChannelIndex("Health"), t, out v)) s += "Health " + Num(v, "0");
                if (run.TryStateAt(run.ChannelIndex("Stamina"), t, out v)) s += (s.Length > 0 ? "   " : "") + "Stamina " + Num(v, "0");
            }
            if (SpeedAt(run.Samples, t, out v)) s += (s.Length > 0 ? "   " : "") + "Speed " + Num(v, "0.0") + " m/s";
            return s;
        }

        public const string NoState = "Health and stamina: not recorded in this run";

        /// Index of the last element at or before t (-1 before the first).
        public static int IndexAt(IList<RunSample> samples, float t)
        {
            int lo = -1, hi = samples.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (samples[mid].T <= t) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        public static int IndexAt(IList<StateSample> states, float t)
        {
            int lo = -1, hi = states.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (states[mid].T <= t) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        /// Line() cached: the string is rebuilt only when the run, the
        /// snapshot index or the sample index changed (the bar refreshes
        /// at 10 Hz, the data steps at 5 Hz).
        public sealed class Cache
        {
            private Attempt _run;
            private int _state = -2, _sample = -2;
            private string _text = "";
            public int Builds;

            public string Get(Attempt run, float t)
            {
                if (run == null) return "";
                int st = IndexAt(run.States, t);
                int sa = IndexAt(run.Samples, t);
                if (run == _run && st == _state && sa == _sample) return _text;
                _run = run; _state = st; _sample = sa;
                _text = Line(run, t);
                Builds++;
                return _text;
            }
        }

        private static string Num(float v, string format)
        {
            return v.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
