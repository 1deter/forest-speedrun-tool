using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Run categories (run mode phase 4; docs/run-mode.md *Categories*).
    //
    // The moderators define them on the site's /admin (seeded from
    // speedrun.com's categories and rules); the plugin fetches the
    // published ones as text (/api/categories.txt) and keeps a copy for
    // offline starts. A category says what a run allows: the game it is
    // played in (difficulty, Creative, multiplayer), every overlay feature
    // (author, 2026-10-02: "every option customisable for the moderators",
    // the obvious ones off by default) - locked, allowed (the runner's
    // choice) or forced on (manhunt: logs in the inventory for everyone,
    // unchangeable) - whether the anti-splice codes are used, whether an
    // offline (amber) attempt counts, the banned moves and the rules.
    //
    // Every save on the site bumps `version`; each attempt's report names
    // the category and version it ran under, so a later edit never changes
    // how an old attempt reads. Linked into the site, so pure and tested.
    //
    //   [category]
    //   id = any-normal
    //   name = Any% - Normal
    //   version = 3
    //   status = published            draft / published / hidden
    //   difficulty = normal           any / peaceful / normal / hard
    //   creative = no                 any / yes / no
    //   multiplayer = no              any / yes / no
    //   spot = s-0123456789ab         the community run spot (optional)
    //   antisplice = on
    //   amber = accepted              accepted / not accepted
    //   feature godmode = locked      one line per feature not at its default
    //   logcap = 10                   the log cap when "logs" is forced on (1-99)
    //   cap Rock = 50                 an item cap (the game's item name) when
    //                                 "itemcaps" is forced on (1-9999)
    //   banned = The explosives glitch
    //   rule = -Time starts when player movement occurs
    //   src = xk9jypgd/abc123         speedrun.com category / subcategory value
    // ------------------------------------------------------------------
    public sealed class RunCategory
    {
        public const string Locked = "locked";
        public const string Allowed = "allowed";
        public const string Forced = "forced";

        /// A feature the moderators set per category. Actions (a teleport,
        /// a restore) can be allowed but never forced on.
        public sealed class Feature
        {
            public readonly string Key;
            public readonly string Label;
            public readonly bool Toggle;      // a switch that can be forced on
            public readonly string Default;
            public readonly string[] Marks;   // PracticeState.Mark reasons it covers (prefixes)

            public Feature(string key, string label, bool toggle, string def, params string[] marks)
            {
                Key = key; Label = label; Toggle = toggle; Default = def; Marks = marks;
            }
        }

        /// Every feature, in the order /admin and the Runs tab show them.
        public static readonly Feature[] Features =
        {
            new Feature("reload", "Reload save on death", true, Allowed),
            new Feature("go", "Go (teleport to a spot)", false, Locked, "teleport"),
            new Feature("restart", "Restart other spots (F7)", false, Locked, "restart"),
            new Feature("savestates", "Savestates (Quick load / Full load)", false, Locked, "savestate"),
            new Feature("revive", "Practice revive at a spot on death", false, Locked, "death revive"),
            new Feature("godmode", "God mode", true, Locked, "god mode"),
            new Feature("nostagger", "No blood / no stagger", true, Locked, "no blood", "no stagger"),
            new Feature("itemcaps", "Item caps", true, Locked, "item caps"),
            new Feature("logs", "Logs in the inventory", true, Locked, "logs in the inventory"),
            new Feature("fastbuild", "Fast building", true, Locked, "fast building"),
            new Feature("perf", "Experimental performance patches", false, Locked, "experimental"),
            new Feature("freecam", "Freecam", false, Locked, "freecam"),
            new Feature("aerial", "Aerial capture", false, Locked, "aerial capture"),
            new Feature("trajectory", "Trajectory preview", false, Locked, "trajectory preview"),
            new Feature("replaycam", "Replay camera", false, Locked, "replay camera"),
            new Feature("bridge", "Test bridge (dev tool)", false, Locked, "test bridge"),
        };

        public static Feature FindFeature(string key)
        {
            for (int i = 0; i < Features.Length; i++)
                if (Features[i].Key == key) return Features[i];
            return null;
        }

        /// The feature a practice mark's reason belongs to (null: none).
        public static Feature FeatureOfMark(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return null;
            for (int i = 0; i < Features.Length; i++)
                for (int j = 0; j < Features[i].Marks.Length; j++)
                    if (reason.StartsWith(Features[i].Marks[j], StringComparison.Ordinal)) return Features[i];
            return null;
        }

        public string Id = "";
        public string Name = "";
        public int Version;
        public string Status = "draft";
        public string Difficulty = "any";
        public string Creative = "any";
        public string Multiplayer = "any";
        public string Spot = "";
        public bool AntiSplice = true;
        public bool AmberAccepted = true;
        public string Source = "";
        /// The log cap a forced "logs" uses (0: not set - the game's mod default).
        public int LogCap;
        /// The item caps a forced "itemcaps" uses: item name (the game's
        /// database name) -> cap, in the moderators' order (author,
        /// 2026-10-02: a manhunt host sets the numbers).
        public readonly List<KeyValuePair<string, int>> ItemCaps = new List<KeyValuePair<string, int>>();
        public const int LogCapMax = 99, ItemCapMax = 9999;
        public const int DefaultLogCap = 5;
        public readonly List<string> Banned = new List<string>();
        public readonly List<string> Rules = new List<string>();
        private readonly Dictionary<string, string> _features = new Dictionary<string, string>();

        /// locked / allowed / forced (a feature not listed: its default).
        public string Policy(string key)
        {
            string v;
            if (_features.TryGetValue(key, out v)) return v;
            Feature f = FindFeature(key);
            return f == null ? Locked : f.Default;
        }

        /// False for an unknown feature, or forced on an action.
        public bool SetPolicy(string key, string value)
        {
            Feature f = FindFeature(key);
            if (f == null) return false;
            if (value != Locked && value != Allowed && value != Forced) return false;
            if (value == Forced && !f.Toggle) return false;
            if (value == f.Default) _features.Remove(key); else _features[key] = value;
            return true;
        }

        /// Sets an item's cap (a name already listed is replaced, case
        /// ignored). False for an empty name; the cap is clamped.
        public bool SetItemCap(string name, int cap)
        {
            name = ItemName(name);
            if (name.Length == 0) return false;
            cap = Math.Max(1, Math.Min(ItemCapMax, cap));
            for (int i = 0; i < ItemCaps.Count; i++)
                if (string.Equals(ItemCaps[i].Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    ItemCaps[i] = new KeyValuePair<string, int>(name, cap);
                    return true;
                }
            ItemCaps.Add(new KeyValuePair<string, int>(name, cap));
            return true;
        }

        /// The log cap a forced "logs" applies.
        public int EffectiveLogCap { get { return LogCap > 0 ? LogCap : DefaultLogCap; } }

        /// An item name as the format keeps it: one line, no '=' (the
        /// key / value separator), trimmed.
        public static string ItemName(string s)
        {
            return OneLine(s).Replace("=", "").Trim();
        }

        public bool IsLocked(string key) { return Policy(key) == Locked; }
        public bool IsForced(string key) { return Policy(key) == Forced; }

        /// "Any% - Normal (v3)".
        public string Label { get { return Name + " (v" + Version + ")"; } }

        // ------------------------------------------------------------------
        // The game the category is played in, against GameSetup's values
        // (Difficulty: Peaceful / Normal / Hard; Game: Standard / Creative).

        /// The ways `difficulty`, `creative` and `multiplayer` do not match
        /// the game as the report found it; empty when they all do.
        public List<string> GameMismatch(string difficulty, bool creative, bool multiplayer)
        {
            List<string> bad = new List<string>();
            if (Difficulty != "any" && !SameDifficulty(Difficulty, difficulty))
                bad.Add("the category is played on " + Capital(Difficulty) + ", the game was " + (string.IsNullOrEmpty(difficulty) ? "not read" : difficulty));
            if (Creative == "yes" && !creative) bad.Add("the category is played in Creative, the game was not");
            if (Creative == "no" && creative) bad.Add("the category is not played in Creative, the game was");
            if (Multiplayer == "yes" && !multiplayer) bad.Add("the category is multiplayer, the game was single player");
            if (Multiplayer == "no" && multiplayer) bad.Add("the category is single player, the game was multiplayer");
            return bad;
        }

        private static bool SameDifficulty(string category, string game)
        {
            if (string.IsNullOrEmpty(game)) return false;
            string g = game.ToLowerInvariant();
            if (g == "hardsurvival" || g == "hardmode") g = "hard";
            return g == category;
        }

        // ------------------------------------------------------------------
        // Text.

        public static List<RunCategory> Parse(string text)
        {
            List<RunCategory> list = new List<RunCategory>();
            if (string.IsNullOrEmpty(text)) return list;
            RunCategory c = null;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length > 0 && line[0] == '﻿') line = line.Substring(1);
                if (line.Trim() == "[category]") { c = new RunCategory(); list.Add(c); continue; }
                if (c == null || line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf(" =");
                if (eq < 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = eq + 2 < line.Length ? line.Substring(eq + 2) : "";
                if (value.StartsWith(" ")) value = value.Substring(1);
                if (key.StartsWith("feature "))
                {
                    c.SetPolicy(key.Substring(8).Trim(), value.Trim());
                    continue;
                }
                if (key.StartsWith("cap "))
                {
                    int cap;
                    if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out cap) && cap > 0)
                        c.SetItemCap(key.Substring(4), cap);
                    continue;
                }
                switch (key)
                {
                    case "id": c.Id = value.Trim(); break;
                    case "name": c.Name = value.Trim(); break;
                    case "version": int.TryParse(value.Trim(), out c.Version); break;
                    case "status": c.Status = OneOf(value, "draft", "draft", "published", "hidden"); break;
                    case "difficulty": c.Difficulty = OneOf(value, "any", "any", "peaceful", "normal", "hard"); break;
                    case "creative": c.Creative = OneOf(value, "any", "any", "yes", "no"); break;
                    case "multiplayer": c.Multiplayer = OneOf(value, "any", "any", "yes", "no"); break;
                    case "spot": c.Spot = value.Trim(); break;
                    case "antisplice": c.AntiSplice = value.Trim() != "off"; break;
                    case "amber": c.AmberAccepted = value.Trim() != "not accepted"; break;
                    case "src": c.Source = value.Trim(); break;
                    case "logcap":
                        int logs;
                        if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out logs) && logs > 0)
                            c.LogCap = Math.Min(LogCapMax, logs);
                        break;
                    case "banned": if (value.Trim().Length > 0) c.Banned.Add(value.Trim()); break;
                    case "rule": c.Rules.Add(value.TrimEnd()); break;
                }
            }
            list.RemoveAll(x => !ValidId(x.Id));
            return list;
        }

        public static string Format(IList<RunCategory> list)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                list[i].AppendTo(sb);
            }
            return sb.ToString();
        }

        public string Format()
        {
            StringBuilder sb = new StringBuilder();
            AppendTo(sb);
            return sb.ToString();
        }

        private void AppendTo(StringBuilder sb)
        {
            sb.Append("[category]\n");
            Line(sb, "id", Id);
            Line(sb, "name", Name);
            Line(sb, "version", Version.ToString());
            Line(sb, "status", Status);
            Line(sb, "difficulty", Difficulty);
            Line(sb, "creative", Creative);
            Line(sb, "multiplayer", Multiplayer);
            if (Spot.Length > 0) Line(sb, "spot", Spot);
            Line(sb, "antisplice", AntiSplice ? "on" : "off");
            Line(sb, "amber", AmberAccepted ? "accepted" : "not accepted");
            for (int i = 0; i < Features.Length; i++)
            {
                string v;
                if (_features.TryGetValue(Features[i].Key, out v)) Line(sb, "feature " + Features[i].Key, v);
            }
            if (LogCap > 0) Line(sb, "logcap", LogCap.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < ItemCaps.Count; i++)
                Line(sb, "cap " + ItemName(ItemCaps[i].Key), ItemCaps[i].Value.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < Banned.Count; i++) Line(sb, "banned", Banned[i]);
            for (int i = 0; i < Rules.Count; i++) Line(sb, "rule", Rules[i]);
            if (Source.Length > 0) Line(sb, "src", Source);
        }

        private static void Line(StringBuilder sb, string key, string value)
        {
            sb.Append(key).Append(" = ").Append(OneLine(value)).Append('\n');
        }

        /// Text as one line of the format (newlines and control characters out).
        public static string OneLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++) sb.Append(char.IsControl(s[i]) ? ' ' : s[i]);
            return sb.ToString();
        }

        /// Ids are lowercase letters, digits and dashes, 1-60.
        public static bool ValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 60) return false;
            for (int i = 0; i < id.Length; i++)
            {
                char ch = id[i];
                if (!(ch >= 'a' && ch <= 'z') && !(ch >= '0' && ch <= '9') && ch != '-') return false;
            }
            return true;
        }

        /// "Any% (No Explosive Glitch) - Normal" -> "any-no-explosive-glitch-normal".
        public static string Slug(string name)
        {
            StringBuilder sb = new StringBuilder();
            bool dash = false;
            foreach (char raw in (name ?? "").ToLowerInvariant())
            {
                char ch = raw == '%' ? ' ' : raw;
                if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')) { if (dash && sb.Length > 0) sb.Append('-'); sb.Append(ch); dash = false; }
                else dash = true;
            }
            string s = sb.ToString();
            return s.Length > 60 ? s.Substring(0, 60).TrimEnd('-') : s;
        }

        /// The category a run spot names (`run = Any% - Normal`): by id, then by name.
        public static RunCategory Find(IList<RunCategory> list, string idOrName)
        {
            if (list == null || string.IsNullOrEmpty(idOrName)) return null;
            string want = idOrName.Trim();
            for (int i = 0; i < list.Count; i++) if (list[i].Id == want) return list[i];
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].Name, want, StringComparison.OrdinalIgnoreCase)) return list[i];
            return null;
        }

        private static string OneOf(string value, string fallback, params string[] allowed)
        {
            string v = value.Trim().ToLowerInvariant();
            for (int i = 0; i < allowed.Length; i++) if (allowed[i] == v) return v;
            return fallback;
        }

        private static string Capital(string s)
        {
            return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}
