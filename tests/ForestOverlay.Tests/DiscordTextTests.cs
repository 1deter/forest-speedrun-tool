using System.Collections.Generic;
using System.Text;
using ForestOverlay.BridgeMcp;
using Xunit;

namespace ForestOverlay.Tests
{
    // Splitting a long QA post into Discord messages (tools/BridgeMcp).
    public class DiscordTextTests
    {
        [Fact]
        public void Short_text_is_one_message()
        {
            Assert.Equal(new[] { "hello" }, DiscordText.Split("hello"));
            Assert.Empty(DiscordText.Split(""));
        }

        [Fact]
        public void Splits_at_line_breaks_within_the_limit()
        {
            List<string> parts = DiscordText.Split("aaaa\nbbbb\ncccc", 9);
            Assert.Equal(new[] { "aaaa\nbbbb", "cccc" }, parts);
        }

        [Fact]
        public void A_code_block_cut_in_two_is_closed_and_reopened()
        {
            StringBuilder sb = new StringBuilder("QA list v1\n```txt\n");
            for (int i = 1; i <= 30; i++) sb.Append(i).Append(") check item number ").Append(i).Append('\n');
            sb.Append("```\nthanks");
            List<string> parts = DiscordText.Split(sb.ToString(), 200);

            Assert.True(parts.Count > 2);
            foreach (string p in parts)
            {
                Assert.True(p.Length <= 200, p.Length + " chars");
                // Every message has balanced fences.
                Assert.Equal(0, Count(p, "```") % 2);
            }
            Assert.StartsWith("```txt\n", parts[1]);
            Assert.EndsWith("thanks", parts[parts.Count - 1]);

            // Nothing lost: joined back without the added fences, it is the input.
            string joined = string.Join("\n", parts).Replace("\n```\n```txt", "");
            Assert.Equal(sb.ToString(), joined);
        }

        [Fact]
        public void An_overlong_line_is_cut_hard()
        {
            List<string> parts = DiscordText.Split(new string('x', 25), 10);
            Assert.Equal(new[] { "xxxxxxxxxx", "xxxxxxxxxx", "xxxxx" }, parts);
        }

        private static int Count(string s, string what)
        {
            int n = 0;
            for (int i = s.IndexOf(what); i >= 0; i = s.IndexOf(what, i + what.Length)) n++;
            return n;
        }
    }
}
