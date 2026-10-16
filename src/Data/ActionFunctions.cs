using System;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // What an input DID in the game (T-0361, author 2026-10-16): the label of
    // a game-input tile is the function performed, named on its own. The game
    // acts on an action where its script READS it (Game/ActionReaders walks
    // the stack on a press and reports the reader's type and method); this
    // file is the pure table from reader to word, and from the player's
    // control FSM state to the attack words. Words are the builder's - the
    // author corrects them in game; an unknown reader is logged once so a
    // report zip grows the table.
    // ------------------------------------------------------------------
    public static class ActionFunctions
    {
        private sealed class Row
        {
            public string Type, Method, Action, Label;   // Type: simple name, or a "Namespace.*" prefix; Method / Action null = any
            public bool Generic;                         // a script that reads for many things: the player's FSM tells which
        }

        private static Row R(string type, string method, string action, string label)
        {
            Row r = new Row();
            r.Type = type; r.Method = method; r.Action = action; r.Label = label;
            return r;
        }

        private static Row G(string type)
        {
            Row r = new Row();
            r.Type = type; r.Generic = true;
            return r;
        }

        // Specific rows first; the first match wins.
        private static readonly Row[] Rows = new Row[]
        {
            R("PickUp", null, "Take", "Take"),
            R("ShelterTrigger", null, "Save", "Save"),
            R("ShelterTrigger", null, "Take", "Sleep"),
            R("LogHolder", null, "Craft", "Add log"),
            R("Fire2", "UpdateCooking", null, "Cook"),
            R("Fire2", "UpdateFueling", null, "Add fuel"),
            R("Fire2", "UpdateNotLit", null, "Light"),
            R("Fire", null, "Craft", "Cook"),
            R("MultiHolder", "LogContentUpdate", null, "Add log"),
            R("MultiHolder", "RockContentUpdate", null, "Add rock"),
            R("MultiHolder", "StickContentUpdate", null, "Add stick"),
            R("activateClimb", null, null, "Climb"),
            R("activateZipLine", "GrabZiplineUpdate", null, "Zipline"),
            R("Craft_Structure", null, "Build", "Build"),
            R("InventoryItemView", null, "Equip", "Equip"),
            R("InventoryItemView", null, "Combine", "Combine"),
            R("InventoryItemView", null, "Batch", "Batch"),
            R("FirstPersonCharacter", "GetCrouchInput", null, "Crouch"),
            R("FirstPersonCharacter", "GetStangUpInput", null, "Stand up"),
            R("FirstPersonCharacter", "GetJumpInput", null, "Jump"),
            R("FirstPersonCharacter", "Update", "Run", "Sprint"),
            R("BowController", null, "Fire1", "Shoot"),
            G("PlayerInventory"),
            G("weaponInfo"),
            G("HutongGames.PlayMaker.Actions.*"),
            G("playerAnimatorControl*"),
            G("CameraMouseEvents"),
        };

        /// "TheForest.Items.World.PickUp" -> "PickUp".
        public static string SimpleType(string full)
        {
            if (full == null) return "";
            int dot = full.LastIndexOf('.');
            return dot >= 0 ? full.Substring(dot + 1) : full;
        }

        /// The word for a reader (full type name, method) reading `action`. False when
        /// the table does not know it. A generic reader returns true with a null label.
        public static bool Lookup(string type, string method, string action, out string label)
        {
            label = null;
            string simple = SimpleType(type);
            for (int i = 0; i < Rows.Length; i++)
            {
                Row r = Rows[i];
                bool typeOk;
                if (r.Type.EndsWith("*", StringComparison.Ordinal))
                {
                    string prefix = r.Type.Substring(0, r.Type.Length - 1);
                    typeOk = type != null && type.StartsWith(prefix, StringComparison.Ordinal) || simple.StartsWith(prefix, StringComparison.Ordinal);
                }
                else typeOk = r.Type == simple;
                if (!typeOk) continue;
                if (r.Method != null && r.Method != method) continue;
                if (r.Action != null && r.Action != action) continue;
                label = r.Generic ? null : r.Label;
                return true;
            }
            return false;
        }

        // --- the player's control FSM (pmControl.ActiveStateName) -----------------------------

        public const int RankHeavy = 3, RankAttack = 2, RankOther = 1;

        /// The word for a control-FSM state and how strong it is (a heavy press that ends in
        /// waitForCombo still says Heavy attack). Null for any other state.
        public static string FsmLabel(string state, out int rank)
        {
            rank = 0;
            if (string.IsNullOrEmpty(state)) return null;
            switch (state)
            {
                case "initCharge":
                case "doCharge":
                case "overheadAttack":
                case "rockSmashAttack":
                case "axeFlyingSmash":
                    rank = RankHeavy; return "Heavy attack";
                case "doBlock":
                case "blockHit":
                    rank = RankHeavy; return "Block";
                case "startSpearThrow":
                case "throwSpear":
                    rank = RankOther; return "Throw";
                case "drawBow":
                    rank = RankOther; return "Draw bow";
                case "releaseBow":
                case "shootFlareGun":
                    rank = RankOther; return "Shoot";
            }
            if (state.StartsWith("axeSmashAttack", StringComparison.Ordinal)) { rank = RankHeavy; return "Heavy attack"; }
            if (state.StartsWith("stickAttack", StringComparison.Ordinal) ||
                state.StartsWith("axeAttack", StringComparison.Ordinal) ||
                state.StartsWith("normalAxeAttack", StringComparison.Ordinal) ||
                state.StartsWith("rockAttack", StringComparison.Ordinal) ||
                state.StartsWith("chooseRandomAttack", StringComparison.Ordinal) ||
                state.StartsWith("waitForCombo", StringComparison.Ordinal) ||
                state.StartsWith("axeWaitForCombo", StringComparison.Ordinal) ||
                state.StartsWith("axeCombo", StringComparison.Ordinal) ||
                state.StartsWith("genericAttack", StringComparison.Ordinal) ||
                state.StartsWith("chainSawAttack", StringComparison.Ordinal) ||
                state == "spearAttack" || state == "shellAttack" || state == "hangingAttack")
            { rank = RankAttack; return "Attack"; }
            return null;
        }

        // The actions the player controller reads every frame in the open world (game-notes "Who
        // performed an input", `input seen`) plus the two movement axes: held means performed.
        // Every other action is read only in context (Take, Save, Craft, Build, Batch, Equip,
        // Combine): held but unread, the game did nothing.
        private static readonly string[] Continuous =
        {
            "Vertical", "Horizontal", "Jump", "Run", "Crouch", "Fire1", "AltFire", "checkArms", "Drop",
            "Esc", "Inventory", "ItemSlot1", "Lighter", "SurvivalBook",
        };

        public static bool IsContinuous(string action)
        {
            return Array.IndexOf(Continuous, action) >= 0;
        }

        /// The attack and block actions: the player's control FSM tells which function.
        public static bool UsesControlState(string action)
        {
            return action == "Fire1" || action == "AltFire";
        }

        /// The word for a performed press. A reader that named it wins (strength 100); else the
        /// control FSM's word if stronger than what the press already showed (a heavy press that
        /// ends in waitForCombo keeps saying Heavy attack); else what the press already showed;
        /// else the fallback (null for an action whose function only the FSM can tell).
        public static string Pick(string reader, string fsm, int fsmRank, string current, int strength, string fallback, out int newStrength)
        {
            newStrength = strength;
            if (reader != null) { newStrength = 100; return reader; }
            if (strength < 100 && fsm != null && fsmRank > strength) { newStrength = fsmRank; return fsm; }
            return current ?? fallback;
        }

        /// The idle word of a key's actions: its functions' names, one a line, in pick order.
        public static string Idle(string[] words, int count)
        {
            string s = "";
            for (int i = 0; i < count; i++) s += (i > 0 ? "\n" : "") + words[i];
            return s;
        }
    }
}
