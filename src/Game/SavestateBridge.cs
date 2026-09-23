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
        private PropertyInfo _allPrefabs;
        private FieldInfo _playerName;
        private Type _levelLoaderType;

        // Slot file
        private MethodInfo _prefsGetString;      // PlayerPrefsFile.GetString(string, string, bool)
        private MethodInfo _deserializeEntry;    // UnitySerializer.Deserialize<SaveEntry>(byte[])
        private FieldInfo _entryData;

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

        // Hands
        private FieldInfo _inventory;             // LocalPlayer.Inventory
        private MethodInfo _stashWeapon;          // PlayerInventory.StashEquipedWeapon(bool)
        private MethodInfo _stashLeftHand;        // PlayerInventory.StashLeftHand()

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
        private PropertyInfo _slot;

        // Save-routine steps
        private FieldInfo _memorySafe;
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
        private MethodInfo _reParent;
        private MethodInfo _unParent;
        private FieldInfo _animControl;
        private FieldInfo _holdingGlider;
        private FieldInfo _specialActions;
        private PropertyInfo _inOverlook;
        private FieldInfo _finishGameLoad;

        // Pickup diagnostics
        private Type _pickUpType;
        private FieldInfo _pickUpItemId;
        private FieldInfo _pickUpDestroyTarget;

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
                _allPrefabs = ls.GetProperty("AllPrefabs", stat);
                _playerName = ls.GetField("PlayerName", stat);

                if (_levelLoaderType != null)
                {
                    Type complete = typeof(Action<>).MakeGenericType(_levelLoaderType);
                    _loadNow = ls.GetMethod("LoadNow", stat, null,
                        new[] { typeof(object), typeof(bool), typeof(bool), complete }, null);
                }

                Type entry = ls.GetNestedType("SaveEntry", BindingFlags.Public | BindingFlags.NonPublic);
                if (entry != null) _entryData = entry.GetField("Data", inst);

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
                            if (entry != null) _deserializeEntry = m.MakeGenericMethod(entry);
                            if (levelData != null) _deserializeLevelData = m.MakeGenericMethod(levelData);
                            break;
                        }
                    }
                }

                Type compression = GameBridge.FindGameType("CompressionHelper");
                if (compression != null)
                    _decompress = compression.GetMethod("Decompress", stat, null, new[] { typeof(string) }, null);
            }

            Type prefs = GameBridge.FindGameType("PlayerPrefsFile");
            if (prefs != null)
                _prefsGetString = prefs.GetMethod("GetString", stat, null,
                    new[] { typeof(string), typeof(string), typeof(bool) }, null);

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
                _slot = setup.GetProperty("Slot", stat);
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

            Type pp = GameBridge.FindGameType("PlayerPreferences");
            if (pp != null) _memorySafe = pp.GetField("MemorySafeSaveMode", stat);

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
            }

            Type slotType = GameBridge.FindGameType("itemConstrainToHand");
            if (slotType != null) _available = slotType.GetField("Available", inst);

            _fakeParentType = GameBridge.FindGameType("TheForest.Utils.FakeParent");
            if (_fakeParentType != null)
            {
                _reParent = _fakeParentType.GetMethod("ReParent", inst, null, Type.EmptyTypes, null);
                _unParent = _fakeParentType.GetMethod("UnParent", inst, null, Type.EmptyTypes, null);
            }

            Type anim = GameBridge.FindGameType("playerAnimatorControl");
            if (anim != null) _holdingGlider = anim.GetField("holdingGlider", inst);

            _pickUpType = GameBridge.FindGameType("TheForest.Items.World.PickUp");
            if (_pickUpType != null)
            {
                _pickUpItemId = _pickUpType.GetField("_itemId", inst);
                _pickUpDestroyTarget = _pickUpType.GetField("_destroyTarget", inst);
            }

            Status = "serialize:" + (_serializeLevel != null) +
                     " loadNow:" + (_loadNow != null) +
                     " loadSaved:" + (_loadSavedLevel != null) +
                     " resume:" + (_resume != null) +
                     " slotRead:" + (_prefsGetString != null && _deserializeEntry != null && _entryData != null) +
                     " diff:" + (_deserializeLevelData != null && _storedObjectNames != null && _storedItemName != null && _decompress != null) +
                     " stash:" + (_stashWeapon != null && _stashLeftHand != null) +
                     " missions:" + (_requiredIngredients != null && _presentIngredients != null && _addNeededToMission != null) +
                     " streaming:" + (_greebleForcedUnload != null && _caveForcedUnload != null) +
                     " fakeParent:" + (_reParent != null) +
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

        public string CurrentSlot
        {
            get
            {
                if (_slot == null) return "?";
                try { return _slot.GetValue(null, null).ToString(); }
                catch (Exception) { return "?"; }
            }
        }

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
            int deleted = stored != null ? DeleteUnsaved(stored, keepRoot, deletedNames) : 0;
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
                }
                sb.Append(", 'not found' ").Append(_logNotFound);
                sb.Append(", problems ").Append(_logProblems);
                sb.Append(", streaming ").Append(unloadStreaming ? (unloaded ? "force-unloaded" : "unload FAILED") : "kept");
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

        /// The level data inside the current slot's save file, or null
        /// with `error` set.
        public string ReadSlotData(out string error)
        {
            error = null;
            if (!Resolve() || _prefsGetString == null || _deserializeEntry == null || _entryData == null)
            {
                error = "slot reader not bound";
                return null;
            }

            try
            {
                string key = (_playerName != null ? _playerName.GetValue(null) as string : "") + "__RESUME__";
                string b64 = _prefsGetString.Invoke(null, new object[] { key, "", true }) as string;
                if (string.IsNullOrEmpty(b64)) { error = "slot " + CurrentSlot + " has no save"; return null; }

                object entry = _deserializeEntry.Invoke(null, new object[] { Convert.FromBase64String(b64) });
                string data = entry != null ? _entryData.GetValue(entry) as string : null;
                if (string.IsNullOrEmpty(data)) error = "save entry held no data";
                return data;
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                error = "could not read the slot save: " + inner.Message;
                return null;
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
        /// game). Every identifier under the player that the save lacks is
        /// given the id of the saved object with the same GameObject name,
        /// prefab class and parent id - shallowest first, so a child
        /// matches against its parent's NEW id - but only on a unique
        /// match. Returns null when the player is (now) in the save, or why
        /// the restore must not go ahead. No identifier = no verdict.
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
            if (playerRoot == null || _uniqueIdType == null || _uidId == null) return null;

            Component[] ids;
            try { ids = playerRoot.GetComponentsInChildren(_uniqueIdType, true); }
            catch (Exception) { return null; }

            List<KeyValuePair<int, Component>> order = new List<KeyValuePair<int, Component>>();
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] == null) continue;
                int depth = 0;
                for (Transform t = ids[i].transform; t != playerRoot && t != null; t = t.parent) depth++;
                order.Add(new KeyValuePair<int, Component>(depth, ids[i]));
            }
            if (order.Count == 0) return null;
            order.Sort(new ByDepth());

            Component top = order[0].Value;
            string topBefore = ReadString(_uidId, top);
            if (string.IsNullOrEmpty(topBefore) || stored.Contains(topBefore)) return null;   // this game's own state

            HashSet<string> claimed = new HashSet<string>();
            int remapped = 0, unmatched = 0;
            for (int i = 0; i < order.Count; i++)
            {
                Component u = order[i].Value;
                string id = ReadString(_uidId, u);
                if (string.IsNullOrEmpty(id) || stored.Contains(id)) continue;

                Transform parent = u.transform.parent;
                Component parentUid = parent != null ? parent.GetComponent(_uniqueIdType) : null;
                string parentId = parentUid != null ? ReadString(_uidId, parentUid) : null;
                string classId = ReadString(_uidClassId, u);

                SavedObject match = null;
                int found = 0;
                for (int s = 0; s < saved.Count; s++)
                {
                    SavedObject o = saved[s];
                    if (o.GameObjectName != u.gameObject.name || claimed.Contains(o.Id)) continue;
                    if (!string.IsNullOrEmpty(o.ClassId) && !string.IsNullOrEmpty(classId) && o.ClassId != classId) continue;
                    if (u != top && (o.ParentId ?? "") != (parentId ?? "")) continue;
                    match = o;
                    found++;
                }

                if (found != 1) { unmatched++; continue; }
                try { _uidId.SetValue(u, match.Id, null); }
                catch (Exception) { unmatched++; continue; }
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

            note = "adopted the save's player, " + remapped + " id(s) remapped" +
                   (unmatched > 0 ? ", " + unmatched + " unmatched" : "");
            _log.LogInfo("Savestate: from another save - " + note + " ('" + top.gameObject.name + "' " +
                         topBefore + " -> " + topAfter + ").");
            return null;
        }

        private int DeleteUnsaved(HashSet<string> stored, Transform keepRoot, List<string> names)
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

                names.Add(u.gameObject.name);
                CancelBuildMissions(u.gameObject);
                UnityEngine.Object.Destroy(u.gameObject);
                n++;
            }
            return n;
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

        public bool MemorySafeSaveMode { get { return ReadStaticBool(_memorySafe); } }

        // ------------------------------------------------------------------
        // DIAGNOSTICS: would the in-place restore bring this pickup back?
        // The loader recreates a missing object only from a prefab
        // (ClassId in AllPrefabs); a scene object that was destroyed is
        // "Could not find".
        public void DescribePickups(int itemId, List<string> lines)
        {
            lines.Clear();
            if (!Resolve()) { lines.Add("not bound"); return; }
            if (_pickUpType == null || _pickUpItemId == null) { lines.Add("PickUp type not found"); return; }

            IDictionary prefabs = null;
            try { prefabs = _allPrefabs != null ? _allPrefabs.GetValue(null, null) as IDictionary : null; }
            catch (Exception) { }

            UnityEngine.Object[] all;
            try { all = Resources.FindObjectsOfTypeAll(_pickUpType); }
            catch (Exception ex) { lines.Add("search failed: " + ex.Message); return; }

            int found = 0;
            for (int i = 0; i < all.Length && found < 10; i++)
            {
                Component c = all[i] as Component;
                if (c == null || c.gameObject.hideFlags != HideFlags.None) continue;
                if (!c.gameObject.scene.IsValid()) continue;   // prefab assets, not scene objects

                int id;
                try { id = (int)_pickUpItemId.GetValue(c); }
                catch (Exception) { continue; }
                if (id != itemId) continue;

                found++;
                StringBuilder sb = new StringBuilder();
                sb.Append(Path(c.transform)).Append(c.gameObject.activeInHierarchy ? " (active)" : " (inactive)");
                sb.Append(" | self/parents: ").Append(DescribeIdentifiers(c.gameObject, prefabs));

                GameObject target = null;
                try { target = _pickUpDestroyTarget != null ? _pickUpDestroyTarget.GetValue(c) as GameObject : null; }
                catch (Exception) { }
                if (target != null && target != c.gameObject)
                    sb.Append(" | destroy target ").Append(target.name).Append(": ").Append(DescribeIdentifiers(target, prefabs));

                lines.Add(sb.ToString());
            }

            if (found == 0) lines.Add("no pickup with item id " + itemId + " is loaded (it may be streamed out, or already taken)");
            lines.Insert(0, "Item " + itemId + ": " + found + " pickup(s). Identifiers " + IdentifierCount +
                            ", prefabs known " + (prefabs != null ? prefabs.Count.ToString() : "?"));
        }

        private string DescribeIdentifiers(GameObject go, IDictionary prefabs)
        {
            if (_uniqueIdType == null) return "?";
            Component[] ids = go.GetComponentsInParent(_uniqueIdType, true);
            if (ids == null || ids.Length == 0) return "no identifier (not saved: comes back only with the scene)";

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < ids.Length && i < 3; i++)
            {
                if (i > 0) sb.Append("; ");
                Component u = ids[i];
                string classId = ReadString(_uidClassId, u);
                bool isPrefab = prefabs != null && !string.IsNullOrEmpty(classId) && prefabs.Contains(classId);
                sb.Append(u.GetType().Name).Append(" on ").Append(u.gameObject.name);
                sb.Append(isPrefab ? " [prefab: recreated in place]" : " [scene object: NOT recreated in place]");
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

        private static bool ReadStaticBool(FieldInfo f)
        {
            if (f == null) return false;
            try { return (bool)f.GetValue(null); }
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
