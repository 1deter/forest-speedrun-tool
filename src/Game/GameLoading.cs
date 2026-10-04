using System;
using System.Reflection;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Is the game loading right now? For load-removed time (Data/LoadTimes,
    // docs/run-mode.md *Load-removed time*). The game's own load state, no
    // timing guesses:
    //
    //   LevelSerializer.LevelLoadingOperation (static AsyncOperation, IL):
    //     set by LevelSerializer.LoadSavedLevel to its
    //     SceneManager.LoadSceneAsync - every save load: the title screen's
    //     Continue (via LoadSave.Awake -> Resume), Reload save on death's
    //     Resume from in game, a Full load restore - and set back to null by
    //     LevelLoader's Load coroutine once the scene is done. The loading
    //     screen's progress bar (TheForest.UI.LoadingProgress.Update) reads
    //     it. It covers the scene load before the new scene's LoadSave.Awake,
    //     which FinishGameLoad does not.
    //   TheForest.Utils.Scene.FinishGameLoad (static bool): cleared by
    //     LoadSave.Awake when the game scene starts (and in the title scene),
    //     set when LoadSave's activation sequence ends. The LiveSplit
    //     autosplitter's "loaded" (its reset is !loaded).
    //
    // Loading = the operation is under way (set and not done - a set one the
    // game failed to clear never reads as a load forever), or the game has
    // not finished loading. The two overlap: LoadSave.Awake clears
    // FinishGameLoad while the operation activates the scene.
    // Not loads: cave streaming and the endgame's streamed scenes (additive,
    // neither field moves, no loading screen), the cave door fade
    // (CaveTriggers.CaveDoorRoutine: a fixed 1.5 s fade), Quick load
    // (LoadNow in place, no scene load).
    //
    // Read once a frame (cached by frame) with garbage-free getters.
    // Missing fields (a game update) = never loading, said by Status.
    // ------------------------------------------------------------------
    public static class GameLoading
    {
        private static bool _resolved;
        private static Func<bool> _finished;       // Scene.FinishGameLoad
        private static Func<object> _operation;    // LevelSerializer.LevelLoadingOperation
        private static int _frame = -1;
        private static bool _now;

        /// What was found: "FinishGameLoad + LevelLoadingOperation", or what is missing.
        public static string Status { get; private set; }

        /// The game is loading (this frame).
        public static bool Now
        {
            get
            {
                int f = Time.frameCount;
                if (f == _frame) return _now;
                _frame = f;
                _now = Read();
                return _now;
            }
        }

        private static bool Read()
        {
            if (!_resolved) Resolve();
            try
            {
                if (_operation != null)
                {
                    AsyncOperation op = _operation() as AsyncOperation;
                    if (op != null && !op.isDone) return true;
                }
                if (_finished != null && !_finished()) return true;
            }
            catch (Exception) { }
            return false;
        }

        /// Binds the fields now and returns Status (for the startup log line).
        public static string Resolve()
        {
            if (_resolved) return Status;
            _resolved = true;
            const BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            string missing = "";
            Type scene = GameBridge.FindGameType("TheForest.Utils.Scene");
            FieldInfo fin = scene != null ? scene.GetField("FinishGameLoad", stat) : null;
            if (fin != null && fin.FieldType == typeof(bool)) _finished = FastField.Static<bool>(fin);
            else missing += " Scene.FinishGameLoad";
            Type ls = GameBridge.FindGameType("LevelSerializer");
            FieldInfo op = ls != null ? ls.GetField("LevelLoadingOperation", stat) : null;
            if (op != null && !op.FieldType.IsValueType) _operation = FastField.Static<object>(op);
            else missing += " LevelSerializer.LevelLoadingOperation";
            Status = missing.Length == 0 ? "FinishGameLoad + LevelLoadingOperation" : "missing:" + missing;
            return Status;
        }
    }
}
