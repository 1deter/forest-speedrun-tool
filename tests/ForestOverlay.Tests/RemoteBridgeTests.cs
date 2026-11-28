using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ForestOverlay.BridgeMcp;
using Xunit;

namespace ForestOverlay.Tests
{
    // The MCP server's remote transport (T-0491) against a fake of the
    // site's live relay: /send queues lines, a "game" answers each in the
    // out.txt shape on the next /out poll, /file serves an uploaded PNG.
    public class RemoteBridgeTests
    {
        private static readonly byte[] Png = { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 0, 0, 0, 13 };

        private sealed class FakeRelay : HttpMessageHandler
        {
            public readonly List<string> Paths = new List<string>();
            public readonly List<string> Tokens = new List<string>();
            public readonly StringBuilder Out = new StringBuilder("== bridge on, ForestOverlay v0.24.402, polling the site\n");
            public readonly Queue<string> Pending = new Queue<string>();
            public int FileMissesLeft = 2;
            public bool Closed, AnswerNothing, Trim;
            private int _n;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
            {
                string path = req.RequestUri.PathAndQuery;
                lock (Paths) Paths.Add(req.Method + " " + path);
                Tokens.Add(req.Headers.TryGetValues("X-Admin-Token", out var t) ? t.First() : null);
                if (path == "/api/admin/live")
                    return Text("[{\"code\":\"AB12\",\"opened\":\"2026-11-28T10:00:00Z\",\"lastCommand\":null,\"outBytes\":60}]",
                                "application/json");
                if (!path.StartsWith("/api/admin/live/AB12/")) return new HttpResponseMessage(HttpStatusCode.NotFound);
                string tail = path.Substring("/api/admin/live/AB12/".Length);

                if (tail == "send")
                {
                    foreach (string l in (await req.Content.ReadAsStringAsync(ct)).Split('\n'))
                        if (l.Length > 0) Pending.Enqueue(l);
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
                if (tail.StartsWith("out?after="))
                {
                    // The game answers what was queued before this poll.
                    while (!AnswerNothing && Pending.Count > 0) Answer(Pending.Dequeue());
                    byte[] all = Encoding.UTF8.GetBytes(Out.ToString());
                    int after = Math.Min(int.Parse(tail.Substring("out?after=".Length).Split('&')[0]), all.Length);
                    HttpResponseMessage r = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(all, after, all.Length - after),
                    };
                    r.Headers.Add("X-Live-Offset", all.Length.ToString());
                    r.Headers.Add("X-Live-State", Closed ? "closed" : "open");
                    if (Trim) r.Headers.Add("X-Live-Trimmed", "1");
                    return r;
                }
                if (tail == "file/shot1.png")
                {
                    if (FileMissesLeft-- > 0) return new HttpResponseMessage(HttpStatusCode.NotFound);
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) };
                }
                if (tail == "close") { Closed = true; return new HttpResponseMessage(HttpStatusCode.NoContent); }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            private void Answer(string line)
            {
                // `echo` writes its text alone, as BridgeModule does.
                if (line.StartsWith("echo ")) { Out.Append(line.Substring(5)).Append('\n'); return; }
                int n = ++_n;
                Out.Append("> #").Append(n).Append(' ').Append(line).Append('\n');
                if (line == "status") Out.Append("scene: ForestMain, practice: no, savestates idle\n");
                else if (line.StartsWith("shot ")) Out.Append("saved ").Append(line.Substring(5)).Append(".png\n");
                Out.Append("< #").Append(n).Append(" ok (1 ms)\n");
            }

