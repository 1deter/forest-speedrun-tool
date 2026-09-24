using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // One log line describing which parts of the world are loaded: the
    // player's area flags, every loaded scene, and each streamed section
    // (Scene.SceneLoaders, TheForest.World.SceneUnloadInCave).
    //
    // WHY: a runner reported the lab and the hellcave not coming back as
    // captured - even with a load - once the red elevator had loaded the
    // overlook area; the last lab section must stay "loaded in collision
    // wise but invisible", which runners use blind. Which object holds
    // that state is not visible in IL (the endgame areas are switched by
    // scene objects and PlayMaker, not code the scanner can follow), so
    // this line is logged at capture and after every restore first
    // (gotcha 25): the difference between the two is the fix's target.
    // ------------------------------------------------------------------
    public static class AreaReport
    {
        private static bool _resolved;
        private static PropertyInfo _inCaves, _inEndgame, _inOverlook;
        private static FieldInfo _loaders;
        private static FieldInfo _sceneName, _forced, _root;

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
            if (local != null)
            {
                _inCaves = local.GetProperty("IsInCaves", stat);
                _inEndgame = local.GetProperty("IsInEndgame", stat);
                _inOverlook = local.GetProperty("IsInOverlookArea", stat);
            }

            // The full name: a bare "Scene" found nothing (v0.24.4-0.24.8
            // logged "streamed: (none bound)").
            Type scene = GameBridge.FindGameType("TheForest.Utils.Scene");
            if (scene != null) _loaders = scene.GetField("SceneLoaders", stat);

            Type loader = GameBridge.FindGameType("TheForest.World.SceneUnloadInCave");
            if (loader != null)
            {
                _sceneName = loader.GetField("_sceneName", inst);
                _forced = loader.GetField("_forcedUnload", inst);
                _root = loader.GetField("_loadedSceneRoot", inst);
            }
        }

        public static string Describe()
        {
            try
            {
                Resolve();
                StringBuilder sb = new StringBuilder(256);
                sb.Append("caves ").Append(Flag(_inCaves))
                  .Append(", endgame ").Append(Flag(_inEndgame))
                  .Append(", overlook ").Append(Flag(_inOverlook));

                // Sorted: the same set in another load order is the same
                // state, and the capture / restore lines are compared as text.
                List<string> scenes = new List<string>();
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    UnityEngine.SceneManagement.Scene s = SceneManager.GetSceneAt(i);
                    scenes.Add(s.isLoaded ? s.name : s.name + " (loading)");
                }
                scenes.Sort(StringComparer.Ordinal);
                sb.Append(" | scenes: ").Append(string.Join(", ", scenes.ToArray()));

                Array loaders = _loaders != null ? _loaders.GetValue(null) as Array : null;
                sb.Append(" | streamed:");
                if (loaders == null) sb.Append(" (none bound)");
                else
                {
                    List<string> streamed = new List<string>();
                    for (int i = 0; i < loaders.Length; i++)
                    {
                        object l = loaders.GetValue(i);
                        UnityEngine.Object alive = l as UnityEngine.Object;
                        if (alive == null) { streamed.Add("(destroyed)"); continue; }

                        string name = _sceneName != null ? _sceneName.GetValue(l) as string : null;
                        GameObject root = _root != null ? _root.GetValue(l) as GameObject : null;
                        bool forced = _forced != null && (bool)_forced.GetValue(l);

                        streamed.Add((string.IsNullOrEmpty(name) ? ((Component)l).name : name) +
                                     (root == null ? " unloaded" : (root.activeInHierarchy ? " loaded" : " loaded-inactive")) +
                                     (forced ? " (forced unload)" : ""));
                    }
                    streamed.Sort(StringComparer.Ordinal);
                    sb.Append(streamed.Count == 0 ? " (none)" : " " + string.Join(", ", streamed.ToArray()));
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "area report failed: " + ex.Message;
            }
        }

        /// The overlook flag (`LocalPlayer.SetInOverlookArea`, set by scene
        /// objects on the red elevator's ride) is not in the save, and the
        /// game reads it every frame (atmosphere, cull distances,
        /// IsInClosedArea). Capture is refused while it is set, so every
        /// savestate was taken outside: an in-place restore clears it
        /// (runner's log: `overlook yes` after the restore, `no` at capture).
        public static string LeaveOverlook()
        {
            try
            {
                Resolve();
                if (_inOverlook == null) return "";
                if (!(bool)_inOverlook.GetValue(null, null)) return "";
                MethodInfo set = _inOverlook.GetSetMethod(true);
                if (set == null) return "overlook: still set (no setter)";
                set.Invoke(null, new object[] { false });
                return "overlook: left (set by the elevator ride, not at capture)";
            }
            catch (Exception ex)
            {
                return "overlook: clearing failed (" + ex.Message + ")";
            }
        }

        private static string Flag(PropertyInfo p)
        {
            if (p == null) return "?";
            try { return (bool)p.GetValue(null, null) ? "yes" : "no"; }
            catch (Exception) { return "?"; }
        }
    }
}
