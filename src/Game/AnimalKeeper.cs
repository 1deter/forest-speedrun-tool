using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // A kept animal's health, hurt skin and poison across a Quick load
    // (T-0425; game-notes *An animal's health, hurt skin and poison*).
    // `animalHealth` is not in the save and a Quick load touches none of
    // it, and T-0415 keeps the animal object, so what happened to it since
    // the capture stayed. The capture writes each live animal's health, the
    // four _Damage floats of its skin's property block and whether it was
    // poisoned (Data/AnimalStates); a Quick load of the same world and
    // launch puts health and skin back and stops a poison started since.
    // Poison running at the capture is left as it is (the game keeps no
    // time left).
    //
    // T-0428 (game-notes *An animal's death, spawn and pool*): the capture
    // also writes where each zone animal stood and in which zone, each
    // zone's cap and respawn timers and the scene's animal counters
    // (Data/AnimalSpawns). A Quick load of the same world and launch then
    // removes a zone animal spawned since (the game's own despawn), brings
    // back one killed since (the same pool root pulled to the pool's head,
    // spawned at its spot, set up as the game does), moves the others back
    // and puts the zones' caps and the counters as they were. Birds, fish,
    // turtles and zone-less animals are left. Reflection only; no game name
    // leaves this file.
    // ------------------------------------------------------------------
    public sealed class AnimalKeeper
    {
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly string[] DamageNames = { "_Damage1", "_Damage2", "_Damage3", "_Damage4" };
        private static readonly string[] CountNames =
        {
            "currLizardAmount", "currTurtleAmount", "currRabbitAmount", "currTortoiseAmount", "currRaccoonAmount", "currDeerAmount",
            "maxLizardAmount", "maxTurtleAmount", "maxRabbitAmount", "maxSharkAmount", "maxTortoiseAmount", "maxRaccoonAmount", "maxDeerAmount",
        };

        // A root the pool made new for a brought-back animal (its old root
        // was culled): new instance id -> the captured id it stands for. In
        // memory, this launch (like PickupKeeper.RemadeFrom).
        private static readonly Dictionary<int, int> StandIn = new Dictionary<int, int>();
        private static readonly Dictionary<int, GameObject> StandObj = new Dictionary<int, GameObject>();

        private readonly ManualLogSource _log;
        private Type _type;
        private FieldInfo _health, _skin;
        private bool _resolved, _ok;
        private MaterialPropertyBlock _block;

        // The spawn side (T-0428).
        private bool _spawnResolved, _spawnOk;
        private Type _ctrlType, _zoneType, _despType, _aiType, _funcType, _poolMgrType, _sceneType, _trackerType;
        private FieldInfo _ctrlZones, _zoneMax, _zoneSpawned, _zoneBacks, _despZone, _aiTr, _sceneTracker;
        private MethodInfo _onSpawned, _updateSpawnState, _despawnFn, _getPrefab, _spawn;
        private FieldInfo _prefabPoolsField, _poolPrefab, _poolDespawned;
        private PropertyInfo _prefabPoolsProp;
        private MethodInfo _backsEnqueue, _backsClear;

        public AnimalKeeper(ManualLogSource log) { _log = log; }

        private bool Resolve()
        {
            if (_resolved) return _ok;
            _resolved = true;
            try
            {
                _type = GameBridge.FindGameType("animalHealth");
                if (_type != null)
                {
                    _health = _type.GetField("Health", Inst);
                    _skin = _type.GetField("mySkin", Inst);
                }
            }
            catch (Exception ex) { _log.LogWarning("AnimalKeeper: resolving failed: " + ex.Message); }
            _ok = _type != null && _health != null && _health.FieldType == typeof(int);
            if (!_ok) _log.LogWarning("AnimalKeeper: animalHealth.Health not found; animals are left as they are.");
            return _ok;
        }

        private bool ResolveSpawn()
        {
            if (_spawnResolved) return _spawnOk;
            _spawnResolved = true;
            try
            {
                _ctrlType = GameBridge.FindGameType("AnimalSpawnController");
                _zoneType = GameBridge.FindGameType("AnimalSpawnZone");
                _despType = GameBridge.FindGameType("AnimalDespawner");
                _aiType = GameBridge.FindGameType("animalAI");
                _funcType = GameBridge.FindGameType("animalSpawnFunctions");
                _poolMgrType = GameBridge.FindGameType("PathologicalGames.PoolManager");
                Type spawnPool = GameBridge.FindGameType("PathologicalGames.SpawnPool");
                Type prefabPool = GameBridge.FindGameType("PathologicalGames.PrefabPool");
                _sceneType = GameBridge.FindGameType("TheForest.Utils.Scene");
                _trackerType = GameBridge.FindGameType("sceneTracker");
                if (_ctrlType == null || _zoneType == null || _despType == null || _poolMgrType == null || spawnPool == null || prefabPool == null)
                    throw new Exception("a spawn type not found");
                _ctrlZones = _ctrlType.GetField("Zones", Inst);
                _onSpawned = _ctrlType.GetMethod("OnSpawnedFromAssetBundle", Inst);
                _zoneMax = _zoneType.GetField("MaxAnimalsTotal", Inst);
                _zoneSpawned = _zoneType.GetField("SpawnedAnimals", Inst);
                _zoneBacks = _zoneType.GetField("AddSpawnBackTimes", Inst);
                _despZone = _despType.GetField("SpawnedFromZone", Inst);
                _updateSpawnState = _despType.GetMethod("UpdateSpawnState", Inst, null, new[] { typeof(bool) }, null);
                if (_aiType != null) _aiTr = _aiType.GetField("animatorTr", Inst);
                if (_funcType != null) _despawnFn = _funcType.GetMethod("despawn", Inst, null, Type.EmptyTypes, null);
                if (_sceneType != null) _sceneTracker = _sceneType.GetField("SceneTracker", Stat);
                _getPrefab = spawnPool.GetMethod("GetPrefab", Inst, null, new[] { typeof(Transform) }, null);
                _spawn = spawnPool.GetMethod("Spawn", Inst, null, new[] { typeof(Transform), typeof(Vector3), typeof(Quaternion) }, null);
                _prefabPoolsProp = spawnPool.GetProperty("prefabPools", Inst);
                if (_prefabPoolsProp == null) _prefabPoolsField = spawnPool.GetField("prefabPools", Inst);
                _poolPrefab = prefabPool.GetField("prefab", Inst);
                _poolDespawned = prefabPool.GetField("_despawned", Inst);
                if (_zoneBacks != null)
                {
                    _backsEnqueue = _zoneBacks.FieldType.GetMethod("Enqueue", Inst);
                    _backsClear = _zoneBacks.FieldType.GetMethod("Clear", Inst);
                }
                if (_ctrlZones == null || _onSpawned == null || _zoneMax == null || _zoneSpawned == null || _zoneBacks == null ||
                    _despZone == null || _updateSpawnState == null || _getPrefab == null || _spawn == null ||
                    (_prefabPoolsProp == null && _prefabPoolsField == null) || _poolPrefab == null || _poolDespawned == null ||
                    _backsEnqueue == null || _backsClear == null)
                    throw new Exception("a spawn member not found");
                _spawnOk = true;
            }
            catch (Exception ex)
            {
                _log.LogWarning("AnimalKeeper: spawn side unresolved (" + ex.Message + "); animals killed, spawned or moved since a capture are left as they are.");
            }
            return _spawnOk;
        }

        private Renderer SkinOf(Component ah)
        {
            return _skin != null ? _skin.GetValue(ah) as Renderer : null;
        }

        private float[] ReadSkin(Component ah)
        {
            float[] d = new float[4];
            Renderer r = SkinOf(ah);
            if (r == null) return d;
            if (_block == null) _block = new MaterialPropertyBlock();
            r.GetPropertyBlock(_block);
            for (int i = 0; i < 4; i++) d[i] = _block.GetFloat(DamageNames[i]);
            return d;
        }

        private static bool Poisoned(Component ah)
        {
            MonoBehaviour mb = ah as MonoBehaviour;
            return mb != null && mb.IsInvoking("HitPoison");
        }

        /// The `animals` line: every live (active) animal now.
        public string Capture()
        {
            if (!Resolve()) return "";
            try
            {
                UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(_type);
                List<AnimalStates.Entry> list = new List<AnimalStates.Entry>();
                for (int i = 0; i < all.Length; i++)
                {
                    Component ah = all[i] as Component;
                    if (ah == null || ah.gameObject == null) continue;
                    AnimalStates.Entry e = new AnimalStates.Entry();
                    e.Id = ah.gameObject.GetInstanceID();
                    e.Health = (int)_health.GetValue(ah);
                    e.Damage = ReadSkin(ah);
                    e.Poisoned = Poisoned(ah);
                    list.Add(e);
                }
                return AnimalStates.Format(list);
            }
            catch (Exception ex)
            {
                _log.LogWarning("AnimalKeeper: capture failed: " + ex.Message);
                return "";
            }
        }

        // ---------------- T-0428: poses, zones, counters ----------------

        private List<object> ReadPools()
        {
            List<object> list = new List<object>();
            object dict = null;
            FieldInfo f = _poolMgrType.GetField("Pools", Stat);
            if (f != null) dict = f.GetValue(null);
            else
            {
                PropertyInfo p = _poolMgrType.GetProperty("Pools", Stat);
                if (p != null) dict = p.GetValue(null, null);
            }
            IEnumerable en = dict as IEnumerable;
            if (en == null) return list;
            foreach (object e in en)
            {
                if (e == null) continue;
                object pool = e;
                if (!(e is Component))
                {
                    PropertyInfo v = e.GetType().GetProperty("Value");
                    pool = v != null ? v.GetValue(e, null) : null;
                }
                if (pool != null) list.Add(pool);
            }
            return list;
        }

        private IDictionary PrefabPoolsOf(object pool)
        {
            object d = _prefabPoolsProp != null ? _prefabPoolsProp.GetValue(pool, null) : _prefabPoolsField.GetValue(pool);
            return d as IDictionary;
        }

        /// The prefab's name the root was spawned from ("-" when unknown).
        private string PrefabNameOf(List<object> pools, Transform root)
        {
            for (int i = 0; i < pools.Count; i++)
            {
                Transform prefab = null;
                try { prefab = _getPrefab.Invoke(pools[i], new object[] { root }) as Transform; }
                catch (Exception) { }
                if (prefab != null) return AnimalSpawns.NameOk(prefab.name) ? prefab.name : "-";
            }
            return "-";
        }

        private Quaternion FacingOf(GameObject go)
        {
            Transform t = AnimatorOf(go);
            return t != null ? t.rotation : go.transform.rotation;
        }

        private Transform AnimatorOf(GameObject go)
        {
            if (_aiType == null || _aiTr == null) return null;
            Component ai = go.GetComponent(_aiType);
            if (ai == null) ai = go.GetComponentInChildren(_aiType, true);
            return ai != null ? _aiTr.GetValue(ai) as Transform : null;
        }

        private Array ZonesNow()
        {
            UnityEngine.Object c = UnityEngine.Object.FindObjectOfType(_ctrlType);
            return c != null ? _ctrlZones.GetValue(c) as Array : null;
        }

        private object TrackerNow()
        {
            if (_sceneTracker == null) return null;
            object t = _sceneTracker.GetValue(null);
            return (t as UnityEngine.Object) != null ? t : null;
        }

        /// The `animalposes`, `animalzones` and `animalcounts` lines.
        public void CaptureSpawns(out string poses, out string zones, out string counts)
        {
            poses = ""; zones = ""; counts = "";
            if (!Resolve() || !ResolveSpawn()) return;
            try
            {
                Array zoneArr = ZonesNow();
                Dictionary<int, int> zoneIndex = new Dictionary<int, int>();
                List<AnimalSpawns.ZoneState> zoneStates = new List<AnimalSpawns.ZoneState>();
                if (zoneArr != null)
                {
                    float now = Time.time;
                    for (int i = 0; i < zoneArr.Length; i++)
                    {
                        Component z = zoneArr.GetValue(i) as Component;
                        if (z == null) continue;
                        zoneIndex[z.GetInstanceID()] = i;
                        AnimalSpawns.ZoneState zs = new AnimalSpawns.ZoneState();
                        zs.Index = i;
                        zs.Max = (int)_zoneMax.GetValue(z);
                        IEnumerable list = _zoneSpawned.GetValue(z) as IEnumerable;
                        if (list != null)
                            foreach (object o in list)
                            {
                                GameObject g = o as GameObject;
                                if (g != null) zs.Ids.Add(g.GetInstanceID());
                            }
                        IEnumerable backs = _zoneBacks.GetValue(z) as IEnumerable;
                        if (backs != null)
                            foreach (object o in backs) zs.Backs.Add(Convert.ToSingle(o) - now);
                        zoneStates.Add(zs);
                    }
                }
                zones = AnimalSpawns.FormatZones(zoneStates);

                List<object> pools = ReadPools();
                UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(_type);
                List<AnimalSpawns.Pose> list2 = new List<AnimalSpawns.Pose>();
                for (int i = 0; i < all.Length; i++)
                {
                    Component ah = all[i] as Component;
                    if (ah == null || ah.gameObject == null) continue;
                    GameObject go = ah.gameObject;
                    AnimalSpawns.Pose p = new AnimalSpawns.Pose();
                    p.Id = go.GetInstanceID();
                    p.Prefab = PrefabNameOf(pools, go.transform);
                    Component d = go.GetComponent(_despType);
                    Component zone = d != null ? _despZone.GetValue(d) as Component : null;
                    int idx;
                    p.Zone = zone != null && zoneIndex.TryGetValue(zone.GetInstanceID(), out idx) ? idx : -1;
                    Vector3 pos = go.transform.position;
                    p.Pos = new[] { pos.x, pos.y, pos.z };
                    Quaternion q = FacingOf(go);
                    p.Rot = new[] { q.x, q.y, q.z, q.w };
                    list2.Add(p);
                }
                poses = AnimalSpawns.FormatPoses(list2);

                object tracker = TrackerNow();
                if (tracker != null)
                {
                    List<KeyValuePair<string, int>> cs = new List<KeyValuePair<string, int>>();
                    for (int i = 0; i < CountNames.Length; i++)
                    {
                        FieldInfo cf = _trackerType.GetField(CountNames[i], Inst);
                        if (cf != null && cf.FieldType == typeof(int)) cs.Add(new KeyValuePair<string, int>(CountNames[i], (int)cf.GetValue(tracker)));
                    }
                    counts = AnimalSpawns.FormatCounts(cs);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning("AnimalKeeper: capture of spawns failed: " + ex.Message);
                poses = ""; zones = ""; counts = "";
            }
        }

        // The ids the file being restored holds; a root with one of them
        // counts as itself, whatever an older restore's stand-in map says.
        private static readonly HashSet<int> CapIds = new HashSet<int>();

        private static int Mapped(int id)
        {
            return AnimalSpawns.Resolve(id, CapIds, StandIn);
        }

        private static void ReadCapIds(string line, string posesLine, string zonesLine)
        {
            CapIds.Clear();
            List<AnimalStates.Entry> es = AnimalStates.Parse(line);
            for (int i = 0; i < es.Count; i++) CapIds.Add(es[i].Id);
            List<AnimalSpawns.Pose> ps = AnimalSpawns.ParsePoses(posesLine);
            for (int i = 0; i < ps.Count; i++) CapIds.Add(ps[i].Id);
            List<AnimalSpawns.ZoneState> zs = AnimalSpawns.ParseZones(zonesLine);
            for (int i = 0; i < zs.Count; i++)
                for (int k = 0; k < zs[i].Ids.Count; k++) CapIds.Add(zs[i].Ids[k]);
        }

        private static void PruneStandIns()
        {
            List<int> gone = new List<int>();
            foreach (KeyValuePair<int, GameObject> kv in StandObj)
                if (kv.Value == null) gone.Add(kv.Key);
            for (int i = 0; i < gone.Count; i++) { StandObj.Remove(gone[i]); StandIn.Remove(gone[i]); }
        }

        /// Live animal roots now, by id as the capture knew it (a stand-in
        /// root under the id it stands for).
        private Dictionary<int, Component> LiveMap(out List<Component> raw)
        {
            UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(_type);
            Dictionary<int, Component> live = new Dictionary<int, Component>();
            raw = new List<Component>();
            for (int i = 0; i < all.Length; i++)
            {
                Component ah = all[i] as Component;
                if (ah == null || ah.gameObject == null) continue;
                raw.Add(ah);
                live[Mapped(ah.gameObject.GetInstanceID())] = ah;
            }
            return live;
        }

        private void MoveTo(GameObject go, AnimalSpawns.Pose p)
        {
            go.transform.position = new Vector3(p.Pos[0], p.Pos[1], p.Pos[2]);
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            Transform an = AnimatorOf(go);
            if (an != null) an.rotation = new Quaternion(p.Rot[0], p.Rot[1], p.Rot[2], p.Rot[3]);
        }

        /// Brings one captured animal back; true when it stands again.
        private bool BringBack(List<object> pools, AnimalSpawns.Pose p, Array zoneArr)
        {
            if (p.Zone < 0)
            {
                _log.LogInfo("AnimalKeeper: a " + p.Prefab + " with no zone (killed since) is not brought back.");
                return false;
            }
            if (zoneArr == null || p.Zone >= zoneArr.Length) return false;
            Component zone = zoneArr.GetValue(p.Zone) as Component;
            if (zone == null) return false;
            for (int i = 0; i < pools.Count; i++)
            {
                IDictionary pp = PrefabPoolsOf(pools[i]);
                if (pp == null || !pp.Contains(p.Prefab)) continue;
                object prefabPool = pp[p.Prefab];
                Transform prefab = prefabPool != null ? _poolPrefab.GetValue(prefabPool) as Transform : null;
                IList despawned = prefabPool != null ? _poolDespawned.GetValue(prefabPool) as IList : null;
                if (prefab == null) return false;
                if (despawned != null)
                {
                    for (int k = 0; k < despawned.Count; k++)
                    {
                        Transform x = despawned[k] as Transform;
                        if (x == null || x.gameObject.GetInstanceID() != p.Id) continue;
                        despawned.RemoveAt(k);
                        despawned.Insert(0, x);
                        break;
                    }
                }
                Vector3 pos = new Vector3(p.Pos[0], p.Pos[1], p.Pos[2]);
                Transform r = _spawn.Invoke(pools[i], new object[] { prefab, pos, Quaternion.identity }) as Transform;
                if (r == null) return false;
                GameObject go = r.gameObject;
                int rid = go.GetInstanceID();
                if (rid != p.Id) { StandIn[rid] = p.Id; StandObj[rid] = go; }
                // Listed once only: the game's setup adds it again.
                IList spawned = _zoneSpawned.GetValue(zone) as IList;
                if (spawned != null)
                    for (int k = spawned.Count - 1; k >= 0; k--)
                        if (ReferenceEquals(spawned[k], go) || (spawned[k] as GameObject) == go) spawned.RemoveAt(k);
                _onSpawned.Invoke(CtrlOf(), new object[] { go, zone });
                MoveTo(go, p);
                return true;
            }
            return false;
        }

        private UnityEngine.Object _ctrlCache;
        private UnityEngine.Object CtrlOf()
        {
            if (_ctrlCache == null) _ctrlCache = UnityEngine.Object.FindObjectOfType(_ctrlType);
            return _ctrlCache;
        }

        // Zone animals only (they have an AnimalDespawner); a zone-less one
        // is left alone.
        private bool Despawn(Component ah)
        {
            Component d = ah.gameObject.GetComponent(_despType);
            if (d == null) return false;
            _updateSpawnState.Invoke(d, new object[] { false });
            return true;
        }

        private int ResetZones(string zonesLine, Array zoneArr)
        {
            if (zoneArr == null) return 0;
            List<AnimalSpawns.ZoneState> states = AnimalSpawns.ParseZones(zonesLine);
            // Every root a zone could list: its current entries, the live ones.
            Dictionary<int, GameObject> roots = new Dictionary<int, GameObject>();
            List<Component> raw;
            Dictionary<int, Component> live = LiveMap(out raw);
            foreach (KeyValuePair<int, Component> kv in live) roots[kv.Key] = kv.Value.gameObject;
            for (int i = 0; i < zoneArr.Length; i++)
            {
                Component z = zoneArr.GetValue(i) as Component;
                IEnumerable cur = z != null ? _zoneSpawned.GetValue(z) as IEnumerable : null;
                if (cur == null) continue;
                foreach (object o in cur)
                {
                    GameObject g = o as GameObject;
                    if (g != null && !roots.ContainsKey(Mapped(g.GetInstanceID()))) roots[Mapped(g.GetInstanceID())] = g;
                }
            }
            float now = Time.time;
            int reset = 0;
            for (int s = 0; s < states.Count; s++)
            {
                AnimalSpawns.ZoneState st = states[s];
                if (st.Index < 0 || st.Index >= zoneArr.Length) continue;
                Component z = zoneArr.GetValue(st.Index) as Component;
                if (z == null) continue;
                bool changed = (int)_zoneMax.GetValue(z) != st.Max;
                _zoneMax.SetValue(z, st.Max);

                IList spawned = _zoneSpawned.GetValue(z) as IList;
                if (spawned != null)
                {
                    List<int> have = new List<int>();
                    foreach (object o in spawned)
                    {
                        GameObject g = o as GameObject;
                        if (g != null) have.Add(Mapped(g.GetInstanceID()));
                    }
                    List<GameObject> want = new List<GameObject>();
                    for (int k = 0; k < st.Ids.Count; k++)
                    {
                        GameObject g;
                        if (roots.TryGetValue(st.Ids[k], out g) && g != null) want.Add(g);
                    }
                    bool same = have.Count == want.Count;
                    for (int k = 0; same && k < want.Count; k++)
                        if (have[k] != Mapped(want[k].GetInstanceID())) same = false;
                    if (!same)
                    {
                        changed = true;
                        spawned.Clear();
                        for (int k = 0; k < want.Count; k++) spawned.Add(want[k]);
                    }
                }

                IEnumerable oldBacks = _zoneBacks.GetValue(z) as IEnumerable;
                int oldCount = 0;
                if (oldBacks != null) foreach (object o in oldBacks) oldCount++;
                if (oldCount != st.Backs.Count) changed = true;
                object queue = _zoneBacks.GetValue(z);
                if (queue != null)
                {
                    _backsClear.Invoke(queue, null);
                    for (int k = 0; k < st.Backs.Count; k++)
                        _backsEnqueue.Invoke(queue, new object[] { now + st.Backs[k] });
                }
                if (changed) reset++;
            }
            return reset;
        }

        private void ResetCounts(string countsLine)
        {
            object tracker = TrackerNow();
            if (tracker == null) return;
            List<KeyValuePair<string, int>> cs = AnimalSpawns.ParseCounts(countsLine);
            for (int i = 0; i < cs.Count; i++)
            {
                FieldInfo f = _trackerType.GetField(cs[i].Key, Inst);
                if (f != null && f.FieldType == typeof(int)) f.SetValue(tracker, cs[i].Value);
            }
        }

        /// After a Quick load; `line` is the file's `animals` line (null: an
        /// older file), the next three the T-0428 lines (null: older too),
        /// `sameWorld` that the file is from this world and launch
        /// (instance ids mean something). Returns the log note.
        public string Restore(string line, string posesLine, string zonesLine, string countsLine, bool sameWorld)
        {
            if (!sameWorld || !Resolve()) return "";
            try
            {
                int brought = 0, removed = 0, moved = 0, zonesReset = 0;
                ReadCapIds(line, posesLine, zonesLine);
                bool spawns = !string.IsNullOrEmpty(zonesLine) && ResolveSpawn();
                if (spawns)
                {
                    try { SpawnSteps(posesLine, zonesLine, countsLine, out brought, out removed, out moved, out zonesReset); }
                    catch (Exception ex) { _log.LogWarning("AnimalKeeper: spawn restore failed: " + ex.Message); }
                }
                string putBack = HealthStep(line);
                return AnimalSpawns.Describe(putBack, brought, removed, moved, zonesReset);
            }
            catch (Exception ex)
            {
                _log.LogWarning("AnimalKeeper: restore failed: " + ex.Message);
                return "animals: failed (" + ex.Message + ")";
            }
        }

        private void SpawnSteps(string posesLine, string zonesLine, string countsLine,
                                out int brought, out int removed, out int moved, out int zonesReset)
        {
            brought = removed = moved = zonesReset = 0;
            PruneStandIns();
            _ctrlCache = null;
            List<AnimalSpawns.Pose> poses = AnimalSpawns.ParsePoses(posesLine);
            Dictionary<int, AnimalSpawns.Pose> byId = new Dictionary<int, AnimalSpawns.Pose>();
            List<int> captured = new List<int>();
            for (int i = 0; i < poses.Count; i++) { byId[poses[i].Id] = poses[i]; captured.Add(poses[i].Id); }

            List<Component> raw;
            Dictionary<int, Component> live = LiveMap(out raw);
            List<int> liveIds = new List<int>();
            for (int i = 0; i < raw.Count; i++) liveIds.Add(raw[i].gameObject.GetInstanceID());
            AnimalSpawns.Plan plan = AnimalSpawns.MakePlan(captured, liveIds, StandIn);
            Array zoneArr = ZonesNow();

            // 1. Despawn what was spawned since.
            for (int i = 0; i < raw.Count; i++)
            {
                if (!plan.Despawn.Contains(raw[i].gameObject.GetInstanceID())) continue;
                try { if (Despawn(raw[i])) removed++; }
                catch (Exception ex) { _log.LogWarning("AnimalKeeper: despawn failed: " + ex.Message); }
            }

            // 2. Bring back what died since.
            if (plan.Respawn.Count > 0)
            {
                List<object> pools = ReadPools();
                for (int i = 0; i < plan.Respawn.Count; i++)
                {
                    try { if (BringBack(pools, byId[plan.Respawn[i]], zoneArr)) brought++; }
                    catch (Exception ex) { _log.LogWarning("AnimalKeeper: bring back failed: " + ex.Message); }
                }
            }

            // 3. Move the kept ones back (only those that are off their spot).
            live = LiveMap(out raw);
            for (int i = 0; i < plan.Move.Count; i++)
            {
                Component ah;
                AnimalSpawns.Pose p;
                if (!live.TryGetValue(plan.Move[i], out ah) || ah == null || !byId.TryGetValue(plan.Move[i], out p)) continue;
                Vector3 want = new Vector3(p.Pos[0], p.Pos[1], p.Pos[2]);
                if ((ah.transform.position - want).sqrMagnitude < 0.25f * 0.25f) continue;
                MoveTo(ah.gameObject, p);
                moved++;
            }

            // 5. The zones' caps, timers and lists, then the counters
            // (the health step, 4, runs after this).
            zonesReset = ResetZones(zonesLine, zoneArr);
            if (!string.IsNullOrEmpty(countsLine)) ResetCounts(countsLine);
        }

        private string HealthStep(string line)
        {
            if (string.IsNullOrEmpty(line)) return "";
            List<AnimalStates.Entry> entries = AnimalStates.Parse(line);
            if (entries.Count == 0) return "";
            List<Component> raw;
            Dictionary<int, Component> live = LiveMap(out raw);
            int animals = 0, health = 0, skin = 0, poison = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                AnimalStates.Entry e = entries[i];
                Component ah;
                if (!live.TryGetValue(e.Id, out ah) || ah == null) continue;
                bool changed = false;
                if ((int)_health.GetValue(ah) != e.Health)
                {
                    _health.SetValue(ah, e.Health);
                    health++; changed = true;
                }
                float[] now = ReadSkin(ah);
                bool differs = false;
                for (int k = 0; k < 4; k++) if (now[k] != e.Damage[k]) differs = true;
                Renderer r = SkinOf(ah);
                if (differs && r != null)
                {
                    r.GetPropertyBlock(_block);
                    for (int k = 0; k < 4; k++) _block.SetFloat(DamageNames[k], e.Damage[k]);
                    r.SetPropertyBlock(_block);
                    skin++; changed = true;
                }
                MonoBehaviour mb = ah as MonoBehaviour;
                if (!e.Poisoned && mb != null && (mb.IsInvoking("HitPoison") || mb.IsInvoking("disablePoison")))
                {
                    mb.CancelInvoke("HitPoison");
                    mb.CancelInvoke("disablePoison");
                    poison++; changed = true;
                }
                if (changed) animals++;
            }
            return AnimalStates.Describe(animals, health, skin, poison);
        }
    }
}
