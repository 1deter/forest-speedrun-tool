using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Patches that make the game allocate less (Next up 6). Every managed
    // allocation brings the next garbage collection closer, and each one
    // is a 90+ ms frame (the Boehm pause over a ~280 MB heap). Measured
    // with the allocation tracker (v0.24.91, Slot 1 surface, standing
    // still, ~200 fps): ~420 KB/s. These cut ~250 KB/s of it.
    //
    // Each one is behaviour-preserving (the game computes and draws the
    // same things), has its own switch (`[Performance]`, on by default,
    // Debug views tab), applies and removes live, and logs one line.
    //
    // 1. Overlay layout (ours): Unity runs an IMGUI layout pass - a new
    //    GUILayoutGroup, its list and a RectOffset - for every OnGUI
    //    behaviour with useGUILayout on, every frame. The overlay uses no
    //    GUILayout; only GUI.Window needs the pass, so it is on only while
    //    one of our windows is showing (Plugin.Update reads OverlayLayout).
    // 2. Post-processing layout: PostProcessingBehaviour.OnGUI acts on
    //    Repaint only (the Scion eye-adaptation switch, debug textures by
    //    GUI.DrawTexture) - its layout pass is pure waste, per camera.
    // 3. Ocean comparer: Ceto.ProjectedGrid.m_grids is a Dictionary keyed
    //    by the MESH_RESOLUTION enum with the default comparer, which on
    //    this Mono boxes the key on every lookup (~21 a frame: Update and
    //    every camera's OceanOnPostRender). Same dictionary, a comparer
    //    that compares the enum's int without boxing; hash = the value,
    //    as the default's, so even the iteration order is unchanged.
    // 4. Atmosphere arrays: TheForestAtmosphere.UpdateShaderParameters
    //    (per camera, per frame) allocates two Vector3[4] that
    //    Camera.CalculateFrustumCorners fills completely before they are
    //    read, and that never leave the method. A transpiler hands it two
    //    kept arrays instead.
    // 5. Asset unloads merged: entering a cave, GreebleZonesManager and
    //    SceneUnloadInCave each ask ResourcesHelper.UnloadUnusedAssets for
    //    a sweep 0.1 s after their scenes unload - two full walks of every
    //    loaded object at once (bridge, 2026-09-26: 660 ms and 1.02 s,
    //    frames of 361 / 368 ms). A request while a sweep is still running
    //    gets that sweep's operation (callers only wait on it). A scene
    //    unloaded after the running sweep began earns one sweep after it,
    //    so nothing is left unswept - only the overlap goes.
    // 7. Save load hand-over - EXPERIMENTAL, off by default (author's
    //    rule, 2026-09-26: anything that differs from the game goes in a
    //    labelled section, off). LoadSave.Activation sets
    //    sceneTracker.waitForLoadSequence, yields WaitPointSixSeconds, then
    //    waits while sceneTracker.doingGlobalNavUpdate. The 0.6 s gives the
    //    building nav cut (gridObjectBlockerManager.NavCutRountine, waiting
    //    on that flag) its turn: it calls doNavCut on every registered
    //    blocker, whose StartCoroutine(doGlobalStructureBoundsNavRemove)
    //    sets doingGlobalNavUpdate at once. Everything else that waits on
    //    the flag (spawns, animals, birds, Astar regions) only starts on
    //    it. So the wait becomes: at least 3 frames and until the manager
    //    is no longer _running, never longer than the 0.6 s. Single player
    //    only (Bolt not running).
    //    A/B (bridge, 2026-09-26, v0.24.95): Activation 2.70 -> 1.35 s from
    //    the title screen, 2.41 -> 1.47 s on a Full load; player, time,
    //    animals, cannibal spawners and the picture the same. The one
    //    difference: a building nav update the game ran inside the wait
    //    (339 ms, from blockers that register during it) runs after the
    //    hand-over instead - so the player can move while it finishes.
    // 8. The endgame load of our restores in the background (on by
    //    default: the restore holds the player anyway; the game's own
    //    trigger crossing is untouched) - EndgameLoader.PatchStream.
    // 9. The endgame load in play in the background - on by default since
    //    v0.24.111 (author, 2026-09-26: on "if it doesn't affect run time,
    //    or anything that would usually invalidate a speedrun"; key renamed
    //    from EndgameAsyncInRuns so v0.24.107-110's saved "off" goes):
    //    the game's own load behind the vault door (a ~5 s frozen frame
    //    inside the door's cutscene) goes async too; the player is pinned
    //    if it outlasts the cutscene. Same transpiler as 8. A run's real
    //    time is unchanged: Time.maximumDeltaTime is 9 here, so the frozen
    //    frame counts as game time and the cutscene ends when it would.
    // 10. The endgame-animation sweep at load (IL, 2026-09-26): on every
    //    game-scene load animClipMemoryManager.Start -> UnloadEndGameAnimation
    //    starts three AnimationLoadManager.UnloadAnimation coroutines
    //    (refreshAssets false) and, in the same frame, a full
    //    ResourcesHelper.UnloadUnusedAssets - 1.05-1.20 s, one frame of
    //    550-800 ms, inside the load. Each coroutine first waits on a
    //    Resources.LoadAsync of the "<clip> Empty" placeholder and only then
    //    swaps the clip out, so the sweep runs while the three clips are
    //    still referenced and cannot free them. A transpiler removes that
    //    one `call UnloadUnusedAssets; pop`; the coroutines run as before.
    //    A/B (bridge, v0.24.109, Full loads of phantom-a, off/on/on/off):
    //    Unity objects after the load 497431 / 497250 / 497458 / 497451 -
    //    the sweep frees nothing; "in game after" 5.4 / 5.0 / 5.2 / 5.7 s.
    //    The big frame at that point stays (766-917 ms with it on): it is
    //    the new scene's own start-up; the sweep only added to it. On by
    //    default since v0.24.110 (key renamed so v0.24.109's saved "off"
    //    does not keep it off).
    // 11-12. Cameras that draw what nobody sees (raw FPS, v0.24.116):
    //    the terrain's leftover grass camera off, the endgame's plane
    //    screen rendered only when the screen is drawn - Game/CameraTrim.
    //    Each camera costs the main thread ~0.25 ms a frame whatever it
    //    draws; these are frame time, not garbage.
    //    Not done: skipping the action-icon camera mid-frame (v0.24.119,
    //    withdrawn in v0.24.120) - disabling the last screen camera in an
    //    earlier camera's onPreCull left the picture frozen (game-notes).
    // 13. EXPERIMENTAL: the sun's shadow map (Sunshine) every second frame,
    //    by the game's own UpdateInterval option - Game/CameraTrim.
    // 14. EXPERIMENTAL: the grass-bending camera off while in a cave -
    //    Game/CameraTrim.
    // 15. REMOVED in v0.24.210 (author: changes physics noticeably, maks).
    //    Was: physics at 30 Hz - the game's own hidden
    //    "Low Quality Physics" option (PlayerPreferences.SetLowQualityPhysics:
    //    fixedDeltaTime 1/30 instead of 1/60; treeHitTrigger and RaftPush
    //    read the flag and adapt). Its widget is gone from the options menu;
    //    the game still reads the saved pref at startup and has the console
    //    command `physics30Fps`. A save load resets fixedDeltaTime
    //    (LevelLoader.Load), so it is applied again whenever the step is back
    //    at exactly 1/60 (never over PlayMaker's ScaleTime slow motion).
    //    Measured: 5.26 -> 4.98 ms a frame here (a step in 32% -> 15% of
    //    frames); a physics step costs ~1.8 ms on sxczurass's i5.
    // ------------------------------------------------------------------
    public sealed class PerfPatches
    {
        /// Fix 1, read by Plugin.Update: only lay out our OnGUI while a
        /// window of ours is showing.
        public static bool OverlayLayout;

        private sealed class Fix
        {
            public string Key;
            public string Label;
            public ConfigEntry<bool> Cfg;
            public Func<string> Apply;    // "" = applied, else why not
            public Action Remove;
            public bool Applied;
            public string Status = "off";
            public bool Experimental;     // changes the game: off by default, own section
            public string Note = "";      // what it changes, shown under it
        }

        private readonly ManualLogSource _log;
        private readonly Harmony _harmony;
        private readonly List<Fix> _fixes = new List<Fix>();
        private readonly CameraTrim _cameras;

        public PerfPatches(ManualLogSource log, ConfigFile config, string harmonyId)
        {
            _log = log;
            _harmony = new Harmony(harmonyId + ".perf");
            _cameras = new CameraTrim(log, _harmony);

            Add(config, "OverlayLayoutOnlyForWindows", "Overlay: no GUI layout pass without a window",
                "Skip Unity's GUI layout pass for the overlay while none of its windows is open (saves ~40 KB/s of garbage).",
                delegate { OverlayLayout = true; return ""; }, delegate { OverlayLayout = false; });
            Add(config, "PostProcessingNoLayout", "Post-processing: no GUI layout pass",
                "Skip Unity's GUI layout pass for the game's post-processing, which never uses it (saves ~80-110 KB/s of garbage).",
                ApplyPostProcessing, RemovePostProcessing);
            Add(config, "OceanNoBoxing", "Ocean: look up grids without boxing",
                "Give the ocean's grid table a comparer that does not box its keys (saves ~80 KB/s of garbage).",
                ApplyOcean, RemoveOcean);
            Add(config, "AtmosphereReuseArrays", "Atmosphere: reuse two arrays per camera",
                "Let the atmosphere reuse the two small arrays it fills every frame per camera (saves ~30 KB/s of garbage).",
                ApplyAtmosphere, RemoveAtmosphere);
            Add(config, "MergeAssetUnloads", "Loads: merge overlapping asset clean-ups",
                "When the game asks for an unused-asset clean-up while one is still running (entering a cave asks twice), " +
                "share the running one instead of walking everything again (saves a ~0.4 s hitch on entering a cave).",
                ApplyUnloads, RemoveUnloads);
            Add(config, "VRSwitcherNoLayout", "VR switcher: no GUI layout pass",
                "Skip Unity's GUI layout pass for the game's VR switcher, which draws nothing outside VR (saves ~50 KB/s of garbage).",
                ApplyVrSwitcher, RemoveVrSwitcher);
            Add(config, "SaveLoadNoFixedWait", "Save loads: skip the game's fixed wait before you get control",
                "EXPERIMENTAL, changes the game: a save load hands over control without the game's fixed 0.6 s wait " +
                "(about 1 s sooner, as the wait runs slow during a load). The game's nav-mesh update for buildings, which it " +
                "finished inside that wait, can then still be running for a fraction of a second after you can move. Off = the game's own code.",
                ApplyHandOver, RemoveHandOver, false);
            _fixes[_fixes.Count - 1].Experimental = true;
            _fixes[_fixes.Count - 1].Note = "Changes the game: the nav-mesh update for buildings can finish just after you get control " +
                                            "(enemies' paths around them). Saves about 1 s per save load.";
            Add(config, "EndgameAsyncForRestores", "Savestates: load the endgame area in the background",
                "When a savestate restore loads the endgame area (a Full load of a state captured with it, or a Quick load that needs it), " +
                "load it in the background while the restore holds you, instead of the game's one ~5 s frozen frame. " +
                "The game's own load when you walk in during play is unchanged.",
                delegate { return EndgameLoader.PatchStream(_harmony, _log, false); }, delegate { EndgameLoader.UnpatchStream(_harmony, false); });
            Add(config, "EndgameAsyncAtVaultDoor", "Endgame: load it in the background at the vault door",
                "The game's own load of the endgame area after the vault door opens runs in the background during the door's cutscene, " +
                "instead of freezing the picture for one ~5 s frame. The cutscene ends at the same moment either way (the game counts " +
                "the frozen frame as time passed), so a run's time does not change. If the load outlasts the cutscene you are held in " +
                "place until it is in. A load started outside the cutscene (walking back through the endgame's load trigger) is the game's own.",
                delegate { return EndgameLoader.PatchStream(_harmony, _log, true); }, delegate { EndgameLoader.UnpatchStream(_harmony, true); });
            _fixes[_fixes.Count - 1].Note = "The door's cutscene plays smoothly instead of freezing for ~5 s; a run's time is the same.";
            Add(config, "SkipEndgameAnimSweepAtLoad", "Loads: skip the endgame-animation clean-up",
                "Every save load, the game starts unloading three endgame animations and, in the same frame, walks every loaded asset " +
                "to free unused ones (~1 s, one frame of 0.5-0.8 s) - before the animations are actually unloaded, so that walk frees " +
                "nothing (measured: the same number of objects after the load). Skip that one walk; the animations are unloaded as " +
                "before (saves ~0.4 s per load).",
                ApplyAnimSweep, RemoveAnimSweep);
            Add(config, "TerrainGrassCameraOff", "Grass: switch off the terrain's unused grass camera",
                "The terrain carries a second grass-bending camera that draws, every frame, into a picture nothing uses - the grass " +
                "bends from the game's own camera that follows you. Switch the unused one off (each camera costs every frame, " +
                "~0.25 ms here; more on a slower processor).",
                _cameras.ApplyGrass, _cameras.RemoveGrass);
            Add(config, "EndgameScreenOnDemand", "Endgame: draw the plane screen only when it is on screen",
                "While the endgame area is loaded, a camera draws a small scene for one screen in the control room every frame - a " +
                "screen that is off until the end-crash ending (the game meant to switch the camera by distance, but its switch " +
                "skips cameras). Draw it only in frames where that screen is being drawn (~0.35 ms a frame here while the endgame " +
                "is loaded).",
                _cameras.ApplyScreen, _cameras.RemoveScreen);
            Add(config, "SunShadowsEveryOtherFrame", "Sun shadows: redraw every second frame",
                "EXPERIMENTAL, changes the picture: the game's sun-shadow system (Sunshine) redraws its shadow map every second frame " +
                "instead of every frame, using its own built-in option for this. Still shadows look the same; shadows of moving things " +
                "(you, enemies, swaying trees) update at half your frame rate. Saves ~0.3 ms a frame here (0.35-0.4 ms on the runners' " +
                "machines measured). Off = the game's own setting.",
                _cameras.ApplySunshine, _cameras.RemoveSunshine, false);
            _fixes[_fixes.Count - 1].Experimental = true;
            _fixes[_fixes.Count - 1].Note = "Changes the picture: moving shadows update at half the frame rate. Saves ~0.3 ms a frame.";
            Add(config, "GrassBendingOffInCaves", "Caves: no grass bending while inside",
                "EXPERIMENTAL, changes the picture: while you are in a cave, the game's grass-bending camera (it draws where you " +
                "and enemies push the grass and ferns aside) stops drawing - there is no grass inside a cave. The one difference: " +
                "looking out of a cave mouth, grass outside no longer bends around enemies until you leave the cave. Back on in the " +
                "frame you leave. Saves ~0.25 ms a frame in caves here (up to ~2 ms measured on a laptop). Off = the game's own code.",
                _cameras.ApplyCaveGrass, _cameras.RemoveCaveGrass, false);
            _fixes[_fixes.Count - 1].Experimental = true;
            _fixes[_fixes.Count - 1].Note = "Changes the picture: from inside a cave, grass outside the mouth does not bend around enemies. " +
                                            "Saves ~0.25 ms a frame in caves.";
            // 15 was physics at 30 Hz: removed (author, 2026-10-02 - maks found
            // it changes the game's physics noticeably). ClearPhysics30 undoes
            // it once for anyone who had it on.
            ClearPhysics30(config);

            Add(config, "NavRemovalOwnArea", "Buildings removed: recalculate enemy paths only where they stood",
                "When a building (or the plane wreck) goes, the game recalculates enemy paths over the area it stood on - but it never " +
                "forgets earlier removals, so each one covers every place anything was removed since the game started, up to most " +
                "of the map (16 s of background work measured, during which enemies cannot find new paths and the next load waits). " +
                "Forget the earlier areas once their own recalculation is queued; each removal then covers its own place. " +
                "Removals at the same moment far apart (a Quick load deleting buildings across the map) are recalculated place by " +
                "place, not as one box over everything between them.",
                ApplyNavRemoval, RemoveNavRemoval);

            for (int i = 0; i < _fixes.Count; i++)
                if (_fixes[i].Cfg.Value) Set(_fixes[i], true);
        }

        private void Add(ConfigFile config, string key, string label, string description, Func<string> apply, Action remove)
        {
            Add(config, key, label, description + " Behaviour-preserving; off = the game's own code.", apply, remove, true);
        }

        private void Add(ConfigFile config, string key, string label, string description, Func<string> apply, Action remove, bool defaultOn)
        {
            Fix f = new Fix();
            f.Key = key;
            f.Label = " " + label;   // the toggle's text, built once
            f.Cfg = config.Bind("Performance", key, defaultOn, description);
            f.Apply = apply;
            f.Remove = remove;
            _fixes.Add(f);
        }

        public int Count { get { return _fixes.Count; } }
        public string Label(int i) { return _fixes[i].Label; }
        public bool IsOn(int i) { return _fixes[i].Cfg.Value; }
        public string Status(int i) { return _fixes[i].Status; }
        public bool IsExperimental(int i) { return _fixes[i].Experimental; }
        public string Note(int i) { return _fixes[i].Note; }

        /// The GUI switch (and the bridge): on / off, saved, applied live.
        public void Toggle(int i)
        {
            Fix f = _fixes[i];
            f.Cfg.Value = !f.Cfg.Value;
            if (f.Experimental && _experimentalSuspended) return;   // applied when run mode ends
            Set(f, f.Cfg.Value);
        }

        private bool _experimentalSuspended;

        /// Run mode: the experimental (game-changing) switches are taken
        /// out while it is on and put back as saved after. Cheap when
        /// nothing changes - called every tick.
        public void SuspendExperimental(bool suspend)
        {
            if (suspend == _experimentalSuspended) return;
            _experimentalSuspended = suspend;
            for (int i = 0; i < _fixes.Count; i++)
                if (_fixes[i].Experimental) Set(_fixes[i], !suspend && _fixes[i].Cfg.Value);
            _log.LogInfo("Performance patches: experimental ones " + (suspend ? "suspended (run mode)." : "back as saved."));
        }

        public bool ExperimentalSuspended { get { return _experimentalSuspended; } }

        /// Dev (bridge): every live script with an OnGUI that still gets
        /// Unity's layout pass (useGUILayout on, enabled) - what is left
        /// of the per-frame GUILayoutGroup garbage.
        public string ListLayoutUsers()
        {
            Dictionary<string, int> found = new Dictionary<string, int>();
            UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(typeof(MonoBehaviour));
            for (int i = 0; i < all.Length; i++)
            {
                MonoBehaviour mb = all[i] as MonoBehaviour;
                if (mb == null || !mb.enabled || !mb.useGUILayout) continue;
                bool hasGui = false;
                for (Type t = mb.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                    if (t.GetMethod("OnGUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                                    null, Type.EmptyTypes, null) != null) { hasGui = true; break; }
                if (!hasGui) continue;
                string key = mb.GetType().FullName + " on '" + mb.gameObject.name + "'";
                int n;
                found.TryGetValue(key, out n);
                found[key] = n + 1;
            }
            if (found.Count == 0) return "none";
            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, int> kv in found) parts.Add(kv.Key + (kv.Value > 1 ? " x" + kv.Value : ""));
            return string.Join("; ", parts.ToArray());
        }

        /// Once a frame (Debug views module).
        public void Tick(PlayerRef player, GameEvents events)
        {
            EndgameLoader.Tick(player, events);
            _cameras.Tick();
            if (!_unloadTrailing || _unloadRunning == null) return;
            bool done;
            try { done = _unloadRunning.isDone; }
            catch (Exception) { done = true; }
            if (!done) return;
            _unloadTrailing = false;
            _unloadSceneSince = false;
            _unloadRunning = Resources.UnloadUnusedAssets();
            _log.LogInfo("Performance: one more asset clean-up - a scene unloaded while the merged one ran.");
        }

        public void Shutdown()
        {
            for (int i = 0; i < _fixes.Count; i++) if (_fixes[i].Applied) Set(_fixes[i], false);
        }

        private void Set(Fix f, bool on)
        {
            if (on == f.Applied) return;
            try
            {
                if (on)
                {
                    string why = f.Apply();
                    f.Applied = why.Length == 0;
                    f.Status = f.Applied ? "on" : "not applied: " + why;
                }
                else
                {
                    f.Remove();
                    f.Applied = false;
                    f.Status = "off";
                }
            }
            catch (Exception ex)
            {
                f.Applied = false;
                f.Status = "failed: " + (ex.InnerException ?? ex).Message;
            }
            _log.LogInfo("Performance patch '" + f.Key + "': " + f.Status + ".");
        }

        private static Type GameType(string name)
        {
            return GameBridge.FindGameType(name);
        }

        // ------------------------------------------------------------------
        // 15. Physics at 30 Hz - removed in v0.24.210. It switched on the
        // game's hidden "Low Quality Physics" (PlayerPreferences
        // .SetLowQualityPhysics), whose saved pref the game reads at startup
        // and whose widget is gone from the options menu: someone who had the
        // switch on and saved the options could be left at 30 Hz with no way
        // back. Once, for a config that had it on: set 60 Hz and clear the pref.
        private void ClearPhysics30(ConfigFile config)
        {
            try
            {
                ConfigEntry<bool> old = config.Bind("Performance", "Physics30Hz", false,
                    "Removed in v0.24.210 (it changed the game's physics). Left false; nothing reads it.");
                if (!old.Value) return;
                old.Value = false;
                Type t = GameType("PlayerPreferences");
                MethodInfo set = t == null ? null : t.GetMethod("SetLowQualityPhysics", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null);
                if (set != null) set.Invoke(null, new object[] { false });
                if (PlayerPrefs.GetInt("LowQualityPhysics", 0) != 0) PlayerPrefs.SetInt("LowQualityPhysics", 0);
                _log.LogInfo("Performance: the removed 30 Hz physics switch was on - physics back at 60 Hz (fixed step " +
                             Time.fixedDeltaTime.ToString("0.0000") + " s).");
            }
            catch (Exception ex) { _log.LogWarning("Performance: clearing the old 30 Hz physics switch failed: " + ex.Message); }
        }

        // ------------------------------------------------------------------
        // 2. Post-processing
        private const string PostType = "UnityEngine.PostProcessing.PostProcessingBehaviour";
        private MethodInfo _postEnable;

        private string ApplyPostProcessing()
        {
            Type t = GameType(PostType);
            if (t == null) return PostType + " not found";
            _postEnable = t.GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (_postEnable == null) return "OnEnable not found";
            _harmony.Patch(_postEnable, postfix: new HarmonyMethod(typeof(PerfPatches).GetMethod("PostEnablePostfix", BindingFlags.Static | BindingFlags.NonPublic)));
            SetLayout(t, false);
            return "";
        }

        private void RemovePostProcessing()
        {
            if (_postEnable != null) _harmony.Unpatch(_postEnable, HarmonyPatchType.Postfix, _harmony.Id);
            Type t = GameType(PostType);
            if (t != null) SetLayout(t, true);
        }

        private static void SetLayout(Type t, bool layout)
        {
            UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(t);
            for (int i = 0; i < all.Length; i++)
            {
                MonoBehaviour mb = all[i] as MonoBehaviour;
                if (mb != null) mb.useGUILayout = layout;
            }
        }

        private static void PostEnablePostfix(MonoBehaviour __instance)
        {
            try { if (__instance != null) __instance.useGUILayout = false; }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------
        // 5. Asset unloads
        private MethodInfo _unloadMethod;
        private static PerfPatches _self;
        private static AsyncOperation _unloadRunning;
        private static bool _unloadSceneSince;
        private static bool _unloadTrailing;

        private string ApplyUnloads()
        {
            Type t = GameType("TheForest.Utils.ResourcesHelper");
            if (t == null) return "ResourcesHelper not found";
            _unloadMethod = t.GetMethod("UnloadUnusedAssets", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (_unloadMethod == null || _unloadMethod.ReturnType != typeof(AsyncOperation)) return "UnloadUnusedAssets not found";
            _self = this;
            _harmony.Patch(_unloadMethod,
                new HarmonyMethod(typeof(PerfPatches).GetMethod("UnloadPrefix", BindingFlags.Static | BindingFlags.NonPublic)),
                new HarmonyMethod(typeof(PerfPatches).GetMethod("UnloadPostfix", BindingFlags.Static | BindingFlags.NonPublic)));
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            return "";
        }

        private void RemoveUnloads()
        {
            if (_unloadMethod != null) _harmony.Unpatch(_unloadMethod, HarmonyPatchType.All, _harmony.Id);
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            _unloadTrailing = false;
        }

        private static void OnSceneUnloaded(Scene s)
        {
            if (_unloadRunning != null) _unloadSceneSince = true;
        }

        private static bool UnloadPrefix(ref AsyncOperation __result)
        {
            try
            {
                if (_unloadRunning == null || _unloadRunning.isDone) return true;
                __result = _unloadRunning;
                if (_unloadSceneSince) _unloadTrailing = true;
                if (_self != null) _self._log.LogInfo("Performance: asset clean-up merged into the one running" +
                                                      (_unloadTrailing ? " (one more after it: a scene unloaded meanwhile)" : "") + ".");
                return false;
            }
            catch (Exception) { return true; }
        }

        private static void UnloadPostfix(AsyncOperation __result)
        {
            if (__result == null || ReferenceEquals(__result, _unloadRunning)) return;
            _unloadRunning = __result;
            _unloadSceneSince = false;
        }

        // ------------------------------------------------------------------
        // 10. The endgame-animation sweep at load
        private MethodInfo _animUnload;
        private static int _animSweepFound;
        public static int AnimSweepsSkipped { get; private set; }

        private string ApplyAnimSweep()
        {
            Type t = GameType("animClipMemoryManager");
            if (t == null) return "animClipMemoryManager not found";
            _animUnload = t.GetMethod("UnloadEndGameAnimation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (_animUnload == null) return "UnloadEndGameAnimation not found";
            _self = this;
            _animSweepFound = 0;
            _harmony.Patch(_animUnload,
                postfix: new HarmonyMethod(typeof(PerfPatches).GetMethod("AnimSweepPostfix", BindingFlags.Static | BindingFlags.NonPublic)),
                transpiler: new HarmonyMethod(typeof(PerfPatches).GetMethod("AnimSweepTranspiler", BindingFlags.Static | BindingFlags.NonPublic)));
            if (_animSweepFound == 1) return "";
            // Not the code this was written for: the game's own code back.
            _harmony.Unpatch(_animUnload, HarmonyPatchType.All, _harmony.Id);
            return "expected one clean-up call, found " + _animSweepFound + " (game updated?)";
        }

        private void RemoveAnimSweep()
        {
            if (_animUnload != null) _harmony.Unpatch(_animUnload, HarmonyPatchType.All, _harmony.Id);
        }

        /// `call ResourcesHelper.UnloadUnusedAssets; pop` -> two nops
        /// (labels stay on the instructions).
        private static IEnumerable<CodeInstruction> AnimSweepTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> list = new List<CodeInstruction>(instructions);
            int found = 0;
            for (int i = 0; i + 1 < list.Count; i++)
            {
                MethodInfo m = list[i].operand as MethodInfo;
                if (list[i].opcode != OpCodes.Call || m == null || m.Name != "UnloadUnusedAssets" ||
                    m.DeclaringType == null || m.DeclaringType.Name != "ResourcesHelper") continue;
                if (list[i + 1].opcode != OpCodes.Pop) continue;
                list[i].opcode = OpCodes.Nop;
                list[i].operand = null;
                list[i + 1].opcode = OpCodes.Nop;
                found++;
            }
            _animSweepFound = found;
            return list;
        }

        private static void AnimSweepPostfix()
        {
            AnimSweepsSkipped++;
            if (_self != null)
                _self._log.LogInfo("Performance: skipped the endgame-animation asset clean-up at load (" + AnimSweepsSkipped + " this session).");
        }

        // ------------------------------------------------------------------
        // 6. VR switcher (same as 2)
        // --- NavRemovalOwnArea -------------------------------------------
        // sceneTracker.startDummyNavRemove (from setupNavRemoveRoot.OnDestroy)
        // adds the removed thing's bounds to dummyNavBounds, waits 7 s and
        // queues one graph update over ALL of dummyNavBounds - and clears
        // only dummyNavStructures, never the bounds (IL). Every removal
        // re-covers every earlier one: a Quick load's wreck removal took a
        // 988 x 930 m update, 16.2 s (v0.24.142 PathfindingWatch lines).
        // A prefix clears the list when no batch is pending: a batch keeps
        // merging what goes within its 7 s, as the game meant, and areas
        // already recalculated are not recalculated again (same navmesh).
        //
        // v0.24.177: the batch itself is one box too (Encapsulate), however
        // far apart its removals are. A Quick load from another save deletes
        // buildings across the map within the 7 s: one update of 1536 x
        // 1406 m (Tom's logs, v0.24.173 - queued shortly before both of his
        // sessions that ended in a native crash; 16-60 s of background work
        // on a slower PC). The prefix now runs the batch with the game's own
        // steps (wait 7 s, the dummyRootNavRemove prefab, doRootNavRemove -
        // navRemoveRoot.startRemove queues the update) but gathers by place:
        // a removal joins a waiting batch only while it stays within
        // NavBatchSpan, else it starts its own. Same areas recalculated,
        // none of the land between them.
        private MethodInfo _dummyNavRemove;
        private static FieldInfo _dummyNavBounds;     // List<Bounds>
        private static FieldInfo _doingDummyNav;      // bool
        private static FieldInfo _astarActive;        // static AstarPath.active
        private const float NavBatchSpan = 150f;      // m, x / z
        private const float NavBatchWait = 7f;        // s, the game's

        private sealed class NavBatch
        {
            public Bounds Area;
            public Vector3 Pos;
            public readonly List<int> Roots = new List<int>();
        }

        private static readonly List<NavBatch> _navBatches = new List<NavBatch>();
        private static int _navWaveBatches;           // batches since none was waiting
        private static int _navWaveRemovals;
        private static Bounds _navWaveArea;           // what the game would have made one box of

        private string ApplyNavRemoval()
        {
            Type t = GameType("sceneTracker");
            if (t == null) return "sceneTracker not found";
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _dummyNavRemove = t.GetMethod("startDummyNavRemove", inst);
            _dummyNavBounds = t.GetField("dummyNavBounds", inst);
            _doingDummyNav = t.GetField("doingDummyNavUpdate", inst);
            if (_dummyNavRemove == null || _dummyNavBounds == null || _doingDummyNav == null) return "startDummyNavRemove / its fields not found";
            Type astar = GameType("AstarPath");
            _astarActive = astar != null ? astar.GetField("active", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) : null;
            if (_astarActive == null) return "AstarPath.active not found";
            ParameterInfo[] ps = _dummyNavRemove.GetParameters();
            if (ps.Length != 3 || ps[0].ParameterType != typeof(GameObject) || ps[1].ParameterType != typeof(Vector3) ||
                ps[2].ParameterType != typeof(Bounds) || _dummyNavRemove.ReturnType != typeof(IEnumerator))
                return "startDummyNavRemove is not (GameObject, Vector3, Bounds) -> IEnumerator";
            _self = this;
            _harmony.Patch(_dummyNavRemove, prefix: new HarmonyMethod(typeof(PerfPatches).GetMethod("DummyNavRemovePrefix", BindingFlags.Static | BindingFlags.NonPublic)));
            return "";
        }

        private void RemoveNavRemoval()
        {
            if (_dummyNavRemove != null) _harmony.Unpatch(_dummyNavRemove, HarmonyPatchType.Prefix, _harmony.Id);
        }

        private static bool DummyNavRemovePrefix(object __instance, GameObject __0, Vector3 __1, Bounds __2, ref IEnumerator __result)
        {
            try
            {
                // The game's own batch from before the switch went on: join it.
                if ((bool)_doingDummyNav.GetValue(__instance)) return true;
                System.Collections.IList list = _dummyNavBounds.GetValue(__instance) as System.Collections.IList;
                if (list != null && list.Count > 0)
                {
                    int n = list.Count;
                    list.Clear();
                    if (_self != null) _self._log.LogInfo("Performance: building removal - " + n + " earlier removal area(s) dropped from the game's list " +
                                 "(already recalculated); this one covers its own place.");
                }
                int id = __0 != null ? __0.GetInstanceID() : 0;
                NavBatch join = null;
                for (int i = 0; i < _navBatches.Count; i++)
                {
                    NavBatch nb = _navBatches[i];
                    if (id != 0 && nb.Roots.Contains(id)) { __result = Nothing(); return false; }   // the game's "already listed"
                    if (join == null && WithinSpan(nb.Area, __2)) join = nb;
                }
                if (_navBatches.Count == 0) { _navWaveBatches = 0; _navWaveRemovals = 0; _navWaveArea = __2; }
                else _navWaveArea.Encapsulate(__2);
                _navWaveRemovals++;
                if (join != null)
                {
                    join.Area.Encapsulate(__2);
                    if (id != 0) join.Roots.Add(id);
                    __result = Nothing();
                    return false;
                }
                NavBatch batch = new NavBatch { Area = __2, Pos = __1 };
                if (id != 0) batch.Roots.Add(id);
                _navBatches.Add(batch);
                _navWaveBatches++;
                __result = RunNavBatch(batch);
                return false;
            }
            catch (Exception)
            {
                return true;   // the game's own batch
            }
        }

        private static bool WithinSpan(Bounds area, Bounds add)
        {
            area.Encapsulate(add);
            return area.size.x <= NavBatchSpan && area.size.z <= NavBatchSpan;
        }

        private static IEnumerator Nothing()
        {
            yield break;
        }

        /// The game's batch for one place: wait, then its own dummy object
        /// queues the update (navRemoveRoot.startRemove).
        private static IEnumerator RunNavBatch(NavBatch batch)
        {
            yield return new WaitForSeconds(NavBatchWait);
            _navBatches.Remove(batch);
            UnityEngine.Object astar = _astarActive.GetValue(null) as UnityEngine.Object;
            GameObject prefab = astar != null ? Resources.Load("dummyRootNavRemove") as GameObject : null;
            if (prefab != null)
            {
                GameObject nav = (GameObject)UnityEngine.Object.Instantiate(prefab, batch.Pos, Quaternion.identity);
                nav.SendMessage("doRootNavRemove", batch.Area);
            }
            if (_navBatches.Count == 0 && _navWaveBatches > 1 && _self != null)
                _self._log.LogInfo("Performance: building removals - " + _navWaveRemovals + " at once recalculated in " + _navWaveBatches +
                                   " places instead of one area of " + _navWaveArea.size.x.ToString("0") + " x " +
                                   _navWaveArea.size.z.ToString("0") + " m.");
        }

        private const string VrType = "VRSwitcher";
        private MethodInfo _vrEnable;

        private string ApplyVrSwitcher()
        {
            Type t = GameType(VrType);
            if (t == null) return VrType + " not found";
            if (UsesLayout(t)) return "its OnGUI uses GUILayout / GUI.Window";
            _vrEnable = t.GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) ??
                        t.GetMethod("Awake", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) ??
                        t.GetMethod("Start", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (_vrEnable != null)
                _harmony.Patch(_vrEnable, postfix: new HarmonyMethod(typeof(PerfPatches).GetMethod("PostEnablePostfix", BindingFlags.Static | BindingFlags.NonPublic)));
            SetLayout(t, false);
            return "";
        }

        private void RemoveVrSwitcher()
        {
            if (_vrEnable != null) _harmony.Unpatch(_vrEnable, HarmonyPatchType.Postfix, _harmony.Id);
            Type t = GameType(VrType);
            if (t != null) SetLayout(t, true);
        }

        // A guard for the layout switches: an OnGUI that calls GUILayout or
        // GUI.Window needs the pass. Reads the IL's call targets.
        private static bool UsesLayout(Type t)
        {
            MethodInfo gui = t.GetMethod("OnGUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (gui == null) return false;
            MethodBody body = gui.GetMethodBody();
            if (body == null) return true;
            byte[] il = body.GetILAsByteArray();
            for (int i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != 0x28 && il[i] != 0x6F) continue;   // call / callvirt
                int token = BitConverter.ToInt32(il, i + 1);
                MethodBase m;
                try { m = gui.Module.ResolveMethod(token); }
                catch (Exception) { continue; }
                if (m == null || m.DeclaringType == null) continue;
                if (m.DeclaringType.Name == "GUILayout" || m.DeclaringType.Name == "GUILayoutUtility" ||
                    (m.DeclaringType == typeof(GUI) && (m.Name == "Window" || m.Name == "ModalWindow"))) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // 7. Save load hand-over
        private const int SixSecondsState = 12;   // the iterator's $PC after `yield return WaitPointSixSeconds`
        private const float HandOverCap = 0.6f;
        private MethodInfo _actMoveNext;
        private static FieldInfo _actPcField, _actCurrent, _mgrInstance, _mgrRunning;
        private static PropertyInfo _boltRunning;
        private static object _sixSeconds;
        private static object _holding;           // the iterator being held, null = none
        private static float _holdStart;
        private static int _holdFrames;

        private string ApplyHandOver()
        {
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Type act = LoadTiming.ActivationIterator();
            if (act == null) return "LoadSave.Activation not found";
            _actPcField = act.GetField("$PC", inst);
            _actCurrent = act.GetField("$current", inst);
            _actMoveNext = act.GetMethod("MoveNext", inst, null, Type.EmptyTypes, null);
            if (_actPcField == null || _actCurrent == null || _actMoveNext == null) return "Activation iterator fields not found";
            Type presets = GameType("YieldPresets");
            FieldInfo six = presets != null ? presets.GetField("WaitPointSixSeconds", stat) : null;
            _sixSeconds = six != null ? six.GetValue(null) : null;
            if (_sixSeconds == null) return "YieldPresets.WaitPointSixSeconds not found";
            Type mgr = GameType("gridObjectBlockerManager");
            _mgrInstance = mgr != null ? mgr.GetField("instance", stat) : null;
            _mgrRunning = mgr != null ? mgr.GetField("_running", inst) : null;
            if (_mgrInstance == null || _mgrRunning == null) return "gridObjectBlockerManager fields not found";
            Type bolt = null;
            Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < all.Length && bolt == null; i++)
            {
                try { bolt = all[i].GetType("BoltNetwork", false); }
                catch (Exception) { }
            }
            _boltRunning = bolt != null ? bolt.GetProperty("isRunning", stat) : null;
            if (_boltRunning == null) return "BoltNetwork.isRunning not found";
            _self = this;
            _holding = null;
            _harmony.Patch(_actMoveNext,
                new HarmonyMethod(typeof(PerfPatches).GetMethod("HandOverPrefix", BindingFlags.Static | BindingFlags.NonPublic)),
                new HarmonyMethod(typeof(PerfPatches).GetMethod("HandOverPostfix", BindingFlags.Static | BindingFlags.NonPublic)));
            return "";
        }

        private void RemoveHandOver()
        {
            if (_actMoveNext != null) _harmony.Unpatch(_actMoveNext, HarmonyPatchType.All, _harmony.Id);
            _holding = null;
        }

        // The iterator just yielded the 0.6 s wait: yield one frame instead
        // and hold it in that state (the prefix) until the nav cut has run.
        private static void HandOverPostfix(object __instance, bool __result)
        {
            try
            {
                if (!__result || !ReferenceEquals(_actCurrent.GetValue(__instance), _sixSeconds)) return;
                if ((int)_actPcField.GetValue(__instance) != SixSecondsState) return;
                if ((bool)_boltRunning.GetValue(null, null)) return;
                _actCurrent.SetValue(__instance, null);
                _holding = __instance;
                _holdStart = Time.realtimeSinceStartup;
                _holdFrames = 0;
            }
            catch (Exception) { _holding = null; }
        }

        private static bool HandOverPrefix(object __instance, ref bool __result)
        {
            if (_holding == null || !ReferenceEquals(__instance, _holding)) return true;
            try
            {
                if ((int)_actPcField.GetValue(__instance) != SixSecondsState) { _holding = null; return true; }
                _holdFrames++;
                float held = Time.realtimeSinceStartup - _holdStart;
                bool navCutPending = false;
                object mgr = _mgrInstance.GetValue(null);
                if (mgr != null && !(mgr is UnityEngine.Object && (UnityEngine.Object)mgr == null))
                    navCutPending = (bool)_mgrRunning.GetValue(mgr);
                if ((_holdFrames < 3 || navCutPending) && held < HandOverCap)
                {
                    __result = true;          // still waiting; $current is null = next frame
                    return false;
                }
                _holding = null;
                if (_self != null)
                    _self._log.LogInfo("Performance: save load handed over after " + (held * 1000f).ToString("0") + " ms, " + _holdFrames +
                                       " frame(s) instead of the fixed 600 ms" + (navCutPending ? " (nav cut still pending: cap reached)" : "") + ".");
                return true;
            }
            catch (Exception) { _holding = null; return true; }
        }

        // ------------------------------------------------------------------
        // 3. Ocean
        private const string GridType = "Ceto.ProjectedGrid";
        private FieldInfo _grids;
        private MethodInfo _gridEnable;
        private static object _comparer;          // IEqualityComparer<MESH_RESOLUTION>
        private static FieldInfo _gridsStatic;

        private string ApplyOcean()
        {
            Type t = GameType(GridType);
            if (t == null) return GridType + " not found";
            _grids = t.GetField("m_grids", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (_grids == null || !_grids.FieldType.IsGenericType) return "m_grids not found";
            Type[] args = _grids.FieldType.GetGenericArguments();
            if (args.Length != 2 || !args[0].IsEnum || Enum.GetUnderlyingType(args[0]) != typeof(int)) return "m_grids is not keyed by an int enum";
            _comparer = Activator.CreateInstance(typeof(IntEnumComparer<>).MakeGenericType(args[0]));
            _gridsStatic = _grids;
            _gridEnable = t.GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (_gridEnable == null) return "OnEnable not found";
            _harmony.Patch(_gridEnable, new HarmonyMethod(typeof(PerfPatches).GetMethod("GridEnablePrefix", BindingFlags.Static | BindingFlags.NonPublic)));
            SwapAll(t, _comparer);
            return "";
        }

        private void RemoveOcean()
        {
            if (_gridEnable != null) _harmony.Unpatch(_gridEnable, HarmonyPatchType.Prefix, _harmony.Id);
            Type t = GameType(GridType);
            if (t != null && _grids != null) SwapAll(t, null);
        }

        private void SwapAll(Type t, object comparer)
        {
            UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(t);
            for (int i = 0; i < all.Length; i++) Swap(all[i], comparer);
        }

        // Replaces the table with a copy under the given comparer (null =
        // the default). Contents and their order are kept.
        private static void Swap(object grid, object comparer)
        {
            IDictionary old = _gridsStatic.GetValue(grid) as IDictionary;
            if (old == null) return;
            PropertyInfo cmp = old.GetType().GetProperty("Comparer");
            object current = cmp != null ? cmp.GetValue(old, null) : null;
            if (comparer != null ? ReferenceEquals(current, comparer) : !(current is IIntEnumComparer)) return;
            Type dictType = old.GetType();
            object copy = comparer != null
                ? Activator.CreateInstance(dictType, new object[] { comparer })
                : Activator.CreateInstance(dictType);
            IDictionary dst = (IDictionary)copy;
            foreach (DictionaryEntry e in old) dst.Add(e.Key, e.Value);
            _gridsStatic.SetValue(grid, copy);
        }

        private static void GridEnablePrefix(object __instance)
        {
            try { if (_comparer != null) Swap(__instance, _comparer); }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------
        // 4. Atmosphere
        private const string AtmosType = "TheForestAtmosphere";
        private MethodInfo _atmosUpdate;
        private static int _atmosReplaced;
        public static readonly Vector3[] CornersA = new Vector3[4];
        public static readonly Vector3[] CornersB = new Vector3[4];

        private string ApplyAtmosphere()
        {
            Type t = GameType(AtmosType);
            if (t == null) return AtmosType + " not found";
            _atmosUpdate = t.GetMethod("UpdateShaderParameters", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (_atmosUpdate == null) return "UpdateShaderParameters not found";
            _atmosReplaced = 0;
            _harmony.Patch(_atmosUpdate, transpiler: new HarmonyMethod(typeof(PerfPatches).GetMethod("AtmosTranspiler", BindingFlags.Static | BindingFlags.NonPublic)));
            if (_atmosReplaced == 2) return "";
            // Not the code this was written for: the game's own code back.
            _harmony.Unpatch(_atmosUpdate, HarmonyPatchType.Transpiler, _harmony.Id);
            return "expected 2 new Vector3[4], found " + _atmosReplaced + " (game updated?)";
        }

        private void RemoveAtmosphere()
        {
            if (_atmosUpdate != null) _harmony.Unpatch(_atmosUpdate, HarmonyPatchType.Transpiler, _harmony.Id);
        }

        // `ldc.i4.4; newarr Vector3` -> `ldsfld CornersA` (then CornersB).
        private static IEnumerable<CodeInstruction> AtmosTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> list = new List<CodeInstruction>(instructions);
            FieldInfo[] kept = { typeof(PerfPatches).GetField("CornersA"), typeof(PerfPatches).GetField("CornersB") };
            int found = 0;
            for (int i = 0; i + 1 < list.Count; i++)
            {
                if (list[i].opcode != OpCodes.Ldc_I4_4 || list[i + 1].opcode != OpCodes.Newarr) continue;
                if (!ReferenceEquals(list[i + 1].operand, typeof(Vector3))) continue;
                if (found < kept.Length)
                {
                    // Keep the first instruction's labels on the load.
                    list[i].opcode = OpCodes.Ldsfld;
                    list[i].operand = kept[found];
                    list[i + 1].opcode = OpCodes.Nop;
                    list[i + 1].operand = null;
                }
                found++;
            }
            _atmosReplaced = found;
            return list;
        }
    }

    /// Marks our comparer, whatever its enum.
    public interface IIntEnumComparer { }

    /// Compares an int-based enum by value without boxing it. The IL for
    /// "return the argument" reads an enum as its int.
    public sealed class IntEnumComparer<T> : IEqualityComparer<T>, IIntEnumComparer
    {
        private readonly Func<T, int> _value;

        public IntEnumComparer()
        {
            DynamicMethod dm = new DynamicMethod("EnumValue", typeof(int), new[] { typeof(T) }, typeof(IntEnumComparer<T>).Module, true);
            ILGenerator il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ret);
            _value = (Func<T, int>)dm.CreateDelegate(typeof(Func<T, int>));
        }

        public bool Equals(T a, T b) { return _value(a) == _value(b); }
        public int GetHashCode(T v) { return _value(v); }
    }
}
