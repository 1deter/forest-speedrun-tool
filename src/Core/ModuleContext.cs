using BepInEx.Logging;
using ForestOverlay.Game;

namespace ForestOverlay.Core
{
    // Shared services handed to every module. Modules never reach for
    // globals or for each other - anything cross-cutting arrives here,
    // which is what keeps a new module a single self-contained file.
    public sealed class ModuleContext
    {
        public ManualLogSource Log;
        public GameBridge Bridge;
        public InventoryReader Inventory;
        public PlayerRef Player;
        public PracticeState Practice;
        public string ConfigDirectory;
    }
}
