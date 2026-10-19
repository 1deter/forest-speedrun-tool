using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using ForestOverlay.Data;
using HarmonyLib;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // 24-27. Four small garbage makers in the game (T-0203), each exact:
    // the game computes the same answer, it just stops allocating for it.
    // Measured 2026-10-08 (Slot 2, tracker): 3.8 + 3.8 + 3.2 + 7.5 KB/s.
    // Why each is exact: docs/game-notes.md *Garbage in play*.
    //
    // 24. OceanReuseConditionKey - Ceto.WaveSpectrum.NewSpectrumConditionKey
    //     builds a key object per Update only to be compared by value; for
    //     the Unified spectra the kept key is returned while the six inputs
    //     are equal (Data/OceanKeyMemo).
    // 25. CaveSpawnSortNoClosure - mutantController.sortCaveSpawnsByDistance
    //     makes a closure and a Comparison per call; the same RemoveAll /
    //     TrimExcess / List.Sort(Comparison) with one kept comparison.
    // 26. TerrainTextureCache - TerrainHelper.GetProminantTextureIndex reads
    //     a one-cell alphamap (a new float[1,1,L]) every fixed step; the
    //     answer per cell is kept (Data/TerrainCells) until SetAlphamaps.
    // 27. FishSpawnNoIdleCoroutine - animalController.doSpawnCreatures
    //     starts the "spawnFish" coroutine every 4 s; in single player a
    //     gate does its first segment inline when it would only write a few
    //     fields and end, and starts the coroutine when it would spawn or
    //     despawn.
    // ------------------------------------------------------------------
    public static class GarbagePatches
    {
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Own = BindingFlags.Static | BindingFlags.NonPublic;

        private static HarmonyMethod Mine(string name)
        {
            return new HarmonyMethod(typeof(GarbagePatches).GetMethod(name, Own));
        }

        private static HarmonyMethod Mine(string name, Type generic)
        {
            return new HarmonyMethod(typeof(GarbagePatches).GetMethod(name, Own).MakeGenericMethod(generic));
        }

        // ------------------------------------------------------------------
        // 24. Ocean key
        private static readonly OceanKeyMemo OceanMemo = new OceanKeyMemo();
        private static MethodInfo _oceanKey;
        private static Func<object, int> _oceanType, _oceanGrids;

        public static string ApplyOcean(Harmony harmony)
        {
            Type t = GameBridge.FindGameType("Ceto.WaveSpectrum");
            if (t == null) return "Ceto.WaveSpectrum not found";
            MethodInfo m = t.GetMethod("NewSpectrumConditionKey", Inst, null, new[] { typeof(int), typeof(float), typeof(float), typeof(float) }, null);
            if (m == null) return "NewSpectrumConditionKey not found";
            if (m.ReturnType.IsValueType || m.ReturnType == typeof(void)) return "NewSpectrumConditionKey does not return an object";
            FieldInfo type = t.GetField("spectrumType", Inst);
            FieldInfo grids = t.GetField("numberOfGrids", Inst);
            if (type == null || !type.FieldType.IsEnum || Enum.GetUnderlyingType(type.FieldType) != typeof(int)) return "spectrumType not found";
            if (grids == null || grids.FieldType != typeof(int)) return "numberOfGrids not found";
            _oceanType = FastField.Instance<int>(type);
            _oceanGrids = FastField.Instance<int>(grids);
            _oceanKey = m;
            OceanMemo.Clear();
            harmony.Patch(m, Mine("OceanPrefix", m.ReturnType), Mine("OceanPostfix", m.ReturnType));
            return "";
        }

        public static void RemoveOcean(Harmony harmony)
        {
            if (_oceanKey != null) harmony.Unpatch(_oceanKey, HarmonyPatchType.All, harmony.Id);
            _oceanKey = null;
            OceanMemo.Clear();
        }

        // Generic only so that __result has the method's exact return type.
        private static bool OceanPrefix<TKey>(object __instance, int __0, float __1, float __2, float __3, ref TKey __result) where TKey : class
        {
            try
            {
                if (!OceanMemo.Matches(__instance, __0, __1, __2, __3, _oceanType(__instance), _oceanGrids(__instance))) return true;
                __result = (TKey)OceanMemo.Key;
                return false;
            }
            catch (Exception) { return true; }
        }

        private static void OceanPostfix<TKey>(object __instance, int __0, float __1, float __2, float __3, TKey __result) where TKey : class
        {
            try
            {
                // After the call: the custom spectrum can switch the type inside it.
                OceanMemo.Keep(__instance, __result, __0, __1, __2, __3, _oceanType(__instance), _oceanGrids(__instance));
            }
            catch (Exception) { OceanMemo.Clear(); }
        }

        // ------------------------------------------------------------------
        // 25. Cave spawn sort
        private static MethodInfo _caveSort;
        private static Func<object, List<GameObject>> _caveList;
        private static Transform _sortTarget;
        private static readonly Predicate<GameObject> IsNull = IsNullObject;
        private static readonly Comparison<GameObject> ByDistance = CompareByDistance;

        public static string ApplyCaveSort(Harmony harmony)
        {
            Type t = GameBridge.FindGameType("mutantController");
            if (t == null) return "mutantController not found";
            MethodInfo m = t.GetMethod("sortCaveSpawnsByDistance", Inst, null, new[] { typeof(Transform) }, null);
            FieldInfo list = t.GetField("allCaveSpawns", Inst);
            if (m == null) return "sortCaveSpawnsByDistance not found";
            if (list == null || list.FieldType != typeof(List<GameObject>)) return "allCaveSpawns not found";
            _caveList = FastField.Instance<List<GameObject>>(list);
            _caveSort = m;
            harmony.Patch(m, prefix: Mine("CaveSortPrefix"));
            return "";
        }

        public static void RemoveCaveSort(Harmony harmony)
        {
            if (_caveSort != null) harmony.Unpatch(_caveSort, HarmonyPatchType.Prefix, harmony.Id);
            _caveSort = null;
            _sortTarget = null;
        }

        private static bool IsNullObject(GameObject o) { return o == null; }

        // The game's comparison: the nearer to the target sorts first.
        private static int CompareByDistance(GameObject a, GameObject b)
        {
            Vector3 d1 = _sortTarget.position - a.transform.position;
            float m1 = d1.sqrMagnitude;
            Vector3 d2 = _sortTarget.position - b.transform.position;
            return m1.CompareTo(d2.sqrMagnitude);
        }

        private static bool CaveSortPrefix(object __instance, Transform __0)
        {
            try
            {
                List<GameObject> list = _caveList(__instance);
                if (list == null || __0 == null) return true;   // the game's own (and its error)
                list.RemoveAll(IsNull);
                list.TrimExcess();
                _sortTarget = __0;
                try { list.Sort(ByDistance); }
                finally { _sortTarget = null; }
                return false;
            }
            catch (Exception) { return true; }
        }

        // ------------------------------------------------------------------
        // 26. Terrain texture
        private static readonly TerrainCells Cells = new TerrainCells();
        private static MethodInfo _terrainIndex;
        private static MethodInfo _setAlphamaps;

        public static string ApplyTerrain(Harmony harmony)
        {
            Type t = null;
            Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < all.Length && t == null; i++)
            {
                try { t = all[i].GetType("TerrainHelper", false); }
                catch (Exception) { }
            }
            if (t == null) return "TerrainHelper not found";
            MethodInfo m = t.GetMethod("GetProminantTextureIndex", Stat, null, new[] { typeof(Terrain), typeof(Vector3) }, null);
            if (m == null || m.ReturnType != typeof(int)) return "GetProminantTextureIndex not found";
            MethodInfo set = typeof(TerrainData).GetMethod("SetAlphamaps", Inst, null, new[] { typeof(int), typeof(int), typeof(float[,,]) }, null);
            if (set == null) return "TerrainData.SetAlphamaps not found";
            Cells.Release();
            try
            {
                harmony.Patch(set, postfix: Mine("AlphamapsWrittenPostfix"));
                _setAlphamaps = set;
            }
            catch (Exception ex) { return "cannot watch TerrainData.SetAlphamaps: " + (ex.InnerException ?? ex).Message; }
            harmony.Patch(m, prefix: Mine("TerrainPrefix"));
            _terrainIndex = m;
            return "";
        }

        public static void RemoveTerrain(Harmony harmony)
        {
            if (_terrainIndex != null) harmony.Unpatch(_terrainIndex, HarmonyPatchType.Prefix, harmony.Id);
            if (_setAlphamaps != null) harmony.Unpatch(_setAlphamaps, HarmonyPatchType.Postfix, harmony.Id);
            _terrainIndex = null;
            _setAlphamaps = null;
            Cells.Release();
        }

        private static void AlphamapsWrittenPostfix()
        {
            Cells.Clear();
        }

        // The game's method, line for line, with the cell read kept.
        private static bool TerrainPrefix(Terrain __0, Vector3 __1, ref int __result)
        {
            try
            {
                if (__0 == null) { __result = -1; return false; }
                Vector3 origin = __0.GetPosition();
                TerrainData data = __0.terrainData;
                int res = data.alphamapResolution;
                int x = TerrainCells.Index(__1.x, origin.x, data.size.x, res);
                int z = TerrainCells.Index(__1.z, origin.z, data.size.z, res);
                if (!TerrainCells.InRange(x, z, res)) { __result = -1; return false; }
                int layers = data.alphamapLayers;
                if (!Cells.Bind(data, res, layers)) return true;
                int v = Cells.Get(x, z);
                if (v == TerrainCells.Unread)
                {
                    v = TerrainCells.Heaviest(data.GetAlphamaps(x, z, 1, 1), layers);
                    Cells.Set(x, z, v);
                }
                __result = v;
                return false;
            }
            catch (Exception) { return true; }
        }

        // ------------------------------------------------------------------
        // 27. Fish spawn
        private static MethodInfo _fishSpawn;
        private static Func<bool> _boltRunning, _dedicated, _inCaves;
        private static Func<Transform> _localTransform;
        private static Func<object, GameObject[]> _fishGo;
        private static Func<object, bool> _caveFish, _fishSpawned;
        private static Func<object, float> _spawnDist;
        private static Func<object, Transform> _thisTr;
        private static Func<object, List<GameObject>> _allFish;
        private static Func<object, bool, Transform> _closestPlayer;
        private static Action<object, float> _setDist;
        private static Action<object, bool> _setSpawned;
        private static Action<object, int> _setTotal;
        private static int _fishFound;

        public static string ApplyFish(Harmony harmony)
        {
            Type t = GameBridge.FindGameType("animalController");
            if (t == null) return "animalController not found";
            MethodInfo m = t.GetMethod("doSpawnCreatures", Inst, null, Type.EmptyTypes, null);
            if (m == null) return "doSpawnCreatures not found";
            FieldInfo go = t.GetField("fishGo", Inst), cave = t.GetField("caveFish", Inst), spawned = t.GetField("fishSpawned", Inst);
            FieldInfo dist = t.GetField("fishSpawnDist", Inst), tr = t.GetField("thisTr", Inst), all = t.GetField("allFish", Inst);
            FieldInfo playerDist = t.GetField("fishPlayerDist", Inst), total = t.GetField("totalFish", Inst);
            MethodInfo closest = t.GetMethod("getClosestPlayer", Inst, null, new[] { typeof(bool) }, null);
            if (go == null || go.FieldType != typeof(GameObject[]) || cave == null || cave.FieldType != typeof(bool) ||
                spawned == null || spawned.FieldType != typeof(bool) || dist == null || dist.FieldType != typeof(float) ||
                tr == null || tr.FieldType != typeof(Transform) || all == null || all.FieldType != typeof(List<GameObject>) ||
                playerDist == null || playerDist.FieldType != typeof(float) || total == null || total.FieldType != typeof(int))
                return "animalController's fish fields not found";
            if (closest == null || closest.ReturnType != typeof(Transform)) return "getClosestPlayer not found";

            Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
            Type ds = GameBridge.FindGameType("SteamDSConfig");
            Type bolt = null;
            Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length && bolt == null; i++)
            {
                try { bolt = asms[i].GetType("BoltNetwork", false); }
                catch (Exception) { }
            }
            PropertyInfo inCaves = local != null ? local.GetProperty("IsInCaves", Stat) : null;
            FieldInfo localTr = local != null ? local.GetField("Transform", Stat) : null;
            FieldInfo dedicated = ds != null ? ds.GetField("isDedicatedServer", Stat) : null;
            PropertyInfo running = bolt != null ? bolt.GetProperty("isRunning", Stat) : null;
            if (inCaves == null || inCaves.PropertyType != typeof(bool)) return "LocalPlayer.IsInCaves not found";
            if (localTr == null || localTr.FieldType != typeof(Transform)) return "LocalPlayer.Transform not found";
            if (dedicated == null || dedicated.FieldType != typeof(bool)) return "SteamDSConfig.isDedicatedServer not found";
            if (running == null || running.PropertyType != typeof(bool)) return "BoltNetwork.isRunning not found";

            _inCaves = FastField.StaticProperty<bool>(inCaves);
            _localTransform = FastField.Static<Transform>(localTr);
            _dedicated = FastField.Static<bool>(dedicated);
            _boltRunning = FastField.StaticProperty<bool>(running);
            _fishGo = FastField.Instance<GameObject[]>(go);
            _caveFish = FastField.Instance<bool>(cave);
            _fishSpawned = FastField.Instance<bool>(spawned);
            _spawnDist = FastField.Instance<float>(dist);
            _thisTr = FastField.Instance<Transform>(tr);
            _allFish = FastField.Instance<List<GameObject>>(all);
            _setDist = FastField.InstanceSetter<float>(playerDist);
            _setSpawned = FastField.InstanceSetter<bool>(spawned);
            _setTotal = FastField.InstanceSetter<int>(total);
            _closestPlayer = ClosestPlayerCaller(t, closest);
            if (_closestPlayer == null) return "getClosestPlayer cannot be called";

            _fishFound = 0;
            harmony.Patch(m, transpiler: Mine("FishTranspiler"));
            if (_fishFound == 1) { _fishSpawn = m; return ""; }
            // Not the code this was written for: the game's own code back.
            harmony.Unpatch(m, HarmonyPatchType.Transpiler, harmony.Id);
            return "expected one StartCoroutine(\"spawnFish\"), found " + _fishFound + " (game updated?)";
        }

        public static void RemoveFish(Harmony harmony)
        {
            if (_fishSpawn != null) harmony.Unpatch(_fishSpawn, HarmonyPatchType.Transpiler, harmony.Id);
            _fishSpawn = null;
        }

        // `(o, cave) => ((animalController)o).getClosestPlayer(cave)` without MethodInfo.Invoke (it boxes).
        private static Func<object, bool, Transform> ClosestPlayerCaller(Type t, MethodInfo m)
        {
            try
            {
                DynamicMethod dm = new DynamicMethod("Call_getClosestPlayer", typeof(Transform), new[] { typeof(object), typeof(bool) }, typeof(GarbagePatches).Module, true);
                ILGenerator il = dm.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, t);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, m);
                il.Emit(OpCodes.Ret);
                return (Func<object, bool, Transform>)dm.CreateDelegate(typeof(Func<object, bool, Transform>));
            }
            catch (Exception) { return null; }
        }

        // `ldstr "spawnFish"; call MonoBehaviour.StartCoroutine(string)` -> `call FishGate`.
        private static IEnumerable<CodeInstruction> FishTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> list = new List<CodeInstruction>(instructions);
            MethodInfo gate = typeof(GarbagePatches).GetMethod("FishGate", Own);
            int found = 0;
            for (int i = 1; i < list.Count; i++)
            {
                MethodInfo m = list[i].operand as MethodInfo;
                if (list[i].opcode != OpCodes.Call || m == null || m.Name != "StartCoroutine" || m.DeclaringType != typeof(MonoBehaviour)) continue;
                if (list[i - 1].opcode != OpCodes.Ldstr || !"spawnFish".Equals(list[i - 1].operand)) continue;
                list[i].operand = gate;
                found++;
            }
            _fishFound = found;
            return list;
        }

        // StartCoroutine("spawnFish"), unless its first segment would only
        // write a few fields and end (single player): then those writes,
        // here, and no iterator / Coroutine.
        private static Coroutine FishGate(MonoBehaviour self, string name)
        {
            try { if (FishRunsInline(self)) return null; }
            catch (Exception) { }
            return self.StartCoroutine(name);
        }

        // true = done here; false = start the coroutine (it spawns, despawns, or errs).
        private static bool FishRunsInline(object self)
        {
            if (_boltRunning()) return false;
            GameObject[] go = _fishGo(self);
            if (go == null) return false;
            if (go.Length == 0) return true;
            bool cave = _caveFish(self);
            Transform closest = cave && !_dedicated() && _inCaves() ? _localTransform() : _closestPlayer(self, cave);
            if (closest == null) return !_fishSpawned(self);   // spawned: the coroutine despawns
            Transform tr = _thisTr(self);
            if (tr == null) return false;
            float d = Vector3.Distance(closest.position, tr.position);
            _setDist(self, d);
            float spawnDist = _spawnDist(self);
            bool spawned = _fishSpawned(self);
            if (d < spawnDist) return spawned;                 // not spawned: the coroutine spawns
            if (d > spawnDist)
            {
                if (spawned) return false;                      // the coroutine despawns
                List<GameObject> all = _allFish(self);
                if (all == null) return false;
                _setSpawned(self, false);
                all.Clear();
                _setTotal(self, 0);
            }
            return true;
        }
    }
}
