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
    //   Load timing: activation steps: lists 310 ms/41f, 0.6 s wait 602 ms/1f, nav update 1.21 s/87f
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

            Type act = ActivationIterator();
            if (act != null)
            {
                _actPc = act.GetField("$PC", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (_actPc != null) n += Hook(act, "MoveNext", "ActPrefix", "ActPostfix", ref missing);
                else missing += " Activation $PC";
            }
            else missing += " LoadSave.Activation";

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

        // ------------------------------------------------------------------
        // The save load's last stage, LoadSave.Activation, step by step: one
        // line when it ends, each state of the iterator ($PC = where it
        // resumes) with its time and frames, e.g.
        //   Load timing: activation steps: lists 310 ms/41f, 0.6 s wait 602 ms/1f, nav update 1.21 s/87f
        // States under 1 ms and one frame are left out.
        private static FieldInfo _actPc;
        private static object _actIter;           // the run being timed
        private static int _actState = -2;        // -2 = not running
        private static long _actSince;
        private static int _actFrames;
        private static readonly StringBuilder _actLine = new StringBuilder();

        /// The compiler's iterator class of LoadSave.Activation (shared with PerfPatches).
        public static Type ActivationIterator()
        {
            Type ls = GameBridge.FindGameType("LoadSave");
            if (ls == null) return null;
            Type[] nested = ls.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < nested.Length; i++)
                if (nested[i].Name.StartsWith("<Activation>", StringComparison.Ordinal)) return nested[i];
            return null;
        }

        // What the iterator waits on in each state (IL, v0.24.95).
        private static string ActName(int pc)
        {
            switch (pc)
            {
                case 0: return "start";
                case 1: return "player rigidbody";
                case 2: return "MP plane";
                case 3: return "step 0";
                case 4: return "Astar on";
                case 5: return "lists";
                case 6: return "player object";
                case 7: return "step 3";
                case 8: return "step 3 frame";
                case 9: return "0.5 s wait";
                case 10: return "MP client 0.5 s";
                case 11: return "MP client ground";
                case 12: return "0.6 s wait";
                case 13: return "nav update";
                case 14: return "endgame wait";
                case 15: return "endgame frame";
                default: return "state " + pc;
            }
        }

        private static void ActPrefix(object __instance)
        {
            // A new run (or one left unfinished when its scene went) starts over.
            if (ReferenceEquals(__instance, _actIter) || _actPc == null) return;
            try
            {
                _actIter = __instance;
                _actState = (int)_actPc.GetValue(__instance);
                _actSince = Stopwatch.GetTimestamp();
                _actFrames = 0;
                _actLine.Length = 0;
            }
            catch (Exception) { _actState = -2; }
        }

        private static void ActPostfix(object __instance, bool __result)
        {
            if (_actState == -2 || _instance == null || !ReferenceEquals(__instance, _actIter)) return;
            try
            {
                int pc = __result ? (int)_actPc.GetValue(__instance) : -1;
                _actFrames++;
                if (pc == _actState) return;
                long now = Stopwatch.GetTimestamp();
                long ticks = now - _actSince;
                if (_actFrames > 1 || ticks * 1000 >= Stopwatch.Frequency)
                {
                    if (_actLine.Length > 0) _actLine.Append(", ");
                    _actLine.Append(ActName(_actState)).Append(' ').Append(Ms(ticks)).Append('/').Append(_actFrames).Append('f');
                }
                _actState = pc;
                _actSince = now;
                _actFrames = 0;
                if (pc != -1) return;
                _actState = -2;
                _actIter = null;
                _instance._log.LogInfo(Prefix + "activation steps: " + (_actLine.Length > 0 ? _actLine.ToString() : "all in one frame"));
            }
            catch (Exception) { _actState = -2; }
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
