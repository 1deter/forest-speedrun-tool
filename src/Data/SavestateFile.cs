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
    //   streaming = unloaded
    //   pickups = 210@1283.2,-70.2,615.0;...
    //   book = 23:00000100000000000000001
    //   held = 53
    //   panels = 30@120.5,-80.1,33.0;...
    //   cutscene = megan-transform@12.40
    //   areas = caves no, endgame yes, overlook no | scenes: ... | streamed: ...
    //   data = <base64>
    //
    // `streaming` says whether streamed content was force-unloaded around
    // the capture; a restore must do the same or it would duplicate or
    // delete streamed objects. Files from v0.20.0 have no line: "kept".
    // `pickups` lists the world pickups present at capture (see
    // PickupKey); absent in v0.20.0 files, which then restore every pickup
    // taken since. `book` is the survival book's open page (BookPageState);
    // absent before v0.24.0, and then the page is left as it is. `held`
    // lists the item ids in the equipment slots (hands first), re-equipped
    // after an in-place restore; absent before v0.24.1. `panels` lists
    // every cave wooden panel's health as "health@x,y,z" (PickupKey's
    // format); absent before v0.24.2. `cutscene` names the endgame
    // cutscene running at capture (a GameEvents event) and how far into it
    // (game seconds from the cutscene flag's rising edge); absent when none.
    // `areas` is Game/AreaReport's line at capture - for the log, so a
    // restore can say what differs (v0.24.4).
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
        public bool StreamingUnloaded;

        /// Null when the file has no pickups line (v0.20.0), which is not
        /// the same as a capture that saw no pickups.
        public List<string> Pickups;

        /// BookPageState's value; "" when not captured.
        public string Book = "";

        /// Null when the file has no held line (before v0.24.1).
        public List<int> Held;

        /// Null when the file has no panels line (before v0.24.2).
        public List<string> Panels;

        /// The cutscene running at capture, "" for none; CutsceneAt is how
        /// far into it, in game seconds (-1 for none).
        public string Cutscene = "";
        public float CutsceneAt = -1f;

        /// AreaReport.Describe() at capture; "" before v0.24.4.
        public string Areas = "";

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
            Line(sb, "streaming", StreamingUnloaded ? "unloaded" : "kept");
            if (Pickups != null) Line(sb, "pickups", string.Join(";", Pickups.ToArray()));
            if (Book.Length > 0) Line(sb, "book", Book);
            if (Held != null)
            {
                string[] ids = new string[Held.Count];
                for (int i = 0; i < Held.Count; i++) ids[i] = Held[i].ToString(CultureInfo.InvariantCulture);
                Line(sb, "held", string.Join(",", ids));
            }
            if (Panels != null) Line(sb, "panels", string.Join(";", Panels.ToArray()));
            if (Areas.Length > 0) Line(sb, "areas", Areas);
            if (Cutscene.Length > 0 && CutsceneAt >= 0f)
                Line(sb, "cutscene", Cutscene + "@" + CutsceneAt.ToString("0.00", CultureInfo.InvariantCulture));
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
                    case "streaming": s.StreamingUnloaded = value == "unloaded"; break;
                    case "pickups":
                        {
                            s.Pickups = new List<string>();
                            string[] keys = value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                            for (int k = 0; k < keys.Length; k++) s.Pickups.Add(keys[k].Trim());
                            break;
                        }
                    case "book": s.Book = value; break;
                    case "areas": s.Areas = value; break;
                    case "cutscene":
                        {
                            int at = value.LastIndexOf('@');
                            float t;
                            if (at > 0 && TryF(value.Substring(at + 1), out t) && t >= 0f)
                            {
                                s.Cutscene = value.Substring(0, at).Trim();
                                s.CutsceneAt = t;
                            }
                            break;
                        }
                    case "panels":
                        {
                            s.Panels = new List<string>();
                            string[] keys = value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                            for (int k = 0; k < keys.Length; k++) s.Panels.Add(keys[k].Trim());
                            break;
                        }
                    case "held":
                        {
                            s.Held = new List<int>();
                            string[] ids = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                            for (int k = 0; k < ids.Length; k++)
                            {
                                int id;
                                if (int.TryParse(ids[k].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) s.Held.Add(id);
                            }
                            break;
                        }
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

        /// Identifies a world pickup across a restore - it has no save
        /// identifier, so item id plus position (0.1 m) is what there is.
        /// Positions are rounded to one decimal, so jitter below 5 cm does
        /// not change the key.
        public static string PickupKey(int itemId, float x, float y, float z)
        {
            return itemId.ToString(CultureInfo.InvariantCulture) + "@" + K(x) + "," + K(y) + "," + K(z);
        }

        // Rounded first so -0.04 reads "0.0", not "-0.0".
        private static string K(float v)
        {
            double r = Math.Round(v, 1);
            if (r == 0) r = 0;
            return r.ToString("0.0", CultureInfo.InvariantCulture);
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
