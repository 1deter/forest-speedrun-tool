using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using BepInEx.Configuration;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Savestates - the engine behind segment start states (Practice:
    // Capture / Restart, F7, a death at a start-state spot) and the test
    // bridge's capture / restore. PRACTICE ONLY. No tab of its own since
    // v0.24.106: its options and the Memory section are drawn in Debug
    // views (DrawOptions). See Game/SavestateBridge.cs and game-notes
    // "Saving and loading".
    //
    //   Quick load - LoadNow into the running scene, no load; deletes what
    //                the save does not know and puts back world pickups
    //                taken since (Game/PickupKeeper), plus the keepers.
    //   Full load  - LoadSavedLevel: one scene load, then the fix-ups.
    //
    // Every action logs one "Savestate ..." line; that line is the test.
    //
    // A segment's start state is savestates/segments/<safe segment id>.fosave
    // - named from the id, so a shared segment and its start state travel
    // together. The Practice module restores it on every restart, in place
    // or with a load per Segment.StartRestoreWithLoad, then teleports to
    // the spawn. No file: the restart keeps the game state, which some
    // routes need (runner request). Named files in savestates/ are the
    // bridge's (capture / restore / savestates).
    // ------------------------------------------------------------------
    public sealed class SavestateModule : OverlayModule
    {
        private const float LoadTimingTimeout = 180f;

        public override string Id { get { return "savestates"; } }
        public override string DisplayName { get { return "Savestates"; } }
        public override bool IsPracticeOnly { get { return true; } }

        private SavestateBridge _bridge;
        private PickupKeeper _keeper;
        private BookPages _book;
        private PanelKeeper _panels;
        private BossHold _bossHold;
        private SetupHold _setupHold;
        private MeganKeeper _megan;
        private ElevatorKeeper _elevators;
        private SlidingDoorKeeper _slidingDoors;
        private WeatherKeeper _weather;
        private KeypadDoorKeeper _doors;
        private AreaKeeper _area;
        private NatureKeeper _nature;
        private FireKeeper _fire;
        private AnimalKeeper _animals;
        private GreebleKeeper _greebles;
        private BreakableKeeper _breakables;
        private EnemyKeeper _enemies;
        private readonly PlayerKeep _player = new PlayerKeep();
        private string _dir;

        // The start state file the last RestoreStartState read, and whose
        // segment: a Keep loaded restart reads its keepers' lines (T-0212).
        private SavestateFile _startFile;
        private string _startFileFor = "";

        /// An in-place or Full restore's areas settled (LogAreas, 2 s on):
        /// true when they are the capture's (or the file names none).
        public Action<bool> OnRestoreSettled;

        private bool _busy;
        // EndgameFirst ran for the next RestoreInPlace: never twice.
        private bool _endgameTried;
        private ConfigEntry<bool> _respawnEnemies;
        // AfterInPlace's wreck clears so far; LogNewPickups lists after one.
        private int _planeClears;
        private string _lastCaptureHash = "";
        private float _busySince;

        // The bridge's named files (savestates), rebuilt on each listing.
        private readonly List<string> _files = new List<string>();
        private readonly List<string> _fileLabels = new List<string>();

        // The last action's outcome; callers get it through their callback.
        private string _status = "";

        // Timing a scene-load restore across the load.
        private bool _timingLoad;
        private Action<string> _loadAfter;
        private bool _sawLoading;
        private float _loadStarted;
        // A Full load's "can the player move" line (Game/PlayerHold), due
        // this long after in game; -1 = none due.
        private const float HoldCheckDelay = 3f;
        private float _holdCheckAt = -1f;
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
        private ConfigEntry<bool> _pathPoolFix;
        private PathPoolCleanup _pathPool;
        private float _censusDue;
        private string _censusLabel = "";
        private readonly GUIContent _censusText = new GUIContent("");
        private const float CensusDelay = 1.5f;

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
            _slidingDoors = new SlidingDoorKeeper(ctx.Log);
            _weather = new WeatherKeeper(ctx.Log);
            _doors = new KeypadDoorKeeper(ctx.Log);
            _area = new AreaKeeper(ctx.Log);
            _nature = new NatureKeeper(ctx.Log);
            _nature.Install(OverlayPlugin.PluginGuid);
            _fire = new FireKeeper(ctx.Log);
            _fire.Install(OverlayPlugin.PluginGuid);
            _animals = new AnimalKeeper(ctx.Log);
            _greebles = new GreebleKeeper(ctx.Log);
            _greebles.Install(OverlayPlugin.PluginGuid);
            _breakables = new BreakableKeeper(ctx.Log);
            _breakables.Install(OverlayPlugin.PluginGuid);
            CutsceneAudio.Install(ctx.Log, OverlayPlugin.PluginGuid);
            FullCapacityWatch.Install(ctx.Log, OverlayPlugin.PluginGuid);
            TitleLoad.Install(ctx.Log, OverlayPlugin.PluginGuid);
            NatureGuideKeeper.Install(ctx.Log, OverlayPlugin.PluginGuid);
            SlotInfoGuard.Install(ctx.Log, OverlayPlugin.PluginGuid);
            PathfindingWatch.Install(ctx.Log, OverlayPlugin.PluginGuid);
            // The keepers' scene searches, kept between restores, and the
            // re-created plane wreck's crash clearing skipped (T-0148).
            SceneCache.Install();
            WreckClearing.Install(ctx.Log, OverlayPlugin.PluginGuid);
            // The elevators that started a ride, so a teleport stops them
            // without a scene search (T-0184).
            ElevatorRides.Install(ctx.Log, OverlayPlugin.PluginGuid);
            _dir = Path.Combine(ctx.ConfigDirectory, "savestates");
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

            // The A* path pool keeps the old world's navmesh (T-0034).
            _pathPoolFix = ctx.Config.Bind("Fixes", "ClearPathPoolOnLoad", true,
                "When a new world starts, empty the game's pool of old pathfinding paths - it keeps the previous world's " +
                "navigation mesh in memory (~100 MB) for a minute or more after a load.");
            PathPoolCleanup.Enabled = _pathPoolFix.Value;
            _pathPool = new PathPoolCleanup(ctx.Log);
            _pathPool.Install(OverlayPlugin.PluginGuid);
            // Off since v0.23.6 (the leak is fixed; the census is the ~0.7 s
            // hitch after a load). A new key, so existing configs turn it off.
            _censusOnLoad = ctx.Config.Bind("Diagnostics", "MemoryCensusAfterEveryLoad", false,
                "After every load, log the Mono heap and which static references hold destroyed objects " +
                "(the load memory leak investigation). Costs a hitch of up to a second or so, just after a load.");

            _respawnEnemies = ctx.Config.Bind("Savestates", "RespawnEnemiesInPlace", true,
                "After an in-place restore, enemies as after a load: the game's own family setup respawns enemies " +
                "killed since the capture (where the game places them, not exactly where they stood), and dead " +
                "bodies left since the capture are cleared. Nothing is spawned when the game has enemies off.");
        }

        public override void Shutdown()
        {
            PickupKeeper.Armed = false;
            if (_keeper != null) _keeper.Uninstall();
            if (_panels != null) _panels.Uninstall();
            if (_nature != null) _nature.Uninstall();
            if (_fire != null) _fire.Uninstall();
            if (_greebles != null) _greebles.Uninstall();
            if (_breakables != null) _breakables.Uninstall();
            if (_bossHold != null) _bossHold.Uninstall();
            if (_setupHold != null) _setupHold.Uninstall();
            CutsceneAudio.Uninstall();
            FullCapacityWatch.Uninstall();
            TitleLoad.Uninstall();
            NatureGuideKeeper.Uninstall();
            SlotInfoGuard.Uninstall();
            PathfindingWatch.Uninstall();
            WreckClearing.Uninstall();
            ElevatorRides.Uninstall();
            SceneCache.Uninstall();
            if (_threads != null) _threads.Uninstall();
            if (_pathPool != null) _pathPool.Uninstall();
        }

        // ------------------------------------------------------------------
        public override void Tick()
        {
            // A waiting greeble record never acts in another game.
            if (PlayerRef.AtTitleScreen && _greebles != null) _greebles.Clear();
            if (PlayerRef.AtTitleScreen && _breakables != null) _breakables.Clear();

            if (_timingLoad) TimeLoad();
            if (_holdCheckAt > 0f && Time.realtimeSinceStartup >= _holdCheckAt)
            {
                _holdCheckAt = -1f;
                if (Ctx.Player.Found)
                    Ctx.Log.LogInfo("Savestate after the load: player " + HoldCheckDelay.ToString("0") +
                                    " s after in game - " + PlayerHold.Describe() + ".");
            }
            WatchLoads();
            PathfindingWatch.Tick();
            DrainWrites();
            if (_nature != null) _nature.Tick();

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
                _holdCheckAt = Time.realtimeSinceStartup + HoldCheckDelay;
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
                _nature.ForgetWorld();
                FireKeeper.Prune();
                _keeper.PruneDestroyed();
                _panels.PruneDestroyed();
                DeathHooks.ForgetDeath();
                DrawingsReader.ForgetView();
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
            TitleLoad.Clear();
            Action<string> after = _loadAfter;
            _loadAfter = null;
            if (after == null) return;
            try { after(error); }
            catch (Exception ex) { Ctx.Log.LogWarning("Savestate: continuation failed: " + ex.Message); }
        }

        // ------------------------------------------------------------------
        // Actions

        /// `path` null: a new file in the savestates folder, never
        /// overwriting. `after` gets null on success or the reason.
        private void CaptureTo(string name, string path, Action<string> after)
        {
            CaptureTo(name, path, null, after);
        }

        private void CaptureTo(string name, string path, CaptureOptions opts, Action<string> after)
        {
            if (RefusedInRun("capture", after)) return;
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

            // The game saves with its inventory and pause menu closed (its
            // save routine closes them): the crafting cog is not in the save,
            // so items on it would be lost, and an open one stops game time,
            // which the capture's frames need (it sat until the inventory
            // closed - audit, v0.24.203).
            // A checkpoint capture mid-run skips instead: closing the
            // runner's menu would be the overlay playing the game.
            if (opts != null && opts.Background && MenuClose.AnyOpen())
            {
                if (after != null) after("the pause menu or the inventory is open");
                return;
            }
            string menu = MenuClose.IfOpen();
            if (menu.Length > 0) Ctx.Log.LogInfo("Savestate capture: " + menu + " first, as the game's save does.");

            _busy = true;
            _busySince = Time.realtimeSinceStartup;
            Ctx.Practice.Mark("savestate capture");
            PickupKeeper.Armed = true;
            SetStatus("capturing...");

            CaptureParts p = new CaptureParts();
            p.Name = name;
            p.Path = path;
            p.Opts = opts;
            p.After = after;
            p.Pos = Ctx.Player.Found ? Ctx.Player.Transform.position : Vector3.zero;
            p.InCave = Ctx.Bridge.IsInCaves();

            // A checkpoint state mid-run (T-0072, PerfPatches 22): the
            // snapshots spread over frames, a few ms of them each.
            if (opts != null && opts.Background && PerfPatches.CheckpointCaptureSpread)
            {
                Ctx.Runner.StartCoroutine(SnapshotsSpread(p));
                return;
            }
            // One frame, in the order captures always used: pickups first.
            RunSnapshotStep(PickupsStep, p);
            for (int i = 0; i < PickupsStep; i++) RunSnapshotStep(i, p);
            p.Frames = 1;
            p.MaxFrameMs = p.PreMs;
            StartSerialize(p);
        }

        // ------------------------------------------------------------------
        // A capture's snapshots: what the keepers note before the level is
        // serialized (all outside the save). In one frame: pickups first, then
        // in this order (as before T-0072). A checkpoint state's over several
        // frames (SnapshotsSpread): in this order, the world pickups last, so
        // they are listed in the frame before the serialize's, as when
        // everything ran in one frame (a pickup taken in between would be
        // both in the bag and on the ground).

        private sealed class CaptureParts
        {
            public string Name, Path;
            public CaptureOptions Opts;
            public Action<string> After;
            public Vector3 Pos;
            public bool InCave;
            public List<string> Pickups = new List<string>();
            public string Book = "", BookNote = "";
            public List<int> Held = new List<int>();
            public List<string> HeldBefore = new List<string>();
            public string Ride = "", Blueprints = "";
            public string Cutscene;
            public float CutsceneAt = -1f;
            public string Megan = "", Elevators = "", SlidingDoors = "", Weather = "", KeypadDoor = "";
            public string ActiveArea = "", AreaWarning = "";
            public string Blueprint = "", Stance = "", Rope = "";
            public int Logs;
            public string Bushes = "", Spawned = "", Dropped = "";
            // Null until its step ran: no line rather than "none lit".
            public string Fires;
            public string Animals = "";
            public string AnimalPoses = "", AnimalZones = "", AnimalCounts = "";
            public List<string> CutBushes, Chopped, Greebles, Broken;
            public string Areas = "", EnemyNote = "";
            public List<string> Enemies = new List<string>();
            public List<string> Families = new List<string>();
            public List<string> Panels = new List<string>();
            // The snapshots' cost: all steps, the steps of a ms or more, the
            // frames they took and the largest frame's share.
            public long PreTicks;
            public readonly StringBuilder Laps = new StringBuilder();
            public int Frames;
            public long MaxFrameMs;
            public long PreMs { get { return PreTicks * 1000 / System.Diagnostics.Stopwatch.Frequency; } }
        }

        private const int SnapshotSteps = 15;
        private const int PickupsStep = 14;
        /// A spread capture's snapshot work a frame: steps run until it is
        /// used (at least one a frame).
        private const double SpreadBudgetMs = 4.0;

        private static readonly string[] SnapshotStepNames =
        {
            "book, held, ride, blueprints", "megan", "elevators", "doors", "weather", "area",
            "build, stance, logs, rope", "bushes", "greebles", "stalagmites", "areas", "enemies", "panels",
            "nearest panel", "pickups",
        };

        /// One step, timed; a step that throws is logged and the capture
        /// goes on (its part stays empty).
        private double RunSnapshotStep(int i, CaptureParts p)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { SnapshotStep(i, p); }
            catch (Exception ex) { Ctx.Log.LogWarning("Savestate: capture step '" + SnapshotStepNames[i] + "' failed: " + ex.Message); }
            long dt = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            p.PreTicks += dt;
            double ms = dt * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (ms >= 1.0) p.Laps.Append(p.Laps.Length > 0 ? ", " : "").Append(SnapshotStepNames[i]).Append(' ').Append(ms.ToString("0"));
            return ms;
        }

        private void SnapshotStep(int i, CaptureParts p)
        {
            switch (i)
            {
                case 0:
                {
                    p.Book = _book.Capture(out p.BookNote);
                    p.Held = _bridge.HeldIds();
                    p.HeldBefore = _bridge.PreviousHeld();
                    // A ride / climb is outside the save: noted here, before the
                    // capture's frames move the player on (RideModes).
                    p.Ride = RideModes.Capture(p.Pos);
                    p.Blueprints = BlueprintKeeper.Capture(Ctx.Player.Found ? Ctx.Player.Transform.root : null);
                    if (p.Ride.Length > 0)
                        Ctx.Log.LogInfo("Savestate capture: on a " + RideModes.Current() + " (" + p.Ride + ") - a restore puts you back on it.");
                    // The book stows the hands: the save then says nothing is held,
                    // and what closing the book would take back out is the
                    // inventory's "previously equipped" memory. A capture with the
                    // book open restored with empty hands (Quick load audit,
                    // 2026-10-01) - so those are the held items.
                    List<int> held = p.Held;
                    List<string> heldBefore = p.HeldBefore;
                    if (held.Count == 0 && heldBefore.Count > 0 && BookClose.IsOpen())
                    {
                        for (int k = 0; k < heldBefore.Count; k++)
                        {
                            string e = heldBefore[k];
                            int colon = e.IndexOf(':'), id;
                            if (colon > 0 && int.TryParse(e.Substring(colon + 1), out id) && id > 0 && !held.Contains(id)) held.Add(id);
                        }
                        if (held.Count > 0)
                            Ctx.Log.LogInfo("Savestate capture: the book is open - held items taken from what it put away (" + HeldNames(held) + ").");
                    }
                    break;
                }
                case 1:
                    // Captured during an endgame cutscene: note which and how far in,
                    // so a restore can fast-forward the replay to this moment.
                    p.Cutscene = Ctx.Events != null ? Ctx.Events.CutsceneRunning : null;
                    p.CutsceneAt = p.Cutscene != null ? Time.time - Ctx.Events.CutsceneStartedAt : -1f;
                    p.Megan = _megan.Capture();
                    break;
                case 2:
                {
                    float rideAge = Ctx.Events != null && Ctx.Events.RedElevatorAt >= 0f ? Time.time - Ctx.Events.RedElevatorAt : -1f;
                    p.Elevators = _elevators.Capture(rideAge);
                    break;
                }
                case 3: p.SlidingDoors = _slidingDoors.Capture(); break;
                case 4: p.Weather = _weather.Capture(); break;
                case 5:
                    p.KeypadDoor = p.Cutscene == GameEvents.KeycardDoor ? _doors.Capture(GameEvents.LastDoorPos) : "";
                    p.ActiveArea = _area.Capture();
                    p.AreaWarning = p.ActiveArea == AreaKeeper.None ? _area.UnenteredSection(p.Pos) : "";
                    break;
                case 6:
                    p.Blueprint = BuildMode.Capture();
                    p.Stance = Stance.Capture();
                    p.Logs = LogStore.Stored();
                    p.Rope = RopeClimb.Capture();
                    break;
                case 7:
                    p.Bushes = _nature.CaptureMark();
                    p.Spawned = CaptureSpawnMark();
                    p.Dropped = PickupKeeper.CaptureDropped(Ctx.Player.Found ? Ctx.Player.Transform : null);
                    p.CutBushes = _nature.CaptureCuts();
                    p.Chopped = _nature.CaptureChopped();
                    p.Fires = _fire.Capture();
                    p.Animals = _animals.Capture();
                    _animals.CaptureSpawns(out p.AnimalPoses, out p.AnimalZones, out p.AnimalCounts);
                    break;
                case 8: p.Greebles = _greebles.Capture(); break;
                case 9: p.Broken = _breakables.Capture(); break;
                // Before the capture force-unloads streaming: what is loaded as
                // the player sees it.
                case 10: p.Areas = AreaReport.Describe(); break;
                case 11: _enemies.Capture(p.Enemies, p.Families, out p.EnemyNote); break;
                case 12: _panels.Snapshot(p.Panels); break;
                case 13:
                {
                    string near = Ctx.Player.Found ? _panels.DescribeNearest(Ctx.Player.Transform.position) : null;
                    if (near != null) Ctx.Log.LogInfo("Savestate: nearest cave panel at capture: " + near);
                    break;
                }
                // Before the capture unloads streaming, while every pickup here is
                // still loaded.
                case 14: _keeper.Snapshot(p.Pickups); break;
            }
        }

        private IEnumerator SnapshotsSpread(CaptureParts p)
        {
            int i = 0;
            while (i < SnapshotSteps)
            {
                double frame = 0.0;
                do frame += RunSnapshotStep(i++, p);
                while (i < SnapshotSteps && frame < SpreadBudgetMs);
                p.Frames++;
                if ((long)frame > p.MaxFrameMs) p.MaxFrameMs = (long)frame;
                yield return null;
            }
            StartSerialize(p);
        }

        private void StartSerialize(CaptureParts p)
        {
            CaptureOptions opts = p.Opts;
            bool light = opts != null && opts.Light;
            if (opts != null)
            {
                opts.PreMs = p.PreMs;
                opts.PreParts = p.Laps.ToString() + (p.Frames > 1 ? (p.Laps.Length > 0 ? "; " : "") + "over " + p.Frames + " frames, " + p.MaxFrameMs + " ms the most in one" : "");
            }
            Ctx.Runner.StartCoroutine(_bridge.Capture(light, delegate(SavestateBridge.Result r)
            {
                string error = OnCaptured(r, p);
                // The streaming reload brought back story-spot pickups taken before (T-0066).
                if (error == null && r.StreamingUnloaded)
                    Ctx.Runner.StartCoroutine(StoryPickupsAfterReload(p.Pickups, p.Areas, "capture '" + p.Name + "'"));
                // Written on a worker thread: `after` runs once the file is
                // on disk (DrainWrites, main thread).
                if (error == null && opts != null && opts.Queued) { opts.After = p.After; return; }
                if (error == null && p.AreaWarning.Length > 0)
                {
                    Ctx.Log.LogWarning("Savestate captured '" + p.Name + "': " + p.AreaWarning + ".");
                    Ctx.Notice.Show("Captured - but " + p.AreaWarning + ".", 10f);
                }
                if (p.After != null) p.After(error);
            }));
        }

        private string OnCaptured(SavestateBridge.Result r, CaptureParts p)
        {
            string name = p.Name, path = p.Path;
            Vector3 pos = p.Pos;
            bool inCave = p.InCave;
            List<string> pickups = p.Pickups, heldBefore = p.HeldBefore, panels = p.Panels;
            List<int> held = p.Held;
            string book = p.Book, bookNote = p.BookNote, cutscene = p.Cutscene, megan = p.Megan, elevators = p.Elevators;
            string slidingDoors = p.SlidingDoors, activeArea = p.ActiveArea, keypadDoor = p.KeypadDoor, blueprint = p.Blueprint;
            string areas = p.Areas, enemyNote = p.EnemyNote ?? "", bushes = p.Bushes, spawned = p.Spawned, dropped = p.Dropped, stance = p.Stance, rope = p.Rope, ride = p.Ride;
            string blueprints = p.Blueprints, weather = p.Weather;
            float cutsceneAt = p.CutsceneAt;
            List<string> enemies = p.Enemies, families = p.Families, cutBushes = p.CutBushes, chopped = p.Chopped, greebles = p.Greebles, broken = p.Broken;
            int logs = p.Logs;
            CaptureOptions opts = p.Opts;
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
                f.BaseDifficulty = r.BaseDifficulty;
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
                f.SlidingDoors = slidingDoors;
                f.ActiveArea = activeArea;
                f.KeypadDoor = keypadDoor;
                f.Blueprint = blueprint;
                f.Stance = stance;
                f.Logs = logs;
                f.Rope = rope;
                f.Ride = ride;
                f.Blueprints = blueprints;
                f.Weather = weather;
                f.Bushes = bushes;
                f.Spawned = spawned;
                f.Dropped = dropped;
                f.CutBushes = cutBushes;
                f.Fires = p.Fires;
                f.Animals = p.Animals;
                f.AnimalPoses = p.AnimalPoses; f.AnimalZones = p.AnimalZones; f.AnimalCounts = p.AnimalCounts;
                f.Chopped = chopped;
                f.Greebles = greebles;
                f.Broken = broken;
                f.Areas = areas;
                f.Enemies = enemies;
                f.Families = families;
                // A lean capture's text is built by the write's worker.
                if (r.Data == null && r.Bytes != null && (opts == null || !opts.Background)) r.Data = SavestateBridge.DataText(r.Bytes);
                f.Data = r.Data;

                if (path == null)
                {
                    Directory.CreateDirectory(_dir);
                    path = UniquePath(SavestateFile.SafeFileName(name));
                }
                else Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (opts != null && opts.Background)
                {
                    // Serialized here, on the main thread (the game's
                    // serializer is Unity's); building the text and the
                    // disk write go to a worker thread.
                    QueueWrite(f, path, opts, r);
                }
                else
                {
                    File.WriteAllText(path, f.Write(), new UTF8Encoding(false));
                    _lastCaptureHash = Segment.HashText(f.Data);
                }

                string line = "captured '" + name + "' -> " + Path.GetFileName(path) + ": " + r.Message +
                              ", " + pickups.Count + " world pickups listed, " + bookNote +
                              ", held " + HeldNames(held) +
                              (heldBefore != null && heldBefore.Count > 0 ? " (before that: " + HeldBeforeNames(heldBefore) + ")" : "") +
                              (panels.Count > 0 ? ", " + panels.Count + " cave panels" : "") +
                              (cutscene != null ? ", during cutscene '" + cutscene + "' at " + cutsceneAt.ToString("0.0") + " s" : "") +
                              (megan.Length > 0 ? ", Megan " + megan : "") +
                              (blueprint.Length > 0 ? ", blueprint " + blueprint + " out" : "") +
                              (stance == Stance.Crouched ? ", crouched" : "") +
                              (rope.Length > 0 ? ", on a rope" : "") +
                              (weather.Length > 0 ? ", weather " + DescribeWeather(weather) : "") +
                              (enemyNote.Length > 0 ? ", " + enemyNote : "");
                Ctx.Log.LogInfo("Savestate " + line);
                Ctx.Log.LogInfo("Savestate areas at capture: " + areas);
                SetStatus(line);

                return null;
            }
            catch (Exception ex)
            {
                Ctx.Log.LogWarning("Savestate capture: could not write the file: " + ex.Message);
                SetStatus("capture failed writing the file: " + ex.Message);
                return "could not write the file: " + ex.Message;
            }
        }

        /// `done` gets null on success or the reason (not called for an
        /// unreadable file - LoadSelected has said why).
        private void RestoreFile(SavestateFile f, bool load, Action<string> done)
        {
            if (f == null) { if (done != null) done("could not read the file - " + _status); return; }
            if (_busy) { if (done != null) done("a savestate action is still running"); return; }
            if (RefusedInRun("restore", done)) return;
            if (!load && MustLoad(f, "'" + f.Name + "'")) load = true;

            if (!load)
            {
                HashSet<string> present = f.Pickups != null ? new HashSet<string>(f.Pickups) : null;
                RestoreInPlace(f.Data, f.StreamingUnloaded, present, "'" + f.Name + "'", f.InCave ? 1 : 0, f, done);
                return;
            }

            FullLoad(f, "restore '" + f.Name + "' with load", done);
        }

        private void FullLoad(SavestateFile f, string what, Action<string> done)
        {
            Ctx.Practice.Mark("savestate restore (load)");
            PickupKeeper.Armed = true;
            CloseMenuBeforeLoad();
            _greebles.Restore(f.Greebles, false);
            _breakables.Restore(f.Broken, false);
            string err = _bridge.RestoreWithLoad(f.Data, f.Difficulty, f.BaseDifficulty);
            StartLoad(what, err, AfterLoad(f, done));
        }

        // ------------------------------------------------------------------
        // The test bridge (Modules/BridgeModule).

        /// "name  (size, date)" per savestate file (not the segment ones).
        public void ListFiles(List<string> into)
        {
            RefreshFiles();
            into.AddRange(_fileLabels);
        }

        public void CaptureNamed(string name, Action<string> done)
        {
            CaptureTo(name, null, done);
        }

        /// By file name without the extension, case-insensitive.
        public void RestoreNamed(string name, bool load, Action<string> done)
        {
            RestoreGarbage.Begin();
            RefreshFiles();
            int found = -1;
            for (int i = 0; i < _files.Count; i++)
                if (string.Equals(Path.GetFileNameWithoutExtension(_files[i]), name, StringComparison.OrdinalIgnoreCase)) found = i;
            if (found < 0) { done("no savestate '" + name + "' (savestates lists them)"); return; }

            RestoreFile(LoadFile(_files[found]), load, done);
        }

        // ------------------------------------------------------------------
        // A death's Reload in place (Modules/DeathModule, the author's idea;
        // a "Quick load the slot's save" button did it until v0.24.106):
        // the slot's own save, the data LevelSerializer.Resume would load,
        // restored like a savestate's Quick load - no scene load. Not the
        // game's load, so never in run mode and it marks practice
        // (RestoreInPlace). No file: nothing outside the game's data is put
        // back (held items, Megan, elevators, weather) - the save's own
        // state is what LoadNow gives; every pickup taken since comes back,
        // as a load would bring it. The cave state is sent from the save's
        // own flag (Data/SlotSaveFlags).

        /// Null when the restore started (`done` then gets null, or why it
        /// failed), else why it cannot be done in place.
        public string ReloadSlotInPlace(int slot, Action<string> done)
        {
            RestoreGarbage.Begin();
            InPlaceCheck c = new InPlaceCheck();
            c.RunActive = Ctx.Run.Active;
            c.AtTitle = PlayerRef.AtTitleScreen;
            c.Busy = Busy;
            string error = null;
            string data = c.RunActive || c.AtTitle || c.Busy ? null : _bridge.ReadSlotData(out error);
            c.SlotRead = data != null;
            c.ReadError = error;
            bool inEndgame = false, inCaves = false;
            if (data != null) c.FlagsKnown = _bridge.ReadAreaFlags(data, out inEndgame, out inCaves);
            c.SaveInEndgame = inEndgame;
            c.LiveInEndgame = AreaReport.InEndgame();
            c.EndgameLoaded = EndgameLoader.LabLoaded();

            string refusal = DeathPlan.InPlaceRefusal(c);
            if (refusal != null) return refusal;

            RestoreInPlace(data, _bridge.MemorySafeSaveMode, null, "slot " + slot + "'s save",
                           inCaves ? 1 : 0, null, done);
            return null;
        }

        /// A restore at the title screen deserialized the save into the
        /// menu scene (bridge, v0.24.72: `identifiers 0 -> 105`, no
        /// player); capture already refused there (v0.24.59).
        // Run mode: no capture or restore during a run.
        private bool RefusedInRun(string what, Action<string> done)
        {
            if (!Ctx.Run.Refuse("savestates", "savestate " + what)) return false;
            string why = Ctx.Run.RefusedText("Savestate " + what);
            SetStatus(why);
            if (done != null) done(why);
            return true;
        }

        private bool RefusedAtTitle(string what, Action<string> done)
        {
            if (!PlayerRef.AtTitleScreen) return false;
            string why = what + " unavailable: no player (load a game first)";
            SetStatus(why);
            Ctx.Log.LogInfo("Savestate: " + why + ".");
            if (done != null) done(why);
            return true;
        }

        // The inventory / pause menu stop game time; a Full load is the
        // game's own load, which starts from the game running.
        private void CloseMenuBeforeLoad()
        {
            string menu = MenuClose.IfOpen();
            if (menu.Length > 0) Ctx.Log.LogInfo("Savestate restore with load: " + menu + " first.");
        }

        private void RestoreInPlace(string data, bool unloadStreaming, HashSet<string> presentPickups, string what,
                                    int savedInCave, SavestateFile file, Action<string> after)
        {
            if (_busy) { if (after != null) after("a savestate action is still running"); return; }
            if (RefusedAtTitle("restore", after)) return;
            // The game's own death in progress: its timers and cutscenes
            // carried on through the restore - the menu, the capture, the
            // hanging rope (runner maks, T-0248). Ended first; one that
            // cannot be ended in place gets a Full load, which builds a new
            // player (author, 2026-10-09).
            string death = DeathSequence.End();
            if (DeathSequence.Failed(death) && file != null)
            {
                Ctx.Log.LogWarning("Savestate restore " + what + ": " + death + " - a Full load instead.");
                FullLoad(file, what + " with load", after);
                return;
            }
            if (death.Length > 0) Ctx.Log.LogInfo("Savestate restore " + what + ": " + death + ".");
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
            FullCapacityWatch.RestoreStarted();
            PathfindingWatch.RestoreStarted();
            TodoListKeeper.BeforeRestore(Ctx.Log);
            int cannibalsBefore, familiesBefore;
            _bridge.CountEnemies(out cannibalsBefore, out familiesBefore);

            // Before as well as after: a physics step during the restore
            // could land the old fall at the restored spot.
            string fall = Ctx.Bridge.EndFall();
            // A game menu (F7 closed it already; the bridge's restore and
            // the rest come here open), the book (runner sxczurass), then a
            // swing / action in progress is cut (runner maks).
            string menu = MenuClose.IfOpen();
            if (menu.Length > 0) fall += (fall.Length > 0 ? ", " : "") + menu;
            string book = BookClose.IfOpen();
            if (book.Length > 0) fall += (fall.Length > 0 ? ", " : "") + book;
            // The death count the save does not hold - a load gives 0
            // (runner maks, T-0248: the death after a capture was a real one).
            string deaths = DeathSequence.ForgetDeaths();
            if (deaths.Length > 0) fall += (fall.Length > 0 ? ", " : "") + deaths;
            // The cold is not in the save either - a load gives a warm
            // player (T-0269: frozen since the capture stayed frozen).
            string cold = ColdReset.AsLoad();
            if (cold.Length > 0) fall += (fall.Length > 0 ? ", " : "") + cold;
            string anim = AnimReset.Cancel();
            if (anim.Length > 0) fall += (fall.Length > 0 ? ", " : "") + anim;
            // A blueprint in the hands is outside the save (runner
            // sxczurass: it stayed out); the captured one comes back last.
            string blueprint = BuildMode.PutAway();
            if (blueprint.Length > 0) fall += (fall.Length > 0 ? ", " : "") + blueprint;
            // The climb is outside the save; a free body at a rope spot is
            // thrown out of the rock (runner maks, cave 4). On the rope
            // before the restore, a restore keeps the player on it; its
            // height is set again once the climb idles (HoldHeight, T-0065).
            string rope = RopeClimb.Prepare(file != null ? file.Rope : "",
                                            file != null ? new Vector3(file.X, file.Y, file.Z) : Vector3.zero);
            if (rope.Length > 0) fall += (fall.Length > 0 ? ", " : "") + rope;
            // A cliff climb, sled, glider or zipline in flight holds the body
            // too: ended the game's way before the restore moves it.
            // A glider in the hands goes into the world, so the restore
            // gives back the capture's (its own, if it had one).
            string ride = RideModes.Leave(true);
            if (ride.Length > 0) fall += (fall.Length > 0 ? ", " : "") + ride;

            Transform keep = Ctx.Player.Found ? Ctx.Player.Transform.root : null;
            // Outside a cutscene replay (its hands are the replay's).
            _bridge.KeepHandsIfHeld = file != null && file.CutsceneAt < 0f ? file.Held : null;
            _bridge.BlueprintsAtCapture = file != null ? file.Blueprints : null;
            _bridge.SpawnedThreshold = file != null ? SpawnMark.Threshold(file.Spawned, NatureKeeper.WorldKey()) : SpawnMark.Unknown;
            _bridge.DroppedAtCapture = file != null ? file.Dropped : "";
            RestoreGarbage.Mark("before the restore");
            Ctx.Runner.StartCoroutine(_bridge.RestoreInPlace(data, unloadStreaming, keep, delegate(SavestateBridge.Result r)
            {
                // The steps below share one frame (T-0148: theirs was the
                // first hitch after a restart) - the line says what it took.
                System.Diagnostics.Stopwatch steps = System.Diagnostics.Stopwatch.StartNew();
                int searchesBefore = SceneCache.Searches, keptBefore = SceneCache.Hits;
                _busy = false;
                FullCapacityWatch.RestoreEnded();
                PathfindingWatch.RestoreEnded();

                // The restore can bring the saved body's speed back, and a
                // fall in progress keeps its air time (runner: restoring in
                // mid-air dealt landing damage).
                string after2 = Ctx.Bridge.EndFall();
                if (fall.Length == 0) fall = after2;

                // A capture with the book open saved the hands as stowed;
                // 1.5 s after the load the game reads those slots and hides
                // whatever is held. Its slots get the items the book put
                // away, so the game equips them itself (v0.24.189).
                string slotNote = r.Ok && file != null ? UnstowSavedSlots(file) : "";
                if (slotNote.Length > 0) Ctx.Log.LogInfo("Savestate restore " + what + ": " + slotNote + ".");

                int pickups = 0;
                if (r.Ok)
                {
                    try { pickups = _keeper.Restore(presentPickups); }
                    catch (Exception ex) { Ctx.Log.LogWarning("Savestate: pickup restore failed: " + ex.Message); }
                }
                // T-0418: after the pickups step (put-back copies shown again),
                // each recorded copy's despawn timer as it stood at the capture.
                int timers = PickupKeeper.ResumeTimers(r.Ok);
                RestoreGarbage.Mark("pickups");

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

                RestoreGarbage.Mark("cave state");
                string overlookNote = r.Ok ? AreaReport.LeaveOverlook() : "";
                // The endgame flag is not in the save either: restored from
                // the lab, a cave capture kept it, and the game then keeps
                // every cave's props scene unloaded - Cave 6's body piles,
                // ropes and planks were missing (T-0075). Left the game's
                // way, as walking out does. The other way round the lab's
                // load sets it (EndgameFirst).
                string endgameNote = r.Ok && file != null && CapturedAreas.ShouldLeaveEndgame(file.Areas, AreaReport.InEndgame())
                    ? AreaReport.LeaveEndgame() : "";
                if (endgameNote.Length > 0)
                    overlookNote += (overlookNote.Length > 0 ? ", " : "") + endgameNote + " (captured outside it)";
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
                RestoreGarbage.Mark("enemies");

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
                RestoreGarbage.Mark("panels");

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
                RestoreGarbage.Mark("Megan");

                // The elevators are outside the save too (ElevatorKeeper).
                // A ride under way at capture is replayed once the player
                // is placed (after `after`: a restart's teleport cuts
                // player actions).
                List<Component> rides = new List<Component>();
                string elevatorNote = r.Ok && file != null
                    ? _elevators.Restore(file.Elevators, RideCutsceneAt(file), false, rides) : "";
                RestoreGarbage.Mark("elevators");
                // And the endgame's sliding doors (the car door: open after
                // a ride, it stayed open through the next one).
                string slidingNote = r.Ok && file != null ? _slidingDoors.Restore(file.SlidingDoors) : "";
                RestoreGarbage.Mark("sliding doors");
                // Rain, clouds and fog are not in the save: the live
                // weather stayed (maks's fog after a Quick load).
                string weatherNote = r.Ok && file != null ? _weather.Restore(file.Weather) : "";
                RestoreGarbage.Mark("weather");
                // So is the endgame's active area, which switches the
                // sections' renderers (AreaKeeper).
                string areaNote = r.Ok && file != null ? _area.Restore(file.ActiveArea) : "";
                RestoreGarbage.Mark("area");
                // Fire lit since the capture (FireKeeper, T-0419): before the
                // bushes come back, so a copy never meets a live fire.
                string fireNote = r.Ok ? _fire.Restore(file != null ? file.Bushes : "", file != null ? file.Fires : null) : "";
                RestoreGarbage.Mark("fire");
                // A kept animal's health, hurt skin and poison (T-0425);
                // after the fire put-out, same world and launch only.
                string animalNote = "";
                if (r.Ok && file != null)
                {
                    bool sameWorld = SpawnMark.IsSameWorld(SpawnMark.Threshold(file.Spawned, NatureKeeper.WorldKey()));
                    animalNote = _animals.Restore(file.Animals, file.AnimalPoses, file.AnimalZones, file.AnimalCounts, sameWorld);
                }
                RestoreGarbage.Mark("animals");
                // Trees chopped and bushes cut since are outside what an
                // in-place LoadNow puts back (NatureKeeper); a slot's too.
                string natureNote = r.Ok ? _nature.Restore(file != null ? file.Bushes : "", file != null ? file.CutBushes : null, file != null ? file.Chopped : null) : "";
                RestoreGarbage.Mark("nature");
                // The nature guide's ticks: the save's list is back, the
                // entries are not (NatureGuideKeeper).
                string guideNote = "";
                if (r.Ok)
                {
                    try { guideNote = NatureGuideKeeper.Restore(); }
                    catch (Exception ex) { guideNote = "nature guide: failed (" + ex.Message + ")"; }
                    string todo;
                    try { todo = TodoListKeeper.AfterRestore(); }
                    catch (Exception ex) { todo = "to-do list: failed (" + ex.Message + ")"; }
                    if (todo.Length > 0) guideNote += (guideNote.Length > 0 ? " | " : "") + todo;
                }
                RestoreGarbage.Mark("guide + to-do list");
                // The sticks / rocks around trees come from pool objects
                // that carry their own seed (GreebleKeeper).
                string greebleNote = "";
                if (r.Ok && file != null)
                {
                    try { greebleNote = _greebles.Restore(file.Greebles, true); }
                    catch (Exception ex) { greebleNote = "greebles: failed (" + ex.Message + ")"; }
                    // Stalagmites broken since are whole again, the ones
                    // broken at capture broken (BreakableKeeper).
                    string brokenNote;
                    try { brokenNote = _breakables.Restore(file.Broken, true); }
                    catch (Exception ex) { brokenNote = "stalagmites: failed (" + ex.Message + ")"; }
                    if (brokenNote.Length > 0) greebleNote += (greebleNote.Length > 0 ? " | " : "") + brokenNote;
                }
                RestoreGarbage.Mark("greebles");

                // Saved relative to a parent (a keycard cutscene holds the
                // player under the card reader): the restore put the
                // player at those numbers in the world.
                string placeNote = r.Ok && file != null ? PutPlayerBack(file, false) : "";
                // Toggle crouch outlives the restore (runner sxczurass).
                string stanceNote = r.Ok && file != null ? Stance.Apply(file.Stance) : "";
                if (r.Ok && file != null) LogStore.Apply(file.Logs, "Savestate restore " + what);

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
                // The captured ride after the hands: a cliff climb needs
                // the climbing axe held, a zipline / sled grab stows it.
                string rideBack = r.Ok && file != null ? file.Ride : "";
                if (r.Ok && file != null && file.Held != null && file.Held.Count > 0)
                    Ctx.Runner.StartCoroutine(_bridge.ReEquip(file.Held, NameOfItem,
                        delegate(string note)
                        {
                            Ctx.Log.LogInfo("Savestate restore " + what + ": " + note + ".");
                            PullOutBlueprint(pullOut, "Savestate restore " + what);
                            PutRideBack(file, rideBack, "Savestate restore " + what);
                        }));
                else
                {
                    PullOutBlueprint(pullOut, "Savestate restore " + what);
                    PutRideBack(file, rideBack, "Savestate restore " + what);
                }
                if (r.Ok && file != null) HoldRopeHeight(file, "Savestate restore " + what);

                string line = "restore " + what + " in place: " + r.Message +
                              ", pickups put back " + pickups +
                              (presentPickups == null ? " (all kept)" : "") +
                              (timers > 0 ? ", despawn timers set as at the capture " + timers : "") +
                              (string.IsNullOrEmpty(cave) ? "" : " | cave: " + cave) +
                              (fall.Length == 0 ? "" : " | " + fall) +
                              (placeNote.Length == 0 ? "" : " | " + placeNote) +
                              (stanceNote.Length == 0 ? "" : " | " + stanceNote) +
                              (overlookNote.Length == 0 ? "" : " | " + overlookNote) +
                              (bookNote.Length == 0 ? "" : " | " + bookNote) +
                              (washNote.Length == 0 ? "" : " | " + washNote) +
                              (panelNote.Length == 0 ? "" : " | " + panelNote) +
                              (meganNote.Length == 0 ? "" : " | " + meganNote) +
                              (elevatorNote.Length == 0 ? "" : " | " + elevatorNote) +
                              (slidingNote.Length == 0 ? "" : " | " + slidingNote) +
                              (weatherNote.Length == 0 ? "" : " | " + weatherNote) +
                              (areaNote.Length == 0 ? "" : " | " + areaNote) +
                              (fireNote.Length == 0 ? "" : " | " + fireNote) +
                              (animalNote.Length == 0 ? "" : " | " + animalNote) +
                              (natureNote.Length == 0 ? "" : " | " + natureNote) +
                              (guideNote.Length == 0 ? "" : " | " + guideNote) +
                              (greebleNote.Length == 0 ? "" : " | " + greebleNote) +
                              (enemyNote.Length == 0 ? "" : " | " + enemyNote) +
                              " | after the load: " + steps.ElapsedMilliseconds + " ms, " +
                              (SceneCache.Searches - searchesBefore) + " scene search(es), " + (SceneCache.Hits - keptBefore) + " kept";
                RestoreGarbage.Mark("the rest");
                string garbage = RestoreGarbage.Describe();
                if (garbage.Length > 0) line += " | " + garbage;
                if (r.Ok) Ctx.Log.LogInfo("Savestate " + line);
                else Ctx.Log.LogWarning("Savestate " + line);
                if (r.Ok) Ctx.Runner.StartCoroutine(LogAreas(file));
                if (r.Ok) Ctx.Runner.StartCoroutine(SyncSun("restore " + what));
                if (r.Ok) Ctx.Runner.StartCoroutine(AfterInPlace(what, _respawnEnemies.Value, familiesBefore, file));
                // A file from another world: the scene load re-created every
                // placed pickup taken before the capture, as in a Full load (T-0066).
                SavestateFile otherWorld = r.Ok && file != null && file.Pickups != null && file.Pickups.Count > 0 &&
                                           !LitFires.SameWorld(file.Bushes, NatureKeeper.WorldKey()) ? file : null;
                if (otherWorld != null) Ctx.Runner.StartCoroutine(RemoveLatePickups(otherWorld));
                if (r.Ok && presentPickups != null) Ctx.Runner.StartCoroutine(LogNewPickups(presentPickups, what, otherWorld));
                // Our streaming force-unload re-creates the story spots' pickups (T-0066).
                if (r.Ok && file != null && unloadStreaming)
                    Ctx.Runner.StartCoroutine(StoryPickupsAfterReload(file.Pickups, file.Areas, "restore " + what));
                // Saplings cut again from another world drop their sticks again
                // (NatureKeeper.ApplyCuts): the ones taken before the capture go
                // once the new ones have settled.
                int droppedSticks = _nature.TakeDropped();   // read on every restore, so it never carries over
                if (r.Ok && presentPickups != null && droppedSticks > 0)
                    Ctx.Runner.StartCoroutine(StickPassLater(presentPickups, "restore " + what));
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

        private int _fastForwards;
        /// A cutscene fast-forward is running: the plugin itself is driving Time.timeScale.
        public bool FastForwarding { get { return _fastForwards > 0; } }

        private IEnumerator FastForwardCutscene(SavestateFile f, int startsBefore, string what)
        {
            _fastForwards++;
            try { yield return Ctx.Runner.StartCoroutine(FastForwardCutsceneRun(f, startsBefore, what)); }
            finally { _fastForwards--; }
        }

        private IEnumerator FastForwardCutsceneRun(SavestateFile f, int startsBefore, string what)
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
            RestoreGarbage.Start garbage = RestoreGarbage.Current();
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
                Ctx.Log.LogInfo("Savestate after restoring " + what + " in place: " + plane + (cave != null ? " | " + cave : "") + Since(garbage) + ".");
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
                Ctx.Log.LogInfo("Savestate after restoring " + what + " in place: " + plane + " | " + rebuilt + Since(garbage) + ".");
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
                            (positions.Length > 0 ? " | " + positions : "") + Since(garbage) + ".");
        }

        private static string Since(RestoreGarbage.Start start)
        {
            string g = RestoreGarbage.Since(start);
            return g.Length > 0 ? " | " + g : "";
        }

        private static string DescribeWeather(string value)
        {
            WeatherState s;
            return WeatherState.TryParse(value, out s) ? s.Describe() : "?";
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
            if (f == null || f.Areas.Length == 0) Ctx.Log.LogInfo("Savestate areas after the restore: " + now);
            else Ctx.Log.LogInfo("Savestate areas after the restore: " +
                                 (now == f.Areas ? "same as at capture (" + now + ")" : now + " || at capture: " + f.Areas));
            if (OnRestoreSettled != null) OnRestoreSettled(f == null || f.Areas.Length == 0 || now == f.Areas);
        }

        // World pickups there now that the capture did not list (keyed by
        // item and position). Diagnostic for what an in-place restore leaves
        // behind - severed limbs (author, v0.24.7), and greeble sticks and
        // rocks that came back elsewhere (fix list 4). The removals 0.5 s
        // after the restore, when the bodies it cleared are gone; the
        // listing once the plane wreck has settled.
        private IEnumerator LogNewPickups(HashSet<string> present, string what, SavestateFile otherWorld = null)
        {
            yield return new WaitForSecondsRealtime(0.5f);

            if (otherWorld != null)
            {
                RemoveTakenPickups(otherWorld, false, "Savestate restore " + what);
                yield return null;   // let Destroy land before the listing below
            }

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

        // Our streaming force-unload (capture and Quick load) makes the game
        // load MainSceneWorldStorySpots again, and a reload re-creates its
        // placed pickups: ones taken before are back (T-0066, the film crew
        // tape). Once it is loaded again, placed pickups there the list does
        // not account for go. Placed only: clones (greeble rocks, zone
        // sticks) re-roll and are not ours.
        private const string StorySpots = "MainSceneWorldStorySpots";

        private IEnumerator StoryPickupsAfterReload(List<string> list, string areas, string prefix)
        {
            if (list == null || list.Count == 0 || string.IsNullOrEmpty(areas)) yield break;
            if (!ScenesIn(areas).Contains(StorySpots)) yield break;
            yield return new WaitForSecondsRealtime(1f);
            float from = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - from < 10f)
            {
                bool loaded = false;
                try { loaded = UnityEngine.SceneManagement.SceneManager.GetSceneByName(StorySpots).isLoaded; }
                catch (Exception) { }
                if (loaded) break;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(1f);   // its pickups switch on after the load
            try
            {
                HashSet<string> scenes = new HashSet<string>();
                scenes.Add(StorySpots);
                Dictionary<int, int> counts = new Dictionary<int, int>();
                int[] skipped = new int[3];
                int n = _keeper.RemoveTakenAfterLoad(new HashSet<string>(list), scenes, counts, skipped, Vector3.zero, true);
                if (n == 0) yield break;
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<int, int> kv in counts)
                    sb.Append(sb.Length == 0 ? "" : ", ").Append(NameOfItem(kv.Key)).Append(" x").Append(kv.Value);
                Ctx.Log.LogInfo("Savestate " + prefix + ": removed " + n + " story-spot pickup(s) the scene reload brought back (taken before the capture: " + sb + ").");
            }
            catch (Exception ex) { Ctx.Log.LogWarning("Savestate: removing story-spot pickups failed: " + ex.Message); }
        }

        // The sticks a re-cut sapling dropped fall 3.3 m first; once they lie,
        // the ones taken before the capture are removed (T-0067). `prefix`:
        // "restore <what>" or "after the load" (log: Savestate restore, Savestate after the load).
        private IEnumerator StickPassLater(HashSet<string> present, string prefix)
        {
            yield return new WaitForSecondsRealtime(3.5f);
            int sticks = 0;
            try { sticks = _keeper.RemoveExtra(present, IsStickItem, NatureKeeper.UnderACut, false); }
            catch (Exception ex) { Ctx.Log.LogWarning("Savestate: removing sapling sticks failed: " + ex.Message); }
            Ctx.Log.LogInfo("Savestate " + prefix + ": removed " + sticks +
                            " sapling stick(s) taken before the capture (saplings cut again).");
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

        // T-0411: the engine's next instance id, as the "made since" mark.
        private static string CaptureSpawnMark()
        {
            try
            {
                string world = NatureKeeper.WorldKey();
                if (world.Length == 0) return "";
                GameObject probe = new GameObject("ForestOverlay spawn mark");
                int id = probe.GetInstanceID();
                UnityEngine.Object.DestroyImmediate(probe);
                return SpawnMark.Format(world, id);
            }
            catch (Exception) { return ""; }
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
                    string stance = Stance.Apply(f.Stance);
                    LogStore.Apply(f.Logs, "Savestate after the load");
                    Ctx.Log.LogInfo("Savestate after the load: " + _book.Apply(f.Book) +
                                    (panels.Length > 0 ? " | " + panels : "") +
                                    (stance.Length > 0 ? " | " + stance : "") +
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
            // The endgame's scenes only when the capture had the lab
            // (Data/CapturedAreas): a lingering endgame_animPrefabs is not
            // loaded again and would hold the player the full 30 s.
            HashSet<string> needed = CapturedAreas.ScenesToWaitFor(f.Areas);
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
            // A load rebuilds the player off any rope (RopeClimb); after
            // the pin, which undid a climb entered before it (bridge).
            string rope = f.Rope.Length > 0 ? RopeClimb.Prepare(f.Rope, at) : "";
            HoldRopeHeight(f, "Savestate after the load");
            PutRideBack(f, f.Ride, "Savestate after the load");
            Ctx.Log.LogInfo("Savestate after the load: held the player at the captured spot for " +
                            (Time.realtimeSinceStartup - start).ToString("F1") + " s" +
                            (missing.Length > 0 ? " - gave up waiting for " + missing : " until the captured scenes were loaded") +
                            (rope.Length > 0 ? " | " + rope : "") + ".");
            RemoveTakenPickups(f, false);
            Ctx.Runner.StartCoroutine(RemoveLatePickups(f));
            // A load regrows every bush; the ones cut at capture go again.
            string again = f != null ? _nature.ApplyCuts(f.CutBushes) : "";
            if (again.Length > 0) Ctx.Log.LogInfo("Savestate after the load: " + again + ".");
            int droppedSticks = _nature.TakeDropped();
            if (f != null && f.Pickups != null && droppedSticks > 0)
                Ctx.Runner.StartCoroutine(StickPassLater(new HashSet<string>(f.Pickups), "after the load"));
            // Trees half-chopped at capture are chopped again once their view is up.
            try
            {
                string chopped = _nature.ChopAgain(f != null ? f.Chopped : null);
                if (chopped.Length > 0) Ctx.Log.LogInfo("Savestate after the load: " + chopped + ".");
            }
            catch (Exception ex) { Ctx.Log.LogWarning("Savestate after the load: chopping trees again failed: " + ex.Message); }
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
            // Spots broken at capture broke as their zones spawned in the
            // load; the live pass breaks any standing whole since.
            if (f.Broken != null)
            {
                int late = BreakableKeeper.Late;
                string stalagmites;
                try { stalagmites = _breakables.Restore(f.Broken, true); }
                catch (Exception ex) { stalagmites = "stalagmites: failed (" + ex.Message + ")"; }
                Ctx.Log.LogInfo("Savestate after the load: " + (stalagmites.Length > 0 ? stalagmites : "stalagmites: nothing to do") + " (" + late + " broken as they spawned in the load).");
            }
            // A load builds the weather afresh (clear); the capture's back.
            string weatherNote = _weather.Restore(f.Weather);
            if (weatherNote.Length > 0) Ctx.Log.LogInfo("Savestate after the load: " + weatherNote + ".");
            Ctx.Runner.StartCoroutine(SyncSun("after the load"));
            FullCapacityWatch.RestoreEnded();
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
        /// The restored inventory's saved hand slots, when they hold no item
        /// but the file says what was held: the "previously equipped" slots
        /// the book put away. "" when nothing was changed.
        private string UnstowSavedSlots(SavestateFile file)
        {
            if (file.Held == null || file.Held.Count == 0 || file.HeldBefore == null || file.HeldBefore.Count == 0) return "";
            try
            {
                object inv = GameBridge.ReadStaticField("TheForest.Utils.LocalPlayer", "Inventory");
                if (inv == null) return "";
                System.Reflection.FieldInfo f = inv.GetType().GetField("_equipmentSlotsIds",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                int[] slots = f != null ? f.GetValue(inv) as int[] : null;
                if (slots == null) return "";
                for (int i = 0; i < slots.Length; i++) if (slots[i] > 0) return "";   // the save has the hands

                int set = 0;
                for (int i = 0; i < file.HeldBefore.Count; i++)
                {
                    string e = file.HeldBefore[i];
                    int colon = e.IndexOf(':'), slot, id;
                    if (colon <= 0 || !int.TryParse(e.Substring(0, colon), out slot) || !int.TryParse(e.Substring(colon + 1), out id)) continue;
                    if (slot < 0 || slot >= slots.Length || id <= 0 || !file.Held.Contains(id)) continue;
                    slots[slot] = id;
                    set++;
                }
                return set == 0 ? "" : "the save had the hands stowed (book open at capture) - " + set + " slot(s) given back their items for the game's re-equip";
            }
            catch (Exception ex) { return "giving the stowed hands back failed: " + ex.Message; }
        }

        private void PullOutBlueprint(string type, string prefix)
        {
            if (string.IsNullOrEmpty(type)) return;
            Ctx.Runner.StartCoroutine(PullOutLater(type, prefix));
        }

        /// The capture's zipline / sled / glider / cliff climb, put back
        /// (RideModes); its own log line when it is done.
        private void PutRideBack(SavestateFile f, string ride, string prefix)
        {
            if (string.IsNullOrEmpty(ride)) return;
            Ctx.Runner.StartCoroutine(RideModes.PutBack(ride, new Vector3(f.X, f.Y, f.Z), delegate(string note)
            {
                if (note.Length > 0) Ctx.Log.LogInfo(prefix + ": " + note + ".");   // log: Savestate restore, Savestate after the load
            }));
        }

        /// The captured rope climb's height, set again once the grab is
        /// over (RopeClimb.HoldHeight); its own log line.
        private void HoldRopeHeight(SavestateFile f, string prefix)
        {
            if (string.IsNullOrEmpty(f.Rope) || (f.X == 0f && f.Y == 0f && f.Z == 0f)) return;
            Ctx.Runner.StartCoroutine(RopeClimb.HoldHeight(f.Rope, new Vector3(f.X, f.Y, f.Z), delegate(string note)
            {
                if (note.Length > 0) Ctx.Log.LogInfo(prefix + ": " + note + ".");   // log: Savestate restore, Savestate after the load
            }));
        }

        private IEnumerator PullOutLater(string type, string prefix)
        {
            yield return new WaitForSecondsRealtime(0.3f);
            Ctx.Log.LogInfo(prefix + ": " + BuildMode.PullOut(type) + ".");   // log: Savestate restore, Savestate after the load
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

        private void RemoveTakenPickups(SavestateFile f, bool late, string restorePrefix = null)
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
                    if (!late && restorePrefix == null) Ctx.Log.LogInfo("Savestate after the load: pickups - none back that were taken before the capture." + kept);
                    return;
                }
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<int, int> kv in counts)
                    sb.Append(sb.Length == 0 ? "" : ", ").Append(NameOfItem(kv.Key)).Append(" x").Append(kv.Value);
                string head = restorePrefix != null ? restorePrefix + ": pickups - removed " : "Savestate after the load: pickups - removed ";
                Ctx.Log.LogInfo(head + n + (late ? " that appeared late" : "") +   // log: Savestate restore, Savestate after the load
                                " the scene load since the capture brought back (taken before the capture: " + sb + ")." + (late ? "" : kept));
            }
            catch (Exception ex) { Ctx.Log.LogWarning("Savestate after the load: removing taken pickups failed: " + ex.Message); }
        }

        /// The loaded scenes in an area line ("... | scenes: a, b (loading), c | ...").
        private static HashSet<string> ScenesIn(string areas)
        {
            return CapturedAreas.Scenes(areas, false);
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
            FullCapacityWatch.RestoreStarted();
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

        // ------------------------------------------------------------------
        // Checkpoint states (Data/CheckpointStates): one Quick-load state
        // per checkpoint of a timed segment, captured as a practice run
        // fires it (Modules/PracticeRunModule.Checkpoints) - newest wins.

        private sealed class CaptureOptions
        {
            /// No memory clean-up frames (SavestateBridge.Capture light).
            public bool Light;
            /// The file's text built and written on a worker thread.
            public bool Background;
            /// Written after the savestate, by the same worker; built on the
            /// main thread from the capture's result.
            public string MetaPath;
            public Func<SavestateBridge.Result, string> Meta;
            /// For the one log line.
            public string What = "";
            /// The keepers' snapshots before the serialize, ms (same frame),
            /// and the ones of a ms or more ("pickups 12, panels 80").
            internal long PreMs;
            internal string PreParts = "";
            internal bool Queued;
            internal Action<string> After;
        }

        private sealed class PendingWrite
        {
            public string Path, MetaPath, MetaText, Error;
            public SavestateFile File;
            public CaptureOptions Opts;
            public long SerializeMs, FramesMs, WriteMs, PreMs;
            public int Chars;
            /// A lean capture's level bytes: the worker builds File.Data.
            public byte[] Bytes;
            public string Parts = "";
            public int Gcs;
            public bool Lean;
            public string PreParts = "";
        }

        // Paths a worker is writing (main thread only), and the finished
        // writes for DrainWrites (shared with the workers: locked).
        private readonly HashSet<string> _writing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<PendingWrite> _written = new List<PendingWrite>();

        private string SegmentsDir { get { return Path.Combine(_dir, "segments"); } }

        public string CheckpointStatePath(Segment s, int index)
        {
            return Path.Combine(SegmentsDir, CheckpointFiles.BaseName(SavestateFile.SafeFileName(s.Id), index) + SavestateFile.Extension);
        }

        private string CheckpointMetaPath(Segment s, int index)
        {
            return Path.Combine(SegmentsDir, CheckpointFiles.BaseName(SavestateFile.SafeFileName(s.Id), index) + CheckpointState.Extension);
        }

        /// The checkpoints (0-based) with a complete state on disk, not
        /// being written now. A folder listing: call on a change, not per
        /// frame.
        public List<int> CheckpointStatesOf(Segment s)
        {
            List<int> none = new List<int>();
            if (s == null || string.IsNullOrEmpty(s.Id)) return none;
            try
            {
                if (!Directory.Exists(SegmentsDir)) return none;
                string safe = SavestateFile.SafeFileName(s.Id);
                string[] paths = Directory.GetFiles(SegmentsDir, safe + ".cp*");
                List<string> names = new List<string>(paths.Length);
                for (int i = 0; i < paths.Length; i++)
                    if (!_writing.Contains(paths[i])) names.Add(Path.GetFileName(paths[i]));
                return CheckpointFiles.Complete(names, safe, SavestateFile.Extension);
            }
            catch (Exception ex)
            {
                Ctx.Log.LogWarning("Checkpoint states of '" + s.Id + "': could not list them: " + ex.Message);
                return none;
            }
        }

        /// The checkpoint's meta, or null and why.
        public CheckpointState ReadCheckpointState(Segment s, int index, out string error)
        {
            try
            {
                string path = CheckpointMetaPath(s, index);
                if (_writing.Contains(CheckpointStatePath(s, index))) { error = "still being written"; return null; }
                if (!File.Exists(path)) { error = "no state captured"; return null; }
                CheckpointState st = CheckpointState.Parse(File.ReadAllText(path, Encoding.UTF8), out error);
                if (st != null && st.Index != index) { error = "the file is for checkpoint " + (st.Index + 1); return null; }
                return st;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        /// Captures checkpoint `index`'s state for `s` without disturbing the
        /// run more than it must: never in run mode, skipped with a menu
        /// open or another savestate action running, no memory clean-up,
        /// and the file written on a worker thread. `meta` builds the
        /// sidecar from the capture's result (main thread). `done` gets null
        /// once both files are on disk, or why not - on the main thread.
        public void CaptureCheckpointState(Segment s, int index, Func<SavestateBridge.Result, string> meta, Action<string> done)
        {
            // Run mode never captures (the caller checks too): quietly.
            if (Ctx.Run.Active) { done("run mode is on"); return; }
            if (Busy) { done("another savestate action is still running"); return; }
            string path = CheckpointStatePath(s, index);
            if (_writing.Contains(path)) { done("its last capture is still being written"); return; }

            CaptureOptions opts = new CaptureOptions();
            opts.Light = true;
            opts.Background = true;
            opts.MetaPath = CheckpointMetaPath(s, index);
            opts.Meta = meta;
            opts.What = "checkpoint " + (index + 1) + "/" + s.Checkpoints.Count + " of '" + s.Id + "'";
            CaptureTo("checkpoint " + (index + 1) + " of " + s.Name + " (" + s.Id + ")", path, opts, done);
        }

        public int DeleteCheckpointStates(Segment s)
        {
            int n = 0;
            if (s == null) return 0;
            try
            {
                if (!Directory.Exists(SegmentsDir)) return 0;
                string safe = SavestateFile.SafeFileName(s.Id);
                string[] paths = Directory.GetFiles(SegmentsDir, safe + ".cp*");
                for (int i = 0; i < paths.Length; i++)
                {
                    string name = Path.GetFileName(paths[i]);
                    if (_writing.Contains(paths[i])) continue;
                    if (CheckpointFiles.IndexOf(name, safe, SavestateFile.Extension) < 0 &&
                        CheckpointFiles.IndexOf(name, safe, CheckpointState.Extension) < 0) continue;
                    File.Delete(paths[i]);
                    n++;
                }
                Ctx.Log.LogInfo("Checkpoint states of '" + s.Id + "': " + n + " file(s) deleted.");
            }
            catch (Exception ex) { Ctx.Log.LogWarning("Checkpoint states of '" + s.Id + "': delete failed: " + ex.Message); }
            return n;
        }

        /// Restores checkpoint `index`'s state: a Quick load, or a Full load
        /// where a Quick load cannot (MustLoad). `done` as RestoreStartState.
        public void RestoreCheckpointState(Segment s, int index, Action<string> done)
        {
            if (Busy) { done("a savestate action is still running"); return; }
            if (RefusedInRun("restore", done)) return;
            string path = CheckpointStatePath(s, index);
            if (_writing.Contains(path)) { done("its capture is still being written"); return; }

            SavestateFile f;
            try
            {
                string error;
                f = ReadState(path, out error);
                if (f == null) { done("checkpoint state unreadable: " + error); return; }
            }
            catch (Exception ex) { done("checkpoint state unreadable: " + ex.Message); return; }

            string what = "checkpoint " + (index + 1) + " of '" + s.Name + "'";
            if (MustLoad(f, what))
            {
                Ctx.Practice.Mark("savestate restore (load)");
                PickupKeeper.Armed = true;
                CloseMenuBeforeLoad();
                _greebles.Restore(f.Greebles, false);
                _breakables.Restore(f.Broken, false);
                StartLoad(what + " with load", _bridge.RestoreWithLoad(f.Data, f.Difficulty, f.BaseDifficulty), AfterLoad(f, done));
                return;
            }
            HashSet<string> present = f.Pickups != null ? new HashSet<string>(f.Pickups) : null;
            RestoreInPlace(f.Data, f.StreamingUnloaded, present, what, f.InCave ? 1 : 0, f, done);
        }

        // The worker: the old meta goes first, so a write cut short (the
        // game closed) leaves a state with no meta - never offered - rather
        // than a new meta beside an old state.
        private void QueueWrite(SavestateFile f, string path, CaptureOptions opts, SavestateBridge.Result r)
        {
            PendingWrite w = new PendingWrite();
            w.Path = path;
            w.MetaPath = opts.MetaPath;
            w.File = f;
            w.Opts = opts;
            w.SerializeMs = r.SerializeMs;
            w.FramesMs = r.TotalMs;
            w.Chars = SavestateBridge.DataChars(r);
            w.Bytes = r.Bytes;
            w.Lean = r.Bytes != null;
            w.Parts = r.SerializeParts;
            w.Gcs = r.SerializeGcs;
            w.PreMs = opts.PreMs;
            w.PreParts = opts.PreParts;
            try { w.MetaText = opts.Meta != null ? opts.Meta(r) : null; }
            catch (Exception ex) { w.Error = "could not build its meta: " + ex.Message; }
            opts.Queued = true;
            _writing.Add(path);

            if (w.Error != null) { lock (_written) _written.Add(w); return; }
            ThreadPool.QueueUserWorkItem(WriteOnWorker, w);
        }

        private void WriteOnWorker(object state)
        {
            PendingWrite w = (PendingWrite)state;
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (w.MetaPath != null && File.Exists(w.MetaPath)) File.Delete(w.MetaPath);
                if (w.Bytes != null) { w.File.Data = SavestateBridge.DataText(w.Bytes); w.Bytes = null; }
                string tmp = w.Path + ".tmp";
                File.WriteAllText(tmp, w.File.Write(), new UTF8Encoding(false));
                if (File.Exists(w.Path)) File.Delete(w.Path);
                File.Move(tmp, w.Path);
                if (w.MetaPath != null && w.MetaText != null) File.WriteAllText(w.MetaPath, w.MetaText, new UTF8Encoding(false));
            }
            catch (Exception ex) { w.Error = "could not write the file: " + ex.Message; }
            sw.Stop();
            w.WriteMs = sw.ElapsedMilliseconds;
            lock (_written) _written.Add(w);
        }

        // Main thread: one log line per capture with its cost, then the
        // caller's continuation.
        private void DrainWrites()
        {
            if (_writing.Count == 0) return;
            PendingWrite[] done;
            lock (_written)
            {
                if (_written.Count == 0) return;
                done = _written.ToArray();
                _written.Clear();
            }
            for (int i = 0; i < done.Length; i++)
            {
                PendingWrite w = done[i];
                _writing.Remove(w.Path);
                if (w.Error == null)
                    Ctx.Log.LogInfo("Checkpoint state " + w.Opts.What + " captured: " + (w.PreMs + w.FramesMs) + " ms of frame work on the main thread (" +
                                    "snapshots " + w.PreMs + " ms" + (w.PreParts.Length > 0 ? " (" + w.PreParts + ")" : "") + ", serialize " + w.SerializeMs + " ms" +
                                    (w.Parts.Length > 0 ? " - " + w.Parts : "") + (w.Gcs > 0 ? ", GC x" + w.Gcs : "") +
                                    (w.Lean ? ", no memory clean-up, no asset sweep" : ", no memory clean-up") + "), " +
                                    SavestateBridge.Kb(w.Chars) + " written in " +
                                    w.WriteMs + " ms on a worker thread -> " + Path.GetFileName(w.Path) + ".");
                else
                    Ctx.Log.LogWarning("Checkpoint state " + w.Opts.What + " not saved: " + w.Error + ".");
                Action<string> after = w.Opts.After;
                w.Opts.After = null;
                if (after == null) continue;
                try { after(w.Error); }
                catch (Exception ex) { Ctx.Log.LogWarning("Checkpoint state: continuation failed: " + ex.Message); }
            }
        }

        /// Restores the segment's start state the segment's way; `done` gets
        /// null once the game is back in play (after the load, for a load),
        /// or the reason it could not.
        public void RestoreStartState(Segment s, Action<string> done)
        {
            RestoreStartState(s, false, done);
        }

        /// `runStart`: a run spot's restart (Modules/RunModeModule) - allowed
        /// in run mode, and always a Full load (author, 2026-10-02).
        public void RestoreStartState(Segment s, bool runStart, Action<string> done)
        {
            if (Busy) { done("a savestate action is still running"); return; }
            if (!runStart && RefusedInRun("restore", done)) return;

            RestoreGarbage.Begin();
            SavestateFile f;
            try
            {
                string error;
                f = ReadState(StartStatePath(s), out error);
                if (f == null) { done("start state unreadable: " + error); return; }
            }
            catch (Exception ex) { done("start state unreadable: " + ex.Message); return; }
            RestoreGarbage.Mark("file read");

            // A shared segment names the state it was timed from; a file
            // that is not that state still restores, but its times will not
            // compare fairly - say so.
            if (s.StartState.Length > 0 && Segment.HashText(f.Data) != s.StartState)
                Ctx.Log.LogWarning("Savestate: the start state file of '" + s.Id + "' is not the one the segment expects (" +
                                   s.StartState + ") - recapture it to make it so.");
            RestoreGarbage.Mark("start state hash");

            _startFile = f;
            _startFileFor = s.Id;
            string what = "start state of '" + s.Name + "'";
            if (runStart) Ctx.Log.LogInfo("Savestate: " + what + " starts a run - a Full load.");
            if (runStart || s.StartRestoreWithLoad || MustLoad(f, what))
            {
                Ctx.Practice.Mark("savestate restore (load)");
                PickupKeeper.Armed = true;
                CloseMenuBeforeLoad();
                _greebles.Restore(f.Greebles, false);
                _breakables.Restore(f.Broken, false);
                string err = _bridge.RestoreWithLoad(f.Data, f.Difficulty, f.BaseDifficulty);
                // A run starts as a fresh game of the capture's mode: no
                // cheat a console / bridge left on (statics outlive loads).
                if (runStart && err == null)
                {
                    string cheats = _bridge.ResetModeCheats();
                    if (cheats.Length > 0) Ctx.Log.LogInfo("Savestate: " + what + ": " + cheats + ".");
                }
                StartLoad(what + " with load", err, AfterLoad(f, done));
            }
            else
            {
                HashSet<string> present = f.Pickups != null ? new HashSet<string>(f.Pickups) : null;
                RestoreInPlace(f.Data, f.StreamingUnloaded, present, what, f.InCave ? 1 : 0, f, done);
            }
        }

        /// Keep loaded (T-0212): right after the segment's start state was
        /// restored - the player's state as the restore left it. "" or why not.
        public string TakePlayer() { return _player.Take(); }

        /// A Keep loaded restart without the restore (T-0212; author,
        /// 2026-10-10: "Player + endgame movers"): the endgame elevators,
        /// sliding doors and active area from the start state file, the
        /// player's state as the last restore left it, the held items and
        /// stored logs from the file. The caller teleports after. Returns
        /// null and the note, or why it cannot (then the caller restores).
        public string RestartKept(Segment s, out string note)
        {
            note = "";
            if (_busy) return "a savestate action is still running";
            if (RefusedInRun("restore", null)) return "run mode";
            SavestateFile f = _startFileFor == s.Id ? _startFile : null;
            if (f == null) return "its start state file is not read yet";
            // A capture inside a cutscene (a red elevator ride, a keycard
            // door) is replayed by the restore; a teleport cannot.
            if (f.CutsceneAt >= 0f) return "its start state was captured in a cutscene";
            if (!_player.Has) return "the player's state was not taken";

            Ctx.Practice.Mark("keep loaded restart");
            string fall = Ctx.Bridge.EndFall();
            string menu = BookClose.IfOpen();
            if (menu.Length > 0) fall += (fall.Length > 0 ? ", " : "") + menu;
            string anim = AnimReset.Cancel();
            if (anim.Length > 0) fall += (fall.Length > 0 ? ", " : "") + anim;
            string ride = RideModes.Leave(true);
            if (ride.Length > 0) fall += (fall.Length > 0 ? ", " : "") + ride;

            string elevators = _elevators.Restore(f.Elevators, -1f, false, new List<Component>());
            string doors = _slidingDoors.Restore(f.SlidingDoors);
            string area = _area.Restore(f.ActiveArea);
            string player = _player.Restore();
            LogStore.Apply(f.Logs, "Restart '" + s.Id + "' (kept)");
            if (f.Held != null && f.Held.Count > 0)
                Ctx.Runner.StartCoroutine(_bridge.ReEquip(f.Held, NameOfItem, delegate(string held)
                {
                    Ctx.Log.LogInfo("Restart '" + s.Id + "' (kept): " + held + ".");
                }));

            note = player +
                   (elevators.Length > 0 ? " | " + elevators : "") +
                   (doors.Length > 0 ? " | " + doors : "") +
                   (area.Length > 0 ? " | " + area : "") +
                   (fall.Length > 0 ? " | " + fall : "");
            return null;
        }

        // A Quick load needs a game to restore into, and the game's mode:
        // Creative and the difficulty are set up as the game loads and are
        // not in the save. So from the title screen, or into another mode
        // (Creative <-> survival, author 2026-10-02) or difficulty (Normal /
        // Hard / Peaceful ...: a Quick load kept the live game's enemies and
        // settings - bridge, 2026-10-04), the restore is a Full load,
        // whatever the setting - the load switches them.
        private bool MustLoad(SavestateFile f, string what)
        {
            string why = null;
            if (PlayerRef.AtTitleScreen) why = "from the title screen";
            else if (_bridge.Resolve())
                why = f.ModeMismatch(_bridge.CurrentDifficulty, _bridge.CurrentBaseDifficulty);
            if (why == null) return false;
            Ctx.Log.LogInfo("Savestate: " + what + " restores with a Full load - " + why + ".");
            return true;
        }

        // ------------------------------------------------------------------
        // The last state file read, kept parsed (T-0202): a restart loop on
        // one spot read and parsed the same ~0.3 MB file every time - ~3.6 MB
        // of garbage a restore (the text, the line split, the data line's
        // copies). Kept by path, size and write time, so a capture over it
        // (or any write) reads the new file; each caller gets its own Copy()
        // (no list shared between restores). Data/KeptRead decides; behind
        // the `[Performance]` switch RestoreKeepLastRead (PerfPatches 17).
        private readonly KeptRead _kept = new KeptRead();
        private SavestateFile _keptFile;

        private SavestateFile ReadState(string path, out string error)
        {
            error = null;
            FileInfo fi = new FileInfo(path);
            long length = fi.Length;   // throws for a missing file, as ReadAllText did
            long ticks = fi.LastWriteTimeUtc.Ticks;
            bool keep = PerfPatches.RestoreKeepLastRead;
            if (keep && _keptFile != null && _kept.Matches(path, length, ticks))
                return _keptFile.Copy();

            _keptFile = null;
            _kept.Forget();
            SavestateFile f = SavestateFile.Parse(File.ReadAllText(path, Encoding.UTF8), out error);
            if (f == null || !keep) return f;
            _kept.Remember(path, length, ticks);
            _keptFile = f;
            return f.Copy();
        }

        private SavestateFile LoadFile(string path)
        {
            try
            {
                string error;
                SavestateFile f = ReadState(path, out error);
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
                        _fileLabels.Add(Path.GetFileNameWithoutExtension(found[i]) + "   (" +
                            SavestateBridge.Kb((int)Math.Min(fi.Length, int.MaxValue)) + ", " +
                            fi.LastWriteTime.ToString("MM-dd HH:mm") + ")");
                    }
                }
            }
            catch (Exception ex) { SetStatus("could not list savestates: " + ex.Message); }
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
            _status = s;
        }

        // ------------------------------------------------------------------
        // Drawn by the Settings tab's Loads and savestates fold (the
        // Savestates tab is gone since v0.24.106; T-0226 moved these out of
        // Debug views).
        public float DrawOptions(float x, float y, float w)
        {
            bool respawn = UiKit.Toggle(new Rect(x, y, w, 22), _respawnEnemies.Value,
                                      " Savestates: put the captured enemies back after a restore (respawn, clear bodies)");
            if (respawn != _respawnEnemies.Value) _respawnEnemies.Value = respawn;
            y += 26f;

            // The load leak: what each load leaves behind.
            bool fix = UiKit.Toggle(new Rect(x, y, w, 22), _threadsFix.Value,
                                  " Fix: stop the worker threads the game leaves running after a load");
            if (fix != _threadsFix.Value) { _threadsFix.Value = fix; LeakedThreads.Enabled = fix; }
            y += 26f;
            bool subs = UiKit.Toggle(new Rect(x, y, w, 22), _subscribersFix.Value,
                                   " Fix: drop the old world's event subscriptions after a load (the game keeps them)");
            if (subs != _subscribersFix.Value) { _subscribersFix.Value = subs; StaleSubscribers.Enabled = subs; }
            y += 26f;
            bool pool = UiKit.Toggle(new Rect(x, y, w, 22), _pathPoolFix.Value,
                                   " Fix: empty the old pathfinding paths when a world starts (they keep its navmesh)");
            if (pool != _pathPoolFix.Value) { _pathPoolFix.Value = pool; PathPoolCleanup.Enabled = pool; }
            y += 26f;
            return y;
        }

        // Drawn by the Developer tab (T-0226): the memory census.
        public float DrawCensus(float x, float y, float w)
        {
            bool census = UiKit.Toggle(new Rect(x, y, w, 22), _censusOnLoad.Value,
                                     " Memory census after every load (log; a short hitch after the load)");
            if (census != _censusOnLoad.Value) _censusOnLoad.Value = census;
            y += 26f;
            // Run from Tick, never inside OnGUI.
            if (WinPx.Button(new Rect(x, y, 200, 22), "Memory census now"))
            {
                _censusLabel = "a click (" + _loads.Loads + " loads so far)";
                _censusDue = Time.unscaledTime;
                _censusText.text = "running...";
            }
            y += 26f;
            y += UiText.Draw(x, y, w, _censusText);
            return y;
        }
    }
}
