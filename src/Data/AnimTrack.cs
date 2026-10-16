using System;
using System.Collections.Generic;

namespace ForestOverlay.Data
{
    /// One float parameter changing (Index into AnimTrack.Floats).
    public struct AnimFloat
    {
        public float T;
        public int Index;
        public float Value;
    }

    /// The equipment slot ids from this time on (an empty slot is -1).
    public struct AnimHeld
    {
        public float T;
        public int[] Ids;
    }

    /// One animator reading, filled by the game side into reusable buffers
    /// (Game/AnimReader) and handed to AnimTrack.Add - nothing allocates
    /// per sample.
    public sealed class AnimSnapshot
    {
        public string[] FloatNames = new string[0];
        public int Layers;
        public float Yaw;
        public int[] Hash = new int[0];
        public float[] Time = new float[0];
        public float[] Weight = new float[0];
        public float[] Floats = new float[0];
        public int[] Held = new int[0];

        public void Size(int layers, string[] floatNames)
        {
            Layers = layers;
            FloatNames = floatNames;
            if (Hash.Length != layers) { Hash = new int[layers]; Time = new float[layers]; Weight = new float[layers]; }
            if (Floats.Length != floatNames.Length) Floats = new float[floatNames.Length];
        }
    }

    // ------------------------------------------------------------------
    // The runner's arms, as animator state (T-0387): per layer the state
    // (its path hash), its normalized time and weight, the blend-tree
    // floats and the held items. The first-person replay poses a copy of
    // the player's body from it (Game/ArmsReplica). Own lines in the .run
    // (anim / a / af / ah): older readers and the site skip them.
    //
    // Flat lists, no array per sample. A layer at weight 0 is stored as
    // hash 0, time 0, weight 0. Time is Unity's cumulative normalizedTime,
    // so it interpolates without wrapping.
    // ------------------------------------------------------------------
    public sealed class AnimTrack
    {
        /// Sample interval the recorder uses (10 Hz + interpolation).
        public const float Interval = 0.1f;
        public const float FloatEpsilon = 0.01f;

        public string[] Floats = new string[0];
        public int Layers;

        public readonly List<float> T = new List<float>();
        public readonly List<float> Yaw = new List<float>();
        public readonly List<int> Hash = new List<int>();
        public readonly List<float> Time = new List<float>();
        public readonly List<float> Weight = new List<float>();
        public readonly List<AnimFloat> FloatChanges = new List<AnimFloat>();
        public readonly List<AnimHeld> HeldChanges = new List<AnimHeld>();

        private float[] _lastFloats;
        private bool _floatsDirty = true;
        private List<int>[] _byFloat;

        public AnimTrack(int layers, string[] floats)
        {
            Layers = layers;
            Floats = floats ?? new string[0];
            _lastFloats = new float[Floats.Length];
        }

        public int Count { get { return T.Count; } }

        /// True when `s` fits this track's layer and float counts.
        public bool Fits(AnimSnapshot s)
        {
            return s.Layers == Layers && s.Floats.Length == Floats.Length;
        }

        /// Appends one reading at `t`; floats and held ids only when they changed.
        public void Add(float t, AnimSnapshot s)
        {
            bool first = T.Count == 0;
            T.Add(t);
            Yaw.Add(s.Yaw);
            for (int i = 0; i < Layers; i++)
            {
                bool off = s.Weight[i] <= 0f && i > 0;
                Hash.Add(off ? 0 : s.Hash[i]);
                Time.Add(off ? 0f : s.Time[i]);
                Weight.Add(off ? 0f : (i == 0 ? 1f : s.Weight[i]));
            }
            for (int i = 0; i < Floats.Length; i++)
            {
                float v = s.Floats[i];
                if (!first && Math.Abs(v - _lastFloats[i]) <= FloatEpsilon) continue;
                _lastFloats[i] = v;
                AddFloat(t, i, v);
            }
            if (s.Held != null && s.Held.Length > 0 && HeldDiffers(s.Held))
                AddHeld(t, (int[])s.Held.Clone());
        }

        /// Loading from a file: no diffing.
        public void AddSampleRaw(float t, float yaw, int[] hash, float[] time, float[] weight)
        {
            T.Add(t);
            Yaw.Add(yaw);
            for (int i = 0; i < Layers; i++) { Hash.Add(hash[i]); Time.Add(time[i]); Weight.Add(weight[i]); }
        }

        public void AddFloat(float t, int index, float v)
        {
            AnimFloat f;
            f.T = t; f.Index = index; f.Value = v;
            FloatChanges.Add(f);
            _floatsDirty = true;
        }

