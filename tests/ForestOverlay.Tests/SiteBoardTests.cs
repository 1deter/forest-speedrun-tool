using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // The website's board of each runner's best (other runners' PBs as
    // comparisons in game). The site writes it with the same file.
    // ------------------------------------------------------------------
    public class SiteBoardTests
    {
        private static BoardEntry Entry(long id, string runner, string name, float duration, params float[] splits)
        {
            return new BoardEntry { RunId = id, RunnerId = runner, Name = name, Duration = duration, Splits = splits };
        }

        [Fact]
        public void RoundTrip_KeepsOrderNamesAndUnknownSplits()
        {
            var board = new List<BoardEntry>
            {
                Entry(12, "r-00000000000000aa", "maks | the runner", 9.25f, 4.5f, 9.25f),
                Entry(7, "r-00000000000000bb", "sx", 9.5f, float.NaN, 9.5f),
            };
            List<BoardEntry> back = SiteBoard.Parse(SiteBoard.Write(board));

            Assert.Equal(2, back.Count);
            Assert.Equal(12, back[0].RunId);
            Assert.Equal("r-00000000000000aa", back[0].RunnerId);
            Assert.Equal("maks | the runner", back[0].Name);
            Assert.Equal(9.25f, back[0].Duration, 3);
            Assert.Equal(new[] { 4.5f, 9.25f }, back[0].Splits);
            Assert.True(float.IsNaN(back[1].Splits[0]));
            Assert.Equal(9.5f, back[1].Splits[1], 3);
        }

        [Fact]
        public void Parse_RefusesWhatIsNotABoard()
        {
            Assert.Null(SiteBoard.Parse(null));
            Assert.Null(SiteBoard.Parse("{\"title\":\"Not Found\"}"));
            Assert.Null(SiteBoard.Parse("forest-board 2\npb|1|r-a|1|1|x\n"));
            Assert.Empty(SiteBoard.Parse("forest-board 1\r\n"));
            // A broken line is skipped, not fatal.
            Assert.Single(SiteBoard.Parse("forest-board 1\npb|x|r-a|1|1|bad id\npb|3|r-b|2|2|ok\n"));
        }

        [Fact]
        public void Others_LeavesOutTheRunnerAndOtherRowCounts()
        {
            var board = new List<BoardEntry>
            {
                Entry(1, "r-me", "me", 8f, 3f, 8f),
                Entry(2, "r-a", "a", 9f, 4f, 9f),
                Entry(3, "r-b", "b", 9f, 9f),   // another checkpoint count
            };
            List<BoardEntry> others = SiteBoard.Others(board, "r-me", 2);
            Assert.Single(others);
            Assert.Equal("r-a", others[0].RunnerId);
        }

        [Fact]
        public void Url_EscapesIdAndRoute()
        {
            Assert.Equal("https://x.test/api/spots/s-1/ab%2Bc%3D/board.txt", SiteBoard.Url("https://x.test/", "s-1", "ab+c="));
            Assert.Equal("https://x.test/api/runs/42/file", SiteBoard.RunFileUrl("https://x.test", 42));
        }
    }
}
