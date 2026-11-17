using BepInEx.Configuration;
using ForestOverlay.Core;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // The game fixes runners install the mod for, in one always-on module
    // (T-0458, REVIEW.md Top 10 item 3): the three load-leak fixes
    // (Game/LeakedThreads, StaleSubscribers, PathPoolCleanup; game-notes
    // "The load leak") and the performance patches (Game/PerfPatches, which
    // creates CameraTrim and GarbagePatches). They used to live in
    // SavestateModule and DebugViewModule; a profile that dropped those
    // would have dropped them too.
    //
    // A pure move: the same config sections, keys, defaults and log lines,
    // and the same toggles in the same places - Settings > Loads and
    // savestates (DrawLeakFixes, under the savestate option), Settings >
    // Performance (DrawPerformance) and Developer > Experimental
    // (DrawExperimental, under the trajectory preview).
    //
    // Stale subscribers are pruned once per load (by load number).
    // SavestateModule calls PruneSubscribers just before its census / heap
    // line, as before, so that line shows the result; this module's own
    // load watcher covers a load when SavestateModule is not there.
    // Registered last in BuildModules, so the bridge's module indexes stay;
    // it initialises last too, so LoadTiming orders its hooks after
    // PerfPatches by Harmony id (the old order). The other game methods
    // these patch are patched by nobody else, by patches with an explicit
    // priority (WreckNav), or were already patched first by the other
    // owner (PathfindingWatch on AstarPath.Awake, before PathPoolCleanup).
    //
    // Not practice-only: the leak fixes and the behaviour-preserving
    // patches change no gameplay. The experimental patches are marked on
    // (Ctx.Practice.SetOn) and suspended in run mode, as before.
    // ------------------------------------------------------------------
    public sealed class GameFixesModule : OverlayModule
    {
        private const float Row = 24f;

        public override string Id { get { return "gamefixes"; } }
        public override string DisplayName { get { return "Game fixes"; } }

        private LeakedThreads _threads;
        private ConfigEntry<bool> _threadsFix;
        private StaleSubscribers _subscribers;
        private ConfigEntry<bool> _subscribersFix;
        private PathPoolCleanup _pathPool;
        private ConfigEntry<bool> _pathPoolFix;
        private readonly LoadWatcher _loads = new LoadWatcher();
        private int _prunedLoad;   // the last load number pruned for

        private PerfPatches _perf;
        private string[] _perfNames;   // the HUD's ON NOW names, built once

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);

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

            _perf = new PerfPatches(Ctx.Log, Ctx.Config, OverlayPlugin.PluginGuid);
        }

        /// Drops the previous world's event subscribers, once per load:
        /// `load` is a LoadWatcher's count (1 = the first load). Both
        /// watchers (SavestateModule's and this one's) count the same
        /// FinishGameLoad edges from the plugin's start, so the number
        /// names the load. SavestateModule calls it before its load line;
        /// this module's Tick covers a load when SavestateModule is gone.
        public void PruneSubscribers(int load)
        {
            if (_subscribers == null || load <= _prunedLoad) return;
            _prunedLoad = load;
            _subscribers.Prune();
        }

        /// A performance patch on / off (the bridge calls this).
        public void TogglePerfPatch(int i)
        {
            if (_perf != null && i >= 0 && i < _perf.Count) _perf.Toggle(i);
        }

        public override void Tick()
        {
            if (_loads.Tick()) PruneSubscribers(_loads.Loads);

            if (_perf == null) return;
            // The experimental performance switches change the game: say
            // which are on (the HUD's ON NOW line).
            _perf.SuspendExperimental(Ctx.Run.Locks("perf"));
            if (_perfNames == null)
            {
                _perfNames = new string[_perf.Count];
                for (int i = 0; i < _perfNames.Length; i++) _perfNames[i] = _perf.FlagName(i);
            }
            for (int i = 0; i < _perf.Count && i < _perfNames.Length; i++)
                if (_perf.IsExperimental(i)) Ctx.Practice.SetOn(_perfNames[i], _perf.IsOn(i) && !_perf.ExperimentalSuspended);
            _perf.Tick(Ctx.Player, Ctx.Events);
        }

        public override void Shutdown()
        {
            if (_threads != null) _threads.Uninstall();
            if (_pathPool != null) _pathPool.Uninstall();
            if (_perf != null) _perf.Shutdown();
        }

        // --- drawn by the Settings tab's Loads and savestates fold ----------
        // The load leak: what each load leaves behind (under the savestate
        // option, as before T-0458).
        public float DrawLeakFixes(float x, float y, float w)
        {
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

        // --- drawn by the Settings tab's Performance fold ---------------------
        // The behaviour-preserving patches: on by default and for everyone
        // (author, 2026-10-08: not developer-only). Draws from x 0, width w.
        public float DrawPerformance(float y, float w)
        {
            y += UiText.Note(12, y, w - 24, PerformanceNote) + 4f;
            return DrawPerf(y, w, false);
        }

        // --- drawn by the Developer tab's Experimental fold -------------------
        // The gameplay-altering performance switches, under the trajectory
        // preview (DebugViewModule.DrawExperimental).
        public float DrawExperimental(float y, float w)
        {
            { float hh = UiText.Draw(12, y, w - 24, "Gameplay-altering performance switches"); UiText.Note(12, y + hh, w - 24, ExperimentalNote); y += hh; }
            return DrawPerf(y, w, true) + 4f;
        }

        // One group of performance switches: the behaviour-preserving ones,
        // or the experimental ones with what each changes under it.
        private float DrawPerf(float y, float w, bool experimental)
        {
            for (int i = 0; i < _perf.Count; i++)
            {
                if (_perf.IsExperimental(i) != experimental) continue;
                bool guiWas = GUI.enabled;
                if (experimental && _perf.ExperimentalSuspended) GUI.enabled = false;
                bool on = UiKit.Toggle(new Rect(12, y, w - 24, 22), _perf.IsOn(i), _perf.Label(i));
                GUI.enabled = guiWas;
                if (on != _perf.IsOn(i)) _perf.Toggle(i);
                y += Row;
                if (_perf.Note(i).Length > 0) UiText.Note(30, y, w - 42, _perf.Note(i));
                if (_perf.Status(i) != (_perf.IsOn(i) ? "on" : "off"))
                    y += UiText.Draw(30, y, w - 42, _perf.Status(i));
            }
            return y;
        }

        private static readonly GUIContent PerformanceNote = new GUIContent(
            "Less work for the game each frame, and less garbage to collect (fewer hitches); " +
            "each one keeps what the game does. Untick one to get the game's own code back.");
        private static readonly GUIContent ExperimentalNote = new GUIContent(
            "Off by default. These change what the game does, not only how fast it runs; each one says what under it.");
    }
}
