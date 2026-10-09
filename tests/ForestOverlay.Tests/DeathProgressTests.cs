using System;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class DeathProgressTests
    {
        [Fact]
        public void NothingShowing_IsNoDeath()
        {
            Assert.Equal("", DeathProgress.Reason(false, false, false, false, null, false));
            Assert.Equal("", DeathProgress.Reason(false, false, false, false, "", false));
        }

        [Fact]
        public void EachSign_IsADeath()
        {
            Assert.Equal("dead", DeathProgress.Reason(true, false, false, false, null, false));
            Assert.Equal("hanging in the cave", DeathProgress.Reason(false, true, false, false, null, false));
            Assert.Equal("drag-away", DeathProgress.Reason(false, false, true, false, null, false));
            Assert.Equal("death view", DeathProgress.Reason(false, false, false, true, null, false));
            Assert.Equal("GameOver pending", DeathProgress.Reason(false, false, false, false, "GameOver", false));
            Assert.Equal("dead cam", DeathProgress.Reason(false, false, false, false, null, true));
        }

        [Fact]
        public void Dead_NamedFirst()
        {
            Assert.Equal("dead", DeathProgress.Reason(true, true, true, true, "KillPlayer", true));
        }

        // Queued outside a death: the intro's knock-out, a new game's
        // Start, every hit. A restore then must not end a "death".
        [Theory]
        [InlineData("CutSceneWake")]
        [InlineData("CutSceneBlackToMorning")]
        [InlineData("CheckArmsStart")]
        [InlineData("ResetHit")]
        [InlineData("PlayWakeMusic")]
        public void OrdinaryInvokes_AreNotSigns(string name)
        {
            Assert.DoesNotContain(name, DeathProgress.Signs);
        }

        [Fact]
        public void EverySign_IsCancelled()
        {
            foreach (string s in DeathProgress.Signs) Assert.Contains(s, DeathProgress.Cancelled);
            Assert.DoesNotContain("ResetHit", DeathProgress.Cancelled);
            Assert.DoesNotContain("CutSceneWake", DeathProgress.Cancelled);
        }
    }
}
