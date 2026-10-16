using System;
using System.Collections.Generic;

namespace ForestOverlay.Data
{
    /// One animator layer at one moment. Hash 0 = the layer is off (a
    /// layer above 0 at weight 0). In a crossfade, Next is the state it
    /// goes to and P how far (0..1). Rates are per second of run time:
    /// what the playback adds to Time / NextTime / P / Weight by itself.
    public struct AnimLayerState
    {
        public int Hash;
        public float Time, Rate, Weight, WeightRate;
        public int Next;
        public float NextTime, NextRate, P, PRate;
    }

    /// A layer's state from T on (the playback advances it by its rates).
    public struct AnimLayerKey
    {
        public float T;
        public int Layer;
        public AnimLayerState S;
    }

    /// A float parameter from T on: Value + Rate per second.
    public struct AnimFloatKey
    {
        public float T;
        public int Index;
        public float Value, Rate;
    }

    /// The body's yaw from T on: Yaw + Rate per second (degrees).
    public struct AnimYawKey
    {
        public float T;
        public float Yaw, Rate;
    }

    /// The equipment slot ids from this time on (an empty slot is -1).
    public struct AnimHeld
    {
        public float T;
        public int[] Ids;
    }

    /// One animator reading, filled by the game side into reusable buffers
    /// (Game/AnimReader) and handed to AnimTrack - nothing allocates per
    /// reading. Per layer: the current state, and in a crossfade the next
    /// one and the progress (Next 0 = none).
    public sealed class AnimSnapshot
    {
        public string[] FloatNames = new string[0];
        public int Layers;
        public float Yaw;
        public int[] Hash = new int[0];
        public float[] Time = new float[0];
        public float[] Rate = new float[0];
        public float[] Weight = new float[0];
        public int[] Next = new int[0];
        public float[] NextTime = new float[0];
        public float[] NextRate = new float[0];
        public float[] P = new float[0];
        public float[] Floats = new float[0];
        public int[] Held = new int[0];

        public void Size(int layers, string[] floatNames)
        {
            Layers = layers;
            FloatNames = floatNames;
            if (Hash.Length != layers)
            {
                Hash = new int[layers]; Time = new float[layers]; Rate = new float[layers]; Weight = new float[layers];
                Next = new int[layers]; NextTime = new float[layers]; NextRate = new float[layers]; P = new float[layers];
            }
            if (Floats.Length != floatNames.Length) Floats = new float[floatNames.Length];
        }
    }

    // ------------------------------------------------------------------
    // The runner's arms, as animator state (T-0387): the first-person
    // replay poses a copy of the player's body from it (Game/ArmsReplica).
    // Own lines in the .run (anim / ak / af / ay / ah): older readers and
    // the site skip them.
    //
    // Changes only, by dead reckoning (author's choice after a measured
    // comparison of 30 Hz, 10 Hz and changes only - tasks/notes/T-0387.md):
    // a layer is read every frame and keyed when its state or crossfade
    // changes, or when the playback's own advance (time, progress, weight
    // by their rates) would be off by more than LayerThreshold; the floats
    // and the yaw are read on the 30 Hz position tick and keyed when their
    // extrapolation drifts. Values are kept at file precision, so a track
    // read back from its lines plays exactly as recorded.
    // ------------------------------------------------------------------
    public sealed class AnimTrack
    {
        /// Normalized time, crossfade progress and weight.
        public const float LayerThreshold = 0.02f;
        public const float FloatThreshold = 0.003f;
        public const float YawThreshold = 0.5f;
        /// A float / the yaw is never extrapolated further than this (a gap
        /// in the recording, a load, must not run a value away).
        public const float MaxExtrapolate = 1f;

        public string[] Floats = new string[0];
        public int Layers;

        public readonly List<AnimLayerKey> LayerKeys = new List<AnimLayerKey>();
        public readonly List<AnimFloatKey> FloatKeys = new List<AnimFloatKey>();
        public readonly List<AnimYawKey> YawKeys = new List<AnimYawKey>();
        public readonly List<AnimHeld> HeldChanges = new List<AnimHeld>();

        // Recording.
        private readonly AnimLayerState[] _prev;
        private float _prevT = -1f;
        private readonly int[] _lastLayerKey;
        private readonly float[] _prevFloat;
        private float _prevFloatT = -1f;
        private readonly int[] _lastFloatKey;
        private float _prevYaw;

        // Reading: key indexes per layer / float, rebuilt after an add.
        private bool _indexDirty = true;
        private List<int>[] _byLayer;
        private List<int>[] _byFloat;

        public AnimTrack(int layers, string[] floats)
        {
            Layers = layers;
            Floats = floats ?? new string[0];
            _prev = new AnimLayerState[layers];
            _lastLayerKey = new int[layers];
            for (int i = 0; i < layers; i++) _lastLayerKey[i] = -1;
            _prevFloat = new float[Floats.Length];
            _lastFloatKey = new int[Floats.Length];
            for (int i = 0; i < _lastFloatKey.Length; i++) _lastFloatKey[i] = -1;
        }

