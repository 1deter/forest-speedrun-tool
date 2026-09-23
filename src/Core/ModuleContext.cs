using BepInEx.Configuration;
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
        public ConfigFile Config;
        public GameBridge Bridge;
        public InventoryReader Inventory;
        public PlayerStateReader PlayerState;
        public PlayerRef Player;
        public PracticeState Practice;

        /// A few seconds of text on screen, for when no panel is open.
        public Notice Notice;

        /// Named game events (endgame cutscenes etc.) seen by Harmony
        /// postfixes. Read-only log; consumers keep their own position.
        public GameEvents Events;
        public string ConfigDirectory;

        /// The plugin behaviour, for modules that need to start a
        /// coroutine (the update check is the only one so far).
        public UnityEngine.MonoBehaviour Runner;

        /// Absolute path of the loaded ForestOverlay.dll, used to stage an
        /// update beside it.
        public string PluginPath;
    }
}
