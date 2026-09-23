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

            Type scene = GameBridge.FindGameType("Scene");
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

                sb.Append(" | scenes:");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    UnityEngine.SceneManagement.Scene s = SceneManager.GetSceneAt(i);
                    sb.Append(i == 0 ? " " : ", ").Append(s.name);
                    if (!s.isLoaded) sb.Append(" (loading)");
                }

                Array loaders = _loaders != null ? _loaders.GetValue(null) as Array : null;
                sb.Append(" | streamed:");
                if (loaders == null) sb.Append(" (none bound)");
                else
                {
                    for (int i = 0; i < loaders.Length; i++)
                    {
                        object l = loaders.GetValue(i);
                        UnityEngine.Object alive = l as UnityEngine.Object;
                        if (alive == null) { sb.Append(i == 0 ? " " : ", ").Append("(destroyed)"); continue; }

                        string name = _sceneName != null ? _sceneName.GetValue(l) as string : null;
                        GameObject root = _root != null ? _root.GetValue(l) as GameObject : null;
                        bool forced = _forced != null && (bool)_forced.GetValue(l);

                        sb.Append(i == 0 ? " " : ", ").Append(string.IsNullOrEmpty(name) ? ((Component)l).name : name)
                          .Append(root == null ? " unloaded" : (root.activeInHierarchy ? " loaded" : " loaded-inactive"));
                        if (forced) sb.Append(" (forced unload)");
                    }
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "area report failed: " + ex.Message;
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
