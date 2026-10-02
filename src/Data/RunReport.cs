using System.Collections.Generic;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // What ran during a run attempt, in plain words (run mode, phase 1).
    //
    // Gathered by Modules/RunModeModule: the game's code (file hashes
    // against the known Steam build), the other mods BepInEx loaded, any
    // other code loaded from outside the game, every Harmony patch whose
    // owner is not ForestOverlay, the game's own cheat switches, and run
    // mode's flags. Findings() turns them into the lines a verifier reads
    // (author, 2026-10-02: plain words, never code); Format() is the file
    // written beside the runs. Pure, so it is tested.
    // ------------------------------------------------------------------
    public sealed class RunReport
    {
        /// Assembly-CSharp.dll of the Steam build (the game's last update;
        /// hashed from the author's install, 2026-10-02).
        public static readonly string[] KnownGameHashes =
        {
            "3fcb3958d5c6a04c16b010a056bc0798ec4e1a4705cdc1e974d70ba2fb5c1c59",
        };

        public int Attempt;
        public string Started = "";       // "Normal", "Hard, multiplayer", ...
        public string StartedAt = "";     // local time, "2026-10-02 14:03:11"
        public string PluginVersion = "";
        public string AttemptId = "";     // run mode's log and link (phase 2)

        /// The category the attempt ran under (phase 4): its id and version
        /// ("" / 0 when none was known - an old plugin, a category the site
        /// does not have), and the game as GameSetup said at the start.
        public string Category = "";
        public int CategoryVersion;
        public string Difficulty = "";    // Peaceful / Normal / Hard
        public bool Creative;
        public bool Multiplayer;
        /// Features the category allowed and the runner used ("godmode").
        public readonly List<string> Used = new List<string>();

        /// SHA-256 of Assembly-CSharp.dll; "" while not read, "error: ..." when unreadable.
        public string GameHash = "";

        public readonly List<string> OtherPlugins = new List<string>();   // "Name 1.0 (file.dll)"
        public readonly List<string> OtherPatchers = new List<string>();  // file names
        public readonly List<string> OtherCode = new List<string>();      // assemblies loaded from elsewhere
        public readonly List<string> ForeignPatches = new List<string>(); // "Type.Method (by owner)"
        public int OwnPatchedMethods;
        public readonly List<string> Cheats = new List<string>();         // the game's cheat switches seen on
        public readonly List<string> Flags = new List<string>();          // run mode's flags
        /// Only when the game's file hash is not a known build: "<top-level
        /// type> <16 hex>" per type of the loaded game code (Game/RunIntegrity.HashTypes),
        /// so the site can name what changed (phase 3). Not a finding here.
        public readonly List<string> TypeHashes = new List<string>();
        public string PracticeBefore = "";                                // a practice action earlier this session

        public static bool IsKnownGame(string hash)
        {
            if (string.IsNullOrEmpty(hash)) return false;
            for (int i = 0; i < KnownGameHashes.Length; i++)
                if (string.Equals(KnownGameHashes[i], hash, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// True when nothing in the report makes the attempt not valid.
        public bool Clean
        {
            get
            {
                return IsKnownGame(GameHash) && OtherPlugins.Count == 0 && OtherPatchers.Count == 0 &&
                       OtherCode.Count == 0 && ForeignPatches.Count == 0 && Cheats.Count == 0 && Flags.Count == 0;
            }
        }

        /// The plain-words lines: "OK  ..." for each check passed,
        /// "NOT OK  ..." for each problem, "Note  ..." for what is only
        /// worth knowing.
        public List<string> Findings()
        {
            List<string> lines = new List<string>();

            if (GameHash.Length == 0) lines.Add("Note  The game's files are still being checked.");
            else if (GameHash.StartsWith("error")) lines.Add("NOT OK  The game's code could not be checked (" + GameHash + ").");
            else if (IsKnownGame(GameHash)) lines.Add("OK  The game's code is the unmodified Steam game.");
            else lines.Add("NOT OK  The game's code is not the Steam game's - it was changed or is another version (" + Short(GameHash) + ").");

            if (OtherPlugins.Count == 0 && OtherPatchers.Count == 0 && OtherCode.Count == 0)
                lines.Add("OK  No other mods loaded.");
            for (int i = 0; i < OtherPlugins.Count; i++) lines.Add("NOT OK  Another mod is loaded: " + OtherPlugins[i] + ".");
            for (int i = 0; i < OtherPatchers.Count; i++) lines.Add("NOT OK  Another BepInEx patcher is installed: " + OtherPatchers[i] + ".");
            for (int i = 0; i < OtherCode.Count; i++) lines.Add("NOT OK  Code from outside the game is loaded: " + OtherCode[i] + ".");

            if (ForeignPatches.Count == 0) lines.Add("OK  Nothing else changes the game's code while it runs.");
            for (int i = 0; i < ForeignPatches.Count; i++) lines.Add("NOT OK  Another mod changes the game's code: " + ForeignPatches[i] + ".");

            if (Cheats.Count == 0) lines.Add("OK  The game's own cheats are off.");
            for (int i = 0; i < Cheats.Count; i++) lines.Add("NOT OK  A game cheat is on: " + Cheats[i] + ".");

            if (Flags.Count == 0) lines.Add("OK  No practice feature was used during the run.");
            // A cheat is also a run flag (the HUD says it): said once.
            for (int i = 0; i < Flags.Count; i++)
            {
                string line = "NOT OK  " + Capital(Flags[i]) + ".";
                if (!lines.Contains(line)) lines.Add(line);
            }

            for (int i = 0; i < Used.Count; i++)
            {
                RunCategory.Feature f = RunCategory.FindFeature(Used[i]);
                lines.Add("Note  Used, allowed by the category: " + (f != null ? f.Label : Used[i]) + ".");
            }

            if (PracticeBefore.Length > 0)
                lines.Add("Note  Practice was used before this attempt (" + PracticeBefore + "); the attempt started clean.");
            return lines;
        }

        /// The report file's text.
        public string Format()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("[runreport]\n");
            sb.Append("attempt = ").Append(Attempt).Append('\n');
            sb.Append("started = ").Append(Started).Append('\n');
            sb.Append("at = ").Append(StartedAt).Append('\n');
            sb.Append("plugin = ").Append(PluginVersion).Append('\n');
            if (AttemptId.Length > 0) sb.Append("id = ").Append(AttemptId).Append('\n');
            if (Category.Length > 0) sb.Append("category = ").Append(Category).Append(" v").Append(CategoryVersion).Append('\n');
            if (Difficulty.Length > 0)
            {
                sb.Append("difficulty = ").Append(Difficulty).Append('\n');
                sb.Append("creative = ").Append(Creative ? "yes" : "no").Append('\n');
                sb.Append("multiplayer = ").Append(Multiplayer ? "yes" : "no").Append('\n');
            }
            sb.Append("game = ").Append(GameHash).Append('\n');
            sb.Append("verdict = ").Append(Clean ? "clean" : "not valid").Append('\n');
            sb.Append("ownpatches = ").Append(OwnPatchedMethods).Append('\n');
            List(sb, "otherplugin", OtherPlugins);
            List(sb, "otherpatcher", OtherPatchers);
            List(sb, "othercode", OtherCode);
            List(sb, "foreignpatch", ForeignPatches);
            List(sb, "cheat", Cheats);
            List(sb, "flag", Flags);
            List(sb, "used", Used);
            if (PracticeBefore.Length > 0) sb.Append("practicebefore = ").Append(PracticeBefore).Append('\n');
            List(sb, "typehash", TypeHashes);
            sb.Append('\n');
            List<string> f = Findings();
            for (int i = 0; i < f.Count; i++) sb.Append("# ").Append(f[i]).Append('\n');
            return sb.ToString();
        }

        /// Format()'s text back (the site reads the report after an
        /// attempt log's `[report]`). Unknown keys are skipped; the findings
        /// after the blank line are not read - Findings() makes them again.
        public static RunReport Parse(string text)
        {
            RunReport r = new RunReport();
            if (string.IsNullOrEmpty(text)) return r;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line == "[runreport]") continue;
                if (line.Length == 0) break;
                int eq = line.IndexOf(" = ");
                string key = eq < 0 ? line.TrimEnd(' ', '=') : line.Substring(0, eq);
                string value = eq < 0 ? "" : line.Substring(eq + 3);
                switch (key)
                {
                    case "attempt": int.TryParse(value, out r.Attempt); break;
                    case "started": r.Started = value; break;
                    case "at": r.StartedAt = value; break;
                    case "plugin": r.PluginVersion = value; break;
                    case "id": r.AttemptId = value; break;
                    case "category":
                    {
                        int v = value.LastIndexOf(" v");
                        if (v > 0 && int.TryParse(value.Substring(v + 2), out r.CategoryVersion)) r.Category = value.Substring(0, v);
                        else r.Category = value;
                        break;
                    }
                    case "difficulty": r.Difficulty = value; break;
                    case "creative": r.Creative = value == "yes"; break;
                    case "multiplayer": r.Multiplayer = value == "yes"; break;
                    case "used": r.Used.Add(value); break;
                    case "game": r.GameHash = value; break;
                    case "ownpatches": int.TryParse(value, out r.OwnPatchedMethods); break;
                    case "otherplugin": r.OtherPlugins.Add(value); break;
                    case "otherpatcher": r.OtherPatchers.Add(value); break;
                    case "othercode": r.OtherCode.Add(value); break;
                    case "foreignpatch": r.ForeignPatches.Add(value); break;
                    case "cheat": r.Cheats.Add(value); break;
                    case "flag": r.Flags.Add(value); break;
                    case "practicebefore": r.PracticeBefore = value; break;
                    case "typehash": r.TypeHashes.Add(value); break;
                }
            }
            return r;
        }

        /// One log line: the verdict and what is wrong.
        public string Summary()
        {
            if (Clean) return "clean (Steam game, no other mods, no cheats)";
            List<string> bad = new List<string>();
            List<string> f = Findings();
            for (int i = 0; i < f.Count; i++)
                if (f[i].StartsWith("NOT OK  ")) bad.Add(f[i].Substring(8).TrimEnd('.'));
            if (bad.Count == 0) return "checks pending";
            return "NOT VALID: " + string.Join("; ", bad.ToArray());
        }

        private static void List(StringBuilder sb, string key, List<string> items)
        {
            for (int i = 0; i < items.Count; i++) sb.Append(key).Append(" = ").Append(items[i]).Append('\n');
        }

        private static string Short(string hash)
        {
            return hash.Length > 12 ? hash.Substring(0, 12) : hash;
        }

        private static string Capital(string s)
        {
            return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}
