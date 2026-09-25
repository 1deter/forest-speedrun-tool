using System.Collections.Generic;
using ForestOverlay.BridgeMcp;
using Xunit;

namespace ForestOverlay.Tests
{
    // The MCP server's text side (tools/BridgeMcp): quoting for the
    // bridge's tokenizer, reading out.txt back, and the log search.
    // Transcripts are the shapes BridgeModule really writes.
    public class BridgeMcpTests
    {
        [Fact]
        public void Parse_splits_blocks_and_keeps_order()
        {
            string raw =
                "> #17 type OverlayPlugin all\n" +
                "#-88  BepInEx_Manager  (0, 0, 0)  1030.8 m\n" +
                "1 object(s), nearest first\n" +
                "< #17 ok (22 ms)\n" +
                "hi\n" +
                "> #18 get #-88 OverlayPlugin._host._modules[99].TabTitle\n" +
                "< #18 error: OverlayPlugin._host._modules[99]: index 99 past the end (15) (0 ms)\n" +
                "__mcp_done_x\n";
            BridgeText.Transcript t = BridgeText.Parse(raw, "__mcp_done_x");

            Assert.Equal(2, t.Blocks.Count);
            Assert.Equal("type OverlayPlugin all", t.Blocks[0].Command);
            Assert.Equal(2, t.Blocks[0].Lines.Count);
            Assert.True(t.Blocks[0].Ok);
            Assert.Equal("22 ms", t.Blocks[0].Took);

            // The error text keeps its own parentheses; the time is the last group.
            Assert.Equal("OverlayPlugin._host._modules[99]: index 99 past the end (15)", t.Blocks[1].Error);
            Assert.Equal("0 ms", t.Blocks[1].Took);
            Assert.Equal(1, t.Errors);

            Assert.Equal(new[] { "hi" }, t.Other);
            Assert.Equal(3, t.Items.Count);
            Assert.Same(t.Blocks[0], t.Items[0]);
            Assert.Equal("hi", t.Items[1]);
        }

        [Fact]
        public void Parse_waiting_command_stays_open_until_closed()
        {
            string raw =
                "> #5 restore test-v1\n" +
                "restoring 'test-v1' in place\n" +
                "< #5 ok (0.85 s)\n" +
                "> #6 wait 30\n";
            BridgeText.Transcript t = BridgeText.Parse(raw, null);
            Assert.True(t.Blocks[0].Ok);
            Assert.Single(t.Blocks[0].Lines);
            Assert.False(t.Blocks[1].Closed);
            Assert.False(t.Blocks[1].Ok);
        }

        [Fact]
        public void Parse_close_from_an_earlier_batch_is_kept_as_other()
        {
            BridgeText.Transcript t = BridgeText.Parse("< #3 ok (12.00 s)\n> #4 status\n< #4 ok (0 ms)\n", null);
            Assert.Single(t.Blocks);
            Assert.Equal(new[] { "< #3 ok (12.00 s)" }, t.Other);
        }

        [Fact]
        public void Arg_quotes_spaces_and_strips_what_the_tokenizer_cannot_hold()
        {
            Assert.Equal("player", BridgeText.Arg("player"));
            Assert.Equal("\"SMASH NOW\"", BridgeText.Arg("SMASH NOW"));
            Assert.Equal("\"\"", BridgeText.Arg(""));
            Assert.Equal("\"say 'hi' twice\"", BridgeText.Arg("say \"hi\"\ntwice"));
            Assert.Equal("call BepInEx_Manager OverlayPlugin._notice.Show \"Retest in 10 s\" 8",
                         BridgeText.Command("call", "BepInEx_Manager", "OverlayPlugin._notice.Show", "Retest in 10 s", "8"));
            Assert.Equal("restore test", BridgeText.Command("restore", "test", null));
        }

        [Fact]
        public void Value_reads_get_lines()
        {
            Assert.Equal("15", BridgeText.Value("OverlayPlugin._host.Count = 15  (Int32)"));
            Assert.Equal("Run info", BridgeText.Value("OverlayPlugin._host._modules[3].TabTitle = \"Run info\"  (String)"));
            Assert.Equal("(817.85, 91.49, 620.63)", BridgeText.Value("Transform.position = (817.85, 91.49, 620.63)  (Vector3)"));
            Assert.Null(BridgeText.Value("1 object(s), nearest first"));
        }

        [Fact]
        public void EstimateSeconds_adds_waits_and_slow_commands()
        {
            Assert.Equal(30, BridgeText.EstimateSeconds(new[] { "status" }));
            Assert.Equal(42.5, BridgeText.EstimateSeconds(new[] { "wait 10", "shot a", "wait 0.5" }));
            Assert.Equal(180, BridgeText.EstimateSeconds(new[] { "restore x load" }));
        }

        [Fact]
        public void BannerVersion_takes_the_newest()
        {
            string text = "== bridge on, ForestOverlay v0.24.57, 2026-09-25 10:00:00 ==\n> #1 status\n" +
                          "== bridge on, ForestOverlay v0.24.58, 2026-09-25 18:03:40 ==\n";
            Assert.Equal("0.24.58", BridgeText.BannerVersion(text));
            Assert.Null(BridgeText.BannerVersion("nothing"));
        }

        [Fact]
        public void Grep_numbers_lines_merges_context_and_keeps_the_newest()
        {
            List<string> lines = new List<string> { "a", "Bridge #1: x", "b", "Bridge #2: y", "c", "d", "e", "Bridge #3: z" };
            int n;
            string one = LogSearch.Grep(lines, "bridge #[0-9]+", 1, 10, out n);
            Assert.Equal(3, n);
            Assert.Equal("1- a\n2: Bridge #1: x\n3- b\n4: Bridge #2: y\n5- c\n--\n7- e\n8: Bridge #3: z\n", one);

            string newest = LogSearch.Grep(lines, "Bridge", 0, 1, out n);
            Assert.Equal("8: Bridge #3: z\n", newest);

            // Not a regex: searched literally.
            Assert.Equal("", LogSearch.Grep(lines, "Bridge #(", 0, 5, out n));
            Assert.Equal(0, n);
        }

        [Fact]
        public void Tail_takes_the_last_lines()
        {
            Assert.Equal("2- b\n3- c\n", LogSearch.Tail(new List<string> { "a", "b", "c" }, 2));
        }
    }
}
