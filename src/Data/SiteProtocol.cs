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

        /// The site's page for a spot.
        public static string SpotUrl(string baseUrl, string segmentId)
        {
            return TrimUrl(baseUrl) + "/#/spot/" + Uri.EscapeDataString(segmentId ?? "");
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
