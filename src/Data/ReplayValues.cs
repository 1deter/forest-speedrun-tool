using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Which recorded values the replay bar shows (T-0344; author,
    // 2026-10-19: the runner's choice, default the site's Basic list).
    //
    // Basic is the website's attempt page preset (site Runs.ShownChannels
    // and app.js SHOWN, without the old item channels), the speed first as
    // there; any other channel the run recorded can be ticked too. Labels
    // and formats follow the site's: Fullness / Thirst are fractions shown
    // as %, Battery %, body temperature in degrees C, Cold / IsLit yes /
    // no, a whole number with no decimals, else one.
    //
    // A value is the run's last 5 Hz snapshot at or before the time (step,
    // as Attempt.TryStateAt), the speed its last position sample. Pure;
    // the bar asks the Cache from its throttled text refresh, never OnGUI.
    // ------------------------------------------------------------------
    public static class ReplayValues
    {
        public const string Speed = "Speed";

        /// The default list, in the order shown.
        public static readonly string[] Basic =
        {
            Speed, "Health", "Stamina", "Energy", "Fullness", "Thirst", "Armor", "ColdArmor",
            "BatteryCharge", "BodyTemp", "Stealth", "Cold", "IsLit",
        };

        public static string BasicConfig { get { return Join(Basic); } }

        public const string NotRecorded = "Values: not recorded in this run";
        public const string NoneChosen = "";

        /// The words a runner reads for a value.
        public static string Label(string name)
        {
            switch (name)
            {
                case "Armor": return "Armour";
                case "ColdArmor": return "Cold armour";
                case "BatteryCharge": return "Battery";
                case "BodyTemp": return "Body temp";
                case "IsLit": return "Light on";
                default: return name;
            }
        }

        public static bool IsBasic(string name)
        {
            for (int i = 0; i < Basic.Length; i++)
                if (string.Equals(Basic[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// One value as the site shows it.
        public static string Format(string name, float v)
        {
            switch (name)
            {
                case Speed: return Num(v, "0.0") + " m/s";
                case "Cold":
                case "IsLit": return v != 0f ? "yes" : "no";
                case "Fullness":
                case "Thirst": return Plain(v * 100f) + "%";
                case "BatteryCharge": return Plain(v) + "%";
                case "BodyTemp": return Num(v, "0.0") + " \u00b0C";
                default: return Plain(v);
            }
        }

        private static string Plain(float x)
        {
            if (Math.Abs(x) >= 1e5f) return x.ToString("0.00e+0", CultureInfo.InvariantCulture);
            return Num(x, x == (float)Math.Round(x) ? "0" : "0.0");
        }

        private static string Num(float v, string format)
        {
            return v.ToString(format, CultureInfo.InvariantCulture);
        }

        // --- the setting ------------------------------------------------------------

        /// The setting's comma list as names (blank entries and repeats dropped).
        public static List<string> Parse(string config)
        {
            List<string> names = new List<string>();
            if (string.IsNullOrEmpty(config)) return names;
            string[] parts = config.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string n = parts[i].Trim();
                if (n.Length > 0 && !names.Contains(n)) names.Add(n);
            }
            return names;
        }

        public static string Join(IList<string> names)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(names[i]);
            }
            return sb.ToString();
        }

        /// What can be picked: Basic in its order, then every other channel
        /// in `channels` (the player's recorded state) in its order.
        public static List<string> Choices(string[] channels)
        {
            List<string> all = new List<string>(Basic);
            if (channels == null) return all;
            for (int i = 0; i < channels.Length; i++)
            {
                string c = channels[i];
                if (string.IsNullOrEmpty(c) || IsBasic(c) || all.Contains(c)) continue;
                all.Add(c);
            }
            return all;
        }

        /// `chosen` reordered to the order of `choices` (unknown names kept, last):
        /// the bar reads the same way whatever order they were ticked in.
        public static List<string> Ordered(IList<string> chosen, IList<string> choices)
        {
            List<string> o = new List<string>();
            for (int i = 0; i < choices.Count; i++)
                if (chosen.Contains(choices[i])) o.Add(choices[i]);
            for (int i = 0; i < chosen.Count; i++)
                if (!o.Contains(chosen[i])) o.Add(chosen[i]);
            return o;
        }

        // --- the bar's line ---------------------------------------------------------

        /// "Speed 5.2 m/s   Health 80   Stamina 100 ..." at t, the chosen
        /// values in their order. A value the run did not record is left
        /// out; a run with no state at all says so when a state value is
        /// chosen. "" for no run or nothing chosen.
        public static string Line(Attempt run, float t, IList<string> chosen)
        {
            if (run == null || chosen == null || chosen.Count == 0) return NoneChosen;
            int st = ReplayStats.IndexAt(run.States, t);
            StringBuilder sb = new StringBuilder();
            bool wantsState = false;
            for (int i = 0; i < chosen.Count; i++)
            {
                string name = chosen[i];
                float v;
                if (string.Equals(name, Speed, StringComparison.OrdinalIgnoreCase))
                {
                    if (!ReplayStats.SpeedAt(run.Samples, t, out v)) continue;
                    Append(sb, Speed, v);
                    continue;
                }
                wantsState = true;
                if (st < 0) continue;
                int c = run.ChannelIndex(name);
                float[] values = run.States[st].Values;
                if (c < 0 || values == null || c >= values.Length) continue;
                Append(sb, run.Channels[c], values[c]);
            }
            if (wantsState && run.States.Count == 0)
                return sb.Length > 0 ? NotRecorded + "   " + sb : NotRecorded;
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, string name, float v)
        {
            if (sb.Length > 0) sb.Append("   ");
            sb.Append(Label(name)).Append(' ').Append(Format(name, v));
        }

        /// Line() kept until the run, the choice (its Version), the snapshot
        /// or the position sample changes (the bar refreshes at 10 Hz, the
        /// data steps at 5 Hz).
        public sealed class Cache
        {
            private Attempt _run;
            private int _version = -1, _state = -2, _sample = -2;
            private string _text = "";
            public int Builds;

            public string Get(Attempt run, float t, IList<string> chosen, int version)
            {
                if (run == null) return "";
                int st = ReplayStats.IndexAt(run.States, t);
                int sa = ReplayStats.IndexAt(run.Samples, t);
                if (ReferenceEquals(run, _run) && version == _version && st == _state && sa == _sample) return _text;
                _run = run; _version = version; _state = st; _sample = sa;
                _text = Line(run, t, chosen);
                Builds++;
                return _text;
            }
        }
    }
}
