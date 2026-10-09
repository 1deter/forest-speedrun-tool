using System;
using System.Collections.Generic;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The Runs tab's recent attempts against the website (T-0144): an
    // attempt deleted on the site leaves uploads/attempts/sent.txt so its
    // dead link is not listed. Pure, so the decisions are tested; the
    // requests are Modules/RunUploadModule.Attempts'.
    //
    // The author's decision (2026-10-07): when the Runs tab opens, each
    // listed attempt not yet checked this session gets one
    //   GET /api/attempts/<id>
    // 404 = deleted there: drop it from sent.txt. 200 = it is there: not
    // asked again this session. No answer / anything else: it stays
    // listed (asked again the next time the tab opens).
    //
    // Only the site's own 404 drops one ({"error":"no such attempt"},
    // site/ForestSite/Program.cs, pinned by AttemptTests): a wrong Url or a
    // proxy answering 404 to every path would empty the list one by one.
    //
    // sent.txt is one line per sent attempt: id|verdict|when.
    // ------------------------------------------------------------------
    public enum SentCheck
    {
        There,    // 200: on the site - not asked again this session
        Gone,     // 404: deleted on the site - drop it
        Unknown,  // offline, rate limited, server trouble - leave it
    }

    public static class SentAttempts
    {
        /// The site's error text for an attempt it does not have.
        public const string GoneError = "no such attempt";

        /// What one GET /api/attempts/<id> answer means. `code` 0 = no answer.
        public static SentCheck Meaning(long code, string body)
        {
            if (code == 200) return SentCheck.There;
            if (code == 404 && SiteProtocol.Field(body, "error") == GoneError) return SentCheck.Gone;
            return SentCheck.Unknown;
        }

        /// The listed ids to ask the site about now: those past the first
        /// `waiting` (still in the outbox - not on the site yet), not known to
        /// be there this session, not asked already since the tab opened.
        public static List<string> ToCheck(IList<string> listed, int waiting,
                                           ICollection<string> there, ICollection<string> askedThisOpen)
        {
            List<string> ids = new List<string>();
            if (listed == null) return ids;
            for (int i = Math.Max(0, waiting); i < listed.Count; i++)
            {
                string id = listed[i];
                if (string.IsNullOrEmpty(id) || ids.Contains(id)) continue;
                if (there != null && there.Contains(id)) continue;
                if (askedThisOpen != null && askedThisOpen.Contains(id)) continue;
                ids.Add(id);
            }
            return ids;
        }

        /// sent.txt's text without the lines of `id` (every one: an id can
        /// be listed twice); the other lines are kept as they are. Null when
        /// no line names it (nothing to rewrite).
        public static string Without(string text, string id)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(id)) return null;
            StringBuilder kept = new StringBuilder(text.Length);
            bool dropped = false;
            int start = 0;
            while (start < text.Length)
            {
                int end = text.IndexOf('\n', start);
                int next = end < 0 ? text.Length : end + 1;
                string line = text.Substring(start, (end < 0 ? text.Length : end) - start).TrimEnd('\r');
                int bar = line.IndexOf('|');
                string lineId = bar < 0 ? line : line.Substring(0, bar);
                if (lineId == id) dropped = true;
                else kept.Append(text, start, next - start);
                start = next;
            }
            return dropped ? kept.ToString() : null;
        }
    }
}
