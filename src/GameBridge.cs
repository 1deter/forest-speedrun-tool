using System;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay
{
    // ------------------------------------------------------------------
    // Reflection bridge to The Forest's own types.
    //
    // Everything here is looked up by name at runtime rather than
    // referenced at compile time. That keeps the build free of game DLLs
    // (so CI works) and makes a game update degrade into a logged warning
    // instead of a crash.
    //
    // Names below were confirmed from a live F11 dump, not guessed:
    //   FirstPersonCharacter          - movement controller on "player"
    //     .Locked          (bool)     - game's own full input lock
    //     .MovementLocked  (bool)     - movement-only lock
    //     .walkSpeed 6.5 / .runSpeed 13.5 / .maximumVelocity 55
    //   TheForest.Items.Inventory.PlayerInventory
    //     ._possessedItemsCount (int)
    //     ._possessedItems      (List<InventoryItem>)
    // ------------------------------------------------------------------
    public class GameBridge
    {
        private readonly ManualLogSource _log;

        private Component _fpc;
        private FieldInfo _lockedField;
        private FieldInfo _movementLockedField;
        private bool _fpcResolved;

        private Component _inventory;
        private FieldInfo _possessedCountField;
        private bool _inventoryResolved;

        public bool PlayerLockAvailable { get { return _lockedField != null || _movementLockedField != null; } }
        public bool InventoryAvailable { get { return _possessedCountField != null && _inventory != null; } }

        public GameBridge(ManualLogSource log)
        {
            _log = log;
        }

        public void Reset()
        {
            _fpc = null;
            _lockedField = null;
            _movementLockedField = null;
            _fpcResolved = false;

            _inventory = null;
            _possessedCountField = null;
            _inventoryResolved = false;
        }

        // ------------------------------------------------------------------
        // Movement lock - uses the game's OWN flags.
        //
        // This replaces the Time.timeScale approach, which did nothing:
        // The Forest re-asserts timeScale every frame, so an external
        // override loses. Setting the controller's own Locked flag works
        // with the game rather than against it.
        // ------------------------------------------------------------------
        public void ResolvePlayerController(Transform playerRoot)
        {
            if (_fpcResolved || playerRoot == null) return;
            _fpcResolved = true;

            Component[] comps;
            try { comps = playerRoot.GetComponentsInChildren(typeof(Component), true); }
            catch (Exception ex) { _log.LogWarning("Component scan failed: " + ex.Message); return; }

            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] == null) continue;

                Type t = comps[i].GetType();
                if (t.Name != "FirstPersonCharacter") continue;

                _fpc = comps[i];
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                _lockedField = t.GetField("Locked", flags);
                _movementLockedField = t.GetField("MovementLocked", flags);

                _log.LogInfo("FirstPersonCharacter found. Locked:" + (_lockedField != null) +
                             " MovementLocked:" + (_movementLockedField != null));
                return;
            }

            _log.LogWarning("FirstPersonCharacter not found on player subtree - movement lock unavailable.");
        }

        public void SetPlayerLocked(bool locked)
        {
            if (_fpc == null) return;

            try
            {
                if (_lockedField != null) _lockedField.SetValue(_fpc, locked);
                if (_movementLockedField != null) _movementLockedField.SetValue(_fpc, locked);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Could not set player lock: " + ex.Message);
            }
        }

        public bool IsPlayerLocked()
        {
            if (_fpc == null || _lockedField == null) return false;
            try { return (bool)_lockedField.GetValue(_fpc); }
            catch (Exception) { return false; }
        }

        // ------------------------------------------------------------------
        // Inventory - first real read of game data.
        //
        // Only the total count is wired up so far. Per-item breakdown needs
        // InventoryItem's field layout, which the explorer can reveal:
        // filter for "InventoryItem" and inspect it.
        // ------------------------------------------------------------------
        public void ResolveInventory()
        {
            if (_inventoryResolved) return;

            Type invType = FindGameType("TheForest.Items.Inventory.PlayerInventory");
            if (invType == null) return;

            UnityEngine.Object found;
            try { found = UnityEngine.Object.FindObjectOfType(invType); }
            catch (Exception) { return; }

            if (found == null) return;   // not spawned yet - retry later

            _inventoryResolved = true;
            _inventory = found as Component;

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _possessedCountField = invType.GetField("_possessedItemsCount", flags);

            _log.LogInfo("PlayerInventory resolved. _possessedItemsCount:" + (_possessedCountField != null));
        }

        public int GetPossessedItemCount()
        {
            if (_inventory == null || _possessedCountField == null) return -1;
            try { return (int)_possessedCountField.GetValue(_inventory); }
            catch (Exception) { return -1; }
        }

        // ------------------------------------------------------------------
        public static Type FindGameType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            for (int a = 0; a < assemblies.Length; a++)
            {
                string name;
                try { name = assemblies[a].GetName().Name; }
                catch (Exception) { continue; }
                if (!name.StartsWith("Assembly-CSharp")) continue;

                try
                {
                    Type t = assemblies[a].GetType(fullName, false);
                    if (t != null) return t;
                }
                catch (Exception) { }
            }
            return null;
        }
    }
}
