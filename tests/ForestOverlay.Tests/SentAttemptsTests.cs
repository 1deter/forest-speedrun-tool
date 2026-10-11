using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // The Runs tab's recent attempts against the site (T-0144): a wrong
    // call here drops a runner's attempt from the list while it is still on
    // the site, or keeps asking the site about it.
    // ------------------------------------------------------------------
    public class SentAttemptsTests
    {
        [Fact]
        public void OnlyTheSites404_DropsIt_OnlyA200_IsNotAskedAgain()
        {
            const string gone = "{\"error\":\"no such attempt\"}";
            Assert.Equal(SentCheck.There, SentAttempts.Meaning(200, "{\"id\":\"a-1\",\"verdict\":\"green\"}"));
            Assert.Equal(SentCheck.There, SentAttempts.Meaning(200, null));
            Assert.Equal(SentCheck.Gone, SentAttempts.Meaning(404, gone));
            foreach (long code in new long[] { 0, 204, 400, 401, 403, 410, 429, 500, 502, 503 })
                Assert.Equal(SentCheck.Unknown, SentAttempts.Meaning(code, gone));

            // A 404 that is not the site's (a wrong Url, a proxy, a page):
            // the attempt stays listed.
            Assert.Equal(SentCheck.Unknown, SentAttempts.Meaning(404, null));
            Assert.Equal(SentCheck.Unknown, SentAttempts.Meaning(404, ""));
            Assert.Equal(SentCheck.Unknown, SentAttempts.Meaning(404, "<html>Not Found</html>"));
            Assert.Equal(SentCheck.Unknown, SentAttempts.Meaning(404, "{\"error\":\"no such spot\"}"));
        }

        [Fact]
        public void ExistsUrl_NamesEachId_Escaped()
        {
            Assert.Equal("https://x.y/api/attempts/exists?ids=a-1,a-2",
                         SentAttempts.ExistsUrl("https://x.y/ ", new List<string> { "a-1", "a-2" }));
            // A broken sent.txt line cannot split into two ids or another query.
            Assert.Equal("https://x.y/api/attempts/exists?ids=a%2Cb%26c%3D1",
                         SentAttempts.ExistsUrl("https://x.y", new List<string> { "a,b&c=1" }));
        }

        [Fact]
        public void ExistsMeaning_ReadsTheSitesLists_A404_IsAnOldSite()
        {
            // T-0266: the site's light answer (site/ForestSite/Program.cs, pinned by AttemptTests).
            var asked = new List<string> { "a-1", "a-2", "a-3" };
            bool old;
            Assert.Equal(new[] { SentCheck.There, SentCheck.Gone, SentCheck.Unknown },
                         SentAttempts.ExistsMeaning(200, "{\"there\":[\"a-1\"],\"gone\":[\"a-2\"]}", asked, out old));
            Assert.False(old);
            Assert.Equal(new[] { SentCheck.Gone, SentCheck.There, SentCheck.Gone },
                         SentAttempts.ExistsMeaning(200, "{ \"there\" : [ \"a-2\" ], \"gone\": [\"a-1\", \"a-3\"] }", asked, out old));
            Assert.Equal(new[] { SentCheck.There, SentCheck.There, SentCheck.There },
                         SentAttempts.ExistsMeaning(200, "{\"there\":[\"a-1\",\"a-2\",\"a-3\"],\"gone\":[]}", asked, out old));
            // A prefix is not the id.
            Assert.Equal(SentCheck.Unknown,
                         SentAttempts.ExistsMeaning(200, "{\"there\":[\"a-12\"],\"gone\":[]}", new List<string> { "a-1" }, out old)[0]);

            // A site from before the check: its /attempts/{id} route answers.
            Assert.Equal(new[] { SentCheck.Unknown, SentCheck.Unknown, SentCheck.Unknown },
                         SentAttempts.ExistsMeaning(404, "{\"error\":\"no such attempt\"}", asked, out old));
            Assert.True(old);

            // Anything else - not the site's lists - leaves every one listed.
            foreach (string body in new[] { null, "", "<html>ok</html>", "{\"there\":[\"a-1\"]}", "{\"gone\":[\"a-2\"]}",
                                            "{\"there\":\"a-1\",\"gone\":\"a-2\"}" })
            {
                Assert.All(SentAttempts.ExistsMeaning(200, body, asked, out old), m => Assert.Equal(SentCheck.Unknown, m));
                Assert.False(old);
            }
            foreach (long code in new long[] { 0, 400, 401, 403, 429, 500, 502, 503 })
            {
                Assert.All(SentAttempts.ExistsMeaning(code, "{\"there\":[],\"gone\":[\"a-1\"]}", asked, out old),
                           m => Assert.Equal(SentCheck.Unknown, m));
                Assert.False(old);
            }
            Assert.Empty(SentAttempts.ExistsMeaning(200, "{\"there\":[],\"gone\":[]}", null, out old));
        }

        [Fact]
        public void ToCheck_SkipsWaiting_There_AndAskedSinceTheTabOpened()
        {
            var listed = new List<string> { "a-w1", "a-1", "a-2", "a-3", "a-4" };
            var there = new HashSet<string> { "a-2" };
            var asked = new HashSet<string> { "a-3" };

            // a-w1 is still in the outbox: the site has no log of it yet.
            Assert.Equal(new[] { "a-1", "a-4" }, SentAttempts.ToCheck(listed, 1, there, asked));
            Assert.Equal(new[] { "a-w1", "a-1", "a-4" }, SentAttempts.ToCheck(listed, 0, there, asked));
            Assert.Equal(listed, SentAttempts.ToCheck(listed, 0, null, null));

            // All waiting, all known, nothing listed.
            Assert.Empty(SentAttempts.ToCheck(listed, 5, there, asked));
            Assert.Empty(SentAttempts.ToCheck(listed, 9, there, asked));
            Assert.Empty(SentAttempts.ToCheck(listed, 0, new HashSet<string>(listed), asked));
            Assert.Empty(SentAttempts.ToCheck(new List<string>(), 0, there, asked));
            Assert.Empty(SentAttempts.ToCheck(null, 0, there, asked));
        }

        [Fact]
        public void ToCheck_AsksEachIdOnce()
        {
            var listed = new List<string> { "a-1", "", null, "a-1", "a-2" };
            Assert.Equal(new[] { "a-1", "a-2" }, SentAttempts.ToCheck(listed, 0, null, null));
        }

        [Fact]
        public void Without_DropsEveryLineOfTheId_KeepsTheRestAsItIs()
        {
            string text = "a-1|green|2026-10-07 10:00\n" +
                          "a-2|amber|2026-10-07 10:05\n" +
                          "a-12|red|2026-10-07 10:06\n" +
                          "a-2|refused|2026-10-07 10:09\n" +
                          "a-3|green|2026-10-07 10:10\n";
            Assert.Equal("a-1|green|2026-10-07 10:00\n" +
                         "a-12|red|2026-10-07 10:06\n" +
                         "a-3|green|2026-10-07 10:10\n", SentAttempts.Without(text, "a-2"));

            // The last line, with no line end; Windows line ends kept.
            Assert.Equal("a-1|green|x\r\n", SentAttempts.Without("a-1|green|x\r\na-3|amber|y", "a-3"));
            Assert.Equal("a-3|amber|y", SentAttempts.Without("a-1|green|x\r\na-3|amber|y", "a-1"));

            // The only line: an empty file.
            Assert.Equal("", SentAttempts.Without("a-1|green|x\n", "a-1"));

            // A line of a damaged file is kept; its id is everything up to the bar.
            Assert.Equal("junk\n\n", SentAttempts.Without("junk\na-1|green|x\n\n", "a-1"));
        }

        [Fact]
        public void Without_NothingToRewrite_IsNull()
        {
            Assert.Null(SentAttempts.Without("a-1|green|x\n", "a-2"));
            Assert.Null(SentAttempts.Without("a-12|green|x\n", "a-1"));   // a prefix is not the id
            Assert.Null(SentAttempts.Without("", "a-1"));
            Assert.Null(SentAttempts.Without(null, "a-1"));
            Assert.Null(SentAttempts.Without("a-1|green|x\n", ""));
            Assert.Null(SentAttempts.Without("a-1|green|x\n", null));
        }
    }
}
