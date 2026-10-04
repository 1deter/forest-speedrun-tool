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
    // Structures placed and finished during a timed run, with their place,
    // rotation and box - the replay draws them as wireframe "schematics"
    // (Data/RunBuilding, docs/run-audit-and-replays.md part 2). Read-only
    // prefixes: they look at the blueprint before the game's method runs
    // and never change it.
    //
    // WHAT (IL, ilscan 2026-10-04):
    //   Placed: Create.PlaceGhostRoutine (a coroutine) places the ghost -
    //     "OnPlaced" sent, its `Trigger` child SetActive(true) - and then
    //     calls Create.ClearReferences(bool) while `_currentGhost` is still
    //     the placed blueprint. CancelPlace (and the book's
    //     OpenBookSequence) call ClearReferences too, on a ghost never
    //     placed: its Trigger child is still inactive. So: a prefix on
    //     ClearReferences, recorded when `_currentGhost` is there and its
    //     Trigger is active. The kind is `_currentBlueprint._type`
    //     (BuildingBlueprint, a BuildingTypes value).
    //   Built: Craft_Structure.Build() reads `_type` (publishes
    //     BuiltStructure, then sets `_type` = None), then instantiates
    //     `Built` at `_ghost`'s position / rotation (`_ghost` null = the
    //     Craft_Structure's parent). A prefix reads `_type` and `_ghost`
    //     before any of that.
    //
    // The box: every active MeshFilter under the blueprint, its mesh
    // bounds' corners turned into the blueprint's own frame (unscaled),
    // capped at 400 meshes; none (or something absurd) = a default box per
    // kind (Data/ReplayMarks.DefaultSize). Once per placement - not a
    // per-frame cost. One `Replay:` log line per structure.
    // ------------------------------------------------------------------
    public static class BuildWatch
    {
        /// Set by Modules/PracticeRunModule while a timed run records.
        /// Off, both prefixes return at once.
        public static bool Recording;

        /// Caught since the module last took them (main thread). T is set
        /// by the recorder.
        public static readonly List<RunBuilding> Pending = new List<RunBuilding>();

        public static string Status = "not installed";

        private const int MaxPending = 100;
        private const int MaxMeshes = 400;
        private const float MaxSize = 200f;

        private static Harmony _harmony;
        private static ManualLogSource _log;
        private static FieldInfo _currentGhost, _currentBlueprint, _blueprintType, _craftType, _craftGhost;
        // Each blueprint once (gotcha 52: a hook can run twice before the
        // Destroy lands); cleared per run.
        private static readonly HashSet<int> Seen = new HashSet<int>();

        public static void Install(ManualLogSource log, string harmonyId)
        {
            _log = log;
            List<string> watching = new List<string>();
            List<string> missing = new List<string>();
            try
            {
                _harmony = new Harmony(harmonyId + ".buildwatch");
                const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                Type create = GameBridge.FindGameType("TheForest.Buildings.Creation.Create");
                Type blueprint = GameBridge.FindGameType("TheForest.Buildings.Creation.BuildingBlueprint");
                MethodInfo clear = create != null ? create.GetMethod("ClearReferences", inst, null, new[] { typeof(bool) }, null) : null;
                _currentGhost = create != null ? create.GetField("_currentGhost", inst) : null;
                _currentBlueprint = create != null ? create.GetField("_currentBlueprint", inst) : null;
                _blueprintType = blueprint != null ? blueprint.GetField("_type", inst) : null;
                if (clear != null && _currentGhost != null)
                {
                    _harmony.Patch(clear, prefix: Hook("PlacedPrefix"));
                    watching.Add("blueprints placed");
                }
                else missing.Add("blueprints placed (Create.ClearReferences / _currentGhost)");

                Type craft = GameBridge.FindGameType("TheForest.Buildings.Creation.Craft_Structure");
                MethodInfo build = craft != null ? craft.GetMethod("Build", inst, null, Type.EmptyTypes, null) : null;
                _craftType = craft != null ? craft.GetField("_type", inst) : null;
                _craftGhost = craft != null ? craft.GetField("_ghost", inst) : null;
                if (build != null)
                {
                    _harmony.Patch(build, prefix: Hook("BuiltPrefix"));
                    watching.Add("structures finished");
                }
                else missing.Add("structures finished (Craft_Structure.Build)");
            }
            catch (Exception e)
            {
                missing.Add("error: " + e.Message);
            }
            Status = (watching.Count > 0 ? "watching " + string.Join(", ", watching.ToArray()) : "watching nothing") +
                     (missing.Count > 0 ? "; not found: " + string.Join(", ", missing.ToArray()) : "");
            log.LogInfo("Replay buildings: " + Status + ".");
        }

        private static HarmonyMethod Hook(string name)
        {
            return new HarmonyMethod(typeof(BuildWatch).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));
        }

        public static void Uninstall()
        {
            Recording = false;
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception) { }
        }

        /// A new run: nothing carries over.
        public static void Reset()
        {
            Pending.Clear();
            Seen.Clear();
        }

        // --- prefixes: read-only, never throw into the game ---------------------

        private static void PlacedPrefix(object __instance)
        {
            if (!Recording || Pending.Count >= MaxPending) return;
            try
            {
                GameObject ghost = _currentGhost.GetValue(__instance) as GameObject;
                if (ghost == null) return;
                Transform trigger = ghost.transform.Find("Trigger");
                if (trigger == null || !trigger.gameObject.activeSelf) return;   // cancelled, never placed
                string kind = null;
                object bp = _currentBlueprint != null ? _currentBlueprint.GetValue(__instance) : null;
                if (bp != null && _blueprintType != null)
                {
                    object t = _blueprintType.GetValue(bp);
                    if (t != null && Convert.ToInt32(t) != 0) kind = t.ToString();
                }
                Add(ghost, kind, RunBuilding.Placed);
            }
            catch (Exception) { }
        }

        private static void BuiltPrefix(object __instance)
        {
            if (!Recording || Pending.Count >= MaxPending) return;
            try
            {
                Component c = __instance as Component;
                if (c == null) return;
                GameObject ghost = _craftGhost != null ? _craftGhost.GetValue(__instance) as GameObject : null;
                if (ghost == null && c.transform.parent != null) ghost = c.transform.parent.gameObject;
                if (ghost == null) ghost = c.gameObject;
                string kind = null;
                object t = _craftType != null ? _craftType.GetValue(__instance) : null;
                if (t != null && Convert.ToInt32(t) != 0) kind = t.ToString();
                Add(ghost, kind, RunBuilding.Built);
            }
            catch (Exception) { }
        }

        private static void Add(GameObject ghost, string kind, string state)
        {
            int key = ghost.GetInstanceID() * 2 + (state == RunBuilding.Built ? 1 : 0);
            if (!Seen.Add(key)) return;
            if (string.IsNullOrEmpty(kind)) kind = NameOf(ghost);

            Transform t = ghost.transform;
            RunBuilding b = default(RunBuilding);
            b.State = state;
            b.Kind = kind;
            b.P = t.position;
            b.Euler = t.rotation.eulerAngles;
            int meshes;
            bool measured = LocalBox(ghost, out b.Center, out b.Size, out meshes);
            if (!measured)
            {
                b.Size = ReplayMarks.DefaultSize(kind);
                b.Center = new Vector3(0f, b.Size.y * 0.5f, 0f);
            }
            Pending.Add(b);
            if (_log != null)
                _log.LogInfo("Replay: " + kind + " " + state + " at " + b.P.ToString("F1") + ", yaw " + b.Euler.y.ToString("F0") +
                             ", box " + b.Size.ToString("F1") + (measured ? " from " + meshes + " mesh(es)" : " (default size)") + ".");
        }

        // "Ghost_LogCabin(Clone)" -> "LogCabin".
        private static string NameOf(GameObject go)
        {
            string n = go.name ?? "";
            if (n.EndsWith("(Clone)", StringComparison.Ordinal)) n = n.Substring(0, n.Length - 7);
            if (n.StartsWith("Ghost_", StringComparison.Ordinal)) n = n.Substring(6);
            return n.Trim();
        }

        // The box of every active mesh under `root`, in root's own frame
        // (unscaled: the rotation's inverse, not InverseTransformPoint).
        private static bool LocalBox(GameObject root, out Vector3 center, out Vector3 size, out int meshes)
        {
            center = Vector3.zero;
            size = Vector3.zero;
            meshes = 0;
            Transform rt = root.transform;
            Quaternion inv = Quaternion.Inverse(rt.rotation);
            Vector3 origin = rt.position;
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>();
            for (int i = 0; i < filters.Length && meshes < MaxMeshes; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh == null) continue;
                meshes++;
                Bounds bb = mesh.bounds;
                Transform ft = filters[i].transform;
                Vector3 bmin = bb.min, bmax = bb.max;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = new Vector3((k & 1) == 0 ? bmin.x : bmax.x, (k & 2) == 0 ? bmin.y : bmax.y, (k & 4) == 0 ? bmin.z : bmax.z);
                    Vector3 local = inv * (ft.TransformPoint(corner) - origin);
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                }
            }
            if (meshes == 0) return false;
            size = max - min;
            if (size.x > MaxSize || size.y > MaxSize || size.z > MaxSize || size.sqrMagnitude < 0.0001f) return false;
            center = (min + max) * 0.5f;
            return true;
        }
    }
}
