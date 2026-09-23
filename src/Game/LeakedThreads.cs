using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Two worker threads the game leaves behind on every game-scene load
    // (v0.23.2 census: OS threads +2 a load, 151 -> 189 over 21 loads;
    // game-notes "The load leak"). A thread's stack is a GC root no walk
    // of statics can see, so whatever it holds lives on.
    //
    // WorkScheduler (IL): OnEnable starts `ThreadedUpdate`, a loop
    //     while (secondaryThreadState < 3) { mutex.WaitOne(); ...; mutex.Reset(); }
    // and only LateUpdate calls mutex.Set(). OnDestroy sets the state to 3
    // and clears the batches - but the thread is parked in WaitOne, and a
    // destroyed object has no more LateUpdates: it never wakes, never sees
    // the 3, and keeps the old scheduler alive. Fix: a postfix on OnDestroy
    // sets the mutex once more; the loop wakes, skips its work (state is
    // not 1) and exits.
    //
    // FocusLostAudio (IL): OnEnable starts a worker thread, moves itself to
    // the root and calls DontDestroyOnLoad. Every game scene has one, so
    // every load adds a copy that only goes away at the title screen
    // (OnLevelWasLoaded destroys it there). Fix: when a new one is enabled,
    // destroy the older copies - what the game does at the title. Their
    // OnDisable issues Shutdown, which ends the thread.
    //
    // Memory only, no gameplay effect: not practice-only.
    // ------------------------------------------------------------------
    public sealed class LeakedThreads
    {
        /// Set by the owning module from its config switch.
        public static bool Enabled = true;

        public static int SchedulersWoken { get; private set; }
        public static int AudioCopiesRemoved { get; private set; }

        private static ManualLogSource _log;
        private static FieldInfo _mutex;
        private static FieldInfo _thread;
        // Weak: a finished Thread still references its start delegate, i.e. the
        // old scheduler - holding it here would be the leak again.
        private static readonly List<WeakReference> Woken = new List<WeakReference>();
        private static readonly List<MonoBehaviour> AudioCopies = new List<MonoBehaviour>();

        private Harmony _harmony;

        public string Status { get; private set; }

        public LeakedThreads(ManualLogSource log)
        {
            _log = log;
            Status = "not installed";
        }

        public void Install(string harmonyId)
        {
            _harmony = new Harmony(harmonyId + ".threads");
            List<string> done = new List<string>();
            BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            try
            {
                Type ws = GameBridge.FindGameType("WorkScheduler");
                MethodInfo onDestroy = ws != null ? ws.GetMethod("OnDestroy", inst, null, Type.EmptyTypes, null) : null;
                _mutex = ws != null ? ws.GetField("mutex", inst) : null;
                _thread = ws != null ? ws.GetField("thread", inst) : null;
                if (onDestroy != null && _mutex != null)
                {
                    _harmony.Patch(onDestroy, postfix: new HarmonyMethod(typeof(LeakedThreads).GetMethod("SchedulerDestroyed", BindingFlags.Static | BindingFlags.NonPublic)));
                    done.Add("WorkScheduler");
                }
                else _log.LogWarning("LeakedThreads: WorkScheduler.OnDestroy / mutex not found.");
            }
            catch (Exception ex) { _log.LogWarning("LeakedThreads: WorkScheduler hook failed: " + ex.Message); }

            try
            {
                Type fla = GameBridge.FindGameType("FocusLostAudio");
                MethodInfo onEnable = fla != null ? fla.GetMethod("OnEnable", inst, null, Type.EmptyTypes, null) : null;
                if (onEnable != null)
                {
                    _harmony.Patch(onEnable, postfix: new HarmonyMethod(typeof(LeakedThreads).GetMethod("AudioEnabled", BindingFlags.Static | BindingFlags.NonPublic)));
                    done.Add("FocusLostAudio");
                }
                else _log.LogWarning("LeakedThreads: FocusLostAudio.OnEnable not found.");
            }
            catch (Exception ex) { _log.LogWarning("LeakedThreads: FocusLostAudio hook failed: " + ex.Message); }

            Status = done.Count == 2 ? "installed" : "installed " + done.Count + "/2";
            _log.LogInfo("LeakedThreads: " + Status + " (" + string.Join(", ", done.ToArray()) + ").");
        }

        public void Uninstall()
        {
            if (_harmony != null) _harmony.UnpatchSelf();
        }

        /// For the census label: "woken n (k still running)".
        public static string Summary()
        {
            int running = 0;
            for (int i = Woken.Count - 1; i >= 0; i--)
            {
                bool alive;
                try { Thread t = Woken[i].Target as Thread; alive = t != null && t.IsAlive; }
                catch (Exception) { alive = false; }
                if (alive) running++;
                else Woken.RemoveAt(i);   // finished: let it go
            }
            return "leaked threads stopped: scheduler " + SchedulersWoken + (running > 0 ? " (" + running + " still running)" : "") +
                   ", focus audio " + AudioCopiesRemoved;
        }

        // ------------------------------------------------------------------
        private static void SchedulerDestroyed(object __instance)
        {
            try
            {
                if (!Enabled) return;
                EventWaitHandle mutex = _mutex.GetValue(__instance) as EventWaitHandle;
                if (mutex == null) return;
                Thread t = _thread != null ? _thread.GetValue(__instance) as Thread : null;
                if (t != null && !t.IsAlive) return;

                mutex.Set();   // the loop wakes, sees state 3, exits
                SchedulersWoken++;
                if (t != null) Woken.Add(new WeakReference(t));
                _log.LogInfo("Threads: woke the destroyed WorkScheduler's thread so it exits (" + SchedulersWoken + " this session).");
            }
            catch (Exception ex) { _log.LogWarning("LeakedThreads: WorkScheduler wake failed: " + ex.Message); }
        }

        private static void AudioEnabled(object __instance)
        {
            try
            {
                MonoBehaviour fresh = __instance as MonoBehaviour;
                if (fresh == null) return;

                int removed = 0;
                for (int i = AudioCopies.Count - 1; i >= 0; i--)
                {
                    MonoBehaviour old = AudioCopies[i];
                    if (old == null) { AudioCopies.RemoveAt(i); continue; }   // destroyed already (title screen)
                    if (ReferenceEquals(old, fresh) || !Enabled) continue;
                    UnityEngine.Object.Destroy(old.gameObject);   // OnDisable -> Shutdown ends its thread
                    AudioCopies.RemoveAt(i);
                    removed++;
                }
                if (!AudioCopies.Contains(fresh)) AudioCopies.Add(fresh);

                if (removed > 0)
                {
                    AudioCopiesRemoved += removed;
                    _log.LogInfo("Threads: removed " + removed + " older FocusLostAudio cop" + (removed == 1 ? "y" : "ies") +
                                 " and its worker thread (" + AudioCopiesRemoved + " this session).");
                }
            }
            catch (Exception ex) { _log.LogWarning("LeakedThreads: FocusLostAudio cleanup failed: " + ex.Message); }
        }
    }
}
