using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class ActionFunctionsTests
    {
        private static string Word(string type, string method, string action, out bool known)
        {
            string label;
            known = ActionFunctions.Lookup(type, method, action, out label);
            return label;
        }

        [Fact]
        public void Readers_NameTheFunction()
        {
            bool known;
            Assert.Equal("Take", Word("TheForest.Items.World.PickUp", "GetTakeInputDown", "Take", out known));
            Assert.Equal("Save", Word("TheForest.Buildings.World.ShelterTrigger", "Update", "Save", out known));
            Assert.Equal("Sleep", Word("ShelterTrigger", "Update", "Take", out known));
            Assert.Equal("Crouch", Word("FirstPersonCharacter", "GetCrouchInput", "Crouch", out known));
            Assert.Equal("Stand up", Word("FirstPersonCharacter", "GetStangUpInput", "Crouch", out known));
            Assert.Equal("Sprint", Word("FirstPersonCharacter", "Update", "Run", out known));
            Assert.True(known);
        }

        [Fact]
        public void SpecificBeatsGenericOnAType()
        {
            bool known;
            Assert.Equal("Shoot", Word("BowController", "Update", "Fire1", out known));
            Assert.Null(Word("TheForest.Items.Inventory.PlayerInventory", "Update", "Fire1", out known));
            Assert.True(known);   // generic: known, no word of its own (the player's FSM tells)
        }

        [Fact]
        public void GenericPrefixes()
        {
            bool known;
            Assert.Null(Word("HutongGames.PlayMaker.Actions.GetButtonUp", "OnUpdate", "Fire1", out known));
            Assert.True(known);
            Assert.Null(Word("playerAnimatorControl", "checkChargeConditions", "Fire1", out known));
            Assert.True(known);
        }

        [Fact]
        public void UnknownReader_IsNotKnown()
        {
            bool known;
            Assert.Null(Word("Some.New.Script", "Update", "Take", out known));
            Assert.False(known);
            Word("PickUp", "Update", "Craft", out known);   // right script, an action it does not read here
            Assert.False(known);
        }

        [Fact]
        public void Fsm_AttackHeavyBlock()
        {
            int rank;
            Assert.Equal("Attack", ActionFunctions.FsmLabel("stickAttack", out rank));
            Assert.Equal(ActionFunctions.RankAttack, rank);
            Assert.Equal("Attack", ActionFunctions.FsmLabel("waitForCombo 2", out rank));
            Assert.Equal("Heavy attack", ActionFunctions.FsmLabel("initCharge", out rank));
            Assert.Equal(ActionFunctions.RankHeavy, rank);
            Assert.Equal("Heavy attack", ActionFunctions.FsmLabel("doCharge", out rank));
            Assert.Equal("Heavy attack", ActionFunctions.FsmLabel("axeSmashAttack 2", out rank));
            Assert.Equal("Block", ActionFunctions.FsmLabel("doBlock", out rank));
            Assert.Null(ActionFunctions.FsmLabel("waitForInput", out rank));
            Assert.Equal(0, rank);
            Assert.Null(ActionFunctions.FsmLabel("", out rank));
        }

        [Fact]
        public void Idle_IsTheFunctionsInPickOrder()
        {
            Assert.Equal("Crouch\nBatch", ActionFunctions.Idle(new[] { "Crouch", "Batch" }, 2));
            Assert.Equal("Take\nBuild", ActionFunctions.Idle(new[] { "Take", "Build" }, 2));
            Assert.Equal("Attack", ActionFunctions.Idle(new[] { "Attack", "Equip" }, 1));
        }

        [Fact]
        public void Pick_ReaderBeatsFsmBeatsFallback()
        {
            int st;
            Assert.Equal("Shoot", ActionFunctions.Pick("Shoot", "Attack", 2, null, 0, "Attack", out st));
            Assert.Equal(100, st);
            Assert.Equal("Attack", ActionFunctions.Pick(null, "Attack", 2, null, 0, null, out st));
            Assert.Equal(2, st);
            Assert.Equal("Take", ActionFunctions.Pick(null, null, 0, null, 0, "Take", out st));
            Assert.Equal(0, st);
        }

        [Fact]
        public void Pick_StrongestStateOfThePressStays()
        {
            int st;
            // heavy, then the press ends in waitForCombo (Attack, weaker): still Heavy attack
            string w = ActionFunctions.Pick(null, "Heavy attack", ActionFunctions.RankHeavy, null, 0, null, out st);
            w = ActionFunctions.Pick(null, "Attack", ActionFunctions.RankAttack, w, st, null, out st);
            Assert.Equal("Heavy attack", w);
            Assert.Equal(ActionFunctions.RankHeavy, st);
            // a reader's word is never replaced by the FSM
            Assert.Equal("Crouch", ActionFunctions.Pick(null, "Block", ActionFunctions.RankHeavy, "Crouch", 100, null, out st));
        }

        [Fact]
        public void Pick_NothingForAnActionOnlyTheFsmCanTell()
        {
            int st;
            Assert.Null(ActionFunctions.Pick(null, null, 0, null, 0, null, out st));
        }

        [Fact]
        public void Continuous_OnlyWhatTheControllerReadsEveryFrame()
        {
            foreach (string a in new[] { "Vertical", "Horizontal", "Jump", "Run", "Crouch", "Fire1", "AltFire" })
                Assert.True(ActionFunctions.IsContinuous(a), a);
            foreach (string a in new[] { "Take", "Save", "Craft", "Build", "Batch", "Equip", "Combine" })
                Assert.False(ActionFunctions.IsContinuous(a), a);
            Assert.True(ActionFunctions.UsesControlState("Fire1"));
            Assert.False(ActionFunctions.UsesControlState("Take"));
        }
    }
}
