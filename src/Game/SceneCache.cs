using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Scene objects the restore keepers look up, found once and kept
    // (T-0148).
    //
    // WHY (bridge, 2026-10-07, v0.24.251): every FindObjectOfType /
    // FindObjectsOfType walks the whole scene - 20-25 ms in ForestMain
    // whatever the type (CoopTreeId 37 ms). An in-place restore's
    // continuation ran six of them in one frame (NatureKeeper three, Megan,
    // the elevators, the nature guide - 168 ms of a 216 ms frame), and its
    // start frame two more (the to-do list, Megan): the first of the two
    // "Load timing: hitch" lines after every spot restart.
    //
    // WHAT: One(type) is FindObjectOfType, All(type) FindObjectsOfType,
    // the result kept until a scene loads or unloads (SceneManager's
    // events; a Full load, the caves' and the endgame's streaming), or
    // until a kept object is destroyed or inactive - then searched again,
    // so the answer is what the search would give. All() is only for
    // types whose objects come with their scene and are active (trees -
    // 8628 CoopTreeId, all active also in a cave - and the endgame's two
    // ElevatorSystem): one made or switched on later without a scene
    // event would be missed until the next one. Absence is not kept.
    // ------------------------------------------------------------------
    public static class SceneCache
    {
        private static readonly Dictionary<Type, Component> Ones = new Dictionary<Type, Component>();
        private static readonly Dictionary<Type, UnityEngine.Object[]> Alls = new Dictionary<Type, UnityEngine.Object[]>();
        private static bool _hooked;

        /// Searches run (a miss or a stale entry) and answers from the cache, this launch.
        public static int Searches { get; private set; }
        public static int Hits { get; private set; }

        /// Once, before the first use (the savestate module's Initialise).
        public static void Install()
        {
            if (_hooked) return;
            _hooked = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        public static void Uninstall()
        {
            if (!_hooked) return;
            _hooked = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            Clear();
        }

        public static void Clear()
        {
            Ones.Clear();
            Alls.Clear();
        }

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, LoadSceneMode m) { Clear(); }
        private static void OnSceneUnloaded(UnityEngine.SceneManagement.Scene s) { Clear(); }

        /// UnityEngine.Object.FindObjectOfType(t), kept: null when none.
        public static Component One(Type t)
        {
            if (t == null) return null;
            Component c;
            if (_hooked && Ones.TryGetValue(t, out c) && Usable(c)) { Hits++; return c; }
            Searches++;
            c = UnityEngine.Object.FindObjectOfType(t) as Component;
            if (!_hooked) return c;
            if (c != null) Ones[t] = c;
            else Ones.Remove(t);
            return c;
        }

        /// UnityEngine.Object.FindObjectsOfType(t), kept. The array is the
        /// cache's own: read it, never change it.
        public static UnityEngine.Object[] All(Type t)
        {
            if (t == null) return new UnityEngine.Object[0];
            UnityEngine.Object[] all;
            if (_hooked && Alls.TryGetValue(t, out all) && AllUsable(all)) { Hits++; return all; }
            Searches++;
            all = UnityEngine.Object.FindObjectsOfType(t);
            if (_hooked) Alls[t] = all;
            return all;
        }

        private static bool Usable(Component c)
        {
            return c != null && c.gameObject.activeInHierarchy;
        }

        private static bool AllUsable(UnityEngine.Object[] all)
        {
            for (int i = 0; i < all.Length; i++)
            {
                Component c = all[i] as Component;
                if (!Usable(c)) return false;
            }
            return true;
        }
    }
}
