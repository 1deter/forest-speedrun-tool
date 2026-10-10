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
    // Breakable greebles (the caves' stalagmites) after a restore (T-0273).
    //
    // WHY (bridge + IL, 2026-10-10; game-notes *Breakable stalagmites move
    // between visits*): a stalagmite is a pooled root
    // (`Pooling/Pool_Greebles/Stalagmite4_low(Clone)001`) with one child
    // `default` that holds the collider and `BreakCrate`. Breaking it
    // (`BreakCrate.ExplosionReal`) destroys only that child, so the root
    // stays active and empty: its zone never learns, the save does not
    // hold it, and an in-place restore kept it broken. The empty root also
    // goes back to the pool and later stands in for a stalagmite
    // somewhere else, which is then missing.
    //
    // WHAT (author, 2026-10-10: "if a save is captured, something is
    // broken after the save, then the save is reloaded, the state should
    // be exactly as it was during capture"; restore only, normal play
    // untouched): a prefix on ExplosionReal remembers each broken root.
    // Capture writes where the broken roots stand (`broken`). A restore
    // kills every empty root and respawns the zones that held one, so the
    // stalagmites broken since are whole again; then the spots broken at
    // capture are broken again (their child removed, as the game's break
    // leaves them) - at once when standing, else as their zone spawns
    // (SpawnIndex postfix: a Full load, or walking up). Each spot acts
    // once. The debris a break leaves (`Stalagmite4Broken(Clone)`, a
    // scene root the game removes after ~30 s; chunks the player does not
    // collide with) goes too, unless its spot was broken at capture.
    // ------------------------------------------------------------------
    public sealed class BreakableKeeper
    {
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static ManualLogSource _log;
        private static Type _zone, _crate, _destroyAfter;
        private static FieldInfo _instances, _brokenPrefab;
        private static MethodInfo _despawn, _spawn, _remove;

        // The roots whose breakable child the game destroyed, by instance
        // id. Dead entries (a scene load) are dropped as they are met.
        private static readonly Dictionary<int, GameObject> Broken = new Dictionary<int, GameObject>();
        // Spots broken at capture whose stalagmite has not stood yet.
        private static readonly HashSet<long> Pending = new HashSet<long>();
        private static int _late;
        // The debris each break left, with the spot it broke at.
        private static readonly List<KeyValuePair<GameObject, long>> Debris = new List<KeyValuePair<GameObject, long>>();

        // What the prefix hands the postfix: the break being made.
        private sealed class Break
        {
            public long Spot;
            public string Name;
            public Vector3 At;
        }

        private Harmony _harmony;

        public string Status { get; private set; }

        public BreakableKeeper(ManualLogSource log)
        {
            _log = log;
            Status = "not installed";
        }

        public void Install(string harmonyId)
        {
            _zone = GameBridge.FindGameType("GreebleZone");
            _crate = GameBridge.FindGameType("BreakCrate");
            Type plugin = GameBridge.FindGameType("GreeblePlugin");
            _destroyAfter = GameBridge.FindGameType("destroyAfter");
            _brokenPrefab = _crate != null ? _crate.GetField("Broken", Inst) : null;
            _instances = _zone != null ? _zone.GetField("instances", Inst) : null;
            _despawn = _zone != null ? _zone.GetMethod("Despawn", Inst, null, Type.EmptyTypes, null) : null;
            _spawn = _zone != null ? _zone.GetMethod("Spawn", Inst, null, Type.EmptyTypes, null) : null;
            MethodInfo spawnIndex = _zone != null ? _zone.GetMethod("SpawnIndex", Inst, null, new[] { typeof(int) }, null) : null;
            MethodInfo explode = _crate != null ? _crate.GetMethod("ExplosionReal", Inst, null, Type.EmptyTypes, null) : null;
            _remove = plugin != null ? plugin.GetMethod("Remove", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(GameObject) }, null) : null;
            if (_instances == null || _despawn == null || _spawn == null || spawnIndex == null || explode == null || _remove == null)
            {
                Status = "GreebleZone / BreakCrate / GreeblePlugin members not found";
                _log.LogWarning("BreakableKeeper: " + Status);
                return;
            }

            try
            {
                _harmony = new Harmony(harmonyId + ".breakables");
                _harmony.Patch(explode, new HarmonyMethod(typeof(BreakableKeeper).GetMethod("ExplodePrefix", BindingFlags.Static | BindingFlags.NonPublic)),
                               new HarmonyMethod(typeof(BreakableKeeper).GetMethod("ExplodePostfix", BindingFlags.Static | BindingFlags.NonPublic)));
                _harmony.Patch(spawnIndex, null, new HarmonyMethod(typeof(BreakableKeeper).GetMethod("SpawnIndexPostfix", BindingFlags.Static | BindingFlags.NonPublic)));
                Status = "hooked";
            }
            catch (Exception ex)
            {
                Status = "Harmony: " + ex.Message;
            }
            _log.LogInfo("BreakableKeeper: " + Status + ".");
        }

        public void Uninstall()
        {
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception) { }
            Pending.Clear();
            Broken.Clear();
            Debris.Clear();
        }

        /// Nothing waits once the game is left.
        public void Clear()
        {
            Pending.Clear();
        }

        /// Where the broken roots standing in a zone are; null when none.
        public List<string> Capture()
        {
            if (_harmony == null || Broken.Count == 0) return null;
            List<string> list = null;
            List<int> dead = null;
            foreach (KeyValuePair<int, GameObject> e in Broken)
            {
                GameObject root = e.Value;
                if (root == null) { (dead ?? (dead = new List<int>())).Add(e.Key); continue; }
                if (!root.activeInHierarchy) continue;   // in the pool, standing nowhere
                Vector3 p = root.transform.position;
                (list ?? (list = new List<string>())).Add(SavestateFile.SpotKey(p.x, p.y, p.z));
            }
            if (dead != null) for (int i = 0; i < dead.Count; i++) Broken.Remove(dead[i]);
            return list;
        }

        /// Arms the file's spots and, live, puts the standing stalagmites
        /// back as captured (`live` false before a Full load: the load
        /// builds fresh pools, the spots act as their zones spawn).
        /// Returns a note for the restore line, "" when nothing was to do.
        public string Restore(List<string> broken, bool live)
        {
            Pending.Clear();
            _late = 0;
            if (_harmony == null) return "";
            if (broken != null)
                for (int i = 0; i < broken.Count; i++)
                {
                    long key;
                    if (PositionKey.TryParse(broken[i], 0, out key)) Pending.Add(key);
                }
            if (!live) return "";
            int debris = RemoveDebris();

            // The empty roots: standing ones mark their zone for a respawn
            // unless their spot stays broken; every one is killed, so the
            // pool never hands it out again.
            List<GameObject> empty = new List<GameObject>();
            List<int> dead = null;
            foreach (KeyValuePair<int, GameObject> e in Broken)
            {
                if (e.Value == null) { (dead ?? (dead = new List<int>())).Add(e.Key); continue; }
                empty.Add(e.Value);
            }
            if (dead != null) for (int i = 0; i < dead.Count; i++) Broken.Remove(dead[i]);
            if (empty.Count == 0 && Pending.Count == 0)
                return debris > 0 ? "stalagmites: " + debris + " debris removed" : "";

            int whole = 0, again = 0, killed = 0;
            UnityEngine.Object[] zones = UnityEngine.Object.FindObjectsOfType(_zone);
            List<Component> respawn = new List<Component>();
            for (int i = 0; i < zones.Length; i++)
            {
                Component z = zones[i] as Component;
                GameObject[] inst = z != null ? _instances.GetValue(z) as GameObject[] : null;
                if (inst == null) continue;
                int fix = 0;
                for (int k = 0; k < inst.Length; k++)
                {
                    if (inst[k] == null || !Broken.ContainsKey(inst[k].GetInstanceID())) continue;
                    Vector3 p = inst[k].transform.position;
                    if (!Pending.Contains(PositionKey.Of(p.x, p.y, p.z))) fix++;
                }
                if (fix == 0) continue;
                // Despawn hands every instance to the pool, the empty ones
                // too; they are killed before the zone draws again.
                _despawn.Invoke(z, null);
                respawn.Add(z);
                whole += fix;
            }
            // An empty root in the pool would stand in for a whole
            // stalagmite somewhere else: none is left there. One still
            // standing is on a spot that stays broken.
            for (int i = 0; i < empty.Count; i++)
            {
                GameObject root = empty[i];
                if (root == null || root.activeInHierarchy) continue;
                Broken.Remove(root.GetInstanceID());
                try { _remove.Invoke(null, new object[] { root }); killed++; }
                catch (Exception ex) { _log.LogWarning("BreakableKeeper: removing an empty root failed: " + ex.Message); }
            }
            // Spawn draws the zone's spots again (spread over frames: a
            // spot broken at capture breaks again in the postfix).
            for (int i = 0; i < respawn.Count; i++) _spawn.Invoke(respawn[i], null);

            // A spot broken at capture with its empty root still standing
            // is as captured already: it does not wait for a later spawn.
            foreach (GameObject root in Broken.Values)
                if (root != null && root.activeInHierarchy)
                {
                    Vector3 p = root.transform.position;
                    Pending.Remove(PositionKey.Of(p.x, p.y, p.z));
                }

            // The spots broken at capture that stand whole now.
            if (Pending.Count > 0)
                for (int i = 0; i < zones.Length; i++)
                {
                    Component z = zones[i] as Component;
                    GameObject[] inst = z != null ? _instances.GetValue(z) as GameObject[] : null;
                    if (inst == null) continue;
                    for (int k = 0; k < inst.Length && Pending.Count > 0; k++)
                        if (TryBreak(inst[k])) again++;
                }

            if (whole == 0 && again + _late == 0 && Pending.Count == 0 && killed == 0 && debris == 0) return "";
            return "stalagmites: " + whole + " put back whole, " + (again + _late) + " broken again, " +
                   Pending.Count + " waiting to spawn, " + killed + " empty pool object(s) removed, " + debris + " debris removed";
        }

        /// Removes the debris of every break whose spot is not broken in
        /// the file (Pending holds the file's spots here); returns how many.
        private static int RemoveDebris()
        {
            int n = 0;
            for (int i = Debris.Count - 1; i >= 0; i--)
            {
                GameObject d = Debris[i].Key;
                if (d != null && Pending.Contains(Debris[i].Value)) continue;
                if (d != null) { UnityEngine.Object.Destroy(d); n++; }
                Debris.RemoveAt(i);
            }
            return n;
        }

        /// How many waiting spots were broken as their zone spawned since
        /// the last Restore.
        public static int Late { get { return _late; } }

        /// Breaks `root` the way the game leaves it - its breakable child
        /// gone - when it stands on a waiting spot and is whole.
        private static bool TryBreak(GameObject root)
        {
            if (root == null || Broken.ContainsKey(root.GetInstanceID())) return false;
            Vector3 p = root.transform.position;
            if (!Pending.Remove(PositionKey.Of(p.x, p.y, p.z))) return false;
            Component crate = root.GetComponentInChildren(_crate);
            if (crate == null) return false;
            UnityEngine.Object.Destroy(crate.gameObject);
            Broken[root.GetInstanceID()] = root;
            return true;
        }

        // The game's break, before it destroys the child: remember the
        // pooled root it leaves empty. Never throws into the game.
        private static void ExplodePrefix(object __instance, out Break __state)
        {
            __state = null;
            try
            {
                Component c = __instance as Component;
                Transform root = c != null ? c.transform.parent : null;
                if (root == null || root.root.name != "Pooling") return;
                Broken[root.gameObject.GetInstanceID()] = root.gameObject;
                GameObject prefab = _brokenPrefab != null ? _brokenPrefab.GetValue(__instance) as GameObject : null;
                if (prefab == null || _destroyAfter == null) return;
                Vector3 p = root.position;
                __state = new Break { Spot = PositionKey.Of(p.x, p.y, p.z), Name = prefab.name + "(Clone)", At = c.transform.position };
            }
            catch (Exception ex)
            {
                _log.LogWarning("BreakableKeeper: break prefix failed: " + ex.Message);
            }
        }

        // After the break: the debris it just instantiated - a scene root
        // of the prefab's name where the child stood. Only on a break, so
        // the search is rare. Never throws into the game.
        private static void ExplodePostfix(Break __state)
        {
            if (__state == null) return;
            try
            {
                UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(_destroyAfter);
                for (int i = 0; i < all.Length; i++)
                {
                    Component d = all[i] as Component;
                    if (d == null || d.transform.parent != null || d.name != __state.Name) continue;
                    if ((d.transform.position - __state.At).sqrMagnitude > 0.01f || Tracked(d.gameObject)) continue;
                    Debris.Add(new KeyValuePair<GameObject, long>(d.gameObject, __state.Spot));
                    return;
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning("BreakableKeeper: break postfix failed: " + ex.Message);
            }
        }

        private static bool Tracked(GameObject g)
        {
            for (int i = 0; i < Debris.Count; i++) if (Debris[i].Key == g) return true;
            return false;
        }

        // After the game spawns one instance of a zone: a spot broken at
        // capture is broken again. Never throws into the game.
        private static void SpawnIndexPostfix(object __instance, int index)
        {
            if (Pending.Count == 0) return;
            try
            {
                GameObject[] inst = _instances.GetValue(__instance) as GameObject[];
                if (inst == null || index < 0 || index >= inst.Length) return;
                if (TryBreak(inst[index])) _late++;
            }
            catch (Exception ex)
            {
                _log.LogWarning("BreakableKeeper: spawn postfix failed: " + ex.Message);
            }
        }
    }
}
