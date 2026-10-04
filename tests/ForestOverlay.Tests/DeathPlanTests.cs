using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class DeathPlanTests
    {
        // A loaded game, Reload save on death on with both extra toggles
        // (the defaults), no spot, practice mode off, no run.
        private static DeathSituation Base()
        {
            DeathSituation s = new DeathSituation();
            s.Kind = DeathCase.Real;
            s.ReloadOn = true;
            s.ReloadOnCapture = true;
            s.ReloadInBoss = true;
            s.SlotKnown = true;
            return s;
        }

        private static DeathSituation WithSpot(bool startState, bool full = false)
        {
            DeathSituation s = Base();
            s.HasSpot = true;
            s.SpotName = "Cave 6 drop";
            s.HasStartState = startState;
            s.FullLoad = full;
            return s;
        }

        private static DeathOutcome Out(DeathChoice c, DeathSituation s) { return DeathPlan.Decide(c, s).Outcome; }

        // ---- Automatic = the rules from before the choice ----

        [Fact]
        public void AutomaticRestartsASpotWithAStartStateEvenWithPracticeModeOff()
        {
            DeathDecision d = DeathPlan.Decide(DeathChoice.Automatic, WithSpot(true));
            Assert.Equal(DeathOutcome.RestartSpot, d.Outcome);
            Assert.Equal("restarts 'Cave 6 drop' (Quick load of its start state) - Automatic: it is the current spot and has a start state", d.Text);
            Assert.Contains("(Full load", DeathPlan.Decide(DeathChoice.Automatic, WithSpot(true, true)).Text);
        }

        [Fact]
        public void AutomaticRevivesAtThePlainSpotOnlyInPracticeMode()
        {
            DeathSituation s = WithSpot(false);
            DeathDecision d = DeathPlan.Decide(DeathChoice.Automatic, s);
            Assert.Equal(DeathOutcome.ReloadSave, d.Outcome);
            Assert.Contains("practice mode is off and 'Cave 6 drop' has no start state", d.Text);

            s.PracticeOn = true;
            d = DeathPlan.Decide(DeathChoice.Automatic, s);
            Assert.Equal(DeathOutcome.RestartSpot, d.Outcome);
            Assert.StartsWith("revives you at 'Cave 6 drop' (health back, teleport)", d.Text);
        }

        [Fact]
        public void AutomaticWithNoSpotReloadsOrDiesAsTheToggleSays()
        {
            DeathSituation s = Base();
            s.PracticeOn = true;
            Assert.Equal("reloads your save (the game's own load) - Reload save on death is on",
                         DeathPlan.Decide(DeathChoice.Automatic, s).Text);
            s.ReloadOn = false;
            DeathDecision d = DeathPlan.Decide(DeathChoice.Automatic, s);
            Assert.Equal(DeathOutcome.GameDeath, d.Outcome);
            Assert.EndsWith("Reload save on death is off", d.Text);
        }

        [Fact]
        public void ReloadKeepsTheFirstDeathBossAndPermadeathRules()
        {
            DeathSituation s = Base();
            s.Kind = DeathCase.Capture;
            Assert.Equal(DeathOutcome.ReloadSave, Out(DeathChoice.Automatic, s));
            s.ReloadOnCapture = false;
            DeathDecision d = DeathPlan.Decide(DeathChoice.ReloadSave, s);
            Assert.Equal(DeathOutcome.GameDeath, d.Outcome);
            Assert.StartsWith("the game's own death: you are captured", d.Text);

            s = Base();
            s.Kind = DeathCase.BossWake;
            s.ReloadInBoss = false;
            Assert.Equal(DeathOutcome.GameDeath, Out(DeathChoice.Automatic, s));

            s = Base();
            s.Kind = DeathCase.PermaDeath;
            Assert.Equal(DeathOutcome.GameDeath, Out(DeathChoice.ReloadSave, s));

            s = Base();
            s.SlotKnown = false;
            Assert.Contains("no save slot", DeathPlan.Decide(DeathChoice.ReloadSave, s).Text);
        }

        [Fact]
        public void AStartStateSpotStillRevivesOnAFirstDeathAsBefore()
        {
            DeathSituation s = WithSpot(true);
            s.Kind = DeathCase.Capture;
            s.ReloadOnCapture = false;
            Assert.Equal(DeathOutcome.RestartSpot, Out(DeathChoice.Automatic, s));
        }

        [Fact]
        public void MultiplayerIsNeverTouched()
        {
            DeathSituation s = WithSpot(true);
            s.Kind = DeathCase.Multiplayer;
            foreach (DeathChoice c in new[] { DeathChoice.Automatic, DeathChoice.ReloadSave, DeathChoice.RestartSpot, DeathChoice.ReviveAtSpot })
                Assert.Equal(DeathOutcome.GameDeath, Out(c, s));
        }

        // ---- explicit choices ----

        [Fact]
        public void ReloadTheSaveIgnoresTheMainToggleAndAnySpot()
        {
            DeathSituation s = WithSpot(true);
            s.PracticeOn = true;
            s.ReloadOn = false;
            DeathDecision d = DeathPlan.Decide(DeathChoice.ReloadSave, s);
            Assert.Equal(DeathOutcome.ReloadSave, d.Outcome);
            Assert.EndsWith("you chose Reload the save", d.Text);
        }

        [Fact]
        public void RestartTheSpotWithoutPracticeModeAndWithoutAStartState()
        {
            DeathDecision d = DeathPlan.Decide(DeathChoice.RestartSpot, WithSpot(false));
            Assert.Equal(DeathOutcome.RestartSpot, d.Outcome);
            Assert.Contains("it has no start state, so only a teleport", d.Text);
        }

        [Fact]
        public void ReviveAtTheSpotNeverRestoresAndSaysSo()
        {
            DeathDecision d = DeathPlan.Decide(DeathChoice.ReviveAtSpot, WithSpot(true));
            Assert.Equal(DeathOutcome.ReviveAtSpot, d.Outcome);
            Assert.Equal("revives you at 'Cave 6 drop' (health back, teleport only, start state not restored) - you chose Revive at the current spot", d.Text);
        }

        [Fact]
        public void ASpotChoiceWithNoSpotSaysWhatHappensInstead()
        {
            DeathSituation s = Base();
            DeathDecision d = DeathPlan.Decide(DeathChoice.RestartSpot, s);
            Assert.Equal(DeathOutcome.ReloadSave, d.Outcome);
            Assert.Equal("reloads your save (the game's own load) - no current spot to restart (Go to or save one in Practice); Reload save on death is on", d.Text);

            s.ReloadOn = false;
            d = DeathPlan.Decide(DeathChoice.ReviveAtSpot, s);
            Assert.Equal(DeathOutcome.GameDeath, d.Outcome);
            Assert.Contains("no current spot to revive at", d.Text);
        }

        [Fact]
        public void TheGamesOwnDeathDoesNothing()
        {
            DeathSituation s = WithSpot(true);
            s.PracticeOn = true;
            DeathDecision d = DeathPlan.Decide(DeathChoice.GameDeath, s);
            Assert.Equal(DeathOutcome.GameDeath, d.Outcome);
            Assert.Equal("the game's own death (dead cam, then the menu) - you chose The game's own death", d.Text);
        }

        // ---- run mode ----

        [Fact]
        public void RunModeLocksTheReviveAndFallsBackToTheReload()
        {
            DeathSituation s = WithSpot(true);
            s.PracticeOn = true;
            s.ReviveLocked = true;
            s.RestartLocked = true;
            s.GoLocked = true;

            DeathDecision d = DeathPlan.Decide(DeathChoice.Automatic, s);
            Assert.Equal(DeathOutcome.ReloadSave, d.Outcome);
            Assert.Contains("run mode locks the practice revive", d.Text);

            Assert.Equal(DeathOutcome.ReloadSave, Out(DeathChoice.RestartSpot, s));
            Assert.Contains("run mode locks restarting", DeathPlan.Decide(DeathChoice.RestartSpot, s).Text);
            Assert.Equal(DeathOutcome.ReloadSave, Out(DeathChoice.ReviveAtSpot, s));
        }

        [Fact]
        public void ACategoryAllowingTheReviveStillNeedsRestartOrGo()
        {
            DeathSituation s = WithSpot(true);
            s.RestartLocked = true;   // not the run's own spot
            Assert.Equal(DeathOutcome.ReloadSave, Out(DeathChoice.Automatic, s));
            Assert.Equal(DeathOutcome.ReviveAtSpot, Out(DeathChoice.ReviveAtSpot, s));
            s.GoLocked = true;
            Assert.Equal(DeathOutcome.ReloadSave, Out(DeathChoice.ReviveAtSpot, s));
        }

        [Fact]
        public void RunModeLockedOrForcedReload()
        {
            DeathSituation s = Base();
            s.ReloadLocked = true;
            DeathDecision d = DeathPlan.Decide(DeathChoice.ReloadSave, s);
            Assert.Equal(DeathOutcome.GameDeath, d.Outcome);
            Assert.Contains("the run's category locks Reload save on death", d.Text);

            s = Base();
            s.ReloadOn = false;
            s.ReloadForced = true;
            Assert.Equal(DeathOutcome.ReloadSave, Out(DeathChoice.Automatic, s));
            d = DeathPlan.Decide(DeathChoice.GameDeath, s);
            Assert.Equal(DeathOutcome.ReloadSave, d.Outcome);
            Assert.Contains("forces Reload save on death", d.Text);
        }

        [Fact]
        public void ReloadInPlaceSaysSoExceptInARun()
        {
            DeathSituation s = Base();
            s.ReloadInPlace = true;
            Assert.Equal("reloads your save in place (a Quick load of the slot's save; marks practice) - Reload save on death is on",
                         DeathPlan.Decide(DeathChoice.Automatic, s).Text);
            s.RunActive = true;
            DeathDecision d = DeathPlan.Decide(DeathChoice.Automatic, s);
            Assert.Equal(DeathOutcome.ReloadSave, d.Outcome);
            Assert.StartsWith("reloads your save (the game's own load - run mode never reloads in place)", d.Text);
        }

        // Slot 1's case (2026-10-04): saved in the lab, reloaded in the lab.
        private static InPlaceCheck InLab()
        {
            InPlaceCheck c = new InPlaceCheck();
            c.SlotRead = true;
            c.FlagsKnown = true;
            c.SaveInEndgame = true;
            c.LiveInEndgame = true;
            c.EndgameLoaded = true;
            return c;
        }

        [Fact]
        public void InPlaceAppliesOnTheSameSideOfTheVaultDoor()
        {
            Assert.Null(DeathPlan.InPlaceRefusal(InLab()));
            InPlaceCheck c = new InPlaceCheck();
            c.SlotRead = true;
            c.FlagsKnown = true;
            Assert.Null(DeathPlan.InPlaceRefusal(c));   // surface save, surface death, no lab loaded
            c.EndgameLoaded = true;
            Assert.Null(DeathPlan.InPlaceRefusal(c));   // a lab left loaded does not matter outside
        }

        [Fact]
        public void InPlaceRefusesTheEndgameOnOneSideOnly()
        {
            // The bridge test: a tp out of the lab, then the lab's save in
            // place - the lab was unloaded and IsInEndgame stayed false.
            InPlaceCheck c = InLab();
            c.LiveInEndgame = false;
            c.EndgameLoaded = false;
            Assert.Contains("the save is in the endgame and you are not", DeathPlan.InPlaceRefusal(c));
            c = InLab();
            c.EndgameLoaded = false;
            Assert.Contains("the lab is not loaded", DeathPlan.InPlaceRefusal(c));
            c = InLab();
            c.SaveInEndgame = false;
            Assert.Contains("you are in the endgame and the save is not", DeathPlan.InPlaceRefusal(c));
        }

        [Fact]
        public void InPlaceRefusesRunModeFirstThenWhatItCannotRead()
        {
            InPlaceCheck c = InLab();
            c.RunActive = true;
            c.SlotRead = false;
            Assert.Equal("run mode: a run reloads only with the game's own load", DeathPlan.InPlaceRefusal(c));
            c = InLab();
            c.Busy = true;
            Assert.Equal("a savestate action is still running", DeathPlan.InPlaceRefusal(c));
            c = InLab();
            c.SlotRead = false;
            c.ReadError = "the slot has no save";
            Assert.Equal("the slot has no save", DeathPlan.InPlaceRefusal(c));
            c = InLab();
            c.FlagsKnown = false;
            Assert.Contains("could not be read", DeathPlan.InPlaceRefusal(c));
        }
    }
}
