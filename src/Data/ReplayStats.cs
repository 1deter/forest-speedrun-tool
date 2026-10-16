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

        /// "Health 80   Stamina 100   Speed 5.2 m/s" at t; "" when the run
        /// holds none of the three. A stat the run lacks is left out.
        public static string Line(Attempt run, float t)
        {
            if (run == null) return "";
            string s = "";
            float v;
            if (run.TryStateAt(run.ChannelIndex("Health"), t, out v)) s += "Health " + Num(v, "0");
            if (run.TryStateAt(run.ChannelIndex("Stamina"), t, out v)) s += (s.Length > 0 ? "   " : "") + "Stamina " + Num(v, "0");
            if (SpeedAt(run.Samples, t, out v)) s += (s.Length > 0 ? "   " : "") + "Speed " + Num(v, "0.0") + " m/s";
            return s;
        }

        private static string Num(float v, string format)
        {
            return v.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
