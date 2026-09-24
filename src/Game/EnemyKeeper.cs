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
    // Cannibals at a savestate capture, put back after an in-place restore
    // (fix list 2; Data/EnemyRecord has the format and the matching).
    //
    // All of it seen live through the test bridge (2026-09-24):
    //   mutantController.activeCannibals - the live cannibal roots;
    //   each root: enemyType.Type (EnemyType enum), mutantTypeSetup.spawner
    //   (its family's spawnMutants), mutantTypeSetup.health (EnemyHealth,
    //   Int32 Health on the _BASE child).
    //   spawnMutants.fixMutantPosition(Transform, Vector3) - the game's own
    //   placement (holds the position ~1 s, sends
    //   updateWorldTransformPosition); a cannibal moved with it next to the
    //   player slept standing, woke when approached and fought normally.
    // ------------------------------------------------------------------
    public sealed class EnemyKeeper
    {
        private readonly ManualLogSource _log;
        private bool _bound;
        private FieldInfo _controller;      // static Scene.MutantControler
        private FieldInfo _active;          // mutantController.activeCannibals
        private Type _typeSetup;            // mutantTypeSetup
        private FieldInfo _spawner;         // mutantTypeSetup.spawner
        private FieldInfo _health;          // mutantTypeSetup.health
        private FieldInfo _healthValue;     // EnemyHealth.Health
        private Type _enemyType;            // enemyType
        private PropertyInfo _enemyTypeValue; // enemyType.Type
        private MethodInfo _fixPosition;    // spawnMutants.fixMutantPosition

        public string Status { get; private set; }

        public EnemyKeeper(ManualLogSource log)
        {
            _log = log;
            Status = "not bound";
        }

        private bool Bind()
        {
            if (_bound) return _active != null && _spawner != null && _fixPosition != null && _enemyTypeValue != null;
            _bound = true;
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            Type scene = GameBridge.FindGameType("TheForest.Utils.Scene");
            if (scene != null) _controller = scene.GetField("MutantControler", stat);
            Type ctrl = GameBridge.FindGameType("mutantController");
            if (ctrl != null) _active = ctrl.GetField("activeCannibals", inst);
            _typeSetup = GameBridge.FindGameType("mutantTypeSetup");
            if (_typeSetup != null)
            {
                _spawner = _typeSetup.GetField("spawner", inst);
                _health = _typeSetup.GetField("health", inst);
            }
            Type health = GameBridge.FindGameType("EnemyHealth");
            if (health != null) _healthValue = health.GetField("Health", inst);
            _enemyType = GameBridge.FindGameType("enemyType");
            if (_enemyType != null) _enemyTypeValue = _enemyType.GetProperty("Type", inst);
            Type spawn = GameBridge.FindGameType("spawnMutants");
            if (spawn != null) _fixPosition = spawn.GetMethod("fixMutantPosition", inst, null, new[] { typeof(Transform), typeof(Vector3) }, null);

            Status = "list:" + (_controller != null && _active != null) + " family:" + (_spawner != null) +
                     " type:" + (_enemyTypeValue != null) + " health:" + (_health != null && _healthValue != null) +
                     " place:" + (_fixPosition != null);
            _log.LogInfo("EnemyKeeper bound. " + Status);
            return _active != null && _spawner != null && _fixPosition != null && _enemyTypeValue != null;
        }

        private struct Cannibal
        {
            public GameObject Go;
            public Component Setup;
            public MonoBehaviour Spawner;
            public string Type;
        }

        private List<Cannibal> LiveCannibals()
        {
            List<Cannibal> list = new List<Cannibal>();
            object ctrl = _controller != null ? _controller.GetValue(null) : null;
            IList active = ctrl != null ? _active.GetValue(ctrl) as IList : null;
            if (active == null) return list;

            for (int i = 0; i < active.Count; i++)
            {
                GameObject go = active[i] as GameObject;
                if (go == null || !go.activeInHierarchy) continue;
                Component setup = go.GetComponent(_typeSetup);
                Component type = go.GetComponent(_enemyType);
                if (setup == null || type == null) continue;

                Cannibal c;
                c.Go = go;
                c.Setup = setup;
                c.Spawner = _spawner.GetValue(setup) as MonoBehaviour;
                object t = null;
                try { t = _enemyTypeValue.GetValue(type, null); } catch (Exception) { }
                c.Type = t != null ? t.ToString() : "?";
                list.Add(c);
            }
            return list;
        }

        /// The live cannibals as EnemyRecord entries; "" note when none.
        public List<string> Capture(out string note)
        {
            List<string> entries = new List<string>();
            note = "";
            if (!Bind()) { note = "enemies: not bound (" + Status + ")"; return entries; }
            try
            {
                List<Cannibal> live = LiveCannibals();
                Dictionary<MonoBehaviour, int> families = new Dictionary<MonoBehaviour, int>();
                for (int i = 0; i < live.Count; i++)
                {
                    Cannibal c = live[i];
                    int family;
                    if (c.Spawner == null) family = 1000 + i;   // no family: its own
                    else if (!families.TryGetValue(c.Spawner, out family)) families[c.Spawner] = family = families.Count;

                    EnemyRecord r = new EnemyRecord();
                    r.Family = family;
                    r.Type = c.Type;
                    r.Position = c.Go.transform.position;
                    r.Yaw = c.Go.transform.eulerAngles.y;
                    r.Health = ReadHealth(c.Setup);
                    entries.Add(r.Encode());
                }
                note = live.Count + " cannibal(s) in " + families.Count + " famil" + (families.Count == 1 ? "y" : "ies");
            }
            catch (Exception ex) { note = "enemies: capture failed (" + ex.Message + ")"; }
            return entries;
        }

        private int ReadHealth(Component setup)
        {
            try
            {
                object h = _health != null ? _health.GetValue(setup) : null;
                return h != null && _healthValue != null ? (int)_healthValue.GetValue(h) : -1;
            }
            catch (Exception) { return -1; }
        }

        private void WriteHealth(Component setup, int value)
        {
            if (value <= 0) return;
            try
            {
                object h = _health != null ? _health.GetValue(setup) : null;
                if (h != null && _healthValue != null) _healthValue.SetValue(h, value);
            }
            catch (Exception) { }
        }

        /// Moves the game's live cannibals to the captured ones' places.
        /// Call once the families are back (after the restore's enemy
        /// check). Returns the note for the log.
        public string Restore(List<string> entries)
        {
            if (entries == null) return "";
            if (!Bind()) return "positions: not bound (" + Status + ")";
            try
            {
                List<EnemyRecord> captured = new List<EnemyRecord>();
                int bad = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    EnemyRecord r;
                    if (EnemyRecord.TryDecode(entries[i], out r)) captured.Add(r);
                    else bad++;
                }
                if (captured.Count == 0) return "positions: none captured";

                List<Cannibal> live = LiveCannibals();
                List<EnemyRecord.Live> keys = new List<EnemyRecord.Live>();
                Dictionary<MonoBehaviour, int> families = new Dictionary<MonoBehaviour, int>();
                for (int i = 0; i < live.Count; i++)
                {
                    int family;
                    if (live[i].Spawner == null) family = 1000 + i;
                    else if (!families.TryGetValue(live[i].Spawner, out family)) families[live[i].Spawner] = family = families.Count;
                    EnemyRecord.Live k;
                    k.Family = family;
                    k.Type = live[i].Type;
                    keys.Add(k);
                }

                int whole;
                int[] match = EnemyRecord.Match(captured, keys, out whole);
                int placed = 0;
                for (int i = 0; i < captured.Count; i++)
                {
                    if (match[i] < 0) continue;
                    Cannibal c = live[match[i]];
                    Transform t = c.Go.transform;
                    t.rotation = Quaternion.Euler(0f, captured[i].Yaw, 0f);
                    MonoBehaviour host = c.Spawner != null && c.Spawner.isActiveAndEnabled ? c.Spawner : null;
                    if (host != null)
                    {
                        IEnumerator routine = _fixPosition.Invoke(c.Spawner, new object[] { t, captured[i].Position }) as IEnumerator;
                        if (routine != null) host.StartCoroutine(routine);
                    }
                    else t.position = captured[i].Position;
                    WriteHealth(c.Setup, captured[i].Health);
                    placed++;
                }
                return "positions: " + placed + " of " + captured.Count + " placed (" + whole + " famil" + (whole == 1 ? "y" : "ies") +
                       " matched whole, " + live.Count + " live)" + (bad > 0 ? ", " + bad + " unreadable" : "");
            }
            catch (Exception ex) { return "positions: failed (" + (ex.InnerException ?? ex).Message + ")"; }
        }
    }
}
