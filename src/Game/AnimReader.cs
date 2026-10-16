using System;
using System.Collections.Generic;
using UnityEngine;
using ForestOverlay.Data;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Reads the player's animator for the run recorder (T-0387): per layer
    // the state, its normalized time and weight, the blend-tree floats, and
    // the held items - into the reusable AnimSnapshot, no allocation per
    // read (the parameter list is read once per animator instance).
    // Unlike the look, it is read while freecam flies the view: the
    // animator is the player's either way.
    // ------------------------------------------------------------------
    public sealed class AnimReader
    {
        private Animator _animator;
        private string[] _names = new string[0];
        private int[] _hashes = new int[0];
        private readonly InventoryReader _inventory;

        public AnimReader(InventoryReader inventory)
        {
            _inventory = inventory;
        }

        /// False when there is no player animator. `player` is the recorded
        /// position's transform (its yaw is the body's facing).
        public bool Read(AnimSnapshot s, Transform player)
        {
            Animator an = AnimProbe.PlayerAnimator();
            if (an == null) return false;
            if (!ReferenceEquals(an, _animator)) Bind(an);

            int layers = an.layerCount;
            if (layers <= 0) return false;
            if (s.Layers != layers || !ReferenceEquals(s.FloatNames, _names)) s.Size(layers, _names);

            for (int i = 0; i < layers; i++)
            {
                AnimatorStateInfo st = an.GetCurrentAnimatorStateInfo(i);
                // Past the middle of a crossfade the next state is the one
                // that shows (the replay cannot blend two states).
                if (an.IsInTransition(i) && an.GetAnimatorTransitionInfo(i).normalizedTime >= 0.5f)
                    st = an.GetNextAnimatorStateInfo(i);
                s.Hash[i] = st.fullPathHash;
                s.Time[i] = st.normalizedTime;
                s.Weight[i] = i == 0 ? 1f : an.GetLayerWeight(i);
            }
            for (int i = 0; i < _hashes.Length; i++) s.Floats[i] = an.GetFloat(_hashes[i]);

            s.Yaw = player != null ? player.eulerAngles.y : 0f;

            int[] ids = _inventory != null ? _inventory.EquipmentIds() : null;
            if (ids == null) { if (s.Held.Length != 0) s.Held = new int[0]; }
            else
            {
                if (s.Held.Length != ids.Length) s.Held = new int[ids.Length];
                Array.Copy(ids, s.Held, ids.Length);
            }
            return true;
        }

        private void Bind(Animator an)
        {
            _animator = an;
            List<string> names = new List<string>();
            List<int> hashes = new List<int>();
            AnimatorControllerParameter[] ps = an.parameters;
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i].type != AnimatorControllerParameterType.Float) continue;
                names.Add(ps[i].name);
                hashes.Add(ps[i].nameHash);
            }
            _names = names.ToArray();
            _hashes = hashes.ToArray();
        }
    }
}
