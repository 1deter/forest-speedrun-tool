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
    // Keeps taken world pickups so an in-place savestate restore can put
    // them back.
    //
    // WHY (IL, TheForest.Items.World.PickUp.ClearOut(fakeDrop)): unless it
    // is a fake drop or a multiplayer destroy, a taken pickup is
    //   _disableInsteadOfDestroy -> Used = true, GrabExit, target.SetActive(false)
    //   otherwise               -> unparent, TryPool() or Destroy(_destroyTarget)
    //                              (never for _infinite pickups)
    // Most world pickups (the keycard: C6_Props/.../Keycard) carry no
    // UniqueIdentifier, so the level serializer never records them and
    // only a fresh scene load brings them back - which is why a menu load
    // respawns them and LoadNow cannot.
    //
    // WHAT: while armed, a prefix sets _disableInsteadOfDestroy on such a
    // pickup before the game's own ClearOut runs, so the game takes its
    // own "disable" branch instead of destroying it, and remembers it.
    // Restore() re-enables the ones the savestate had. Pickups that DO
    // carry an identifier are left to the serializer.
    //
    // Armed only once savestates are in use this session (practice), so
    // normal play never keeps hidden objects around.
    // ------------------------------------------------------------------
    public sealed class PickupKeeper
    {
        private sealed class Taken
        {
            public Component Pickup;
            public GameObject Target;
            public string Key;
        }

        public static bool Armed;

        private static readonly List<Taken> TakenList = new List<Taken>();

        private static ManualLogSource _log;
        private static Type _uniqueIdType;
        private static FieldInfo _itemId;
        private static FieldInfo _destroyTarget;
        private static FieldInfo _disable;
        private static FieldInfo _infinite;
        private static PropertyInfo _used;

        private Harmony _harmony;

        public string Status { get; private set; }
        public int KeptCount { get { return TakenList.Count; } }

        public PickupKeeper(ManualLogSource log)
        {
            _log = log;
            Status = "not installed";
        }

        public void Install(string harmonyId)
        {
            Type pickUp = GameBridge.FindGameType("TheForest.Items.World.PickUp");
            _uniqueIdType = GameBridge.FindGameType("UniqueIdentifier");
            if (pickUp == null || _uniqueIdType == null) { Status = "PickUp type not found"; _log.LogWarning("PickupKeeper: " + Status); return; }

            BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _itemId = pickUp.GetField("_itemId", inst);
            _destroyTarget = pickUp.GetField("_destroyTarget", inst);
            _disable = pickUp.GetField("_disableInsteadOfDestroy", inst);
            _infinite = pickUp.GetField("_infinite", inst);
            _used = pickUp.GetProperty("Used", inst);

            MethodInfo clearOut = pickUp.GetMethod("ClearOut", inst, null, new[] { typeof(bool) }, null);
            if (clearOut == null || _destroyTarget == null || _disable == null)
            {
                Status = "PickUp.ClearOut / fields not found";
                _log.LogWarning("PickupKeeper: " + Status);
                return;
            }

            try
            {
                _harmony = new Harmony(harmonyId + ".pickups");
                _harmony.Patch(clearOut, new HarmonyMethod(typeof(PickupKeeper).GetMethod("ClearOutPrefix",
                    BindingFlags.Static | BindingFlags.NonPublic)));
                Status = "hooked";
            }
            catch (Exception ex)
            {
                Status = "Harmony: " + ex.Message;
            }
            _log.LogInfo("PickupKeeper: " + Status + ".");
        }

        public void Uninstall()
        {
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception) { }
            TakenList.Clear();
        }

        // Never throws into the game; never skips the original.
        private static void ClearOutPrefix(object __instance, bool fakeDrop)
        {
            if (!Armed || fakeDrop) return;
            try
            {
                if ((bool)_disable.GetValue(__instance)) return;              // the game already hides it
                if (_infinite != null && (bool)_infinite.GetValue(__instance)) return;

                GameObject target = _destroyTarget.GetValue(__instance) as GameObject;
                Component pickup = __instance as Component;
                if (target == null || pickup == null) return;
                if (HasIdentifier(target)) return;                            // the serializer owns it

                _disable.SetValue(__instance, true);

                Taken t = new Taken();
                t.Pickup = pickup;
                t.Target = target;
                t.Key = KeyFor(pickup, target);
                TakenList.Add(t);
            }
            catch (Exception) { }
        }

        /// Puts back kept pickups: those in `present` (the capture's list),
        /// or every kept pickup when `present` is null (no list: a v0.20.0
        /// file or a slot save - a normal load would respawn them all).
        /// Returns how many came back.
        public int Restore(HashSet<string> present)
        {
            int restored = 0;
            for (int i = TakenList.Count - 1; i >= 0; i--)
            {
                Taken t = TakenList[i];
                if (t.Target == null || t.Pickup == null) { TakenList.RemoveAt(i); continue; }   // scene changed
                if (present != null && !present.Contains(t.Key)) continue;

                try
                {
                    if (_used != null) _used.SetValue(t.Pickup, false, null);
                    _disable.SetValue(t.Pickup, false);
                    t.Target.SetActive(true);
                    restored++;
                }
                catch (Exception ex)
                {
                    _log.LogWarning("PickupKeeper: could not restore " + t.Key + ": " + ex.Message);
                }
                TakenList.RemoveAt(i);
            }
            return restored;
        }

        /// Every loaded world pickup without an identifier, keyed for a
        /// capture. Walks all PickUp components once - capture only.
        public void Snapshot(List<string> keys)
        {
            keys.Clear();
            if (_destroyTarget == null) return;

            Type pickUp = GameBridge.FindGameType("TheForest.Items.World.PickUp");
            if (pickUp == null) return;

            UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(pickUp);
            for (int i = 0; i < all.Length; i++)
            {
                Component c = all[i] as Component;
                if (c == null) continue;
                GameObject target = null;
                try { target = _destroyTarget.GetValue(c) as GameObject; }
                catch (Exception) { }
                if (target == null) target = c.gameObject;
                if (!target.activeInHierarchy || HasIdentifier(target)) continue;
                keys.Add(KeyFor(c, target));
            }
        }

        private static string KeyFor(Component pickup, GameObject target)
        {
            int id = 0;
            try { if (_itemId != null) id = (int)_itemId.GetValue(pickup); }
            catch (Exception) { }
            Vector3 p = target.transform.position;
            return SavestateFile.PickupKey(id, p.x, p.y, p.z);
        }

        private static bool HasIdentifier(GameObject go)
        {
            Component[] ids = go.GetComponentsInParent(_uniqueIdType, true);
            return ids != null && ids.Length > 0;
        }
    }
}
