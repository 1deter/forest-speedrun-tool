using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Trees, bushes and saplings after a Quick load (fix list 1, author:
    // a tree chopped since the capture stayed a stump, a cut bush stayed
    // gone; a Full load put both back).
    //
    // WHY (IL + bridge, 2026-09-25; game-notes *Trees, bushes and
    // saplings*): the save holds trees only as
    // MassDestructionSaveManager.CutDownTreeIds (a tree counts as cut when
    // its LOD_Trees is disabled with no view), and a load only ever CUTS
    // the listed trees - on a fresh scene that is exact, in place it leaves
    // every tree cut since the capture down. A tree half-chopped (its
    // chopped model up) is not on the list either. Bushes (LOD_Bush,
    // LOD_SmallBush) and saplings (LOD_Sapling - the ones that drop two
    // sticks) are not in the save at all: cutting one DESTROYS its scene
    // object (DestroyInsteadOfDisable), and every load regrows them all.
    //
    // WHAT:
    // - Trees: after a Quick load, every disabled tree not on the restored
    //   cut list is regrown the game's own way (ShelterTrigger.
    //   CheckRegrowTrees: DontSpawn off, enabled, RefreshLODs, the LOD
    //   grid told, stumps under it despawned and destroyed), after its
    //   chopped model or falling trunk is removed.
    // - Bushes / saplings: while armed (savestates in use this session, as
    //   PickupKeeper), a prefix on the cut keeps a copy of the scene object
    //   under an inactive holder - inactive so it does not wake - and a
    //   Quick load moves every copy back: as after any load, bushes cut
    //   before the capture come back too.
    // - The logs and sticks those cuts dropped: SavestateModule removes
    //   the ones not at capture (PickupKeeper.RemoveExtra).
    // ------------------------------------------------------------------
    public sealed class NatureKeeper
    {
        private sealed class Kept
        {
            public GameObject Original;
            public GameObject Spare;
            public Transform Parent;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
        }

        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly List<Kept> KeptList = new List<Kept>();
        private static readonly HashSet<int> KeptIds = new HashSet<int>();
        // LOD_Trees instance id -> the trunk its fall spawned.
        private static readonly Dictionary<int, GameObject> Trunks = new Dictionary<int, GameObject>();
        // "<MyCut name>(Clone)" of every cut kept: where a cut's sticks lie.
        private static readonly HashSet<string> CutNames = new HashSet<string>();
        private static readonly Dictionary<Type, FieldInfo> LodFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> CutFields = new Dictionary<Type, FieldInfo>();

        private static ManualLogSource _log;
        private static GameObject _holder;

        private static PropertyInfo _destroyInstead;   // LOD_Base.DestroyInsteadOfDisable (virtual)
        private static FieldInfo _lodTree;             // TreeHealth.LodTree
        private static FieldInfo _isSpawned, _lodTransform, _lodDestroyed, _currentLod;   // LOD_Base

        private Type _lodTrees, _lodStump, _treeId, _manager, _grid;
        private FieldInfo _dontSpawn, _id, _cutIds;
        private PropertyInfo _currentView;
        private MethodInfo _refresh, _despawn, _regrowth;
        private Harmony _harmony;

        public string Status { get; private set; }
        public int KeptCount { get { return KeptList.Count; } }

        public NatureKeeper(ManualLogSource log)
        {
            _log = log;
            Status = "not installed";
        }

        public void Install(string harmonyId)
        {
            Type lodBase = GameBridge.FindGameType("LOD_Base");
            Type treeHealth = GameBridge.FindGameType("TreeHealth");
            Type bush = GameBridge.FindGameType("BushDamage");
            Type cutBush = GameBridge.FindGameType("CutBush2");
            _lodTrees = GameBridge.FindGameType("LOD_Trees");
            _lodStump = GameBridge.FindGameType("LOD_Stump");
            _treeId = GameBridge.FindGameType("CoopTreeId");
            _manager = GameBridge.FindGameType("MassDestructionSaveManager");
            _grid = GameBridge.FindGameType("TheForest.Utils.TreeLodGrid");
            if (lodBase == null || _lodTrees == null || _treeId == null || _manager == null)
            {
                Status = "LOD / tree types not found";
                _log.LogWarning("NatureKeeper: " + Status);
                return;
            }

            _destroyInstead = lodBase.GetProperty("DestroyInsteadOfDisable", Inst);
            _dontSpawn = lodBase.GetField("DontSpawn", Inst);
            _isSpawned = lodBase.GetField("isSpawned", Inst);
            _lodTransform = lodBase.GetField("CurrentLodTransform", Inst);
            _lodDestroyed = lodBase.GetField("lodWasDestroyed", Inst);
            _currentLod = lodBase.GetField("currentLOD", Inst);
            _refresh = lodBase.GetMethod("RefreshLODs", Inst, null, Type.EmptyTypes, null);
            _despawn = lodBase.GetMethod("DespawnCurrent", Inst, null, Type.EmptyTypes, null);
            _currentView = _lodTrees.GetProperty("CurrentView", Inst);
            _id = _treeId.GetField("Id", Inst);
            _cutIds = _manager.GetField("CutDownTreeIds", Inst);
            _regrowth = _grid != null ? _grid.GetMethod("RegisterTreeRegrowth", Inst, null, new[] { typeof(Vector3) }, null) : null;
            _lodTree = treeHealth != null ? treeHealth.GetField("LodTree", Inst) : null;

            int hooks = 0;
            try
            {
                _harmony = new Harmony(harmonyId + ".nature");
                HarmonyMethod keep = new HarmonyMethod(typeof(NatureKeeper).GetMethod("CutPrefix", BindingFlags.Static | BindingFlags.NonPublic));
                hooks += Patch(bush, "CutDownReal", keep, null);
                hooks += Patch(bush, "DespawnBush", keep, null);
                hooks += Patch(cutBush, "CutDown", keep, null);
                hooks += Patch(cutBush, "DespawnBush", keep, null);
                HarmonyMethod trunk = new HarmonyMethod(typeof(NatureKeeper).GetMethod("FallPostfix", BindingFlags.Static | BindingFlags.NonPublic));
                hooks += Patch(treeHealth, "DoFallTree", null, trunk);
                hooks += Patch(treeHealth, "DoFallTreeExplosion", null, trunk);
                Status = "hooked (" + hooks + "/6)";
            }
            catch (Exception ex)
            {
                Status = "Harmony: " + ex.Message;
            }
            _log.LogInfo("NatureKeeper: " + Status + (_regrowth == null ? ", no TreeLodGrid" : "") + ".");
        }

        private int Patch(Type t, string method, HarmonyMethod prefix, HarmonyMethod postfix)
        {
            MethodInfo m = t != null ? t.GetMethod(method, Inst, null, Type.EmptyTypes, null) : null;
            if (m == null) return 0;
            _harmony.Patch(m, prefix, postfix);
            return 1;
        }

        public void Uninstall()
        {
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception) { }
            KeptList.Clear();
            KeptIds.Clear();
            Trunks.Clear();
        }

        /// The sticks a kept cut dropped lie under an object of one of these
        /// names (`Sapling1_Cut(Clone)`).
        public static bool UnderACut(GameObject target)
        {
            Transform p = target != null ? target.transform.parent : null;
            return p != null && CutNames.Contains(p.name);
        }

        // Before the game cuts a bush or sapling. Never throws into the
        // game; never skips the original.
        private static void CutPrefix(object __instance)
        {
            if (!PickupKeeper.Armed) return;
            try
            {
                Component view = __instance as Component;
                if (view == null) return;
                Type t = view.GetType();
                Component lod = Field(LodFields, t, "LodBase").GetValue(__instance) as Component;
                if (lod == null) return;
                GameObject go = lod.gameObject;
                if (KeptIds.Contains(go.GetInstanceID())) return;
                // A greeble's bush is the greeble system's (a pooled clone).
                if (go.transform.root.name == "Pooling") return;
                if (_destroyInstead == null || !(bool)_destroyInstead.GetValue(lod, null)) return;

                if (_holder == null)
                {
                    _holder = new GameObject("ForestOverlay_NatureSpares");
                    _holder.SetActive(false);
                }

                Transform tr = go.transform;
                Kept k = new Kept();
                k.Original = go;
                k.Spare = UnityEngine.Object.Instantiate(go, _holder.transform) as GameObject;
                if (k.Spare == null) return;
                // Off, so it wakes only once it is back in place (OnEnable
                // registers its position with the LOD scheduler).
                k.Spare.SetActive(false);
                k.Spare.name = go.name;
                // As OnDisable leaves a LOD: the copy took the cut view's
                // state, and a "spawned" LOD with no view destroys itself on
                // its first refresh (RefreshLODs, lodWasDestroyed).
                Component copy = k.Spare.GetComponent(lod.GetType());
                if (copy != null)
                {
                    if (_isSpawned != null) _isSpawned.SetValue(copy, false);
                    if (_lodTransform != null) _lodTransform.SetValue(copy, null);
                    if (_lodDestroyed != null) _lodDestroyed.SetValue(copy, false);
                    if (_currentLod != null) _currentLod.SetValue(copy, -1);
                }
                k.Parent = tr.parent;
                k.LocalPosition = tr.localPosition;
                k.LocalRotation = tr.localRotation;
                k.LocalScale = tr.localScale;
                KeptList.Add(k);
                KeptIds.Add(go.GetInstanceID());

                UnityEngine.Object cut = Field(CutFields, t, "MyCut").GetValue(__instance) as UnityEngine.Object;
                if (cut != null) CutNames.Add(cut.name + "(Clone)");
            }
            catch (Exception ex)
            {
                _log.LogWarning("NatureKeeper: keeping a bush failed: " + ex.Message);
            }
        }

        private static FieldInfo Field(Dictionary<Type, FieldInfo> cache, Type t, string name)
        {
            FieldInfo f;
            if (!cache.TryGetValue(t, out f)) cache[t] = f = t.GetField(name, Inst);
            if (f == null) throw new MissingFieldException(t.Name, name);
            return f;
        }

        private static void FallPostfix(object __instance, GameObject __result)
        {
            if (!PickupKeeper.Armed || __result == null || _lodTree == null) return;
            try
            {
                Component tree = _lodTree.GetValue(__instance) as Component;
                if (tree != null) Trunks[tree.GetInstanceID()] = __result;
            }
            catch (Exception) { }
        }

        /// After a Quick load, once the serializer has put the save's cut
        /// list back. Returns the log note ("" when nothing changed).
        public string Restore()
        {
            string trees = "", bushes = "";
            try { trees = RegrowTrees(); }
            catch (Exception ex)
            {
                while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
                trees = "trees: regrowing failed (" + ex.Message + ")";
            }
            try { bushes = PutBushesBack(); }
            catch (Exception ex) { bushes = "bushes: putting back failed (" + ex.Message + ")"; }
            return trees + (trees.Length > 0 && bushes.Length > 0 ? ", " : "") + bushes;
        }

        private string RegrowTrees()
        {
            if (_refresh == null || _currentView == null || _id == null || _cutIds == null || _dontSpawn == null) return "";
            UnityEngine.Object manager = UnityEngine.Object.FindObjectOfType(_manager);
            if (manager == null) return "";
            HashSet<int> cut = new HashSet<int>();
            int[] ids = _cutIds.GetValue(manager) as int[];
            if (ids != null) for (int i = 0; i < ids.Length; i++) if (ids[i] >= 0) cut.Add(ids[i]);

            UnityEngine.Object grid = _regrowth != null ? UnityEngine.Object.FindObjectOfType(_grid) : null;
            int regrown = 0, midChop = 0;
            UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(_treeId);
            for (int i = 0; i < all.Length; i++)
            {
                Component idc = all[i] as Component;
                if (idc == null) continue;
                Behaviour lod = idc.GetComponent(_lodTrees) as Behaviour;
                if (lod == null || lod.enabled) continue;
                if (cut.Contains((int)_id.GetValue(idc))) continue;

                // Its chopped model (half-chopped) or falling trunk, then the
                // game's own regrowth.
                Component view = _currentView.GetValue(lod, null) as Component;
                if (view != null) { UnityEngine.Object.Destroy(view.gameObject); midChop++; }
                GameObject trunk;
                if (Trunks.TryGetValue(lod.GetInstanceID(), out trunk))
                {
                    if (trunk != null) UnityEngine.Object.Destroy(trunk);
                    Trunks.Remove(lod.GetInstanceID());
                }
                _currentView.SetValue(lod, null, null);

                Transform t = lod.transform;
                List<Transform> children = new List<Transform>();
                for (int c = 0; c < t.childCount; c++) children.Add(t.GetChild(c));
                for (int c = 0; c < children.Count; c++)
                {
                    Component stump = _lodStump != null ? children[c].GetComponent(_lodStump) : null;
                    if (stump != null)
                    {
                        if (_despawn != null) _despawn.Invoke(stump, null);
                        _currentView.SetValue(stump, null, null);
                    }
                    UnityEngine.Object.Destroy(children[c].gameObject);
                }

                _dontSpawn.SetValue(lod, false);
                lod.enabled = true;
                _refresh.Invoke(lod, null);
                if (grid != null) _regrowth.Invoke(grid, new object[] { t.position });
                regrown++;
            }

            // Trunks of trees still cut (on the list) stay; forget the dead.
            List<int> dead = null;
            foreach (KeyValuePair<int, GameObject> kv in Trunks)
                if (kv.Value == null) (dead ?? (dead = new List<int>())).Add(kv.Key);
            if (dead != null) for (int i = 0; i < dead.Count; i++) Trunks.Remove(dead[i]);

            if (regrown == 0) return "";
            return "trees: " + regrown + " regrown (not cut at capture" + (midChop > 0 ? "; " + midChop + " half-chopped or falling" : "") + ")";
        }

        private static string PutBushesBack()
        {
            int back = 0, gone = 0;
            for (int i = 0; i < KeptList.Count; i++)
            {
                Kept k = KeptList[i];
                if (k.Spare == null) continue;                      // a load destroyed the holder
                if (k.Original != null || k.Parent == null) { UnityEngine.Object.Destroy(k.Spare); gone++; continue; }
                Transform t = k.Spare.transform;
                t.SetParent(k.Parent, false);
                t.localPosition = k.LocalPosition;
                t.localRotation = k.LocalRotation;
                t.localScale = k.LocalScale;
                k.Spare.SetActive(true);
                back++;
            }
            KeptList.Clear();
            KeptIds.Clear();
            if (back == 0 && gone == 0) return "";
            return "bushes: " + back + " back (cut since the scene loaded)" + (gone > 0 ? ", " + gone + " spare(s) dropped" : "");
        }
    }
}
