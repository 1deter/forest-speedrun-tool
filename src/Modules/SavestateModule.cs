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
        private string _dir;

        private bool _busy;
        private ConfigEntry<bool> _allowCrossMode;
        private ConfigEntry<bool> _respawnEnemies;
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
            _keeper.Install(OverlayPlugin.PluginGuid);
            _panels.Install(OverlayPlugin.PluginGuid);
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
            map.Add("savestate.restoreInPlace", KeyCode.None, "Savestate: restore selected in place", RestoreSelectedInPlace);
            map.Add("savestate.restoreLoad", KeyCode.None, "Savestate: restore selected with load", RestoreSelectedWithLoad);
        }

        public override void Shutdown()
        {
            PickupKeeper.Armed = false;
            if (_keeper != null) _keeper.Uninstall();
            if (_panels != null) _panels.Uninstall();
            if (_threads != null) _threads.Uninstall();
        }

        // ------------------------------------------------------------------
        public override void Tick()
        {
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
                    Ctx.Log.LogInfo("Load " + _loads.Loads + " finished" + (_loads.LastFromOtherScene ? " (from the title screen)" : "") +
                                    ": Mono heap after GC " + (GC.GetTotalMemory(true) / (1024 * 1024)) + " MB.");
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

            // Captured during an endgame cutscene: note which and how far in,
            // so a restore can fast-forward the replay to this moment.
            string cutscene = Ctx.Events != null ? Ctx.Events.CutsceneRunning : null;
            float cutsceneAt = cutscene != null ? Time.time - Ctx.Events.CutsceneStartedAt : -1f;

            // Before the capture force-unloads streaming: what is loaded as
            // the player sees it.
            string areas = AreaReport.Describe();
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
                string error = OnCaptured(r, name, path, pos, inCave, pickups, book, bookNote, held, panels, cutscene, cutsceneAt, areas);
                if (after != null) after(error);
            }));
        }

        private string OnCaptured(SavestateBridge.Result r, string name, string path, Vector3 pos, bool inCave, List<string> pickups,
                                  string book, string bookNote, List<int> held, List<string> panels,
                                  string cutscene, float cutsceneAt, string areas)
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
                f.Panels = panels;
                if (cutscene != null) { f.Cutscene = cutscene; f.CutsceneAt = cutsceneAt; }
                f.Areas = areas;
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
                              (panels.Count > 0 ? ", " + panels.Count + " cave panels" : "") +
                              (cutscene != null ? ", during cutscene '" + cutscene + "' at " + cutsceneAt.ToString("0.0") + " s" : "");
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
        private void RestoreInPlace(string data, bool unloadStreaming, HashSet<string> presentPickups, string what,
                                    int savedInCave, SavestateFile file, Action<string> after)
        {
            if (_busy) { if (after != null) after("a savestate action is still running"); return; }
            _busy = true;
            _busySince = Time.realtimeSinceStartup;
            int cutsceneStarts = Ctx.Events != null ? Ctx.Events.CutsceneStarts : 0;
            Ctx.Practice.Mark("savestate restore (in place)");
            PickupKeeper.Armed = true;
            SetStatus("restoring " + what + " in place...");
            Ctx.Log.LogInfo("Savestate restore " + what + " in place: starting.");
            int cannibalsBefore, familiesBefore;
            _bridge.CountEnemies(out cannibalsBefore, out familiesBefore);

            // Before as well as after: a physics step during the restore
            // could land the old fall at the restored spot.
            string fall = Ctx.Bridge.EndFall();

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

                string bookNote = r.Ok && file != null ? _book.Apply(file.Book) : "";
                // Blood on the player's body and weapon is not in the save.
                string washNote = r.Ok ? _bridge.Wash() : "";

                // A slot reload in place too: every in-place restore.
                string enemyNote = r.Ok && _respawnEnemies.Value ? _bridge.RespawnEnemies(surfaceSent) : "";
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

                // The hands were emptied for the restore; put back what they
                // held at capture (runner maks: the lighter came back away,
                // and unlit). Its own log line, a moment later.
                if (r.Ok && file != null && file.CutsceneAt >= 0f)
                    Ctx.Runner.StartCoroutine(FastForwardCutscene(file, cutsceneStarts, what));

                if (r.Ok && file != null && file.Held != null && file.Held.Count > 0)
                    Ctx.Runner.StartCoroutine(_bridge.ReEquip(file.Held, NameOfItem,
                        delegate(string note) { Ctx.Log.LogInfo("Savestate restore " + what + ": " + note + "."); }));

                string line = "restore " + what + " in place: " + r.Message +
                              ", pickups put back " + pickups +
                              (presentPickups == null ? " (all kept)" : "") +
                              (string.IsNullOrEmpty(cave) ? "" : " | cave: " + cave) +
                              (fall.Length == 0 ? "" : " | " + fall) +
                              (bookNote.Length == 0 ? "" : " | " + bookNote) +
                              (washNote.Length == 0 ? "" : " | " + washNote) +
                              (panelNote.Length == 0 ? "" : " | " + panelNote) +
                              (enemyNote.Length == 0 ? "" : " | " + enemyNote);
                if (r.Ok) Ctx.Log.LogInfo("Savestate " + line);
                else Ctx.Log.LogWarning("Savestate " + line);
                if (r.Ok) Ctx.Runner.StartCoroutine(LogAreas(file));
                if (r.Ok) Ctx.Runner.StartCoroutine(AfterInPlace(what, _respawnEnemies.Value, familiesBefore));
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
        private const float CutsceneSpeed = 6f;
        private const float CutsceneWait = 20f;

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
            while (ev.CutsceneRunning != null)
            {
                float remaining = f.CutsceneAt - (Time.time - ev.CutsceneStartedAt);
                if (remaining <= 0f) break;

                // Paused (the ESC menu sets 0) stays paused.
                if (Time.timeScale > 0f)
                {
                    float dt = Time.unscaledDeltaTime > 0.0001f ? Time.unscaledDeltaTime : 0.016f;
                    Time.timeScale = Mathf.Clamp(remaining / dt, 1f, CutsceneSpeed);
                }
                yield return null;
            }
            if (Time.timeScale > 0f) Time.timeScale = 1f;

            float reached = ev.CutsceneRunning != null ? Time.time - ev.CutsceneStartedAt : -1f;
            Ctx.Log.LogInfo("Savestate " + what + ": cutscene '" + (running ?? f.Cutscene) + "' " +
                            (reached >= 0f
                                ? "fast-forwarded to " + reached.ToString("0.00") + " s (captured at " + f.CutsceneAt.ToString("0.00") + " s)"
                                : "ended before the captured " + f.CutsceneAt.ToString("0.00") + " s") +
                            " in " + (Time.realtimeSinceStartup - realStart).ToString("0.0") + " s real time.");
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

        private IEnumerator AfterInPlace(string what, bool enemies, int familiesBefore)
        {
            yield return new WaitForSecondsRealtime(1.5f);
            string plane = _bridge.ClearOldPlaneHulls();
            if (!enemies)
            {
                Ctx.Log.LogInfo("Savestate after restoring " + what + " in place: " + plane + ".");
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
            Ctx.Log.LogInfo("Savestate after restoring " + what + " in place: " + plane + " | " + check + ".");
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
        // rocks that came back elsewhere (fix list 4). Once, 0.5 s after the
        // restore, when the bodies it cleared are gone.
        private IEnumerator LogNewPickups(HashSet<string> present, string what)
        {
            yield return new WaitForSecondsRealtime(0.5f);

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
                yield return null;   // let Destroy land before the listing below
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
            return delegate(string error)
            {
                if (error == null)
                {
                    string panels = "";
                    try { panels = _panels.Restore(f.Panels, false); }
                    catch (Exception ex) { panels = "panels: restore failed (" + ex.Message + ")"; }
                    Ctx.Log.LogInfo("Savestate after the load: " + _book.Apply(f.Book) +
                                    (panels.Length > 0 ? " | " + panels : "") + ".");
                    Ctx.Runner.StartCoroutine(LogAreas(f));
                    if (f.CutsceneAt >= 0f)
                        Ctx.Runner.StartCoroutine(FastForwardCutscene(f, cutsceneStarts, "'" + f.Name + "'"));
                }
                if (after != null) after(error);
            };
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
            if (GUI.Button(new Rect(0, y, 260, 24), "Restore in place (no load)")) RestoreSelectedInPlace();
            y += 28f;
            if (GUI.Button(new Rect(0, y, 260, 24), "Restore with load")) RestoreSelectedWithLoad();
            y += 28f;
            if (GUI.Button(new Rect(0, y, 120, 22), Time.unscaledTime <= _deleteArmedUntil ? "Delete - sure?" : "Delete")) DeleteSelected();
            GUI.enabled = true;
            y += 28f;
            y += StatusIf(Anchor.List, y, w);
            y += 4f;

            // The slot the game is running on
            y += UiText.Draw(0, y, w, _slotLabel);
            GUI.enabled = !_busy;
            if (GUI.Button(new Rect(0, y, 260, 24), "Reload slot save in place (no load)")) SlotInPlace();
            y += 28f;
            if (GUI.Button(new Rect(0, y, 260, 24), "Load slot save without the menu")) SlotWithoutMenu();
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
