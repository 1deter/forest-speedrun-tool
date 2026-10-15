using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class GameLinesStateTests
    {
        [Fact]
        public void FlagsFirstThenTheMarkerLowest()
        {
            GameLinesState s = new GameLinesState();
            Assert.True(s.SetText("fast save loads\nno stagger", "god mode"));
            Assert.Equal(new[] { "fast save loads", "no stagger", "god mode" }, s.Lines.ToArray());
            Assert.Equal(2, s.MarkerStart);
        }

        [Fact]
        public void EmptyPartsAndATwoLineMarkerGiveOneLineEach()
        {
            GameLinesState s = new GameLinesState();
            s.SetText("", "ModAPI\nRUN MODE - not valid");
            Assert.Equal(new[] { "ModAPI", "RUN MODE - not valid" }, s.Lines.ToArray());
            s.SetText(null, null);
            Assert.Empty(s.Lines);
        }

        [Fact]
        public void TheSameTextChangesNothing()
        {
            GameLinesState s = new GameLinesState();
            s.SetText("a", "b");
            int v = s.Version;
            Assert.False(s.SetText("a", "b"));
            Assert.False(s.SetText(new string('a', 1), "b"));   // equal, another instance
            Assert.Equal(v, s.Version);
            Assert.True(s.SetText("a", "c"));
            Assert.Equal(v + 1, s.Version);
        }

        [Fact]
        public void TheGameDrawsOnlyWhenFoundAndOnScreen()
        {
            Assert.Equal(GameLinesMode.Game, GameLinesState.Decide(true, true));
            Assert.Equal(GameLinesMode.Overlay, GameLinesState.Decide(true, false));    // inventory, HUD off, cutscene
            Assert.Equal(GameLinesMode.Overlay, GameLinesState.Decide(false, true));    // a game update
        }

        [Fact]
        public void MissingMembersWarnOnce()
        {
            GameLinesState s = new GameLinesState();
            Assert.True(s.WarnMissing());
            Assert.False(s.WarnMissing());
            Assert.False(s.WarnMissing());
        }
    }
}
