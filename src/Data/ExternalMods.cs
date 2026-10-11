using System;
using System.Collections.Generic;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Anything external running beside the game (T-0308): another BepInEx
    // plugin, a patcher other than ours, a DLL loaded from outside the
    // game's own folders, a Harmony patch owned by someone else. The
    // session is flagged like ModAPI - a label, never a gate (rule 12).
    // This is the pure part: names from the lists Game/RunIntegrity
    // gathers, merged for the session, and the flag text.
    // ------------------------------------------------------------------
    public static class ExternalMods
    {
        /// The attempt flag / .run flag: "Mods Detected: A, B". The site
        /// reads the same prefix (AttemptFormat / RunReport keep a copy).
        public const string FlagPrefix = "Mods Detected: ";

        /// Names shown before "+N more" (the full list stays in the report).
        public const int ShownNames = 4;

        /// Adds the names the four lists show to `into` (once each, in
        /// order). Plugins: "Name 1.0 (file.dll)" -> "Name"; patchers and
        /// code: the file; patches: the owner's id. Failures of the
        /// gatherer ("could not list ...") name nothing.
        public static void Collect(IList<string> plugins, IList<string> patchers, IList<string> code,
                                   IList<string> patches, List<string> into)
        {
            if (plugins != null)
                for (int i = 0; i < plugins.Count; i++) Add(into, PluginName(plugins[i]));
            if (patchers != null)
                for (int i = 0; i < patchers.Count; i++) Add(into, Plain(patchers[i]));
            if (code != null)
                for (int i = 0; i < code.Count; i++) Add(into, Plain(code[i]));
            if (patches != null)
                for (int i = 0; i < patches.Count; i++) Add(into, PatchOwner(patches[i]));
        }

        /// "Name 1.0 (file.dll)" -> "Name" (the version is the last word
        /// before the file); a name with no such shape is kept whole.
        public static string PluginName(string entry)
        {
            string s = Plain(entry);
            if (s.Length == 0) return "";
            int paren = s.LastIndexOf(" (", StringComparison.Ordinal);
            if (paren > 0) s = s.Substring(0, paren);
            int sp = s.LastIndexOf(' ');
            if (sp > 0 && sp + 1 < s.Length && char.IsDigit(s[sp + 1])) s = s.Substring(0, sp);
            return s.Trim();
        }

        /// "Type.Method (by owner)" -> "owner".
        public static string PatchOwner(string entry)
        {
            string s = Plain(entry);
            int at = s.LastIndexOf("(by ", StringComparison.Ordinal);
            if (at < 0 || !s.EndsWith(")", StringComparison.Ordinal)) return "";
            return s.Substring(at + 4, s.Length - at - 5).Trim();
        }

        private static string Plain(string s)
        {
            if (string.IsNullOrEmpty(s) || s.StartsWith("could not", StringComparison.Ordinal)) return "";
            return s.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        private static void Add(List<string> into, string name)
        {
            if (name.Length > 0 && !into.Contains(name)) into.Add(name);
        }

        /// "A, B, C, D +2 more": the names for the HUD and the flag.
        public static string Join(IList<string> names)
        {
            if (names == null || names.Count == 0) return "";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int shown = Math.Min(names.Count, ShownNames);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(names[i]);
            }
            if (names.Count > shown) sb.Append(" +").Append(names.Count - shown).Append(" more");
            return sb.ToString();
        }

        public static string Flag(string names)
        {
            return FlagPrefix + names;
        }

        /// The names part of the first mods flag in `flags` ("" when none).
        public static string FromFlags(IList<string> flags)
        {
            if (flags == null) return "";
            for (int i = 0; i < flags.Count; i++)
                if (flags[i] != null && flags[i].StartsWith(FlagPrefix, StringComparison.Ordinal))
                    return flags[i].Substring(FlagPrefix.Length);
            return "";
        }
    }
}
