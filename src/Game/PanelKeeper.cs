using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Logging;
using ForestOverlay.Data;
using HarmonyLib;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Puts the caves' wooden panels back as they were at capture (runner
    // maks: axe clips chip a panel a little each time, so over in-place
    // restores it wore down and broke - "it breaks when you try without
    // the restore").
    //
    // WHY (IL): a panel is BreakWoodSimple - `int Health`; Hit(damage)
    // subtracts and at <= 0 calls CutDown, which frees its three pieces
    // (Cut1..3: unparented, pushed) and Destroy()s the panel. Every panel
    // is listed in the scene-authored CoopWoodPlanks.Instance.Planks. An
    // in-place restore leaves Health alone, and a destroyed scene object
    // cannot be brought back by the serializer (like a taken pickup).
    //
    // WHAT: the capture lists every live panel's health by position
    // (`panels` header). While armed (savestates in use this session, as
    // PickupKeeper), a prefix on CutDown first copies the whole panel,
    // pieces in place, under an inactive holder - inactive so the copy
    // does not wake - and lets the game break the original as usual. An
    // in-place restore then sets each listed panel's health, and for one
    // that broke since, moves its copy back into place and deletes the
    // flying pieces. A load restore only sets health.
    // ------------------------------------------------------------------
    public sealed class PanelKeeper
    {
        private sealed class Kept
        {
            public GameObject Spare;
            public Transform Parent;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
            public int Index;
            public string Key;
            public GameObject[] Pieces;
        }

        private static readonly List<Kept> KeptList = new List<Kept>();
        private static ManualLogSource _log;
        private static GameObject _holder;

        private static Type _woodType;
        private static FieldInfo _health;
        private static FieldInfo[] _cuts;
        private static FieldInfo _instance;   // static CoopWoodPlanks.Instance
        private static FieldInfo _planks;     // CoopWoodPlanks.Planks

        private Harmony _harmony;

        public string Status { get; private set; }
        public int KeptCount { get { return KeptList.Count; } }

        public PanelKeeper(ManualLogSource log)
        {
            _log = log;
            Status = "not installed";
        }

        public void Install(string harmonyId)
        {
            BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            _woodType = GameBridge.FindGameType("BreakWoodSimple");
            Type planks = GameBridge.FindGameType("CoopWoodPlanks");
            if (_woodType == null || planks == null) { Status = "BreakWoodSimple / CoopWoodPlanks not found"; _log.LogWarning("PanelKeeper: " + Status); return; }

            _health = _woodType.GetField("Health", inst);
            _cuts = new[] { _woodType.GetField("Cut1", inst), _woodType.GetField("Cut2", inst), _woodType.GetField("Cut3", inst) };
            _instance = planks.GetField("Instance", stat);
            _planks = planks.GetField("Planks", inst);

            MethodInfo cutDown = _woodType.GetMethod("CutDown", inst, null, Type.EmptyTypes, null);
            if (cutDown == null || _health == null || _instance == null || _planks == null)
            {
                Status = "CutDown / fields not found";
                _log.LogWarning("PanelKeeper: " + Status);
                return;
            }

            try
            {
                _harmony = new Harmony(harmonyId + ".panels");
                _harmony.Patch(cutDown, new HarmonyMethod(typeof(PanelKeeper).GetMethod("CutDownPrefix",
                    BindingFlags.Static | BindingFlags.NonPublic)));
                Status = "hooked";
            }
            catch (Exception ex)
            {
                Status = "Harmony: " + ex.Message;
            }
            _log.LogInfo("PanelKeeper: " + Status + ".");
        }

        public void Uninstall()
        {
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception) { }
            KeptList.Clear();
        }

        // Never throws into the game; never skips the original.
        private static void CutDownPrefix(object __instance)
        {
            if (!PickupKeeper.Armed) return;
            try
            {
                Component wood = __instance as Component;
                if (wood == null) return;
                GameObject go = wood.gameObject;
                Transform t = go.transform;

                // Under an inactive holder the copy does not wake (no Awake,
                // no OnEnable) until it is moved back into the scene.
                if (_holder == null)
                {
                    _holder = new GameObject("ForestOverlay_PanelSpares");
                    _holder.SetActive(false);
                }

                Kept k = new Kept();
                k.Spare = UnityEngine.Object.Instantiate(go, _holder.transform) as GameObject;
                k.Parent = t.parent;
                k.LocalPosition = t.localPosition;
                k.LocalRotation = t.localRotation;
                k.LocalScale = t.localScale;
                k.Index = IndexOf(wood);
                k.Key = Key(t.position);
                k.Pieces = new GameObject[_cuts.Length];
                for (int i = 0; i < _cuts.Length; i++)
                    if (_cuts[i] != null) k.Pieces[i] = _cuts[i].GetValue(__instance) as GameObject;

                if (k.Spare == null) return;
                k.Spare.name = go.name;
                KeptList.Add(k);
                _log.LogInfo("PanelKeeper: panel " + k.Key + " broke; kept for a restore (" + KeptList.Count + " kept).");
            }
            catch (Exception ex)
            {
                _log.LogWarning("PanelKeeper: keeping a panel failed: " + ex.Message);
            }
        }

        /// Every live panel's health, as "health@x,y,z" (the pickup key's
        /// format, with the health where the item id goes).
        public void Snapshot(List<string> entries)
        {
            Array planks = Planks();
            if (planks == null) return;

            for (int i = 0; i < planks.Length; i++)
            {
                Component p = planks.GetValue(i) as Component;
                if (p == null) continue;
                Vector3 pos = p.transform.position;
                entries.Add(SavestateFile.PickupKey((int)_health.GetValue(p), pos.x, pos.y, pos.z));
            }
        }

        /// One line describing the panel nearest `from` and everything under
        /// it: path, active, components, rigidbodies. Diagnostic for the
        /// author's "half-repaired" panels (v0.24.7): after a restore a hit
        /// panel still looked crooked, its crossed boards on the floor, and
        /// nothing in IL but BreakWoodSimple breaks a panel - so which
        /// children move, and are they physics bodies?
        public string DescribeNearest(Vector3 from)
        {
            Array planks = Planks();
            if (planks == null) return null;

            Component best = null;
            float bestSq = 30f * 30f;
            for (int i = 0; i < planks.Length; i++)
            {
                Component p = planks.GetValue(i) as Component;
                if (p == null) continue;
                float d = (p.transform.position - from).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = p; }
            }
            if (best == null) return null;

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("health ").Append((int)_health.GetValue(best)).Append(", ")
              .Append(Mathf.Sqrt(bestSq).ToString("0.0", CultureInfo.InvariantCulture)).Append(" m away: ");
            Transform root = best.transform;
            if (root.parent != null) sb.Append("parent '").Append(root.parent.name).Append("' ");
            Describe(root, root, sb, 0);
            return sb.ToString();
        }

        private static void Describe(Transform t, Transform root, System.Text.StringBuilder sb, int depth)
        {
            if (depth > 4) return;
            sb.Append(depth == 0 ? "" : "; ").Append(new string('>', depth)).Append(t.name);
            if (!t.gameObject.activeSelf) sb.Append(" (off)");
            if (t != root)
            {
                Vector3 lp = t.localPosition;
                sb.Append(" @").Append(lp.x.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                  .Append(lp.y.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                  .Append(lp.z.ToString("0.00", CultureInfo.InvariantCulture));
            }
            Component[] cs = t.GetComponents<Component>();
            sb.Append(" [");
            for (int i = 0; i < cs.Length; i++)
            {
                if (cs[i] == null || cs[i] is Transform) continue;
                sb.Append(cs[i].GetType().Name);
                Rigidbody rb = cs[i] as Rigidbody;
                if (rb != null) sb.Append(rb.isKinematic ? "(kinematic)" : "(dynamic)");
                sb.Append(' ');
            }
            sb.Append(']');
            for (int c = 0; c < t.childCount; c++) Describe(t.GetChild(c), root, sb, depth + 1);
        }

        /// Sets each captured panel's health; with `rebuild`, also puts back
        /// the ones that broke since (in place only - a load has fresh
        /// panels). Returns a note for the log line, "" when there is
        /// nothing to say.
        public string Restore(List<string> captured, bool rebuild)
        {
            if (captured == null) return "";
            Array planks = Planks();
            if (planks == null) return "panels: none in this scene";

            Dictionary<string, int> want = new Dictionary<string, int>();
            for (int i = 0; i < captured.Count; i++)
            {
                string e = captured[i];
                int at = e.IndexOf('@');
                int h;
                if (at <= 0 || !int.TryParse(e.Substring(0, at), NumberStyles.Integer, CultureInfo.InvariantCulture, out h)) continue;
                want[e.Substring(at + 1)] = h;
            }

            int healed = 0, rebuilt = 0;
            HashSet<string> seen = new HashSet<string>();

            for (int i = 0; i < planks.Length; i++)
            {
                Component p = planks.GetValue(i) as Component;
                if (p == null) continue;
                string key = Key(p.transform.position);
                seen.Add(key);

                int h;
                if (!want.TryGetValue(key, out h)) continue;
                if ((int)_health.GetValue(p) == h) continue;
                _health.SetValue(p, h);
                healed++;
            }

            if (rebuild)
            {
                for (int i = KeptList.Count - 1; i >= 0; i--)
                {
                    Kept k = KeptList[i];
                    int h;
                    if (k.Spare == null) { KeptList.RemoveAt(i); continue; }
                    if (seen.Contains(k.Key) || !want.TryGetValue(k.Key, out h)) continue;

                    Transform t = k.Spare.transform;
                    t.SetParent(k.Parent, false);
                    t.localPosition = k.LocalPosition;
                    t.localRotation = k.LocalRotation;
                    t.localScale = k.LocalScale;

                    Component wood = k.Spare.GetComponent(_woodType);
                    if (wood != null)
                    {
                        _health.SetValue(wood, h);
                        if (k.Index >= 0 && k.Index < planks.Length) planks.SetValue(wood, k.Index);
                    }
                    for (int c = 0; c < k.Pieces.Length; c++)
                        if (k.Pieces[c] != null) UnityEngine.Object.Destroy(k.Pieces[c]);

                    seen.Add(k.Key);
                    KeptList.RemoveAt(i);
                    rebuilt++;
                }
            }

            int missing = 0;
            foreach (string key in want.Keys)
                if (!seen.Contains(key)) missing++;

            if (healed == 0 && rebuilt == 0 && missing == 0) return "";
            return "panels: " + healed + " healed, " + rebuilt + " rebuilt" +
                   (missing > 0 ? ", " + missing + " broken and not kept" : "");
        }

        /// Drops copies a load destroyed: the holder lives in the scene, so
        /// a load takes it and every copy with it.
        public int PruneDestroyed()
        {
            int n = 0;
            for (int i = KeptList.Count - 1; i >= 0; i--)
            {
                if (KeptList[i].Spare != null) continue;
                KeptList.RemoveAt(i);
                n++;
            }
            return n;
        }

        private static Array Planks()
        {
            if (_instance == null || _planks == null) return null;
            object owner = _instance.GetValue(null);
            UnityEngine.Object alive = owner as UnityEngine.Object;
            if (alive == null) return null;
            return _planks.GetValue(owner) as Array;
        }

        private static int IndexOf(Component wood)
        {
            Array planks = Planks();
            if (planks == null) return -1;
            for (int i = 0; i < planks.Length; i++)
                if (ReferenceEquals(planks.GetValue(i), wood)) return i;
            return -1;
        }

        private static string Key(Vector3 p)
        {
            string k = SavestateFile.PickupKey(0, p.x, p.y, p.z);
            return k.Substring(k.IndexOf('@') + 1);
        }
    }
}
