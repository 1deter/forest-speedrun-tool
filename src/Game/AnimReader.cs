using System;
using System.Collections.Generic;
using UnityEngine;
using ForestOverlay.Data;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Reads the player's animator for the run recorder (T-0387): per layer
    // the state, its normalized time, its rate and weight, and in a
    // crossfade the next state and the progress - every frame; the
    // blend-tree floats, the body's yaw and the held items when asked (the
    // 30 Hz tick). Into the reusable AnimSnapshot, no allocation per read
    // (the parameter list is read once per animator instance).
    // Unlike the look, it is read while freecam flies the view: the
    // animator is the player's either way.
    // ------------------------------------------------------------------
    public sealed class AnimReader
    {
        private Animator _animator;
        private string[] _names = new string[0];
        private int[] _hashes = new int[0];
        private readonly InventoryReader _inventory;
        private readonly int[] _held = new int[8];

        public AnimReader(InventoryReader inventory)
        {
            _inventory = inventory;
        }

        /// False when there is no player animator. `player` is the recorded
        /// position's transform (its yaw is the body's facing).
        public bool Read(AnimSnapshot s, Transform player, bool floats)
        {
            Animator an = AnimProbe.PlayerAnimator();
            if (an == null) return false;
            if (!ReferenceEquals(an, _animator)) Bind(an);

            int layers = an.layerCount;
            if (layers <= 0) return false;
            if (s.Layers != layers || !ReferenceEquals(s.FloatNames, _names)) s.Size(layers, _names);

            // Normalized time per second of run time (the recorder's clock is
            // unscaled); AnimatorStateInfo.length already holds the state's
            // speed (measured: 1 / length matched, speed / length did not).
            float pace = an.speed * Time.timeScale;
            for (int i = 0; i < layers; i++)
            {
                AnimatorStateInfo st = an.GetCurrentAnimatorStateInfo(i);
                s.Hash[i] = st.fullPathHash;
                s.Time[i] = st.normalizedTime;
                s.Rate[i] = st.length > 0.0001f ? pace / st.length : 0f;
                s.Weight[i] = i == 0 ? 1f : an.GetLayerWeight(i);
                if (an.IsInTransition(i))
                {
                    AnimatorStateInfo nx = an.GetNextAnimatorStateInfo(i);
                    s.Next[i] = nx.fullPathHash;
                    s.NextTime[i] = nx.normalizedTime;
                    s.NextRate[i] = nx.length > 0.0001f ? pace / nx.length : 0f;
                    s.P[i] = an.GetAnimatorTransitionInfo(i).normalizedTime;
                }
                else s.Next[i] = 0;
            }
            if (!floats) return true;

            for (int i = 0; i < _hashes.Length; i++) s.Floats[i] = an.GetFloat(_hashes[i]);
            s.Yaw = player != null ? player.eulerAngles.y : 0f;

            int n = _inventory != null ? _inventory.EquipmentIds(_held) : 0;
            if (s.Held.Length != n) s.Held = new int[n];   // once: the slot count is fixed
            Array.Copy(_held, s.Held, n);
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