        /// True when there is a pose to show.
        public bool HasPose { get { return LayerKeys.Count > 0; } }

        /// True when `s` fits this track's layer and float counts.
        public bool Fits(AnimSnapshot s)
        {
            return s.Layers == Layers && s.Floats.Length == Floats.Length;
        }

        // --- recording -------------------------------------------------------

        /// One reading of the layers (every frame): a key per layer whose
        /// state changed or whose advance drifted.
        public void AddLayers(float t, AnimSnapshot s)
        {
            float dt = _prevT >= 0f ? t - _prevT : 0f;
            for (int l = 0; l < Layers; l++)
            {
                AnimLayerState c = Read(s, l);
                AnimLayerState p = _prev[l];
                bool same = _prevT >= 0f && p.Hash == c.Hash && p.Next == c.Next;
                if (c.Hash != 0 && l > 0 && same && dt > 0f) c.WeightRate = (c.Weight - p.Weight) / dt;
                if (c.Next != 0 && dt > 0f) c.PRate = same ? (c.P - p.P) / dt : c.P / dt;
                c = Quantize(c);
                _prev[l] = c;

                int ki = _lastLayerKey[l];
                bool write = ki < 0;
                if (!write)
                {
                    AnimLayerKey k = LayerKeys[ki];
                    if (k.S.Hash != c.Hash || k.S.Next != c.Next) write = true;
                    else if (c.Hash != 0) write = Drifted(Advance(k.S, t - k.T), c);
                }
                if (!write) continue;
                AnimLayerKey nk;
                nk.T = KeyTime(t); nk.Layer = l; nk.S = c;
                _lastLayerKey[l] = LayerKeys.Count;
                AddLayerKey(nk);
            }
            _prevT = t;
        }

        /// One reading of the floats, the yaw and the held items (the 30 Hz
        /// tick): keys where the extrapolation drifted.
        public void AddFloats(float t, AnimSnapshot s)
        {
            float dt = _prevFloatT >= 0f ? t - _prevFloatT : 0f;
            for (int i = 0; i < Floats.Length; i++)
            {
                float v = s.Floats[i];
                float rate = dt > 0f ? (v - _prevFloat[i]) / dt : 0f;
                _prevFloat[i] = v;
                int ki = _lastFloatKey[i];
                if (ki >= 0 && Math.Abs(FloatAt(FloatKeys[ki], t) - v) <= FloatThreshold) continue;
                AnimFloatKey k;
                k.T = KeyTime(t); k.Index = i; k.Value = Q(v, 1000f); k.Rate = Q(rate, 1000f);
                _lastFloatKey[i] = FloatKeys.Count;
                AddFloatKey(k);
            }

            float yr = dt > 0f ? DeltaAngle(_prevYaw, s.Yaw) / dt : 0f;
            _prevYaw = s.Yaw;
            if (YawKeys.Count == 0 || Math.Abs(DeltaAngle(YawAt(YawKeys[YawKeys.Count - 1], t), s.Yaw)) > YawThreshold)
            {
                AnimYawKey y;
                y.T = KeyTime(t); y.Yaw = Q(Repeat(s.Yaw), 10f); y.Rate = Q(yr, 10f);
                AddYawKey(y);
            }
            _prevFloatT = t;

            if (s.Held != null && s.Held.Length > 0 && HeldDiffers(s.Held))
                AddHeld(KeyTime(t), (int[])s.Held.Clone());
        }

        public void AddLayerKey(AnimLayerKey k) { LayerKeys.Add(k); _indexDirty = true; }
        public void AddFloatKey(AnimFloatKey k) { FloatKeys.Add(k); _indexDirty = true; }
        public void AddYawKey(AnimYawKey k) { YawKeys.Add(k); }

        public void AddHeld(float t, int[] ids)
        {
            AnimHeld h;
            h.T = t; h.Ids = ids;
            HeldChanges.Add(h);
        }

        private static AnimLayerState Read(AnimSnapshot s, int l)
        {
            AnimLayerState c = new AnimLayerState();
            if (l > 0 && s.Weight[l] <= 0f) return c;   // off
            c.Hash = s.Hash[l];
            c.Time = s.Time[l];
            c.Rate = s.Rate[l];
            c.Weight = l == 0 ? 1f : s.Weight[l];
            if (s.Next[l] != 0)
            {
                c.Next = s.Next[l];
                c.NextTime = s.NextTime[l];
                c.NextRate = s.NextRate[l];
                c.P = s.P[l];
            }
            return c;
        }

        private static bool Drifted(AnimLayerState pred, AnimLayerState c)
        {
            if (Math.Abs(pred.Time - c.Time) > LayerThreshold) return true;
            if (Math.Abs(pred.Weight - c.Weight) > LayerThreshold) return true;
            if (c.Next != 0 && (Math.Abs(pred.NextTime - c.NextTime) > LayerThreshold || Math.Abs(pred.P - c.P) > LayerThreshold)) return true;
            return false;
        }

