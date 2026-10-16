using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // A copy of the player's body (player_BASE) with nothing but its
    // animator, renderers and lights, posed from recorded animator state -
    // the first-person replay's arms (T-0387).
    //
    // WHY a copy and not the player: the live player's animator and arms
    // are never touched by a replay. WHY under an inactive holder: the
    // copy carries ~every player script (playerScriptSetup, PlayMaker
    // FSMs, the camera and its image effects, ...); instantiated as a
    // child of an inactive object none of them gets Awake / OnEnable, so
    // they are destroyed before they could register themselves as the
    // LocalPlayer. Only then is the holder switched on.
    //
    // The copy's animator shares the live one's controller (the same
    // AnimatorOverrideController instance) and avatar, so a recorded
    // state hash plays the same clip. Posing: Animator.Play(hash, layer,
    // normalizedTime) per layer + SetLayerWeight + the blend-tree floats,
    // with speed 0 so the animator never runs on by itself.
    // ------------------------------------------------------------------
    public sealed class ArmsReplica
    {
        public GameObject Holder;   // stands in for "player" (the recorded position)
        public GameObject Body;     // the copy of player_BASE
        public Animator Anim;
        public Transform Head;      // the copy of camFollowHead.headJnt (camFollowGo)
        public int Renderers;
        public int Stripped;
        public int Left;
        public float BuildMs;
        public Vector3 CamOffset;   // the view camera from Head, in the camera's frame

        /// False once a scene load took the copy (it lives in the active scene).
        public bool Alive { get { return Holder != null && Anim != null; } }

        /// Where the first-person camera goes for a look `look`: the copy's
        /// head joint as the game's camera follows the live one. Call after
        /// Evaluate so the head is this frame's.
        public Vector3 EyeAt(Quaternion look)
        {
            return Head != null ? Head.position + look * CamOffset : (Holder != null ? Holder.transform.position : Vector3.zero);
        }

        /// Shown only in the first-person view.
        public void Show(bool on)
        {
            if (Holder != null && Holder.activeSelf != on) Holder.SetActive(on);
        }

        /// Null (and `why`) when the player or its animator is not there.
        public static ArmsReplica Build(ManualLogSource log, out string why)
        {
            why = null;
            Animator live = AnimProbe.PlayerAnimator();
            if (live == null) { why = "no player animator"; return null; }
            float t0 = Time.realtimeSinceStartup;

            GameObject holder = new GameObject("ForestOverlay_ArmsReplica");
            holder.SetActive(false);
            GameObject src = live.gameObject;
            GameObject body = UnityEngine.Object.Instantiate(src, holder.transform) as GameObject;
            if (body == null) { UnityEngine.Object.Destroy(holder); why = "copy failed"; return null; }
            body.name = src.name;

            ArmsReplica r = new ArmsReplica();
            r.Holder = holder;
            r.Body = body;
            r.Stripped = Strip(body);
            r.Left = Remaining(body);
            if (r.Left > 0)
            {
                // A script left on would wake with the copy as a second player.
                UnityEngine.Object.Destroy(holder);
                why = r.Left + " components could not be removed";
                if (log != null) log.LogWarning("Arms replica: not built, " + why + ".");
                return null;
            }
            CopyPropertyBlocks(src, body);
            r.MapHeld(src.transform, body.transform);
            r.Anim = body.GetComponent<Animator>();
            r.Renderers = body.GetComponentsInChildren<Renderer>(true).Length;

            // The head the game's camera follows (camFollowHead.headJnt),
            // found by its path under player_BASE in the copy.
            Transform liveHead = HeadJoint();
            if (liveHead != null)
            {
                string rel = RelativePath(src.transform, liveHead);
                if (rel != null) r.Head = rel.Length == 0 ? body.transform : body.transform.Find(rel);
                // The view camera's place from that joint, in the camera's
                // own frame (0.05 m behind it: camFollowHead.offsetZ).
                // Measured 0.050 m; a camera flown away (freecam, the replay
                // camera already on) gives the default.
                r.CamOffset = new Vector3(0f, 0f, -0.05f);
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Vector3 off = Quaternion.Inverse(cam.transform.rotation) * (cam.transform.position - liveHead.position);
                    if (off.magnitude < 0.3f) r.CamOffset = off;
                }
            }

            if (r.Anim != null)
            {
                r.Anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                r.Anim.applyRootMotion = false;
                r.Anim.speed = 0f;
            }
            holder.SetActive(true);
            r.BuildMs = (Time.realtimeSinceStartup - t0) * 1000f;
            if (log != null)
                log.LogInfo("Arms replica: built in " + r.BuildMs.ToString("0.0") + " ms, " + r.Stripped +
                            " components stripped (" + r.Left + " left), " + r.Renderers + " renderers, " + r.HeldCount + " held models, head " + (r.Head != null ? r.Head.name : "not found") + ".");
            return r;
        }

        /// The recorded player position and yaw (the "player" transform;
        /// player_BASE keeps its local offset under the holder).
        public void Place(Vector3 playerPos, float yaw)
        {
            if (Holder == null) return;
            Holder.transform.position = playerPos;
            Holder.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// One layer's recorded state. Layer 0's weight is always 1.
        public void Pose(int layer, int fullPathHash, float normalizedTime, float weight)
        {
            if (Anim == null) return;
            if (layer > 0) Anim.SetLayerWeight(layer, weight);
            Anim.Play(fullPathHash, layer, normalizedTime);
        }

        public void SetFloat(int nameHash, float v)
        {
            if (Anim != null) Anim.SetFloat(nameHash, v);
        }

        /// Evaluates the pose set this frame now (else it shows on the
        /// animator's next update).
        public void Evaluate()
        {
            if (Anim != null) Anim.Update(0f);
        }

        public void Destroy()
        {
            if (Holder != null) UnityEngine.Object.Destroy(Holder);
            Holder = null;
            Body = null;
            Anim = null;
            Head = null;
        }

        /// The held models of these items shown, every other held model
        /// hidden (-1 = an empty hand). One id per equipment slot, as
        /// PlayerInventory._equipmentSlotsIds. A model not in the copy yet
        /// is copied on first use (a few ms; Prepare does it up front).
        public void SetHeld(int[] itemIds)
        {
            for (int i = 0; i < itemIds.Length; i++)
                if (itemIds[i] >= 0 && !_held.ContainsKey(itemIds[i])) CopyHeld(itemIds[i]);
            foreach (KeyValuePair<int, GameObject> kv in _held)
            {
                bool on = false;
                for (int i = 0; i < itemIds.Length; i++) if (itemIds[i] == kv.Key) on = true;
                if (kv.Value != null && kv.Value.activeSelf != on) kv.Value.SetActive(on);
            }
        }

        /// Copies the held models of these items now (when the replay
        /// starts, from the recording's held ids).
        public void Prepare(IList<int> itemIds)
        {
            for (int i = 0; i < itemIds.Count; i++)
                if (itemIds[i] >= 0 && !_held.ContainsKey(itemIds[i])) CopyHeld(itemIds[i]);
        }

        public int HeldCount { get { return _held.Count; } }

        private readonly Dictionary<int, GameObject> _held = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> _liveHeld = new Dictionary<int, GameObject>();
        private Transform _srcRoot;

        // Item id -> the live held model (InventoryItemView._itemId /
        // _held). A model that is in player_BASE now (the held ones, and
        // the few without a FakeParent) is already in the copy: mapped by
        // its path. The rest are FakeParent'ed: while not held they sit
        // unparented at the scene root, and FakeParent.ReParent puts them
        // under their target (rightHandHeld, leftHandHeld, ...) at
        // RealLocalPosition / RealLocalRotation when equipped - CopyHeld.
        private void MapHeld(Transform srcRoot, Transform copyRoot)
        {
            _srcRoot = srcRoot;
            try
            {
                Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
                object inv = local != null ? local.GetField("Inventory").GetValue(null) : null;
                if (inv == null) return;
                Array views = inv.GetType().GetField("_itemViews", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(inv) as Array;
                if (views == null) return;
                FieldInfo idF = null, heldF = null;
                for (int i = 0; i < views.Length; i++)
                {
                    object v = views.GetValue(i);
                    if (v == null) continue;
                    if (idF == null)
                    {
                        BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                        idF = v.GetType().GetField("_itemId", f);
                        heldF = v.GetType().GetField("_held", f);
                        if (idF == null || heldF == null) return;
                    }
                    GameObject held = heldF.GetValue(v) as GameObject;
                    if (held == null) continue;
                    int id = (int)idF.GetValue(v);
                    if (_liveHeld.ContainsKey(id)) continue;
                    _liveHeld[id] = held;
                    string rel = RelativePath(srcRoot, held.transform);
                    if (string.IsNullOrEmpty(rel)) continue;
                    Transform copy = copyRoot.Find(rel);
                    if (copy != null) _held[id] = copy.gameObject;
                }
            }
            catch (Exception) { }
        }

        private static Type _fakeParentType;
        private static PropertyInfo _fpPos, _fpRot;
        private static FieldInfo _fpTarget;

        private void CopyHeld(int id)
        {
            _held[id] = null;   // tried once
            GameObject live;
            if (Body == null || !_liveHeld.TryGetValue(id, out live) || live == null) return;
            try
            {
                if (_fakeParentType == null)
                {
                    _fakeParentType = GameBridge.FindGameType("TheForest.Utils.FakeParent");
                    if (_fakeParentType == null) return;
                    _fpTarget = _fakeParentType.GetField("target");
                    _fpPos = _fakeParentType.GetProperty("RealLocalPosition");
                    _fpRot = _fakeParentType.GetProperty("RealLocalRotation");
                }
                Component fp = live.GetComponent(_fakeParentType);
                if (fp == null) return;
                Transform target = _fpTarget.GetValue(fp) as Transform;
                string rel = target != null ? RelativePath(_srcRoot, target) : null;
                if (rel == null) return;
                Transform copyTarget = rel.Length == 0 ? Body.transform : Body.transform.Find(rel);
                if (copyTarget == null) return;

                // As the body: under an inactive holder, stripped, then moved.
                GameObject temp = new GameObject("ForestOverlay_ArmsHeldTemp");
                temp.SetActive(false);
                GameObject copy = UnityEngine.Object.Instantiate(live, temp.transform) as GameObject;
                if (copy != null)
                {
                    copy.name = live.name;
                    Strip(copy);
                    if (Remaining(copy) > 0) { UnityEngine.Object.Destroy(temp); return; }
                    copy.SetActive(false);
                    copy.transform.SetParent(copyTarget, false);
                    copy.transform.localPosition = (Vector3)_fpPos.GetValue(fp, null);
                    copy.transform.localRotation = (Quaternion)_fpRot.GetValue(fp, null);
                    _held[id] = copy;
                }
                UnityEngine.Object.Destroy(temp);
            }
            catch (Exception) { }
        }

        // The body's blood / dirt and the clothes' tints are per-renderer
        // property blocks (PlayerStats.MyBody: _Damage1-4), which a copy
        // does not take. Both hierarchies are the same shape, so the
        // renderers come in the same order.
        private static void CopyPropertyBlocks(GameObject src, GameObject copy)
        {
            Renderer[] a = src.GetComponentsInChildren<Renderer>(true);
            Renderer[] b = copy.GetComponentsInChildren<Renderer>(true);
            if (a.Length != b.Length) return;
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == null || b[i] == null) continue;
                mpb.Clear();
                a[i].GetPropertyBlock(mpb);
                if (!mpb.isEmpty) b[i].SetPropertyBlock(mpb);
            }
        }

        // ------------------------------------------------------------------

        private static Transform HeadJoint()
        {
            try
            {
                Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
                if (local == null) return null;
                object follow = local.GetField("CamFollowHead").GetValue(null);
                if (follow == null) return null;
                object head = follow.GetType().GetField("headJnt").GetValue(follow);
                return head as Transform;
            }
            catch (Exception) { return null; }
        }

        private static string RelativePath(Transform root, Transform t)
        {
            List<string> parts = new List<string>();
            while (t != null && t != root)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            if (t != root) return null;
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        // Everything but the transforms, the animator, renderers (with
        // their mesh filters), particle systems and lights. Scripts first,
        // and never one another component on its object still needs
        // (Needed): DestroyImmediate refuses those with a Unity error per
        // try ("Can't remove Camera because GUILayer depends on it" - the
        // first build logged six). Sweeps repeat until nothing goes.
        private static int Strip(GameObject body)
        {
            int n = 0;
            for (int pass = 0; pass < 8; pass++)
            {
                int gone = Sweep(body, true);
                n += gone;
                if (gone == 0) break;
            }
            for (int pass = 0; pass < 8; pass++)
            {
                int gone = Sweep(body, false);
                n += gone;
                if (gone == 0) break;
            }
            return n;
        }

        /// Components a strip could not take (0 expected): they would wake
        /// with the copy.
        private static int Remaining(GameObject body)
        {
            int n = 0;
            Component[] all = body.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < all.Length; i++) if (all[i] != null && !Keep(all[i])) n++;
            return n;
        }

        private static int Sweep(GameObject body, bool scriptsOnly)
        {
            int n = 0;
            Component[] all = body.GetComponentsInChildren<Component>(true);
            for (int i = all.Length - 1; i >= 0; i--)
            {
                Component c = all[i];
                if (c == null || Keep(c)) continue;
                if (scriptsOnly && !(c is MonoBehaviour)) continue;
                if (Needed(c)) continue;
                UnityEngine.Object.DestroyImmediate(c);
                if (c == null) n++;
            }
            return n;
        }

        // Another component on the same object still needs this one: a
        // [RequireComponent] (scripts, and the engine's own: GUILayer and
        // FlareLayer on a Camera), or a Joint's Rigidbody.
        private static bool Needed(Component c)
        {
            Type ct = c.GetType();
            Component[] mates = c.GetComponents<Component>();
            for (int i = 0; i < mates.Length; i++)
            {
                Component m = mates[i];
                if (m == null || m == c || Keep(m)) continue;
                if (c is Rigidbody && m is Joint) return true;
                if (c is Camera && m is Behaviour && !(m is MonoBehaviour) && !(m is Camera)) return true;
                object[] req = m.GetType().GetCustomAttributes(typeof(RequireComponent), true);
                for (int k = 0; k < req.Length; k++)
                {
                    RequireComponent r = (RequireComponent)req[k];
                    if (Fits(r.m_Type0, ct) || Fits(r.m_Type1, ct) || Fits(r.m_Type2, ct)) return true;
                }
            }
            return false;
        }

        private static bool Fits(Type required, Type t)
        {
            return required != null && required.IsAssignableFrom(t);
        }

        private static bool Keep(Component c)
        {
            return c is Transform || c is Animator || c is Renderer || c is MeshFilter ||
                   c is ParticleSystem || c is Light;
        }
    }
}
