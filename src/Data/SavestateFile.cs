using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // A savestate on disk: a few header lines, then the game's own level
    // serialization (LevelSerializer.SerializeLevel - base64 on a single
    // line) as `data = ...`.
    //
    //   ForestOverlay savestate 1
    //   name = Plane crash start
    //   level = ForestMain_v07
    //   difficulty = Normal
    //   created = 2026-09-23 21:04:11
    //   plugin = 0.20.0
    //   position = -1000.50 90.25 550.13
    //   cave = 0
    //   data = <base64>
    //
    // Pure so the round trip is tested: a savestate is meant to be shared
    // beside a segment, and a writer/parser disagreement would corrupt
    // everyone's copy. The header is for people and the panel; only
    // `data` is handed to the game.
    // ------------------------------------------------------------------
    public sealed class SavestateFile
    {
        public const string Magic = "ForestOverlay savestate 1";
        public const string Extension = ".fosave";

        public string Name = "";
        public string Level = "";
        public string Difficulty = "";
        public string Created = "";
        public string PluginVersion = "";
        public float X, Y, Z;
        public bool InCave;
        public string Data = "";

        public string Write()
        {
            StringBuilder sb = new StringBuilder(Data.Length + 256);
            sb.Append(Magic).Append('\n');
            Line(sb, "name", Name);
            Line(sb, "level", Level);
            Line(sb, "difficulty", Difficulty);
            Line(sb, "created", Created);
            Line(sb, "plugin", PluginVersion);
            Line(sb, "position", F(X) + " " + F(Y) + " " + F(Z));
            Line(sb, "cave", InCave ? "1" : "0");
            Line(sb, "data", Data);
            return sb.ToString();
        }

        /// Returns null and sets `error` when the text is not a savestate
        /// this version can read. Unknown keys are ignored, so a later
        /// version can add header lines without breaking older readers.
        public static SavestateFile Parse(string text, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(text)) { error = "empty file"; return null; }

            string[] lines = text.Split('\n');
            if (lines[0].TrimEnd('\r') != Magic)
            {
                error = "not a ForestOverlay savestate (first line: '" + Clip(lines[0].TrimEnd('\r')) + "')";
                return null;
            }

            SavestateFile s = new SavestateFile();
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                if (line.Length == 0) continue;

                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "name": s.Name = value; break;
                    case "level": s.Level = value; break;
                    case "difficulty": s.Difficulty = value; break;
                    case "created": s.Created = value; break;
                    case "plugin": s.PluginVersion = value; break;
                    case "cave": s.InCave = value == "1"; break;
                    case "data": s.Data = value; break;
                    case "position":
                        {
                            string[] p = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (p.Length == 3)
                            {
                                TryF(p[0], out s.X);
                                TryF(p[1], out s.Y);
                                TryF(p[2], out s.Z);
                            }
                            break;
                        }
                }
            }

            if (s.Data.Length == 0) { error = "no data line"; return null; }
            return s;
        }

        /// A name safe as a file name on Windows: invalid characters become
        /// '-', runs are collapsed, and an empty result becomes "savestate".
        public static string SafeFileName(string name)
        {
            if (name == null) name = "";
            const string invalid = "<>:\"/\\|?*";

            StringBuilder sb = new StringBuilder(name.Length);
            bool lastDash = false;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool bad = c < 32 || invalid.IndexOf(c) >= 0;
                if (bad || c == ' ')
                {
                    if (!lastDash && sb.Length > 0) { sb.Append('-'); lastDash = true; }
                    continue;
                }
                sb.Append(c);
                lastDash = false;
            }

            string s = sb.ToString().Trim('-', '.');
            return s.Length == 0 ? "savestate" : s;
        }

        // Header values are single-line: a newline would end the value and
        // start a bogus key.
        private static void Line(StringBuilder sb, string key, string value)
        {
            if (value == null) value = "";
            value = value.Replace('\r', ' ').Replace('\n', ' ');
            sb.Append(key).Append(" = ").Append(value).Append('\n');
        }

        private static string F(float v)
        {
            return v.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static bool TryF(string s, out float v)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        private static string Clip(string s)
        {
            return s.Length > 40 ? s.Substring(0, 40) + "..." : s;
        }
    }
}
