using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Keeps a fast-forwarded cutscene's sounds in step with its picture
    // (runner maks: after a restore into Megan's transformation, her
    // transformation audio played on late, into the fight).
    //
    // WHY (IL, v0.24.36): the fast-forward raises timeScale; FMOD plays in
    // real time. Every sound the skipped part of the cutscene starts
    // (animation events -> FMOD_AnimationEventHandler.playFMODEvent,
    // FMODCommon.PlayOneshot, emitters) begins at its own start, so a
    // 50 s skip leaves anything long up to 50 s behind the picture, all of
    // it squeezed into a second or two. FMODCommon.CleanupOneshotEvents
    // stops a timed-out one-shot when `Time.time - startTime > 10` - game
    // time, so it already agrees with a normal run.
    //
    // WHAT: while a fast-forward runs (Begin .. End), a postfix on FMOD's
    // EventInstance.start() notes each started instance with the game time
    // and mutes it. At the captured moment (End): a one-shot whose length
    // has run out in game time is stopped; any other is moved to where a
    // normal run would be (setTimelinePosition = its game-time age) and
    // unmuted; looping ones (no length) are only unmuted. One log line.
    // ------------------------------------------------------------------
    public static class CutsceneAudio
    {
        private sealed class Started
        {
            public object Instance;
            public float At;
            public float Volume;
        }

        private static ManualLogSource _log;
        private static Harmony _harmony;
        private static bool _active;
        private static readonly List<Started> _started = new List<Started>();

        private static MethodInfo _getVolume, _setVolume, _getDescription, _getPlaybackState, _setTimeline, _stop;
        private static MethodInfo _getLength, _getPath;
        private static object _stopImmediate;

        public static string Status { get; private set; }

        public static void Install(ManualLogSource log, string harmonyId)
        {
            _log = log;
            try
            {
                Type inst = GameBridge.FindGameType("FMOD.Studio.EventInstance");
                Type desc = GameBridge.FindGameType("FMOD.Studio.EventDescription");
                Type stopMode = GameBridge.FindGameType("FMOD.Studio.STOP_MODE");
                if (inst == null || desc == null || stopMode == null) { Status = "FMOD types not found"; Warn(); return; }

                BindingFlags pub = BindingFlags.Instance | BindingFlags.Public;
                MethodInfo start = inst.GetMethod("start", pub, null, Type.EmptyTypes, null);
                _getVolume = inst.GetMethod("getVolume", pub);
                _setVolume = inst.GetMethod("setVolume", pub);
                _getDescription = inst.GetMethod("getDescription", pub);
                _getPlaybackState = inst.GetMethod("getPlaybackState", pub);
                _setTimeline = inst.GetMethod("setTimelinePosition", pub);
                _stop = inst.GetMethod("stop", pub);
                _getLength = desc.GetMethod("getLength", pub);
                _getPath = desc.GetMethod("getPath", pub);
                _stopImmediate = Enum.Parse(stopMode, "IMMEDIATE");
                if (start == null || _getVolume == null || _setVolume == null || _getDescription == null ||
                    _getPlaybackState == null || _setTimeline == null || _stop == null || _getLength == null)
                {
                    Status = "FMOD methods not found";
                    Warn();
                    return;
                }

                _harmony = new Harmony(harmonyId + ".cutsceneaudio");
                _harmony.Patch(start, postfix: new HarmonyMethod(typeof(CutsceneAudio).GetMethod("StartPostfix", BindingFlags.Static | BindingFlags.NonPublic)));
                Status = "hooked";
                _log.LogInfo("CutsceneAudio: hooked.");
            }
            catch (Exception ex)
            {
                Status = "Harmony: " + ex.Message;
                Warn();
            }
        }

        private static void Warn()
        {
            if (_log != null) _log.LogWarning("CutsceneAudio: " + Status + " - a fast-forwarded cutscene's sounds stay as the game plays them.");
        }

        public static void Uninstall()
        {
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception) { }
        }

        /// The fast-forward starts: note and mute every sound from here.
        public static void Begin()
        {
            _started.Clear();
            _active = Status == "hooked";
        }

        // Never throws into the game; cheap when no fast-forward runs.
        private static void StartPostfix(object __instance)
        {
            if (!_active || __instance == null) return;
            try
            {
                object[] a = { 0f };
                _getVolume.Invoke(__instance, a);
                Started s = new Started { Instance = __instance, At = Time.time, Volume = (float)a[0] };
                _setVolume.Invoke(__instance, new object[] { 0f });
                _started.Add(s);
            }
            catch (Exception) { }
        }

        /// The captured moment is reached: put every sound where a normal
        /// run would have it. Returns the log note ("" when none started).
        public static string End()
        {
            if (!_active) return "";
            _active = false;
            int moved = 0, over = 0, looping = 0, gone = 0;
            List<string> names = new List<string>();
            for (int i = 0; i < _started.Count; i++)
            {
                Started s = _started[i];
                try
                {
                    object[] st = { null };
                    _getPlaybackState.Invoke(s.Instance, st);
                    // PLAYBACK_STATE: PLAYING 0, SUSTAINING 1, STOPPED 2, STARTING 3, STOPPING 4.
                    int state = st[0] != null ? Convert.ToInt32(st[0]) : 2;
                    if (state == 2 || state == 4) { gone++; continue; }

                    object[] d = { null };
                    _getDescription.Invoke(s.Instance, d);
                    int length = 0;
                    string path = "?";
                    if (d[0] != null)
                    {
                        object[] l = { 0 };
                        _getLength.Invoke(d[0], l);
                        length = (int)l[0];
                        if (_getPath != null)
                        {
                            object[] p = { null };
                            _getPath.Invoke(d[0], p);
                            if (p[0] is string) path = (string)p[0];
                        }
                    }

                    int age = (int)((Time.time - s.At) * 1000f);
                    if (length <= 0)
                    {
                        looping++;
                    }
                    else if (age >= length)
                    {
                        _stop.Invoke(s.Instance, new object[] { _stopImmediate });
                        over++;
                        continue;
                    }
                    else
                    {
                        _setTimeline.Invoke(s.Instance, new object[] { age });
                        moved++;
                        names.Add(Short(path) + "@" + (age / 1000f).ToString("0.0"));
                    }
                    _setVolume.Invoke(s.Instance, new object[] { s.Volume });
                }
                catch (Exception) { }
            }
            int total = _started.Count;
            _started.Clear();
            if (total == 0) return "";
            return "sounds: " + total + " started during it - " + moved + " moved to their place" +
                   (names.Count > 0 ? " (" + string.Join(", ", names.ToArray()) + ")" : "") +
                   ", " + over + " already over, " + gone + " ended by the game, " + looping + " looping";
        }

        // "event:/endgame/sfx_endgame/crying_girl" -> "sfx_endgame/crying_girl"
        private static string Short(string path)
        {
            int a = path.LastIndexOf('/');
            int b = a > 0 ? path.LastIndexOf('/', a - 1) : -1;
            return b >= 0 ? path.Substring(b + 1) : path;
        }
    }
}
