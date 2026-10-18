namespace ForestOverlay.Game
{
    // Pure tables for LogStore (linked into the tests): which game methods
    // have their log reads rewritten, and to what. Type, method name.
    public static class LogStoreTables
    {
        /// Read the STORE (holders, repairs): HolderAmount / HolderHasLogs.
        public static readonly string[][] Holder =
        {
            new[] { "LogHolder", "Update" },
            new[] { "TheForest.Buildings.World.MultiHolder", "LogContentUpdate" },
            new[] { "TheForest.Buildings.World.MultiHolder", "GrabEnter" },
            new[] { "TheForest.Buildings.World.RepairTool", "Update" },
            new[] { "TheForest.Buildings.World.RepairTool", "ValidateCollider" },
            new[] { "TheForest.Buildings.World.RepairTool", "OnGrabberEnter" },
            new[] { "TheForest.Buildings.World.RepairTool", "OnGrabberExit" },
            new[] { "TheForest.Buildings.World.RepairTool", "GrabExit" },
            new[] { "TheForest.Buildings.World.BuildingRepair", "Update" },
            new[] { "TheForest.Buildings.World.BuildingRepair", "OnTriggerEnter" },
        };

        /// Read EMPTY ARMS (HasLogs false, Amount 0). The getter patch alone
        /// misses callers the JIT compiled (inlined the getter) before it
        /// (T-0431). Every overload of a name is rewritten.
        public static readonly string[][] Arms =
        {
            new[] { "TheForest.Items.Inventory.PlayerInventory", "Equip" },
            new[] { "TheForest.Items.Inventory.PlayerInventory", "HideAllEquiped" },
            new[] { "TheForest.Items.Inventory.PlayerInventory", "HideRightHand" },
            new[] { "TheForest.Items.Inventory.PlayerInventory", "AddItemNF" },
            new[] { "TheForest.Items.Inventory.PlayerInventory", "RefreshDropIcon" },
            new[] { "activateZipLine", "GrabEnter" },
            new[] { "activateZipLine", "Update" },
            new[] { "playerZipLineAction", "EnterZipLine" },
            new[] { "mutantPickUp", "Update" },
            new[] { "playerAnimatorControl", "Update" },
            new[] { "playerAnimatorControl", "disableSwimming" },
            new[] { "TheForest.Player.Actions.PlayerClimbRopeAction", "enterClimbRopeTop" },
            new[] { "PlayerHangGliderAction", "Update" },
            new[] { "TheForest.Player.Actions.PlayerSitAction", "SitOnBench" },
            new[] { "playerCraneClimbAction", "EnterCraneClimb" },
            new[] { "PlayerStats", "FallDownDead" },
            new[] { "TheForest.Player.Actions.PlayerEatMeatAction+<eatMeatRoutine>c__Iterator0", "MoveNext" },
            new[] { "TheForest.Player.Actions.PlayerSkinAnimalAction+<skinAnimalRoutine>c__Iterator0", "MoveNext" },
            new[] { "playerEnterCaveAction+<doCave>c__Iterator0", "MoveNext" },
            new[] { "TheForest.Buildings.World.MultiHolder", "BodyContentUpdate" },
            new[] { "TheForest.Buildings.World.MultiHolder", "BodyContentUpdate_MP" },
            new[] { "PlayerStats", "GetTired" },
            new[] { "PlayerStats+CaloriesSystemData", "Refresh" },
        };
    }
}
