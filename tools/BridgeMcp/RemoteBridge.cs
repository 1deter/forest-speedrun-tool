using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace ForestOverlay.BridgeMcp
{
    /// One open live session as GET /api/admin/live lists it.
    internal sealed class LiveEntry
    {
        /// The site's JSON (camelCase): code, opened, lastCommand, lastPoll,
        /// outBytes, state ("open" or "closed: <why>").
        public string Code, Opened, LastCommand, LastPoll, State;
        public long OutBytes;

        public bool IsOpen { get { return State == null || State == "open"; } }

        public override string ToString()
        {
            return Code + " (" + (State ?? "open") + "; opened " + (Opened ?? "?") + ", last command " + (LastCommand ?? "none") +
                   ", game's last poll " + (LastPoll ?? "?") + ", " + OutBytes.ToString(CultureInfo.InvariantCulture) +
                   " bytes of output)";
        }

        /// A shot name as the game uploads it (LiveLink.ShotName): no .png,
        /// [A-Za-z0-9_-] only (others become _), at most 64.
        public static string FileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            char[] cs = name.ToCharArray();
            for (int i = 0; i < cs.Length; i++)
            {
                char c = cs[i];
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-')) cs[i] = '_';
            }
            string n = new string(cs);
            if (n.Length > 64) n = n.Substring(0, 64);
            return n.Length > 0 ? n : null;
        }
    }

    // ------------------------------------------------------------------
    // The site's live relay (docs/bridge.md *Live sessions*). A consenting
    // runner's game polls forest.deter.cloud; the admin side, here, posts
    // bridge lines to /send and reads the game's out.txt-format replies
    // back from /out?after=<byte offset>. The admin token travels in
    // X-Admin-Token on every call and is never printed or logged.
    // ------------------------------------------------------------------
    internal sealed class LiveSite
    {
        public const string DefaultBase = "https://forest.deter.cloud";
        /// The site owner's admin token: the name scripts/e2e.py and
        /// scripts/aerial-upload.py read (the site's own is FOREST_ADMIN_TOKEN).
        public const string TokenVariable = "FOREST_SITE_ADMIN_TOKEN";
        /// Another base URL - a local copy of the site or the fake-site check.
        public const string BaseVariable = "FOREST_LIVE_BASE";

        public readonly string Base;
        private readonly string _token;
        private readonly HttpClient _http;

        public LiveSite(string baseUrl, string token, HttpClient http)
        {
            Base = (baseUrl ?? DefaultBase).TrimEnd('/');
            _token = token;
            _http = http;
        }

        public bool HasToken { get { return !string.IsNullOrEmpty(_token); } }

        public async Task<HttpResponseMessage> Call(HttpMethod method, string path, HttpContent body, CancellationToken ct)
        {
            using HttpRequestMessage req = new HttpRequestMessage(method, Base + path) { Content = body };
            req.Headers.TryAddWithoutValidation("X-Admin-Token", _token ?? "");
            using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromSeconds(20));
            try { return await _http.SendAsync(req, limit.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new HttpRequestException("no answer from " + Base + " in 20 s");
            }
        }

        /// GET /api/admin/live; null with the problem set when it fails.
        public async Task<(List<LiveEntry> list, string problem)> List(CancellationToken ct)
        {
            if (!HasToken) return (null, "set the " + TokenVariable + " environment variable to the site's admin token first");
            try
            {
                using HttpResponseMessage r = await Call(HttpMethod.Get, "/api/admin/live", null, ct);
                if (!r.IsSuccessStatusCode) return (null, "GET /api/admin/live: " + Status(r));
                JsonArray a = JsonNode.Parse(await r.Content.ReadAsStringAsync(ct)) as JsonArray;
                List<LiveEntry> list = new List<LiveEntry>();
                if (a != null)
                    foreach (JsonNode n in a)
                    {
                        if (!(n is JsonObject o)) continue;
                        list.Add(new LiveEntry
                        {
                            Code = Str(o["code"]),
                            Opened = Str(o["opened"]),
                            LastCommand = Str(o["lastCommand"]),
                            LastPoll = Str(o["lastPoll"]),
                            State = Str(o["state"]),
                            OutBytes = o["outBytes"] is JsonValue v && v.TryGetValue(out long b) ? b : 0,
                        });
                    }
                return (list, null);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is System.Text.Json.JsonException)
            {
                return (null, "GET /api/admin/live: " + ex.Message);
            }
        }

        private static string Str(JsonNode n)
        {
            if (n == null) return null;
            return n is JsonValue v && v.TryGetValue(out string s) ? s : n.ToJsonString();
        }

        public static string Status(HttpResponseMessage r)
        {
            int code = (int)r.StatusCode;
            if (code == 401 || code == 403) return code + " - the admin token was refused (" + TokenVariable + ")";
            return code + " " + r.ReasonPhrase;
        }
    }

    // ------------------------------------------------------------------
    // The remote transport: the batch plus `echo <marker>` goes to /send,
    // then /out?after=&wait= long-polls from the session's end (the site
    // answers as soon as there is output, or on close; at most 15 s) until
    // the marker comes back - the same transcript BridgeClient reads from
    // out.txt, so BridgeText.Parse and the tools take it as it is. One
    // batch at a time. A batch cannot be withdrawn once posted.
    // ------------------------------------------------------------------
    internal sealed class RemoteBridge : IBridgeTransport
    {
        /// The admin endpoints share 120 requests a minute: a /out that
        /// comes back empty sooner than this (a site without the long
        /// poll) waits the rest before the next one.
        public static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(600);
        /// Seconds per long poll (the site caps `wait` at 15).
        public const int LongPoll = 10;

        public readonly LiveSite Site;
        public readonly string Code;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly TimeSpan _poll, _backoff;
        private long _offset;
        private string _banner;

        /// The site said X-Live-State: closed (the runner ended it, or it expired).
        public bool Closed { get; private set; }
        /// Total output bytes read so far (the session's X-Live-Offset).
        public long Offset { get { return _offset; } }
        public bool Trimmed { get; private set; }

        public RemoteBridge(LiveSite site, string code, TimeSpan? poll = null, TimeSpan? backoff = null)
        {
            Site = site;
            Code = code;
            _poll = poll ?? Poll;
            _backoff = backoff ?? TimeSpan.FromSeconds(2);
        }

        private string P(string tail) { return "/api/admin/live/" + Uri.EscapeDataString(Code) + tail; }

        /// Reads the output so far without waiting: the next batch's replies
        /// start after it, and the banner (plugin version) is taken from it.
        public async Task<string> Prime(CancellationToken ct)
        {
            var (bytes, problem) = await ReadOut(ct);
            if (problem != null) return problem;
            Remember(Encoding.UTF8.GetString(bytes));
            return null;
        }

        public string PluginVersionFromBanner() { return _banner; }

        private void Remember(string text)
        {
            string v = BridgeText.BannerVersion(text);
            if (v != null) _banner = v;
        }

        public async Task<BridgeReply> Send(IList<string> commands, TimeSpan timeout, CancellationToken ct,
                                            TimeSpan? readWithin = null)
        {
            await _gate.WaitAsync(ct);
            try { return await SendLocked(commands, timeout, readWithin ?? TimeSpan.FromSeconds(30), ct); }
            finally { _gate.Release(); }
        }

        private async Task<BridgeReply> SendLocked(IList<string> commands, TimeSpan timeout, TimeSpan readWithin,
                                                   CancellationToken ct)
        {
            BridgeReply reply = new BridgeReply();
            if (Closed)
            {
                reply.Problem = ClosedText();
                return reply;
            }

            // Output nobody waited for (an earlier batch that timed out,
            // `anim watch`) is not this batch's reply.
            var (_, stale) = await ReadOut(ct);
            if (stale != null) { reply.Problem = stale; return reply; }
            if (Closed)
            {
                reply.Problem = ClosedText();
                return reply;
            }

            string marker = "__mcp_done_" + Guid.NewGuid().ToString("N");
            StringBuilder sb = new StringBuilder();
            foreach (string c in commands)
            {
                string line = BridgeText.Line(c);
                if (line.Length > 0) sb.Append(line).Append('\n');
            }
            sb.Append("echo ").Append(marker).Append('\n');

            try
            {
                using HttpResponseMessage r = await Site.Call(HttpMethod.Post, P("/send"),
                    new StringContent(sb.ToString(), new UTF8Encoding(false), "text/plain"), ct);
                if (r.StatusCode == HttpStatusCode.Gone)
                {
                    Closed = true;
                    reply.Problem = "not delivered: " + ClosedText();
                    return reply;
                }
                if (r.StatusCode == HttpStatusCode.NotFound)
                {
                    Closed = true;
                    reply.Problem = "not delivered: live session " + Code + " is unknown or expired - live_disconnect, then live_connect to a listed one.";
                    return reply;
                }
                if (!r.IsSuccessStatusCode)
                {
                    reply.Problem = "not delivered: POST /send " + LiveSite.Status(r);
                    return reply;
                }
            }
            catch (HttpRequestException ex)
            {
                reply.Problem = "not delivered: POST /send failed - " + ex.Message;
                return reply;
            }

            MemoryStream got = new MemoryStream();
            Stopwatch sw = Stopwatch.StartNew();
            int failures = 0;
            string text = "";
            while (true)
            {
                // Wait no longer than the deadline that applies now (whole
                // seconds, rounded down: under 1 s left is a plain read).
                TimeSpan until = (reply.Delivered ? timeout : (readWithin < timeout ? readWithin : timeout)) - sw.Elapsed;
                int wait = Math.Max(0, Math.Min(LongPoll, (int)Math.Floor(until.TotalSeconds)));
                TimeSpan asked = sw.Elapsed;
                var (bytes, problem) = await ReadOut(ct, wait);
                if (problem != null)
                {
                    // A dropped request or two is the network, not the end;
                    // a 429 is the shared rate limit - back off (never past the deadline).
                    if (++failures >= 5) { reply.Problem = problem + " (5 times in a row)"; break; }
                    await Task.Delay(Shorter(TimeSpan.FromTicks(_backoff.Ticks * failures), until - (sw.Elapsed - asked)), ct);
                }
                else
                {
                    failures = 0;
                    if (bytes.Length == 0 && !Closed && sw.Elapsed - asked < _poll)
                        await Task.Delay(Shorter(_poll - (sw.Elapsed - asked), until - (sw.Elapsed - asked)), ct);
                    if (bytes.Length > 0)
                    {
                        got.Write(bytes, 0, bytes.Length);
                        reply.Delivered = true;
                        text = Encoding.UTF8.GetString(got.GetBuffer(), 0, (int)got.Length);
                    }
                    if (text.Contains(marker, StringComparison.Ordinal))
                    {
                        reply.Delivered = reply.Complete = true;
                        break;
                    }
                    if (Closed) { reply.Problem = ClosedText(); break; }
                }

                if (!reply.Delivered && sw.Elapsed > readWithin)
                {
                    reply.Problem = "not delivered in " + (int)readWithin.TotalSeconds +
                                    " s: the runner's game has not answered - it may be loading, frozen, or off the live " +
                                    "connection (live_status). The batch stays queued on the site.";
                    break;
                }
                if (sw.Elapsed > timeout)
                {
                    reply.Problem = "timed out after " + (int)timeout.TotalSeconds +
                                    " s - the rest still runs in the runner's game; later replies are skipped by the next call.";
                    break;
                }
            }

            if (Trimmed) text = "(the site dropped older output - the start of this reply may be missing)\n" + text;
            Remember(text);
            reply.Raw = text;
            reply.Transcript = BridgeText.Parse(text, marker);
            return reply;
        }

        /// The shorter of two spans, never below zero.
        private static TimeSpan Shorter(TimeSpan a, TimeSpan b)
        {
            TimeSpan t = a < b ? a : b;
            return t > TimeSpan.Zero ? t : TimeSpan.Zero;
        }

        private string ClosedText()
        {
            return "live session " + Code + " is closed (the runner ended it or it expired) - live_disconnect.";
        }

        /// GET /out?after=<offset>[&wait=<s>]: the new bytes, the offset
        /// moved on. `wait` > 0 long-polls until there is output or the
        /// session closes.
        private async Task<(byte[] bytes, string problem)> ReadOut(CancellationToken ct, int wait = 0)
        {
            try
            {
                string q = "/out?after=" + _offset.ToString(CultureInfo.InvariantCulture) +
                           (wait > 0 ? "&wait=" + wait.ToString(CultureInfo.InvariantCulture) : "");
                using HttpResponseMessage r = await Site.Call(HttpMethod.Get, P(q), null, ct);
                if (r.StatusCode == HttpStatusCode.NotFound)
                {
                    Closed = true;
                    return (new byte[0], null);
                }
                if (!r.IsSuccessStatusCode) return (null, "GET /out " + LiveSite.Status(r));
                byte[] bytes = await r.Content.ReadAsByteArrayAsync(ct);
                string off = Header(r, "X-Live-Offset");
                _offset = long.TryParse(off, NumberStyles.Integer, CultureInfo.InvariantCulture, out long o)
                    ? o : _offset + bytes.Length;
                if (Header(r, "X-Live-Trimmed") == "1") Trimmed = true;
                if (string.Equals(Header(r, "X-Live-State"), "closed", StringComparison.OrdinalIgnoreCase)) Closed = true;
                return (bytes, null);
            }
            catch (HttpRequestException ex)
            {
                return (null, "GET /out failed - " + ex.Message);
            }
        }

        private static string Header(HttpResponseMessage r, string name)
        {
            return r.Headers.TryGetValues(name, out IEnumerable<string> v) ? v.FirstOrDefault() : null;
        }

        /// A `shot <name>` picture the game uploaded; retried until it is
        /// there (the upload follows the shot) or `within` runs out.
        public async Task<(byte[] png, string problem)> FetchFile(string name, TimeSpan within, CancellationToken ct)
        {
            Stopwatch sw = Stopwatch.StartNew();
            string last = "not uploaded";
            while (true)
            {
                try
                {
                    using HttpResponseMessage r = await Site.Call(HttpMethod.Get, P("/file/" + Uri.EscapeDataString(name)), null, ct);
                    if (r.IsSuccessStatusCode)
                    {
                        byte[] png = await r.Content.ReadAsByteArrayAsync(ct);
                        if (IsPng(png)) return (png, null);
                        last = "not a PNG (" + png.Length + " bytes)";
                    }
                    else if (r.StatusCode != HttpStatusCode.NotFound) last = LiveSite.Status(r);
                }
                catch (HttpRequestException ex) { last = ex.Message; }
                if (sw.Elapsed > within)
                    return (null, "screenshot " + name + " never arrived from the runner's game in " +
                                  (int)within.TotalSeconds + " s (" + last + ")");
                await Task.Delay(1000, ct);   // ~10 tries: the shared rate limit
            }
        }

        private static bool IsPng(byte[] b)
        {
            return b != null && b.Length > 8 && b[0] == 0x89 && b[1] == (byte)'P' && b[2] == (byte)'N' && b[3] == (byte)'G';
        }

        /// POST /close: ends the session on the site (the runner's game stops polling).
        public async Task<string> Close(CancellationToken ct)
        {
            try
            {
                using HttpResponseMessage r = await Site.Call(HttpMethod.Post, P("/close"), null, ct);
                Closed = true;
                if (r.IsSuccessStatusCode || r.StatusCode == HttpStatusCode.NotFound) return null;
                return "POST /close " + LiveSite.Status(r);
            }
            catch (HttpRequestException ex) { return "POST /close failed - " + ex.Message; }
        }
    }
}
