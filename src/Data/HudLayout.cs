using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The HUD customiser's saved layout (docs/ui-redesign.md): which info
    // box values are their own widget, where they sit and how big they are.
    //
    // A widget is a line of the info box known by its HudLines config key
    // ("ShowSpeed"). Only FREE widgets (taken out of the box) are listed;
    // everything else stays in the box exactly as before, so an empty
    // layout is today's look.
    //
    //   # ForestOverlay HUD layout
    //   ShowSpeed: free, x=24, y=80, scale=2.5, label=off
    //
    // Pure and forgiving: unknown keys / tokens are ignored, numbers are
    // clamped, a bad line is skipped. Linked into the tests.
    // ------------------------------------------------------------------
    public sealed class HudWidgetLayout
    {
        public string Key;
        public float X, Y;
        public float Scale = 1f;
        public bool ShowLabel = true;
    }

    public sealed class HudLayout
    {
        public const float MinScale = 0.5f, MaxScale = 6f;
        public const string Header = "# ForestOverlay HUD layout - one line per widget taken out of the info box.";

        public readonly List<HudWidgetLayout> Widgets = new List<HudWidgetLayout>();

        public HudWidgetLayout Find(string key)
        {
            if (key == null) return null;
            for (int i = 0; i < Widgets.Count; i++)
                if (string.Equals(Widgets[i].Key, key, StringComparison.Ordinal)) return Widgets[i];
            return null;
        }

        /// The widget for a key, taken out of the box at (x, y) if it was in it.
        public HudWidgetLayout Detach(string key, float x, float y)
        {
            HudWidgetLayout w = Find(key);
            if (w == null)
            {
                w = new HudWidgetLayout { Key = key };
                Widgets.Add(w);
            }
            w.X = x;
            w.Y = y;
            return w;
        }

        /// Back into the box. False when it already was.
        public bool Attach(string key)
        {
            for (int i = 0; i < Widgets.Count; i++)
                if (string.Equals(Widgets[i].Key, key, StringComparison.Ordinal)) { Widgets.RemoveAt(i); return true; }
            return false;
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
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string key = line.Substring(0, colon).Trim();
                if (key.Length == 0 || layout.Find(key) != null) continue;

                HudWidgetLayout w = new HudWidgetLayout { Key = key };
                bool free = false;
                string[] tokens = line.Substring(colon + 1).Split(',');
                for (int t = 0; t < tokens.Length; t++)
                {
                    string tok = tokens[t].Trim();
                    if (tok.Length == 0) continue;
                    if (string.Equals(tok, "free", StringComparison.OrdinalIgnoreCase)) { free = true; continue; }
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
                        case "label":
                            w.ShowLabel = !string.Equals(val, "off", StringComparison.OrdinalIgnoreCase) &&
                                          !string.Equals(val, "false", StringComparison.OrdinalIgnoreCase);
                            break;
                    }
                }
                if (free) layout.Widgets.Add(w);
            }
            return layout;
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
                sb.Append(w.Key).Append(": free, x=").Append(N(w.X)).Append(", y=").Append(N(w.Y))
                  .Append(", scale=").Append(N(ClampScale(w.Scale)));
                if (!w.ShowLabel) sb.Append(", label=off");
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
