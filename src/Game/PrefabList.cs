using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The game's list of prefabs a save's objects are made from, filled the
    // way the menu's load fills it.
    //
    // WHY (bridge, 2026-10-04): LevelSerializer.AllPrefabs (`_allPrefabs`,
    // ClassId -> prefab) is filled only by the title screen's load of a
    // save (LoadAsync: instantiate Resources "PreloadingPrefabs", then
    // LevelSerializer.InitPrefabList) and by a multiplayer client. A NEW
    // game skips both, and the scene's own SaveGameManager has no
    // requiredObjects - so in a launch whose first game was New the list is
    // empty (0 entries, read live; 352 after a menu load). Every object a
    // save holds that the scene lacks then becomes an empty "CreatedObject"
    // (LevelLoader), the player included: a Full load hung on LOADING for
    // good (Activation waits for LocalPlayer.Rigidbody), in the same mode or
    // across, and a Quick load could not rebuild what was destroyed since
    // the capture. The list survives the title screen, so only the first
    // game of a launch matters.
    //
    // WHAT: InitPrefabList's own filter (GetFilteredRequiredObjects) over
    // the PreloadingPrefabs asset's SaveGameManager.requiredObjects - read
    // from the asset, never instantiated (a second SaveGameManager would
    // replace the scene's and its id table). Confirmed over the bridge by
    // pointing SaveGameManager.instance at the asset for one InitPrefabList
    // call: 352 prefabs, and the Full load that hung came up in 11 s.
    // ------------------------------------------------------------------
    internal static class PrefabList
    {
        private const BindingFlags Stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// Fills the list when it is empty. Null when nothing was needed,
        /// else what was done (or why not) - for the restore's log line.
        public static string Ensure()
        {
            try
            {
                Type ls = GameBridge.FindGameType("LevelSerializer");
                FieldInfo all = ls != null ? ls.GetField("_allPrefabs", Stat) : null;
                if (all == null) return "prefab list: LevelSerializer._allPrefabs not found";
                System.Collections.IDictionary current =all.GetValue(null) as System.Collections.IDictionary;
                if (current != null && current.Count > 0) return null;

                Type sgm = GameBridge.FindGameType("SaveGameManager");
                Type pid = GameBridge.FindGameType("PrefabIdentifier");
                FieldInfo required = sgm != null ? sgm.GetField("requiredObjects", Inst) : null;
                PropertyInfo classId = pid != null ? pid.GetProperty("ClassId", Inst) : null;
                if (required == null || classId == null) return "prefab list empty - SaveGameManager / PrefabIdentifier not found";

                GameObject asset = Resources.Load("PreloadingPrefabs") as GameObject;
                Component manager = asset != null ? asset.GetComponent(sgm) : null;
                UnityEngine.Object[] objects = manager != null ? required.GetValue(manager) as UnityEngine.Object[] : null;
                if (objects == null) return "prefab list empty - Resources 'PreloadingPrefabs' not found";

                Dictionary<string, GameObject> map = new Dictionary<string, GameObject>();
                for (int i = 0; i < objects.Length; i++)
                {
                    GameObject go = objects[i] as GameObject;
                    if (go == null) continue;
                    Component id = go.GetComponent(pid);
                    if (id == null) continue;
                    string key = classId.GetValue(id, null) as string;
                    if (key == null || map.ContainsKey(key)) continue;
                    map.Add(key, go);
                }
                all.SetValue(null, map);
                return "prefab list filled as the menu's load does (" + map.Count + " prefabs; empty after a new game this launch)";
            }
            catch (Exception ex)
            {
                return "prefab list: " + (ex.InnerException ?? ex).Message;
            }
        }
    }
}
