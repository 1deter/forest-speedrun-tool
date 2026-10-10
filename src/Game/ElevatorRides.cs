using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using ForestOverlay.Data;
using HarmonyLib;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The endgame elevators that started a ride, noted as they start it,
    // so a teleport's "stop a ride under way" (ElevatorKeeper.StopRides)
    // needs no scene search (T-0184).
    //
    // WHY (bridge, 2026-10-10, v0.24.268): every Go, F7 restart without a
    // start state and auto-restart stops the elevator rides first, and
    // StopRides found them with FindObjectsOfType(ElevatorSystem) - 24-26
    // ms on the surface with no elevator loaded, nearly all of a Go's 23-24
    // ms (the arm itself 1-2 ms). An auto-restart runs it in the run
    // module's Tick: "Slow tick: 'practicerun' took 23.7 ms" every time.
    //
    // WHAT (IL): `_moving` is written only by ElevatorSystem.Goto, and Goto
    // is started only by GotoRemotePoint (no StartCoroutine by name), so
    // every ride - the button's, the keycard sequence's stage, the bridge's
    // `call` - passes through it. A postfix on GotoRemotePoint notes the
    // elevator; StopRides walks the noted ones (Data/StartedSet: dropped
    // once destroyed or not moving). Only those on an active GameObject are
    // handed out - what FindObjectsOfType would have found. Not installed:
    // StopRides searches the scene as before.
    // ------------------------------------------------------------------
    public static class ElevatorRides
    {
        private static ManualLogSource _log;
        private static Harmony _harmony;
        private static readonly StartedSet<Component> Started = new StartedSet<Component>();

        /// True once the postfix is in: StopRides can trust the noted set.
        public static bool Installed { get; private set; }

        /// Elevators noted and not yet dropped (the bridge reads it).
        public static int Noted { get { return Started.Count; } }

        public static void Install(ManualLogSource log, string harmonyId)
        {
            _log = log;
            if (Installed) return;
            try
            {
                Type t = GameBridge.FindGameType("TheForest.World.ElevatorSystem");
                MethodInfo target = t != null
                    ? t.GetMethod("GotoRemotePoint", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
                    : null;
                if (target == null) { _log.LogWarning("ElevatorRides: ElevatorSystem.GotoRemotePoint not found - a teleport searches the scene for elevator rides."); return; }
                _harmony = new Harmony(harmonyId + ".elevatorrides");
                _harmony.Patch(target, postfix: new HarmonyMethod(typeof(ElevatorRides).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)));
                Installed = true;
            }
            catch (Exception ex) { _log.LogWarning("ElevatorRides: " + ex.Message); }
        }

        public static void Uninstall()
        {
            Installed = false;
            Started.Clear();
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception) { }
        }

        private static void Postfix(object __instance)
        {
            Started.Note(__instance as Component);
        }

        private static readonly Func<Component, bool> AliveCheck = Alive;
        private static readonly Func<Component, bool> ActiveCheck = Active;

        /// The noted elevators whose `moving` reads true, on an active
        /// GameObject, into `into`.
        public static void Moving(Func<Component, bool> moving, List<Component> into)
        {
            Started.Running(AliveCheck, moving, ActiveCheck, into);
        }

        private static bool Alive(Component c) { return c != null; }   // Unity's null: destroyed
        private static bool Active(Component c) { return c.gameObject.activeInHierarchy; }
    }
}
