using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Placed blueprints across a Quick load (audit, v0.24.203).
    //
    // A blueprint placed or built since the capture is deleted by the
    // restore (not in the save) and one finished since comes back from the
    // save - both confirmed over the bridge. What was missed: a blueprint
    // that was there at capture and got more logs / sticks since.
    // LoadNow puts its counts back (Craft_Structure._presentIngredients),
    // but OnDeserialized only calls Initialize, which returns at once for
    // a blueprint already set up (`_initialized`): the logs added since
    // stayed drawn (BuildIngredients.SetBuilt only ever turns pieces ON),
    // and the build HUD kept the later tally ("GATHER LOGS 0/2" for a
    // blueprint that needed 6 again).
    //
    // FIX: the capture lists every placed blueprint's counts (`blueprints`
    // header, Data/BlueprintState). Before a Quick load, a blueprint whose
    // counts differ is deleted like an object the save lacks (its HUD
    // share cancelled first, SavestateBridge.CancelBuildMissions), so
    // LoadNow builds it from the save the way a Full load does: drawn and
    // counted by the game's own Initialize. After the restore the HUD
    // tally (BuildMission.ActiveMissions, one number per item, floored at
    // 0) is recounted from the live blueprints - what a Full load gives,
    // and it repairs a tally that drifted (the floor at 0 loses counts).
    // ------------------------------------------------------------------
    public static class BlueprintKeeper
    {
        private static Type _craft;              // Craft_Structure (on the blueprint's Trigger)
        private static FieldInfo _required;      // List<BuildIngredients> _requiredIngredients
        private static FieldInfo _present;       // ReceipeIngredient[] _presentIngredients
        private static FieldInfo _initialized;   // bool _initialized
        private static FieldInfo _itemId;        // ReceipeIngredient._itemID
        private static FieldInfo _amount;        // ReceipeIngredient._amount
        private static PropertyInfo _all;        // static UniqueIdentifier.AllIdentifiers
        private static PropertyInfo _id;         // UniqueIdentifier.Id
        private static Type _prefabId;           // PrefabIdentifier (a blueprint's root)
        private static FieldInfo _missions;      // static BuildMission.ActiveMissions
        private static FieldInfo _amountNeeded;  // BuildMission._amountNeeded
        private static MethodInfo _addNeeded;    // static BuildMission.AddNeededToBuildMission(int, int, bool)
        private static bool _resolved;

        /// The `blueprints` header: every placed blueprint outside the
        /// player; null when the game's types are missing.
        public static string Capture(Transform keepRoot)
        {
            try
            {
                if (!Resolve()) return null;
                List<string> ids = new List<string>();
                List<int[]> present = new List<int[]>();
                List<Component> crafts = Live();
                for (int i = 0; i < crafts.Count; i++)
                {
                    Component c = crafts[i];
                    if (keepRoot != null && c.transform.IsChildOf(keepRoot)) continue;
                    Component u = Identifier(c);
                    if (u == null || !(bool)_initialized.GetValue(c)) continue;
                    ids.Add(_id.GetValue(u, null) as string);
                    present.Add(Present(c));
                }
                return BlueprintState.Write(ids, present);
            }
            catch (Exception) { return null; }
        }

        /// The roots of the blueprints whose counts changed since the
        /// capture - for the restore to delete, so LoadNow builds them
        /// again from the save. Empty without a header.
        public static List<GameObject> Changed(string header, Transform keepRoot)
        {
            List<GameObject> roots = new List<GameObject>();
            try
            {
                Dictionary<string, int[]> saved = BlueprintState.Parse(header);
                if (saved == null || saved.Count == 0 || !Resolve()) return roots;
                List<Component> crafts = Live();
                for (int i = 0; i < crafts.Count; i++)
                {
                    Component c = crafts[i];
                    if (keepRoot != null && c.transform.IsChildOf(keepRoot)) continue;
                    int[] was;
                    Component u = Identifier(c);
                    string id = u != null ? _id.GetValue(u, null) as string : null;
                    if (id == null || !saved.TryGetValue(id, out was)) continue;
                    if (BlueprintState.Same(was, Present(c))) continue;
                    GameObject root = Root(c.transform);
                    if (root != null && !roots.Contains(root)) roots.Add(root);
                }
            }
            catch (Exception) { }
            return roots;
        }

        /// Sets the build HUD's tally to what the live blueprints still
        /// need. Says what it changed; "" when it already matched.
        public static string RecountMissions()
        {
            try
            {
                if (!Resolve() || _missions == null || _amountNeeded == null || _addNeeded == null) return "";
                Dictionary<int, int> target = new Dictionary<int, int>();
                List<Component> crafts = Live();
                for (int i = 0; i < crafts.Count; i++)
                {
                    Component c = crafts[i];
                    if (!(bool)_initialized.GetValue(c)) continue;
                    IList required = _required.GetValue(c) as IList;
                    if (required == null) continue;
                    int[] ids = new int[required.Count];
                    int[] amounts = new int[required.Count];
                    for (int k = 0; k < required.Count; k++)
                    {
                        if (required[k] == null) continue;
                        ids[k] = (int)_itemId.GetValue(required[k]);
                        amounts[k] = (int)_amount.GetValue(required[k]);
                    }
                    BlueprintState.AddNeeded(target, ids, amounts, Present(c));
                }

                IDictionary missions = _missions.GetValue(null) as IDictionary;
                List<int> items = new List<int>(target.Keys);
                if (missions != null)
                    foreach (object k in missions.Keys)
                        if (!items.Contains((int)k)) items.Add((int)k);

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < items.Count; i++)
                {
                    int item = items[i];
                    int want;
                    target.TryGetValue(item, out want);
                    int now = 0;
                    if (missions != null && missions.Contains(item))
                    {
                        UnityEngine.Object m = missions[item] as UnityEngine.Object;
                        if (m == null) { sb.Append(sb.Length > 0 ? ", " : "").Append("item ").Append(item).Append(": its HUD line is gone, left"); continue; }
                        now = (int)_amountNeeded.GetValue(m);
                    }
                    if (now == want) continue;
                    _addNeeded.Invoke(null, new object[] { item, want - now, want < now });
                    sb.Append(sb.Length > 0 ? ", " : "").Append("item ").Append(item).Append(' ').Append(now).Append(" -> ").Append(want);
                }
                return sb.Length > 0 ? "build HUD recounted (" + sb + ")" : "";
            }
            catch (Exception ex)
            {
                return "build HUD recount failed: " + (ex.InnerException ?? ex).Message;
            }
        }

        // Every Craft_Structure on an identified object - the ones a save
        // holds. AllIdentifiers, not a scene search (gotcha 11).
        private static List<Component> Live()
        {
            List<Component> list = new List<Component>();
            IList all = _all.GetValue(null, null) as IList;
            if (all == null) return list;
            for (int i = 0; i < all.Count; i++)
            {
                Component u = all[i] as Component;
                if (u == null) continue;
                Component c = u.GetComponent(_craft);
                if (c != null && !list.Contains(c)) list.Add(c);
            }
            return list;
        }

        private static Component Identifier(Component c)
        {
            Component[] ids = c.GetComponents(_id.DeclaringType);
            return ids.Length > 0 ? ids[0] : null;
        }

        private static int[] Present(Component c)
        {
            Array p = _present.GetValue(c) as Array;
            if (p == null) return new int[0];
            int[] a = new int[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                object e = p.GetValue(i);
                a[i] = e != null ? (int)_amount.GetValue(e) : 0;
            }
            return a;
        }

        // The nearest object above (or at) the Trigger that the save
        // creates from a prefab: the Ghost_<X>(Clone).
        private static GameObject Root(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent)
                if (p.GetComponent(_prefabId) != null) return p.gameObject;
            return null;
        }

        private static bool Resolve()
        {
            if (_resolved) return _craft != null && _present != null && _all != null && _id != null && _prefabId != null;
            _resolved = true;
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            _craft = GameBridge.FindGameType("TheForest.Buildings.Creation.Craft_Structure");
            Type ingredient = GameBridge.FindGameType("TheForest.Items.Craft.ReceipeIngredient");
            Type uid = GameBridge.FindGameType("UniqueIdentifier");
            _prefabId = GameBridge.FindGameType("PrefabIdentifier");
            Type mission = GameBridge.FindGameType("TheForest.Buildings.Creation.BuildMission");
            if (_craft != null)
            {
                _required = _craft.GetField("_requiredIngredients", inst);
                _present = _craft.GetField("_presentIngredients", inst);
                _initialized = _craft.GetField("_initialized", inst);
            }
            if (ingredient != null)
            {
                _itemId = ingredient.GetField("_itemID", inst);
                _amount = ingredient.GetField("_amount", inst);
            }
            if (uid != null)
            {
                _all = uid.GetProperty("AllIdentifiers", stat);
                _id = uid.GetProperty("Id", inst);
            }
            if (mission != null)
            {
                _missions = mission.GetField("ActiveMissions", stat);
                _amountNeeded = mission.GetField("_amountNeeded", inst);
                _addNeeded = mission.GetMethod("AddNeededToBuildMission", stat, null,
                    new[] { typeof(int), typeof(int), typeof(bool) }, null);
            }
            if (_required == null || _initialized == null || _itemId == null || _amount == null) _craft = null;
            return _craft != null && _present != null && _all != null && _id != null && _prefabId != null;
        }
    }
}