        public void AddHeld(float t, int[] ids)
        {
            AnimHeld h;
            h.T = t; h.Ids = ids;
            HeldChanges.Add(h);
        }

        private bool HeldDiffers(int[] ids)
        {
            if (HeldChanges.Count == 0) return true;
            int[] last = HeldChanges[HeldChanges.Count - 1].Ids;
            if (last.Length != ids.Length) return true;
            for (int i = 0; i < ids.Length; i++) if (last[i] != ids[i]) return true;
            return false;
        }

        // --- reading ----------------------------------------------------------

        /// The first sample index with T >= t (Count when none).
        private int Index(float t)
        {
            int lo = 0, hi = T.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (T[mid] < t) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        /// The recorded pose at `t` into the caller's buffers (Layers long;
        /// floats Floats.Length long; a layer off has hash 0). Same state in
        /// the samples around t: time and weight interpolate; a different
        /// one: the earlier sample's state (a switch shows at its recorded
        /// time). False when there are no samples.
        public bool PoseAt(float t, int[] hash, float[] time, float[] weight, float[] floats, out float yaw)
        {
            yaw = 0f;
            if (T.Count == 0) return false;
            int i = Index(t);
            int a, b;
            float f;
            if (i <= 0) { a = b = 0; f = 0f; }
            else if (i >= T.Count) { a = b = T.Count - 1; f = 0f; }
            else
            {
                a = i - 1; b = i;
                float span = T[b] - T[a];
                f = span <= 0f ? 1f : (t - T[a]) / span;
            }

            yaw = LerpAngle(Yaw[a], Yaw[b], f);
            for (int l = 0; l < Layers; l++)
            {
                int ia = a * Layers + l, ib = b * Layers + l;
                hash[l] = Hash[ia];
                if (a != b && Hash[ia] == Hash[ib] && Hash[ia] != 0)
                {
                    time[l] = Time[ia] + (Time[ib] - Time[ia]) * f;
                    weight[l] = Weight[ia] + (Weight[ib] - Weight[ia]) * f;
                }
                else
                {
                    time[l] = Time[ia];
                    weight[l] = Weight[ia];
                }
            }

            if (_floatsDirty) BuildFloatIndex();
            for (int k = 0; k < Floats.Length; k++) floats[k] = FloatAt(k, t);
            return true;
        }

        private void BuildFloatIndex()
        {
            _byFloat = new List<int>[Floats.Length];
            for (int k = 0; k < _byFloat.Length; k++) _byFloat[k] = new List<int>();
            for (int i = 0; i < FloatChanges.Count; i++)
            {
                int k = FloatChanges[i].Index;
                if (k >= 0 && k < _byFloat.Length) _byFloat[k].Add(i);
            }
            _floatsDirty = false;
        }

        private float FloatAt(int k, float t)
        {
            List<int> ix = _byFloat[k];
            if (ix.Count == 0) return 0f;
            int lo = 0, hi = ix.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (FloatChanges[ix[mid]].T < t) lo = mid + 1; else hi = mid;
            }
            if (lo <= 0) return FloatChanges[ix[0]].Value;
            if (lo >= ix.Count) return FloatChanges[ix[ix.Count - 1]].Value;
            AnimFloat c0 = FloatChanges[ix[lo - 1]], c1 = FloatChanges[ix[lo]];
            float span = c1.T - c0.T;
            float f = span <= 0f ? 1f : (t - c0.T) / span;
            return c0.Value + (c1.Value - c0.Value) * f;
        }

        /// Index of the last held change at or before t (-1 before the first).
        public int HeldIndexAt(float t)
        {
            int n = -1;
            for (int i = 0; i < HeldChanges.Count && HeldChanges[i].T <= t; i++) n = i;
            return n;
        }

        /// The equipment ids at t (null before any change was recorded).
        public int[] HeldAt(float t)
        {
            int i = HeldIndexAt(t);
            return i < 0 ? null : HeldChanges[i].Ids;
        }

        /// Every item id held at any time (Prepare: one hitch, not mid-replay).
        public void AllHeldIds(List<int> into)
        {
            for (int i = 0; i < HeldChanges.Count; i++)
                for (int j = 0; j < HeldChanges[i].Ids.Length; j++)
                {
                    int id = HeldChanges[i].Ids[j];
                    if (id >= 0 && !into.Contains(id)) into.Add(id);
                }
        }

        private static float LerpAngle(float a, float b, float f)
        {
            float d = (b - a) % 360f;
            if (d > 180f) d -= 360f;
            if (d < -180f) d += 360f;
            return a + d * f;
        }
    }
}
