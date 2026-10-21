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
            Assert.True(s.SetText("fast save loads\nno stagger", "god mode", false));
            Assert.Equal(new[] { "fast save loads", "no stagger", "god mode" }, s.Lines.ToArray());
            Assert.Equal(2, s.MarkerStart);
        }

        [Fact]
        public void EmptyPartsAndATwoLineMarkerGiveOneLineEach()
        {
            GameLinesState s = new GameLinesState();
            s.SetText("", "ModAPI\nRUN MODE - not valid", false);
            Assert.Equal(new[] { "ModAPI", "RUN MODE - not valid" }, s.Lines.ToArray());
            s.SetText(null, null, false);
            Assert.Empty(s.Lines);
        }

        [Fact]
        public void ALongRunLineWrapsAtSpacesAndStaysTheMarkers()
        {
            GameLinesState s = new GameLinesState();
            s.SetText("fast save loads", "RUN MODE - attempt 12 (test label here) v1.2 - the test bridge is on - flagged", true);
            Assert.Equal(new[] { "fast save loads", "RUN MODE - attempt 12 (test label here) v1.2", "- the test bridge is on - flagged" }, s.Lines.ToArray());
            Assert.Equal(1, s.MarkerStart);
            foreach (string l in s.Lines) Assert.True(l.Length <= GameLinesState.MaxLineChars + 4);
            Assert.True(s.LineWarns(2));
        }

        [Fact]
        public void TheSameTextChangesNothing()
        {
            GameLinesState s = new GameLinesState();
            s.SetText("a", "b", false);
            int v = s.Version;
            Assert.False(s.SetText("a", "b", false));
            Assert.False(s.SetText(new string('a', 1), "b", false));   // equal, another instance
            Assert.Equal(v, s.Version);
            Assert.True(s.SetText("a", "c", false));
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

        [Fact]
        public void TheWarningColoursTheMarkerLinesOnly()
        {
            GameLinesState s = new GameLinesState();
            s.SetText("god mode", "RUN MODE - not valid", false);
            int v = s.Version;
            Assert.False(s.LineWarns(1));
            Assert.True(s.SetText("god mode", "RUN MODE - not valid", true));   // the colour alone changed
            Assert.Equal(v + 1, s.Version);
            Assert.False(s.LineWarns(0));   // a flag
            Assert.True(s.LineWarns(1));    // the marker
        }

        [Fact]
        public void MissingMembersAreNamed()
        {
            string[] names = { "A.a", "B.b", "C.c" };
            Assert.Equal("", GameLinesState.MissingMembers(names, new[] { true, true, true }));
            Assert.Equal("A.a", GameLinesState.MissingMembers(names, new[] { false, true, true }));
            Assert.Equal("B.b, C.c", GameLinesState.MissingMembers(names, new[] { true, false, false }));
            Assert.Equal("A.a, B.b, C.c", GameLinesState.MissingMembers(names, new[] { false, false, false }));
        }

        [Fact]
        public void ThreeFailuresInARowGiveUp()
        {
            GameLinesState s = new GameLinesState();
            Assert.False(s.GiveUpAfterFailure(10f));
            Assert.False(s.GiveUpAfterFailure(20f));
            Assert.True(s.GiveUpAfterFailure(30f));
        }

        [Fact]
        public void AQuietSpellStartsTheCountAgain()
        {
            GameLinesState s = new GameLinesState();
            Assert.False(s.GiveUpAfterFailure(0f));
            Assert.False(s.GiveUpAfterFailure(5f));
            // A minute without one: a long session's scene changes never add up.
            Assert.False(s.GiveUpAfterFailure(5f + GameLinesState.QuietSeconds));
            Assert.Equal(1, s.Failures);
            Assert.False(s.GiveUpAfterFailure(70f));
            Assert.True(s.GiveUpAfterFailure(71f));
        }
    }
}
