using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // How an attempt started (T-0414 F4, D3).
    public class RunStartCheckTests
    {
        private static readonly string Sha = new string('a', 64);
        private static readonly string OtherSha = new string('b', 64);

        [Fact]
        public void AMatchingOrUnnamedStartStateIsSilent()
        {
            Assert.Null(RunStartCheck.StartStateFlag(Sha, "", Sha));
            Assert.Null(RunStartCheck.StartStateFlag(Sha, "", ""));
            Assert.Null(RunStartCheck.StartStateFlag("", "", null));
            Assert.Null(RunStartCheck.StartStateFlag(Sha, "9e3c0a1b", "9e3c0a1b"));   // a spot named the old way
        }

        [Fact]
        public void AnotherStartStateIsNamedWithBoth()
        {
            Assert.Equal("the run spot's start state is not the one the spot names (loaded " + Sha + ", the spot names " + OtherSha + ")",
                         RunStartCheck.StartStateFlag(Sha, "", OtherSha));
            Assert.Equal("the run spot's start state is not the one the spot names (loaded 9e3c0a1b, the spot names 12345678)",
                         RunStartCheck.StartStateFlag(Sha, "9e3c0a1b", "12345678"));
            Assert.Equal("the run spot's start state is not the one the spot names (loaded none, the spot names 12345678)",
                         RunStartCheck.StartStateFlag("", "", "12345678"));
        }

        [Fact]
        public void ARunByHandSaysItIsNotFromACategorySpot()
        {
            Assert.Equal("Any%, not from a category spot", RunStartCheck.ByHandLabel("Any%"));
            Assert.Equal("not from a category spot", RunStartCheck.ByHandLabel(""));
        }

        [Fact]
        public void PracticeBeforeStartingByHandIsAFlag()
        {
            Assert.Null(RunStartCheck.ByHandAfterPracticeFlag(""));
            Assert.Null(RunStartCheck.ByHandAfterPracticeFlag(null));
            Assert.Equal("run mode was started by hand after practice (savestate restore), not from a category spot",
                         RunStartCheck.ByHandAfterPracticeFlag("savestate restore"));
        }
    }
}
