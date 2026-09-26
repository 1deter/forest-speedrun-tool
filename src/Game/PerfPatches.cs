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
    // 9. The endgame load in play in the background - EXPERIMENTAL, off:
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
    //    Ships off until an A/B shows the sweep frees nothing that matters.
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

        public PerfPatches(ManualLogSource log, ConfigFile config, string harmonyId)
        {
            _log = log;
            _harmony = new Harmony(harmonyId + ".perf");

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
            Add(config, "EndgameAsyncInRuns", "Endgame: load it in the background during play (vault door)",
                "EXPERIMENTAL, changes the game: the game's own load of the endgame area after the vault door opens runs in the background " +
                "during the door's cutscene, instead of freezing the picture for one ~5 s frame. The cutscene ends at the same moment either " +
                "way (the game counts the frozen frame as time passed), so a run's time does not change. If the load outlasts the cutscene " +
                "you are held in place until it is in. Off = the game's own code.",
                delegate { return EndgameLoader.PatchStream(_harmony, _log, true); }, delegate { EndgameLoader.UnpatchStream(_harmony, true); }, false);
            _fixes[_fixes.Count - 1].Experimental = true;
            _fixes[_fixes.Count - 1].Note = "Changes how the game runs that moment: the door's cutscene plays smoothly instead of freezing " +
                                            "for ~5 s. A run's time is the same (the cutscene ends when it would). You are held in place " +
                                            "if the load outlasts the cutscene.";
            Add(config, "SkipEndgameAnimSweep", "Loads: skip the endgame-animation clean-up",
                "Every save load, the game starts unloading three endgame animations and, in the same frame, walks every loaded asset " +
                "to free unused ones (~1 s, one frame of 0.5-0.8 s) - before the animations are actually unloaded, so that walk cannot " +
                "free them. Skip that one walk; the animations are unloaded as before. Memory only; off = the game's own code.",
                ApplyAnimSweep, RemoveAnimSweep, false);

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
            Set(f, f.Cfg.Value);
        }

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
