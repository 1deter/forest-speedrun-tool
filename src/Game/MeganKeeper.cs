using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Puts seated Megan back after a Quick load (runner maks: a Quick load
    // taken during her transformation did nothing - the transformed boss
    // stayed, no cutscene).
    //
    // WHY (bridge + IL, v0.24.35): single player's boss Megan is spawned by
    // setupGirlMutant (on girlTransformPrefab1, scene endgame_animPrefabs):
    // within 350 m it instantiates `realPrefab` (girlMutant) at its
    // `placedPrefab` placeholder, destroys the placeholder, hands the new
    // animator to the trigger (activateGirlTransform.girlAnimator) and turns
    // itself off. The trigger's enter sets `pickup` and turns its collider
    // off; its AnimationSequence moves to stage 0; the cutscene transforms
    // the same girlMutant(Clone) into the boss (mutantAI
    // .girlFullyTransformed, a `girlSpawnGo` home). None of that is in the
    // save, so an in-place restore kept the boss (or her body) and a spent
    // trigger. Rebuilt by hand through the bridge: Megan sat down again and
    // the whole cutscene and fight played normally.
    //
    // WHAT: capture writes `megan = seated x y z yaw` (placeholder still
    // there, or Megan not yet fully transformed - mid-cutscene included),
    // else `transformed` / `gone`. After a Quick load of a seated capture,
    // when the live Megan is not seated: stop a running transformation
    // (the game's own unlockPlayerParams), remove what the fight left
    // within 80 m of her seat (the boss, her ragdoll and pickup,
    // girlSpawnGo, boss babies and their spawners - all scene roots; the
    // overworld Megan is never that close), reset the trigger and the
    // sequence, and re-arm setupGirlMutant with a new placeholder. BossHold
    // holds the trigger until the new Megan exists; the cutscene
    // fast-forward then works as after a Full load.
    // ------------------------------------------------------------------
    public sealed class MeganKeeper
    {
        public const string Seated = "seated";
        public const string Transformed = "transformed";
        public const string Gone = "gone";

        /// GameEvents' name for her transformation cutscene.
        public const string TransformEvent = "megan-transform";

        // A bare `seated` (files before v0.24.35, inferred from their
        // cutscene line): the seat is girlTransformPrefab1's, with the
        // spawned Megan's height above it (measured live: 0.09 m).
        private const float SeatAbovePrefab = 0.09f;

        private const float Reach = 80f;

        // Scene roots the transformation and the fight leave (confirmed live).
        private static readonly string[] Leftovers =
        {
            "girlMutant(Clone)", "girlMutant_RAGDOLL(Clone)", "girl_Pickup(Clone)",
            "girlSpawnGo", "bossBabySpawner(Clone)", "mutant_baby(Clone)"
        };

        private readonly ManualLogSource _log;
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public MeganKeeper(ManualLogSource log)
        {
            _log = log;
        }

        private static Type T(string name)
        {
            return GameBridge.FindGameType(name);
        }

        private static object Get(object o, string field)
        {
            if (o == null) return null;
            FieldInfo f = o.GetType().GetField(field, Inst);
            return f != null ? f.GetValue(o) : null;
        }

        private static bool Set(object o, string field, object value)
        {
            if (o == null) return false;
            FieldInfo f = o.GetType().GetField(field, Inst);
            if (f == null) return false;
            f.SetValue(o, value);
            return true;
        }

        private static Component FindSetup()
        {
            Type t = T("setupGirlMutant");
            if (t == null) return null;
            return UnityEngine.Object.FindObjectOfType(t) as Component;
        }

        private static string Pose(Transform t)
        {
            Vector3 p = t.position;
            return Seated + " " + F(p.x) + " " + F(p.y) + " " + F(p.z) + " " + F(t.eulerAngles.y);
        }

        private static string F(float v)
        {
            return v.ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// The header value at capture; "" when the boss room is not loaded.
        public string Capture()
        {
            try
            {
                Component setup = FindSetup();
                if (setup == null) return "";
                GameObject placed = Get(setup, "placedPrefab") as GameObject;
                if (placed != null) return Pose(placed.transform);

                Component girl = LiveGirl(setup);
                if (girl == null) return Gone;
                return Transformed_(girl) ? Transformed : Pose(girl.transform.root);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Megan: capture failed: " + ex.Message);
                return "";
            }
        }

        /// The girl's Animator (on girl_base), or null when she is gone.
        private static Component LiveGirl(Component setup)
        {
            object trigger = Get(setup, "activateGirlScript");
            Component anim = Get(trigger, "girlAnimator") as Component;
            if (anim != null) return anim;
            object tracker = GameBridge.ReadStaticField("TheForest.Utils.Scene", "SceneTracker");
            GameObject boss = Get(tracker, "EndgameBoss") as GameObject;
            return boss != null ? boss.transform : null;
        }

        private static bool Transformed_(Component girl)
        {
            Type ai = T("mutantAI");
            Component c = ai != null ? girl.GetComponent(ai) : null;
            object v = Get(c, "girlFullyTransformed");
            return v is bool && (bool)v;
        }

        private static bool TryParse(string saved, out Vector3 pos, out float yaw)
        {
            pos = Vector3.zero;
            yaw = 0f;
            if (string.IsNullOrEmpty(saved)) return false;
            string[] p = saved.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length != 5 || p[0] != Seated) return false;
            float[] v = new float[4];
            for (int i = 0; i < 4; i++)
                if (!float.TryParse(p[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return false;
            pos = new Vector3(v[0], v[1], v[2]);
            yaw = v[3];
            return true;
        }

        /// After a Quick load. `saved` is the file's megan value;
        /// `transformRunning`: Megan's transformation was playing when the
        /// restore began. Returns the log note, "" when there is nothing
        /// to do.
        public string Restore(string saved, bool transformRunning)
        {
            Vector3 seat = Vector3.zero;
            float yaw = 0f;
            bool bare = saved == Seated;
            if (!bare && !TryParse(saved, out seat, out yaw)) return "";
            try
            {
                Component setup = FindSetup();
                if (setup == null) return "megan: the boss room is not loaded - left as it is";
                if (bare)
                {
                    seat = setup.transform.position + Vector3.up * SeatAbovePrefab;
                    yaw = setup.transform.eulerAngles.y;
                }
                object trigger = Get(setup, "activateGirlScript");
                if (trigger == null) return "megan: no trigger on setupGirlMutant - left as it is";

                bool spent = Get(trigger, "pickup") is bool && (bool)Get(trigger, "pickup");
                if ((Get(setup, "placedPrefab") as GameObject) != null && !spent) return "megan: still to spawn, as at capture";
                Component girl = LiveGirl(setup);
                if (girl != null && !spent && !Transformed_(girl)) return "megan: seated, as at capture";

                string stopped = transformRunning ? StopTransformation() : "";

                List<string> removed = new List<string>();
                RemoveLeftovers(seat, removed);

                // The trigger and its sequence as before the enter.
                Set(trigger, "pickup", false);
                Set(trigger, "girlAnimator", null);
                Component tc = trigger as Component;
                Collider col = tc != null ? tc.GetComponent<Collider>() : null;
                if (col != null) col.enabled = true;
                object seq = Get(trigger, "sequence");
                Set(seq, "_stage", -1);
                Set(seq, "_progress", -1);
                Set(seq, "_isActor", false);
                Array stages = Get(seq, "_stages") as Array;
                if (stages != null)
                    for (int i = 0; i < stages.Length; i++) Set(stages.GetValue(i), "_completed", false);

                // setupGirlMutant spawns her at the placeholder on its next
                // Update (it destroys the placeholder itself).
                GameObject placeholder = new GameObject("ForestOverlay girl seat");
                placeholder.transform.position = seat;
                placeholder.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                Set(setup, "placedPrefab", placeholder);
                Set(setup, "updateTimer", 0f);
                ((Behaviour)setup).enabled = true;

                string boss = SetFightingBoss();

                return "megan: seated Megan put back" +
                       (removed.Count > 0 ? " (removed " + string.Join(", ", removed.ToArray()) + ")" : "") +
                       (stopped.Length > 0 ? ", " + stopped : "") +
                       (boss.Length > 0 ? ", " + boss : "");
            }
            catch (Exception ex)
            {
                _log.LogWarning("Megan: restore failed: " + ex);
                return "megan: put back failed (" + ex.Message + ")";
            }
        }

        // The transformation is a coroutine on the player's
        // PlayerGirlTransformAction; stopped mid-way, its own unlock hands
        // control back (a fresh one runs when the trigger is entered again).
        private string StopTransformation()
        {
            GameObject actions = GameBridge.ReadStaticField("TheForest.Utils.LocalPlayer", "SpecialActions") as GameObject;
            Type t = T("TheForest.Player.Actions.PlayerGirlTransformAction");
            MonoBehaviour a = actions != null && t != null ? actions.GetComponent(t) as MonoBehaviour : null;
            if (a == null) return "transformation not found to stop";
            a.StopAllCoroutines();
            MethodInfo unlock = t.GetMethod("unlockPlayerParams", Inst);
            if (unlock != null) unlock.Invoke(a, null);
            return "the running transformation stopped";
        }

        private static string SetFightingBoss()
        {
            object stats = GameBridge.ReadStaticField("TheForest.Utils.LocalPlayer", "Stats");
            if (stats == null) return "";
            PropertyInfo p = stats.GetType().GetProperty("IsFightingBoss", Inst);
            if (p == null || !p.CanWrite) return "";
            object was = p.GetValue(stats, null);
            if (was is bool && !(bool)was) return "";
            p.SetValue(stats, false, null);
            return "boss fight flag cleared";
        }

        private void RemoveLeftovers(Vector3 seat, List<string> removed)
        {
            object tracker = GameBridge.ReadStaticField("TheForest.Utils.Scene", "SceneTracker");
            IList visible = Get(tracker, "visibleEnemies") as IList;
            Dictionary<string, int> counts = new Dictionary<string, int>();

            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                UnityEngine.SceneManagement.Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    GameObject go = roots[i];
                    if (go == null) continue;
                    string kind = Leftover(go.name);
                    if (kind == null) continue;
                    if (Vector3.Distance(go.transform.position, seat) > Reach) continue;
                    if (visible != null && visible.Contains(go)) visible.Remove(go);
                    go.SetActive(false);
                    UnityEngine.Object.Destroy(go);
                    int n;
                    counts.TryGetValue(kind, out n);
                    counts[kind] = n + 1;
                }
            }
            foreach (KeyValuePair<string, int> kv in counts)
                removed.Add(kv.Value > 1 ? kv.Key + " x" + kv.Value : kv.Key);
        }

        // Babies carry a number after the name (mutant_baby(Clone)0010).
        private static string Leftover(string name)
        {
            for (int i = 0; i < Leftovers.Length; i++)
                if (name.StartsWith(Leftovers[i], StringComparison.Ordinal)) return Leftovers[i];
            return null;
        }
    }
}
