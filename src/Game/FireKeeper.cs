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
    // Fire across a Quick load (T-0419; game-notes *Fire, and what a Quick
    // load leaves of it*; tasks/notes/T-0419.md).
    //
    // WHY (bridge + IL, 2026-10-18, v0.24.357): nothing in a Quick load
    // knows about fire. A tree lit since the capture burned on; a bush lit
    // by Burn lost its scene object at once (its LOD, left without a view,
    // destroys itself as after a cut) and never came back; a built fire and
    // an effigy got `Lit` / `lightBool` false from the save but kept their
    // flames (the effigy its pending `die`, which destroys it); a cave fire
    // (not in the save) stayed lit; a kept cave cannibal and a kept rabbit
    // burned on, losing health.
    //
    // WHAT (decisions.md *Mirror essentially everything*): a Quick load puts
    // out every fire lit after the capture and leaves what burned at it.
    // - Hooks: FireDamage.Ignite numbers each new fire on a tree / bush view
    //   with NatureKeeper's sequence, so the file's `bushes` mark tells
    //   before from after; FireDamage.Burn (bushes; trees override it) and
    //   the spread's Ignite keep a copy of a bush's LOD as a cut does
    //   (NatureKeeper.KeepLod), which the Quick load puts back. Fire2.On,
    //   enableEffigy.lightEffigyReal, EnemyHealth.Burn / singeBurn and
    //   animalHealth.Burn list what they lit.
    // - Capture: the `fires` line, the listed Fire2s lit now (Data/LitFires).
    // - Before LoadNow: each listed effigy's `die` / `ShutdownFire` invokes
    //   cancelled; the load invokes them again, once, for the ones lit at
    //   the capture.
    // - After LoadNow: views lit after the mark put out the game's way
    //   (OnDisable: StopBurningEvent + StopBurning), an orphan bush view
    //   despawned to its pool; a Fire2 not in `fires` showing flames put
    //   out (Off, the never-lit look), one in `fires` gone out lit again;
    //   an effigy not lit with its fire on put out; cannibals and animals
    //   on fire put out (known gap T-0424: also those burning at the
    //   capture - their timers are not recorded).
    // A file from another world puts out every tree / bush fire, as a load
    // would. Nothing here runs on a Full load (the scene is fresh).
    // ------------------------------------------------------------------
    public sealed class FireKeeper
    {
        private sealed class LitView
        {
            public Component View;
            public int Seq;
        }

        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const int PruneAt = 256;

        // Instance id -> the thing lit.
        private static readonly Dictionary<int, LitView> Views = new Dictionary<int, LitView>();
        private static readonly Dictionary<int, Component> Fires = new Dictionary<int, Component>();
        private static readonly Dictionary<int, Component> Effigies = new Dictionary<int, Component>();
        private static readonly Dictionary<int, Component> Enemies = new Dictionary<int, Component>();
        private static readonly Dictionary<int, Component> Animals = new Dictionary<int, Component>();

        private static ManualLogSource _log;
        private Harmony _harmony;

        // FireDamage (bush / tree views) and FireDamageTree.
        private static Type _fdType, _fdTree;
        private static FieldInfo _fdLodBase, _fdMyBurnt, _fdSmoke;
        private static PropertyInfo _fdBurning, _fdFuel;
        // Fire2 (built fires, cave fires).
        private static PropertyInfo _f2Lit;
        private static MethodInfo _f2Off, _f2Light;
        private static FieldInfo _f2Flames, _f2ModelLit, _f2ModelUnlit, _f2FuelStart;
        // enableEffigy.
        private static FieldInfo _efLight, _efFires, _efRange, _efBirds;
        // EnemyHealth, animalHealth.
        private static FieldInfo _ehOnFire, _ahFire;
        private static MethodInfo _ehDisable, _ahCancel;
        // PathologicalGames.SpawnPool.
        private static Type _poolType;
        private static MethodInfo _poolIsSpawned, _poolDespawn;

        public string Status { get; private set; }

        public FireKeeper(ManualLogSource log)
        {
            _log = log;
            Status = "not installed";
        }

        public void Install(string harmonyId)
        {
            _fdType = GameBridge.FindGameType("FireDamage");
            _fdTree = GameBridge.FindGameType("FireDamageTree");
            Type fire2 = GameBridge.FindGameType("Fire2");
            Type effigy = GameBridge.FindGameType("enableEffigy");
            Type enemy = GameBridge.FindGameType("EnemyHealth");
            Type animal = GameBridge.FindGameType("animalHealth");
            _poolType = GameBridge.FindGameType("PathologicalGames.SpawnPool");

            if (_fdType != null)
            {
                // Declared on FireDamage (private): a subclass's GetField misses it.
                _fdLodBase = _fdType.GetField("LodBase", Inst);
                _fdMyBurnt = _fdType.GetField("MyBurnt", Inst);
                _fdSmoke = _fdType.GetField("SmokeShown", Inst);
                _fdBurning = _fdType.GetProperty("isBurning", Inst | BindingFlags.DeclaredOnly);
                _fdFuel = _fdType.GetProperty("usedFuelSeconds", Inst | BindingFlags.DeclaredOnly);
            }
            if (fire2 != null)
            {
                _f2Lit = fire2.GetProperty("Lit", Inst);
                _f2Off = fire2.GetMethod("Off", Inst, null, Type.EmptyTypes, null);
                _f2Light = fire2.GetMethod("Action_LightFire", Inst, null, Type.EmptyTypes, null);
                _f2Flames = fire2.GetField("FlamesAll", Inst);
                _f2ModelLit = fire2.GetField("ModelLit", Inst);
                _f2ModelUnlit = fire2.GetField("ModelUnlit", Inst);
                _f2FuelStart = fire2.GetField("FuelStart", Inst);
            }
            if (effigy != null)
            {
                _efLight = effigy.GetField("lightBool", Inst);
                _efFires = effigy.GetField("fires", Inst);
                _efRange = effigy.GetField("effigyRange", Inst);
                _efBirds = effigy.GetField("BirdMarkers", Inst);
            }
            if (enemy != null)
            {
                _ehOnFire = enemy.GetField("onFire", Inst);
                _ehDisable = enemy.GetMethod("disableBurn", Inst, null, Type.EmptyTypes, null);
            }
            if (animal != null)
            {
                _ahFire = animal.GetField("Fire", Inst);
                _ahCancel = animal.GetMethod("cancelFire", Inst, null, Type.EmptyTypes, null);
            }
            if (_poolType != null)
            {
                _poolIsSpawned = _poolType.GetMethod("IsSpawned", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(Transform) }, null);
                _poolDespawn = _poolType.GetMethod("Despawn", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(Transform) }, null);
            }

            int hooks = 0;
            try
            {
                _harmony = new Harmony(harmonyId + ".fire");
                bool views = _fdLodBase != null && _fdBurning != null;
                if (views)
                {
                    hooks += Patch(_fdType, "Burn", Type.EmptyTypes, "BurnPrefix", true);
                    hooks += Patch(_fdType, "Ignite", new[] { typeof(Vector3), typeof(int) }, "IgnitePrefix", true);
                }
                if (_f2Lit != null && _f2Off != null) hooks += Patch(fire2, "On", Type.EmptyTypes, "FireOnPostfix", false);
                if (_efLight != null && _efFires != null) hooks += Patch(effigy, "lightEffigyReal", Type.EmptyTypes, "EffigyPostfix", false);
                if (_ehOnFire != null && _ehDisable != null)
                {
                    hooks += Patch(enemy, "Burn", Type.EmptyTypes, "EnemyPostfix", false);
                    hooks += Patch(enemy, "singeBurn", Type.EmptyTypes, "EnemyPostfix", false);
                }
                if (_ahFire != null && _ahCancel != null) hooks += Patch(animal, "Burn", Type.EmptyTypes, "AnimalPostfix", false);
                Status = "hooked (" + hooks + "/7)";
            }
            catch (Exception ex)
            {
                Status = "Harmony: " + ex.Message;
            }
            _log.LogInfo("FireKeeper: " + Status + (_poolDespawn == null ? ", no SpawnPool.Despawn" : "") + ".");
        }

        private int Patch(Type t, string method, Type[] args, string hook, bool prefix)
        {
            MethodInfo m = t != null ? t.GetMethod(method, Inst | BindingFlags.DeclaredOnly, null, args, null) : null;
            if (m == null) return 0;
            HarmonyMethod h = new HarmonyMethod(typeof(FireKeeper).GetMethod(hook, BindingFlags.Static | BindingFlags.NonPublic));
            if (prefix) _harmony.Patch(m, h, null);
            else _harmony.Patch(m, null, h);
            return 1;
        }

        public void Uninstall()
        {
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception) { }
            Views.Clear();
            Fires.Clear();
            Effigies.Clear();
            Enemies.Clear();
            Animals.Clear();
        }

        // ---------------------------------------------------------- hooks
        // Never throw into the game; never skip the original.

        // FireDamage.Burn (a bush's; trees override it): the LOD loses its
        // view here, so its copy is kept before the body runs.
        private static void BurnPrefix(object __instance)
        {
            try
            {
                Component lod = _fdLodBase.GetValue(__instance) as Component;
                if (lod != null) NatureKeeper.KeepLod(lod, _fdMyBurnt != null ? _fdMyBurnt.GetValue(__instance) as UnityEngine.Object : null);
            }
            catch (Exception) { }
        }

        // FireDamage.Ignite (Burn's, and the spread's): a new fire, numbered.
        private static void IgnitePrefix(object __instance)
        {
            try
            {
                Component view = __instance as Component;
                if (view == null || IsBurning(view)) return;   // Ignite returns early then
                LitView e = new LitView();
                e.View = view;
                e.Seq = NatureKeeper.NextSeq();
                Views[view.GetInstanceID()] = e;
                // The spread lights a bush with its LOD still on: when it
                // burns out, the view is destroyed and the LOD with it.
                if (!IsTree(view))
                {
                    Component lod = _fdLodBase.GetValue(view) as Component;
                    if (lod != null) NatureKeeper.KeepLod(lod, _fdMyBurnt != null ? _fdMyBurnt.GetValue(view) as UnityEngine.Object : null);
                }
                if (Views.Count > PruneAt) PruneViews();
            }
            catch (Exception) { }
        }

        private static void FireOnPostfix(object __instance) { Note(Fires, __instance); }
        private static void EffigyPostfix(object __instance) { Note(Effigies, __instance); }
        private static void AnimalPostfix(object __instance) { Note(Animals, __instance); }

        private static void EnemyPostfix(object __instance)
        {
            try { if ((bool)_ehOnFire.GetValue(__instance)) Note(Enemies, __instance); }
            catch (Exception) { }
        }

        private static void Note(Dictionary<int, Component> into, object o)
        {
            Component c = o as Component;
            if (c == null) return;
            into[c.GetInstanceID()] = c;
            if (into.Count > PruneAt) PruneDead(into);
        }

        // ------------------------------------------------------ capture
        /// The file's `fires` line: the listed Fire2s lit now ("" for none).
        public string Capture()
        {
            PruneDead(Fires);
            if (_f2Lit == null) return "";
            List<string> keys = new List<string>();
            foreach (KeyValuePair<int, Component> kv in Fires)
            {
                try { if ((bool)_f2Lit.GetValue(kv.Value, null)) keys.Add(NatureKeeper.KeyOf(kv.Value.gameObject)); }
                catch (Exception) { }
            }
            return LitFires.Format(keys);
        }

        // ------------------------------------------------- before LoadNow
        /// Each listed effigy's pending `die` / `ShutdownFire` cancelled:
        /// LoadNow's OnDeserialized invokes both again (from the effigy's
        /// own timer) for the ones lit at the capture, and only once.
        public static void BeforeLoad()
        {
            try
            {
                PruneDead(Effigies);
                foreach (KeyValuePair<int, Component> kv in Effigies)
                {
                    MonoBehaviour mb = kv.Value as MonoBehaviour;
                    if (mb == null) continue;
                    mb.CancelInvoke("die");
                    mb.CancelInvoke("ShutdownFire");
                }
            }
            catch (Exception ex) { _log.LogWarning("FireKeeper: cancelling effigy timers failed: " + ex.Message); }
        }

        // -------------------------------------------------- after LoadNow
        /// After a Quick load, before NatureKeeper puts bushes back. `mark`
        /// is the file's `bushes` line, `fires` its `fires` line (null for
        /// an older file or a slot). Returns the log note ("" when nothing
        /// changed).
        public string Restore(string mark, string fires)
        {
            int[] counts = new int[LitFires.Kinds];
            int orphans = 0, relit = 0;
            List<string> failed = new List<string>();
            try { PutOutViews(LitFires.SinceOf(mark, NatureKeeper.WorldKey()), counts, ref orphans); }
            catch (Exception ex) { failed.Add("trees / bushes (" + Inner(ex) + ")"); }
            try { PutOutFires(fires != null ? LitFires.Parse(fires) : null, counts, ref relit); }
            catch (Exception ex) { failed.Add("fires (" + Inner(ex) + ")"); }
            try { PutOutEffigies(counts); }
            catch (Exception ex) { failed.Add("effigies (" + Inner(ex) + ")"); }
            try { PutOutEnemies(counts); }
            catch (Exception ex) { failed.Add("cannibals (" + Inner(ex) + ")"); }
            try { PutOutAnimals(counts); }
            catch (Exception ex) { failed.Add("animals (" + Inner(ex) + ")"); }
            string note = LitFires.Describe(counts, orphans, relit);
            if (failed.Count > 0)
                note += (note.Length > 0 ? ", " : "fire: ") + "putting out failed for " + string.Join(", ", failed.ToArray());
            return note;
        }

        /// Drops what a new world destroyed.
        public static void Prune()
        {
            PruneViews();
            PruneDead(Fires);
            PruneDead(Effigies);
            PruneDead(Enemies);
            PruneDead(Animals);
        }

        private static void PutOutViews(int since, int[] counts, ref int orphans)
        {
            if (_fdBurning == null) return;
            List<int> done = new List<int>();
            foreach (KeyValuePair<int, LitView> kv in Views)
            {
                Component view = kv.Value.View;
                if (view == null || !IsBurning(view)) { done.Add(kv.Key); continue; }
                if (!LitFires.PutOut(kv.Value.Seq, since)) continue;   // burning at the capture
                done.Add(kv.Key);
                bool tree = IsTree(view);
                ResetFuel(view);
                Component pool = tree ? null : OrphanPool(view);
                if (pool != null)
                {
                    // Its LOD destroyed itself at the light; NatureKeeper puts
                    // the scene object back, which spawns a view of its own.
                    // OnDisable puts the fire out.
                    _poolDespawn.Invoke(pool, new object[] { view.transform });
                    orphans++;
                }
                else
                {
                    // OnDisable: StopBurningEvent + StopBurning (particles
                    // despawned, sound stopped, material back).
                    Behaviour b = view as Behaviour;
                    if (b != null) { b.enabled = false; b.enabled = true; }
                }
                counts[tree ? LitFires.Tree : LitFires.Bush]++;
            }
            for (int i = 0; i < done.Count; i++) Views.Remove(done[i]);
        }

        private static void ResetFuel(Component view)
        {
            if (_fdFuel != null) _fdFuel.SetValue(view, 0f, null);
            if (_fdSmoke != null) _fdSmoke.SetValue(view, false);
        }

        // A bush view whose LOD is gone (Burn nulls it) and that a pool
        // spawned: the pool, else null.
        private static Component OrphanPool(Component view)
        {
            if (_poolType == null || _poolIsSpawned == null || _poolDespawn == null) return null;
            if (_fdLodBase.GetValue(view) as Component != null) return null;
            Transform parent = view.transform.parent;
            Component pool = parent != null ? parent.GetComponent(_poolType) : null;
            if (pool == null) return null;
            return (bool)_poolIsSpawned.Invoke(pool, new object[] { view.transform }) ? pool : null;
        }

        private static void PutOutFires(HashSet<string> atCapture, int[] counts, ref int relit)
        {
            if (_f2Lit == null || _f2Off == null) return;
            PruneDead(Fires);
            // A copy: lighting one again lists it (FireOnPostfix).
            List<Component> list = new List<Component>(Fires.Values);
            for (int f = 0; f < list.Count; f++)
            {
                Component fire = list[f];
                bool lit = (bool)_f2Lit.GetValue(fire, null);
                GameObject flames = _f2Flames != null ? _f2Flames.GetValue(fire) as GameObject : null;
                bool flaming = flames != null && flames.activeSelf;
                if (atCapture != null)
                {
                    if (atCapture.Contains(NatureKeeper.KeyOf(fire.gameObject)))
                    {
                        // Lit at the capture and gone out since (a cave fire:
                        // not in the save).
                        if (!lit && _f2Light != null) { _f2Light.Invoke(fire, null); relit++; }
                        continue;
                    }
                    if (!lit && !flaming) continue;
                }
                // An older file / a slot: only what the save itself says is
                // out (`_lit` false) but still shows flames.
                else if (lit || !flaming) continue;

                _f2Off.Invoke(fire, null);
                MonoBehaviour mb = fire as MonoBehaviour;
                if (mb != null) mb.CancelInvoke("Action_EnableFireDamage");
                NeverLitLook(fire);
                counts[LitFires.Fire]++;
            }
        }

        // Off() leaves the lit model with the burnt material. A fire that
        // starts with no fuel looks like that from the start (Fire2.Awake);
        // any other is back to its unlit model.
        private static void NeverLitLook(Component fire)
        {
            float fuelStart = 1f;
            if (_f2FuelStart != null)
            {
                try { fuelStart = (float)_f2FuelStart.GetValue(fire); } catch (Exception) { }
            }
            if (fuelStart == 0f) return;
            GameObject litModel = _f2ModelLit != null ? _f2ModelLit.GetValue(fire) as GameObject : null;
            GameObject unlitModel = _f2ModelUnlit != null ? _f2ModelUnlit.GetValue(fire) as GameObject : null;
            if (litModel != null) litModel.SetActive(false);
            if (unlitModel != null) unlitModel.SetActive(true);
        }

        // The reverse of lightEffigyReal for each listed effigy the save
        // says is not lit but whose fire is on.
        private static void PutOutEffigies(int[] counts)
        {
            if (_efLight == null || _efFires == null) return;
            PruneDead(Effigies);
            foreach (KeyValuePair<int, Component> kv in Effigies)
            {
                Component effigy = kv.Value;
                if ((bool)_efLight.GetValue(effigy)) continue;
                GameObject[] fires = _efFires.GetValue(effigy) as GameObject[];
                bool on = false;
                if (fires != null) for (int i = 0; i < fires.Length; i++) if (fires[i] != null && fires[i].activeSelf) on = true;
                if (!on) continue;
                for (int i = 0; i < fires.Length; i++) if (fires[i] != null) fires[i].SetActive(false);
                GameObject range = _efRange != null ? _efRange.GetValue(effigy) as GameObject : null;
                if (range != null) range.SetActive(false);
                GameObject birds = _efBirds != null ? _efBirds.GetValue(effigy) as GameObject : null;
                if (birds != null) birds.SetActive(true);
                MonoBehaviour mb = effigy as MonoBehaviour;
                if (mb != null) { mb.CancelInvoke("die"); mb.CancelInvoke("ShutdownFire"); }
                counts[LitFires.Effigy]++;
            }
        }

        // The game's own end of a cannibal's burn; its pending one cancelled.
        private static void PutOutEnemies(int[] counts)
        {
            if (_ehOnFire == null || _ehDisable == null) return;
            List<int> done = new List<int>();
            foreach (KeyValuePair<int, Component> kv in Enemies)
            {
                Component eh = kv.Value;
                done.Add(kv.Key);
                if (eh == null || !eh.gameObject.activeInHierarchy || !(bool)_ehOnFire.GetValue(eh)) continue;
                _ehDisable.Invoke(eh, null);
                MonoBehaviour mb = eh as MonoBehaviour;
                if (mb != null) mb.CancelInvoke("disableBurn");
                counts[LitFires.Cannibal]++;
            }
            for (int i = 0; i < done.Count; i++) Enemies.Remove(done[i]);
        }

        // The game's own end of an animal's burn; its pending one cancelled.
        private static void PutOutAnimals(int[] counts)
        {
            if (_ahFire == null || _ahCancel == null) return;
            List<int> done = new List<int>();
            foreach (KeyValuePair<int, Component> kv in Animals)
            {
                Component ah = kv.Value;
                done.Add(kv.Key);
                if (ah == null) continue;
                MonoBehaviour mb = ah as MonoBehaviour;
                GameObject fire = _ahFire.GetValue(ah) as GameObject;
                bool burning = (fire != null && fire.activeSelf) || (mb != null && mb.IsInvoking("HitFire"));
                if (!burning) continue;
                _ahCancel.Invoke(ah, null);
                if (mb != null) mb.CancelInvoke("cancelFire");
                counts[LitFires.Animal]++;
            }
            for (int i = 0; i < done.Count; i++) Animals.Remove(done[i]);
        }

        // ------------------------------------------------------- helpers
        private static bool IsBurning(Component view)
        {
            return (bool)_fdBurning.GetValue(view, null);
        }

        private static bool IsTree(Component view)
        {
            return _fdTree != null && _fdTree.IsInstanceOfType(view);
        }

        // Views gone or no longer burning (one lit again is numbered anew).
        private static void PruneViews()
        {
            List<int> dead = null;
            foreach (KeyValuePair<int, LitView> kv in Views)
            {
                bool drop;
                try { drop = kv.Value.View == null || !IsBurning(kv.Value.View); }
                catch (Exception) { drop = true; }
                if (drop) (dead ?? (dead = new List<int>())).Add(kv.Key);
            }
            if (dead != null) for (int i = 0; i < dead.Count; i++) Views.Remove(dead[i]);
        }

        private static void PruneDead(Dictionary<int, Component> d)
        {
            List<int> dead = null;
            foreach (KeyValuePair<int, Component> kv in d)
                if (kv.Value == null) (dead ?? (dead = new List<int>())).Add(kv.Key);
            if (dead != null) for (int i = 0; i < dead.Count; i++) d.Remove(dead[i]);
        }

        private static string Inner(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            return ex.Message;
        }
    }
}
