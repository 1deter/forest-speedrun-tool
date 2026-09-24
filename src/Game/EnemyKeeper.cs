using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Cannibals at a savestate capture, rebuilt after an in-place restore
    // (fix list 2; Data/EnemyRecord has the formats and the matching).
    //
    // All of it seen live through the test bridge (2026-09-24):
    //   mutantController.activeCannibals - the live cannibal roots; all
    //   male kinds share the pooled mutant_male prefab (females
    //   mutant_female) and the spawner makes each its kind by message
    //   (setMaleSkinny, setSkinnyLeader, ...), which leaves
    //   mutantTypeSetup.storeSkinnyBool / storePaleMutantBool /
    //   storeMutantType - enemyType.Type is NOT the kind (a skinny one
    //   read regularMale, a pooled leftover). So a member's kind here is
    //   "<prefab>/<storeMutantType>[/s][/p]".
    //   A family is a spawnMutants (mutantTypeSetup.spawner); its kind is
    //   its settings (amount_*, leader, pale, paintedTribe, ...), rolled at
    //   random by mutantController.setup<Kind>Spawn - a restore's families
    //   came back as other kinds. So the restore builds the captured
    //   families itself, the way updateSpawns does: Instantiate(spawnGo),
    //   settings, the kind list and counters (numActive<Kind>Spawns,
    //   numActiveSpawns), enabled, invokeSpawn(), addToWorldSpawns();
    //   invokeSpawn's first checkSpawn spawns a surface family at once.
    //   Cave families spawn only with a player in the caves within 130 m,
    //   so those are started directly. Members spawn from the "enemies"
    //   pool at the spawner; spawnMutants.fixMutantPosition moves one (a
    //   moved cannibal slept, woke when approached and fought normally -
    //   author).
    //   Sleep: a family rebuilt 20 m from the player came back awake. The
    //   AI is PlayMaker FSMs on the _BASE child; asleep = action_sleepingFSM
    //   in "sleeping". mutantFollowerFunctions.switchToSleep() sends
    //   "toSetSleep": the cannibal walks to action_sleepingFSM's sleepPos
    //   (the spawner by default) and sleeps - set sleepPos to where it
    //   stands first and it sleeps on the spot (seen live, v0.24.18).
    //   The leader is spawnMutants.leaderGo; its kind gets "/L".
    // ------------------------------------------------------------------
    public sealed class EnemyKeeper
    {
        private static readonly string[] KindLists =
        {
            "allRegularSpawns", "allSkinnySpawns", "allSkinnyPaleSpawns", "allPaintedSpawns",
            "allPaleSpawns", "allCreepySpawns", "allSkinnedSpawns", "allSleepingSpawns", "allCaveSpawns"
        };

        // The counter setup<Kind>Spawn raises for a list (IL, 2026-09-24).
        private static readonly string[,] Counters =
        {
            { "allRegularSpawns", "numActiveRegularSpawns" },
            { "allSkinnySpawns", "numActiveSkinnySpawns" },
            { "allSkinnyPaleSpawns", "numActiveSkinnyPaleSpawns" },
            { "allPaintedSpawns", "numActivePaintedSpawns" },
            { "allPaleSpawns", "numActivePaleSpawns" },
            { "allCreepySpawns", "numActiveCreepySpawns" },
            { "allSkinnedSpawns", "numActiveSkinnedSpawns" },
        };

        // spawnMutants fields that are runtime state, not the family.
        private static readonly string[] NotSettings =
        {
            "alreadySpawned", "nameCount", "checkPlayerDist", "doLeader", "debugAddToLists", "debugMovement"
        };

        private readonly ManualLogSource _log;
        private bool _bound;
        private Type _ctrlType;
        private FieldInfo _controller;      // static Scene.MutantControler
        private FieldInfo _active;          // mutantController.activeCannibals
        private FieldInfo _spawnGo;         // mutantController.spawnGo
        private FieldInfo _horde;           // mutantController.hordeModeActive
        private MethodInfo _startSetup;     // mutantController.startSetupFamilies()
        private MethodInfo _despawnGo;      // mutantController.despawnGo(GameObject)
        private PropertyInfo _noEnemies;    // static Cheats.NoEnemies
        private Type _typeSetup;            // mutantTypeSetup
        private FieldInfo _spawner, _health, _skinny, _pale, _mutantType;
        private FieldInfo _healthValue;     // EnemyHealth.Health
        private Type _enemyType;
        private PropertyInfo _enemyTypeValue; // enemyType.Type (old files only)
        private Type _spawnType;            // spawnMutants
        private FieldInfo _members;         // spawnMutants.allMembers
        private FieldInfo _leaderGo;        // spawnMutants.leaderGo
        private Type _follower;             // mutantFollowerFunctions
        private MethodInfo _switchToSleep;  // mutantFollowerFunctions.switchToSleep()
        private FieldInfo _alreadySpawned;
        private FieldInfo _sleepingSpawn;   // spawnMutants.sleepingSpawn
        private Type _dayCycle;             // mutantDayCycle
        private FieldInfo _sleepBlocker;    // mutantDayCycle.sleepBlocker
        private MethodInfo _fixPosition, _invokeSpawn, _addToWorldSpawns;
        private readonly List<FieldInfo> _settings = new List<FieldInfo>();

        public string Status { get; private set; }

        public EnemyKeeper(ManualLogSource log)
        {
            _log = log;
            Status = "not bound";
        }

        private bool Ready
        {
            get
            {
                return _active != null && _spawner != null && _fixPosition != null && _members != null &&
                       _spawnGo != null && _invokeSpawn != null && _addToWorldSpawns != null && _despawnGo != null;
            }
        }

        private bool Bind()
        {
            if (_bound) return Ready;
            _bound = true;
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            Type scene = GameBridge.FindGameType("TheForest.Utils.Scene");
            if (scene != null) _controller = scene.GetField("MutantControler", stat);
            _ctrlType = GameBridge.FindGameType("mutantController");
            if (_ctrlType != null)
            {
                _active = _ctrlType.GetField("activeCannibals", inst);
                _spawnGo = _ctrlType.GetField("spawnGo", inst);
                _horde = _ctrlType.GetField("hordeModeActive", inst);
                _startSetup = _ctrlType.GetMethod("startSetupFamilies", inst, null, Type.EmptyTypes, null);
                _despawnGo = _ctrlType.GetMethod("despawnGo", inst, null, new[] { typeof(GameObject) }, null);
            }
            Type cheats = GameBridge.FindGameType("Cheats");
            if (cheats != null) _noEnemies = cheats.GetProperty("NoEnemies", stat);

            _typeSetup = GameBridge.FindGameType("mutantTypeSetup");
            if (_typeSetup != null)
            {
                _spawner = _typeSetup.GetField("spawner", inst);
                _health = _typeSetup.GetField("health", inst);
                _skinny = _typeSetup.GetField("storeSkinnyBool", inst);
                _pale = _typeSetup.GetField("storePaleMutantBool", inst);
                _mutantType = _typeSetup.GetField("storeMutantType", inst);
            }
            _follower = GameBridge.FindGameType("mutantFollowerFunctions");
            if (_follower != null) _switchToSleep = _follower.GetMethod("switchToSleep", inst, null, Type.EmptyTypes, null);
            _dayCycle = GameBridge.FindGameType("mutantDayCycle");
            if (_dayCycle != null) _sleepBlocker = _dayCycle.GetField("sleepBlocker", inst);
            Type health = GameBridge.FindGameType("EnemyHealth");
            if (health != null) _healthValue = health.GetField("Health", inst);
            _enemyType = GameBridge.FindGameType("enemyType");
            if (_enemyType != null) _enemyTypeValue = _enemyType.GetProperty("Type", inst);

            _spawnType = GameBridge.FindGameType("spawnMutants");
            if (_spawnType != null)
            {
                _members = _spawnType.GetField("allMembers", inst);
                _leaderGo = _spawnType.GetField("leaderGo", inst);
                _alreadySpawned = _spawnType.GetField("alreadySpawned", inst);
                _sleepingSpawn = _spawnType.GetField("sleepingSpawn", inst);
                _fixPosition = _spawnType.GetMethod("fixMutantPosition", inst, null, new[] { typeof(Transform), typeof(Vector3) }, null);
                _invokeSpawn = _spawnType.GetMethod("invokeSpawn", inst, null, Type.EmptyTypes, null);
                _addToWorldSpawns = _spawnType.GetMethod("addToWorldSpawns", inst, null, Type.EmptyTypes, null);
                FieldInfo[] fields = _spawnType.GetFields(inst | BindingFlags.DeclaredOnly);
                for (int i = 0; i < fields.Length; i++)
                {
                    Type ft = fields[i].FieldType;
                    if (ft != typeof(int) && ft != typeof(bool) && ft != typeof(float)) continue;
                    if (Array.IndexOf(NotSettings, fields[i].Name) >= 0) continue;
                    _settings.Add(fields[i]);
                }
            }

            Status = "list:" + (_controller != null && _active != null) + " family:" + (_spawner != null && _members != null) +
                     " kind:" + (_skinny != null && _pale != null && _mutantType != null) +
                     " health:" + (_health != null && _healthValue != null) +
                     " build:" + (_spawnGo != null && _invokeSpawn != null && _addToWorldSpawns != null && _alreadySpawned != null) +
                     " clear:" + (_startSetup != null && _despawnGo != null) +
                     " place:" + (_fixPosition != null) + " sleep:" + (_switchToSleep != null && _sleepingSpawn != null && _sleepBlocker != null) +
                     " leader:" + (_leaderGo != null) + " settings:" + _settings.Count;
            _log.LogInfo("EnemyKeeper bound. " + Status);
            return Ready;
        }

        private MonoBehaviour Controller()
        {
            return _controller != null ? _controller.GetValue(null) as MonoBehaviour : null;
        }

        // ------------------------------------------------------------------
        // Reading the live cannibals

        private struct Cannibal
        {
            public GameObject Go;
            public Component Setup;
            public MonoBehaviour Spawner;
            public string Kind;
        }

        private string KindOf(GameObject go, Component setup)
        {
            string name = go.name;
            int clone = name.IndexOf("(Clone)", StringComparison.Ordinal);
            string prefab = clone > 0 ? name.Substring(0, clone) : name;
            try
            {
                // An enum: a plain (int) cast threw and dropped the kind (v0.24.17).
                int type = _mutantType != null ? Convert.ToInt32(_mutantType.GetValue(setup), CultureInfo.InvariantCulture) : 0;
                bool skinny = _skinny != null && (bool)_skinny.GetValue(setup);
                bool pale = _pale != null && (bool)_pale.GetValue(setup);
                return prefab + "/" + type.ToString(CultureInfo.InvariantCulture) + (skinny ? "/s" : "") + (pale ? "/p" : "");
            }
            catch (Exception) { return prefab; }
        }

        private Cannibal Read(GameObject go)
        {
            Cannibal c = new Cannibal();
            c.Go = go;
            c.Setup = go != null ? go.GetComponent(_typeSetup) : null;
            if (c.Setup == null) return c;
            c.Spawner = _spawner.GetValue(c.Setup) as MonoBehaviour;
            c.Kind = KindOf(go, c.Setup);
            try
            {
                GameObject leader = c.Spawner != null && _leaderGo != null ? _leaderGo.GetValue(c.Spawner) as GameObject : null;
                if (leader != null && leader == go) c.Kind += "/L";
            }
            catch (Exception) { }
            return c;
        }

        private List<Cannibal> LiveCannibals()
        {
            List<Cannibal> list = new List<Cannibal>();
            MonoBehaviour ctrl = Controller();
            IList active = ctrl != null ? _active.GetValue(ctrl) as IList : null;
            if (active == null) return list;
            for (int i = 0; i < active.Count; i++)
            {
                GameObject go = active[i] as GameObject;
                if (go == null || !go.activeInHierarchy) continue;
                Cannibal c = Read(go);
                if (c.Setup != null) list.Add(c);
            }
            return list;
        }

        private string ListsHolding(MonoBehaviour ctrl, GameObject spawner)
        {
            List<string> names = new List<string>();
            for (int i = 0; i < KindLists.Length; i++)
            {
                FieldInfo f = _ctrlType.GetField(KindLists[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                IList list = f != null ? f.GetValue(ctrl) as IList : null;
                if (list != null && list.Contains(spawner)) names.Add(KindLists[i]);
            }
            return string.Join("+", names.ToArray());
        }

        // ------------------------------------------------------------------
        // Capture

        /// Members (EnemyRecord) and their families (FamilyRecord).
        public void Capture(List<string> members, List<string> families, out string note)
        {
            note = "";
            if (!Bind()) { note = "enemies: not bound (" + Status + ")"; return; }
            try
            {
                MonoBehaviour ctrl = Controller();
                List<Cannibal> live = LiveCannibals();
                Dictionary<MonoBehaviour, int> index = new Dictionary<MonoBehaviour, int>();
                for (int i = 0; i < live.Count; i++)
                {
                    Cannibal c = live[i];
                    int family;
                    if (c.Spawner == null) family = 1000 + i;
                    else if (!index.TryGetValue(c.Spawner, out family))
                    {
                        index[c.Spawner] = family = index.Count;
                        families.Add(RecordFamily(ctrl, c.Spawner, family).Encode());
                    }

                    EnemyRecord r = new EnemyRecord();
                    r.Family = family;
                    r.Type = c.Kind;
                    r.Position = c.Go.transform.position;
                    r.Yaw = c.Go.transform.eulerAngles.y;
                    r.Health = ReadHealth(c.Setup);
                    r.Asleep = IsAsleep(c.Go);
                    members.Add(r.Encode());
                }
                note = live.Count + " cannibal(s) in " + index.Count + " famil" + (index.Count == 1 ? "y" : "ies");
            }
            catch (Exception ex) { note = "enemies: capture failed (" + ex.Message + ")"; }
        }

        private FamilyRecord RecordFamily(MonoBehaviour ctrl, MonoBehaviour spawner, int index)
        {
            FamilyRecord f = new FamilyRecord();
            f.Index = index;
            f.Position = spawner.transform.position;
            f.Yaw = spawner.transform.eulerAngles.y;
            f.List = ListsHolding(ctrl, spawner.gameObject);
            for (int i = 0; i < _settings.Count; i++)
            {
                object v = _settings[i].GetValue(spawner);
                f.Settings.Add(new KeyValuePair<string, string>(_settings[i].Name,
                    Convert.ToString(v, CultureInfo.InvariantCulture)));
            }
            return f;
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

        // ------------------------------------------------------------------
        // Restore: rebuild the captured families

        /// Why a rebuild cannot run now, or null.
        public string CannotRebuild()
        {
            if (!Bind()) return "positions: not bound (" + Status + ")";
            MonoBehaviour ctrl = Controller();
            if (ctrl == null) return "positions: no spawn controller";
            try
            {
                if (_horde != null && (bool)_horde.GetValue(ctrl)) return "positions: horde mode";
                if (_noEnemies != null && (bool)_noEnemies.GetValue(null, null)) return "positions: enemies off in this game";
            }
            catch (Exception) { }
            return null;
        }

        /// After an in-place restore on the surface, with its setup lock
        /// clear (~1.5 s): runs the game's family setup (so its spawn loop
        /// is alive - the restore's own run dies partway), replaces the
        /// families it would roll with the captured ones, and puts every
        /// captured cannibal back. `done` gets the log note.
        public IEnumerator Rebuild(List<string> familyEntries, List<string> memberEntries, Action<string> done)
        {
            string why = CannotRebuild();
            if (why != null) { done(why); yield break; }

            List<FamilyRecord> families = new List<FamilyRecord>();
            for (int i = 0; i < familyEntries.Count; i++)
            {
                FamilyRecord f;
                if (FamilyRecord.TryDecode(familyEntries[i], out f)) families.Add(f);
            }
            List<EnemyRecord> members = new List<EnemyRecord>();
            for (int i = 0; i < memberEntries.Count; i++)
            {
                EnemyRecord r;
                if (EnemyRecord.TryDecode(memberEntries[i], out r)) members.Add(r);
            }

            MonoBehaviour ctrl = Controller();
            string setupNote;
            try
            {
                // Despawns every cannibal, destroys the world families, zeroes
                // the counters and starts updateSpawns (game-notes).
                _startSetup.Invoke(ctrl, null);
                setupNote = "setup run";
            }
            catch (Exception ex) { done("positions: the game's setup failed (" + (ex.InnerException ?? ex).Message + ")"); yield break; }
            yield return new WaitForSecondsRealtime(0.5f);

            // Build every captured world family; cave families are scene
            // objects that stay - found again by position.
            Dictionary<int, MonoBehaviour> built = new Dictionary<int, MonoBehaviour>();
            int made = 0, cave = 0, failed = 0;
            for (int i = 0; i < families.Count; i++)
            {
                FamilyRecord f = families[i];
                MonoBehaviour sm = null;
                try
                {
                    if (f.List.IndexOf("allCaveSpawns", StringComparison.Ordinal) >= 0)
                    {
                        sm = FindCaveSpawner(ctrl, f.Position);
                        if (sm != null) { cave++; StartSpawn(sm); }
                    }
                    else
                    {
                        sm = BuildFamily(ctrl, f);
                        if (sm != null) made++;
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning("EnemyKeeper: family " + f.Index + " not built: " + (ex.InnerException ?? ex).Message);
                }
                if (sm != null) built[f.Index] = sm;
                else failed++;
            }

            // The spawn routines yield between members.
            yield return new WaitForSecondsRealtime(1.5f);

            int placed = 0, extra = 0, missing = 0, grounded = 0, unblocked = 0, sleepFamilies = 0;
            StringBuilder drops = new StringBuilder();
            List<Cannibal> sleepers = new List<Cannibal>();
            List<Vector3> sleepAt = new List<Vector3>();
            foreach (KeyValuePair<int, MonoBehaviour> kv in built)
            {
                List<EnemyRecord> want = new List<EnemyRecord>();
                for (int i = 0; i < members.Count; i++) if (members[i].Family == kv.Key) want.Add(members[i]);

                List<Cannibal> have = new List<Cannibal>();
                List<EnemyRecord.Live> keys = new List<EnemyRecord.Live>();
                IList list = kv.Value != null ? _members.GetValue(kv.Value) as IList : null;
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        GameObject go = list[i] as GameObject;
                        if (go == null || !go.activeInHierarchy) continue;
                        Cannibal c = Read(go);
                        if (c.Setup == null) continue;
                        have.Add(c);
                        EnemyRecord.Live k;
                        k.Family = 0;
                        k.Type = c.Kind;
                        keys.Add(k);
                    }
                }

                int whole;
                int[] match = EnemyRecord.Match(want, keys, out whole);
                bool[] used = new bool[have.Count];

                // invokeSpawn re-rolls sleepingSpawn (updateSpawnConditions);
                // a family with sleepers at capture is a sleeping family, so
                // the game's own initWakeUp (5 s after a member is enabled)
                // keeps them asleep instead of waking them (game-notes).
                bool anyAsleep = false;
                for (int i = 0; i < want.Count; i++) if (want[i].Asleep) anyAsleep = true;
                if (anyAsleep && _sleepingSpawn != null && kv.Value != null)
                {
                    try { _sleepingSpawn.SetValue(kv.Value, true); sleepFamilies++; } catch (Exception) { }
                }
                for (int i = 0; i < want.Count; i++)
                {
                    if (match[i] < 0) { missing++; continue; }
                    used[match[i]] = true;
                    Vector3 at = Ground(want[i].Position);
                    float drop = want[i].Position.y - at.y;
                    if (drop > 0.3f)
                    {
                        grounded++;
                        drops.Append(drops.Length > 0 ? ", " : "").Append("family ").Append(kv.Key).Append(' ')
                             .Append(drop.ToString("0.0", CultureInfo.InvariantCulture)).Append(" m");
                    }
                    if (ClearSleepBlocker(have[match[i]].Go)) unblocked++;
                    Place(kv.Value, have[match[i]], want[i], at);
                    placed++;
                    if (want[i].Asleep) { sleepers.Add(have[match[i]]); sleepAt.Add(at); }
                }
                // Members the capture did not have (killed before it).
                for (int i = 0; i < have.Count; i++)
                {
                    if (used[i]) continue;
                    try { ctrl.StartCoroutine((IEnumerator)_despawnGo.Invoke(ctrl, new object[] { have[i].Go })); extra++; }
                    catch (Exception) { }
                }
            }

            // fixMutantPosition holds the position ~1 s; then back to sleep,
            // on the spot.
            int slept = 0;
            if (sleepers.Count > 0)
            {
                yield return new WaitForSecondsRealtime(1.2f);
                for (int i = 0; i < sleepers.Count; i++)
                    if (PutToSleep(sleepers[i].Go, sleepAt[i])) slept++;
            }

            int noFamily = 0;
            for (int i = 0; i < members.Count; i++) if (members[i].Family >= 1000) noFamily++;

            done("families: " + setupNote + ", " + made + " rebuilt" + (cave > 0 ? ", " + cave + " cave" : "") +
                 (failed > 0 ? ", " + failed + " not found / failed" : "") +
                 " | positions: " + placed + " of " + members.Count + " placed" +
                 (grounded > 0 ? " (" + grounded + " captured in the air, put on the ground: " + drops + ")" : "") +
                 ", " + unblocked + " sleep blocker(s) cleared, " + sleepFamilies + " sleeping famil" + (sleepFamilies == 1 ? "y" : "ies") +
                 (sleepers.Count > 0 ? ", " + slept + " of " + sleepers.Count + " put back to sleep" : "") +
                 (missing > 0 ? ", " + missing + " not spawned" : "") +
                 (extra > 0 ? ", " + extra + " extra despawned" : "") +
                 (noFamily > 0 ? ", " + noFamily + " without a family" : ""));
        }

        private MonoBehaviour BuildFamily(MonoBehaviour ctrl, FamilyRecord f)
        {
            GameObject prefab = _spawnGo.GetValue(ctrl) as GameObject;
            if (prefab == null) return null;
            GameObject go = UnityEngine.Object.Instantiate(prefab, f.Position, Quaternion.Euler(0f, f.Yaw, 0f)) as GameObject;
            MonoBehaviour sm = go != null ? go.GetComponent(_spawnType) as MonoBehaviour : null;
            if (sm == null) { if (go != null) UnityEngine.Object.Destroy(go); return null; }

            for (int i = 0; i < f.Settings.Count; i++)
            {
                FieldInfo field = _spawnType.GetField(f.Settings[i].Key, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null || _settings.IndexOf(field) < 0) continue;
                try
                {
                    object v = System.Convert.ChangeType(f.Settings[i].Value, field.FieldType, CultureInfo.InvariantCulture);
                    field.SetValue(sm, v);
                }
                catch (Exception) { }
            }

            // The kind lists and their counters, as setup<Kind>Spawn does.
            string[] lists = f.List.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lists.Length; i++)
            {
                FieldInfo lf = _ctrlType.GetField(lists[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                IList list = lf != null ? lf.GetValue(ctrl) as IList : null;
                if (list != null && !list.Contains(go)) list.Add(go);
                Bump(ctrl, CounterFor(lists[i]));
            }
            Bump(ctrl, "numActiveSpawns");

            // invokeSpawn's first checkSpawn spawns a surface family at once
            // (the 130 m rule is for cave families) - starting doSpawn too
            // spawned every member twice (v0.24.17: "15 extra despawned").
            sm.enabled = true;
            _invokeSpawn.Invoke(sm, null);
            _addToWorldSpawns.Invoke(sm, null);
            return sm;
        }

        /// A cave family's checkSpawn spawns only with a player in the caves
        /// within 130 m; start its members now, and mark it spawned so
        /// checkSpawn never repeats it.
        private void StartSpawn(MonoBehaviour sm)
        {
            if (_alreadySpawned != null) _alreadySpawned.SetValue(sm, true);
            if (!sm.gameObject.activeInHierarchy) return;
            sm.enabled = true;
            sm.StartCoroutine("doSpawn");
        }

        private static string CounterFor(string list)
        {
            for (int i = 0; i < Counters.GetLength(0); i++)
                if (Counters[i, 0] == list) return Counters[i, 1];
            return null;
        }

        private void Bump(MonoBehaviour ctrl, string counter)
        {
            if (counter == null) return;
            FieldInfo f = _ctrlType.GetField(counter, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f == null || f.FieldType != typeof(int)) return;
            f.SetValue(ctrl, (int)f.GetValue(ctrl) + 1);
        }

        private MonoBehaviour FindCaveSpawner(MonoBehaviour ctrl, Vector3 pos)
        {
            FieldInfo lf = _ctrlType.GetField("allCaveSpawns", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            IList list = lf != null ? lf.GetValue(ctrl) as IList : null;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                GameObject go = list[i] as GameObject;
                if (go == null || (go.transform.position - pos).sqrMagnitude > 4f) continue;
                return go.GetComponent(_spawnType) as MonoBehaviour;
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Sleep (PlayMaker FSMs on the _BASE child, by reflection: PlayMaker
        // is its own assembly and nothing here references it).

        private Component Fsm(GameObject go, string name)
        {
            if (go == null) return null;
            Component[] all = go.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Component c = all[i];
                if (c == null || c.GetType().Name != "PlayMakerFSM") continue;
                PropertyInfo p = c.GetType().GetProperty("FsmName");
                if (p != null && (p.GetValue(c, null) as string) == name) return c;
            }
            return null;
        }

        private bool IsAsleep(GameObject go)
        {
            try
            {
                Component fsm = Fsm(go, "action_sleepingFSM");
                PropertyInfo state = fsm != null ? fsm.GetType().GetProperty("ActiveStateName") : null;
                return state != null && (state.GetValue(fsm, null) as string) == "sleeping";
            }
            catch (Exception) { return false; }
        }

        /// mutantDayCycle.sleepBlocker is only ever set (by the search
        /// routines), never cleared: a pooled cannibal that once searched
        /// wakes at its next initWakeUp. A load starts a fresh pool.
        private bool ClearSleepBlocker(GameObject go)
        {
            if (go == null || _dayCycle == null || _sleepBlocker == null) return false;
            try
            {
                Component dc = go.GetComponentInChildren(_dayCycle);
                if (dc == null) return false;
                bool was = (bool)_sleepBlocker.GetValue(dc);
                _sleepBlocker.SetValue(dc, false);
                return was;
            }
            catch (Exception) { return false; }
        }

        private bool PutToSleep(GameObject go, Vector3 at)
        {
            if (go == null || !go.activeInHierarchy || _switchToSleep == null) return false;
            try
            {
                Component fsm = Fsm(go, "action_sleepingFSM");
                if (fsm != null)
                {
                    object vars = fsm.GetType().GetProperty("FsmVariables").GetValue(fsm, null);
                    Array vectors = vars != null ? vars.GetType().GetProperty("Vector3Variables").GetValue(vars, null) as Array : null;
                    for (int i = 0; vectors != null && i < vectors.Length; i++)
                    {
                        object v = vectors.GetValue(i);
                        if (v == null || (v.GetType().GetProperty("Name").GetValue(v, null) as string) != "sleepPos") continue;
                        v.GetType().GetProperty("Value").SetValue(v, at, null);
                    }
                }
                Component follower = go.GetComponent(_follower);
                if (follower == null) return false;
                _switchToSleep.Invoke(follower, null);
                return true;
            }
            catch (Exception) { return false; }
        }

        private void Place(MonoBehaviour host, Cannibal c, EnemyRecord r, Vector3 at)
        {
            Transform t = c.Go.transform;
            t.rotation = Quaternion.Euler(0f, r.Yaw, 0f);
            if (host != null && host.isActiveAndEnabled)
            {
                IEnumerator routine = _fixPosition.Invoke(host, new object[] { t, at }) as IEnumerator;
                if (routine != null) host.StartCoroutine(routine);
            }
            else t.position = at;
            WriteHealth(c.Setup, r.Health);
        }

        /// The ground under a captured position. The game's own spawn can
        /// stack a cannibal on another's head (a female 4.4 m up, seen live,
        /// v0.24.19); placed there, her sleepPos is in the air, she runs
        /// instead of sleeping and wakes her family. Other cannibals, the
        /// player and triggers are not ground. Only ever moves down.
        private static Vector3 Ground(Vector3 p)
        {
            try
            {
                RaycastHit[] hits = Physics.RaycastAll(p + Vector3.up * 0.5f, Vector3.down, 30f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue;
                Vector3 found = p;
                for (int i = 0; i < hits.Length; i++)
                {
                    Collider col = hits[i].collider;
                    if (col == null || hits[i].distance >= best) continue;
                    Transform root = col.transform.root;
                    if (root.name.StartsWith("mutant_", StringComparison.Ordinal) || root.CompareTag("Player")) continue;
                    best = hits[i].distance;
                    found = hits[i].point;
                }
                return found.y < p.y ? found : p;
            }
            catch (Exception) { return p; }
        }

        // ------------------------------------------------------------------
        // v0.24.16 files: members only, typed by enemyType - move the
        // game's own respawned cannibals of that type.

        public string RestoreByType(List<string> entries)
        {
            if (entries == null) return "";
            if (!Bind() || _enemyTypeValue == null) return "positions: not bound (" + Status + ")";
            try
            {
                List<EnemyRecord> captured = new List<EnemyRecord>();
                for (int i = 0; i < entries.Count; i++)
                {
                    EnemyRecord r;
                    if (EnemyRecord.TryDecode(entries[i], out r)) captured.Add(r);
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
                    Component type = live[i].Go.GetComponent(_enemyType);
                    object t = null;
                    try { if (type != null) t = _enemyTypeValue.GetValue(type, null); } catch (Exception) { }
                    EnemyRecord.Live k;
                    k.Family = family;
                    k.Type = t != null ? t.ToString() : "?";
                    keys.Add(k);
                }

                int whole;
                int[] match = EnemyRecord.Match(captured, keys, out whole);
                int placed = 0;
                for (int i = 0; i < captured.Count; i++)
                {
                    if (match[i] < 0) continue;
                    Place(live[match[i]].Spawner, live[match[i]], captured[i], Ground(captured[i].Position));
                    placed++;
                }
                return "positions (by type, a v0.24.16 file): " + placed + " of " + captured.Count + " placed";
            }
            catch (Exception ex) { return "positions: failed (" + (ex.InnerException ?? ex).Message + ")"; }
        }
    }
}
