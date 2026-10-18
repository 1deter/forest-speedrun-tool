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
        private static Type _coopAnimalType;
        private static FieldInfo _itemId;
        private static FieldInfo _destroyTarget;
        private static FieldInfo _disable;
        private static FieldInfo _infinite;
        private static PropertyInfo _used;
        private static MethodInfo _clearOut;

        // The phantom stick (fix list 2, author, once after Quick loads): a
        // stick picked up, gone, nothing in the inventory. A PickUp.Collect
        // that clears the pickup while the item's count does not rise logs
        // one line (the plane axe a Quick load re-created did exactly that
        // at its max of 1). Explained in T-0053: at the max the game fake-
        // drops it (game-notes *A pickup taken with the inventory full*);
        // those are kept for a restore now, and not counted as gone.
        private static FieldInfo _localInventory;
        private static MethodInfo _amountOf;
        private static MethodInfo _maxAmountOf;
        private static bool _inCollect;
        private static bool _clearedInCollect;
        private static int _collectItem;
        private static int _countBefore;

        /// Item id -> name for log lines (set by the owner; may be null).
        public static Func<int, string> NameOf;

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
            _coopAnimalType = GameBridge.FindGameType("CoopAnimal");
            if (pickUp == null || _uniqueIdType == null) { Status = "PickUp type not found"; _log.LogWarning("PickupKeeper: " + Status); return; }

            BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _itemId = pickUp.GetField("_itemId", inst);
            _destroyTarget = pickUp.GetField("_destroyTarget", inst);
            _disable = pickUp.GetField("_disableInsteadOfDestroy", inst);
            _infinite = pickUp.GetField("_infinite", inst);
            _used = pickUp.GetProperty("Used", inst);

            MethodInfo clearOut = pickUp.GetMethod("ClearOut", inst, null, new[] { typeof(bool) }, null);
            _clearOut = clearOut;
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
                    BindingFlags.Static | BindingFlags.NonPublic)),
                    new HarmonyMethod(typeof(PickupKeeper).GetMethod("ClearOutPostfix",
                    BindingFlags.Static | BindingFlags.NonPublic)));
                Status = "hooked";

                MethodInfo collect = pickUp.GetMethod("Collect", inst, null, Type.EmptyTypes, null);
                Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
                Type inv = GameBridge.FindGameType("TheForest.Items.Inventory.PlayerInventory");
                if (local != null) _localInventory = local.GetField("Inventory", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (inv != null)
                {
                    _amountOf = inv.GetMethod("AmountOf", inst, null, new[] { typeof(int), typeof(bool) }, null);
                    _maxAmountOf = inv.GetMethod("GetMaxAmountOf", inst, null, new[] { typeof(int) }, null);
                }
                if (collect != null && _localInventory != null && _amountOf != null && _itemId != null)
                {
                    _harmony.Patch(collect,
                        new HarmonyMethod(typeof(PickupKeeper).GetMethod("CollectPrefix", BindingFlags.Static | BindingFlags.NonPublic)),
                        new HarmonyMethod(typeof(PickupKeeper).GetMethod("CollectPostfix", BindingFlags.Static | BindingFlags.NonPublic)));
                    Status += ", collect watched";
                }
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

        // A fake drop (Collect with the inventory full, T-0053): the game
        // spawns a dynamic copy (Stick_Dynamic) and, when DoFakeDrop says
        // false, clears the pickup like a taken one - so it is kept too. When
        // DoFakeDrop says true the pickup itself is the dropped item and stays
        // active: the postfix undoes what the prefix did.
        private static Component _fakePickup;
        private static GameObject _fakeTarget;
        private static string _fakeKey;

        // Never throws into the game; never skips the original.
        private static void ClearOutPrefix(object __instance, bool fakeDrop)
        {
            if (_inCollect && !fakeDrop) _clearedInCollect = true;
            _fakePickup = null;
            if (!Armed) return;
            try
            {
                if ((bool)_disable.GetValue(__instance)) return;              // the game already hides it
                if (_infinite != null && (bool)_infinite.GetValue(__instance)) return;

                GameObject target = _destroyTarget.GetValue(__instance) as GameObject;
                Component pickup = __instance as Component;
                if (target == null || pickup == null) return;
                if (HasIdentifier(target)) return;                            // the serializer owns it

                _disable.SetValue(__instance, true);

                if (fakeDrop)
                {
                    _fakePickup = pickup;
                    _fakeTarget = target;
                    _fakeKey = KeyFor(pickup, target);
                    return;
                }

                Taken t = new Taken();
                t.Pickup = pickup;
                t.Target = target;
                t.Key = KeyFor(pickup, target);
                TakenList.Add(t);
            }
            catch (Exception) { }
        }

        private static void ClearOutPostfix(object __instance, bool fakeDrop)
        {
            if (!fakeDrop || _fakePickup == null || !ReferenceEquals(_fakePickup, __instance)) return;
            Component pickup = _fakePickup;
            GameObject target = _fakeTarget;
            string key = _fakeKey;
            _fakePickup = null;
            _fakeTarget = null;
            _fakeKey = null;
            try
            {
                if (target != null && target.activeSelf)
                {
                    _disable.SetValue(pickup, false);                         // DoFakeDrop kept it: not taken
                    return;
                }
                Taken t = new Taken();
                t.Pickup = pickup;
                t.Target = target;
                t.Key = key;
                TakenList.Add(t);
            }
            catch (Exception) { }
        }

        // Only while armed (savestates in use), like the keeping itself.
        private static void CollectPrefix(object __instance)
        {
            _inCollect = false;
            if (!Armed) return;
            try
            {
                _collectItem = (int)_itemId.GetValue(__instance);
                _countBefore = AmountOf(_collectItem);
                _clearedInCollect = false;
                _inCollect = true;
            }
            catch (Exception) { }
        }

        private static void CollectPostfix(object __instance)
        {
            if (!_inCollect) return;
            _inCollect = false;
            if (!_clearedInCollect) return;
            try
            {
                int after = AmountOf(_collectItem);
                if (after < 0 || after > _countBefore) return;
                int max = -1;
                object inv = _localInventory.GetValue(null);
                if (_maxAmountOf != null && inv != null) max = (int)_maxAmountOf.Invoke(inv, new object[] { _collectItem });
                if (max == 0) return;                                          // eaten / used on the spot, never carried

                Component c = __instance as Component;
                GameObject target = _destroyTarget.GetValue(__instance) as GameObject;
                if (target == null && c != null) target = c.gameObject;
                string name = NameOf != null ? NameOf(_collectItem) : null;
                _log.LogWarning("Pickup gone, inventory unchanged: " + (name ?? "item") + " (" + _collectItem + ") at " +
                                (target != null ? target.transform.position.ToString() + " '" + PathOf(target.transform) + "'" : "?") +
                                ", count " + _countBefore + " -> " + after + " (max " + max + ").");
            }
            catch (Exception) { }
        }

        private static int AmountOf(int itemId)
        {
            object inv = _localInventory.GetValue(null);
            if (inv == null) return -1;
            return (int)_amountOf.Invoke(inv, new object[] { itemId, false });
        }

        private static string PathOf(Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        /// Drops entries whose objects a load destroyed (only an in-place
        /// restore used to prune them; the census saw 196 dead after 21
        /// load restores). Returns how many.
        public int PruneDestroyed()
        {
            int n = 0;
            for (int i = TakenList.Count - 1; i >= 0; i--)
            {
                Taken t = TakenList[i];
                if (t.Target == null || t.Pickup == null) { TakenList.RemoveAt(i); n++; }
            }
            return n;
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

        /// Destroys loaded world pickups (no identifier) that the capture
        /// did not list and whose item `remove` accepts - severed limbs and
        /// heads left by kills since the capture (author's v0.24.11 log:
        /// `Head x1, Arm x1` not at capture after a restore). Returns how many.
        public int RemoveNew(HashSet<string> present, Func<int, bool> remove)
        {
            if (_destroyTarget == null || _itemId == null) return 0;
            Type pickUp = GameBridge.FindGameType("TheForest.Items.World.PickUp");
            if (pickUp == null) return 0;

            int n = 0;
            UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(pickUp);
            for (int i = 0; i < all.Length; i++)
            {
                Component c = all[i] as Component;
                if (c == null) continue;
                int id;
                try { id = (int)_itemId.GetValue(c); }
                catch (Exception) { continue; }
                if (!remove(id)) continue;

                GameObject target = null;
                try { target = _destroyTarget.GetValue(c) as GameObject; }
                catch (Exception) { }
                if (target == null) target = c.gameObject;
                if (!target.activeInHierarchy || HasIdentifier(target)) continue;
                if (present.Contains(KeyFor(c, target))) continue;

                UnityEngine.Object.Destroy(target);
                n++;
            }
            return n;
        }

        // ---- T-0416: the scene roots a Quick load leaves or moves ----------

        private static bool _rootTypesLoaded;
        private static Type _fakeParentType, _heldIdType;
        private static Type[] _markerTypes;     // destroyAfter, Molotov, Bomb, Explode, FlareWeapon, effigyRangeDetect
        private static readonly string[] MarkerNames = { "destroyAfter", "Molotov", "Bomb", "Explode", "FlareWeapon", "effigyRangeDetect" };
        private static List<object> _pools;
        private static MethodInfo _isSpawned;
        private static bool _poolWarned;

        private static void LoadRootTypes()
        {
            if (_rootTypesLoaded) return;
            _rootTypesLoaded = true;
            _fakeParentType = GameBridge.FindGameType("TheForest.Utils.FakeParent");
            _heldIdType = GameBridge.FindGameType("TheForest.Items.Inventory.HeldItemIdentifier");   // ILScan
            _markerTypes = new Type[MarkerNames.Length];
            for (int i = 0; i < MarkerNames.Length; i++)
            {
                _markerTypes[i] = GameBridge.FindGameType(MarkerNames[i]);
                if (_markerTypes[i] == null) _log.LogWarning("PickupKeeper: game type '" + MarkerNames[i] + "' not found; things thrown since a capture of that kind stay after a restore.");
            }
        }

        /// The game's spawn pools (PoolManager.Pools values); false when they
        /// cannot be read, then nothing is judged "not pooled".
        private static bool LoadPools()
        {
            _pools = null;
            _isSpawned = null;
            try
            {
                Type pm = GameBridge.FindGameType("PathologicalGames.PoolManager");
                Type sp = GameBridge.FindGameType("PathologicalGames.SpawnPool");
                if (pm == null || sp == null) throw new Exception("pool types not found");
                _isSpawned = sp.GetMethod("IsSpawned", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(Transform) }, null);
                BindingFlags st = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                object dict = null;
                FieldInfo f = pm.GetField("Pools", st);
                if (f != null) dict = f.GetValue(null);
                else
                {
                    PropertyInfo p = pm.GetProperty("Pools", st);
                    if (p != null) dict = p.GetValue(null, null);
                }
                System.Collections.IEnumerable en = dict as System.Collections.IEnumerable;
                if (_isSpawned == null || en == null) throw new Exception("PoolManager.Pools / SpawnPool.IsSpawned not readable");
                List<object> list = new List<object>();
                foreach (object e in en)
                {
                    if (e == null) continue;
                    object pool = e;
                    if (!sp.IsInstanceOfType(e))
                    {
                        PropertyInfo v = e.GetType().GetProperty("Value");
                        pool = v != null ? v.GetValue(e, null) : null;
                    }
                    if (pool != null) list.Add(pool);
                }
                _pools = list;
                return true;
            }
            catch (Exception ex)
            {
                if (!_poolWarned) { _poolWarned = true; _log.LogWarning("PickupKeeper: spawn pools unreadable (" + ex.Message + "); things thrown since a capture are left alone."); }
                return false;
            }
        }

        private static bool IsPooled(Transform t)
        {
            if (_pools == null) return false;
            for (int i = 0; i < _pools.Count; i++)
            {
                try { if ((bool)_isSpawned.Invoke(_pools[i], new object[] { t })) return true; }
                catch (Exception) { }
            }
            return false;
        }

        /// The PickUp below root `r` that is aimed at the root (its destroy
        /// target), not a live animal's; null when none.
        private static Component RootPickup(GameObject r, Type pickUp, FieldInfo despawn)
        {
            Component[] pickups = r.GetComponentsInChildren(pickUp, true);
            for (int k = 0; k < pickups.Length; k++)
            {
                Component c = pickups[k];
                try
                {
                    if (despawn != null && (bool)despawn.GetValue(c)) continue;
                    GameObject target = _destroyTarget.GetValue(c) as GameObject;
                    if (target == null ? c.gameObject == r : target == r) return c;
                }
                catch (Exception) { }
            }
            return null;
        }

        private static bool IsRootCandidate(GameObject r, Transform player)
        {
            if (r == null || !r.activeInHierarchy) return false;
            if (player != null && r.transform == player.root) return false;
            if (r.GetComponent(_uniqueIdType) != null) return false;
            if (_coopAnimalType != null && r.GetComponent(_coopAnimalType) != null) return false;
            return true;
        }

        /// T-0416, at a capture: the pose of every loose world item at a
        /// scene root (what MirrorRoots matches), as a `dropped` line (Data/
        /// DroppedPoses), so a restore can move a copy that rolled back.
        public static string CaptureDropped(Transform player)
        {
            try
            {
                if (_destroyTarget == null || _itemId == null || _uniqueIdType == null) return "";
                Type pickUp = GameBridge.FindGameType("TheForest.Items.World.PickUp");
                if (pickUp == null) return "";
                FieldInfo despawn = pickUp.GetField("_poolManagerDespawnCreature", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                LoadRootTypes();
                bool pools = LoadPools();
                List<KeyValuePair<int, DroppedPoses.Pose>> list = new List<KeyValuePair<int, DroppedPoses.Pose>>();
                for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
                {
                    UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                    if (!scene.isLoaded) continue;
                    GameObject[] roots = scene.GetRootGameObjects();
                    for (int i = 0; i < roots.Length; i++)
                    {
                        GameObject r = roots[i];
                        if (!IsRootCandidate(r, player)) continue;
                        Component hit = RootPickup(r, pickUp, despawn);
                        if (hit == null) continue;
                        if (pools && IsPooled(r.transform)) continue;
                        DroppedPoses.Pose p;
                        p.Item = 0;
                        try { p.Item = (int)_itemId.GetValue(hit); } catch (Exception) { }
                        Vector3 pos = r.transform.position;
                        Quaternion q = r.transform.rotation;
                        p.X = pos.x; p.Y = pos.y; p.Z = pos.z;
                        p.Qx = q.x; p.Qy = q.y; p.Qz = q.z; p.Qw = q.w;
                        list.Add(new KeyValuePair<int, DroppedPoses.Pose>(r.GetInstanceID(), p));
                    }
                }
                return DroppedPoses.Format(list);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Savestate capture: recording loose items failed: " + ex.Message);
                return "";
            }
        }

        private static bool PutBack(Transform t, DroppedPoses.Pose p)
        {
            Vector3 pos = t.position;
            Quaternion q = t.rotation;
            bool off = DroppedPoses.Differs(p, pos.x, pos.y, pos.z, q.x, q.y, q.z, q.w);
            if (!off) return false;
            t.position = new Vector3(p.X, p.Y, p.Z);
            t.rotation = new Quaternion(p.Qx, p.Qy, p.Qz, p.Qw);
            Rigidbody rb = t.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic && t.gameObject.activeInHierarchy)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.Sleep();
            }
            return true;
        }

        /// What one root walk did (T-0416).
        public sealed class MirrorResult
        {
            public int Items;                                   // made since the capture, removed (T-0411)
            public int Thrown;                                  // bombs, flares, molotovs ... removed
            public int Moved;                                   // copies put back where they lay
            public Dictionary<int, int> ItemCounts = new Dictionary<int, int>();
            public Dictionary<string, int> ThrownCounts = new Dictionary<string, int>();
        }

        /// One walk over the scene roots at a restore, before the load.
        /// T-0411: destroys world items made after the capture (a full-
        /// inventory take's Stick_Dynamic copy and kin): the root has no
        /// identifier, a lower instance id than `threshold`, and a PickUp
        /// below it whose destroy target is the root. A live animal is never
        /// one (T-0415): a rabbit (rabbitGo, from the creatures pool, which
        /// leaves it at the root) carries an inactive 'Rabbit Alive' PickUp
        /// aimed at itself; destroying it left dead entries in its spawn zone
        /// and the pool.
        /// T-0416: also destroys things thrown since (lit flare, molotov,
        /// bomb and its blast: a root, no PickUp, not pooled, whose own
        /// component is one the game destroys it with on a timer), and, with
        /// `dropped` (the capture's line, only for the same world), puts
        /// copies that moved since back at their captured pose - the ones
        /// taken since too (their kept key is recomputed so the restore
        /// matches the capture's).
        public static MirrorResult MirrorRoots(int threshold, Transform player, string dropped)
        {
            MirrorResult res = new MirrorResult();
            if (_destroyTarget == null || _itemId == null || _uniqueIdType == null) return res;
            Type pickUp = GameBridge.FindGameType("TheForest.Items.World.PickUp");
            if (pickUp == null) return res;
            FieldInfo despawn = pickUp.GetField("_poolManagerDespawnCreature", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            LoadRootTypes();
            Dictionary<int, DroppedPoses.Pose> poses = null;
            if (threshold < 0 && threshold != SpawnMark.Unknown && !string.IsNullOrEmpty(dropped)) poses = DroppedPoses.Parse(dropped);
            if (poses != null && poses.Count == 0) poses = null;

            // Copies taken again after the capture are kept hidden: forget them.
            for (int t = TakenList.Count - 1; t >= 0; t--)
            {
                GameObject tg = TakenList[t].Target;
                if (tg != null && tg.transform.parent == null && SpawnMark.IsAfter(tg.GetInstanceID(), threshold)) TakenList.RemoveAt(t);
            }
            // A copy that rolled and was then taken: put back where it lay and
            // re-keyed, so Restore(present) finds the capture's key.
            if (poses != null)
            {
                for (int t = 0; t < TakenList.Count; t++)
                {
                    Taken tk = TakenList[t];
                    if (tk.Target == null || tk.Pickup == null || tk.Target.transform.parent != null) continue;
                    DroppedPoses.Pose p;
                    if (!poses.TryGetValue(tk.Target.GetInstanceID(), out p)) continue;
                    try
                    {
                        if (PutBack(tk.Target.transform, p)) res.Moved++;
                        tk.Key = KeyFor(tk.Pickup, tk.Target);
                    }
                    catch (Exception) { }
                }
            }

            bool poolsRead = false, poolsOk = false;
            for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
            {
                UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    GameObject r = roots[i];
                    if (r == null) continue;
                    DroppedPoses.Pose pose;
                    if (poses != null && poses.TryGetValue(r.GetInstanceID(), out pose))
                    {
                        if (r.activeInHierarchy && PutBack(r.transform, pose)) res.Moved++;
                        continue;
                    }
                    if (!SpawnMark.IsAfter(r.GetInstanceID(), threshold)) continue;
                    if (!IsRootCandidate(r, player)) continue;
                    Component hit = RootPickup(r, pickUp, despawn);
                    if (hit != null)
                    {
                        int id = 0;
                        try { id = (int)_itemId.GetValue(hit); } catch (Exception) { }
                        UnityEngine.Object.Destroy(r);
                        int have;
                        res.ItemCounts.TryGetValue(id, out have);
                        res.ItemCounts[id] = have + 1;
                        res.Items++;
                        continue;
                    }

                    // Not an item: a thrown thing, by the root's own component.
                    if (_fakeParentType != null && r.GetComponent(_fakeParentType) != null) continue;
                    if (_heldIdType != null && r.GetComponent(_heldIdType) != null) continue;
                    bool[] has = new bool[MarkerNames.Length];
                    bool any = false;
                    for (int m = 0; m < has.Length; m++)
                    {
                        has[m] = _markerTypes[m] != null && r.GetComponent(_markerTypes[m]) != null;
                        any |= has[m];
                    }
                    string kind = DroppedPoses.ThrownKind(has[2], has[3], has[1], has[4], r.name.StartsWith("FlareSpawn", StringComparison.Ordinal), any);
                    if (kind.Length == 0) continue;
                    if (!poolsRead) { poolsRead = true; poolsOk = LoadPools(); }
                    if (!poolsOk || IsPooled(r.transform)) continue;
                    UnityEngine.Object.Destroy(r);
                    int n;
                    res.ThrownCounts.TryGetValue(kind, out n);
                    res.ThrownCounts[kind] = n + 1;
                    res.Thrown++;
                }
            }
            return res;
        }

        /// After a Quick load: loaded world pickups (no identifier) of an
        /// item `item` accepts, where `where` accepts the target, that the
        /// capture does not account for - by item, nearest first within
        /// MatchNear, then (`farToo`) any left over: logs from trees and
        /// sticks from saplings cut since the capture (NatureKeeper). Logs
        /// roll, so not by exact key. A pooled one (Pooling/Pool_PickUps/
        /// Log(Clone)) goes back to its pool the game's way; others are
        /// destroyed. Returns how many.
        public int RemoveExtra(HashSet<string> present, Func<int, bool> item, Func<GameObject, bool> where, bool farToo)
        {
            return RemoveExtra(present, item, where, farToo, null);
        }

        /// As above; `counts` (optional) gets item id -> how many removed.
        public int RemoveExtra(HashSet<string> present, Func<int, bool> item, Func<GameObject, bool> where, bool farToo,
                               Dictionary<int, int> counts)
        {
            if (_destroyTarget == null || _itemId == null) return 0;
            Type pickUp = GameBridge.FindGameType("TheForest.Items.World.PickUp");
            if (pickUp == null) return 0;

            List<PickupMatch.Entry> captured = new List<PickupMatch.Entry>();
            foreach (string key in present)
            {
                PickupMatch.Entry e;
                if (PickupMatch.TryParse(key, out e) && item(e.Id)) captured.Add(e);
            }

            List<Component> pickups = new List<Component>();
            List<GameObject> targets = new List<GameObject>();
            List<PickupMatch.Entry> live = new List<PickupMatch.Entry>();
            List<bool> removable = new List<bool>();
            HashSet<string> seen = new HashSet<string>();
            UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(pickUp);
            for (int i = 0; i < all.Length; i++)
            {
                Component c = all[i] as Component;
                if (c == null) continue;
                PickupMatch.Entry e;
                e.Id = 0;
                try { e.Id = (int)_itemId.GetValue(c); }
                catch (Exception) { continue; }
                if (!item(e.Id)) continue;
                GameObject target = null;
                try { target = _destroyTarget.GetValue(c) as GameObject; }
                catch (Exception) { }
                if (target == null) target = c.gameObject;
                if (!target.activeInHierarchy || HasIdentifier(target)) continue;
                e.Position = target.transform.position;
                if (!seen.Add(SavestateFile.PickupKey(e.Id, e.Position.x, e.Position.y, e.Position.z))) continue;
                pickups.Add(c);
                targets.Add(target);
                live.Add(e);
                // Every one is matched; only those `where` accepts may go
                // (a greeble stick's captured entry must not account for a
                // new sapling stick beside it).
                removable.Add(where(target));
            }
            bool[] remove = PickupMatch.Removable(captured, live, removable, MatchNear, farToo);

            int n = 0;
            for (int i = 0; i < live.Count; i++)
            {
                if (!remove[i]) continue;
                if (targets[i].transform.root.name == "Pooling" && _clearOut != null)
                {
                    // Not kept for a restore: this one is meant to go.
                    bool armed = Armed;
                    Armed = false;
                    try { _clearOut.Invoke(pickups[i], new object[] { false }); }
                    finally { Armed = armed; }
                }
                else UnityEngine.Object.Destroy(targets[i]);
                if (counts != null)
                {
                    int k;
                    counts.TryGetValue(live[i].Id, out k);
                    counts[live[i].Id] = k + 1;
                }
                n++;
            }
            return n;
        }

        /// After a load restore: a load re-creates every placed pickup
        /// (they are not in the save), so cash, tape etc. taken before the
        /// capture came back (maks, v0.24.25; bridge, 2026-09-24: three
        /// `PickUps/Cash` and a `Tape_Roll` back). Removes the live pickups
        /// the capture's list does not account for (Data/PickupMatch: by
        /// item, nearest first - positions move a little across loads);
        /// placed ones only in scenes that were loaded at capture (the list
        /// covers only those). `counts` gets item id -> how many.
        /// `skipped` (optional, 3 long): kept because they have an
        /// identifier, are clones outside the rule below, or lie in a scene
        /// not loaded at capture.
        public int RemoveTakenAfterLoad(HashSet<string> present, ICollection<string> scenesAtCapture, Dictionary<int, int> counts,
                                        int[] skipped, Vector3 capturedAt)
        {
            if (_destroyTarget == null || _itemId == null) return 0;
            Type pickUp = GameBridge.FindGameType("TheForest.Items.World.PickUp");
            if (pickUp == null) return 0;

            List<PickupMatch.Entry> captured = new List<PickupMatch.Entry>();
            foreach (string key in present)
            {
                PickupMatch.Entry e;
                if (PickupMatch.TryParse(key, out e)) captured.Add(e);
            }

            List<Component> pickups = new List<Component>();
            List<GameObject> targets = new List<GameObject>();
            List<PickupMatch.Entry> live = new List<PickupMatch.Entry>();
            HashSet<string> seen = new HashSet<string>();
            UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(pickUp);
            for (int i = 0; i < all.Length; i++)
            {
                Component c = all[i] as Component;
                if (c == null) continue;
                GameObject target = null;
                try { target = _destroyTarget.GetValue(c) as GameObject; }
                catch (Exception) { }
                if (target == null) target = c.gameObject;
                if (!target.activeInHierarchy) continue;
                // As Snapshot: the serializer owns these, the capture never
                // lists them - paired, one took a listed pickup's place and
                // that one was removed (bridge: skulls, a Timmy drawing).
                if (HasIdentifier(target)) { if (skipped != null) skipped[0]++; continue; }
                PickupMatch.Entry e;
                e.Id = 0;
                try { e.Id = (int)_itemId.GetValue(c); }
                catch (Exception) { }
                e.Position = target.transform.position;
                // The capture's list is a set of keys: one object with two
                // PickUp components (skulls, a Timmy drawing, a photo) is
                // listed once, so it is matched once here too - counted
                // twice, the "extra" one destroyed the object (bridge,
                // v0.24.53).
                string key = SavestateFile.PickupKey(e.Id, e.Position.x, e.Position.y, e.Position.z);
                if (!seen.Add(key)) continue;
                pickups.Add(c);
                targets.Add(target);
                live.Add(e);
            }
            bool[] matched = PickupMatch.Match(captured, live, MatchNear);

            int n = 0;
            for (int i = 0; i < live.Count; i++)
            {
                if (matched[i]) continue;
                Component c = pickups[i];
                GameObject target = targets[i];
                // Pooled greebles (Pool_Greebles/Cash(Clone), Coins(Clone))
                // come back on their seeded spots after a load (maks: cave
                // 5's first pile; bridge: taken cash and coins back at the
                // same positions). A greeble can re-roll its item, so a clone
                // on a spot the capture had anything on stays; and only near
                // the captured spot, where its zone was surely loaded then.
                bool clone = target.name.IndexOf("(Clone)", StringComparison.Ordinal) >= 0 ||
                             c.gameObject.name.IndexOf("(Clone)", StringComparison.Ordinal) >= 0;
                if (clone)
                {
                    bool near = (live[i].Position - capturedAt).sqrMagnitude <= CloneRadius * CloneRadius;
                    if (!near || _clearOut == null || PickupMatch.SpotTaken(captured, live[i].Position, SameSpot))
                    { if (skipped != null) skipped[1]++; continue; }
                }
                else if (!scenesAtCapture.Contains(target.scene.name)) { if (skipped != null) skipped[2]++; continue; }

                int k;
                counts.TryGetValue(live[i].Id, out k);
                counts[live[i].Id] = k + 1;
                // A pooled clone goes back to its pool the game's way (as
                // when picked up); a placed pickup is destroyed.
                if (clone) _clearOut.Invoke(c, new object[] { false });
                else UnityEngine.Object.Destroy(target);
                n++;
            }
            return n;
        }

        private const float MatchNear = 2.5f;
        private const float SameSpot = 0.1f;
        private const float CloneRadius = 50f;

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
