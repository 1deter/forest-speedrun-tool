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

        /// browser_download_url of the asset named `assetName`, or null -
        /// also null while the asset's "state" is anything but "uploaded"
        /// (GitHub lists an asset mid-upload as "starter").
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
                if (u < 0) return null;

                // "state" sits between the asset's name and its url. The
                // uploader object in between has no "state" of its own.
                const string stateKey = "\"state\"";
                int st = json.IndexOf(stateKey, i, u - i, StringComparison.Ordinal);
                if (st >= 0 && StringValueAfterKey(json, st + stateKey.Length) != "uploaded") return null;

                return StringValueAfterKey(json, u + urlKey.Length);
            }

            return null;
        }

        /// The release's notes ("body" - the version's CHANGELOG.md section,
        /// put there by CI) as plain text for an IMGUI label, or null when
        /// there are none. Assets and uploaders carry no "body", so the
        /// first one is the release's.
        public static string ExtractNotes(string json)
        {
            string raw = ExtractString(json, "body");
            return string.IsNullOrEmpty(raw) ? null : PlainNotes(Unescape(raw));
        }

        /// JSON string escapes: \n \r \t \" \\ \/ \b \f \uXXXX.
        public static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;

            System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }

                char e = s[++i];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        int code;
                        if (i + 4 < s.Length &&
                            int.TryParse(s.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber,
                                         System.Globalization.CultureInfo.InvariantCulture, out code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                        else sb.Append('u');
                        break;
                    default: sb.Append(e); break;   // \" \\ \/
                }
            }
            return sb.ToString();
        }

        /// Markdown to plain text, lightly: headings lose their #, bold and
        /// code marks go, `*` bullets become `-`, and GitHub's generated
        /// tail ("## What's Changed", "**Full Changelog**: ...") is cut.
        public static string PlainNotes(string markdown)
        {
            if (string.IsNullOrEmpty(markdown)) return null;

            string[] lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            bool blank = true;
            bool afterHeading = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd();
                if (line.StartsWith("**Full Changelog**", StringComparison.Ordinal) ||
                    line.StartsWith("## What's Changed", StringComparison.Ordinal)) break;

                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("#", StringComparison.Ordinal)) line = trimmed.TrimStart('#').Trim();
                else if (trimmed.StartsWith("* ", StringComparison.Ordinal))
                    line = line.Substring(0, line.Length - trimmed.Length) + "- " + trimmed.Substring(2);
                line = line.Replace("**", "").Replace("`", "");

                if (line.Length == 0)
                {
                    if (!blank) sb.Append('\n');
                    blank = true;
                    continue;
                }

                // A hard-wrapped line continues the one above: joined, so
                // the label wraps it to the tab's width instead.
                string start = line.TrimStart();
                bool heading = trimmed.StartsWith("#", StringComparison.Ordinal);
                bool continues = !blank && !afterHeading && !heading &&
                                 !start.StartsWith("- ", StringComparison.Ordinal) && sb.Length > 0;
                if (continues) { sb.Length--; sb.Append(' ').Append(start).Append('\n'); }
                else sb.Append(line).Append('\n');
                blank = false;
                afterHeading = heading;
            }

            string text = sb.ToString().Trim();
            return text.Length == 0 ? null : text;
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
