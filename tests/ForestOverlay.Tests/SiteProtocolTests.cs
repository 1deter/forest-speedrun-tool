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
        [Fact]
        public void StartStateResend_AddsOnlyTheRoutesOwnState()
        {
            var state = new SavestateFile { Name = "boost", Level = "TheForest", Data = "level data" };
            var seg = new Segment { Id = "s-0123456789ab", Name = "Boost" };
            seg.StartState = Segment.HashText(state.Data);
            var b = new SegmentBundle { Segment = seg, Exported = "now", PluginVersion = "test" };
            b.Attempts.Add("duration = 1");
            string why;

            string again = SiteProtocol.StartStateResend(b.Write(), state.Write(), out why);
            Assert.Null(why);
            SegmentBundle sent = SegmentBundle.Parse(again, out why, null);
            Assert.Equal(state.Write(), sent.StartState);
            Assert.Single(sent.Attempts);

            // Sent once already: never again (the site refused it).
            Assert.Null(SiteProtocol.StartStateResend(again, state.Write(), out why));
            Assert.Contains("did not keep", why);

            // No state here, or another one.
            Assert.Null(SiteProtocol.StartStateResend(b.Write(), null, out why));
            var other = new SavestateFile { Name = "boost", Level = "TheForest", Data = "other data" };
            Assert.Null(SiteProtocol.StartStateResend(b.Write(), other.Write(), out why));
            Assert.Contains("not the one", why);

            // A teleport-only spot has none to send.
            seg.StartState = "";
            Assert.Null(SiteProtocol.StartStateResend(b.Write(), state.Write(), out why));
        }

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
        public void Number_ReadsAWholeNumber()
        {
            Assert.Equal(12, SiteProtocol.Number("{\"id\":12,\"replaced\":false}", "id"));
            Assert.Equal(7, SiteProtocol.Number("{\"id\": 7}", "id"));
            Assert.Equal(-1, SiteProtocol.Number("{\"error\":\"x\"}", "id"));
            Assert.Equal(-1, SiteProtocol.Number("{\"id\":\"s-1\"}", "id"));
        }

        [Fact]
        public void SubmitRefusal_OldIdsAndAttempts()
        {
            Assert.Null(SiteProtocol.SubmitRefusal("s-0123456789ab", false));
            Assert.Null(SiteProtocol.SubmitRefusal("s-0123456789abcdef0123456789abcdef", false));
            Assert.Contains("Duplicate", SiteProtocol.SubmitRefusal("spot.my.new-spot-3", false));
            Assert.Contains("Duplicate", SiteProtocol.SubmitRefusal("s-splitstest01", false));
            Assert.Contains("Duplicate", SiteProtocol.SubmitRefusal("", false));
            Assert.Contains("no times", SiteProtocol.SubmitRefusal("s-0123456789ab", true));
        }

        [Fact]
        public void DeleteSpot_UrlAndMessages()
        {
            Assert.Equal("https://forest.deter.cloud/api/spots/s-0123456789ab",
                         SiteProtocol.DeleteSpotUrl("https://forest.deter.cloud/", "s-0123456789ab"));
            Assert.StartsWith("Deleted from the website with 3 runs.", SiteProtocol.DeleteSpotMessage(200, "{\"runs\":3}", null));
            Assert.StartsWith("Deleted from the website with 1 run.", SiteProtocol.DeleteSpotMessage(200, "{\"runs\":1}", null));
            Assert.Equal("Not deleted: not your spot.", SiteProtocol.DeleteSpotMessage(403, "{\"error\":\"not your spot\"}", null));
            Assert.Equal("Not deleted: 2 runs by other runners are on it.",
                         SiteProtocol.DeleteSpotMessage(409, "{\"error\":\"2 runs by other runners are on it\"}", null));
            Assert.StartsWith("Not on the website", SiteProtocol.DeleteSpotMessage(404, "{}", null));
            Assert.Contains("token", SiteProtocol.DeleteSpotMessage(401, null, null));
            Assert.Contains("timed out", SiteProtocol.DeleteSpotMessage(0, null, "timed out"));
            Assert.Contains("HTTP 502", SiteProtocol.DeleteSpotMessage(502, "bad gateway", null));
        }

        [Fact]
        public void SpotUrl()
        {
            Assert.Equal("https://forest.deter.cloud/spot/s-0123456789ab",
                         SiteProtocol.SpotUrl("https://forest.deter.cloud/ ", "s-0123456789ab"));
        }
    }
}
