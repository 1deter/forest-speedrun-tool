using System;
using System.Collections.Generic;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Community packs: spots and timed segments every runner gets without
    // asking (author, 2026-09-25: "so that new runners have a bunch of
    // good saved spots with practice already good to go").
    //
    // The repo's `community/` folder holds .foseg files (Data/SegmentBundle)
    // and `index.txt`, one line per file:
    //
    //   # comment
    //   cave5-practice.foseg 1a2b3c4d
    //
    // the hash being HashOf(the file's text). The plugin fetches the index
    // from raw.githubusercontent.com (not the rate-limited API), downloads
    // only files whose hash changed, keeps them in
    // config/ForestOverlay/community/, and writes their segments to their
    // own segment file (`community.txt`, category "Community", read-only in
    // the editor) - a runner's own file is never touched. The cached
    // bundles keep each entry's own category for sub-categories later.
    //
    // Pure so the parsing and the plan are tested; the fetch is
    // Modules/CommunityModule's.
    // ------------------------------------------------------------------
    public static class CommunityIndex
    {
        public const string IndexFile = "index.txt";
        public const string SegmentFile = "community.txt";
        public const string Category = "Community";

        public struct Entry
        {
            public string File;
            public string Hash;
        }

        /// The index's entries. A line whose file name is not a plain
        /// `<name>.foseg` (no folders, no dots before the extension but
        /// ordinary ones) is skipped and counted: names come off the
        /// network and become local paths.
        public static List<Entry> Parse(string text, out int bad)
        {
            bad = 0;
            List<Entry> list = new List<Entry>();
            if (string.IsNullOrEmpty(text)) return list;

            string[] lines = Normalize(text).Split('\n');
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                string[] p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length != 2 || !SafeName(p[0]) || !IsHash(p[1]) || !seen.Add(p[0])) { bad++; continue; }

                Entry e;
                e.File = p[0];
                e.Hash = p[1].ToLowerInvariant();
                list.Add(e);
            }
            return list;
        }

        /// A file name safe to write in the cache folder.
        public static bool SafeName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 120) return false;
            if (!name.EndsWith(SegmentBundle.Extension, StringComparison.OrdinalIgnoreCase)) return false;
            if (name.StartsWith(".")) return false;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                          c == '-' || c == '_' || c == '.';
                if (!ok) return false;
            }
            return name.IndexOf("..", StringComparison.Ordinal) < 0;
        }

        private static bool IsHash(string h)
        {
            if (h.Length != 8) return false;
            for (int i = 0; i < h.Length; i++)
            {
                char c = char.ToLowerInvariant(h[i]);
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            }
            return true;
        }

        /// The index hash of a file's text: line endings and a BOM do not
        /// count, so a checkout with CRLF hashes the same as the server's.
        public static string HashOf(string text)
        {
            return Segment.HashText(Normalize(text));
        }

        private static string Normalize(string text)
        {
            return (text ?? "").TrimStart('﻿').Replace("\r\n", "\n");
        }

        /// What to download (not cached, or cached with another hash) and
        /// which cached files the index no longer lists. `cachedHash`
        /// gives a cached file's hash, or null when it is not cached.
        public static void Plan(List<Entry> index, IList<string> cachedFiles, Func<string, string> cachedHash,
                                List<Entry> fetch, List<string> drop)
        {
            HashSet<string> listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < index.Count; i++)
            {
                listed.Add(index[i].File);
                string have = cachedHash(index[i].File);
                if (have == null || have != index[i].Hash) fetch.Add(index[i]);
            }
            for (int i = 0; i < cachedFiles.Count; i++)
                if (!listed.Contains(cachedFiles[i])) drop.Add(cachedFiles[i]);
        }

        /// The community segment file's text: every bundle's segment under
        /// the Community category, skipping ids in `taken` (the runner's
        /// own) - those are named in `skipped`.
        public static string SegmentFileText(IList<SegmentBundle> bundles, ICollection<string> taken, List<string> skipped)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("# ForestOverlay community spots - written by the plugin from the\n");
            sb.Append("# community packs; any edit here is replaced on the next update.\n");
            sb.Append("# Duplicate an entry in the Practice tab to make your own copy.\n");
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < bundles.Count; i++)
            {
                Segment s = bundles[i].Segment;
                if (taken.Contains(s.Id) || !ids.Add(s.Id)) { skipped.Add(s.Id); continue; }
                string own = s.Category;
                s.Category = Category;
                SegmentFormat.WriteSegment(sb, s, "\n");
                s.Category = own;
            }
            return sb.ToString();
        }
    }
}
