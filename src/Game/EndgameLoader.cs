using System;
using System.Reflection;
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
                force.Invoke(trigger, null);
                return "endgame: loaded at capture, not by the load - loading it (the game's EndgameLoader)";
            }
            catch (Exception ex)
            {
                return "endgame: loading it failed (" + (ex.InnerException ?? ex).Message + ")";
            }
        }
    }
}
