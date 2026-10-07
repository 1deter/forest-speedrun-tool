using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // The catch of every Unity message method (Update, OnRenderObject,
    // OnDestroy, ...) on our MonoBehaviours (gotcha 3; lint.py checks the
    // wrapping). A throw is logged once per method, then swallowed: an
    // escaping exception in a per-frame method would log every frame, and
    // a throwing Awake leaves the object half set up while BepInEx still
    // says "loaded". Nothing allocates unless something threw.
    // ------------------------------------------------------------------
    public static class Lifecycle
    {
        public static ManualLogSource Log;

        private static readonly HashSet<string> Seen = new HashSet<string>();

        public static void Fail(string where, Exception ex)
        {
            try
            {
                if (!Seen.Add(where) || Log == null) return;
                Log.LogError("Lifecycle: " + where + " threw (logged once, later throws are silent): " + ex);
            }
            catch (Exception) { }
        }
    }
}
