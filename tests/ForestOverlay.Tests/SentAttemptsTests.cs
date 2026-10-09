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
