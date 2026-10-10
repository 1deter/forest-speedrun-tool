using System;
using ForestOverlay.Data;
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
    // WHAT (the rules: Data/LookupCache, tested):
    // - One(type) is FindObjectOfType. The object found is kept while it is
    //   alive and active - destroyed or switched off, the search runs
    //   again, so the answer is the one the search would give. Used for
    //   single objects: the tree manager, TreeLodGrid, Megan's
    //   setupGirlMutant, the player's TickOff / TodoList.
    // - Trees(type) is FindObjectsOfType, for the scene's trees only
    //   (CoopTreeId). Trees come with their scene and are never made or
    //   switched on later: all 8628 are active, in a cave too (`type ...
    //   all` = the active count), no game code instantiates or adds one
    //   (ilscan refs CoopTreeId), and the game keeps the same list itself
    //   (CoopPlayerCallbacks.AllTrees: FindObjectsOfType<CoopTreeId> once).
    //   Anything that can appear without a scene event (the elevators) is
    //   searched every time.
    // - Everything is forgotten when a scene loads or unloads (a Full load,
    //   the caves' and the endgame's streaming); nothing found is never
    //   kept.
    // ------------------------------------------------------------------
    public static class SceneCache
    {
        private static readonly LookupCache<Type, UnityEngine.Object> Cache =
            new LookupCache<Type, UnityEngine.Object>(Usable);
        private static bool _hooked;

        /// Searches run and answers from the cache, this launch.
        public static int Searches { get { return Cache.Searches; } }
        public static int Hits { get { return Cache.Hits; } }

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
            Cache.Clear();
        }

        /// Scenes loaded or unloaded this launch (Data/KeepLoaded: a cheap
        /// restart only while none has since the restore, T-0212).
        public static int SceneEvents { get; private set; }

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, LoadSceneMode m) { SceneEvents++; Cache.Clear(); }
        private static void OnSceneUnloaded(UnityEngine.SceneManagement.Scene s) { SceneEvents++; Cache.Clear(); }

        /// UnityEngine.Object.FindObjectOfType(t), kept: null when none.
        public static Component One(Type t)
        {
            if (t == null) return null;
            if (!_hooked) return UnityEngine.Object.FindObjectOfType(t) as Component;
            return Cache.One(t, FindOne) as Component;
        }

        /// UnityEngine.Object.FindObjectsOfType(t) for the scene's trees
        /// (see above - nothing else). The array is the cache's own: read
        /// it, never change it.
        public static UnityEngine.Object[] Trees(Type t)
        {
            if (t == null) return new UnityEngine.Object[0];
            if (!_hooked) return UnityEngine.Object.FindObjectsOfType(t);
            return Cache.All(t, FindAll);
        }

        private static UnityEngine.Object FindOne(Type t) { return UnityEngine.Object.FindObjectOfType(t); }
        private static UnityEngine.Object[] FindAll(Type t) { return UnityEngine.Object.FindObjectsOfType(t); }

        private static bool Usable(UnityEngine.Object o)
        {
            Component c = o as Component;
            return c != null && c.gameObject.activeInHierarchy;
        }
    }
}
