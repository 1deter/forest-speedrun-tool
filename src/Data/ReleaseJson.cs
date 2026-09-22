using System;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Minimal scraping of GitHub's release payload. A real JSON parser is
    // not worth a dependency on net35 for three fields.
    //
    // Must tolerate whitespace: the API pretty-prints ("name": "x"). The
    // asset lookup once matched only the compact form, so it never found
    // ForestOverlay.dll on any release and every download reported "no
    // asset" - while the version check, which used the tolerant reader,
    // looked healthy. Tested against a real response for that reason.
    // ------------------------------------------------------------------
    public static class ReleaseJson
    {
        /// The first string value for `key`, or null.
        public static string ExtractString(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;

            string needle = "\"" + key + "\"";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            return i < 0 ? null : StringValueAfterKey(json, i + needle.Length);
        }

        /// browser_download_url of the asset named `assetName`, or null.
        public static string ExtractAssetUrl(string json, string assetName)
        {
            if (string.IsNullOrEmpty(json)) return null;

            const string nameKey = "\"name\"";
            int i = 0;

            // The release itself also has a "name"; only the asset's value
            // matches, so walk every occurrence.
            while ((i = json.IndexOf(nameKey, i, StringComparison.Ordinal)) >= 0)
            {
                i += nameKey.Length;
                if (StringValueAfterKey(json, i) != assetName) continue;

                const string urlKey = "\"browser_download_url\"";
                int u = json.IndexOf(urlKey, i, StringComparison.Ordinal);
                return u < 0 ? null : StringValueAfterKey(json, u + urlKey.Length);
            }

            return null;
        }

        /// Why a response carried no release, in words a runner can act on.
        /// GitHub answers errors with {"message": "..."}; the common one is
        /// the anonymous limit of 60 API calls per hour per IP address,
        /// which a shared network (or a developer polling releases) hits.
        public static string DescribeError(string json)
        {
            string message = ExtractString(json, "message");

            if (message != null && message.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0)
                return "GitHub's hourly limit for update checks was reached on this network - try again later";

            if (message == "Not Found")
                return "no release published yet";

            return message != null ? "GitHub said: " + message : "could not read latest release";
        }

        /// Returns >0 when `a` is newer than `b`. Numeric, dot-separated,
        /// tolerant of differing part counts and of trailing suffixes.
        public static int CompareVersions(string a, string b)
        {
            if (a == null) a = "";
            if (b == null) b = "";

            string[] pa = a.Split('.');
            string[] pb = b.Split('.');
            int n = Math.Max(pa.Length, pb.Length);

            for (int i = 0; i < n; i++)
            {
                int va = i < pa.Length ? ParseLeadingInt(pa[i]) : 0;
                int vb = i < pb.Length ? ParseLeadingInt(pb[i]) : 0;
                if (va != vb) return va > vb ? 1 : -1;
            }
            return 0;
        }

        /// `from` is just past a key's closing quote. Accepts whitespace
        /// around the colon; returns null when the value is not a string
        /// (a number, null, an object).
        private static string StringValueAfterKey(string json, int from)
        {
            int i = from;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length || json[i] != ':') return null;

            i++;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length || json[i] != '"') return null;

            int start = i + 1;
            int end = start;
            while (end < json.Length && json[end] != '"')
            {
                if (json[end] == '\\') end++;   // skip escaped char
                end++;
            }
            if (end >= json.Length) return null;

            return json.Substring(start, end - start);
        }

        private static int ParseLeadingInt(string s)
        {
            int value = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] < '0' || s[i] > '9') break;
                value = value * 10 + (s[i] - '0');
            }
            return value;
        }
    }
}
