using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Loads the endgame area after a load restore that left it out (runner:
    // a savestate in the invisible section after the lab, restored with a
    // load, had no endgame textures or floor - "I just fall through the
    // map"; in place was fine).
    //
    // WHY (IL + the runner's log, v0.24.25): the endgame is one additive
    // scene, endgame_streaming (+ endgame_animPrefabs), loaded by the
    // SceneLoadTrigger tagged "EndgameLoader" (EndgameEntrance/LoadEndgame)
    // when the player crosses it, and after a load by
    // LoadSave.Activation - only when ActiveAreaInfo.HasActiveEndgameArea,
    // which needs an active area hash (Area.GetActiveAreaHash at save).
    // Out of bounds in the invisible section no area is active (the runners
    // skip its triggers on purpose), so the load never loads the endgame:
    // the area report read "endgame yes | endgame_streaming" at capture and
    // "endgame no", without it, after every load restore.
    //
    // WHAT: after a load restore whose capture had endgame_streaming loaded
    // and it is not loaded now, the game's own SetCanLoad(true) +
    // ForceLoad() on that trigger.
    //
    // A Quick load loads no scenes: on a save that had not opened the vault
    // door, a spot past it dropped the player through the world (maks,
    // 2026-09-26; bridge: ForceUnload, Quick load in the red elevator =
    // falling at 55 m/s). Since v0.24.85 the Quick load loads the endgame
    // first, waits for it, then restores (SavestateModule.RestoreInPlace).
    // ------------------------------------------------------------------
    public static class EndgameLoader
    {
        private const string Scene = "endgame_streaming";
        private const string AnimScene = "endgame_animPrefabs";

        /// The capture had the endgame and it is not loaded now.
        public static bool Needed(string capturedAreas)
        {
            // Either scene: a capture during the trigger's own load (the
            // vault door opening, v0.24.82) had endgame_animPrefabs and not
            // yet endgame_streaming - the Full load then left both out and
            // the hold waited 30 s for the first.
            if (string.IsNullOrEmpty(capturedAreas) ||
                (capturedAreas.IndexOf(Scene, StringComparison.Ordinal) < 0 &&
                 capturedAreas.IndexOf(AnimScene, StringComparison.Ordinal) < 0)) return false;
            try { return !SceneManager.GetSceneByName(Scene).isLoaded; }
            catch (Exception) { return false; }
        }

        /// endgame_streaming is loaded, and no scene is still loading.
        public static bool Settled()
        {
            try
            {
                if (!SceneManager.GetSceneByName(Scene).isLoaded) return false;
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    if (!SceneManager.GetSceneAt(i).isLoaded) return false;
                return true;
            }
            catch (Exception) { return false; }
        }

        /// "" when there is nothing to do, else a note for the log line.
        public static string EnsureLoaded(string capturedAreas)
        {
            if (!Needed(capturedAreas)) return "";
            try
            {

                GameObject go = GameObject.FindWithTag("EndgameLoader");
                if (go == null) return "endgame: loaded at capture, not now - no EndgameLoader found";
                Component trigger = null;
                Component[] cs = go.GetComponents<Component>();
                for (int i = 0; i < cs.Length; i++)
                    if (cs[i] != null && cs[i].GetType().Name == "SceneLoadTrigger") trigger = cs[i];
                if (trigger == null) return "endgame: loaded at capture, not now - the EndgameLoader has no SceneLoadTrigger";

                BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                MethodInfo canLoad = trigger.GetType().GetMethod("SetCanLoad", inst, null, new[] { typeof(bool) }, null);
                MethodInfo force = trigger.GetType().GetMethod("ForceLoad", inst, null, Type.EmptyTypes, null);
                if (force == null) return "endgame: loaded at capture, not now - ForceLoad not found";
                if (canLoad != null) canLoad.Invoke(trigger, new object[] { true });
                bool background = _streamPatched && _forRestores;
                // The trigger loads _loadDelay (0.5 s) after ForceLoad; the
                // window marks the load as ours (restore), not a run's.
                if (_streamPatched) _asyncUntil = Time.realtimeSinceStartup + 3f;
                force.Invoke(trigger, null);
                return "endgame: loaded at capture, not by the load - loading it (the game's EndgameLoader" +
                       (background ? ", in the background" : "") + ")";
            }
            catch (Exception ex)
            {
                _asyncUntil = -1f;
                return "endgame: loading it failed (" + (ex.InnerException ?? ex).Message + ")";
            }
        }

        // ------------------------------------------------------------------
        // The endgame in the background for our restores (Performance
        // patch EndgameAsyncForRestores; author, 2026-09-26: "do it if the
        // player will not notice").
        //
        // WHY (IL + bridge, 2026-09-26): SceneLoadTrigger.StreamSceneRoutine
        // loads endgame_streaming with the synchronous SceneManager.LoadScene
        // - one frame of 5.2 s (5158-5233 ms in four loads) after every Full
        // load of a state captured with the endgame, and before a Quick load
        // that needs it. The same load by LoadSceneAsync from the same
        // unloaded state: no frame reached 200 ms. Our restores hold the
        // player until the scene is in (HoldUntilLoaded, EndgameFirst), so
        // nothing else changes.
        //
        // WHAT: a transpiler on the routine swaps two instructions -
        //   call SceneManager.LoadScene(string, LoadSceneMode) -> StreamLoad
        //   ldnull (the yield after it)                        -> StreamYield()
        // StreamLoad loads async only when EnsureLoaded asked just before
        // (a 3 s window for the trigger's 0.5 s delay) and the scene is the
        // endgame's; otherwise it calls LoadScene as the game does. So the
        // game's own trigger crossing in a run is untouched. StreamYield
        // hands the routine a wait on that load (null otherwise, as the
        // game's code), so everything after it in the routine -
        // sceneLoaded -> _loadedSceneRoot, _onFinishedLoading, then
        // loadEndBossScene and the loading HUD - runs in the same order,
        // once the scene is in. While it runs, backgroundLoadingPriority
        // is High (the player is held anyway), then set back.
        //
        // IN A RUN (Experimental switch EndgameAsyncInRuns, off; author,
        // 2026-09-26: allowed, off, "only if a genuine improvement"). The
        // game's own load in a run (live wiring + bridge, 2026-09-26): the
        // forward crossing of LoadEndgame sends EnterEndgame (IsInEndgame);
        // the vault door's onDoorOpen then starts DelayedLoad (4.35 s,
        // PlayerInEndgameTester) and SetCanLoad(true); DelayedLoad's
        // ForceLoad runs the routine, 0.5 s, then the one frame - measured
        // 5078 ms, ~4.9 s into the door's cutscene, which lasts ~12 s of
        // game time (the flag fell 17.3 s after the press, freeze included).
        // Time.maximumDeltaTime is 9 in this game, so that frame counts as
        // ~5 s of game time: the cutscene runs on inside the freeze and ends
        // at the same real time (17.3 s after the press with the freeze,
        // 16.7 s with the async load) - the switch removes a frozen picture,
        // not run time. Async measured: 1.09 s, 293 frames, longest 12 ms,
        // all inside the cutscene, no hold.
        // With the switch every endgame_streaming load that is not ours goes
        // async; the rest of the routine is unchanged, as for restores. If
        // the load outlasts the cutscene (or starts outside one - a save
        // loaded into the endgame), the player is pinned where they stand
        // until it is in, as HoldUntilLoaded does.
        // ------------------------------------------------------------------
        public static ManualLogSource Log;
        private static bool _streamPatched;
        private static bool _forRestores, _inRuns;
        private static MethodInfo _streamMoveNext;
        private static int _streamReplaced;
        private static float _asyncUntil = -1f;
        private static AsyncOperation _asyncOp;
        private static ThreadPriority _priorityBefore;

        /// Loading priority while our background load runs (bridge: set it
        /// to compare).
        public static ThreadPriority AsyncPriority = ThreadPriority.High;

        /// Either switch: restores (inRuns false) or runs (true). The
        /// transpiler is shared and stays while either is on.
        public static string PatchStream(Harmony harmony, ManualLogSource log, bool inRuns)
        {
            Log = log;
            if (!_streamPatched)
            {
                string why = Patch(harmony);
                if (why.Length > 0) return why;
            }
            if (inRuns) _inRuns = true;
            else _forRestores = true;
            return "";
        }

        private static string Patch(Harmony harmony)
        {
            Type trigger = GameBridge.FindGameType("TheForest.World.SceneLoadTrigger");
            if (trigger == null) return "SceneLoadTrigger not found";
            Type iter = null;
            Type[] nested = trigger.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public);
            for (int i = 0; i < nested.Length; i++)
                if (nested[i].Name.IndexOf("StreamSceneRoutine", StringComparison.Ordinal) >= 0) iter = nested[i];
            if (iter == null) return "StreamSceneRoutine not found";
            _streamMoveNext = iter.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (_streamMoveNext == null) return "StreamSceneRoutine.MoveNext not found";
            _streamReplaced = 0;
            harmony.Patch(_streamMoveNext, transpiler: new HarmonyMethod(typeof(EndgameLoader).GetMethod("StreamTranspiler", BindingFlags.Static | BindingFlags.NonPublic)));
            if (_streamReplaced == 2) { _streamPatched = true; return ""; }
            // Not the code this was written for: the game's own code back.
            harmony.Unpatch(_streamMoveNext, HarmonyPatchType.Transpiler, harmony.Id);
            return "expected LoadScene and its yield, found " + _streamReplaced + " of 2 (game updated?)";
        }

        public static void UnpatchStream(Harmony harmony, bool inRuns)
        {
            if (inRuns) _inRuns = false;
            else _forRestores = false;
            if (_inRuns || _forRestores || !_streamPatched) return;
            _streamPatched = false;
            _asyncUntil = -1f;
            if (_streamMoveNext != null) harmony.Unpatch(_streamMoveNext, HarmonyPatchType.Transpiler, harmony.Id);
        }

        private static IEnumerable<CodeInstruction> StreamTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> list = new List<CodeInstruction>(instructions);
            MethodInfo load = typeof(SceneManager).GetMethod("LoadScene", new[] { typeof(string), typeof(LoadSceneMode) });
            int found = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].opcode != OpCodes.Call || !ReferenceEquals(list[i].operand, load)) continue;
                list[i].operand = typeof(EndgameLoader).GetMethod("StreamLoad");
                found++;
                // The yield that follows: `ldnull; stfld $current`.
                for (int j = i + 1; j + 1 < list.Count; j++)
                {
                    FieldInfo f = list[j + 1].operand as FieldInfo;
                    if (list[j].opcode != OpCodes.Ldnull || list[j + 1].opcode != OpCodes.Stfld || f == null || f.Name != "$current") continue;
                    // Labels stay on the instruction.
                    list[j].opcode = OpCodes.Call;
                    list[j].operand = typeof(EndgameLoader).GetMethod("StreamYield");
                    found++;
                    break;
                }
                break;
            }
            _streamReplaced = found;
            return list;
        }

        /// In place of the routine's SceneManager.LoadScene.
        public static void StreamLoad(string name, LoadSceneMode mode)
        {
            bool restore = false, async = false;
            if (name == Scene && _streamPatched)
            {
                restore = Time.realtimeSinceStartup <= _asyncUntil;
                _asyncUntil = -1f;
                async = restore ? _forRestores : _inRuns;
            }
            if (async)
            {
                try
                {
                    _priorityBefore = Application.backgroundLoadingPriority;
                    Application.backgroundLoadingPriority = AsyncPriority;
                    _asyncOp = SceneManager.LoadSceneAsync(name, mode);
                    _asyncForRun = !restore;
                    if (_asyncOp != null) return;
                    Application.backgroundLoadingPriority = _priorityBefore;
                }
                catch (Exception)
                {
                    _asyncOp = null;
                    Application.backgroundLoadingPriority = _priorityBefore;
                }
            }
            SceneManager.LoadScene(name, mode);
        }

        /// In place of the routine's `yield return null` after the load: the
        /// load itself (a coroutine waits on it), or null as the game's code.
        public static object StreamYield()
        {
            AsyncOperation op = _asyncOp;
            _asyncOp = null;
            if (op == null) return null;
            _watched = op;
            _watchForRun = _asyncForRun;
            _watchStart = Time.realtimeSinceStartup;
            _watchFrames = 0;
            _watchLongest = 0f;
            _cutsceneFrames = 0;
            _holding = false;
            return op;
        }

        private static AsyncOperation _watched;
        private static bool _asyncForRun, _watchForRun;
        private static float _watchStart, _watchLongest;
        private static int _watchFrames, _cutsceneFrames;

        // The run's hold: where the player was pinned, since when.
        private static bool _holding;
        private static Vector3 _holdAt;
        private static Quaternion _holdRotation;
        private static float _holdStart;

        /// Once a frame (PerfPatches.Tick): times the background load, holds
        /// the player of a run's load outside a cutscene, sets the loading
        /// priority back when it is done.
        public static void Tick(PlayerRef player, GameEvents events)
        {
            if (_watched == null) return;
            _watchFrames++;
            if (Time.unscaledDeltaTime > _watchLongest) _watchLongest = Time.unscaledDeltaTime;
            bool cutscene = events != null && events.CutsceneRunning != null;
            if (cutscene) _cutsceneFrames++;
            bool done;
            try { done = _watched.isDone; }
            catch (Exception) { done = true; }
            if (!done && Time.realtimeSinceStartup - _watchStart < 60f)
            {
                if (_watchForRun) Hold(player, cutscene);
                return;
            }
            _watched = null;
            Application.backgroundLoadingPriority = _priorityBefore;
            if (Log == null) return;
            string took = (Time.realtimeSinceStartup - _watchStart).ToString("0.00") + " s, " + _watchFrames +
                          " frame(s), longest " + (_watchLongest * 1000f).ToString("0") + " ms (" + AsyncPriority + ")";
            string gaveUp = done ? "" : " - gave up watching after 60 s";
            if (!_watchForRun)
            {
                Log.LogInfo("Performance: endgame loaded in the background for the restore - " + took + gaveUp + ".");
                return;
            }
            string held = _holding
                ? "held the player " + (Time.realtimeSinceStartup - _holdStart).ToString("0.00") + " s at " + Fmt(_holdAt) + " until it was in"
                : "no hold needed";
            Log.LogInfo("Performance: endgame loaded in the background in play (Experimental) - " + took + ", " +
                        _cutsceneFrames + " of the frames in a cutscene, " + held + gaveUp + ".");
            _holding = false;
        }

        /// A cutscene holds the player itself; outside one, pin them where
        /// they stood when the hold began (the floor may not be in yet).
        private static void Hold(PlayerRef player, bool cutscene)
        {
            if (cutscene || player == null || !player.Found) return;
            if (!_holding)
            {
                _holding = true;
                _holdAt = player.Transform.position;
                _holdRotation = player.Transform.rotation;
                _holdStart = Time.realtimeSinceStartup;
            }
            player.MoveTo(_holdAt, _holdRotation);
        }

        private static string Fmt(Vector3 v)
        {
            return "(" + v.x.ToString("0.0") + ", " + v.y.ToString("0.0") + ", " + v.z.ToString("0.0") + ")";
        }
    }
}
