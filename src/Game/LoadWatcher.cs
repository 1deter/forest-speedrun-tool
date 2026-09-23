using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Sees EVERY load finish - menu load, quick-load, load restore - by the
    // game's own flag, not by whoever started it.
    //
    // TheForest.Utils.Scene.FinishGameLoad (IL): cleared by LoadSave.Awake
    // when the game scene starts, and by ClearStaticVars.Awake in the title
    // scene; set when LoadSave's "Game Activation Sequence" ends. A rising
    // edge after a falling one = a load finished.
    //
    // Polled a few times a second: reading the static boxes a bool, and a
    // load's end does not need frame accuracy.
    // ------------------------------------------------------------------
    public sealed class LoadWatcher
    {
        private const float PollInterval = 0.2f;

        private FieldInfo _flag;
        private bool _resolved;
        private bool _last = true;
        private float _nextPoll;
        private int _sceneAtStart = -1;

        public int Loads { get; private set; }

        /// True when the last load started in the title scene (a menu load)
        /// rather than as a reload of the game scene over itself.
        public bool LastFromOtherScene { get; private set; }

        /// True for exactly the Tick in which a load was seen to finish.
        public bool Tick()
        {
            if (Time.unscaledTime < _nextPoll) return false;
            _nextPoll = Time.unscaledTime + PollInterval;

            if (!_resolved)
            {
                _resolved = true;
                Type scene = GameBridge.FindGameType("TheForest.Utils.Scene");
                if (scene != null) _flag = scene.GetField("FinishGameLoad", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            }
            if (_flag == null) return false;

            bool now;
            try { now = (bool)_flag.GetValue(null); }
            catch (Exception) { return false; }

            bool finished = false;
            if (!now && _last) _sceneAtStart = SceneManager.GetActiveScene().buildIndex;
            if (now && !_last)
            {
                Loads++;
                LastFromOtherScene = _sceneAtStart != SceneManager.GetActiveScene().buildIndex;
                finished = true;
            }
            _last = now;
            return finished;
        }
    }
}
