using System;
using System.Collections.Generic;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // A HUD profile (T-0019, Momentum Mod style): the whole HUD layout in
    // one file, `config/ForestOverlay/hud/<name>.txt`, so it can be shared.
    // The settings it holds (which values show, the column's place, Compact,
    // Text size, the splits / results panels) are config entries, written
    // as `@Section.Key = value`; the rest of the file is the value layout
    // (Data/HudLayout: placed values, text around them).
    //
    //   # ForestOverlay HUD profile
    //   @HUD.ShowSpeed = true
    //   @Splits.PanelX = -1
    //   ShowSpeed: free, x=24, y=80, scale=2.5
    //
    // A setting missing from the file takes its default. Pure and
    // forgiving like HudLayout; linked into the tests.
    // ------------------------------------------------------------------
    public sealed class HudProfile
    {
        public const string Header = "# ForestOverlay HUD profile - its settings (@Section.Key = value), then the values you changed. Share it by copying the file.";

        private readonly List<string> _keys = new List<string>();
        private readonly List<string> _values = new List<string>();
        public HudLayout Layout = new HudLayout();

        public int Count { get { return _keys.Count; } }
        public string KeyAt(int i) { return _keys[i]; }
        public string ValueAt(int i) { return _values[i]; }

        /// The stored value of "Section.Key", or null when the file has none.
        public string Get(string key)
        {
            int i = IndexOf(key);
            return i < 0 ? null : _values[i];
        }

        public void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            value = OneLine(value);
            int i = IndexOf(key);
            if (i < 0) { _keys.Add(key); _values.Add(value); }
            else _values[i] = value;
        }

        private int IndexOf(string key)
        {
            if (key == null) return -1;
            for (int i = 0; i < _keys.Count; i++)
                if (string.Equals(_keys[i], key, StringComparison.Ordinal)) return i;
            return -1;
        }

        private static string OneLine(string s)
        {
            if (s == null) return "";
            return s.Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        public static HudProfile Parse(string text)
        {
            HudProfile p = new HudProfile();
            if (string.IsNullOrEmpty(text)) return p;
            StringBuilder layout = new StringBuilder();
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] != '@')
                {
                    layout.Append(line).Append('\n');
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 1) continue;
                string key = line.Substring(1, eq - 1).Trim();
                if (key.Length == 0 || p.IndexOf(key) >= 0) continue;   // the first one counts
                p.Set(key, line.Substring(eq + 1));
            }
            p.Layout = HudLayout.Parse(layout.ToString());
            return p;
        }

        public string Format()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Header).Append('\n');
            for (int i = 0; i < _keys.Count; i++)
                sb.Append('@').Append(_keys[i]).Append(" = ").Append(_values[i]).Append('\n');
            sb.Append(Layout.Format());
            return sb.ToString();
        }
    }

    // ------------------------------------------------------------------
    // Profile names = file names (`hud/<name>.txt`), so they must be safe
    // on Windows. Compared ignoring case (the file system does).
    // ------------------------------------------------------------------
    public static class HudProfileNames
    {
        public const int MaxLength = 32;
        public const string DefaultName = "Default";
        public const string Extension = ".txt";

        private const string Forbidden = "<>:\"/\\|?*";
        private static readonly string[] Reserved =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        /// Why `name` (already trimmed) cannot be a profile's name, or null
        /// when it can. `taken`: the other profiles' names (not the one being renamed).
        public static string Problem(string name, IList<string> taken)
        {
            if (string.IsNullOrEmpty(name)) return "Type a name.";
            if (name.Length > MaxLength) return "At most " + MaxLength + " characters.";
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c < ' ' || Forbidden.IndexOf(c) >= 0) return "A name cannot contain " + Forbidden.Replace("\\", "\\ ") + ".";
            }
            if (name[name.Length - 1] == '.') return "A name cannot end with a dot.";
            for (int i = 0; i < Reserved.Length; i++)
                if (string.Equals(name, Reserved[i], StringComparison.OrdinalIgnoreCase)) return "Windows keeps that name for itself.";
            if (Contains(taken, name)) return "There is already a profile called " + name + ".";
            return null;
        }

        public static bool Contains(IList<string> names, string name)
        {
            if (names == null) return false;
            for (int i = 0; i < names.Count; i++)
                if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// `wanted`, or "wanted 2", "wanted 3", ... - the first not taken
        /// (cut to fit MaxLength).
        public static string Unique(string wanted, IList<string> taken)
        {
            if (string.IsNullOrEmpty(wanted)) wanted = "Profile";
            if (wanted.Length > MaxLength) wanted = wanted.Substring(0, MaxLength).TrimEnd();
            if (!Contains(taken, wanted)) return wanted;
            for (int n = 2; ; n++)
            {
                string suffix = " " + n;
                string stem = wanted.Length + suffix.Length > MaxLength ? wanted.Substring(0, MaxLength - suffix.Length).TrimEnd() : wanted;
                string name = stem + suffix;
                if (!Contains(taken, name)) return name;
            }
        }

        /// The names in the order the picker lists them.
        public static void Sort(List<string> names)
        {
            names.Sort(StringComparer.OrdinalIgnoreCase);
        }
    }
}
