using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // What the plugin does with the website's answers: a wrong call here
    // either loses a runner's run (deleted as "done" when it was not) or
    // retries a refused file forever.
    // ------------------------------------------------------------------
    public class SiteProtocolTests
    {
        [Theory]
        [InlineData(200, UploadOutcome.Done)]
        [InlineData(400, UploadOutcome.Refused)]
        [InlineData(413, UploadOutcome.Refused)]
        [InlineData(422, UploadOutcome.Refused)]
        [InlineData(401, UploadOutcome.TokenBad)]
        [InlineData(0, UploadOutcome.RetryLater)]
        [InlineData(429, UploadOutcome.RetryLater)]
        [InlineData(502, UploadOutcome.RetryLater)]
        [InlineData(404, UploadOutcome.RetryLater)]
        public void Classify(long code, UploadOutcome expected)
        {
            Assert.Equal(expected, SiteProtocol.Classify(code));
        }

        [Fact]
        public void RetryDelay_DoublesToFifteenMinutes()
        {
            Assert.Equal(30f, SiteProtocol.RetryDelay(1));
            Assert.Equal(60f, SiteProtocol.RetryDelay(2));
            Assert.Equal(120f, SiteProtocol.RetryDelay(3));
            Assert.Equal(900f, SiteProtocol.RetryDelay(50));
        }

        [Fact]
        public void Field_ReadsTheSitesAnswers()
        {
            Assert.Equal("ft_ab12", SiteProtocol.Field("{\"token\":\"ft_ab12\"}", "token"));
            Assert.Equal("a \"b\"\nc", SiteProtocol.Field("{\"error\": \"a \\\"b\\\"\\nc\"}", "error"));
            Assert.Null(SiteProtocol.Field("{\"other\":\"x\"}", "token"));
            Assert.Null(SiteProtocol.Field(null, "token"));
        }

        [Fact]
        public void Ids_ReadsArrays()
        {
            string json = "{\"added\":[12,13],\"existing\":[],\"skipped\":[\"attempt 2: x\"]}";
            Assert.Equal(new long[] { 12, 13 }, SiteProtocol.Ids(json, "added").ToArray());
            Assert.Empty(SiteProtocol.Ids(json, "existing"));
        }

        [Fact]
        public void RegisterBody_EscapesTheName()
        {
            Assert.Equal("{\"runner\":\"r-0123456789abcdef\",\"name\":\"d\\\"eter\\\\\"}",
                         SiteProtocol.RegisterBody("r-0123456789abcdef", "d\"eter\\"));
        }

        [Fact]
        public void RunnerOf_FindsTheRunnerLine()
        {
            string id, name;
            Assert.True(SiteProtocol.RunnerOf("anchor|s-1\nduration|4.000\nrunner|r-aa|deter\ns|0|1|2|3|4\n", out id, out name));
            Assert.Equal("r-aa", id);
            Assert.Equal("deter", name);
            Assert.False(SiteProtocol.RunnerOf("anchor|s-1\ns|0|1|2|3|4\nrunner|r-late|x\n", out id, out name));
        }

        [Fact]
        public void SpotUrl()
        {
            Assert.Equal("https://forest.deter.cloud/#/spot/s-0123456789ab",
                         SiteProtocol.SpotUrl("https://forest.deter.cloud/ ", "s-0123456789ab"));
        }
    }
}