        /// File precision (AttemptFormat writes these), so the recorder
        /// predicts with exactly what the playback will read.
        private static AnimLayerState Quantize(AnimLayerState c)
        {
            c.Time = Q(c.Time, 1000f); c.Rate = Q(c.Rate, 10000f);
            c.Weight = Q(c.Weight, 1000f); c.WeightRate = Q(c.WeightRate, 1000f);
            c.NextTime = Q(c.NextTime, 1000f); c.NextRate = Q(c.NextRate, 10000f);
            c.P = Q(c.P, 1000f); c.PRate = Q(c.PRate, 1000f);
            return c;
        }

        /// A key's time in the file's milliseconds, rounded down: a key never
        /// starts after the frame it was read on (a state change shows on its
        /// own frame, not the next).
        private static float KeyTime(float t)
        {
            return (float)(Math.Floor(t * 1000.0 + 1e-4) / 1000.0);
        }

        private static float Q(float v, float scale)
        {
            return (float)(Math.Round(v * scale) / scale);
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

        /// A key's state `d` seconds on: the playback's own advance.
        public static AnimLayerState Advance(AnimLayerState s, float d)
        {
            if (d <= 0f || s.Hash == 0) return s;
            s.Time += d * s.Rate;
            if (s.WeightRate != 0f) s.Weight = Clamp01(s.Weight + d * s.WeightRate);
            if (s.Next != 0)
            {
                s.NextTime += d * s.NextRate;
                s.P = Math.Min(1f, s.P + d * s.PRate);
            }
            return s;
        }

        private static float FloatAt(AnimFloatKey k, float t)
        {
            float d = t - k.T;
            if (d <= 0f) return k.Value;
            return k.Value + Math.Min(d, MaxExtrapolate) * k.Rate;
        }

        private static float YawAt(AnimYawKey k, float t)
        {
            float d = t - k.T;
            if (d <= 0f) return k.Yaw;
            return Repeat(k.Yaw + Math.Min(d, MaxExtrapolate) * k.Rate);
        }

        /// The recorded pose at `t` into the caller's buffers (`layers`
        /// Layers long, `floats` Floats.Length long). Before a layer's first
        /// key: that key. False when there is nothing recorded.
        public bool PoseAt(float t, AnimLayerState[] layers, float[] floats, out float yaw)
        {
            yaw = 0f;
            if (LayerKeys.Count == 0) return false;
            if (_indexDirty) BuildIndex();

            for (int l = 0; l < Layers && l < layers.Length; l++)
            {
                List<int> ix = _byLayer[l];
                if (ix.Count == 0) { layers[l] = new AnimLayerState(); continue; }
                int i = LastAtOrBefore(ix, t, true);
                AnimLayerKey k = LayerKeys[ix[i < 0 ? 0 : i]];
                layers[l] = i < 0 ? k.S : Advance(k.S, t - k.T);
            }
            for (int f = 0; f < Floats.Length && f < floats.Length; f++)
            {
                List<int> ix = _byFloat[f];
                if (ix.Count == 0) { floats[f] = 0f; continue; }
                int i = LastAtOrBefore(ix, t, false);
                floats[f] = FloatAt(FloatKeys[ix[i < 0 ? 0 : i]], t);
            }
            if (YawKeys.Count > 0)
            {
                int lo = 0, hi = YawKeys.Count;
                while (lo < hi)
                {
                    int mid = (lo + hi) >> 1;
                    if (YawKeys[mid].T <= t) lo = mid + 1; else hi = mid;
                }
                yaw = YawAt(YawKeys[lo > 0 ? lo - 1 : 0], t);
            }
            return true;
        }

        private int LastAtOrBefore(List<int> ix, float t, bool layerKeys)
        {
            int lo = 0, hi = ix.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                float kt = layerKeys ? LayerKeys[ix[mid]].T : FloatKeys[ix[mid]].T;
                if (kt <= t) lo = mid + 1; else hi = mid;
            }
            return lo - 1;
        }

        private void BuildIndex()
        {
            _byLayer = new List<int>[Layers];
            for (int l = 0; l < Layers; l++) _byLayer[l] = new List<int>();
            for (int i = 0; i < LayerKeys.Count; i++)
            {
                int l = LayerKeys[i].Layer;
                if (l >= 0 && l < Layers) _byLayer[l].Add(i);
            }
            _byFloat = new List<int>[Floats.Length];
            for (int k = 0; k < _byFloat.Length; k++) _byFloat[k] = new List<int>();
            for (int i = 0; i < FloatKeys.Count; i++)
            {
                int k = FloatKeys[i].Index;
                if (k >= 0 && k < _byFloat.Length) _byFloat[k].Add(i);
            }
            _indexDirty = false;
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

        private static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }

        private static float Repeat(float a)
        {
            a %= 360f;
            return a < 0f ? a + 360f : a;
        }

        private static float DeltaAngle(float a, float b)
        {
            float d = (b - a) % 360f;
            if (d > 180f) d -= 360f;
            if (d < -180f) d += 360f;
            return d;
        }
    }
}
