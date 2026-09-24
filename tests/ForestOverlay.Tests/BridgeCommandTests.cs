using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // The test bridge's command lines are typed by hand from outside the
    // game; a misparse must be an error line, never a different command.
    // ------------------------------------------------------------------
    public class BridgeCommandTests
    {
        [Fact]
        public void TokensSplitOnSpacesAndKeepQuotes()
        {
            List<string> t = BridgeCommand.Tokenize("  find \"mutant male\"  50\tall ");
            Assert.Equal(new[] { "find", "mutant male", "50", "all" }, t.ToArray());
        }

        [Fact]
        public void EmptyQuotesAreAToken()
        {
            Assert.Equal(new[] { "echo", "" }, BridgeCommand.Tokenize("echo \"\"").ToArray());
        }

        [Fact]
        public void CommentsAndBlankLinesAreNothing()
        {
            Assert.Empty(BridgeCommand.Tokenize(""));
            Assert.Empty(BridgeCommand.Tokenize("   "));
            Assert.Empty(BridgeCommand.Tokenize("# a note"));
            Assert.Empty(BridgeCommand.Tokenize("#"));
            Assert.Empty(BridgeCommand.Tokenize("// a note"));
        }

        [Fact]
        public void AHandleIsNotAComment()
        {
            Assert.Equal(new[] { "#-1234", "x" }, BridgeCommand.Tokenize("#-1234 x").ToArray());
        }

        [Fact]
        public void PathWithIndexes()
        {
            List<BridgeCommand.Step> steps = new List<BridgeCommand.Step>();
            string err;
            Assert.True(BridgeCommand.TryParsePath("PlayerStats._logs[2].name", steps, out err));
            Assert.Null(err);
            Assert.Equal(3, steps.Count);
            Assert.Equal("PlayerStats", steps[0].Name);
            Assert.Equal(-1, steps[0].Index);
            Assert.Equal("_logs", steps[1].Name);
            Assert.Equal(2, steps[1].Index);
            Assert.Equal("name", steps[2].Name);
        }

        [Theory]
        [InlineData("")]
        [InlineData("a..b")]
        [InlineData("a[1")]
        [InlineData("a[x]")]
        [InlineData("a[-1]")]
        [InlineData("[0]")]
        public void BadPathsAreRefused(string path)
        {
            string err;
            Assert.False(BridgeCommand.TryParsePath(path, new List<BridgeCommand.Step>(), out err));
            Assert.NotNull(err);
        }

        [Fact]
        public void VectorsParseInvariant()
        {
            Vector3 v;
            Assert.True(BridgeCommand.TryParseVector3("(1.5, -2,3e1)", out v));
            Assert.Equal(1.5f, v.x);
            Assert.Equal(-2f, v.y);
            Assert.Equal(30f, v.z);
            Assert.False(BridgeCommand.TryParseVector3("1,2", out v));
            Assert.False(BridgeCommand.TryParseVector3("1,2,x", out v));

            Vector2 w;
            Assert.True(BridgeCommand.TryParseVector2("3,4", out w));
            Assert.Equal(4f, w.y);
        }

        [Theory]
        [InlineData("true", true)]
        [InlineData("ON", true)]
        [InlineData("1", true)]
        [InlineData("off", false)]
        [InlineData("False", false)]
        public void Bools(string text, bool expected)
        {
            bool b;
            Assert.True(BridgeCommand.TryParseBool(text, out b));
            Assert.Equal(expected, b);
        }

        [Fact]
        public void OptionsAndFlagsAreTakenOut()
        {
            List<string> t = BridgeCommand.Tokenize("_Dummy 100 ALL max=5");
            Assert.True(BridgeCommand.TakeFlag(t, "all"));
            Assert.Equal("5", BridgeCommand.TakeOption(t, "max"));
            Assert.Equal(new[] { "_Dummy", "100" }, t.ToArray());
            Assert.False(BridgeCommand.TakeFlag(t, "all"));
            Assert.Null(BridgeCommand.TakeOption(t, "max"));
        }

        [Fact]
        public void OneLineCutsAndFlattens()
        {
            Assert.Equal("a b", BridgeCommand.OneLine("a\r\nb", 10));
            Assert.Equal("abc...(5 chars)", BridgeCommand.OneLine("abcde", 3));
            Assert.Equal("", BridgeCommand.OneLine(null, 3));
        }
    }
}
