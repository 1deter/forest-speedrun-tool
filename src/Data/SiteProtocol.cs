using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The website's side of a run upload (forest.deter.cloud, site/ in this
    // repo): what to send, and what each answer means. Pure, so the
    // decisions are tested; the requests are Modules/RunUploadModule's.
    //
    //   POST /api/register  {"runner":"r-...","name":"..."}  -> {"token":"ft_..."}
    //                       409 = the id is registered already (its token
    //                       was lost - the author can reset it)
    //   POST /api/runs      a .foseg (segment + [attempt]s), Bearer token
    //                       200 stored (or already there), 400 / 422 refused
    //                       (another route version, not a bundle), 401 the
    //                       token is not known, 429 too many
    //   POST /api/submissions  a .foseg (segment + start state, no
    //                       attempts), Bearer token -> {"id":n,"replaced":b}:
    //                       a spot for the author to approve as a community
    //                       spot; a runner's second submit of the same spot
    //                       replaces the one still waiting
    //   DELETE /api/spots/<id>  Bearer token -> {"runs":n}: the runner's own
    //                       spot off the site (they first uploaded on it);
    //                       403 not theirs / a community spot, 404 not on
    //                       the site, 409 other runners have runs on it
    //   POST /api/attempts  {"attempt":"a-...","category":"...","spot":"..."},
    //                       Bearer -> {"nonce":"..."}: a run mode attempt
    //                       starts (docs/run-mode.md phase 2)
    //   POST /api/attempts/<id>/checkpoints  {"step":n,"head":"<64 hex>"},
    //                       about once a minute; 429 = too soon
    //   POST /api/attempts/<id>/log  the attempt's log (Data/AttemptChain)
    //                       -> {"verdict":"green|amber|red","why":[...]};
    //                       409 = a different log is in already
    // ------------------------------------------------------------------
    public enum UploadOutcome
    {
        Done,        // stored (or already on the site) - drop the file
        Refused,     // the site will never take this file - set it aside
        TokenBad,    // stop until the token is sorted out
        RetryLater,  // network, rate limit, server trouble
    }

    public static class SiteProtocol
    {
        public const string DefaultUrl = "https://forest.deter.cloud";

        /// What to do with a pending upload after this answer. `code` 0 =
        /// no answer (network error).
        public static UploadOutcome Classify(long code)
        {
            if (code >= 200 && code < 300) return UploadOutcome.Done;
            if (code == 401) return UploadOutcome.TokenBad;
            if (code == 400 || code == 413 || code == 422) return UploadOutcome.Refused;
            return UploadOutcome.RetryLater;
        }

        /// Seconds to wait before retry n (1-based): 30 s doubling to 15 min.
        public static float RetryDelay(int failures)
        {
            float d = 30f;
            for (int i = 1; i < failures && d < 900f; i++) d *= 2f;
            return Math.Min(d, 900f);
        }

        public static string RegisterBody(string runnerId, string name)
        {
            return "{\"runner\":" + JsonString(runnerId) + ",\"name\":" + JsonString(name) + "}";
        }

        /// A string field of a flat JSON object ("token", "error"); null
        /// when missing. Enough for the site's small answers.
        public static string Field(string json, string name)
        {
            if (string.IsNullOrEmpty(json)) return null;
            string key = "\"" + name + "\"";
            int k = json.IndexOf(key, StringComparison.Ordinal);
            if (k < 0) return null;
            int colon = json.IndexOf(':', k + key.Length);
            if (colon < 0) return null;
            int q = colon + 1;
            while (q < json.Length && json[q] == ' ') q++;
            if (q >= json.Length || json[q] != '"') return null;

            StringBuilder sb = new StringBuilder();
            for (int i = q + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"') return sb.ToString();
                if (c == '\\' && i + 1 < json.Length)
                {
                    char e = json[++i];
                    if (e == 'n') sb.Append('\n');
                    else if (e == 't') sb.Append('\t');
                    else if (e == 'u' && i + 4 < json.Length)
                    {
                        int cp;
                        if (int.TryParse(json.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out cp))
                            sb.Append((char)cp);
                        i += 4;
                    }
                    else sb.Append(e);
                }
                else sb.Append(c);
            }
            return null;
        }

        /// The numbers of a JSON array field ("added": [12, 13]).
        public static List<long> Ids(string json, string name)
        {
            List<long> ids = new List<long>();
            if (string.IsNullOrEmpty(json)) return ids;
            int k = json.IndexOf("\"" + name + "\"", StringComparison.Ordinal);
            if (k < 0) return ids;
            int open = json.IndexOf('[', k);
            int close = open < 0 ? -1 : json.IndexOf(']', open);
            if (close < 0) return ids;
            string[] parts = json.Substring(open + 1, close - open - 1).Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                long v;
                if (long.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) ids.Add(v);
            }
            return ids;
        }

        /// A whole-number field of a flat JSON object ("id": 12); -1 when
        /// missing.
        public static long Number(string json, string name)
        {
            if (string.IsNullOrEmpty(json)) return -1;
            string key = "\"" + name + "\"";
            int k = json.IndexOf(key, StringComparison.Ordinal);
            int colon = k < 0 ? -1 : json.IndexOf(':', k + key.Length);
            if (colon < 0) return -1;
            int a = colon + 1;
            while (a < json.Length && json[a] == ' ') a++;
            int b = a;
            while (b < json.Length && char.IsDigit(json[b])) b++;
            long v;
            return b > a && long.TryParse(json.Substring(a, b - a), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : -1;
        }

        /// Why a spot cannot be submitted as a community spot; null = it can.
        /// Community ids must be the hidden random kind (`s-` + hex, v0.24.74):
        /// an old id (`spot.my.new-spot-3`) is shared by many runners' first
        /// spots, and the plugin skips a pack entry whose id a runner has.
        public static string SubmitRefusal(string segmentId, bool hasAttempts)
        {
            if (!IsRandomId(segmentId))
                return "This entry has an old-style id that other runners' spots share. Duplicate it, Save the copy and submit that.";
            if (hasAttempts) return "A community spot carries no times - submit it without attempts.";
            return null;
        }

        /// The upload to send again with the route's start state after the
        /// site answered `"startstate": "wanted"` (T-0194): the same bundle
        /// plus `stateText` (the spot's .fosave here). Null, with why, when
        /// there is nothing right to send - the bundle already carried one
        /// (the site refused it: never loop), the segment has no startstate
        /// line, there is no state here, or it is not the one the route was
        /// timed from.
        public static string StartStateResend(string bundleText, string stateText, out string why)
        {
            string error;
            SegmentBundle b = SegmentBundle.Parse(bundleText, out error, null);
            if (b == null) { why = "the queued file is unreadable (" + error + ")"; return null; }
            if (b.StartState != null) { why = "the site did not keep the start state sent"; return null; }
            if (string.IsNullOrEmpty(b.Segment.StartState)) { why = "the spot has no start state"; return null; }
            if (stateText == null) { why = "no start state for it on this PC"; return null; }
            SavestateFile f = SavestateFile.Parse(stateText, out error);
            if (f == null) { why = "the start state here is unreadable (" + error + ")"; return null; }
            if (Segment.HashText(f.Data) != b.Segment.StartState) { why = "the start state here is not the one the route was timed from"; return null; }
            b.StartState = stateText;
            why = null;
            return b.Write();
        }

        /// `s-` + 12 to 32 hex digits (PracticeModule.NewId).
        public static bool IsRandomId(string id)
        {
            if (id == null || id.Length < 14 || id.Length > 34 || !id.StartsWith("s-", StringComparison.Ordinal)) return false;
            for (int i = 2; i < id.Length; i++)
                if (!Uri.IsHexDigit(id[i])) return false;
            return true;
        }

        /// The `runner|<id>|<name>` line of a .run text; false when absent.
        public static bool RunnerOf(string runText, out string id, out string name)
        {
            id = null; name = null;
            if (string.IsNullOrEmpty(runText)) return false;
            string[] lines = runText.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("s|") || line.StartsWith("v|")) break;
                if (!line.StartsWith("runner|")) continue;
                string[] p = line.Split('|');
                if (p.Length < 2 || p[1].Length == 0) return false;
                id = p[1];
                name = p.Length > 2 ? p[2] : "";
                return true;
            }
            return false;
        }

        public static string AttemptStartBody(string attemptId, string category, string spot)
        {
            return "{\"attempt\":" + JsonString(attemptId) + ",\"category\":" + JsonString(category) +
                   ",\"spot\":" + JsonString(spot) + "}";
        }

        public static string CheckpointBody(int step, string head)
        {
            return "{\"step\":" + step.ToString(CultureInfo.InvariantCulture) + ",\"head\":" + JsonString(head) + "}";
        }

        /// The site's page for a run mode attempt (the link a runner shares).
        public static string AttemptUrl(string baseUrl, string attemptId)
        {
            return TrimUrl(baseUrl) + "/attempt/" + Uri.EscapeDataString(attemptId ?? "");
        }

        /// The site's page for a spot.
        public static string SpotUrl(string baseUrl, string segmentId)
        {
            return TrimUrl(baseUrl) + "/spot/" + Uri.EscapeDataString(segmentId ?? "");
        }

        /// DELETE here removes the runner's own spot (docs/website.md).
        public static string DeleteSpotUrl(string baseUrl, string segmentId)
        {
            return TrimUrl(baseUrl) + "/api/spots/" + Uri.EscapeDataString(segmentId ?? "");
        }

        /// What the runner reads under "Delete from the website" after the
        /// site's answer (`code` 0 = no answer; `body` the JSON).
        public static string DeleteSpotMessage(long code, string body, string error)
        {
            string why = Field(body, "error");
            if (code >= 200 && code < 300)
            {
                long runs = Number(body, "runs");
                return "Deleted from the website" + (runs >= 0 ? " with " + runs + (runs == 1 ? " run" : " runs") : "") +
                       ". It comes back with your next upload on it - finished runs upload by themselves while uploads are on (Runs tab).";
            }
            if (code == 401) return "The site does not know this install's token - nothing deleted.";
            if (code == 404) return "Not on the website - nothing to delete.";
            if (code == 403 || code == 409) return "Not deleted: " + (why ?? "HTTP " + code) + ".";
            if (code == 429) return "Not deleted - too many requests just now. Try again in a while.";
            return "Not deleted - the site is not reachable (" + (code == 0 ? error ?? "no answer" : "HTTP " + code) + "). Try again later.";
        }

        public static string TrimUrl(string url)
        {
            return (url ?? "").Trim().TrimEnd('/');
        }

        private static string JsonString(string s)
        {
            StringBuilder sb = new StringBuilder("\"");
            string t = s ?? "";
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
