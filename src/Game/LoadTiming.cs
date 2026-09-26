using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Where a load's time goes (author, 2026-09-26: speed up loads - caves
    // and endgame scenes the most). Measure first: one log line per event.
    //
    //   Load timing: UnloadUnusedAssets by SceneLoadTrigger.<UnloadScene>: 2.4 s, 3 frames, longest 2.3 s
    //   Load timing: GC.Collect by SceneLoadTrigger.<UnloadScene>: 96 ms
    //   Load timing: game timer 'Activation': 4.1 s
    //   Load timing: scene 'Cave_IW_Props_Streaming' loaded (additive)
    //   Load timing: hitch 850 ms - during: UnloadUnusedAssets (SceneLoadTrigger.<UnloadScene>)
    //
    // WHAT (IL): every streamed-scene unload (caves, endgame) and the
    // cutscene / cave clean-ups call TheForest.Utils.ResourcesHelper
    // .UnloadUnusedAssets (Resources.UnloadUnusedAssets: walks every loaded
    // object) and most then .GCCollect (a full GC.Collect). The game's own
    // PerfTimerLogger times the load stages but writes to Unity's log,
    // which this build never keeps - its Dispose is read here instead.
    //
    // Read-only: prefixes / postfixes that read the clock and a stack
    // trace (rare calls; the trace allocates, only then). Always on.
    // ------------------------------------------------------------------
    public sealed class LoadTiming
    {
        private const float HitchSeconds = 0.2f;
        private const string Prefix = "Load timing: ";

        private sealed class Pending
        {
            public AsyncOperation Op;
            public string Caller;
            public long Start;
            public int Frames;
            public float Longest;
        }

        private static LoadTiming _instance;
        private readonly ManualLogSource _log;
        private readonly Harmony _harmony;
        private readonly List<Pending> _pending = new List<Pending>();
        // What ran since the last frame (for a hitch line).
        private readonly List<string> _thisFrame = new List<string>();
        private static long _unloadStart;
        private static string _unloadCaller = "";
        private static long _gcStart;
        private static FieldInfo _timerMessage, _timerWatch;

        public string Status { get; private set; }

        public LoadTiming(ManualLogSource log, string harmonyId)
        {
            _log = log;
            _harmony = new Harmony(harmonyId + ".loadtiming");
            Status = "off";
        }

        public void Install()
        {
            _instance = this;
            int n = 0;
            string missing = "";
            Type helper = GameBridge.FindGameType("TheForest.Utils.ResourcesHelper");
            if (helper != null)
            {
                n += Hook(helper, "UnloadUnusedAssets", "UnloadPrefix", "UnloadPostfix", ref missing);
                n += Hook(helper, "GCCollect", "GcPrefix", "GcPostfix", ref missing);
            }
            else missing += " ResourcesHelper";

            Type timer = GameBridge.FindGameType("PerfTimerLogger");
            if (timer != null)
            {
                const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                _timerMessage = timer.GetField("_message", inst);
                _timerWatch = timer.GetField("_timer", inst);
                if (_timerMessage != null && _timerWatch != null) n += Hook(timer, "Dispose", "TimerPrefix", null, ref missing);
                else missing += " PerfTimerLogger fields";
            }
            else missing += " PerfTimerLogger";

            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;

            Status = n + " hook(s)" + (missing.Length > 0 ? ", missing:" + missing : "");
            _log.LogInfo(Prefix + Status + ".");
        }

        public void Uninstall()
        {
            try { _harmony.UnpatchSelf(); }
            catch (Exception) { }
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
        }

        private int Hook(Type t, string method, string prefix, string postfix, ref string missing)
        {
            MethodInfo m = t.GetMethod(method, BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                                       null, Type.EmptyTypes, null);
            if (m == null) { missing += " " + t.Name + "." + method; return 0; }
            try
            {
                _harmony.Patch(m,
                    prefix != null ? new HarmonyMethod(typeof(LoadTiming).GetMethod(prefix, BindingFlags.Static | BindingFlags.NonPublic)) : null,
                    postfix != null ? new HarmonyMethod(typeof(LoadTiming).GetMethod(postfix, BindingFlags.Static | BindingFlags.NonPublic)) : null);
                return 1;
            }
            catch (Exception ex)
            {
                missing += " " + t.Name + "." + method + " (" + ex.Message + ")";
                return 0;
            }
        }

        // ------------------------------------------------------------------
        /// Once a frame.
        public void Tick()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                Pending p = _pending[i];
                p.Frames++;
                if (dt > p.Longest) p.Longest = dt;
                bool done;
                try { done = p.Op == null || p.Op.isDone; }
                catch (Exception) { done = true; }
                if (!done) continue;
                _pending.RemoveAt(i);
                _log.LogInfo(Prefix + "UnloadUnusedAssets by " + p.Caller + ": " + Ms(Stopwatch.GetTimestamp() - p.Start) +
                             ", " + p.Frames + " frame(s), longest " + (p.Longest * 1000f).ToString("0") + " ms");
            }

            if (dt >= HitchSeconds)
                _log.LogInfo(Prefix + "hitch " + (dt * 1000f).ToString("0") + " ms" +
                             (_thisFrame.Count > 0 ? " - during: " + string.Join(", ", _thisFrame.ToArray()) : "") +
                             (_pending.Count > 0 ? " (an asset unload running)" : ""));
            _thisFrame.Clear();
        }

        private void Note(string what)
        {
            if (_thisFrame.Count < 8) _thisFrame.Add(what);
        }

        private static string Ms(long ticks)
        {
            double ms = ticks * 1000.0 / Stopwatch.Frequency;
            return ms >= 1000 ? (ms / 1000.0).ToString("0.00") + " s" : ms.ToString("0") + " ms";
        }

        // The first frame outside the helper, Harmony and us: who asked.
        // A coroutine reads as Outer.<Routine>.
        private static string Caller()
        {
            try
            {
                StackTrace st = new StackTrace(2, false);
                for (int i = 0; i < st.FrameCount; i++)
                {
                    MethodBase m = st.GetFrame(i).GetMethod();
                    if (m == null || m.DeclaringType == null) continue;
                    Type t = m.DeclaringType;
                    if (t == typeof(LoadTiming) || t.Name == "ResourcesHelper") continue;
                    string asm = t.Assembly.GetName().Name;
                    if (asm.StartsWith("0Harmony", StringComparison.Ordinal) || asm.StartsWith("MonoMod", StringComparison.Ordinal) ||
                        asm.StartsWith("HarmonyX", StringComparison.Ordinal)) continue;
                    if (t.Name.StartsWith("<", StringComparison.Ordinal) && t.DeclaringType != null)
                    {
                        int end = t.Name.IndexOf('>');
                        return t.DeclaringType.Name + "." + (end > 0 ? t.Name.Substring(0, end + 1) : t.Name);
                    }
                    return t.Name + "." + m.Name;
                }
            }
            catch (Exception) { }
            return "?";
        }

        // ------------------------------------------------------------------
        private static void UnloadPrefix()
        {
            _unloadCaller = Caller();
            _unloadStart = Stopwatch.GetTimestamp();
        }

        private static void UnloadPostfix(AsyncOperation __result)
        {
            if (_instance == null) return;
            Pending p = new Pending();
            p.Op = __result;
            p.Caller = _unloadCaller;
            p.Start = _unloadStart;
            _instance._pending.Add(p);
            _instance.Note("UnloadUnusedAssets (" + _unloadCaller + ", call " + Ms(Stopwatch.GetTimestamp() - _unloadStart) + ")");
        }

        private static void GcPrefix()
        {
            _gcStart = Stopwatch.GetTimestamp();
        }

        private static void GcPostfix()
        {
            if (_instance == null) return;
            string ms = Ms(Stopwatch.GetTimestamp() - _gcStart);
            string caller = Caller();
            _instance._log.LogInfo(Prefix + "GC.Collect by " + caller + ": " + ms);
            _instance.Note("GC.Collect (" + caller + ", " + ms + ")");
        }

        private static void TimerPrefix(object __instance)
        {
            if (_instance == null || __instance == null) return;
            try
            {
                string msg = _timerMessage.GetValue(__instance) as string ?? "?";
                Stopwatch sw = _timerWatch.GetValue(__instance) as Stopwatch;
                if (sw == null) return;
                msg = msg.Replace("[<color=#FFF>TIMER</color>] ", "");
                _instance._log.LogInfo(Prefix + "game timer '" + msg + "': " + Ms(sw.ElapsedTicks));
            }
            catch (Exception) { }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _log.LogInfo(Prefix + "scene '" + scene.name + "' loaded" + (mode == LoadSceneMode.Additive ? " (additive)" : ""));
            Note("scene '" + scene.name + "' loaded");
        }

        private void OnSceneUnloaded(Scene scene)
        {
            _log.LogInfo(Prefix + "scene '" + scene.name + "' unloaded");
            Note("scene '" + scene.name + "' unloaded");
        }
    }
}
