using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Savestates through the game's own serializer (UnitySerializer's
    // LevelSerializer). Everything below is from IL - see game-notes
    // "Saving and loading".
    //
    // CAPTURE mirrors PlayerStats.OnSaveSlotSelectedRoutine step for step,
    // except the UI toggling and the slot writes:
    //   drop the glider -> force-unload greeble zones and the surface
    //   scene loaders, CheckInCave, UnloadUnusedAssets, GCCollect -> FakeParent
    //   .ReParent on inactive held items -> SerializeLevel -> undo the
    //   force-unload, wait 0.3 s, UnParent.
    // The game force-unloads only in MemorySafeSaveMode; savestates always
    // do (since v0.20.1), so streamed content is never in the save and an
    // in-place restore can unload, diff and reload it cleanly.
    // The game's routine ends in Checkpoint(), which writes the slot file
    // (and Steam Cloud). We call SerializeLevel(false) instead - the same
    // string Checkpoint would store - and keep it in our own file.
    //
    // RESTORE, two ways:
    //   in place - LevelSerializer.LoadNow(data, false, false, complete):
    //              no scene load. Recreates missing prefab objects and
    //              restores the rest - but its delete step only runs for a
    //              partial save (LevelData.rootObject set), NEVER for a
    //              full level, so objects built after the capture survived
    //              v0.20.0. We delete them first: every identifier whose Id
    //              is not in the save's StoredObjectNames, outside the
    //              player. Held items are stashed first (the inventory
    //              restore does not touch the hands). Streaming is
    //              force-unloaded around it exactly as around the capture.
    //   with load - LevelSerializer.LoadSavedLevel(data): what the game's
    //              Resume() does after reading the slot file - one scene
    //              load, not the two a menu load costs.
    //
    // PHASE 0: this is a probe. Every step logs what it did so the
    // author's test can decide which restore is worth building on.
    // ------------------------------------------------------------------
    public sealed class SavestateBridge
    {
        public sealed class Result
        {
            public bool Ok;
            public string Message = "";
            public string Data;
            public string Level = "";
            public string Difficulty = "";
            public bool StreamingUnloaded;
        }

        private const float LoadTimeout = 120f;

        private readonly ManualLogSource _log;
        private bool _resolved;

        public string Status { get; private set; }

        // LevelSerializer
        private MethodInfo _serializeLevel;      // static string SerializeLevel(bool urgent)
        private MethodInfo _loadNow;             // static void LoadNow(object, bool, bool, Action<LevelLoader>)
        private MethodInfo _loadSavedLevel;      // static LevelLoader LoadSavedLevel(string)
        private MethodInfo _resume;              // static void Resume()
        private PropertyInfo _isSuspended;
        private PropertyInfo _isDeserializing;
        private Type _levelLoaderType;

        // Level data, for the delete step
        private MethodInfo _deserializeLevelData; // UnitySerializer.Deserialize<LevelData>(byte[])
        private FieldInfo _storedObjectNames;     // LevelData.StoredObjectNames : List<StoredItem>
        private FieldInfo _storedItemName;        // StoredItem.Name
        private FieldInfo _storedItemGoName;      // StoredItem.GameObjectName
        private FieldInfo _storedItemParent;      // StoredItem.ParentName = the parent's identifier id
        private FieldInfo _storedItemClass;       // StoredItem.ClassId (prefabs only)
        private MethodInfo _decompress;           // CompressionHelper.Decompress(string) : byte[]

        // Build missions (the "GATHER LOGS 0/4" HUD). Craft_Structure adds
        // to BuildMission's static tally when a ghost is placed and takes it
        // back in SpawnBackIngredients when one is cancelled; deleting a
        // ghost skips that, so the HUD line stayed (author, v0.20.1).
        private Type _craftStructureType;
        private FieldInfo _requiredIngredients;   // List<BuildIngredients>
        private FieldInfo _presentIngredients;    // ReceipeIngredient[]
        private MethodInfo _addNeededToMission;   // static BuildMission.AddNeededToBuildMission(int, int, bool)

        // Weapon-upgrade receivers (feathers, teeth, glass) on the inventory's
        // item views: scene objects that hold each weapon's upgrades. Never
        // built after a capture, so never ours to delete - see DeleteUnsaved.
        private Type _upgradeReceiverType;

        // Hands
        private FieldInfo _inventory;             // LocalPlayer.Inventory
        private MethodInfo _stashWeapon;          // PlayerInventory.StashEquipedWeapon(bool)
        private MethodInfo _stashLeftHand;        // PlayerInventory.StashLeftHand()

        // Held items (v0.24.1): what the hands held at capture is put back
        // after an in-place restore.
        private FieldInfo _equipmentSlots;        // PlayerInventory._equipmentSlots (InventoryItemView[])
        private FieldInfo _noEquipedItem;         // PlayerInventory._noEquipedItem - the "empty" view
        private FieldInfo _equipmentSlotsPrevious; // PlayerInventory._equipmentSlotsPrevious (InventoryItemView[])
        private FieldInfo _itemViewsCache;        // PlayerInventory._inventoryItemViewsCache (id -> views)
        private FieldInfo _viewItemId;            // InventoryItemView._itemId
        private MethodInfo _equipById;            // PlayerInventory.Equip(int, bool)
        private MethodInfo _isSlotLocked;         // PlayerInventory.IsSlotLocked(EquipmentSlot)
        private object _leftHandSlot;             // Item.EquipmentSlot.LeftHand
        private FieldInfo _lighterBusy;           // static LighterControler.IsBusy

        // Enemies (v0.24.5): the game's own enemy restart.
        private FieldInfo _mutantControler;       // static Scene.MutantControler
        private MethodInfo _startSetupFamilies;   // mutantController.startSetupFamilies()
        private FieldInfo _hordeActive;           // mutantController.hordeModeActive
        private FieldInfo _activeCannibals;       // mutantController.activeCannibals
        private FieldInfo _allWorldSpawns;        // mutantController.allWorldSpawns
        private FieldInfo _planeCrash;            // static Scene.PlaneCrash
        private FieldInfo _spawnedHull;           // PlaneCrashController.spawnedHullPrefab
        private MethodInfo _gotCleanReal;         // PlayerStats.GotCleanReal()
        private PropertyInfo _isBloody;           // PlayerStats.IsBloody
        private PropertyInfo _noEnemies;          // static Cheats.NoEnemies
        private FieldInfo _playerStats;           // static LocalPlayer.Stats
        private FieldInfo _delayedSpawnCheck;     // PlayerStats.delayedMutantSpawnCheck
        private Type _ragdollifyType;             // clsragdollify
        private FieldInfo _ragdollPrefab;         // clsragdollify.vargamragdoll (Transform prefab)
        private HashSet<string> _ragdollNames;    // "<prefab>(Clone)"

        // Identifiers
        private Type _uniqueIdType;
        private PropertyInfo _allIdentifiers;
        private PropertyInfo _uidId;
        private PropertyInfo _uidClassId;

        // GameSetup
        private MethodInfo _setInitType;
        private object _initContinue;
        private PropertyInfo _difficulty;
        private PropertyInfo _isCreative;
        private MethodInfo _setDifficulty;
        private Type _difficultyType;

        // Save-routine steps
        private FieldInfo _greebleManager;
        private MethodInfo _greebleForcedUnload;
        private MethodInfo _greebleCheckInCave;
        private FieldInfo _sceneLoaders;
        private MethodInfo _caveForcedUnload;
        private MethodInfo _caveCheckInCave;
        private MethodInfo _unloadUnused;
        private MethodInfo _gcCollect;
        private FieldInfo _itemSlots;
        private FieldInfo _available;
        private Type _fakeParentType;
        private FieldInfo _fakeParentTarget;     // FakeParent.target (the hand bone it belongs under)
        private MethodInfo _reParent;
        private MethodInfo _unParent;
        private FieldInfo _animControl;
        private FieldInfo _holdingGlider;
        private FieldInfo _specialActions;
        private PropertyInfo _inOverlook;
        private FieldInfo _finishGameLoad;

        // In-place restore bookkeeping.
        private bool _loadDone;
        private int _logNotFound;
        private int _logProblems;
        private readonly List<string> _logSamples = new List<string>();

        public SavestateBridge(ManualLogSource log)
        {
            _log = log;
            Status = "not resolved";
        }

        // ------------------------------------------------------------------
        public bool Resolve()
        {
            if (_resolved) return _serializeLevel != null;
            _resolved = true;

            BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            Type ls = GameBridge.FindGameType("LevelSerializer");
            _levelLoaderType = GameBridge.FindGameType("LevelLoader");
            if (ls != null)
            {
                _serializeLevel = ls.GetMethod("SerializeLevel", stat, null, new[] { typeof(bool) }, null);
                _loadSavedLevel = ls.GetMethod("LoadSavedLevel", stat, null, new[] { typeof(string) }, null);
                _resume = ls.GetMethod("Resume", stat, null, Type.EmptyTypes, null);
                _isSuspended = ls.GetProperty("IsSuspended", stat);
                _isDeserializing = ls.GetProperty("IsDeserializing", stat);

                if (_levelLoaderType != null)
                {
                    Type complete = typeof(Action<>).MakeGenericType(_levelLoaderType);
                    _loadNow = ls.GetMethod("LoadNow", stat, null,
                        new[] { typeof(object), typeof(bool), typeof(bool), complete }, null);
                }

                Type levelData = ls.GetNestedType("LevelData", BindingFlags.Public | BindingFlags.NonPublic);
                Type storedItem = ls.GetNestedType("StoredItem", BindingFlags.Public | BindingFlags.NonPublic);
                if (levelData != null) _storedObjectNames = levelData.GetField("StoredObjectNames", inst);
                if (storedItem != null)
                {
                    _storedItemName = storedItem.GetField("Name", inst);
                    _storedItemGoName = storedItem.GetField("GameObjectName", inst);
                    _storedItemParent = storedItem.GetField("ParentName", inst);
                    _storedItemClass = storedItem.GetField("ClassId", inst);
                }

                Type us = GameBridge.FindGameType("Serialization.UnitySerializer");
                if (us != null)
                {
                    foreach (MethodInfo m in us.GetMethods(stat))
                    {
                        if (m.Name != "Deserialize" || !m.IsGenericMethodDefinition) continue;
                        ParameterInfo[] p = m.GetParameters();
                        if (p.Length == 1 && p[0].ParameterType == typeof(byte[]))
                        {
                            if (levelData != null) _deserializeLevelData = m.MakeGenericMethod(levelData);
                            break;
                        }
                    }
                }

                Type compression = GameBridge.FindGameType("CompressionHelper");
                if (compression != null)
                    _decompress = compression.GetMethod("Decompress", stat, null, new[] { typeof(string) }, null);
            }

            _uniqueIdType = GameBridge.FindGameType("UniqueIdentifier");
            if (_uniqueIdType != null)
            {
                _allIdentifiers = _uniqueIdType.GetProperty("AllIdentifiers", stat);
                _uidId = _uniqueIdType.GetProperty("Id", inst);
                _uidClassId = _uniqueIdType.GetProperty("ClassId", inst);
            }

            Type setup = GameBridge.FindGameType("TheForest.Utils.GameSetup");
            if (setup != null)
            {
                _difficulty = setup.GetProperty("Difficulty", stat);
                _isCreative = setup.GetProperty("IsCreativeGame", stat);
                if (_difficulty != null)
                {
                    _difficultyType = _difficulty.PropertyType;
                    _setDifficulty = setup.GetMethod("SetDifficulty", stat, null, new[] { _difficultyType }, null);
                }

                PropertyInfo init = setup.GetProperty("Init", stat);
                if (init != null)
                {
                    _setInitType = setup.GetMethod("SetInitType", stat, null, new[] { init.PropertyType }, null);
                    try { _initContinue = Enum.Parse(init.PropertyType, "Continue"); }
                    catch (Exception) { _initContinue = null; }
                }
            }

            Type scene = GameBridge.FindGameType("TheForest.Utils.Scene");
            if (scene != null)
            {
                _greebleManager = scene.GetField("GreebleZonesManager", stat);
                _sceneLoaders = scene.GetField("SceneLoaders", stat);
                _finishGameLoad = scene.GetField("FinishGameLoad", stat);
            }

            Type greeble = GameBridge.FindGameType("GreebleZonesManager");
            if (greeble != null)
            {
                _greebleForcedUnload = greeble.GetMethod("ForcedUnload", inst, null, new[] { typeof(bool) }, null);
                _greebleCheckInCave = greeble.GetMethod("CheckInCave", inst, null, Type.EmptyTypes, null);
            }

            Type cave = GameBridge.FindGameType("TheForest.World.SceneUnloadInCave");
            if (cave != null)
            {
                _caveForcedUnload = cave.GetMethod("ForcedUnload", inst, null, new[] { typeof(bool) }, null);
                _caveCheckInCave = cave.GetMethod("CheckInCave", inst, null, Type.EmptyTypes, null);
            }

            Type res = GameBridge.FindGameType("TheForest.Utils.ResourcesHelper");
            if (res != null)
            {
                _unloadUnused = res.GetMethod("UnloadUnusedAssets", stat, null, Type.EmptyTypes, null);
                _gcCollect = res.GetMethod("GCCollect", stat, null, Type.EmptyTypes, null);
            }

            Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
            if (local != null)
            {
                _itemSlots = local.GetField("ItemSlots", stat);
                _animControl = local.GetField("AnimControl", stat);
                _specialActions = local.GetField("SpecialActions", stat);
                _inOverlook = local.GetProperty("IsInOverlookArea", stat);
                _inventory = local.GetField("Inventory", stat);
            }

            _upgradeReceiverType = GameBridge.FindGameType("TheForest.Items.Craft.UpgradeViewReceiver");
            _craftStructureType = GameBridge.FindGameType("TheForest.Buildings.Creation.Craft_Structure");
            if (_craftStructureType != null)
            {
                _requiredIngredients = _craftStructureType.GetField("_requiredIngredients", inst);
                _presentIngredients = _craftStructureType.GetField("_presentIngredients", inst);
            }
            Type mission = GameBridge.FindGameType("TheForest.Buildings.Creation.BuildMission");
            if (mission != null)
                _addNeededToMission = mission.GetMethod("AddNeededToBuildMission", stat, null,
                    new[] { typeof(int), typeof(int), typeof(bool) }, null);

            Type inv = GameBridge.FindGameType("TheForest.Items.Inventory.PlayerInventory");
            if (inv != null)
            {
                _stashWeapon = inv.GetMethod("StashEquipedWeapon", inst, null, new[] { typeof(bool) }, null);
                _stashLeftHand = inv.GetMethod("StashLeftHand", inst, null, Type.EmptyTypes, null);
                _equipmentSlots = inv.GetField("_equipmentSlots", inst);
                _noEquipedItem = inv.GetField("_noEquipedItem", inst);
                _equipmentSlotsPrevious = inv.GetField("_equipmentSlotsPrevious", inst);
                _itemViewsCache = inv.GetField("_inventoryItemViewsCache", inst);
                _equipById = inv.GetMethod("Equip", inst, null, new[] { typeof(int), typeof(bool) }, null);
                _isSlotLocked = inv.GetMethod("IsSlotLocked", inst);
                if (_isSlotLocked != null)
                {
                    try { _leftHandSlot = Enum.Parse(_isSlotLocked.GetParameters()[0].ParameterType, "LeftHand"); }
                    catch (Exception) { _leftHandSlot = null; }
                }
            }
            Type view = GameBridge.FindGameType("TheForest.Items.Inventory.InventoryItemView");
            if (view != null) _viewItemId = view.GetField("_itemId", inst);
            Type lighter = GameBridge.FindGameType("TheForest.Items.Special.LighterControler");
            if (lighter != null) _lighterBusy = lighter.GetField("IsBusy", stat);

            Type sceneStatics = GameBridge.FindGameType("TheForest.Utils.Scene");
            if (sceneStatics != null) _mutantControler = sceneStatics.GetField("MutantControler", stat);
            Type mutants = GameBridge.FindGameType("mutantController");
            if (mutants != null)
            {
                _startSetupFamilies = mutants.GetMethod("startSetupFamilies", inst, null, Type.EmptyTypes, null);
                _hordeActive = mutants.GetField("hordeModeActive", inst);
                _activeCannibals = mutants.GetField("activeCannibals", inst);
                _allWorldSpawns = mutants.GetField("allWorldSpawns", inst);
            }
            if (sceneStatics != null) _planeCrash = sceneStatics.GetField("PlaneCrash", stat);
            Type planeCrash = GameBridge.FindGameType("PlaneCrashController");
            if (planeCrash != null) _spawnedHull = planeCrash.GetField("spawnedHullPrefab", inst);
            Type cheats = GameBridge.FindGameType("Cheats");
            if (cheats != null) _noEnemies = cheats.GetProperty("NoEnemies", stat);
            Type localPlayer = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
            if (localPlayer != null) _playerStats = localPlayer.GetField("Stats", stat);
            Type playerStats = GameBridge.FindGameType("PlayerStats");
            if (playerStats != null)
            {
                _delayedSpawnCheck = playerStats.GetField("delayedMutantSpawnCheck", inst);
                _gotCleanReal = playerStats.GetMethod("GotCleanReal", inst, null, Type.EmptyTypes, null);
                _isBloody = playerStats.GetProperty("IsBloody", inst);
            }
            _ragdollifyType = GameBridge.FindGameType("clsragdollify");
            if (_ragdollifyType != null) _ragdollPrefab = _ragdollifyType.GetField("vargamragdoll", inst);

            Type slotType = GameBridge.FindGameType("itemConstrainToHand");
            if (slotType != null) _available = slotType.GetField("Available", inst);

            _fakeParentType = GameBridge.FindGameType("TheForest.Utils.FakeParent");
            if (_fakeParentType != null)
            {
                _reParent = _fakeParentType.GetMethod("ReParent", inst, null, Type.EmptyTypes, null);
                _unParent = _fakeParentType.GetMethod("UnParent", inst, null, Type.EmptyTypes, null);
                _fakeParentTarget = _fakeParentType.GetField("target", inst);
            }

            Type anim = GameBridge.FindGameType("playerAnimatorControl");
            if (anim != null) _holdingGlider = anim.GetField("holdingGlider", inst);

            Status = "serialize:" + (_serializeLevel != null) +
                     " loadNow:" + (_loadNow != null) +
                     " loadSaved:" + (_loadSavedLevel != null) +
                     " resume:" + (_resume != null) +
                     " diff:" + (_deserializeLevelData != null && _storedObjectNames != null && _storedItemName != null && _decompress != null) +
                     " stash:" + (_stashWeapon != null && _stashLeftHand != null) +
                     " held:" + (_equipmentSlots != null && _viewItemId != null && _equipById != null && _leftHandSlot != null && _lighterBusy != null) +
                     " enemies:" + (_mutantControler != null && _startSetupFamilies != null && _noEnemies != null) +
                     " bodies:" + (_ragdollPrefab != null) +
                     " respawnCheck:" + (_activeCannibals != null && _allWorldSpawns != null) +
                     " plane:" + (_planeCrash != null && _spawnedHull != null) +
                     " wash:" + (_gotCleanReal != null && _isBloody != null) +
                     " missions:" + (_requiredIngredients != null && _presentIngredients != null && _addNeededToMission != null) +
                     " streaming:" + (_greebleForcedUnload != null && _caveForcedUnload != null) +
                     " fakeParent:" + (_reParent != null && _fakeParentTarget != null) +
                     " init:" + (_setInitType != null && _initContinue != null);
            _log.LogInfo("Savestates bound. " + Status);
            return _serializeLevel != null;
        }

        // ------------------------------------------------------------------
        // State the panel shows.

        public bool IsDeserializing
        {
            get { return ReadBool(_isDeserializing); }
        }

        public bool GameLoadFinished
        {
            get
            {
                if (_finishGameLoad == null) return true;
                try { return (bool)_finishGameLoad.GetValue(null); }
                catch (Exception) { return true; }
            }
        }

        /// "Creative", the difficulty's name, or "" when unknown.
        public string CurrentDifficulty { get { return DifficultyName(); } }

        public int IdentifierCount
        {
            get
            {
                if (_allIdentifiers == null) return -1;
                try
                {
                    ICollection c = _allIdentifiers.GetValue(null, null) as ICollection;
                    return c != null ? c.Count : -1;
                }
                catch (Exception) { return -1; }
            }
        }

        // ------------------------------------------------------------------
        // CAPTURE. A coroutine because the game's routine spreads its steps
        // over frames (asset unload, GC, the 0.3 s before UnParent).
        public IEnumerator Capture(Action<Result> done)
        {
            Result r = new Result();

            if (!Resolve()) { r.Message = "LevelSerializer.SerializeLevel not found"; done(r); yield break; }
            if (IsDeserializing) { r.Message = "the game is loading"; done(r); yield break; }
            if (ReadBool(_inOverlook)) { r.Message = "the game refuses to save in the overlook area"; done(r); yield break; }

            Stopwatch total = Stopwatch.StartNew();

            DropGlider();

            bool unloaded = ForceUnloadStreaming(true, true);
            yield return null;
            Call(_unloadUnused);
            yield return null;
            yield return null;
            Call(_gcCollect);
            yield return null;

            ReParentHeld(true);
            yield return null;

            // The game's SaveGame defers a non-urgent save while serialization
            // is suspended; wait for it the same way rather than forcing it.
            float waitStart = Time.realtimeSinceStartup;
            while (ReadBool(_isSuspended) && Time.realtimeSinceStartup - waitStart < 5f)
                yield return null;

            Stopwatch serialize = Stopwatch.StartNew();
            try
            {
                if (ReadBool(_isSuspended))
                {
                    r.Message = "serialization stayed suspended for 5 s";
                }
                else
                {
                    r.Data = _serializeLevel.Invoke(null, new object[] { false }) as string;
                    r.Ok = !string.IsNullOrEmpty(r.Data);
                    if (!r.Ok) r.Message = "SerializeLevel returned nothing";
                }
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                r.Message = "SerializeLevel threw: " + inner.GetType().Name + ": " + inner.Message;
                _log.LogWarning("Savestate capture: " + inner);
            }
            serialize.Stop();

            r.Level = SceneManager.GetActiveScene().name;
            r.Difficulty = DifficultyName();
            r.StreamingUnloaded = unloaded;

            // Undo, in the game's order.
            if (unloaded)
            {
                ForceUnloadStreaming(false, false);
                yield return new WaitForSeconds(0.3f);
            }
            ReParentHeld(false);

            total.Stop();
            if (r.Ok)
            {
                r.Message = "serialized in " + serialize.ElapsedMilliseconds + " ms (total " +
                            total.ElapsedMilliseconds + " ms), " + Kb(r.Data.Length) +
                            ", streaming " + (unloaded ? "force-unloaded" : "unload FAILED (kept)") +
                            ", " + IdentifierCount + " identifiers";
            }
            done(r);
        }

        // ------------------------------------------------------------------
        // RESTORE IN PLACE - no scene load.
        /// `unloadStreaming` must match how the data was captured: true for
        /// savestates since v0.20.1, the header's value for older files, the
        /// game's MemorySafeSaveMode for a slot save. `keepRoot` (the
        /// player) is never deleted from.
        public IEnumerator RestoreInPlace(string data, bool unloadStreaming, Transform keepRoot, Action<Result> done)
        {
            Result r = new Result();

            if (!Resolve() || _loadNow == null) { r.Message = "LevelSerializer.LoadNow not found"; done(r); yield break; }
            if (IsDeserializing) { r.Message = "the game is already loading"; done(r); yield break; }

            string diffError;
            List<SavedObject> saved;
            HashSet<string> stored = StoredNames(data, out diffError, out saved);

            string adoptNote = null;
            string foreign = stored != null ? AdoptPlayer(stored, saved, keepRoot, out adoptNote) : null;
            if (foreign != null) { r.Message = foreign; done(r); yield break; }

            StashHands();

            // Putting the lighter away is an animated routine
            // (LighterControler.StashLighterRoutine): it locks the left-hand
            // slot and unequips at its end. Restoring meanwhile, the game's
            // re-equip found the slot locked and fell back to AddItem -
            // "CANNOT CARRY ANY MORE LIGHTERS" - and the routine then put
            // away the lighter the restore had just equipped (runner maks).
            float stashStart = Time.realtimeSinceStartup;
            while (HandsBusy() && Time.realtimeSinceStartup - stashStart < 2f) yield return null;
            float stashWait = Time.realtimeSinceStartup - stashStart;
            bool stashStuck = HandsBusy();
            // Cheesecake's log (v0.24.97): twice 2 s, cause unknown - say
            // what held the hands next time.
            if (stashStuck) _log.LogInfo("Savestate restore (in place): hands still busy after 2 s - " + HandsBusyWhy() +
                                         " | " + PlayerHold.Describe() + ".");

            int before = IdentifierCount;
            bool unloaded = false;
            if (unloadStreaming)
            {
                unloaded = ForceUnloadStreaming(true, true);
                // Greeble zones and scene unloads take a few frames.
                yield return new WaitForSeconds(0.25f);
            }

            // LoadNow never deletes for a full-level save; do it here.
            List<string> deletedNames = new List<string>();
            int keptReceivers = 0, keptHeld = 0;
            int deleted = stored != null ? DeleteUnsaved(stored, keepRoot, deletedNames, ref keptReceivers, ref keptHeld) : 0;
            if (deleted > 0) yield return null;   // let Destroy land before the loader looks

            _loadDone = false;
            _logNotFound = 0;
            _logProblems = 0;
            _logSamples.Clear();
            Application.logMessageReceived += OnUnityLog;

            Stopwatch sw = Stopwatch.StartNew();
            bool started = false;
            try
            {
                Delegate complete = null;
                try
                {
                    complete = Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(_levelLoaderType), this,
                        GetType().GetMethod("OnLoadComplete", BindingFlags.Instance | BindingFlags.NonPublic));
                }
                catch (Exception) { complete = null; }

                _loadNow.Invoke(null, new object[] { data, false, false, complete });
                started = true;
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                r.Message = "LoadNow threw: " + inner.GetType().Name + ": " + inner.Message;
                _log.LogWarning("Savestate restore (in place): " + inner);
            }

            if (started)
            {
                // Without a completion callback, fall back to watching the
                // serializer's own flag go up and come back down.
                bool sawDeserializing = false;
                while (!_loadDone && sw.Elapsed.TotalSeconds < LoadTimeout)
                {
                    bool d = IsDeserializing;
                    if (d) sawDeserializing = true;
                    else if (sawDeserializing) { _loadDone = true; break; }
                    yield return null;
                }
            }
            sw.Stop();
            Application.logMessageReceived -= OnUnityLog;

            if (unloaded)
            {
                ForceUnloadStreaming(false, false);
                yield return null;
            }

            if (started)
            {
                int after = IdentifierCount;
                r.Ok = _loadDone;
                StringBuilder sb = new StringBuilder();
                sb.Append(_loadDone ? "done in " : "TIMED OUT after ").Append(sw.ElapsedMilliseconds).Append(" ms");
                sb.Append(", identifiers ").Append(before).Append(" -> ").Append(after);
                if (stored == null) sb.Append(", delete step SKIPPED (").Append(diffError).Append(")");
                else
                {
                    sb.Append(", deleted ").Append(deleted).Append(" not in the save");
                    for (int i = 0; i < deletedNames.Count && i < 6; i++) sb.Append(i == 0 ? " (" : ", ").Append(deletedNames[i]);
                    if (deletedNames.Count > 0) sb.Append(deletedNames.Count > 6 ? ", ...)" : ")");
                    if (keptReceivers > 0) sb.Append(", kept ").Append(keptReceivers).Append(" weapon-upgrade receiver(s) the save lacks");
                    if (keptHeld > 0) sb.Append(", kept ").Append(keptHeld).Append(" held-item object(s) of the player the save lacks");
                }
                sb.Append(", 'not found' ").Append(_logNotFound);
                sb.Append(", problems ").Append(_logProblems);
                sb.Append(", streaming ").Append(unloadStreaming ? (unloaded ? "force-unloaded" : "unload FAILED") : "kept");
                if (stashWait > 0.05f || stashStuck)
                    sb.Append(", hands put away in ").Append((int)(stashWait * 1000f)).Append(" ms").Append(stashStuck ? " (STILL BUSY)" : "");
                if (adoptNote != null) sb.Append(", ").Append(adoptNote);
                for (int i = 0; i < _logSamples.Count; i++) sb.Append(" | ").Append(_logSamples[i]);
                r.Message = sb.ToString();
            }
            done(r);
        }

        // Signature matches Action<LevelLoader> by relaxed binding.
        private void OnLoadComplete(object loader)
        {
            _loadDone = true;
        }

        private void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            if (condition == null) return;
            bool notFound = condition.StartsWith("Could not find") || condition.StartsWith("Not found");
            bool problem = condition.StartsWith("Problem ");
            if (!notFound && !problem) return;

            if (notFound) _logNotFound++;
            if (problem) _logProblems++;
            if (_logSamples.Count < 6) _logSamples.Add(condition.Length > 120 ? condition.Substring(0, 120) : condition);
        }

        // ------------------------------------------------------------------
        // RESTORE WITH A LOAD - the second half of the game's own load.
        public string RestoreWithLoad(string data, string difficulty)
        {
            if (!Resolve() || _loadSavedLevel == null) return "LevelSerializer.LoadSavedLevel not found";
            if (IsDeserializing) return "the game is already loading";

            string prep = PrepareContinue(difficulty);
            try
            {
                _loadSavedLevel.Invoke(null, new object[] { data });
                return null;
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                _log.LogWarning("Savestate restore (load): " + inner);
                return "LoadSavedLevel threw: " + inner.Message + (prep != null ? " (" + prep + ")" : "");
            }
        }

        /// The current slot's save, loaded without the menu: Resume() reads
        /// the slot file, sets the difficulty from it and calls
        /// LoadSavedLevel - exactly what LoadSave.Awake does.
        public string LoadSlotWithoutMenu()
        {
            if (!Resolve() || _resume == null) return "LevelSerializer.Resume not found";
            if (IsDeserializing) return "the game is already loading";

            PrepareContinue(null);
            try
            {
                _resume.Invoke(null, null);
                return null;
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                _log.LogWarning("Savestate slot load: " + inner);
                return "Resume threw: " + inner.Message;
            }
        }

        // A menu load sets InitType Continue on the title screen; a game
        // that began as New would otherwise treat the reload as a new game.
        private string PrepareContinue(string difficulty)
        {
            string note = null;
            try
            {
                if (_setInitType != null && _initContinue != null)
                    _setInitType.Invoke(null, new[] { _initContinue });
                else note = "InitType not set";
            }
            catch (Exception ex) { note = "InitType: " + ex.Message; }

            // Resume() sets the difficulty from the save's name; LoadSavedLevel
            // does not. Creative is a game type, not a difficulty - left alone.
            if (!string.IsNullOrEmpty(difficulty) && difficulty != "Creative" &&
                _setDifficulty != null && _difficultyType != null)
            {
                try { _setDifficulty.Invoke(null, new[] { Enum.Parse(_difficultyType, difficulty) }); }
                catch (Exception) { note = "difficulty '" + difficulty + "' not applied"; }
            }
            return note;
        }

        // ------------------------------------------------------------------
        // The ids the save knows - LevelData.StoredObjectNames[i].Name, which
        // the loader compares against UniqueIdentifier.Id. The data is
        // "NOCOMPRESSION" + base64, or CompressionHelper's format.
        private HashSet<string> StoredNames(string data, out string error)
        {
            List<SavedObject> unused;
            return StoredNames(data, out error, out unused);
        }

        private HashSet<string> StoredNames(string data, out string error, out List<SavedObject> objects)
        {
            error = null;
            objects = new List<SavedObject>();
            if (_deserializeLevelData == null || _storedObjectNames == null || _storedItemName == null)
            {
                error = "level data reader not bound";
                return null;
            }

            try
            {
                byte[] bytes;
                if (data.StartsWith("NOCOMPRESSION")) bytes = Convert.FromBase64String(data.Substring(13));
                else if (_decompress != null) bytes = _decompress.Invoke(null, new object[] { data }) as byte[];
                else { error = "compressed data and no CompressionHelper"; return null; }

                object level = _deserializeLevelData.Invoke(null, new object[] { bytes });
                IList items = level != null ? _storedObjectNames.GetValue(level) as IList : null;
                if (items == null) { error = "no StoredObjectNames"; return null; }

                HashSet<string> names = new HashSet<string>();
                for (int i = 0; i < items.Count; i++)
                {
                    object it = items[i];
                    string n = it != null ? _storedItemName.GetValue(it) as string : null;
                    if (string.IsNullOrEmpty(n)) continue;
                    names.Add(n);

                    SavedObject o = new SavedObject();
                    o.Id = n;
                    o.GameObjectName = _storedItemGoName != null ? _storedItemGoName.GetValue(it) as string : null;
                    o.ParentId = _storedItemParent != null ? _storedItemParent.GetValue(it) as string : null;
                    o.ClassId = _storedItemClass != null ? _storedItemClass.GetValue(it) as string : null;
                    objects.Add(o);
                }
                return names;
            }
            catch (Exception ex)
            {
                error = "could not read the level data: " + (ex.InnerException ?? ex).Message;
                return null;
            }
        }

        /// Makes the live player the save's player, so a savestate from
        /// ANOTHER save restores into it. Each game gives its player its own
        /// UniqueIdentifier id; in place, LoadNow could not find the saved
        /// one and instantiated it beside the live player - two players, two
        /// inventories (author, v0.22.0: a Hard start state in a Creative
        /// game). When the player is not in the save, EVERY live identifier
        /// the save lacks is given the id of the saved object with the same
        /// GameObject name, prefab class and parent id - shallowest first,
        /// so a child matches against its parent's NEW id - but only on a
        /// unique match, and never an id a live object holds. Returns null
        /// when the player is (now) in the save, or why the restore must
        /// not go ahead. No identifier = no verdict.
        public string AdoptPlayer(string data, Transform playerRoot, out string note)
        {
            note = null;
            string error;
            List<SavedObject> saved;
            HashSet<string> stored = StoredNames(data, out error, out saved);
            return stored != null ? AdoptPlayer(stored, saved, playerRoot, out note) : null;
        }

        private sealed class SavedObject
        {
            public string Id, GameObjectName, ParentId, ClassId;
        }

        private sealed class ByDepth : IComparer<KeyValuePair<int, Component>>
        {
            public int Compare(KeyValuePair<int, Component> a, KeyValuePair<int, Component> b) { return a.Key.CompareTo(b.Key); }
        }

        private string AdoptPlayer(HashSet<string> stored, List<SavedObject> saved, Transform playerRoot, out string note)
        {
            note = null;
            if (playerRoot == null || _uniqueIdType == null || _uidId == null || _allIdentifiers == null) return null;

            // The player's shallowest identifier says whose save this is.
            Component top = null;
            int topDepth = int.MaxValue;
            try
            {
                Component[] under = playerRoot.GetComponentsInChildren(_uniqueIdType, true);
                for (int i = 0; i < under.Length; i++)
                {
                    if (under[i] == null) continue;
                    int d = Depth(under[i].transform);
                    if (d < topDepth) { top = under[i]; topDepth = d; }
                }
            }
            catch (Exception) { return null; }
            if (top == null) return null;

            string topBefore = ReadString(_uidId, top);
            if (string.IsNullOrEmpty(topBefore) || stored.Contains(topBefore)) return null;   // this game's own state

            // Another save. Not only the player carries per-game ids: the
            // inventory's item views (Spear_Upgraded_Inv, CraftedBomb1...)
            // sit outside the player and were deleted and rebuilt from the
            // save as a SECOND inventory (author, v0.22.2). So every live
            // identifier the save lacks is matched, not just the player's.
            IList all;
            try { all = _allIdentifiers.GetValue(null, null) as IList; }
            catch (Exception) { return null; }
            if (all == null) return null;

            HashSet<string> live = new HashSet<string>();
            List<KeyValuePair<int, Component>> order = new List<KeyValuePair<int, Component>>();
            for (int i = 0; i < all.Count; i++)
            {
                Component u = all[i] as Component;
                if (u == null) continue;
                string id = ReadString(_uidId, u);
                if (string.IsNullOrEmpty(id)) continue;
                live.Add(id);
                if (!stored.Contains(id)) order.Add(new KeyValuePair<int, Component>(Depth(u.transform), u));
            }
            order.Sort(new ByDepth());

            Dictionary<string, List<SavedObject>> byName = new Dictionary<string, List<SavedObject>>();
            for (int s = 0; s < saved.Count; s++)
            {
                SavedObject o = saved[s];
                if (o.GameObjectName == null || live.Contains(o.Id)) continue;   // never give a live object's id away
                List<SavedObject> list;
                if (!byName.TryGetValue(o.GameObjectName, out list)) byName[o.GameObjectName] = list = new List<SavedObject>();
                list.Add(o);
            }

            HashSet<string> claimed = new HashSet<string>();
            HashSet<Component> done = new HashSet<Component>();
            int remapped = 0, unmatched = 0, playerUnmatched = 0;
            List<string> playerMisses = new List<string>();
            List<string> otherMisses = new List<string>();
            for (int i = 0; i < order.Count; i++)
            {
                Component u = order[i].Value;
                if (u == null || done.Contains(u)) continue;

                List<SavedObject> named;
                if (!byName.TryGetValue(u.gameObject.name, out named)) { Miss(u, top, playerRoot, ref unmatched, ref playerUnmatched, playerMisses, otherMisses, 0); continue; }

                Transform parent = u.transform.parent;
                Component parentUid = parent != null ? parent.GetComponent(_uniqueIdType) : null;
                string parentId = parentUid != null ? ReadString(_uidId, parentUid) : null;
                string classId = ReadString(_uidClassId, u);

                SavedObject match = null;
                int found = 0;
                for (int s = 0; s < named.Count; s++)
                {
                    SavedObject o = named[s];
                    if (claimed.Contains(o.Id)) continue;
                    if (!string.IsNullOrEmpty(o.ClassId) && !string.IsNullOrEmpty(classId) && o.ClassId != classId) continue;
                    if (u != top && (o.ParentId ?? "") != (parentId ?? "")) continue;
                    match = o;
                    found++;
                }

                if (found > 1)
                {
                    int paired = PairInOrder(u, top, named, parentId, classId, order, i, claimed, done);
                    if (paired > 0) { remapped += paired; continue; }
                }
                if (found != 1) { Miss(u, top, playerRoot, ref unmatched, ref playerUnmatched, playerMisses, otherMisses, found); continue; }
                try { _uidId.SetValue(u, match.Id, null); }
                catch (Exception) { Miss(u, top, playerRoot, ref unmatched, ref playerUnmatched, playerMisses, otherMisses, -1); continue; }
                claimed.Add(match.Id);
                remapped++;
            }

            string topAfter = ReadString(_uidId, top);
            if (!stored.Contains(topAfter))
            {
                _log.LogWarning("Savestate: refused - no saved object matches this game's player ('" +
                                top.gameObject.name + "' " + topBefore + ").");
                return "its player could not be matched to yours (see the log)";
            }

            note = "from another save: " + remapped + " id(s) adopted, " + unmatched + " left (" +
                   playerUnmatched + " on the player)";
            StringBuilder sb = new StringBuilder("Savestate: " + note + " ('" + top.gameObject.name + "' " +
                                                 topBefore + " -> " + topAfter + ")");
            for (int i = 0; i < playerMisses.Count; i++) sb.Append(i == 0 ? "; player misses: " : ", ").Append(playerMisses[i]);
            for (int i = 0; i < otherMisses.Count; i++) sb.Append(i == 0 ? "; other misses: " : ", ").Append(otherMisses[i]);
            _log.LogInfo(sb.Append('.').ToString());
            return null;
        }

        // Identical siblings - same name, parent and class, e.g. the three
        // PassengerManifest objects on the player (author, v0.22.3: "3
        // candidates" each, so none matched). When the live group and the
        // saved group are the same size, pair them in order: live by
        // sibling index, saved in the order the save lists them. Copies
        // that alike are interchangeable, so the order barely matters.
        private int PairInOrder(Component first, Component top, List<SavedObject> named, string parentId, string classId,
                                List<KeyValuePair<int, Component>> order, int from,
                                HashSet<string> claimed, HashSet<Component> done)
        {
            List<Component> group = new List<Component>();
            for (int i = from; i < order.Count; i++)
            {
                Component c = order[i].Value;
                if (c == null || c == top || done.Contains(c)) continue;
                if (c.gameObject.name != first.gameObject.name || c.transform.parent != first.transform.parent) continue;
                if (ReadString(_uidClassId, c) != classId) continue;
                group.Add(c);
            }

            List<SavedObject> candidates = new List<SavedObject>();
            for (int s = 0; s < named.Count; s++)
            {
                SavedObject o = named[s];
                if (claimed.Contains(o.Id)) continue;
                if (!string.IsNullOrEmpty(o.ClassId) && !string.IsNullOrEmpty(classId) && o.ClassId != classId) continue;
                if ((o.ParentId ?? "") != (parentId ?? "")) continue;
                candidates.Add(o);
            }

            if (group.Count < 2 || group.Count != candidates.Count) return 0;

            group.Sort(delegate(Component a, Component b)
            {
                return a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex());
            });

            int n = 0;
            for (int k = 0; k < group.Count; k++)
            {
                try { _uidId.SetValue(group[k], candidates[k].Id, null); }
                catch (Exception) { continue; }
                claimed.Add(candidates[k].Id);
                done.Add(group[k]);
                n++;
            }
            return n;
        }

        // Unmatched objects: counted, and a few named - those on the player
        // are what could still come back doubled; the rest say why the
        // adoption missed (parent path and how many saved objects matched).
        private void Miss(Component u, Component top, Transform playerRoot, ref int unmatched, ref int playerUnmatched,
                          List<string> names, List<string> others, int candidates)
        {
            unmatched++;
            string why = candidates > 1 ? " (" + candidates + " candidates)" : candidates == 0 ? " (none)" : "";
            if (!u.transform.IsChildOf(playerRoot))
            {
                if (others.Count < 6) others.Add(Path(u.transform) + why);
                return;
            }
            playerUnmatched++;
            if (names.Count < 8) names.Add(u.gameObject.name + why);
        }

        private static int Depth(Transform t)
        {
            int d = 0;
            for (Transform p = t.parent; p != null; p = p.parent) d++;
            return d;
        }

        // Upgrade receivers are exempt. They are scene objects on the
        // inventory's weapon views (UpgradeViewReceiver: _currentUpgrades,
        // the implanted feathers/teeth/glass) and the game never removes
        // one - OnDeserialized destroys only a stand-in the loader built
        // (it has an EmptyObjectIdentifier). A savestate from another save
        // could not match their per-game ids, and v0.22.x deleted 51 of them
        // (author's log), which would leave the upgrade cog with nothing to
        // implant into until a real load. Unmatched, they keep this game's
        // upgrades; the save's own copies come back as stand-ins and destroy
        // themselves.
        // Held items are exempt too. A held model that is not in hand is
        // unparented to the scene root (FakeParent.UnParent: parent = null),
        // so it is outside the player while it waits. Its "collide" child
        // carries the weapon's weaponInfo; deleting it (v0.24.7, author:
        // every in-place restore of a cross-save state deleted ~20
        // "collide" objects) left the main hit trigger's
        // currentWeaponScript destroyed, and weaponInfo.OnTriggerEnter
        // returns at once when it is null: no chop, no hit, no panel -
        // and re-equipping could not help, because the script that links
        // the held weapon (setupHeldWeapon, on its OnEnable) was gone.
        private int DeleteUnsaved(HashSet<string> stored, Transform keepRoot, List<string> names, ref int keptReceivers, ref int keptHeld)
        {
            if (_allIdentifiers == null || _uidId == null) return 0;

            IList all;
            try { all = _allIdentifiers.GetValue(null, null) as IList; }
            catch (Exception) { return 0; }
            if (all == null) return 0;

            // Copy first: Destroy -> OnDestroy removes from AllIdentifiers.
            Component[] snapshot = new Component[all.Count];
            for (int i = 0; i < all.Count; i++) snapshot[i] = all[i] as Component;

            int n = 0;
            for (int i = 0; i < snapshot.Length; i++)
            {
                Component u = snapshot[i];
                if (u == null) continue;
                if (keepRoot != null && u.transform.IsChildOf(keepRoot)) continue;

                string id = ReadString(_uidId, u);
                if (string.IsNullOrEmpty(id) || stored.Contains(id)) continue;
                if (_upgradeReceiverType != null && u.GetComponent(_upgradeReceiverType) != null) { keptReceivers++; continue; }
                if (HeldByPlayer(u.transform, keepRoot)) { keptHeld++; continue; }

                Transform parent = u.transform.parent;
                names.Add(parent != null ? parent.name + "/" + u.gameObject.name : u.gameObject.name);
                CancelBuildMissions(u.gameObject);
                UnityEngine.Object.Destroy(u.gameObject);
                n++;
            }
            return n;
        }

        // True when t, or an object above it, is a FakeParent whose hand
        // bone is under the player.
        private bool HeldByPlayer(Transform t, Transform keepRoot)
        {
            if (keepRoot == null || _fakeParentType == null || _fakeParentTarget == null) return false;
            for (Transform p = t; p != null; p = p.parent)
            {
                Component fp = p.GetComponent(_fakeParentType);
                if (fp == null) continue;
                Transform target = null;
                try { target = _fakeParentTarget.GetValue(fp) as Transform; }
                catch (Exception) { }
                if (target != null && target.IsChildOf(keepRoot)) return true;
            }
            return false;
        }

        // What Craft_Structure.SpawnBackIngredients does for the HUD, without
        // spawning the committed logs back into the world: for each
        // ingredient i with both entries set,
        //   BuildMission.AddNeededToBuildMission(required[i]._itemID,
        //       -(required[i]._amount - present[i]._amount), true)
        private void CancelBuildMissions(GameObject go)
        {
            if (_craftStructureType == null || _requiredIngredients == null ||
                _presentIngredients == null || _addNeededToMission == null) return;

            Component[] ghosts = go.GetComponentsInChildren(_craftStructureType, true);
            for (int g = 0; g < ghosts.Length; g++)
            {
                try
                {
                    IList required = _requiredIngredients.GetValue(ghosts[g]) as IList;
                    Array present = _presentIngredients.GetValue(ghosts[g]) as Array;
                    if (required == null || present == null) continue;

                    for (int i = 0; i < required.Count && i < present.Length; i++)
                    {
                        object req = required[i];
                        object have = present.GetValue(i);
                        if (req == null || have == null) continue;

                        int itemId = (int)IngredientField(req, "_itemID");
                        int remaining = (int)IngredientField(req, "_amount") - (int)IngredientField(have, "_amount");
                        _addNeededToMission.Invoke(null, new object[] { itemId, -remaining, true });
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning("Savestate: could not clear the build mission for " + go.name + ": " +
                                    (ex.InnerException ?? ex).Message);
                }
            }
        }

        private static object IngredientField(object ingredient, string name)
        {
            // BuildIngredients derives from ReceipeIngredient; walk up for the field.
            for (Type t = ingredient.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(ingredient);
            }
            throw new MissingFieldException(ingredient.GetType().Name, name);
        }

        // The inventory restore rewrites the bag but not the hands: a stick
        // held during the restore survived it (author, v0.20.0).
        private void StashHands()
        {
            try
            {
                object inv = _inventory != null ? _inventory.GetValue(null) : null;
                if (inv == null) return;
                if (_stashWeapon != null) _stashWeapon.Invoke(inv, new object[] { false });
                if (_stashLeftHand != null) _stashLeftHand.Invoke(inv, null);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Savestate: stashing held items failed: " + (ex.InnerException ?? ex).Message);
            }
        }

        private static bool HoldsAll(List<int> held, List<int> wanted)
        {
            for (int i = 0; i < wanted.Count; i++)
                if (!held.Contains(wanted[i])) return false;
            return true;
        }

        private string HandsBusyWhy()
        {
            try
            {
                if (_lighterBusy != null && (bool)_lighterBusy.GetValue(null)) return "the lighter's own routine running (LighterControler.IsBusy)";
                return "the left-hand slot locked by the game";
            }
            catch (Exception ex) { return "unknown (" + ex.Message + ")"; }
        }

        private bool HandsBusy()
        {
            try
            {
                if (_lighterBusy != null && (bool)_lighterBusy.GetValue(null)) return true;
                object inv = _inventory != null ? _inventory.GetValue(null) : null;
                if (inv == null || _isSlotLocked == null || _leftHandSlot == null) return false;
                return (bool)_isSlotLocked.Invoke(inv, new[] { _leftHandSlot });
            }
            catch (Exception) { return false; }
        }

        /// Item ids in the equipment slots now (hands first), read from the
        /// live slots - `_equipmentSlotsIds` is only filled when the game
        /// saves (PlayerInventory.OnSerializing), from these same views.
        public List<int> HeldIds()
        {
            List<int> ids = new List<int>();
            if (!Resolve() || _equipmentSlots == null || _viewItemId == null) return ids;
            try
            {
                object inv = _inventory != null ? _inventory.GetValue(null) : null;
                Array slots = inv != null ? _equipmentSlots.GetValue(inv) as Array : null;
                if (slots == null) return ids;
                object none = _noEquipedItem != null ? _noEquipedItem.GetValue(inv) : null;

                for (int i = 0; i < slots.Length; i++)
                {
                    UnityEngine.Object v = slots.GetValue(i) as UnityEngine.Object;
                    if (v == null || ReferenceEquals(v, none)) continue;
                    int id = (int)_viewItemId.GetValue(v);
                    if (id > 0 && !ids.Contains(id)) ids.Add(id);
                }
            }
            catch (Exception ex) { _log.LogWarning("Savestate: reading held items failed: " + ex.Message); }
            return ids;
        }

        /// The inventory's "previously equipped" memory, "slot:itemId" per
        /// slot that holds one. A cutscene's HideAllEquiped writes the held
        /// weapon there (MemorizeItem) and its ShowAllEquiped re-equips it
        /// (EquipPreviousWeapon / EquipPreviousUtility); the save does not
        /// hold it, so a restored Megan cutscene ended empty-handed (maks,
        /// v0.24.25: "spear not pulled out").
        public List<string> PreviousHeld()
        {
            List<string> list = new List<string>();
            if (!Resolve() || _equipmentSlotsPrevious == null || _viewItemId == null) return list;
            try
            {
                object inv = _inventory != null ? _inventory.GetValue(null) : null;
                Array slots = inv != null ? _equipmentSlotsPrevious.GetValue(inv) as Array : null;
                if (slots == null) return list;
                object none = _noEquipedItem != null ? _noEquipedItem.GetValue(inv) : null;
                for (int i = 0; i < slots.Length; i++)
                {
                    UnityEngine.Object v = slots.GetValue(i) as UnityEngine.Object;
                    if (v == null || ReferenceEquals(v, none)) continue;
                    int id = (int)_viewItemId.GetValue(v);
                    if (id > 0) list.Add(i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                                         id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            catch (Exception ex) { _log.LogWarning("Savestate: reading the previously equipped items failed: " + ex.Message); }
            return list;
        }

        /// Writes PreviousHeld() entries back; returns how many slots were set.
        public int SetPreviousHeld(List<string> entries)
        {
            if (entries == null || entries.Count == 0) return 0;
            if (!Resolve() || _equipmentSlotsPrevious == null || _itemViewsCache == null) return 0;
            int n = 0;
            try
            {
                object inv = _inventory != null ? _inventory.GetValue(null) : null;
                Array slots = inv != null ? _equipmentSlotsPrevious.GetValue(inv) as Array : null;
                IDictionary cache = inv != null ? _itemViewsCache.GetValue(inv) as IDictionary : null;
                if (slots == null || cache == null) return 0;
                for (int k = 0; k < entries.Count; k++)
                {
                    string[] parts = entries[k].Split(':');
                    int slot, id;
                    if (parts.Length != 2 || !int.TryParse(parts[0], out slot) || !int.TryParse(parts[1], out id)) continue;
                    if (slot < 0 || slot >= slots.Length || !cache.Contains(id)) continue;
                    IList views = cache[id] as IList;
                    if (views == null || views.Count == 0 || (views[0] as UnityEngine.Object) == null) continue;
                    slots.SetValue(views[0], slot);
                    n++;
                }
            }
            catch (Exception ex) { _log.LogWarning("Savestate: writing the previously equipped items failed: " + ex.Message); }
            return n;
        }

        /// After an in-place restore: equips each item held at capture that
        /// is not held now, the way the game's own load does
        /// (PlayerInventory.OnDeserialized: Equip(id, ...)). A short wait
        /// first, so the restore's own OnDeserialized routines have run, then
        /// up to 2 s for the game's own equip: it lands later than 0.3 s, and
        /// v0.24.1-0.24.9 called Equip meanwhile and logged "Equip refused"
        /// for items that came back anyway (author's log). `done` gets one
        /// line for the log.
        public IEnumerator ReEquip(List<int> wanted, Func<int, string> nameOf, Action<string> done)
        {
            yield return new WaitForSecondsRealtime(0.3f);

            float start = Time.realtimeSinceStartup;
            while (HandsBusy() && Time.realtimeSinceStartup - start < 2f) yield return null;

            List<int> first = HeldIds();
            List<int> now = first;
            start = Time.realtimeSinceStartup;
            while (!HoldsAll(now, wanted) && Time.realtimeSinceStartup - start < 2f)
            {
                yield return new WaitForSecondsRealtime(0.1f);
                now = HeldIds();
            }

            StringBuilder sb = new StringBuilder("held at capture:");
            try
            {
                object inv = _inventory != null ? _inventory.GetValue(null) : null;
                for (int i = 0; i < wanted.Count; i++)
                {
                    int id = wanted[i];
                    sb.Append(i == 0 ? " " : ", ").Append(nameOf(id));
                    if (first.Contains(id)) { sb.Append(" (held)"); continue; }
                    if (now.Contains(id)) { sb.Append(" (re-equipped by the game)"); continue; }
                    if (inv == null || _equipById == null) { sb.Append(" (cannot equip: not bound)"); continue; }

                    bool ok = (bool)_equipById.Invoke(inv, new object[] { id, false });
                    sb.Append(ok ? " (re-equipped)" : " (Equip refused)");
                }
            }
            catch (Exception ex)
            {
                sb.Append(" | re-equip failed: ").Append((ex.InnerException ?? ex).Message);
            }
            done(sb.ToString());
        }

        /// After a Full load: puts away and equips again what the hands held
        /// at capture. The load equips them while the player is still being
        /// set up, and the animator never got the item's flag (`axeHeld`,
        /// `lighterHeld` off): the axe hung at the side, the lighter clicked
        /// without light (author, v0.24.43). A stash and Equip through the
        /// bridge set the flags and the arm layers came back.
        /// Putting the lighter away is animated and locks the left hand
        /// until it ends; swinging through a restart it outlasted a fixed
        /// 0.5 s and Equip was refused - the lighter gone (runner
        /// sxczurass, bridge v0.24.45). So wait until the hands are free,
        /// and retry a refused Equip for a while.
        public IEnumerator RefreshHeld(List<int> wanted, Func<int, string> nameOf, Action<string> done)
        {
            yield return new WaitForSecondsRealtime(0.3f);
            float start = Time.realtimeSinceStartup;
            while (HandsBusy() && Time.realtimeSinceStartup - start < 2f) yield return null;

            object inv = _inventory != null ? _inventory.GetValue(null) : null;
            if (inv == null || _equipById == null || _stashWeapon == null || _stashLeftHand == null)
            {
                done("held at capture: not refreshed (not bound)");
                yield break;
            }
            string error = null;
            try
            {
                _stashWeapon.Invoke(inv, new object[] { false });
                _stashLeftHand.Invoke(inv, null);
            }
            catch (Exception ex) { error = (ex.InnerException ?? ex).Message; }
            if (error != null) { done("held at capture: putting away failed (" + error + ")"); yield break; }

            yield return new WaitForSecondsRealtime(0.5f);
            float freeStart = Time.realtimeSinceStartup;
            while (HandsBusy() && Time.realtimeSinceStartup - freeStart < 3f) yield return null;
            float busyFor = Time.realtimeSinceStartup - freeStart;

            // Equip each; a refused one is tried again every 0.1 s for 2 s.
            bool[] done_ = new bool[wanted.Count];
            int[] tries = new int[wanted.Count];
            string failure = null;
            float equipStart = Time.realtimeSinceStartup;
            while (true)
            {
                bool all = true;
                try
                {
                    for (int i = 0; i < wanted.Count; i++)
                    {
                        if (done_[i]) continue;
                        tries[i]++;
                        done_[i] = (bool)_equipById.Invoke(inv, new object[] { wanted[i], false });
                        if (!done_[i]) all = false;
                    }
                }
                catch (Exception ex) { failure = (ex.InnerException ?? ex).Message; break; }
                if (all || Time.realtimeSinceStartup - equipStart >= 2f) break;
                yield return new WaitForSecondsRealtime(0.1f);
            }

            StringBuilder sb = new StringBuilder("held at capture, equipped again for the animator:");
            for (int i = 0; i < wanted.Count; i++)
            {
                sb.Append(i == 0 ? " " : ", ").Append(nameOf(wanted[i]));
                if (!done_[i]) sb.Append(" (Equip refused ").Append(tries[i]).Append("x)");
                else if (tries[i] > 1) sb.Append(" (on try ").Append(tries[i]).Append(')');
            }
            if (busyFor >= 0.05f) sb.Append(" - hands busy ").Append(busyFor.ToString("0.0")).Append(" s first");
            if (failure != null) sb.Append(" | failed: ").Append(failure);
            done(sb.ToString());
        }

        // After an in-place restore, enemies as after a load (game-notes
        // *Enemies across an in-place restore*). v0.24.5-0.24.9 ran
        // restartEnemiesFromPauseMenu, which REMOVES every enemy when
        // currentMaxActiveMutants is 0 - and that is 0 whenever
        // Cheats.NoEnemies holds, which in Creative is "Allow enemies" off
        // (PlayerPreferences.AllowEnemiesCreative; the author's game had it
        // off while fighting cave enemies, and every restore removed them).
        // Now: enemies off -> leave them; the restore's own NotInACave
        // already restarted the families (PlayerStats.NotInACave calls
        // startSetupFamilies unless delayedMutantSpawnCheck) -> nothing
        // more, a second setupFamilies would run beside the first;
        // otherwise the game's startSetupFamilies.
        public string RespawnEnemies(bool surfaceMessageSent)
        {
            if (!Resolve() || _mutantControler == null || _startSetupFamilies == null) return "enemies: not bound";
            try
            {
                MonoBehaviour ctrl = _mutantControler.GetValue(null) as MonoBehaviour;
                if (ctrl == null) return "enemies: no spawn controller in this scene";

                if (_hordeActive != null && (bool)_hordeActive.GetValue(ctrl)) return "enemies: horde mode - left to the game";

                bool off = false;
                try { off = _noEnemies != null && (bool)_noEnemies.GetValue(null, null); }
                catch (Exception) { }
                if (off) return "enemies: off in this game (Peaceful, or Creative with 'Allow enemies' off) - left as they are";

                if (surfaceMessageSent && !DelayedMutantSpawnCheck())
                    return "enemies: families restarted by the game (leaving the cave state)";

                _startSetupFamilies.Invoke(ctrl, null);
                return "enemies: families restarted (the game's setup)";
            }
            catch (Exception ex)
            {
                return "enemies: respawn failed (" + (ex.InnerException ?? ex).Message + ")";
            }
        }

        private bool DelayedMutantSpawnCheck()
        {
            try
            {
                object stats = _playerStats != null ? _playerStats.GetValue(null) : null;
                return stats != null && _delayedSpawnCheck != null && (bool)_delayedSpawnCheck.GetValue(stats);
            }
            catch (Exception) { return false; }
        }

        // Dead cannibals are clones of clsragdollify.vargamragdoll made at
        // death (clsragdollify.metgoragdoll: Instantiate at the scene root),
        // with no save identifier, so neither LoadNow nor the delete step
        // touches them (author, v0.24.7: bodies and severed limbs stayed
        // after an in-place restore). Author, 2026-09-24: an in-place
        // restore clears them. Clones that DO carry an identifier are the
        // serializer's and are left alone.
        public string ClearCorpses()
        {
            if (!Resolve()) return "bodies: not bound";
            try
            {
                if ((_ragdollNames == null || _ragdollNames.Count == 0) && _ragdollifyType != null && _ragdollPrefab != null)
                {
                    HashSet<string> names = new HashSet<string>();
                    UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(_ragdollifyType);
                    for (int i = 0; i < all.Length; i++)
                    {
                        Transform prefab = all[i] != null ? _ragdollPrefab.GetValue(all[i]) as Transform : null;
                        if (prefab != null) names.Add(prefab.name + "(Clone)");
                    }
                    if (names.Count > 0) _ragdollNames = names;
                }

                int removed = 0, kept = 0;
                for (int s = 0; s < SceneManager.sceneCount; s++)
                {
                    UnityEngine.SceneManagement.Scene scene = SceneManager.GetSceneAt(s);
                    if (!scene.isLoaded) continue;
                    GameObject[] roots = scene.GetRootGameObjects();
                    for (int i = 0; i < roots.Length; i++)
                    {
                        GameObject go = roots[i];
                        if (go == null || !IsBody(go.name)) continue;
                        // The root's own id only: a body's weapon prop carries
                        // one deep inside (FireStick/StickFlame/Sparks, seen
                        // live 2026-09-24), which kept every body.
                        if (_uniqueIdType != null && go.GetComponent(_uniqueIdType) != null) { kept++; continue; }
                        UnityEngine.Object.Destroy(go);
                        removed++;
                    }
                }
                return "bodies: " + removed + " removed" + (kept > 0 ? ", " + kept + " with a save id kept" : "");
            }
            catch (Exception ex)
            {
                return "bodies: clearing failed (" + (ex.InnerException ?? ex).Message + ")";
            }
        }

        // A body is a scene-root clone of mutantTypeSetup.dummyMutant -
        // "mutant_male_Dummy(Clone)" and its kin (seen live through the test
        // bridge, 2026-09-24: dummyTypeSetup, destroyAfter, setupFeeding, no
        // save identifier). The ragdoll clones are the older rule.
        private bool IsBody(string name)
        {
            if (name.EndsWith("_Dummy(Clone)", StringComparison.Ordinal)) return true;
            return _ragdollNames != null && _ragdollNames.Contains(name);
        }

        /// The plane wreck: PlaneCrashController.OnDeserialized schedules
        /// setupCrashedPlane (0.3 s), and loadCrashPlane instantiates a new
        /// Hull(Clone) into spawnedHullPrefab without destroying the old one
        /// - a load starts from none, an in-place restore added one more each
        /// time, with every wreck pickup (the growing "Axe Plane xN").
        /// Call a second or so after the restore: removes the other wrecks.
        public string ClearOldPlaneHulls()
        {
            if (_planeCrash == null || _spawnedHull == null) return "plane: not bound";
            try
            {
                object ctrl = _planeCrash.GetValue(null);
                GameObject current = ctrl != null ? _spawnedHull.GetValue(ctrl) as GameObject : null;
                if (current == null) return "plane: no wreck";

                int removed = 0;
                for (int s = 0; s < SceneManager.sceneCount; s++)
                {
                    UnityEngine.SceneManagement.Scene scene = SceneManager.GetSceneAt(s);
                    if (!scene.isLoaded) continue;
                    GameObject[] roots = scene.GetRootGameObjects();
                    for (int i = 0; i < roots.Length; i++)
                    {
                        GameObject go = roots[i];
                        if (go == null || go == current || go.name != current.name) continue;
                        UnityEngine.Object.Destroy(go);
                        removed++;
                    }
                }
                return "plane: " + (removed > 0 ? removed + " old wreck(s) removed" : "one wreck");
            }
            catch (Exception ex) { return "plane: failed (" + (ex.InnerException ?? ex).Message + ")"; }
        }

        /// The wreck the game keeps (PlaneCrashController.spawnedHullPrefab),
        /// or null.
        public GameObject CurrentPlaneHull()
        {
            if (_planeCrash == null || _spawnedHull == null) return null;
            try
            {
                object ctrl = _planeCrash.GetValue(null);
                return ctrl != null ? _spawnedHull.GetValue(ctrl) as GameObject : null;
            }
            catch (Exception) { return null; }
        }

        /// Blood on the player is PlayerStats.IsBloody plus the skin and
        /// weapon it painted; the save does not hold it. The game's own wash
        /// (GotCleanReal, what water does) clears it - and mud and burning
        /// too. Confirmed in game through the bridge (author, 2026-09-24).
        public string Wash()
        {
            if (_gotCleanReal == null || _isBloody == null || _playerStats == null) return "blood: not bound";
            try
            {
                object stats = _playerStats.GetValue(null);
                if (stats == null) return "blood: no player";
                bool before = (bool)_isBloody.GetValue(stats, null);
                _gotCleanReal.Invoke(stats, null);
                bool after = (bool)_isBloody.GetValue(stats, null);
                if (!before) return "";
                return after ? "blood: still bloody (the game refused the wash)" : "blood: washed";
            }
            catch (Exception ex) { return "blood: wash failed (" + (ex.InnerException ?? ex).Message + ")"; }
        }

        /// Live cannibals and live family spawners, or -1 when unknown.
        public void CountEnemies(out int cannibals, out int families)
        {
            cannibals = families = -1;
            try
            {
                object ctrl = _mutantControler != null ? _mutantControler.GetValue(null) : null;
                if (ctrl == null || _activeCannibals == null || _allWorldSpawns == null) return;
                cannibals = CountLive(_activeCannibals.GetValue(ctrl) as IList);
                families = CountLive(_allWorldSpawns.GetValue(ctrl) as IList);
            }
            catch (Exception) { }
        }

        private static int CountLive(IList list)
        {
            if (list == null) return -1;
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                UnityEngine.Object o = list[i] as UnityEngine.Object;
                if (o != null) n++;
            }
            return n;
        }

        /// The families' setup that an in-place restore starts dies partway
        /// (seen live, 2026-09-24): it despawns every cannibal and destroys
        /// the spawners, then spawned none (first test) or one family of six
        /// (second) and stopped, while the same call a few seconds later
        /// rebuilt them all. Call once the restore has settled, with the
        /// live family count from just before it: fewer now -> run it again.
        public string EnsureEnemies(int familiesBefore)
        {
            if (_startSetupFamilies == null) return "enemies: not bound";
            try
            {
                MonoBehaviour ctrl = _mutantControler != null ? _mutantControler.GetValue(null) as MonoBehaviour : null;
                if (ctrl == null) return "enemies: no spawn controller";
                if (_hordeActive != null && (bool)_hordeActive.GetValue(ctrl)) return "enemies: horde mode";
                bool off = false;
                try { off = _noEnemies != null && (bool)_noEnemies.GetValue(null, null); }
                catch (Exception) { }
                if (off) return "enemies: off in this game";

                int cannibals, families;
                CountEnemies(out cannibals, out families);
                string now = cannibals + " active, " + families + " famil" + (families == 1 ? "y" : "ies");
                bool short_ = (cannibals == 0 && families == 0) || (familiesBefore > 0 && families >= 0 && families < familiesBefore);
                if (!short_) return "enemies: " + now + " (" + familiesBefore + " before)";
                _startSetupFamilies.Invoke(ctrl, null);
                return "enemies: " + now + " of " + familiesBefore + " families before - the game's setup run again";
            }
            catch (Exception ex) { return "enemies: check failed (" + (ex.InnerException ?? ex).Message + ")"; }
        }

        /// Diagnostic: scene-root objects whose name suggests a dead enemy
        /// (body / ragdoll / dead / mutant / cannibal), counted by name.
        /// v0.24.10's ragdoll-clone rule removed nothing in the author's
        /// Normal game while bodies stayed, so the bodies are something
        /// else - this names them for the next fix.
        public string DescribeBodyCandidates()
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                UnityEngine.SceneManagement.Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    if (roots[i] == null || !roots[i].activeInHierarchy) continue;
                    string n = roots[i].name;
                    string l = n.ToLowerInvariant();
                    if (l.IndexOf("body") < 0 && l.IndexOf("ragdoll") < 0 && l.IndexOf("dead") < 0 &&
                        l.IndexOf("mutant") < 0 && l.IndexOf("cannibal") < 0 && l.IndexOf("corpse") < 0) continue;
                    int c;
                    counts.TryGetValue(n, out c);
                    counts[n] = c + 1;
                }
            }
            if (counts.Count == 0) return null;
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, int> kv in counts)
            {
                if (sb.Length > 900) { sb.Append(", ..."); break; }
                sb.Append(sb.Length == 0 ? "" : ", ").Append(kv.Key).Append(" x").Append(kv.Value);
            }
            return sb.ToString();
        }


        // ------------------------------------------------------------------
        // Save-routine steps. Each is individually guarded: a missing piece
        // is logged, not thrown.

        private void DropGlider()
        {
            try
            {
                object anim = _animControl != null ? _animControl.GetValue(null) : null;
                if (anim == null || _holdingGlider == null || !(bool)_holdingGlider.GetValue(anim)) return;
                GameObject actions = _specialActions != null ? _specialActions.GetValue(null) as GameObject : null;
                if (actions != null) actions.SendMessage("DropGlider", false);
            }
            catch (Exception) { }
        }

        /// ForcedUnload(unload) on the greeble zones and every cave scene
        /// loader, as the game's save does. CheckInCave follows only on the
        /// way in, matching the game.
        private bool ForceUnloadStreaming(bool unload, bool checkInCave)
        {
            if (_greebleForcedUnload == null || _caveForcedUnload == null) return false;
            try
            {
                object greeble = _greebleManager != null ? _greebleManager.GetValue(null) : null;
                if (greeble != null)
                {
                    _greebleForcedUnload.Invoke(greeble, new object[] { unload });
                    if (checkInCave && _greebleCheckInCave != null) _greebleCheckInCave.Invoke(greeble, null);
                }

                Array loaders = _sceneLoaders != null ? _sceneLoaders.GetValue(null) as Array : null;
                if (loaders != null)
                {
                    for (int i = 0; i < loaders.Length; i++)
                    {
                        object l = loaders.GetValue(i);
                        UnityEngine.Object uo = l as UnityEngine.Object;
                        if (l == null || (uo != null && uo == null)) continue;
                        _caveForcedUnload.Invoke(l, new object[] { unload });
                        if (checkInCave && _caveCheckInCave != null) _caveCheckInCave.Invoke(l, null);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Savestate: ForcedUnload(" + unload + ") failed: " + (ex.InnerException ?? ex).Message);
                return false;
            }
        }

        /// ReParent (before saving) / UnParent (after) on held items that
        /// are inactive and carry a FakeParent - the game's exact filter.
        private void ReParentHeld(bool reParent)
        {
            MethodInfo m = reParent ? _reParent : _unParent;
            if (m == null || _itemSlots == null || _available == null || _fakeParentType == null) return;
            try
            {
                Array slots = _itemSlots.GetValue(null) as Array;
                if (slots == null) return;
                for (int s = 0; s < slots.Length; s++)
                {
                    object slot = slots.GetValue(s);
                    if (slot == null) continue;
                    GameObject[] items = _available.GetValue(slot) as GameObject[];
                    if (items == null) continue;
                    for (int i = 0; i < items.Length; i++)
                    {
                        GameObject go = items[i];
                        if (go == null || go.activeSelf) continue;
                        Component fp = go.GetComponent(_fakeParentType);
                        if (fp != null) m.Invoke(fp, null);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning("Savestate: " + (reParent ? "ReParent" : "UnParent") + " failed: " + (ex.InnerException ?? ex).Message);
            }
        }

        private string DifficultyName()
        {
            try
            {
                if (_isCreative != null && (bool)_isCreative.GetValue(null, null)) return "Creative";
                if (_difficulty != null) return _difficulty.GetValue(null, null).ToString();
            }
            catch (Exception) { }
            return "";
        }

        // ------------------------------------------------------------------
        private static void Call(MethodInfo m)
        {
            if (m == null) return;
            try { m.Invoke(null, null); }
            catch (Exception) { }
        }

        private static bool ReadBool(PropertyInfo p)
        {
            if (p == null) return false;
            try { return (bool)p.GetValue(null, null); }
            catch (Exception) { return false; }
        }

        private static string ReadString(PropertyInfo p, object o)
        {
            if (p == null) return null;
            try { return p.GetValue(o, null) as string; }
            catch (Exception) { return null; }
        }

        private static string Path(Transform t)
        {
            string s = t.name;
            int depth = 0;
            while (t.parent != null && depth++ < 4) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }

        public static string Kb(int chars)
        {
            return chars >= 1024 * 1024
                ? (chars / (1024f * 1024f)).ToString("0.0") + " MB"
                : (chars / 1024) + " KB";
        }
    }
}
