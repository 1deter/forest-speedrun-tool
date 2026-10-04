using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The weather at capture (Game/WeatherKeeper), as a savestate's
    // `weather` header - space-separated key=value tokens:
    //
    //   state=Raining type=Heavy dice=4,1,0 overcast=1,1 opacity=2.168,2
    //   alpha=2.518,2 sky=0,0 coverage=0.574,0.6 mat=1,2.168,2.518
    //   vclouds=0.574 fog=300,300
    //
    //   state, type  the weather's state and rain type (the game's enum names)
    //   dice         the rain rolls: dice, stop dice, stop rolls so far
    //   overcast / opacity / alpha / sky / coverage
    //                the cloud values: current, target
    //   mat          the cloud material as drawn: overcast, opacity, alpha
    //   vclouds      the volumetric clouds' coverage as drawn
    //   fog          the fog distance the game eases to, and the one drawn
    //
    // The game keeps none of this in its save. A value the file does not
    // have is NaN (and left as it is); unknown keys are ignored, so a later
    // version can add some.
    // ------------------------------------------------------------------
    public sealed class WeatherState
    {
        public string State = "";
        public string Type = "";

        public bool HasDice;
        public int RainDice, RainDiceStop, RainStopRolls;

        public float OvercastCurrent = float.NaN, OvercastTarget = float.NaN;
        public float OpacityCurrent = float.NaN, OpacityTarget = float.NaN;
        public float AlphaCurrent = float.NaN, AlphaTarget = float.NaN;
        public float SkyCurrent = float.NaN, SkyTarget = float.NaN;
        public float CoverageCurrent = float.NaN, CoverageTarget = float.NaN;

        public float MatOvercast = float.NaN, MatOpacity = float.NaN, MatAlpha = float.NaN;
        public float VCloudsCoverage = float.NaN;

        public float FogTarget = float.NaN, FogDrawn = float.NaN;

        public bool IsEmpty
        {
            get { return State.Length == 0 && Type.Length == 0 && !HasDice && float.IsNaN(OvercastCurrent) && float.IsNaN(FogTarget) && float.IsNaN(MatOvercast); }
        }

        public string Write()
        {
            StringBuilder sb = new StringBuilder(160);
            if (State.Length > 0) Token(sb, "state", State);
            if (Type.Length > 0) Token(sb, "type", Type);
            if (HasDice)
                Token(sb, "dice", RainDice.ToString(CultureInfo.InvariantCulture) + "," +
                                  RainDiceStop.ToString(CultureInfo.InvariantCulture) + "," +
                                  RainStopRolls.ToString(CultureInfo.InvariantCulture));
            Floats(sb, "overcast", OvercastCurrent, OvercastTarget);
            Floats(sb, "opacity", OpacityCurrent, OpacityTarget);
            Floats(sb, "alpha", AlphaCurrent, AlphaTarget);
            Floats(sb, "sky", SkyCurrent, SkyTarget);
            Floats(sb, "coverage", CoverageCurrent, CoverageTarget);
            Floats(sb, "mat", MatOvercast, MatOpacity, MatAlpha);
            Floats(sb, "vclouds", VCloudsCoverage);
            Floats(sb, "fog", FogTarget, FogDrawn);
            return sb.ToString();
        }

        /// False for "" and for text with no token this version reads.
        public static bool TryParse(string text, out WeatherState s)
        {
            s = new WeatherState();
            if (string.IsNullOrEmpty(text)) return false;
            string[] tokens = text.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                int eq = tokens[i].IndexOf('=');
                if (eq <= 0) continue;
                string key = tokens[i].Substring(0, eq), value = tokens[i].Substring(eq + 1);
                float[] f;
                switch (key)
                {
                    case "state": s.State = value; break;
                    case "type": s.Type = value; break;
                    case "dice":
                        {
                            string[] d = value.Split(',');
                            int a, b, c;
                            if (d.Length == 3 && TryI(d[0], out a) && TryI(d[1], out b) && TryI(d[2], out c))
                            {
                                s.HasDice = true;
                                s.RainDice = a; s.RainDiceStop = b; s.RainStopRolls = c;
                            }
                            break;
                        }
                    case "overcast": if (TryFs(value, 2, out f)) { s.OvercastCurrent = f[0]; s.OvercastTarget = f[1]; } break;
                    case "opacity": if (TryFs(value, 2, out f)) { s.OpacityCurrent = f[0]; s.OpacityTarget = f[1]; } break;
                    case "alpha": if (TryFs(value, 2, out f)) { s.AlphaCurrent = f[0]; s.AlphaTarget = f[1]; } break;
                    case "sky": if (TryFs(value, 2, out f)) { s.SkyCurrent = f[0]; s.SkyTarget = f[1]; } break;
                    case "coverage": if (TryFs(value, 2, out f)) { s.CoverageCurrent = f[0]; s.CoverageTarget = f[1]; } break;
                    case "mat": if (TryFs(value, 3, out f)) { s.MatOvercast = f[0]; s.MatOpacity = f[1]; s.MatAlpha = f[2]; } break;
                    case "vclouds": if (TryFs(value, 1, out f)) s.VCloudsCoverage = f[0]; break;
                    case "fog": if (TryFs(value, 2, out f)) { s.FogTarget = f[0]; s.FogDrawn = f[1]; } break;
                }
            }
            return !s.IsEmpty;
        }

        /// For the log: "Raining (Heavy), overcast 1, fog 300 m".
        public string Describe()
        {
            List<string> parts = new List<string>();
            if (State.Length > 0)
                parts.Add(Type.Length > 0 && Type != "None" ? State + " (" + Type + ")" : State);
            if (!float.IsNaN(MatOvercast)) parts.Add("overcast " + F(MatOvercast));
            else if (!float.IsNaN(OvercastCurrent)) parts.Add("overcast " + F(OvercastCurrent));
            if (!float.IsNaN(FogDrawn)) parts.Add("fog " + FogDrawn.ToString("0", CultureInfo.InvariantCulture) + " m");
            return parts.Count == 0 ? "nothing" : string.Join(", ", parts.ToArray());
        }

        private static void Token(StringBuilder sb, string key, string value)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(key).Append('=').Append(value);
        }

        private static void Floats(StringBuilder sb, string key, params float[] values)
        {
            for (int i = 0; i < values.Length; i++)
                if (float.IsNaN(values[i]) || float.IsInfinity(values[i])) return;   // all or nothing
            string[] text = new string[values.Length];
            for (int i = 0; i < values.Length; i++) text[i] = F(values[i]);
            Token(sb, key, string.Join(",", text));
        }

        private static string F(float f)
        {
            return f.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static bool TryI(string text, out int i)
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out i);
        }

        private static bool TryFs(string text, int count, out float[] values)
        {
            values = null;
            string[] p = text.Split(',');
            if (p.Length != count) return false;
            float[] f = new float[count];
            for (int i = 0; i < count; i++)
                if (!float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out f[i]) || float.IsNaN(f[i]) || float.IsInfinity(f[i]))
                    return false;
            values = f;
            return true;
        }
    }
}
