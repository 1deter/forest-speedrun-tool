using System;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The site's side of a live session (T-0490, forest.deter.cloud):
    // what each answer means. Pure, so it is tested; the requests are
    // Modules/LiveLinkModule's. The contract (fixed, docs/bridge.md
    // *Live sessions*):
    //
    //   POST /api/live/open                  -> {"code":"ABC123","secret":"<48 hex>","idle":1800}
    //   GET  /api/live/in     Bearer secret  -> text/plain bridge lines (empty = none); 410 closed
    //   POST /api/live/out    Bearer secret, text/plain body -> 204; 410 closed
    //   POST /api/live/file?name=<[A-Za-z0-9_-]{1,64}>  Bearer, image/png <= 4 MB -> 204
    //   POST /api/live/close  Bearer secret  -> 204
    //
    // No runner token: a runner's upload registration may be broken.
    // ------------------------------------------------------------------
    public static class LiveLink
    {
        /// The site's cap on one uploaded file.
        public const int MaxFileBytes = 4 * 1024 * 1024;

        /// Idle minutes when the site does not say (author, 2026-11-28: 30).
        public const int DefaultIdleSeconds = 1800;

        public enum Answer
        {
            Ok,       // 2xx
            Closed,   // 410 / 404: the session is gone - end it here
            Retry,    // no answer, 5xx, 429: try again next poll
            Refused,  // anything else (400, 401, 413): this request will never pass
        }

        public static Answer Classify(long code)
        {
            if (code >= 200 && code < 300) return Answer.Ok;
            if (code == 410 || code == 404) return Answer.Closed;
            if (code == 0 || code == 429 || code >= 500) return Answer.Retry;
            return Answer.Refused;
        }

        public static string OpenUrl(string baseUrl) { return SiteProtocol.TrimUrl(baseUrl) + "/api/live/open"; }
        public static string InUrl(string baseUrl) { return SiteProtocol.TrimUrl(baseUrl) + "/api/live/in"; }
        public static string OutUrl(string baseUrl) { return SiteProtocol.TrimUrl(baseUrl) + "/api/live/out"; }
        public static string CloseUrl(string baseUrl) { return SiteProtocol.TrimUrl(baseUrl) + "/api/live/close"; }

        /// The upload URL for a file, or null when the name is not one the
        /// site takes ([A-Za-z0-9_-]{1,64}).
        public static string FileUrl(string baseUrl, string name)
        {
            if (!IsFileName(name)) return null;
            return SiteProtocol.TrimUrl(baseUrl) + "/api/live/file?name=" + name;
        }

        public static bool IsFileName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 64) return false;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        /// Reads the open answer; false (with why) when it is not one.
        public static bool ParseOpen(string json, out string code, out string secret, out int idleSeconds, out string why)
        {
            code = SiteProtocol.Field(json, "code");
            secret = SiteProtocol.Field(json, "secret");
            long idle = SiteProtocol.Number(json, "idle");
            idleSeconds = idle > 0 && idle < int.MaxValue ? (int)idle : DefaultIdleSeconds;
            why = null;
            if (!IsCode(code)) why = "the site sent no session code";
            else if (string.IsNullOrEmpty(secret) || secret.Length < 16 || !IsHex(secret)) why = "the site sent no session key";
            if (why == null) return true;
            code = secret = null;
            return false;
        }

        /// Six letters / digits, as the site makes them.
        public static bool IsCode(string code)
        {
            if (code == null || code.Length != 6) return false;
            for (int i = 0; i < code.Length; i++)
                if (!char.IsLetterOrDigit(code[i]) || code[i] > 127) return false;
            return true;
        }

        private static bool IsHex(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
            }
            return true;
        }

        /// The upload name for a bridge screenshot file name ("shot-120355.png"
        /// -> "shot-120355"; characters the site refuses become '_'), or
        /// null if nothing is left.
        public static string ShotName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            string n = fileName;
            if (n.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
            char[] cs = n.ToCharArray();
            for (int i = 0; i < cs.Length; i++)
            {
                char c = cs[i];
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok) cs[i] = '_';
            }
            n = new string(cs);
            if (n.Length > 64) n = n.Substring(0, 64);
            return n.Length > 0 ? n : null;
        }

        /// True when a session has had no command for `idleSeconds`.
        public static bool IdleOver(float now, float lastCommand, int idleSeconds)
        {
            return idleSeconds > 0 && now - lastCommand >= idleSeconds;
        }
    }
}
