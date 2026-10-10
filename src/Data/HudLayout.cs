using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The HUD customiser's saved layout (docs/ui-redesign.md, T-0018): the
    // runner's changes to the HUD values. A value is known by its HudLines
    // config key ("ShowSpeed"). Every ticked value shows; by default it sits
    // in the COLUMN at the HUD position, in the pre-overhaul order. Listed
    // here are only the values with a change:
    //
    //   - FREE: dragged out of the column, placed anywhere, sized by scale;
    //   - text BEFORE / AFTER the value, the runner's own ("Speed ", " u/s").
    //
    //   # ForestOverlay HUD layout
    //   ShowSpeed: free, x=24, y=80, scale=2.5, after=" u/s"
    //   ShowPosition: before="Pos "
    //
    // An empty layout is the default look. Pure and forgiving: unknown keys /
    // tokens are ignored (`label=` from the first redesign builds too),
    // numbers are clamped, a bad line is skipped. Linked into the tests.
    // ------------------------------------------------------------------
    public sealed class HudWidgetLayout
    {
        public string Key;
        /// Out of the column, at (X, Y) and Scale.
        public bool Free;
        public float X, Y;
        public float Scale = 1f;
        /// The runner's text around the value; never null.
        public string Before = "", After = "";

        /// Nothing left to keep: in the column, no text.
        public bool IsDefault { get { return !Free && Before.Length == 0 && After.Length == 0; } }
    }

    public sealed class HudLayout
    {
        public const float MinScale = 0.5f, MaxScale = 6f;
        /// Longest text before / after a value.
        public const int MaxText = 40;
        public const string Header = "# ForestOverlay HUD layout - one line per HUD value you changed (placed on its own, or text around it).";

        public readonly List<HudWidgetLayout> Widgets = new List<HudWidgetLayout>();

        public HudWidgetLayout Find(string key)
        {
            if (key == null) return null;
            for (int i = 0; i < Widgets.Count; i++)
                if (string.Equals(Widgets[i].Key, key, StringComparison.Ordinal)) return Widgets[i];
            return null;
        }

        public bool IsFree(string key)
        {
            HudWidgetLayout w = Find(key);
            return w != null && w.Free;
        }

        private HudWidgetLayout Get(string key)
        {
            HudWidgetLayout w = Find(key);
            if (w == null)
            {
                w = new HudWidgetLayout { Key = key };
                Widgets.Add(w);
            }
            return w;
        }

        /// The value out of the column, at (x, y).
        public HudWidgetLayout Detach(string key, float x, float y)
        {
            HudWidgetLayout w = Get(key);
            w.Free = true;
            w.X = x;
            w.Y = y;
            return w;
        }

        /// Back into the column (its text stays). False when it already was.
        public bool Attach(string key)
        {
            HudWidgetLayout w = Find(key);
            if (w == null || !w.Free) return false;
            w.Free = false;
            w.Scale = 1f;
            if (w.IsDefault) Widgets.Remove(w);
            return true;
        }

        /// The runner's text around a value. False when nothing changed.
        public bool SetText(string key, string before, string after)
        {
            if (key == null) return false;
            before = CleanText(before);
            after = CleanText(after);
            HudWidgetLayout w = Find(key);
            if (w == null)
            {
                if (before.Length == 0 && after.Length == 0) return false;
                w = Get(key);
            }
            if (w.Before == before && w.After == after) return false;
            w.Before = before;
            w.After = after;
            if (w.IsDefault) Widgets.Remove(w);
            return true;
        }

        /// One line, at most MaxText characters (a line break or tab would
        /// break the file and the column's line height).
        public static string CleanText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            char[] chars = null;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] >= ' ' && s[i] != '\u007f') continue;
                if (chars == null) chars = s.ToCharArray();
                chars[i] = ' ';
            }
            if (chars != null) s = new string(chars);
            return s.Length > MaxText ? s.Substring(0, MaxText) : s;
        }

        /// The value with the runner's text around it.
        public static string Decorate(string before, string value, string after)
        {
            if (string.IsNullOrEmpty(before) && string.IsNullOrEmpty(after)) return value ?? "";
            return (before ?? "") + (value ?? "") + (after ?? "");
        }

        public static float ClampScale(float s)
        {
            if (float.IsNaN(s) || float.IsInfinity(s)) return 1f;
            return s < MinScale ? MinScale : s > MaxScale ? MaxScale : s;
        }

        /// The scale a dragged corner gives: the widget was `startWidth` wide at
        /// `startScale`; the corner is now at `newWidth`. Snaps to 0.25.
        public static float ScaleFromDrag(float startScale, float startWidth, float newWidth)
        {
            if (startWidth < 1f) return ClampScale(startScale);
            float s = startScale * (newWidth / startWidth);
            return ClampScale((float)Math.Round(s * 4.0) / 4f);
        }

        // --- text ----------------------------------------------------------

        public static HudLayout Parse(string text)
        {
            HudLayout layout = new HudLayout();
            if (string.IsNullOrEmpty(text)) return layout;
            string[] lines = text.Split('\n');
            List<string> tokens = new List<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string key = line.Substring(0, colon).Trim();
                if (key.Length == 0 || layout.Find(key) != null) continue;

                HudWidgetLayout w = new HudWidgetLayout { Key = key };
                Tokens(line, colon + 1, tokens);
                for (int t = 0; t < tokens.Count; t++)
                {
                    string tok = tokens[t].Trim();
                    if (tok.Length == 0) continue;
                    if (string.Equals(tok, "free", StringComparison.OrdinalIgnoreCase)) { w.Free = true; continue; }
                    int eq = tok.IndexOf('=');
                    if (eq <= 0) continue;
                    string name = tok.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = tok.Substring(eq + 1).Trim();
                    float f;
                    switch (name)
                    {
                        case "x": if (Num(val, out f)) w.X = f; break;
                        case "y": if (Num(val, out f)) w.Y = f; break;
                        case "scale": if (Num(val, out f)) w.Scale = ClampScale(f); break;
                        case "before": w.Before = CleanText(Unquote(val)); break;
                        case "after": w.After = CleanText(Unquote(val)); break;
                    }
                }
                if (!w.IsDefault) layout.Widgets.Add(w);
            }
            return layout;
        }

        // Splits on commas outside "quoted" text (a quote inside is \", a
        // backslash \\).
        private static void Tokens(string line, int start, List<string> into)
        {
            into.Clear();
            StringBuilder sb = new StringBuilder();
            bool quoted = false;
            for (int i = start; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted && c == '\\' && i + 1 < line.Length) { sb.Append(c).Append(line[++i]); continue; }
                if (c == '"') quoted = !quoted;
                if (c == ',' && !quoted) { into.Add(sb.ToString()); sb.Length = 0; continue; }
                sb.Append(c);
            }
            into.Add(sb.ToString());
        }

        private static string Unquote(string val)
        {
            if (val.Length < 2 || val[0] != '"' || val[val.Length - 1] != '"') return val;
            StringBuilder sb = new StringBuilder();
            for (int i = 1; i < val.Length - 1; i++)
            {
                char c = val[i];
                if (c == '\\' && i + 1 < val.Length - 1) c = val[++i];
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string Quote(string s)
        {
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static bool Num(string s, out float f)
        {
            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) return false;
            if (float.IsNaN(f) || float.IsInfinity(f)) { f = 0f; return false; }
            return true;
        }

        public string Format()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Header).Append('\n');
            for (int i = 0; i < Widgets.Count; i++)
            {
                HudWidgetLayout w = Widgets[i];
                if (w.IsDefault) continue;
                sb.Append(w.Key).Append(':');
                string sep = " ";
                if (w.Free)
                {
                    sb.Append(" free, x=").Append(N(w.X)).Append(", y=").Append(N(w.Y))
                      .Append(", scale=").Append(N(ClampScale(w.Scale)));
                    sep = ", ";
                }
                if (w.Before.Length > 0) { sb.Append(sep).Append("before=").Append(Quote(w.Before)); sep = ", "; }
                if (w.After.Length > 0) sb.Append(sep).Append("after=").Append(Quote(w.After));
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private static string N(float v)
        {
            return Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
