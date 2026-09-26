using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Savestates - PHASE 0, EXPERIMENTAL. PRACTICE ONLY.
    //
    // A probe, not the finished feature: capture the game's own level
    // serialization into a file (no save slot, no Steam Cloud), then
    // restore it two ways and log what each did, so an in-game test can
    // decide which restore to build on. See Game/SavestateBridge.cs and
    // game-notes "Saving and loading".
    //
    //   Capture here          - the game's save routine, redirected to
    //                           BepInEx/config/ForestOverlay/savestates.
    //   Restore in place      - LoadNow into the running scene, no load;
    //                           deletes what the save does not know and
    //                           puts back world pickups taken since
    //                           (Game/PickupKeeper).
    //   Restore with load     - LoadSavedLevel: one scene load.
    //   Current slot's save   - the same two restores fed from the slot the
    //                           game is running on (the author's idea: a
    //                           faster quick-load, and a load-free one).
    //   Check pickups         - would the in-place restore bring an item
    //                           (default 210, the keycard) back?
    //
    // Every action logs one "Savestate ..." line; that line is the test.
    //
    // SEGMENT START STATES (phase 1): a segment may keep a savestate at
    // savestates/segments/<safe segment id>.fosave - named from the id, so
    // a shared segment file and its start state travel together. The
    // Practice module restores it on every restart (return to spot), in
    // place or with a load per Segment.StartRestoreWithLoad, then teleports
    // to the spawn as before. No file: the restart keeps the game state,
    // which some routes need (runner request).
    // ------------------------------------------------------------------
    public sealed class SavestateModule : OverlayModule
    {
        private const float LoadTimingTimeout = 180f;

        public override string Id { get { return "savestates"; } }
        public override string DisplayName { get { return "Savestates"; } }
        public override bool HasTab { get { return true; } }
        public override string TabTitle { get { return "Savestates"; } }
        public override int TabOrder { get { return 20; } }
        public override bool IsPracticeOnly { get { return true; } }

        private SavestateBridge _bridge;
        private PickupKeeper _keeper;
        private BookPages _book;
        private PanelKeeper _panels;
        private BossHold _bossHold;
        private SetupHold _setupHold;
        private MeganKeeper _megan;
        private ElevatorKeeper _elevators;
        private KeypadDoorKeeper _doors;
        private AreaKeeper _area;
        private NatureKeeper _nature;
        private GreebleKeeper _greebles;
        private EnemyKeeper _enemies;
        private string _dir;

        private bool _busy;
        // EndgameFirst ran for the next RestoreInPlace: never twice.
        private bool _endgameTried;
        private ConfigEntry<bool> _allowCrossMode;
        private ConfigEntry<bool> _respawnEnemies;
        // AfterInPlace's wreck clears so far; LogNewPickups lists after one.
        private int _planeClears;
        private float _contentHeight = 520f;
        private string _lastCaptureHash = "";
        private float _busySince;
        private string _name = "savestate";
        private string _itemIdText = "210";

        // File list, rebuilt only on refresh.
        private readonly List<string> _files = new List<string>();
        private readonly List<GUIContent> _fileLabels = new List<GUIContent>();
        private int _selected = -1;
        private float _deleteArmedUntil;

        private readonly List<string> _diag = new List<string>();
        private readonly List<GUIContent> _diagLabels = new List<GUIContent>();

        private GUIContent _status = new GUIContent("");

        // Which buttons the status line answers, so it is drawn under them
        // (UI rule: a message goes where the click was). Top = started from
        // elsewhere (a Practice restart), or a timeout.
        private enum Anchor { Top, Capture, List, Slot, Pickups, Memory }
        private Anchor _anchor = Anchor.Top;
        private GUIContent _slotLabel = new GUIContent("");
        private GUIContent _bindLabel = new GUIContent("");
        private GUIContent _dirLabel = new GUIContent("");
        private float _nextSlotRefresh;

        // Timing a scene-load restore across the load.
        private bool _timingLoad;
        private Action<string> _loadAfter;
        private bool _sawLoading;
        private float _loadStarted;
        private string _loadWhat;

        // The load leak (CLAUDE.md Next up 1): every load, whoever started
        // it, is seen here, and a memory census runs shortly after.
        private readonly LoadWatcher _loads = new LoadWatcher();
        private MemoryCensus _census;
        private ConfigEntry<bool> _censusOnLoad;
        private LeakedThreads _threads;
        private ConfigEntry<bool> _threadsFix;
        private StaleSubscribers _subscribers;
        private ConfigEntry<bool> _subscribersFix;
        private float _censusDue;
        private string _censusLabel = "";
        private readonly GUIContent _censusText = new GUIContent("");
        private const float CensusDelay = 1.5f;

        private Vector2 _scroll;

        private static readonly GUIContent Warning = new GUIContent(
            "EXPERIMENTAL (phase 0). Stores the game's own save data in a file - no save slot is used. " +
            "Capture and restores mark the session as practice. Every action logs one 'Savestate' line.");

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);
            _bridge = new SavestateBridge(ctx.Log);
            _keeper = new PickupKeeper(ctx.Log);
            _book = new BookPages(ctx.Log);
            _panels = new PanelKeeper(ctx.Log);
            _enemies = new EnemyKeeper(ctx.Log);
            _keeper.Install(OverlayPlugin.PluginGuid);
            PickupKeeper.NameOf = NameOfItem;
            _panels.Install(OverlayPlugin.PluginGuid);
            _bossHold = new BossHold(ctx.Log, ctx.Runner);
            _bossHold.Install(OverlayPlugin.PluginGuid);
            _setupHold = new SetupHold(ctx.Log);
            _setupHold.Install(OverlayPlugin.PluginGuid);
            _megan = new MeganKeeper(ctx.Log);
            _elevators = new ElevatorKeeper(ctx.Log);
            _doors = new KeypadDoorKeeper(ctx.Log);
            _area = new AreaKeeper(ctx.Log);
            _nature = new NatureKeeper(ctx.Log);
            _nature.Install(OverlayPlugin.PluginGuid);
            _greebles = new GreebleKeeper(ctx.Log);
            _greebles.Install(OverlayPlugin.PluginGuid);
            CutsceneAudio.Install(ctx.Log, OverlayPlugin.PluginGuid);
            FullCapacityWatch.Install(ctx.Log, OverlayPlugin.PluginGuid);
            _dir = Path.Combine(ctx.ConfigDirectory, "savestates");
            _dirLabel = new GUIContent("Savestates (" + _dir + ")");
            RefreshFiles();

            _census = new MemoryCensus(ctx.Log);

            // Two worker threads the game leaves running every load
            // (game-notes "The load leak"). Memory only - no gameplay
            // effect, so not practice-only.
            _threadsFix = ctx.Config.Bind("Fixes", "StopLeakedThreadsOnLoad", true,
                "Stop the two worker threads the game leaves running on every load (the old WorkScheduler's, " +
                "and an extra FocusLostAudio copy's) - each keeps memory from the previous load.");
            LeakedThreads.Enabled = _threadsFix.Value;
            _threads = new LeakedThreads(ctx.Log);
            _threads.Install(OverlayPlugin.PluginGuid);

            // The leak's root: the game's event registries keep every
            // destroyed world's subscribers until the title screen clears them.
            _subscribersFix = ctx.Config.Bind("Fixes", "PruneDeadSubscribersOnLoad", true,
                "After every load, remove the game's event subscriptions left by the previous world's destroyed objects " +
                "(the game only clears them at the title screen) - they keep the old world in memory, ~120 MB a load.");
            StaleSubscribers.Enabled = _subscribersFix.Value;
            _subscribers = new StaleSubscribers(ctx.Log);
            // Off since v0.23.6 (the leak is fixed; the census is the ~0.7 s
            // hitch after a load). A new key, so existing configs turn it off.
            _censusOnLoad = ctx.Config.Bind("Diagnostics", "MemoryCensusAfterEveryLoad", false,
                "After every load, log the Mono heap and which static references hold destroyed objects " +
                "(the load memory leak investigation). Costs a hitch of up to a second or so, just after a load.");

            _allowCrossMode = ctx.Config.Bind("Savestates", "AllowCrossModeRestore", false,
                "Restore a savestate captured in a Creative game into a survival game, or the other way round. " +
                "For testing: the game mode is not in the save, so the world comes back in this game's mode.");

            _respawnEnemies = ctx.Config.Bind("Savestates", "RespawnEnemiesInPlace", true,
                "After an in-place restore, enemies as after a load: the game's own family setup respawns enemies " +
                "killed since the capture (where the game places them, not exactly where they stood), and dead " +
                "bodies left since the capture are cleared. Nothing is spawned when the game has enemies off.");
        }

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("tab.savestates", KeyCode.None, "Open Savestates tab", OpenMyTab);
            map.Add("savestate.capture", KeyCode.None, "Savestate: capture here", Capture);
            map.Add("savestate.restoreInPlace", KeyCode.None, "Savestate: quick load selected", RestoreSelectedInPlace);
            map.Add("savestate.restoreLoad", KeyCode.None, "Savestate: full load selected", RestoreSelectedWithLoad);
        }

        public override void Shutdown()
        {
            PickupKeeper.Armed = false;
            if (_keeper != null) _keeper.Uninstall();
            if (_panels != null) _panels.Uninstall();
            if (_nature != null) _nature.Uninstall();
            if (_greebles != null) _greebles.Uninstall();
            if (_bossHold != null) _bossHold.Uninstall();
            if (_setupHold != null) _setupHold.Uninstall();
            CutsceneAudio.Uninstall();
            FullCapacityWatch.Uninstall();
            if (_threads != null) _threads.Uninstall();
        }

        // ------------------------------------------------------------------
        public override void Tick()
        {
            // A waiting greeble record never acts in another game.
            if (PlayerRef.AtTitleScreen && _greebles != null) _greebles.Clear();

            if (Time.unscaledTime >= _nextSlotRefresh)
            {
                _nextSlotRefresh = Time.unscaledTime + 1f;
                _slotLabel.text = "Current save slot: " + _bridge.CurrentSlot;
                _bindLabel.text = "Bound: " + _bridge.Status + " | pickups: " + _keeper.Status +
                                  (PickupKeeper.Armed ? ", " + _keeper.KeptCount + " kept for a restore" : ", armed by the first capture/restore") +
                                  " | cave panels: " + _panels.Status +
                                  (PickupKeeper.Armed && _panels.KeptCount > 0 ? ", " + _panels.KeptCount + " broken kept" : "");
            }

            if (_timingLoad) TimeLoad();
            WatchLoads();

            // A reset that closed the book: free a pitch lock it left
            // (Game/BookClose), once no restore is running.
            if (!_busy)
            {
                string book = BookClose.Tick();
                if (book.Length > 0) Ctx.Log.LogInfo("Book after a reset: " + book + ".");
            }

            // A coroutine that dies on an exception never calls back; do not
            // leave every button disabled for the rest of the session.
            if (_busy && Time.realtimeSinceStartup - _busySince > LoadTimingTimeout)
            {
                _busy = false;
                SetStatus("the last action never finished - see LogOutput.log");
                Ctx.Log.LogWarning("Savestate: an action never finished; buttons re-enabled.");
            }
        }

        // A scene-load restore is timed from the click to the game's own
        // FinishGameLoad flag (cleared by LoadSave.Awake, set when the
        // activation sequence ends). The heap figure feeds the load-leak
        // investigation.
        private void TimeLoad()
        {
            float elapsed = Time.realtimeSinceStartup - _loadStarted;
            bool finished = _bridge.GameLoadFinished;

            if (!finished) _sawLoading = true;
            else if (_sawLoading)
            {
                _timingLoad = false;
                // The heap figure now comes from WatchLoads, for every load.
                string line = _loadWhat + ": in game after " + elapsed.ToString("0.0") + " s.";
                Ctx.Log.LogInfo("Savestate " + line);
                SetStatus(line);
                Continue(null);
                return;
            }

            if (elapsed > LoadTimingTimeout)
            {
                _timingLoad = false;
                string why = "could not time the load (FinishGameLoad " +
                             (_sawLoading ? "never came back" : "never changed") + ")";
                Ctx.Log.LogWarning("Savestate " + _loadWhat + ": " + why + ".");
                Continue(why);
            }
        }

        private void WatchLoads()
        {
            if (_loads.Tick())
            {
                // Before the census, so its heap line shows the result.
                _subscribers.Prune();
                _keeper.PruneDestroyed();
                _panels.PruneDestroyed();
                DeathHooks.ForgetDeath();
                _censusLabel = "load " + _loads.Loads + (_loads.LastFromOtherScene ? " (from the title screen)" : " (game scene reloaded)") +
                               ", " + LeakedThreads.Summary() + ", stale subscribers removed " + StaleSubscribers.Removed;
                if (_censusOnLoad.Value)
                {
                    // A moment later: the activation sequence's last frames
                    // and the plugin's own rebinding are done by then.
                    _censusDue = Time.unscaledTime + CensusDelay;
                }
                else
                {
                    // No forced collection: on a 280-560 MB heap that was an
                    // 80-140 ms freeze (bridge) just as the player got control
                    // (v0.24.97). The census (switch) still collects.
                    Ctx.Log.LogInfo("Load " + _loads.Loads + " finished" + (_loads.LastFromOtherScene ? " (from the title screen)" : "") +
                                    ": Mono heap " + (GC.GetTotalMemory(false) / (1024 * 1024)) + " MB (garbage included).");
                }
            }

            if (_censusDue > 0f && Time.unscaledTime >= _censusDue)
            {
                _censusDue = 0f;
                RunCensus(_censusLabel);
            }
        }

        private void RunCensus(string label)
        {
            try { _censusText.text = _census.Run(label); }
            catch (Exception ex)
            {
                _censusText.text = "memory census failed: " + ex.Message;
                Ctx.Log.LogWarning("Memory census failed: " + ex);
            }
        }

        private void Continue(string error)
        {
            Action<string> after = _loadAfter;
            _loadAfter = null;
            if (after == null) return;
            try { after(error); }
            catch (Exception ex) { Ctx.Log.LogWarning("Savestate: continuation failed: " + ex.Message); }
        }

        // ------------------------------------------------------------------
        // Actions

        private void Capture()
        {
            _anchor = Anchor.Capture;
            CaptureTo(_name, null, null);
        }

        /// `path` null: a new file in the savestates folder, never
        /// overwriting. `after` gets null on success or the reason.
        private void CaptureTo(string name, string path, Action<string> after)
        {
            if (_busy) { if (after != null) after("a savestate action is still running"); return; }
            if (PlayerRef.AtTitleScreen)
            {
                SetStatus("capture unavailable: no player (load a game first)");
                if (after != null) after("capture unavailable: no player (load a game first)");
                return;
            }
            if (!_bridge.Resolve())
            {
                SetStatus("capture unavailable: " + _bridge.Status);
                if (after != null) after("capture unavailable: " + _bridge.Status);
                return;
            }

            _busy = true;
            _busySince = Time.realtimeSinceStartup;
            Ctx.Practice.Mark("savestate capture");
            PickupKeeper.Armed = true;
            SetStatus("capturing...");

            Vector3 pos = Ctx.Player.Found ? Ctx.Player.Transform.position : Vector3.zero;
            bool inCave = Ctx.Bridge.IsInCaves();

            // Before the capture unloads streaming, while every pickup here is
            // still loaded.
            List<string> pickups = new List<string>();
            try { _keeper.Snapshot(pickups); }
            catch (Exception ex) { Ctx.Log.LogWarning("Savestate: pickup snapshot failed: " + ex.Message); }

            string bookNote;
            string book = _book.Capture(out bookNote);
            List<int> held = _bridge.HeldIds();
            List<string> heldBefore = _bridge.PreviousHeld();

            // Captured during an endgame cutscene: note which and how far in,
            // so a restore can fast-forward the replay to this moment.
            string cutscene = Ctx.Events != null ? Ctx.Events.CutsceneRunning : null;
            float cutsceneAt = cutscene != null ? Time.time - Ctx.Events.CutsceneStartedAt : -1f;
            string megan = _megan.Capture();
            float rideAge = Ctx.Events != null && Ctx.Events.RedElevatorAt >= 0f ? Time.time - Ctx.Events.RedElevatorAt : -1f;
            string elevators = _elevators.Capture(rideAge);
            string keypadDoor = cutscene == GameEvents.KeycardDoor ? _doors.Capture(GameEvents.LastDoorPos) : "";
            string activeArea = _area.Capture();
            string blueprint = BuildMode.Capture();
            string bushes = _nature.CaptureMark();
            List<string> cutBushes = _nature.CaptureCuts();
            List<string> greebles = null;
            try { greebles = _greebles.Capture(); }
            catch (Exception ex) { Ctx.Log.LogWarning("Savestate: greeble capture failed: " + ex.Message); }

            // Before the capture force-unloads streaming: what is loaded as
            // the player sees it.
            string areas = AreaReport.Describe();
            string enemyNote;
            List<string> enemies = new List<string>();
            List<string> families = new List<string>();
            _enemies.Capture(enemies, families, out enemyNote);
            List<string> panels = new List<string>();
            try { _panels.Snapshot(panels); }
            catch (Exception ex) { Ctx.Log.LogWarning("Savestate: panel snapshot failed: " + ex.Message); }
            try
            {
                string near = Ctx.Player.Found ? _panels.DescribeNearest(Ctx.Player.Transform.position) : null;
                if (near != null) Ctx.Log.LogInfo("Savestate: nearest cave panel at capture: " + near);
            }
            catch (Exception ex) { Ctx.Log.LogWarning("Savestate: panel description failed: " + ex.Message); }

            Ctx.Runner.StartCoroutine(_bridge.Capture(delegate(SavestateBridge.Result r)
            {
                string error = OnCaptured(r, name, path, pos, inCave, pickups, book, bookNote, held, heldBefore, panels, cutscene, cutsceneAt, megan, elevators, activeArea, keypadDoor, blueprint, areas, enemies, families, enemyNote, bushes, cutBushes, greebles);
                if (after != null) after(error);
            }));
        }

        private string OnCaptured(SavestateBridge.Result r, string name, string path, Vector3 pos, bool inCave, List<string> pickups,
                                  string book, string bookNote, List<int> held, List<string> heldBefore, List<string> panels,
                                  string cutscene, float cutsceneAt, string megan, string elevators, string activeArea, string keypadDoor, string blueprint, string areas, List<string> enemies,
                                  List<string> families, string enemyNote, string bushes, List<string> cutBushes,
                                  List<string> greebles)
        {
            _busy = false;
            if (!r.Ok)
            {
                Ctx.Log.LogWarning("Savestate capture failed: " + r.Message);
                SetStatus("capture failed: " + r.Message);
                return r.Message;
            }

            try
            {
                SavestateFile f = new SavestateFile();
                f.Name = name;
                f.Level = r.Level;
                f.Difficulty = r.Difficulty;
                f.Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                f.PluginVersion = OverlayPlugin.PluginVersion;
                f.X = pos.x; f.Y = pos.y; f.Z = pos.z;
                f.InCave = inCave;
                f.StreamingUnloaded = r.StreamingUnloaded;
                f.Pickups = pickups;
                f.Book = book;
                f.Held = held;
                f.HeldBefore = heldBefore;
                f.Panels = panels;
                if (cutscene != null) { f.Cutscene = cutscene; f.CutsceneAt = cutsceneAt; }
                f.Megan = megan;
                f.Elevators = elevators;
                f.ActiveArea = activeArea;
                f.KeypadDoor = keypadDoor;
                f.Blueprint = blueprint;
                f.Bushes = bushes;
                f.CutBushes = cutBushes;
                f.Greebles = greebles;
                f.Areas = areas;
                f.Enemies = enemies;
                f.Families = families;
                f.Data = r.Data;

                if (path == null)
                {
                    Directory.CreateDirectory(_dir);
                    path = UniquePath(SavestateFile.SafeFileName(name));
                }
                else Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, f.Write(), new UTF8Encoding(false));
                _lastCaptureHash = Segment.HashText(f.Data);

                string line = "captured '" + name + "' -> " + Path.GetFileName(path) + ": " + r.Message +
                              ", " + pickups.Count + " world pickups listed, " + bookNote +
                              ", held " + HeldNames(held) +
                              (heldBefore != null && heldBefore.Count > 0 ? " (before that: " + HeldBeforeNames(heldBefore) + ")" : "") +
                              (panels.Count > 0 ? ", " + panels.Count + " cave panels" : "") +
                              (cutscene != null ? ", during cutscene '" + cutscene + "' at " + cutsceneAt.ToString("0.0") + " s" : "") +
                              (megan.Length > 0 ? ", Megan " + megan : "") +
                              (blueprint.Length > 0 ? ", blueprint " + blueprint + " out" : "") +
                              (enemyNote.Length > 0 ? ", " + enemyNote : "");
                Ctx.Log.LogInfo("Savestate " + line);
                Ctx.Log.LogInfo("Savestate areas at capture: " + areas);
                SetStatus(line);

                RefreshFiles();
                int listed = _files.IndexOf(path);
                if (listed >= 0) _selected = listed;
                return null;
            }
            catch (Exception ex)
            {
                Ctx.Log.LogWarning("Savestate capture: could not write the file: " + ex.Message);
                SetStatus("capture failed writing the file: " + ex.Message);
                return "could not write the file: " + ex.Message;
            }
        }

        private void RestoreSelectedInPlace()
        {
            _anchor = Anchor.List;
            RestoreFile(LoadSelected(), false, null);
        }

        private void RestoreSelectedWithLoad()
        {
            _anchor = Anchor.List;
            RestoreFile(LoadSelected(), true, null);
        }

        /// `done` gets null on success or the reason (not called for an
        /// unreadable file - LoadSelected has said why).
        private void RestoreFile(SavestateFile f, bool load, Action<string> done)
        {
            if (f == null) { if (done != null) done("could not read the file - " + _status.text); return; }
            if (_busy) { if (done != null) done("a savestate action is still running"); return; }
            if (RefusedAtTitle("restore", done)) return;

            string mode = ModeMismatch(f);
            if (mode != null)
            {
                SetStatus("restore '" + f.Name + "' refused: " + mode);
                if (done != null) done("refused: " + mode);
                return;
            }

            if (!load)
            {
                HashSet<string> present = f.Pickups != null ? new HashSet<string>(f.Pickups) : null;
                RestoreInPlace(f.Data, f.StreamingUnloaded, present, "'" + f.Name + "'", f.InCave ? 1 : 0, f, done);
                return;
            }

            Ctx.Practice.Mark("savestate restore (load)");
            PickupKeeper.Armed = true;
            _greebles.Restore(f.Greebles, false);
            string err = _bridge.RestoreWithLoad(f.Data, f.Difficulty);
            StartLoad("restore '" + f.Name + "' with load", err, AfterLoad(f, done));
        }

        // ------------------------------------------------------------------
        // The test bridge (Modules/BridgeModule).

        /// "name  (size, date)" per savestate file (not the segment ones).
        public void ListFiles(List<string> into)
        {
            RefreshFiles();
            for (int i = 0; i < _fileLabels.Count; i++) into.Add(_fileLabels[i].text);
        }

        public void CaptureNamed(string name, Action<string> done)
        {
            _anchor = Anchor.Capture;
            CaptureTo(name, null, done);
        }

        /// By file name without the extension, case-insensitive; selects
        /// it in the list as a click would.
        public void RestoreNamed(string name, bool load, Action<string> done)
        {
            RefreshFiles();
            int found = -1;
            for (int i = 0; i < _files.Count; i++)
                if (string.Equals(Path.GetFileNameWithoutExtension(_files[i]), name, StringComparison.OrdinalIgnoreCase)) found = i;
            if (found < 0) { done("no savestate '" + name + "' (savestates lists them)"); return; }

            _selected = found;
            _anchor = Anchor.List;
            RestoreFile(LoadSelected(), load, done);
        }

        private void SlotInPlace()
        {
            _anchor = Anchor.Slot;
            string error;
            string data = _bridge.ReadSlotData(out error);
            if (data == null)
            {
                SetStatus("slot reload failed: " + error);
                Ctx.Log.LogWarning("Savestate slot reload in place: " + error);
                return;
            }
            // A slot save was made the game's way: streaming unloaded only in
            // MemorySafeSaveMode. No pickup list - a menu load would bring
            // them all back, so every kept pickup is put back.
            RestoreInPlace(data, _bridge.MemorySafeSaveMode, null, "slot " + _bridge.CurrentSlot, -1, null, null);
        }

        private void SlotWithoutMenu()
        {
            if (_busy) return;
            _anchor = Anchor.Slot;
            Ctx.Practice.Mark("savestate slot load");
            PickupKeeper.Armed = true;
            string err = _bridge.LoadSlotWithoutMenu();
            StartLoad("load slot " + _bridge.CurrentSlot + " without the menu", err, null);
        }

        /// `savedInCave`: the file's cave flag (1 / 0), or -1 when unknown
        /// (a slot save) and the player's position has to decide.
        /// `file`: the savestate, for what lives outside the game's data -
        /// the book page, the held items, the cave panels; null for a slot
        /// save, which leaves them as they are.
        /// A restore at the title screen deserialized the save into the
        /// menu scene (bridge, v0.24.72: `identifiers 0 -> 105`, no
        /// player); capture already refused there (v0.24.59).
        private bool RefusedAtTitle(string what, Action<string> done)
        {
            if (!PlayerRef.AtTitleScreen) return false;
            string why = what + " unavailable: no player (load a game first)";
            SetStatus(why);
            Ctx.Log.LogInfo("Savestate: " + why + ".");
            if (done != null) done(why);
            return true;
        }

        private void RestoreInPlace(string data, bool unloadStreaming, HashSet<string> presentPickups, string what,
                                    int savedInCave, SavestateFile file, Action<string> after)
        {
            if (_busy) { if (after != null) after("a savestate action is still running"); return; }
            if (RefusedAtTitle("restore", after)) return;
            // A spot past the vault door on a save that has not opened it:
            // the endgame first, as a Full load does (EndgameLoader).
            bool tried = _endgameTried;
            _endgameTried = false;
            if (!tried && file != null && EndgameLoader.Needed(file.Areas))
            {
                _busy = true;
                _busySince = Time.realtimeSinceStartup;
                Ctx.Runner.StartCoroutine(EndgameFirst(data, unloadStreaming, presentPickups, what, savedInCave, file, after));
                return;
            }
            _busy = true;
            _busySince = Time.realtimeSinceStartup;
            int cutsceneStarts = Ctx.Events != null ? Ctx.Events.CutsceneStarts : 0;
            bool transformRunning = Ctx.Events != null && Ctx.Events.CutsceneRunning == MeganKeeper.TransformEvent;
            bool meganSeated = file != null && _megan.LiveSeated();
            Ctx.Practice.Mark("savestate restore (in place)");
            PickupKeeper.Armed = true;
            BossHold.Arm();
            SetStatus("restoring " + what + " in place...");
            Ctx.Log.LogInfo("Savestate restore " + what + " in place: starting.");
            int cannibalsBefore, familiesBefore;
            _bridge.CountEnemies(out cannibalsBefore, out familiesBefore);

            // Before as well as after: a physics step during the restore
            // could land the old fall at the restored spot.
            string fall = Ctx.Bridge.EndFall();
            // The book first (runner sxczurass), then a swing / action in
            // progress is cut (runner maks).
            string book = BookClose.IfOpen();
            if (book.Length > 0) fall += (fall.Length > 0 ? ", " : "") + book;
            string anim = AnimReset.Cancel();
            if (anim.Length > 0) fall += (fall.Length > 0 ? ", " : "") + anim;
            // A blueprint in the hands is outside the save (runner
            // sxczurass: it stayed out); the captured one comes back last.
            string blueprint = BuildMode.PutAway();
            if (blueprint.Length > 0) fall += (fall.Length > 0 ? ", " : "") + blueprint;

            Transform keep = Ctx.Player.Found ? Ctx.Player.Transform.root : null;
            Ctx.Runner.StartCoroutine(_bridge.RestoreInPlace(data, unloadStreaming, keep, delegate(SavestateBridge.Result r)
            {
                _busy = false;

                // The restore can bring the saved body's speed back, and a
                // fall in progress keeps its air time (runner: restoring in
                // mid-air dealt landing damage).
                string after2 = Ctx.Bridge.EndFall();
                if (fall.Length == 0) fall = after2;

                int pickups = 0;
                if (r.Ok)
                {
                    try { pickups = _keeper.Restore(presentPickups); }
                    catch (Exception ex) { Ctx.Log.LogWarning("Savestate: pickup restore failed: " + ex.Message); }
                }

                // The serializer restores the IsInCaves flag but not what
                // the cave doors did (terrain collision, lighting,
                // streaming), so a flag test sees nothing to change. The
                // file knows where it was captured: send that state outright.
                string cave = "";
                bool surfaceSent = false;
                if (r.Ok && Ctx.Player.Found)
                {
                    try
                    {
                        cave = savedInCave >= 0 ? Ctx.Bridge.ForceCaveState(savedInCave == 1)
                                                : Ctx.Bridge.SyncCaveState(Ctx.Player.Transform.position);
                        // NotInACave (sent for a surface state) restarts the
                        // enemy families itself - see RespawnEnemies.
                        surfaceSent = savedInCave == 0 && cave == "surface state set";
                    }
                    catch (Exception) { }
                }

                string overlookNote = r.Ok ? AreaReport.LeaveOverlook() : "";
                string bookNote = r.Ok && file != null ? _book.Apply(file.Book) : "";
                // Blood on the player's body and weapon is not in the save.
                string washNote = r.Ok ? _bridge.Wash() : "";

                // A slot reload in place too: every in-place restore.
                // A cave capture's cannibals are kept and put back
                // (AfterInPlace): the game's setup would despawn every cave
                // cannibal and respawn them seconds later (author: the
                // armsy popped back in).
                bool caveFamilies = r.Ok && file != null && file.InCave && file.Families != null && file.Families.Count > 0;
                string enemyNote = !(r.Ok && _respawnEnemies.Value) ? ""
                                 : caveFamilies ? "enemies: cave capture - the live ones kept"
                                 : _bridge.RespawnEnemies(surfaceSent);
                if (r.Ok && _respawnEnemies.Value) enemyNote += " | " + _bridge.ClearCorpses();

                string panelNote = "";
                if (r.Ok && file != null)
                {
                    try { panelNote = _panels.Restore(file.Panels, true); }
                    catch (Exception ex) { panelNote = "panels: restore failed (" + ex.Message + ")"; }
                    try
                    {
                        string near = Ctx.Player.Found ? _panels.DescribeNearest(Ctx.Player.Transform.position) : null;
                        if (near != null) Ctx.Log.LogInfo("Savestate: nearest cave panel after the restore: " + near);
                    }
                    catch (Exception) { }
                }

                // Megan and her trigger are outside the save (MeganKeeper);
                // before the fast-forward, which waits for her cutscene.
                // Files from before v0.24.35 have no megan line: a capture
                // during her transformation had her seated.
                string meganNote = "";
                if (r.Ok && file != null)
                {
                    string megan = file.Megan.Length > 0 ? file.Megan
                                 : file.Cutscene == MeganKeeper.TransformEvent ? MeganKeeper.Seated : "";
                    meganNote = _megan.Restore(megan, transformRunning, meganSeated);
                }

                // The elevators are outside the save too (ElevatorKeeper).
                // A ride under way at capture is replayed once the player
                // is placed (after `after`: a restart's teleport cuts
                // player actions).
                List<Component> rides = new List<Component>();
                string elevatorNote = r.Ok && file != null
                    ? _elevators.Restore(file.Elevators, RideCutsceneAt(file), false, rides) : "";
                // So is the endgame's active area, which switches the
                // sections' renderers (AreaKeeper).
                string areaNote = r.Ok && file != null ? _area.Restore(file.ActiveArea) : "";
                // Trees chopped and bushes cut since are outside what an
                // in-place LoadNow puts back (NatureKeeper); a slot's too.
                string natureNote = r.Ok ? _nature.Restore(file != null ? file.Bushes : "", file != null ? file.CutBushes : null) : "";
                // The sticks / rocks around trees come from pool objects
                // that carry their own seed (GreebleKeeper).
                string greebleNote = "";
                if (r.Ok && file != null)
                {
                    try { greebleNote = _greebles.Restore(file.Greebles, true); }
                    catch (Exception ex) { greebleNote = "greebles: failed (" + ex.Message + ")"; }
                }

                // Saved relative to a parent (a keycard cutscene holds the
                // player under the card reader): the restore put the
                // player at those numbers in the world.
                string placeNote = r.Ok && file != null ? PutPlayerBack(file, false) : "";

                // The hands were emptied for the restore; put back what they
                // held at capture (runner maks: the lighter came back away,
                // and unlit). Its own log line, a moment later.
                if (r.Ok && file != null && rides.Count > 0)
                    Ctx.Runner.StartCoroutine(ReplayRides(file, rides, what));
                else if (r.Ok && DoorCutscene(file))
                    Ctx.Runner.StartCoroutine(ReplayDoor(file, what));
                else if (r.Ok && file != null && file.CutsceneAt >= 0f)
                    Ctx.Runner.StartCoroutine(FastForwardCutscene(file, cutsceneStarts, what));

                // The captured blueprint after the hands (runner maks): the
                // game's CreateBuilding picks the utility to hold beside it.
                string pullOut = r.Ok && file != null && file.CutsceneAt < 0f ? file.Blueprint : "";
                if (r.Ok && file != null && file.Held != null && file.Held.Count > 0)
                    Ctx.Runner.StartCoroutine(_bridge.ReEquip(file.Held, NameOfItem,
                        delegate(string note)
                        {
                            Ctx.Log.LogInfo("Savestate restore " + what + ": " + note + ".");
                            PullOutBlueprint(pullOut, "Savestate restore " + what);
                        }));
                else PullOutBlueprint(pullOut, "Savestate restore " + what);

                string line = "restore " + what + " in place: " + r.Message +
                              ", pickups put back " + pickups +
                              (presentPickups == null ? " (all kept)" : "") +
                              (string.IsNullOrEmpty(cave) ? "" : " | cave: " + cave) +
                              (fall.Length == 0 ? "" : " | " + fall) +
                              (placeNote.Length == 0 ? "" : " | " + placeNote) +
                              (overlookNote.Length == 0 ? "" : " | " + overlookNote) +
                              (bookNote.Length == 0 ? "" : " | " + bookNote) +
                              (washNote.Length == 0 ? "" : " | " + washNote) +
                              (panelNote.Length == 0 ? "" : " | " + panelNote) +
                              (meganNote.Length == 0 ? "" : " | " + meganNote) +
                              (elevatorNote.Length == 0 ? "" : " | " + elevatorNote) +
                              (areaNote.Length == 0 ? "" : " | " + areaNote) +
                              (natureNote.Length == 0 ? "" : " | " + natureNote) +
                              (greebleNote.Length == 0 ? "" : " | " + greebleNote) +
                              (enemyNote.Length == 0 ? "" : " | " + enemyNote);
                if (r.Ok) Ctx.Log.LogInfo("Savestate " + line);
                else Ctx.Log.LogWarning("Savestate " + line);
                if (r.Ok) Ctx.Runner.StartCoroutine(LogAreas(file));
                if (r.Ok) Ctx.Runner.StartCoroutine(SyncSun("restore " + what));
                if (r.Ok) Ctx.Runner.StartCoroutine(AfterInPlace(what, _respawnEnemies.Value, familiesBefore, file));
                if (r.Ok && presentPickups != null) Ctx.Runner.StartCoroutine(LogNewPickups(presentPickups, what));
                SetStatus(line);

                if (after != null)
                {
                    try { after(r.Ok ? null : r.Message); }
                    catch (Exception ex) { Ctx.Log.LogWarning("Savestate: continuation failed: " + ex.Message); }
                }
            }));
        }

        // A savestate taken during an endgame cutscene restores to its start:
        // the cutscene is a coroutine and animator states the serializer
        // does not keep, and the game starts it over (runner maks, Megan's
        // transformation - he wants the last seconds kept as the reference
        // for the boss kill, "always same variables"). So once the same
        // cutscene begins again, replay it faster to the captured moment:
        // the game's own script, only sooner, identical every time.
        // timeScale is safe here: InventoryItemView.Update writes it only
        // when an item is equipped from the open inventory (game-notes
        // *timeScale*). Near the mark it slows so it lands on it.
        // 6x took ~10 s for Megan's 60 s (runner maks: "within ~1 second").
        private const float CutsceneSpeed = 25f;
        private const float CutsceneWait = 20f;

        /// The cutscene time to replay a red elevator ride to, or -1.
        private static float RideCutsceneAt(SavestateFile f)
        {
            return f != null && f.CutsceneAt >= 0f && f.Cutscene == GameEvents.RedElevator ? f.CutsceneAt : -1f;
        }

        // Found live (maks's "elev boost", v0.24.79): the keycard cutscene
        // parents the player to the card reader's `playerPos`, so the save
        // holds the player's LOCAL position - a Quick load dropped the
        // player near the world's origin, a Full load put them on the
        // surface. The header's position is the world one. More than a few
        // metres off = moved there; after a Full load the cave state from
        // the file too (the load decided it from the wrong spot).
        private const float MisplacedDistance = 3f;

        private string PutPlayerBack(SavestateFile f, bool caveToo)
        {
            if (!Ctx.Player.Found) return "";
            Vector3 at = new Vector3(f.X, f.Y, f.Z);
            if (at == Vector3.zero) return "";
            Vector3 was = Ctx.Player.Transform.position;
            if ((was - at).sqrMagnitude < MisplacedDistance * MisplacedDistance) return "";
            string cave = "";
            if (caveToo)
            {
                try { cave = Ctx.Bridge.ForceCaveState(f.InCave); }
                catch (Exception) { }
            }
            Ctx.Player.MoveTo(at, Ctx.Player.Transform.rotation);
            Ctx.Bridge.EndFall();
            return "player put back at the captured spot (restored " + (was - at).magnitude.ToString("F0") +
                   " m away - saved relative to a parent)" + (cave.Length > 0 ? ", " + cave : "");
        }

        // A cutscene the player starts (a ride, a keypad door) is replayed a
        // frame after the restore's continuation (the restart's teleport and
        // action cut), with the player where it starts - the elevator's
        // stage starts nothing from afar, and the game's trigger needs a
        // physics step to see them - then the usual fast-forward.
        private delegate string Starter();

        /// Loads the endgame the capture had, holds the player where they
        /// stand until it is in, then runs the Quick load.
        private IEnumerator EndgameFirst(string data, bool unloadStreaming, HashSet<string> presentPickups, string what,
                                         int savedInCave, SavestateFile file, Action<string> after)
        {
            float start = Time.realtimeSinceStartup;
            SetStatus("restoring " + what + ": loading the endgame area first...");
            string note = EndgameLoader.EnsureLoaded(file.Areas);
            Vector3 at = Ctx.Player.Found ? Ctx.Player.Transform.position : Vector3.zero;
            // The trigger starts its load 0.5 s after ForceLoad (HoldUntilLoaded).
            while (Time.realtimeSinceStartup - start < 30f &&
                   (Time.realtimeSinceStartup - start < 1f || !EndgameLoader.Settled()))
            {
                if (Ctx.Player.Found && at != Vector3.zero) Ctx.Player.MoveTo(at, Ctx.Player.Transform.rotation);
                yield return null;
            }
            // The new scene's objects wake over the next frames.
            yield return null;
            yield return null;
            bool loaded = EndgameLoader.Settled();
            Ctx.Log.LogInfo("Savestate restore " + what + " in place: " + note + " - " +
                            (loaded ? "loaded" : "still not loaded, restoring anyway") + " after " +
                            (Time.realtimeSinceStartup - start).ToString("F1") + " s.");
            _busy = false;
            _endgameTried = true;
            RestoreInPlace(data, unloadStreaming, presentPickups, what, savedInCave, file, after);
        }

        private IEnumerator ReplayCutscene(SavestateFile f, bool stand, Vector3 at, Starter start, string what, string of)
        {
            yield return null;
            if (stand && Ctx.Player.Found)
            {
                Ctx.Player.MoveTo(at, Ctx.Player.Transform.rotation);
                Ctx.Bridge.EndFall();
            }
            float wait = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - wait < 0.2f) yield return null;
            int starts = Ctx.Events != null ? Ctx.Events.CutsceneStarts : 0;
            string note = start();
            Ctx.Log.LogInfo("Savestate " + what + ": captured " + f.CutsceneAt.ToString("0.0") +
                            " s into " + of + " - " + note + ".");
            yield return Ctx.Runner.StartCoroutine(FastForwardCutscene(f, starts, what));
        }

        private IEnumerator ReplayRides(SavestateFile f, List<Component> rides, string what)
        {
            Vector3 at;
            bool stand = _elevators.RideStart(rides[0], out at);
            return ReplayCutscene(f, stand, at, delegate { return _elevators.Replay(rides); }, what, "the red elevator's ride");
        }

        private static bool DoorCutscene(SavestateFile f)
        {
            return f != null && f.CutsceneAt >= 0f && f.KeypadDoor.Length > 0;
        }

        private IEnumerator ReplayDoor(SavestateFile f, string what)
        {
            Component door = _doors.Find(f.KeypadDoor);
            Vector3 at;
            bool stand = _doors.Stand(door, out at);
            return ReplayCutscene(f, stand, at, delegate { return _doors.Replay(door); }, what, "a keypad door's cutscene");
        }

        private IEnumerator FastForwardCutscene(SavestateFile f, int startsBefore, string what)
        {
            GameEvents ev = Ctx.Events;
            if (ev == null) yield break;

            float waitStart = Time.realtimeSinceStartup;
            while (ev.CutsceneStarts <= startsBefore)
            {
                if (Time.realtimeSinceStartup - waitStart > CutsceneWait)
                {
                    Ctx.Log.LogInfo("Savestate " + what + ": captured " + f.CutsceneAt.ToString("0.0") + " s into cutscene '" +
                                    f.Cutscene + "', but no cutscene began within " + CutsceneWait + " s of the restore - nothing fast-forwarded.");
                    yield break;
                }
                yield return null;
            }

            string running = ev.CutsceneRunning;
            if (running != null && running != f.Cutscene && running != GameEvents.AnyCutscene && f.Cutscene != GameEvents.AnyCutscene)
            {
                Ctx.Log.LogInfo("Savestate " + what + ": cutscene '" + running + "' began, not the captured '" + f.Cutscene +
                                "' - left at normal speed.");
                yield break;
            }

            float realStart = Time.realtimeSinceStartup;
            int prevSet = 0;
            // Its sounds play in real time: muted while it runs, then put
            // where a normal run would have them (maks: they ran on late).
            CutsceneAudio.Begin();
            while (ev.CutsceneRunning != null)
            {
                // The replay memorized empty hands at its start; put back
                // what the captured one memorized, so its end re-equips it
                // (every frame: its start runs a frame or two after the event).
                if (f.HeldBefore != null && f.HeldBefore.Count > 0) prevSet = _bridge.SetPreviousHeld(f.HeldBefore);

                float remaining = f.CutsceneAt - (Time.time - ev.CutsceneStartedAt);
                if (remaining <= 0f) break;

                // Paused (the ESC menu sets 0) stays paused.
                if (Time.timeScale > 0f)
                {
                    float dt = Time.unscaledDeltaTime > 0.0001f ? Time.unscaledDeltaTime : 0.016f;
                    // Half the remaining time per frame near the mark: one
                    // slow frame then lands short, not past it (a Full load
                    // overshot 1.61 s to 2.12 s, v0.24.79).
                    Time.timeScale = Mathf.Clamp(remaining / (2f * dt), 1f, CutsceneSpeed);
                }
                yield return null;
            }
            if (Time.timeScale > 0f) Time.timeScale = 1f;
            string sounds = CutsceneAudio.End();

            float reached = ev.CutsceneRunning != null ? Time.time - ev.CutsceneStartedAt : -1f;
            Ctx.Log.LogInfo("Savestate " + what + ": cutscene '" + (running ?? f.Cutscene) + "' " +
                            (reached >= 0f
                                ? "fast-forwarded to " + reached.ToString("0.00") + " s (captured at " + f.CutsceneAt.ToString("0.00") + " s)"
                                : "ended before the captured " + f.CutsceneAt.ToString("0.00") + " s") +
                            " in " + (Time.realtimeSinceStartup - realStart).ToString("0.0") + " s real time" +
                            (f.HeldBefore == null ? "" : f.HeldBefore.Count == 0 ? ", nothing held before it"
                                : ", held before it: " + prevSet + " of " + f.HeldBefore.Count + " slot(s) set back for its end") +
                            (sounds.Length > 0 ? "; " + sounds : "") + ".");
        }

        // The lab / hellcave report (Next up 3): what was loaded at capture
        // beside what is loaded now, once the restore has finished - the
        // difference is what a fix has to put back. Two seconds later:
        // streamed sections load asynchronously after a restore.
        // What settles after an in-place restore, found live through the
        // test bridge (2026-09-24): the plane wreck the game re-creates 0.3 s
        // after deserializing (the old one stays - fix list 4), and the
        // enemy setup that dies partway and leaves no cannibals at all (fix
        // list 2). One log line when it is done.
        private const float EnemyCheckDelay = 6f;

        private IEnumerator AfterInPlace(string what, bool enemies, int familiesBefore, SavestateFile file)
        {
            // A cave capture (v0.24.17 files): its cave families put back at
            // once - the live ones kept, no setup run (RestoreCave).
            float start = Time.realtimeSinceStartup;
            string cave = null;
            if (enemies && file != null && file.InCave && file.Families != null && file.Families.Count > 0)
            {
                // The restore's InACave starts updateCaveSpawns, which
                // enables the spawners over the next fixed updates.
                yield return new WaitForSecondsRealtime(0.1f);
                yield return Ctx.Runner.StartCoroutine(_enemies.RestoreCave(file.Families, file.Enemies ?? new List<string>(), 3f,
                                                                            delegate(string note) { cave = note; }));
            }
            float left = 1.5f - (Time.realtimeSinceStartup - start);
            if (left > 0f) yield return new WaitForSecondsRealtime(left);
            string plane = _bridge.ClearOldPlaneHulls();
            _planeClears++;
            if (!enemies || cave != null)
            {
                Ctx.Log.LogInfo("Savestate after restoring " + what + " in place: " + plane + (cave != null ? " | " + cave : "") + ".");
                yield break;
            }

            // v0.24.17 files on the surface: the captured families rebuilt
            // as they were (kind, members, places, health), ~2 s after the
            // restore - the game's own setup lock clears after 1 s. In a
            // cave the game's setup waits 3 s first; the older path below.
            if (file != null && file.Families != null && file.Families.Count > 0 && !file.InCave)
            {
                string rebuilt = null;
                yield return Ctx.Runner.StartCoroutine(_enemies.Rebuild(file.Families, file.Enemies ?? new List<string>(),
                                                                        delegate(string note) { rebuilt = note; }));
                Ctx.Log.LogInfo("Savestate after restoring " + what + " in place: " + plane + " | " + rebuilt + ".");
                yield break;
            }

            yield return new WaitForSecondsRealtime(EnemyCheckDelay - 1.5f);
            string check = _bridge.EnsureEnemies(familiesBefore);
            bool rerun = check.IndexOf("run again", StringComparison.Ordinal) >= 0;
            if (rerun)
            {
                yield return new WaitForSecondsRealtime(6f);
                int cannibals, families;
                _bridge.CountEnemies(out cannibals, out families);
                check += " -> " + cannibals + " active, " + families + " famil" + (families == 1 ? "y" : "ies") + " 6 s later";
            }
            // Once the families are back: the captured cannibals' places
            // (fix list 2 - author: "ideally in the same position").
            string positions = file != null && file.Enemies != null ? _enemies.RestoreByType(file.Enemies) : "";
            Ctx.Log.LogInfo("Savestate after restoring " + what + " in place: " + plane + " | " + check +
                            (positions.Length > 0 ? " | " + positions : "") + ".");
        }

        // Half a second on, once the game's own snap (inventory off during
        // the restore) has had its frames; one line only when it acts.
        private IEnumerator SyncSun(string what)
        {
            yield return new WaitForSecondsRealtime(0.5f);
            string note = SunSync.Check();
            if (note.Length > 0) Ctx.Log.LogInfo("Savestate " + what + ": " + note + ".");
        }

        private IEnumerator LogAreas(SavestateFile f)
        {
            yield return new WaitForSecondsRealtime(2f);
            string now = AreaReport.Describe();
            if (f == null || f.Areas.Length == 0) { Ctx.Log.LogInfo("Savestate areas after the restore: " + now); yield break; }
            Ctx.Log.LogInfo("Savestate areas after the restore: " +
                            (now == f.Areas ? "same as at capture (" + now + ")" : now + " || at capture: " + f.Areas));
        }

        // World pickups there now that the capture did not list (keyed by
        // item and position). Diagnostic for what an in-place restore leaves
        // behind - severed limbs (author, v0.24.7), and greeble sticks and
        // rocks that came back elsewhere (fix list 4). The removals 0.5 s
        // after the restore, when the bodies it cleared are gone; the
        // listing once the plane wreck has settled.
        private IEnumerator LogNewPickups(HashSet<string> present, string what)
        {
            yield return new WaitForSecondsRealtime(0.5f);

            // Spears thrown or dropped since the capture (author, 2026-09-24:
            // Megan's fight left them on the floor while the restored
            // inventory had them back). Never a greeble, so a new one is
            // always a leftover.
            int spears = 0;
            try { spears = _keeper.RemoveNew(present, IsSpearItem); }
            catch (Exception ex) { Ctx.Log.LogWarning("Savestate: removing thrown spears failed: " + ex.Message); }
            if (spears > 0) Ctx.Log.LogInfo("Savestate restore " + what + ": removed " + spears + " spear(s) thrown or dropped since the capture.");

            // Limbs and heads from kills since the capture (author: clear
            // what the kills left). By item name - Arm / Leg / Head.
            int limbs = 0;
            if (_respawnEnemies.Value)
            {
                try { limbs = _keeper.RemoveNew(present, IsBodyPartItem); }
                catch (Exception ex) { Ctx.Log.LogWarning("Savestate: removing limbs failed: " + ex.Message); }
                if (limbs > 0) Ctx.Log.LogInfo("Savestate restore " + what + ": removed " + limbs + " limb / head pickup(s) left since the capture.");
                try
                {
                    string bodies = _bridge.DescribeBodyCandidates();
                    if (bodies != null) Ctx.Log.LogInfo("Savestate restore " + what + ": body candidates after the restore: " + bodies);
                }
                catch (Exception) { }
            }
            // Logs from trees and sticks from saplings cut since the capture
            // (fix list 1): the trees and saplings are back (NatureKeeper).
            // A log rolls, so by item and nearest place, not exact key;
            // sticks only under a sapling's cut - greeble sticks move
            // (fix list 3) and are not ours to remove.
            int logs = 0, sticks = 0;
            try { logs = _keeper.RemoveExtra(present, IsLogItem, delegate(GameObject g) { return true; }, true); }
            catch (Exception ex) { Ctx.Log.LogWarning("Savestate: removing new logs failed: " + ex.Message); }
            try { sticks = _keeper.RemoveExtra(present, IsStickItem, NatureKeeper.UnderACut, false); }
            catch (Exception ex) { Ctx.Log.LogWarning("Savestate: removing sapling sticks failed: " + ex.Message); }
            if (logs > 0 || sticks > 0)
                Ctx.Log.LogInfo("Savestate restore " + what + ": removed " + logs + " log(s) and " + sticks +
                                " sapling stick(s) from cuts since the capture.");

            // The listing waits for the plane wreck to settle (AfterInPlace):
            // until the old wreck is cleared, both wrecks' pickups are active
            // - the plane axe taken before the capture read as "Axe Plane x2"
            // not at capture (bridge, 2026-09-25), and was gone a second later.
            int cleared = _planeClears;
            float waitFrom = Time.realtimeSinceStartup;
            while (_planeClears == cleared && Time.realtimeSinceStartup - waitFrom < 10f) yield return null;
            yield return null;   // let Destroy land before the listing below

            // The wreck the restore re-created comes with every pickup of a
            // fresh wreck: ones taken before the capture were back - the
            // plane axe, `Axe Plane x1` not at capture after every Quick
            // load, and picking it up gave nothing at its max of 1 (bridge,
            // 2026-09-25).
            GameObject hull = _bridge.CurrentPlaneHull();
            if (hull != null)
            {
                Dictionary<int, int> taken = new Dictionary<int, int>();
                int wreck = 0;
                Transform root = hull.transform;
                try { wreck = _keeper.RemoveExtra(present, delegate(int id) { return true; },
                                                  delegate(GameObject g) { return g.transform.IsChildOf(root); }, false, taken); }
                catch (Exception ex) { Ctx.Log.LogWarning("Savestate: removing wreck pickups failed: " + ex.Message); }
                if (wreck > 0)
                {
                    StringBuilder wsb = new StringBuilder();
                    foreach (KeyValuePair<int, int> kv in taken)
                        wsb.Append(wsb.Length == 0 ? "" : ", ").Append(NameOfItem(kv.Key)).Append(" x").Append(kv.Value);
                    Ctx.Log.LogInfo("Savestate restore " + what + ": removed " + wreck + " plane wreck pickup(s) taken before the capture (" +
                                    wsb + ").");
                    yield return null;
                }
            }

            List<string> now = new List<string>();
            try { _keeper.Snapshot(now); }
            catch (Exception) { yield break; }

            Dictionary<int, int> byItem = new Dictionary<int, int>();
            int n = 0;
            for (int i = 0; i < now.Count; i++)
            {
                if (present.Contains(now[i])) continue;
                n++;
                int at = now[i].IndexOf('@');
                int id;
                if (at > 0 && int.TryParse(now[i].Substring(0, at), out id))
                {
                    int c;
                    byItem.TryGetValue(id, out c);
                    byItem[id] = c + 1;
                }
            }
            if (n == 0) { Ctx.Log.LogInfo("Savestate restore " + what + ": every world pickup matches the capture."); yield break; }

            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<int, int> kv in byItem)
                sb.Append(sb.Length == 0 ? "" : ", ").Append(NameOfItem(kv.Key)).Append(" x").Append(kv.Value);
            Ctx.Log.LogInfo("Savestate restore " + what + ": " + n + " world pickup(s) not at capture (" + sb +
                            ") - moved, or new since the capture.");
        }

        private bool IsSpearItem(int id)
        {
            string n = Ctx.Inventory != null ? Ctx.Inventory.NameForId(id) : null;
            return n == "Spear";
        }

        private bool IsLogItem(int id)
        {
            string n = Ctx.Inventory != null ? Ctx.Inventory.NameForId(id) : null;
            return n == "Log";
        }

        private bool IsStickItem(int id)
        {
            string n = Ctx.Inventory != null ? Ctx.Inventory.NameForId(id) : null;
            return n == "Stick";
        }

        private bool IsBodyPartItem(int id)
        {
            string n = Ctx.Inventory != null ? Ctx.Inventory.NameForId(id) : null;
            return n == "Arm" || n == "Leg" || n == "Head";
        }

        private string NameOfItem(int id)
        {
            string n = Ctx.Inventory != null ? Ctx.Inventory.NameForId(id) : null;
            return string.IsNullOrEmpty(n) ? "item " + id : n;
        }

        private string HeldBeforeNames(List<string> entries)
        {
            string[] names = new string[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                int at = entries[i].IndexOf(':');
                int id;
                names[i] = at > 0 && int.TryParse(entries[i].Substring(at + 1), out id) ? NameOfItem(id) : entries[i];
            }
            return string.Join(", ", names);
        }

        private string HeldNames(List<int> held)
        {
            if (held == null || held.Count == 0) return "nothing";
            string[] names = new string[held.Count];
            for (int i = 0; i < held.Count; i++) names[i] = NameOfItem(held[i]);
            return string.Join(", ", names);
        }

        /// A load rebuilds the book on its default page and the panels at
        /// their scene health; once in game, put back the captured page
        /// (author: a savestate keeps its page) and panel health.
        private Action<string> AfterLoad(SavestateFile f, Action<string> after)
        {
            int cutsceneStarts = Ctx.Events != null ? Ctx.Events.CutsceneStarts : 0;
            BossHold.Arm();
            return delegate(string error)
            {
                BossHold.Arm();
                if (error == null)
                {
                    string placed = PutPlayerBack(f, true);
                    if (placed.Length > 0) Ctx.Log.LogInfo("Savestate after the load: " + placed + ".");
                    string panels = "";
                    try { panels = _panels.Restore(f.Panels, false); }
                    catch (Exception ex) { panels = "panels: restore failed (" + ex.Message + ")"; }
                    string endgame = EndgameLoader.EnsureLoaded(f.Areas);
                    Ctx.Log.LogInfo("Savestate after the load: " + _book.Apply(f.Book) +
                                    (panels.Length > 0 ? " | " + panels : "") +
                                    (endgame.Length > 0 ? " | " + endgame : "") + ".");
                    Ctx.Runner.StartCoroutine(LogAreas(f));
                    // A ride under way at capture is replayed after the
                    // hold (HoldUntilLoaded), which pins the player.
                    if (f.CutsceneAt >= 0f && RideCutsceneAt(f) < 0f && !DoorCutscene(f))
                        Ctx.Runner.StartCoroutine(FastForwardCutscene(f, cutsceneStarts, "'" + f.Name + "'"));
                    if (_respawnEnemies.Value && f.Families != null && f.Families.Count > 0 && !f.InCave)
                        Ctx.Runner.StartCoroutine(EnemiesAfterLoad(f));
                    if (_respawnEnemies.Value && f.Families != null && f.Families.Count > 0 && f.InCave)
                        Ctx.Runner.StartCoroutine(CaveEnemiesAfterLoad(f));
                    Ctx.Runner.StartCoroutine(HoldUntilLoaded(f, after));
                    return;
                }
                if (after != null) after(error);
            };
        }

        /// A load rolls the world families afresh: at other spawners, none
        /// near the player (bridge, 2026-09-24: 16 cannibals 10 s after the
        /// load, our captured family gone; maks: "with load they don't even
        /// spawn"). The captured families are rebuilt as after an in-place
        /// restore - as soon as the game asks for its own setup, which is
        /// held meanwhile (Game/SetupHold, v0.24.45; waiting for the game's
        /// families and its lock took ~5 s more - maks). If the game's setup
        /// slipped through, the old way: its families, its 1 s lock, then
        /// the rebuild (two setups at once break each other).
        /// A Full load in a cave: the game's setup (doStart 2 s after its
        /// Start, then 3 s in a cave) despawns every cannibal, and the cave
        /// families come back from their spawners after it (bridge,
        /// v0.24.49: the cave males appeared ~6 s after the load and stayed).
        /// Then the captured ones are put back as after a Quick load.
        private IEnumerator CaveEnemiesAfterLoad(SavestateFile f)
        {
            float start = Time.realtimeSinceStartup;
            yield return new WaitForSecondsRealtime(6f);
            string note = null;
            yield return Ctx.Runner.StartCoroutine(_enemies.RestoreCave(f.Families, f.Enemies ?? new List<string>(), 6f,
                                                                        delegate(string n) { note = n; }));
            Ctx.Log.LogInfo("Savestate after the load: enemies (cave) " +
                            (Time.realtimeSinceStartup - start).ToString("F1") +
                            " s after in game | " + note + ".");
        }

        private IEnumerator EnemiesAfterLoad(SavestateFile f)
        {
            float start = Time.realtimeSinceStartup;
            bool held = _enemies.CannotRebuild() == null;
            if (held) SetupHold.Arm();
            int cannibals = -1, families = -1;
            while (Time.realtimeSinceStartup - start < 30f)
            {
                if (held && SetupHold.Requested) break;
                _bridge.CountEnemies(out cannibals, out families);
                if (families > 0) break;
                yield return null;
            }
            bool asked = held && SetupHold.Requested;
            float waited = Time.realtimeSinceStartup - start;
            if (!asked && families <= 0)
            {
                SetupHold.Release();
                Ctx.Log.LogInfo("Savestate after the load: enemies - the game made no families within 30 s; captured ones not rebuilt.");
                yield break;
            }
            if (!asked)
            {
                SetupHold.Release();
                yield return new WaitForSecondsRealtime(1.5f);
            }
            // The rebuild's own setup run passes the hold.
            string rebuilt = null;
            yield return Ctx.Runner.StartCoroutine(_enemies.Rebuild(f.Families, f.Enemies ?? new List<string>(),
                                                                    delegate(string note) { rebuilt = note; }));
            float total = _enemies.PlacedAt - start;
            int skippedAtRebuild = SetupHold.Skipped;
            Ctx.Log.LogInfo("Savestate after the load: enemies - " +
                            (asked ? "the game's setup held (asked " + waited.ToString("0.0") + " s after in game)"
                                   : "the game rolled " + families + " famil" + (families == 1 ? "y" : "ies") + " (" + cannibals +
                                     " cannibals) after " + waited.ToString("0.0") + " s" + (held ? ", its setup not held" : "")) +
                            "; the captured ones placed " + total.ToString("0.0") + " s after in game | " + rebuilt + ".");

            // The hold stays on a few seconds more: a later setup call of
            // the game's would throw the rebuilt families away. Said once
            // the window closes, if any came (the proof it is needed).
            if (!asked) yield break;
            float left = SetupHold.Left();
            if (left > 0f) yield return new WaitForSecondsRealtime(left);
            if (SetupHold.Skipped > skippedAtRebuild)
                Ctx.Log.LogInfo("Savestate after the load: enemies - the game's setup was skipped " + SetupHold.Skips() +
                                " after in game, " + (SetupHold.Skipped - skippedAtRebuild) + " of them after the rebuild.");
            SetupHold.Release();
        }

        /// "In game" comes before the world has finished loading: the
        /// streamed cave props and a force-loaded endgame load for seconds
        /// more, and the restart's teleport dropped the runner into the lab
        /// floor before it existed (maks, v0.24.25: "I land inside the
        /// textures"). Keep the player where the save put him until no
        /// scene is still loading (at most 20 s), then carry on.
        private IEnumerator HoldUntilLoaded(SavestateFile f, Action<string> after)
        {
            // v0.24.26 held only while a scene was mid-load, at wherever the
            // player was: the endgame's own trigger starts its load 0.5 s
            // after ForceLoad, so nothing was loading yet, the hold let go
            // at once and maks still fell through (v0.24.26, "the player
            // still moves / falls during the loading"). Now: held at the
            // captured spot until every scene loaded at capture is loaded
            // again, and at least 1 s.
            float start = Time.realtimeSinceStartup;
            Vector3 at = new Vector3(f.X, f.Y, f.Z);
            bool pin = Ctx.Player.Found && at != Vector3.zero;
            HashSet<string> needed = f.Areas.Length > 0 ? ScenesIn(f.Areas) : new HashSet<string>();
            string missing = "";
            while (Time.realtimeSinceStartup - start < 30f)
            {
                missing = "";
                int loading = 0;
                try
                {
                    foreach (string name in needed)
                        if (!UnityEngine.SceneManagement.SceneManager.GetSceneByName(name).isLoaded)
                            missing += (missing.Length == 0 ? "" : ", ") + name;
                    for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                        if (!UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isLoaded) loading++;
                }
                catch (Exception) { }
                bool minimum = Time.realtimeSinceStartup - start >= 1f;
                if (minimum && missing.Length == 0 && loading == 0) break;
                if (pin && Ctx.Player.Found) Ctx.Player.MoveTo(at, Ctx.Player.Transform.rotation);
                yield return null;
            }
            if (pin && Ctx.Player.Found) { Ctx.Player.MoveTo(at, Ctx.Player.Transform.rotation); Ctx.Bridge.EndFall(); }
            Ctx.Log.LogInfo("Savestate after the load: held the player at the captured spot for " +
                            (Time.realtimeSinceStartup - start).ToString("F1") + " s" +
                            (missing.Length > 0 ? " - gave up waiting for " + missing : " until the captured scenes were loaded") + ".");
            RemoveTakenPickups(f, false);
            Ctx.Runner.StartCoroutine(RemoveLatePickups(f));
            // A load regrows every bush; the ones cut at capture go again.
            string again = f != null ? _nature.ApplyCuts(f.CutBushes) : "";
            if (again.Length > 0) Ctx.Log.LogInfo("Savestate after the load: " + again + ".");
            // Zones that spawned during the load took their records in
            // GreebleKeeper's prefix; the live pass says how many.
            if (f.Greebles != null)
            {
                int late = GreebleKeeper.Late;
                string greebles;
                try { greebles = _greebles.Restore(f.Greebles, true); }
                catch (Exception ex) { greebles = "greebles: failed (" + ex.Message + ")"; }
                Ctx.Log.LogInfo("Savestate after the load: " + greebles + " (" + late + " set as they spawned in the load).");
            }
            Ctx.Runner.StartCoroutine(SyncSun("after the load"));
            // A cutscene capture's hands are the fast-forward's business.
            string pullOut = f.CutsceneAt < 0f ? f.Blueprint : "";
            if (f.Held != null && f.Held.Count > 0 && f.CutsceneAt < 0f)
                Ctx.Runner.StartCoroutine(_bridge.RefreshHeld(f.Held, NameOfItem,
                    delegate(string note)
                    {
                        Ctx.Log.LogInfo("Savestate after the load: " + note + ".");
                        PullOutBlueprint(pullOut, "Savestate after the load");
                    }));
            else PullOutBlueprint(pullOut, "Savestate after the load");

            // The red elevator's ride under way at capture (ElevatorKeeper):
            // the load built the elevator fresh; set it up, place the
            // player (`after`), then start it.
            List<Component> rides = new List<Component>();
            if (RideCutsceneAt(f) >= 0f)
            {
                string note = _elevators.Restore(f.Elevators, RideCutsceneAt(f), true, rides);
                if (note.Length > 0) Ctx.Log.LogInfo("Savestate after the load: " + note + ".");
            }
            if (after != null) after(null);
            if (rides.Count > 0) Ctx.Runner.StartCoroutine(ReplayRides(f, rides, "'" + f.Name + "'"));
            else if (DoorCutscene(f)) Ctx.Runner.StartCoroutine(ReplayDoor(f, "'" + f.Name + "'"));
        }

        /// The captured blueprint back in the hands (Game/BuildMode), with
        /// its own log line; nothing when none was out. A moment later, as
        /// the hands are: after a restart's teleport, which cuts actions.
        private void PullOutBlueprint(string type, string prefix)
        {
            if (string.IsNullOrEmpty(type)) return;
            Ctx.Runner.StartCoroutine(PullOutLater(type, prefix));
        }

        private IEnumerator PullOutLater(string type, string prefix)
        {
            yield return new WaitForSecondsRealtime(0.3f);
            Ctx.Log.LogInfo(prefix + ": " + BuildMode.PullOut(type) + ".");
        }

        // A cave's pickups can switch on after the first pass (maks: cave 5's
        // coins came back while bones and booze were removed), and
        // FindObjectsOfType sees active objects only - so look again.
        private IEnumerator RemoveLatePickups(SavestateFile f)
        {
            if (f == null || f.Pickups == null || f.Pickups.Count == 0 || f.Areas.Length == 0) yield break;
            for (int i = 0; i < 10; i++)
            {
                yield return new WaitForSeconds(1f);
                RemoveTakenPickups(f, true);
            }
        }

        private void RemoveTakenPickups(SavestateFile f, bool late)
        {
            if (f == null || f.Pickups == null || f.Pickups.Count == 0 || f.Areas.Length == 0) return;
            try
            {
                HashSet<string> scenes = ScenesIn(f.Areas);
                if (scenes.Count == 0) return;
                Dictionary<int, int> counts = new Dictionary<int, int>();
                int[] skipped = new int[3];
                int n = _keeper.RemoveTakenAfterLoad(new HashSet<string>(f.Pickups), scenes, counts, skipped, new Vector3(f.X, f.Y, f.Z));
                string kept = skipped[0] + skipped[1] + skipped[2] == 0 ? "" :
                    " Not at capture but kept: " + skipped[0] + " with an identifier, " + skipped[1] +
                    " clone(s), " + skipped[2] + " in scenes not loaded at capture.";
                if (n == 0)
                {
                    if (!late) Ctx.Log.LogInfo("Savestate after the load: pickups - none back that were taken before the capture." + kept);
                    return;
                }
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<int, int> kv in counts)
                    sb.Append(sb.Length == 0 ? "" : ", ").Append(NameOfItem(kv.Key)).Append(" x").Append(kv.Value);
                Ctx.Log.LogInfo("Savestate after the load: pickups - removed " + n + (late ? " that appeared late" : "") +
                                " the load brought back (taken before the capture: " + sb + ")." + (late ? "" : kept));
            }
            catch (Exception ex) { Ctx.Log.LogWarning("Savestate after the load: removing taken pickups failed: " + ex.Message); }
        }

        /// The loaded scenes in an area line ("... | scenes: a, b (loading), c | ...").
        private static HashSet<string> ScenesIn(string areas)
        {
            HashSet<string> set = new HashSet<string>();
            int at = areas.IndexOf("| scenes: ", StringComparison.Ordinal);
            if (at < 0) return set;
            at += "| scenes: ".Length;
            int end = areas.IndexOf(" |", at, StringComparison.Ordinal);
            string list = end < 0 ? areas.Substring(at) : areas.Substring(at, end - at);
            string[] parts = list.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string s = parts[i].Trim();
                if (s.Length == 0 || s.EndsWith(" (loading)", StringComparison.Ordinal)) continue;
                set.Add(s);
            }
            return set;
        }

        private void StartLoad(string what, string error, Action<string> after)
        {
            if (error != null)
            {
                Ctx.Log.LogWarning("Savestate " + what + " failed: " + error);
                SetStatus(what + " failed: " + error);
                if (after != null) after(error);
                return;
            }

            _loadAfter = after;
            Ctx.Log.LogInfo("Savestate " + what + ": scene load started.");
            SetStatus(what + ": loading...");
            _timingLoad = true;
            _sawLoading = false;
            _loadStarted = Time.realtimeSinceStartup;
            _loadWhat = what;
        }

        // ------------------------------------------------------------------
        // Segment start states - called by the Practice module.

        public bool Busy { get { return _busy || _timingLoad; } }

        public string StartStatePath(Segment s)
        {
            return Path.Combine(Path.Combine(_dir, "segments"), SavestateFile.SafeFileName(s.Id) + SavestateFile.Extension);
        }

        /// The start state file's text, for a segment export; null when
        /// there is none.
        public string ReadStartStateText(Segment s)
        {
            if (!HasStartState(s)) return null;
            return File.ReadAllText(StartStatePath(s), Encoding.UTF8);
        }

        /// Writes an imported start state as the segment's own. Returns
        /// null, or why it was refused (not a savestate this version reads).
        public string WriteStartStateText(Segment s, string text)
        {
            string error;
            if (SavestateFile.Parse(text, out error) == null) return error;
            string path = StartStatePath(s);
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, text, Encoding.UTF8);
            return null;
        }

        public bool HasStartState(Segment s)
        {
            if (s == null || string.IsNullOrEmpty(s.Id)) return false;
            try { return File.Exists(StartStatePath(s)); }
            catch (Exception) { return false; }
        }

        /// One line for the segment editor; built on demand, not per frame.
        public string DescribeStartState(Segment s)
        {
            if (!HasStartState(s)) return "none - a restart keeps the game as it is";
            try
            {
                FileInfo fi = new FileInfo(StartStatePath(s));
                return "saved " + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm") + ", " +
                       SavestateBridge.Kb((int)Math.Min(fi.Length, int.MaxValue));
            }
            catch (Exception ex) { return "unreadable: " + ex.Message; }
        }

        /// On success the segment names the new state (`StartState`), which
        /// changes its route fingerprint; saving the segment is the caller's.
        public void CaptureStartState(Segment s, Action<string> done)
        {
            _anchor = Anchor.Top;
            CaptureTo("start of " + s.Name + " (" + s.Id + ")", StartStatePath(s), delegate(string error)
            {
                if (error == null)
                {
                    s.StartState = _lastCaptureHash;
                    Ctx.Log.LogInfo("Savestate: start state of '" + s.Id + "' is now " + s.StartState +
                                    " - route " + s.RouteFingerprint() + ".");
                }
                done(error);
            });
        }

        public string DeleteStartState(Segment s)
        {
            try
            {
                string path = StartStatePath(s);
                if (File.Exists(path)) File.Delete(path);
                s.StartState = "";
                Ctx.Log.LogInfo("Savestate: start state of '" + s.Id + "' deleted.");
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// Restores the segment's start state the segment's way; `done` gets
        /// null once the game is back in play (after the load, for a load),
        /// or the reason it could not.
        public void RestoreStartState(Segment s, Action<string> done)
        {
            if (Busy) { done("a savestate action is still running"); return; }
            if (RefusedAtTitle("restore", done)) return;
            _anchor = Anchor.Top;

            SavestateFile f;
            try
            {
                string error;
                f = SavestateFile.Parse(File.ReadAllText(StartStatePath(s), Encoding.UTF8), out error);
                if (f == null) { done("start state unreadable: " + error); return; }
            }
            catch (Exception ex) { done("start state unreadable: " + ex.Message); return; }

            // A shared segment names the state it was timed from; a file
            // that is not that state still restores, but its times will not
            // compare fairly - say so.
            if (s.StartState.Length > 0 && Segment.HashText(f.Data) != s.StartState)
                Ctx.Log.LogWarning("Savestate: the start state file of '" + s.Id + "' is not the one the segment expects (" +
                                   s.StartState + ") - recapture it to make it so.");

            string mode = ModeMismatch(f);
            if (mode != null) { done(mode); return; }

            string what = "start state of '" + s.Name + "'";
            if (s.StartRestoreWithLoad)
            {
                Ctx.Practice.Mark("savestate restore (load)");
                PickupKeeper.Armed = true;
                _greebles.Restore(f.Greebles, false);
                StartLoad(what + " with load", _bridge.RestoreWithLoad(f.Data, f.Difficulty), AfterLoad(f, done));
            }
            else
            {
                HashSet<string> present = f.Pickups != null ? new HashSet<string>(f.Pickups) : null;
                RestoreInPlace(f.Data, f.StreamingUnloaded, present, what, f.InCave ? 1 : 0, f, done);
            }
        }

        // A savestate from another save restores - in place the bridge
        // adopts the saved player, a load rebuilds everything - but not
        // across game modes: Creative is set up before the game loads and
        // is not in the save, so a Hard world would come back in Creative
        // (the author's suggestion, v0.22.1: "perhaps the same game mode").
        private string ModeMismatch(SavestateFile f)
        {
            if (!_bridge.Resolve()) return null;
            string here = _bridge.CurrentDifficulty;
            if (here.Length == 0 || f.Difficulty.Length == 0) return null;
            if ((here == "Creative") == (f.Difficulty == "Creative")) return null;

            if (_allowCrossMode.Value)
            {
                Ctx.Log.LogWarning("Savestate: '" + f.Name + "' was captured in a " + f.Difficulty +
                                   " game, this one is " + here + " - restoring anyway (AllowCrossModeRestore).");
                return null;
            }

            Ctx.Log.LogWarning("Savestate: refused '" + f.Name + "' - captured in a " + f.Difficulty +
                               " game, this one is " + here + ".");
            return "captured in a " + f.Difficulty + " game - this one is " + here + " (Creative and survival do not mix)";
        }

        private void CheckPickups()
        {
            _anchor = Anchor.Pickups;
            int id;
            if (!int.TryParse(_itemIdText.Trim(), out id)) { SetStatus("item id must be a number"); return; }

            _bridge.DescribePickups(id, _diag);
            _diagLabels.Clear();
            for (int i = 0; i < _diag.Count; i++)
            {
                _diagLabels.Add(new GUIContent(_diag[i]));
                Ctx.Log.LogInfo("Savestate pickups: " + _diag[i]);
            }
        }

        private void DeleteSelected()
        {
            if (_selected < 0 || _selected >= _files.Count) return;
            _anchor = Anchor.List;

            if (Time.unscaledTime > _deleteArmedUntil)
            {
                _deleteArmedUntil = Time.unscaledTime + 3f;
                SetStatus("click Delete again within 3 s to delete " + Path.GetFileName(_files[_selected]));
                return;
            }

            _deleteArmedUntil = 0f;
            string path = _files[_selected];
            try
            {
                File.Delete(path);
                Ctx.Log.LogInfo("Savestate deleted " + Path.GetFileName(path));
                SetStatus("deleted " + Path.GetFileName(path));
            }
            catch (Exception ex) { SetStatus("delete failed: " + ex.Message); }

            _selected = -1;
            RefreshFiles();
        }

        // ------------------------------------------------------------------
        private SavestateFile LoadSelected()
        {
            if (_selected < 0 || _selected >= _files.Count) { SetStatus("select a savestate first"); return null; }

            string path = _files[_selected];
            try
            {
                string error;
                SavestateFile f = SavestateFile.Parse(File.ReadAllText(path, Encoding.UTF8), out error);
                if (f == null)
                {
                    SetStatus(Path.GetFileName(path) + ": " + error);
                    Ctx.Log.LogWarning("Savestate " + Path.GetFileName(path) + ": " + error);
                }
                return f;
            }
            catch (Exception ex)
            {
                SetStatus("could not read " + Path.GetFileName(path) + ": " + ex.Message);
                return null;
            }
        }

        private void RefreshFiles()
        {
            string keep = _selected >= 0 && _selected < _files.Count ? _files[_selected] : null;
            _files.Clear();
            _fileLabels.Clear();

            try
            {
                if (Directory.Exists(_dir))
                {
                    string[] found = Directory.GetFiles(_dir, "*" + SavestateFile.Extension);
                    Array.Sort(found, StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < found.Length; i++)
                    {
                        FileInfo fi = new FileInfo(found[i]);
                        _files.Add(found[i]);
                        _fileLabels.Add(new GUIContent(Path.GetFileNameWithoutExtension(found[i]) + "   (" +
                            SavestateBridge.Kb((int)Math.Min(fi.Length, int.MaxValue)) + ", " +
                            fi.LastWriteTime.ToString("MM-dd HH:mm") + ")"));
                    }
                }
            }
            catch (Exception ex) { SetStatus("could not list savestates: " + ex.Message); }

            _selected = keep != null ? _files.IndexOf(keep) : -1;
        }

        private string UniquePath(string baseName)
        {
            string path = Path.Combine(_dir, baseName + SavestateFile.Extension);
            for (int n = 2; File.Exists(path); n++)
                path = Path.Combine(_dir, baseName + "-" + n + SavestateFile.Extension);
            return path;
        }

        private void SetStatus(string s)
        {
            _status = new GUIContent(s);

            // A savestate hotkey with the window closed: the answer goes on
            // screen. Practice restarts (Top) say their own piece there.
            if (_anchor != Anchor.Top && Host != null && !Host.AnyPanelOpen()) Ctx.Notice.Show(s, 5f);
        }

        private float StatusIf(Anchor a, float y, float w)
        {
            return _anchor == a ? UiText.Draw(0, y, w, _status) + 4f : 0f;
        }

        // ------------------------------------------------------------------
        public override void DrawTab(Rect area)
        {
            float w = area.width - 20f;
            // The height drawn last pass: wrapped text makes it vary.
            float contentHeight = Mathf.Max(_contentHeight, 200f);
            _scroll = GUI.BeginScrollView(area, _scroll, new Rect(0, 0, w, contentHeight));

            float y = 4f;
            y += UiText.Draw(0, y, w, Warning) + 4f;
            y += StatusIf(Anchor.Top, y, w);

            bool cross = GUI.Toggle(new Rect(0, y, w, 22), _allowCrossMode.Value,
                                    " Allow restoring across Creative and survival (testing)");
            if (cross != _allowCrossMode.Value) _allowCrossMode.Value = cross;
            y += 26f;

            bool respawn = GUI.Toggle(new Rect(0, y, w, 22), _respawnEnemies.Value,
                                      " Enemies after an in-place restore as a load would (respawn, clear bodies)");
            if (respawn != _respawnEnemies.Value) _respawnEnemies.Value = respawn;
            y += 26f;

            // Capture
            GUI.Label(new Rect(0, y, 50, 22), "Name");
            _name = GUI.TextField(new Rect(52, y, Mathf.Min(260f, w - 52f), 22), _name ?? "");
            y += 26f;
            GUI.enabled = !_busy;
            if (GUI.Button(new Rect(0, y, 200, 24), "Capture here")) Capture();
            GUI.enabled = true;
            y += 28f;
            y += StatusIf(Anchor.Capture, y, w);
            y += 4f;

            // Saved states
            y += UiText.Draw(0, y, w, _dirLabel);
            if (_fileLabels.Count == 0)
            {
                GUI.Label(new Rect(8, y, w, 20), "none yet");
                y += 22f;
            }
            for (int i = 0; i < _fileLabels.Count; i++)
            {
                bool on = GUI.Toggle(new Rect(8, y, w - 8, 22), _selected == i, _fileLabels[i]);
                if (on && _selected != i) { _selected = i; _deleteArmedUntil = 0f; }
                y += 24f;
            }
            if (GUI.Button(new Rect(0, y, 120, 22), "Refresh list")) RefreshFiles();
            y += 30f;

            GUI.enabled = !_busy && _selected >= 0;
            if (GUI.Button(new Rect(0, y, 260, 24), "Quick load (in place)")) RestoreSelectedInPlace();
            y += 28f;
            if (GUI.Button(new Rect(0, y, 260, 24), "Full load (scene reload)")) RestoreSelectedWithLoad();
            y += 28f;
            if (GUI.Button(new Rect(0, y, 120, 22), Time.unscaledTime <= _deleteArmedUntil ? "Delete - sure?" : "Delete")) DeleteSelected();
            GUI.enabled = true;
            y += 28f;
            y += StatusIf(Anchor.List, y, w);
            y += 4f;

            // The slot the game is running on
            y += UiText.Draw(0, y, w, _slotLabel);
            GUI.enabled = !_busy;
            if (GUI.Button(new Rect(0, y, 260, 24), "Quick load the slot's save")) SlotInPlace();
            y += 28f;
            if (GUI.Button(new Rect(0, y, 260, 24), "Full load the slot's save (no menu)")) SlotWithoutMenu();
            GUI.enabled = true;
            y += 28f;
            y += StatusIf(Anchor.Slot, y, w);
            y += 4f;

            // Diagnostics
            GUI.Label(new Rect(0, y, 60, 22), "Item id");
            _itemIdText = GUI.TextField(new Rect(62, y, 60, 22), _itemIdText ?? "");
            if (GUI.Button(new Rect(130, y, 130, 22), "Check pickups")) CheckPickups();
            y += 28f;
            y += StatusIf(Anchor.Pickups, y, w);
            for (int i = 0; i < _diagLabels.Count; i++)
                y += UiText.Draw(8, y, w - 8, _diagLabels[i]);
            y += 6f;

            // The load leak: what each load leaves behind.
            bool fix = GUI.Toggle(new Rect(0, y, w, 22), _threadsFix.Value,
                                  " Fix: stop the worker threads the game leaves running after a load");
            if (fix != _threadsFix.Value) { _threadsFix.Value = fix; LeakedThreads.Enabled = fix; }
            y += 26f;
            bool subs = GUI.Toggle(new Rect(0, y, w, 22), _subscribersFix.Value,
                                   " Fix: drop the old world's event subscriptions after a load (the game keeps them)");
            if (subs != _subscribersFix.Value) { _subscribersFix.Value = subs; StaleSubscribers.Enabled = subs; }
            y += 26f;
            bool census = GUI.Toggle(new Rect(0, y, w, 22), _censusOnLoad.Value,
                                     " Memory census after every load (log; a short hitch after the load)");
            if (census != _censusOnLoad.Value) _censusOnLoad.Value = census;
            y += 26f;
            // Run from Tick, never inside OnGUI.
            if (GUI.Button(new Rect(0, y, 200, 22), "Memory census now"))
            {
                _censusLabel = "a click (" + _loads.Loads + " loads so far)";
                _censusDue = Time.unscaledTime;
                _censusText.text = "running...";
            }
            y += 26f;
            y += UiText.Draw(0, y, w, _censusText);

            y += 6f;
            y += UiText.DrawDim(0, y, w, _bindLabel);
            _contentHeight = y + 8f;

            GUI.EndScrollView();
        }
    }
}
