using System.Collections.Generic;
using ForestOverlay.BridgeMcp;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class NoticeCommandTests
    {
        private static bool Parse(string[] args, out float seconds, out string text)
        {
            return BridgeCommand.TryParseNotice(new List<string>(args), out seconds, out text);
        }

        [Fact]
        public void TextOnly_Uses8Seconds()
        {
            float s; string t;
            Assert.True(Parse(new[] { "hello" }, out s, out t));
            Assert.Equal(8f, s); Assert.Equal("hello", t);
        }

        [Fact]
        public void SecondsThenText_AreClamped()
        {
            float s; string t;
            Assert.True(Parse(new[] { "12", "go", "now" }, out s, out t));
            Assert.Equal(12f, s); Assert.Equal("go now", t);
            Assert.True(Parse(new[] { "99", "x" }, out s, out t)); Assert.Equal(30f, s);
            Assert.True(Parse(new[] { "0", "x" }, out s, out t)); Assert.Equal(1f, s);
        }

        [Fact]
        public void NoArguments_OrOnlySeconds_IsUsage()
        {
            float s; string t;
            Assert.False(Parse(new string[0], out s, out t));
            Assert.True(Parse(new[] { "5" }, out s, out t));   // a lone number is the text
            Assert.Equal("5", t);
        }

        [Fact]
        public void LongText_CutAt300()
        {
            float s; string t;
            Assert.True(Parse(new[] { new string('a', 400) }, out s, out t));
            Assert.Equal(300, t.Length);
        }

        [Fact]
        public void McpNotice_SendsTheCommand()
        {
            Assert.Equal("notice 8 \"Retest in 10 s\"", BridgeText.Command("notice", "8", "Retest in 10 s"));
        }
    }
}
