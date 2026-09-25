using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Skips the game's own cannibal family setup after a Full load, so the
    // captured families can be rebuilt at once (runner maks: cannibals
    // came back ~6 s after control returned).
    //
    // WHY (IL + bridge, v0.24.45): a load runs mutantController.Start ->
    // Invoke("doStart", 2) -> startSetupFamilies -> setupFamilies, which
    // sets setupBreak for 1 s (a second setup meanwhile does nothing) and
    // starts updateSpawns, which waits 3 x 1 s before it rolls a family.
    // The rebuild waited for that family (3.8 s after "in game", bridge),
    // then the lock (1.5 s), then ran the setup again itself - throwing
    // the game's families away - and placed the members: ~7.3 s.
    //
    // WHAT: for ArmedFor seconds from a Full load's "in game" (Arm), a
    // prefix skips startSetupFamilies and records when the game asked
    // (Requested). The rebuild runs the setup itself right then, through
    // RunOwn, which passes. The window stays open after the rebuild so a
    // later call of the game's cannot throw the rebuilt families away;
    // every skip is logged. Every caller is held alike (doStart,
    // NotInACave); the rebuild's own run is the game's setup, so nothing
    // is lost. Release closes the window (the game's setup slipped
    // through, or the rebuild cannot run). Nothing changes outside it.
    // ------------------------------------------------------------------
    public sealed class SetupHold
    {
        private const float ArmedFor = 10f;

        private static ManualLogSource _log;
        private static float _armedAt = -1000f;
        private static bool _armed;
        private static float _requestedAt = -1f;
        private static bool _passing;
        private static int _skipped;
        private static string _skips = "";

        private Harmony _harmony;
        public string Status { get; private set; }

        /// The game called startSetupFamilies while armed (and was skipped).
        public static bool Requested { get { return _requestedAt >= 0f; } }

        /// Seconds from Arm to the game's first skipped call; -1 if none.
        public static float RequestedAfter { get { return _requestedAt >= 0f ? _requestedAt - _armedAt : -1f; } }

        public SetupHold(ManualLogSource log)
        {
            _log = log;
            Status = "not installed";
        }

        public void Install(string harmonyId)
        {
            Type ctrl = GameBridge.FindGameType("mutantController");
            MethodInfo start = ctrl != null
                ? ctrl.GetMethod("startSetupFamilies", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
                : null;
            if (start == null)
            {
                Status = "mutantController.startSetupFamilies not found";
                _log.LogWarning("SetupHold: " + Status);
                return;
            }
            try
            {
                _harmony = new Harmony(harmonyId + ".setuphold");
                _harmony.Patch(start, new HarmonyMethod(typeof(SetupHold).GetMethod("StartPrefix", BindingFlags.Static | BindingFlags.NonPublic)));
                Status = "hooked";
            }
            catch (Exception ex) { Status = "Harmony: " + ex.Message; }
            _log.LogInfo("SetupHold: " + Status + ".");
        }

        public void Uninstall()
        {
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception) { }
        }

        /// A Full load reached "in game" and will rebuild the captured
        /// families: hold the game's own setup until Release.
        public static void Arm()
        {
            _armed = true;
            _armedAt = Time.realtimeSinceStartup;
            _requestedAt = -1f;
            _skipped = 0;
            _skips = "";
        }

        /// Runs the setup the rebuild wants, past the hold.
        public static void RunOwn(Action call)
        {
            _passing = true;
            try { call(); }
            finally { _passing = false; }
        }

        /// Calls skipped since Arm, with their times: "2 (0.8 s, 3.1 s)".
        public static string Skips()
        {
            return _skipped + (_skipped > 0 ? " (" + _skips + ")" : "");
        }

        public static int Skipped { get { return _skipped; } }

        /// Seconds left in the window; 0 when closed.
        public static float Left()
        {
            return Holding() ? ArmedFor - (Time.realtimeSinceStartup - _armedAt) : 0f;
        }

        /// Close the window: the game's setup ran, or no rebuild follows.
        public static void Release()
        {
            _armed = false;
        }

        private static bool Holding()
        {
            if (!_armed) return false;
            if (Time.realtimeSinceStartup - _armedAt <= ArmedFor) return true;
            _armed = false;
            return false;
        }

        // false = skip the original. Never throws into the game.
        private static bool StartPrefix()
        {
            try
            {
                if (_passing || !Holding()) return true;
                float now = Time.realtimeSinceStartup;
                if (_requestedAt < 0f) _requestedAt = now;
                _skipped++;
                _skips += (_skips.Length > 0 ? ", " : "") + (now - _armedAt).ToString("0.0") + " s";
                return false;
            }
            catch (Exception) { return true; }
        }
    }
}
