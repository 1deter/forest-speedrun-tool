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
    //   Quick load moves back the ones cut after the capture (its `bushes`
    //   mark: this world, the last cut's number); one cut before it stays
    //   cut, its sticks where they lay. A file from another world (a load
    //   since, another launch, a slot) has no usable mark: every copy comes
    //   back, as after a load, which regrows them all.
    // - The logs and sticks those cuts dropped: SavestateModule removes
    //   the ones not at capture (PickupKeeper.RemoveExtra).
    // - A Full load regrows every bush (author, 2026-09-25: "if a bush is
    //   cut and it was saved that way, then the savestate should respect
    //   that"). Every cut this scene is recorded (armed or not) by its
    //   scene path and place; capture writes the ones still cut
    //   (`cutbushes`), and after a Full load - or a Quick load from another
    //   world - ApplyCuts cuts them again: the view despawned, the LOD
    //   object destroyed, as the game's cut leaves it. Their sticks are not
    //   put back.
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
            public int Seq;
        }

        // This launch; a mark from another one never matches.
        private static readonly string Launch = Guid.NewGuid().ToString("N").Substring(0, 8);
        private static int _seq;

        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly List<Kept> KeptList = new List<Kept>();
        private static readonly HashSet<int> KeptIds = new HashSet<int>();
        // LOD_Trees instance id -> the trunk its fall spawned.
        private static readonly Dictionary<int, GameObject> Trunks = new Dictionary<int, GameObject>();
        // "<MyCut name>(Clone)" of every cut kept: where a cut's sticks lie.
        private static readonly HashSet<string> CutNames = new HashSet<string>();
        private static readonly Dictionary<Type, FieldInfo> LodFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> CutFields = new Dictionary<Type, FieldInfo>();

        // Every cut this scene (world `_cutsWorld`): key "path@x,y,z", Seq.
        private static readonly List<KeyValuePair<string, int>> Cuts = new List<KeyValuePair<string, int>>();
        private static string _cutsWorld = "";

        private static ManualLogSource _log;
        private static GameObject _holder;

        private static PropertyInfo _destroyInstead;   // LOD_Base.DestroyInsteadOfDisable (virtual)
        private static FieldInfo _lodTree;             // TreeHealth.LodTree
        private static FieldInfo _isSpawned, _lodTransform, _lodDestroyed, _currentLod;   // LOD_Base

        private static Type _manager;
        private Type _lodBase, _lodTrees, _lodStump, _treeId, _grid;
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
            _lodBase = lodBase;
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

                int seq = ++_seq;
                Record(go, seq);
                if (!PickupKeeper.Armed) return;

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
                k.Seq = seq;
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

        private static string KeyOf(GameObject go)
        {
            Vector3 p = go.transform.position;
            return ObjectProbe.PathOf(go.transform) + "@" + F(p.x) + "," + F(p.y) + "," + F(p.z);
        }

        private static string F(float f) { return f.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); }

        private static void Record(GameObject go, int seq)
        {
            string w = World();
            if (w != _cutsWorld) { Cuts.Clear(); _cutsWorld = w; }
            Cuts.Add(new KeyValuePair<string, int>(KeyOf(go), seq));
        }

        /// The bushes / saplings cut this scene and still cut, for the
        /// file's `cutbushes` line.
        public List<string> CaptureCuts()
        {
            List<string> keys = new List<string>();
            if (World() != _cutsWorld) return keys;
            for (int i = 0; i < Cuts.Count; i++) keys.Add(Cuts[i].Key);
            return keys;
        }

        /// Cuts again the file's `cutbushes` still standing (after a Full
        /// load, which regrows every bush, or a Quick load from another
        /// world). Returns the log note ("" when nothing listed).
        public string ApplyCuts(List<string> keys)
        {
            if (keys == null || keys.Count == 0) return "";
            int cut = 0, absent = 0;
            try
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    GameObject go = FindCut(keys[i]);
                    if (go == null) { absent++; continue; }
                    Component lod = _lodBase != null ? go.GetComponent(_lodBase) : null;
                    if (lod != null && _lodTransform != null && _lodTransform.GetValue(lod) != null && _despawn != null)
                        _despawn.Invoke(lod, null);
                    Record(go, ++_seq);
                    UnityEngine.Object.Destroy(go);
                    cut++;
                }
            }
            catch (Exception ex)
            {
                while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
                return "bushes: cutting again failed after " + cut + " (" + ex.Message + ")";
            }
            return "bushes: " + cut + " cut again (cut at capture)" + (absent > 0 ? ", " + absent + " not found" : "");
        }

        // The object at `path` nearest the key's place, within half a metre
        // (names repeat: Nature_Spawned/GreenBush_40).
        private static GameObject FindCut(string key)
        {
            int at = key.LastIndexOf('@');
            if (at <= 0) return null;
            string path = key.Substring(0, at);
            string[] p = key.Substring(at + 1).Split(',');
            float x, y, z;
            System.Globalization.NumberStyles st = System.Globalization.NumberStyles.Float;
            System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;
            if (p.Length != 3 || !float.TryParse(p[0], st, ci, out x) || !float.TryParse(p[1], st, ci, out y) ||
                !float.TryParse(p[2], st, ci, out z)) return null;
            Vector3 want = new Vector3(x, y, z);

            int slash = path.LastIndexOf('/');
            string name = slash >= 0 ? path.Substring(slash + 1) : path;
            List<Transform> candidates = new List<Transform>();
            if (slash >= 0)
            {
                GameObject parent = GameObject.Find(path.Substring(0, slash));
                if (parent == null) return null;
                Transform pt = parent.transform;
                for (int c = 0; c < pt.childCount; c++) candidates.Add(pt.GetChild(c));
            }
            else
            {
                for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
                {
                    UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                    if (!scene.isLoaded) continue;
                    GameObject[] roots = scene.GetRootGameObjects();
                    for (int r = 0; r < roots.Length; r++) candidates.Add(roots[r].transform);
                }
            }
            Transform best = null;
            float bestD = 0.25f;   // (0.5 m)^2
            for (int c = 0; c < candidates.Count; c++)
            {
                Transform t = candidates[c];
                if (t.name != name) continue;
                float d = (t.position - want).sqrMagnitude;
                if (d <= bestD) { bestD = d; best = t; }
            }
            return best != null ? best.gameObject : null;
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

        /// This world (the tree save manager lives as long as the scene; a
        /// Quick load keeps it) and the last cut so far, for the file's
        /// `bushes` line. "" outside a game.
        public string CaptureMark()
        {
            string w = World();
            return w.Length == 0 ? "" : w + ":" + _seq.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string World()
        {
            if (_manager == null) return "";
            UnityEngine.Object m = UnityEngine.Object.FindObjectOfType(_manager);
            return m == null ? "" : Launch + "-" + m.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// After a Quick load, once the serializer has put the save's cut
        /// list back; `mark` is the file's `bushes` line ("" for none).
        /// Returns the log note ("" when nothing changed).
        public string Restore(string mark, List<string> cutAtCapture)
        {
            string trees = "", bushes = "";
            try { trees = RegrowTrees(); }
            catch (Exception ex)
            {
                while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
                trees = "trees: regrowing failed (" + ex.Message + ")";
            }
            int since = -1;
            try
            {
                int colon = mark != null ? mark.LastIndexOf(':') : -1;
                int n;
                if (colon > 0 && mark.Substring(0, colon) == World() &&
                    int.TryParse(mark.Substring(colon + 1), System.Globalization.NumberStyles.Integer,
                                 System.Globalization.CultureInfo.InvariantCulture, out n)) since = n;
            }
            catch (Exception) { }
            try { bushes = PutBushesBack(since); }
            catch (Exception ex) { bushes = "bushes: putting back failed (" + ex.Message + ")"; }
            // A file from another world: its cuts (every copy came back).
            string again = since < 0 ? ApplyCuts(cutAtCapture) : "";
            if (again.Length > 0) bushes += (bushes.Length > 0 ? ", " : "") + again;
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

        /// Puts back the copies of cuts after number `since` (-1: all).
        private static string PutBushesBack(int since)
        {
            int back = 0, gone = 0, stay = 0;
            List<Kept> left = new List<Kept>();
            for (int i = 0; i < KeptList.Count; i++)
            {
                Kept k = KeptList[i];
                if (k.Spare == null) continue;                      // a load destroyed the holder
                if (k.Original != null || k.Parent == null) { UnityEngine.Object.Destroy(k.Spare); gone++; continue; }
                if (k.Seq <= since) { left.Add(k); stay++; continue; }   // cut before the capture
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
            KeptList.AddRange(left);   // their originals are gone: no id to guard
            // The cuts put back are no longer cut.
            if (since < 0) Cuts.Clear();
            else Cuts.RemoveAll(delegate(KeyValuePair<string, int> c) { return c.Value > since; });
            if (back == 0 && gone == 0 && stay == 0) return "";
            return "bushes: " + back + " back" + (since >= 0 ? " (cut since the capture)" : " (every cut this scene - no mark from this world)") +
                   (stay > 0 ? ", " + stay + " cut before the capture left cut" : "") +
                   (gone > 0 ? ", " + gone + " spare(s) dropped" : "");
        }
    }
}
