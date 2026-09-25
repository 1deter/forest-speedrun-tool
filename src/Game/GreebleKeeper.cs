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
    // The sticks and rocks around trees after a restore (fix list 3:
    // "pickups move" - the "not at capture" lines listed Stick / Rock).
    //
    // WHY (bridge + IL, 2026-09-25; game-notes *Greebles*): a greeble zone
    // on a pooled tree (`Pooling/Pool_Trees/...(Clone)00N/Greeb`) keeps its
    // own GZData on the pool object: the seed fixed by the first tree that
    // object served, and its taken flags. Which pool object serves a tree
    // changes whenever the player leaves and comes back, so a restore
    // after a trip showed other sticks around the same tree (three layouts
    // in three visits, the captured sticks inactive). Vanilla behaviour -
    // a restore only makes it visible.
    //
    // WHAT (author, 2026-09-25: restore-only; normal play untouched):
    // capture writes each live pooled zone's place, seed and state bytes
    // (`greebles`). A restore puts them back on the zone standing there:
    // at once for a live zone (despawn, set, spawn), and for one that
    // spawns later - walking up, or during a Full load - from a prefix on
    // GreebleZone.Spawn. Each record acts once; after that the zone is the
    // game's again (walk off and back and it re-rolls, as in vanilla).
    // Zones in GreebleZonesManager are in the save already and left alone.
    // ------------------------------------------------------------------
    public sealed class GreebleKeeper
    {
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static ManualLogSource _log;
        private static Type _zone;
        private static PropertyInfo _data;
        private static FieldInfo _seed, _states, _instanceData;
        private static MethodInfo _despawn, _spawn;

        // Records not yet given back, and how many acted late (in Spawn).
        private static readonly List<GreebleRecord> Pending = new List<GreebleRecord>();
        private static int _late;
        private static bool _applying;

        private Harmony _harmony;

        public string Status { get; private set; }

        public GreebleKeeper(ManualLogSource log)
        {
            _log = log;
            Status = "not installed";
        }

        public void Install(string harmonyId)
        {
            _zone = GameBridge.FindGameType("GreebleZone");
            _data = _zone != null ? _zone.GetProperty("Data", Inst) : null;
            Type gz = _data != null ? _data.PropertyType : null;
            _seed = gz != null ? gz.GetField("_seed", Inst) : null;
            _states = gz != null ? gz.GetField("_instancesState", Inst) : null;
            _instanceData = _zone != null ? _zone.GetField("InstanceData", Inst) : null;
            _despawn = _zone != null ? _zone.GetMethod("Despawn", Inst, null, Type.EmptyTypes, null) : null;
            _spawn = _zone != null ? _zone.GetMethod("Spawn", Inst, null, Type.EmptyTypes, null) : null;
            if (_seed == null || _states == null || _instanceData == null || _despawn == null || _spawn == null)
            {
                Status = "GreebleZone members not found";
                _log.LogWarning("GreebleKeeper: " + Status);
                return;
            }

            try
            {
                _harmony = new Harmony(harmonyId + ".greebles");
                _harmony.Patch(_spawn, new HarmonyMethod(typeof(GreebleKeeper).GetMethod("SpawnPrefix", BindingFlags.Static | BindingFlags.NonPublic)));
                Status = "hooked";
            }
            catch (Exception ex)
            {
                Status = "Harmony: " + ex.Message;
            }
            _log.LogInfo("GreebleKeeper: " + Status + ".");
        }

        public void Uninstall()
        {
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception) { }
            Pending.Clear();
        }

        /// Nothing waits once the game is left.
        public void Clear()
        {
            Pending.Clear();
        }

        /// The live pooled zones as GreebleRecord entries; null when the
        /// type is missing (the file then has no line, as an old one).
        public List<string> Capture()
        {
            if (_zone == null || _seed == null) return null;
            List<string> list = new List<string>();
            UnityEngine.Object[] zones = UnityEngine.Object.FindObjectsOfType(_zone);
            for (int i = 0; i < zones.Length; i++)
            {
                Component z = zones[i] as Component;
                if (!Pooled(z)) continue;
                object data = _data.GetValue(z, null);
                if (data == null) continue;
                int seed = (int)_seed.GetValue(data);
                if (seed == -1) continue;   // never spawned: nothing drawn yet
                byte[] states = _states.GetValue(data) as byte[];
                Vector3 p = z.transform.position;
                GreebleRecord r = new GreebleRecord();
                r.X = p.x; r.Y = p.y; r.Z = p.z;
                r.Seed = seed;
                r.States = states != null ? (byte[])states.Clone() : new byte[0];
                list.Add(r.Format());
            }
            return list;
        }

        /// Arms the file's records and gives them back to the live zones
        /// now (`live` false before a Full load: nothing live to keep).
        /// Returns a note for the restore line, "" when the file has none.
        public string Restore(List<string> entries, bool live)
        {
            Pending.Clear();
            _late = 0;
            if (entries == null || _zone == null) return "";
            int bad;
            Pending.AddRange(GreebleRecord.ParseAll(entries, out bad));
            if (!live) return "";

            int set = 0, matched = 0;
            UnityEngine.Object[] zones = UnityEngine.Object.FindObjectsOfType(_zone);
            for (int i = 0; i < zones.Length; i++)
            {
                Component z = zones[i] as Component;
                if (!Pooled(z)) continue;
                GreebleRecord r = Take(z.transform.position);
                if (r == null) continue;
                try
                {
                    if (Apply(z, r, true)) set++;
                    else matched++;
                }
                catch (Exception ex) { _log.LogWarning("GreebleKeeper: zone at " + z.transform.position + " failed: " + ex.Message); }
            }
            return "greebles: " + set + " tree zone(s) re-drawn as captured, " + matched + " already matched, " +
                   Pending.Count + " waiting to spawn" + (bad > 0 ? ", " + bad + " unreadable" : "");
        }

        /// How many waiting records acted as their zone spawned since the
        /// last Restore.
        public static int Late { get { return _late; } }

        private static bool Pooled(Component z)
        {
            return z != null && z.gameObject.activeInHierarchy && z.transform.root.name == "Pooling";
        }

        private static GreebleRecord Take(Vector3 p)
        {
            int i = GreebleRecord.IndexAt(Pending, p.x, p.y, p.z);
            if (i < 0) return null;
            GreebleRecord r = Pending[i];
            Pending.RemoveAt(i);
            return r;
        }

        /// Gives the record's seed and taken flags to the zone. `respawn`:
        /// the zone is live - clear it, set, spawn again. Otherwise the
        /// game's Spawn follows (the prefix). False when it already matched.
        private static bool Apply(Component z, GreebleRecord r, bool respawn)
        {
            object data = _data.GetValue(z, null);
            if (data == null) return false;
            byte[] states = _states.GetValue(data) as byte[];
            if (r.Matches((int)_seed.GetValue(data), states)) return false;

            // Despawn while InstanceData is intact, so the old instances go
            // back to the pool; it marks missing ones taken - overwritten.
            _despawn.Invoke(z, null);
            _seed.SetValue(data, r.Seed);
            _states.SetValue(data, (byte[])r.States.Clone());
            // Spawn rebuilds InstanceData (the Destroyed flags) from the
            // state bytes only when it is missing or the wrong length.
            _instanceData.SetValue(z, null);
            if (respawn)
            {
                _applying = true;
                try { _spawn.Invoke(z, null); }
                finally { _applying = false; }
            }
            return true;
        }

        // Before the game spawns a zone's greebles. Never throws into the
        // game; never skips the original.
        private static void SpawnPrefix(object __instance)
        {
            if (_applying || Pending.Count == 0) return;
            try
            {
                Component z = __instance as Component;
                if (z == null || z.transform.root.name != "Pooling") return;
                GreebleRecord r = Take(z.transform.position);
                if (r == null) return;
                if (Apply(z, r, false))
                {
                    _late++;
                    _log.LogInfo("GreebleKeeper: tree zone at " + z.transform.position + " spawned with its captured sticks (seed " + r.Seed + ").");
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning("GreebleKeeper: spawn prefix failed: " + ex.Message);
            }
        }
    }
}