            private static HttpResponseMessage Text(string body, string mime)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, mime) };
            }
        }

        private static (RemoteBridge bridge, FakeRelay relay) Make()
        {
            FakeRelay relay = new FakeRelay();
            LiveSite site = new LiveSite("http://fake.test/", "secret-token", new HttpClient(relay));
            return (new RemoteBridge(site, "AB12", TimeSpan.FromMilliseconds(10)), relay);
        }

        [Fact]
        public async Task Lists_the_open_sessions_with_the_token()
        {
            var (bridge, relay) = Make();
            var (list, problem) = await bridge.Site.List(CancellationToken.None);
            Assert.Null(problem);
            Assert.Equal("AB12", Assert.Single(list).Code);
            Assert.Equal(60, list[0].OutBytes);
            Assert.All(relay.Tokens, t => Assert.Equal("secret-token", t));
        }

        [Fact]
        public async Task A_status_round_trip_reads_the_reply_after_the_old_output()
        {
            var (bridge, relay) = Make();
            Assert.Null(await bridge.Prime(CancellationToken.None));
            Assert.Equal("0.24.402", bridge.PluginVersionFromBanner());

            BridgeReply r = await bridge.Send(new[] { "status" }, TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.True(r.Complete, r.Problem);
            Assert.True(r.Delivered);
            BridgeText.Block b = Assert.Single(r.Transcript.Blocks);
            Assert.Equal("status", b.Command);
            Assert.Equal(new[] { "scene: ForestMain, practice: no, savestates idle" }, b.Lines);
            Assert.DoesNotContain("bridge on", r.Raw);   // the banner was before this batch

            Assert.Contains(relay.Paths, p => p == "POST /api/admin/live/AB12/send");
            // The reply is long-polled (wait=), not read on a fast timer.
            Assert.Contains(relay.Paths, p => p.StartsWith("GET /api/admin/live/AB12/out?after=") && p.Contains("&wait="));
            Assert.DoesNotContain(relay.Paths, p => p.Contains("&wait=") && p.Contains("after=0&"));
        }

        [Fact]
        public async Task A_screenshot_is_fetched_once_uploaded()
        {
            var (bridge, relay) = Make();
            await bridge.Prime(CancellationToken.None);
            BridgeReply r = await bridge.Send(new[] { "shot shot1" }, TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.True(r.Complete, r.Problem);
            var (png, problem) = await bridge.FetchFile("shot1.png", TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.Null(problem);
            Assert.Equal(Png, png);
            Assert.Equal(3, relay.Paths.Count(p => p.EndsWith("/file/shot1.png")));   // two 404s, then the PNG
        }

        [Fact]
        public async Task A_missing_screenshot_times_out_with_a_reason()
        {
            var (bridge, relay) = Make();
            relay.FileMissesLeft = int.MaxValue;
            var (png, problem) = await bridge.FetchFile("shot1.png", TimeSpan.FromMilliseconds(600), CancellationToken.None);
            Assert.Null(png);
            Assert.Contains("never arrived", problem);
        }

        [Fact]
        public async Task A_closed_session_ends_the_wait()
        {
            var (bridge, relay) = Make();
            await bridge.Prime(CancellationToken.None);
            relay.AnswerNothing = true;
            relay.Closed = true;
            BridgeReply r = await bridge.Send(new[] { "status" }, TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.False(r.Complete);
            Assert.Contains("closed", r.Problem);
            Assert.True(bridge.Closed);

            // Later calls do not post at all.
            int posts = relay.Paths.Count(p => p.EndsWith("/send"));
            BridgeReply again = await bridge.Send(new[] { "status" }, TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.Contains("closed", again.Problem);
            Assert.Equal(posts, relay.Paths.Count(p => p.EndsWith("/send")));
        }

        [Fact]
        public async Task An_unanswered_batch_is_reported_not_delivered()
        {
            var (bridge, relay) = Make();
            await bridge.Prime(CancellationToken.None);
            relay.AnswerNothing = true;
            BridgeReply r = await bridge.Send(new[] { "status" }, TimeSpan.FromSeconds(5), CancellationToken.None,
                                              TimeSpan.FromMilliseconds(200));
            Assert.False(r.Delivered);
            Assert.StartsWith("not delivered", r.Problem);
        }

        [Fact]
        public async Task An_unknown_code_is_not_delivered()
        {
            FakeRelay relay = new FakeRelay();
            LiveSite site = new LiveSite("http://fake.test", "t", new HttpClient(relay));
            RemoteBridge bridge = new RemoteBridge(site, "ZZ99", TimeSpan.FromMilliseconds(10));
            BridgeReply r = await bridge.Send(new[] { "status" }, TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.False(r.Delivered);
            Assert.True(bridge.Closed);
        }

        [Fact]
        public async Task Close_posts_close()
        {
            var (bridge, relay) = Make();
            Assert.Null(await bridge.Close(CancellationToken.None));
            Assert.True(relay.Closed);
            Assert.True(bridge.Closed);
        }
    }
}
