using System;
using System.Reflection;
using BepInEx.Logging;
using ForestOverlay.Data;
using HarmonyLib;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Feeds Data/MoveDetector once per physics step (banned-move detection:
    // lifts out of colliders and clips; docs/run-mode.md *Banned moves:
    // detection*). Read-only: one OverlapSphere and at most two raycasts a
    // step, nothing allocated (NonAlloc buffers).
    //
    // A FixedUpdate on the plugin's own object (never a component on the
    // player - the save would see it) reads the body capsule's centre and
    // the rigidbody's velocity after the last physics step.
    //
    // Clips: the last place the centre was clear of every solid the player
    // had touched is kept; when it is clear again, the line from there to
    // here is cast and a touched solid entered through a front face is a
    // clip (a mesh's back face does not answer a ray: one face crossed from
    // behind - the yacht cabin's tight furniture, live v0.24.232 - does not
    // count; ending up inside a rock does, coming out of one does not).
    //
    // Clips count only after an axe ground smash: playerAnimatorControl
    // .doingGroundChop (set in its OnAnimatorMove while the full-body layer
    // plays axeGround2 / axeAttack - the head collider following the head
    // bone, game-notes *The axe ground smash*), with FirstPersonCharacter
    // .crouching for "stood up from a crouch". Both read every step through
    // DynamicMethod field getters (no boxing).
    //
    // Lifts name a player-built structure in contact (BuildingHealth /
    // BuildingHealthChunk on it or a parent - a log wall, a custom wall, a
    // hut): the runners' lifts come from one, ordinary geometry lifts the
    // player too. "Touched" comes from the game's
    // own collision proxies on the player (Harmony postfixes on
    // OnCollisionEnterProxy / OnCollisionExitProxy): in contact now, or
    // left less than TouchKeep seconds ago. Pairs the game tells the physics
    // to ignore (Physics.IgnoreCollision: terrain in caves and at cave
    // mouths, ropes, ziplines, structures on rafts - Unity 5.6 cannot read
    // those back) never make a contact, so they never count. Terrain is left
    // out entirely: the game switches its collision per cave entrance, and a
    // contact from walking on it would outlive the switch (passing under it
    // is the cave force load's own detector). Triggers, moving bodies
    // (non-kinematic rigidbodies) and the player's own colliders never count,
    // nor a solid that moved in the last MoverKeep seconds: each touched
    // solid's pose is compared every step (live, v0.24.231: the yacht's hull
    // bobs on a kinematic body and read as a clip and a lift while the player
    // walked on it). A door that has closed counts again once it is still.
    // A step while touching a mover is "carried" for the lift check.
    // ------------------------------------------------------------------
    public sealed class ClipWatch : MonoBehaviour
    {
        public const float TouchKeep = 1f;   // game seconds a left contact still counts
        public const float MoverKeep = 1f;   // game seconds a solid that moved stays a mover
        private const float InsideRadius = 0.05f;

        public static string Status = "not installed";

        /// Physics steps watched / contacts seen since startup ("did it see anything").
        public static int Steps, Contacts;

        private static ManualLogSource _log;
        private static ClipWatch _instance;
        private static FieldInfo _lpTransform;   // static LocalPlayer.Transform

        // Contacts: a small table, slot reused when full (oldest exit first).
        private const int Slots = 32;
        private static readonly Collider[] _touched = new Collider[Slots];
        private static readonly float[] _left = new float[Slots];   // < 0 = in contact now
        private static readonly Vector3[] _pos = new Vector3[Slots];
        private static readonly Quaternion[] _rot = new Quaternion[Slots];
        private static readonly float[] _moved = new float[Slots];  // game time it last moved
        private static string _contactName = "";
        private static readonly string[] _structure = new string[Slots];   // the built structure's name, "" not one
        private static Type _health, _healthChunk;
        private static FieldInfo _animControl, _fpCharacter;   // static LocalPlayer.AnimControl / FpCharacter
        private static Func<object, bool> _groundChop, _crouching;

        private readonly Collider[] _overlap = new Collider[16];
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        private Transform _player;
        private Rigidbody _rb;
        private CapsuleCollider _capsule;
        private int _maskLayer = -1, _mask;
        private bool _hasClear;
        private Vector3 _clear;
        private int _insideSteps;

        public static void Install(GameObject host, Harmony harmony, ManualLogSource log)
        {
            _log = log;
            try
            {
                Type lp = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
                if (lp != null) _lpTransform = lp.GetField("Transform", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                Type enter = GameBridge.FindGameType("TheForest.Utils.Physics.OnCollisionEnterProxy");
                Type exit = GameBridge.FindGameType("TheForest.Utils.Physics.OnCollisionExitProxy");
                BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                MethodInfo onEnter = enter != null ? enter.GetMethod("OnCollisionEnter", f) : null;
                MethodInfo onExit = exit != null ? exit.GetMethod("OnCollisionExit", f) : null;
                BindingFlags s = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                _animControl = lp != null ? lp.GetField("AnimControl", s) : null;
                _fpCharacter = lp != null ? lp.GetField("FpCharacter", s) : null;
                _groundChop = BoolGetter(GameBridge.FindGameType("playerAnimatorControl"), "doingGroundChop");
                _crouching = BoolGetter(GameBridge.FindGameType("FirstPersonCharacter"), "crouching");
                if (_groundChop == null || _animControl == null)
                    _log.LogWarning("ClipWatch: playerAnimatorControl.doingGroundChop not found - clips are never reported.");
                _health = GameBridge.FindGameType("TheForest.Buildings.World.BuildingHealth");
                _healthChunk = GameBridge.FindGameType("TheForest.Buildings.World.BuildingHealthChunk");
                if (_lpTransform == null || onEnter == null || onExit == null || host == null || harmony == null)
                {
                    Status = "the player's collision proxies were not found - clips and lifts are not detected";
                    _log.LogWarning("ClipWatch: " + Status + ".");
                    return;
                }
                harmony.Patch(onEnter, null, new HarmonyMethod(typeof(ClipWatch).GetMethod("EnterPostfix", BindingFlags.Static | BindingFlags.NonPublic)));
                harmony.Patch(onExit, null, new HarmonyMethod(typeof(ClipWatch).GetMethod("ExitPostfix", BindingFlags.Static | BindingFlags.NonPublic)));
                if (_instance == null) _instance = host.AddComponent<ClipWatch>();
                Status = "watching physics steps";
                _log.LogInfo("ClipWatch: " + Status + " (the player's collision proxies + a FixedUpdate).");
            }
            catch (Exception ex)
            {
                Status = "failed: " + ex.Message;
                _log.LogWarning("ClipWatch: " + Status);
            }
        }

        /// `(object o) => ((T)o).field` for a bool instance field, null when missing.
        private static Func<object, bool> BoolGetter(Type t, string field)
        {
            try
            {
                FieldInfo f = t != null ? t.GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null;
                if (f == null || f.FieldType != typeof(bool)) return null;
                System.Reflection.Emit.DynamicMethod dm = new System.Reflection.Emit.DynamicMethod(
                    "Get_" + field, typeof(bool), new[] { typeof(object) }, typeof(ClipWatch).Module, true);
                System.Reflection.Emit.ILGenerator il = dm.GetILGenerator();
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
                il.Emit(System.Reflection.Emit.OpCodes.Castclass, t);
                il.Emit(System.Reflection.Emit.OpCodes.Ldfld, f);
                il.Emit(System.Reflection.Emit.OpCodes.Ret);
                return (Func<object, bool>)dm.CreateDelegate(typeof(Func<object, bool>));
            }
            catch (Exception) { return null; }
        }

        private static bool Read(FieldInfo holder, Func<object, bool> getter)
        {
            if (holder == null || getter == null) return false;
            object o = holder.GetValue(null);
            UnityEngine.Object u = o as UnityEngine.Object;
            return u != null && getter(o);
        }

        private static Transform PlayerTransform()
        {
            return _lpTransform != null ? _lpTransform.GetValue(null) as Transform : null;
        }

        private static void EnterPostfix(Component __instance, Collision col)
        {
            try
            {
                Transform p = PlayerTransform();
                if (p == null || __instance == null || __instance.transform != p || col == null) return;
                Collider c = col.collider;
                if (c == null || c is TerrainCollider) return;
                Contacts++;
                int free = -1, oldest = 0;
                for (int i = 0; i < Slots; i++)
                {
                    if (_touched[i] == c) { free = i; break; }
                    if (free < 0 && (_touched[i] == null || (_left[i] >= 0f && Time.time - _left[i] > TouchKeep))) free = i;
                    if (_left[i] >= 0f && (_left[oldest] < 0f || _left[i] < _left[oldest])) oldest = i;
                }
                if (free < 0) free = oldest;
                if (_touched[free] != c)
                {
                    _pos[free] = c.transform.position;
                    _rot[free] = c.transform.rotation;
                    _moved[free] = -100f;
                    _structure[free] = StructureName(c);
                }
                _touched[free] = c;
                _left[free] = -1f;
                _contactName = Describe(c);
            }
            catch (Exception) { }
        }

        private static void ExitPostfix(Component __instance, Collision col)
        {
            try
            {
                Transform p = PlayerTransform();
                if (p == null || __instance == null || __instance.transform != p || col == null) return;
                Collider c = col.collider;
                if (c == null) return;
                for (int i = 0; i < Slots; i++)
                    if (_touched[i] == c) { _left[i] = Time.time; break; }
            }
            catch (Exception) { }
        }

        private static bool Touched(Collider c)
        {
            for (int i = 0; i < Slots; i++)
                if (_touched[i] == c)
                    return (_left[i] < 0f || Time.time - _left[i] <= TouchKeep) && Time.time - _moved[i] > MoverKeep;
            return false;
        }

        /// "'LeafHutBuilt(Clone)'" when the collider belongs to a player-built
        /// structure, else "".
        private static string StructureName(Collider c)
        {
            Component h = null;
            if (_health != null) h = c.GetComponentInParent(_health);
            if (h == null && _healthChunk != null) h = c.GetComponentInParent(_healthChunk);
            return h != null ? "'" + h.name + "'" : "";
        }

        /// A built structure the player touches now (or left < TouchKeep ago), "" none.
        private static string StructureTouched()
        {
            float now = Time.time;
            for (int i = 0; i < Slots; i++)
                if (_touched[i] != null && !string.IsNullOrEmpty(_structure[i]) && (_left[i] < 0f || now - _left[i] <= TouchKeep))
                    return _structure[i];
            return "";
        }

        /// Notes which touched solids moved since the last step; true when
        /// one the player is touching now moved recently (carried).
        private static bool TrackMovers()
        {
            bool carried = false;
            float now = Time.time;
            for (int i = 0; i < Slots; i++)
            {
                Collider c = _touched[i];
                if (c == null) continue;
                bool recent = _left[i] < 0f || now - _left[i] <= TouchKeep;
                if (!recent) continue;
                Transform ct = c.transform;
                Vector3 p = ct.position;
                Quaternion r = ct.rotation;
                // 0.1 mm / ~0.002 degrees a step: the yacht bobs ~1 mm and ~0.006 degrees
                // a step at 60 Hz (Quaternion.Angle reads that as 0 in floats).
                if ((p - _pos[i]).sqrMagnitude > 1e-8f || ((r * Vector3.forward) - (_rot[i] * Vector3.forward)).sqrMagnitude > 1e-9f ||
                    ((r * Vector3.up) - (_rot[i] * Vector3.up)).sqrMagnitude > 1e-9f) _moved[i] = now;
                _pos[i] = p;
                _rot[i] = r;
                if (_left[i] < 0f && now - _moved[i] <= MoverKeep) carried = true;
            }
            return carried;
        }

        /// A solid the clip check may count: touched, static or kinematic,
        /// not a trigger, not terrain, not the player's own.
        private bool Counts(Collider c)
        {
            if (c == null || c.isTrigger || c is TerrainCollider) return false;
            Rigidbody body = c.attachedRigidbody;
            if (body != null && !body.isKinematic) return false;
            if (c.transform.IsChildOf(_player)) return false;
            return Touched(c);
        }

        /// "'name' under 'parent' (a BoxCollider 0.1 m thick)".
        public static string Describe(Collider c)
        {
            Transform parent = c.transform.parent;
            Vector3 s = c.bounds.size;
            float thin = Mathf.Min(s.x, Mathf.Min(s.y, s.z));
            return "'" + c.name + "'" + (parent != null ? " under '" + parent.name + "'" : "") + " (a " + c.GetType().Name + " " +
                   thin.ToString("0.0#") + " m thick)";
        }

        private void FixedUpdate()
        {
            try
            {
                MoveDetector d = MoveWatch.Detector;
                if (d == null) return;
                Transform t = PlayerTransform();
                if (t != _player)
                {
                    _player = t;
                    _rb = t != null ? t.GetComponent<Rigidbody>() : null;
                    _capsule = t != null ? t.GetComponent<CapsuleCollider>() : null;
                    _hasClear = false;
                }
                bool has = t != null && _rb != null && _capsule != null && t.gameObject.activeInHierarchy;
                if (!has)
                {
                    d.PhysicsStep(Time.fixedDeltaTime, false, false, Vector3.zero, Vector3.zero, "");
                    _hasClear = false;
                    return;
                }
                Steps++;
                Vector3 c = t.TransformPoint(_capsule.center);
                bool carried = TrackMovers();
                bool plain = d.PhysicsStep(Time.fixedDeltaTime, true, _rb.isKinematic, c, _rb.velocity, _contactName, carried, StructureTouched(),
                                           Read(_animControl, _groundChop), Read(_fpCharacter, _crouching));
                if (!plain) { _hasClear = false; _insideSteps = 0; }

                int layer = t.gameObject.layer;
                if (layer != _maskLayer)
                {
                    _maskLayer = layer;
                    _mask = 0;
                    for (int i = 0; i < 32; i++)
                        if (!Physics.GetIgnoreLayerCollision(layer, i)) _mask |= 1 << i;
                }

                if (Inside(c))
                {
                    if (_hasClear) _insideSteps++;
                    return;
                }
                if (_hasClear && (c - _clear).sqrMagnitude > 1e-6f)
                {
                    Collider hit = Across(_clear, c);
                    if (hit != null)
                    {
                        string what = Describe(hit);
                        _log.LogInfo("ClipWatch: the capsule's centre crossed " + what + " from (" + _clear.x.ToString("0.00") + ", " +
                                     _clear.y.ToString("0.00") + ", " + _clear.z.ToString("0.00") + ") to (" + c.x.ToString("0.00") + ", " +
                                     c.y.ToString("0.00") + ", " + c.z.ToString("0.00") + "), " + _insideSteps + " step(s) inside.");
                        d.Clipped(_clear, c, what, _insideSteps, Time.time);
                    }
                }
                _clear = c;
                _hasClear = true;
                _insideSteps = 0;
            }
            catch (Exception) { }
        }

        private bool Inside(Vector3 c)
        {
            int n = Physics.OverlapSphereNonAlloc(c, InsideRadius, _overlap, _mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n && i < _overlap.Length; i++)
                if (Counts(_overlap[i])) return true;
            return false;
        }

        /// The nearest counted solid the line a-b enters through a front
        /// face; null none.
        private Collider Across(Vector3 a, Vector3 b)
        {
            Vector3 dir = b - a;
            float len = dir.magnitude;
            dir /= len;
            int n = Physics.RaycastNonAlloc(a, dir, _hits, len, _mask, QueryTriggerInteraction.Ignore);
            if (n > _hits.Length) n = _hits.Length;
            Collider best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < n; i++)
                if (_hits[i].distance < bestD && Counts(_hits[i].collider)) { best = _hits[i].collider; bestD = _hits[i].distance; }
            return best;
        }
    }
}
