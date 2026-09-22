using System;
using System.Collections.Generic;
using UnityEngine;

namespace ForestOverlay.Data
{
    public struct RunSample
    {
        public float T;          // seconds since the attempt started
        public Vector3 P;
        public float Speed;      // horizontal speed at this sample
    }

    /// One snapshot of the player's full numeric state.
    ///
    /// Kept on a SEPARATE track from the position samples, at a lower
    /// rate. Position needs 30 Hz for the line to look smooth; health and
    /// stamina do not change meaningfully per frame, and storing ~60
    /// channels at 30 Hz would inflate a two-minute run by an order of
    /// magnitude for no analytical gain.
    ///
    /// Values are indexed against Attempt.Channels. A flat float[] rather
    /// than a named dictionary so a sample is cheap to store, cheap to
    /// write, and trivial for the viewer to filter.
    public struct StateSample
    {
        public float T;
        public float[] Values;
    }

    // ------------------------------------------------------------------
    // One recorded attempt: the path taken and how long it took.
    //
    // This is the substrate for everything else that was asked for - run
    // lines, ghosts, deltas, and eventually offline path analysis - so it
    // stores the full sampled path rather than only a duration. Samples
    // are cheap (16 bytes plus overhead) and a two-minute attempt at 30 Hz
    // is under 4000 of them.
    // ------------------------------------------------------------------
    public sealed class Attempt
    {
        public string AnchorLabel = "";

        /// Which route this was run on - see Segment.RouteFingerprint.
        /// Empty for attempts recorded before routes were tracked.
        public string Route = "";

        public DateTime RecordedUtc;
        public float Duration;
        public bool Completed;
        public readonly List<RunSample> Samples = new List<RunSample>();

        /// Names of the state channels, index-aligned with every
        /// StateSample.Values. Empty when state was not captured.
        public string[] Channels = new string[0];
        public readonly List<StateSample> States = new List<StateSample>();

        public int ChannelIndex(string name)
        {
            for (int i = 0; i < Channels.Length; i++)
                if (string.Equals(Channels[i], name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// Value of a channel at time t, using the most recent snapshot at
        /// or before t. Step rather than interpolated: several channels are
        /// booleans, and interpolating those would invent states that never
        /// happened.
        public bool TryStateAt(int channel, float t, out float value)
        {
            value = 0f;
            if (channel < 0 || States.Count == 0) return false;

            int best = -1;
            for (int i = 0; i < States.Count; i++)
            {
                if (States[i].T > t) break;
                best = i;
            }

            if (best < 0) return false;
            if (States[best].Values == null || channel >= States[best].Values.Length) return false;

            value = States[best].Values[channel];
            return true;
        }

        public int Count { get { return Samples.Count; } }

        public float TopSpeed
        {
            get
            {
                float best = 0f;
                for (int i = 0; i < Samples.Count; i++)
                    if (Samples[i].Speed > best) best = Samples[i].Speed;
                return best;
            }
        }

        public float PathLength
        {
            get
            {
                float d = 0f;
                for (int i = 1; i < Samples.Count; i++)
                    d += Vector3.Distance(Samples[i - 1].P, Samples[i].P);
                return d;
            }
        }
    }

    // ------------------------------------------------------------------
    // Comparison maths. Deliberately static and Unity-free apart from
    // Vector3 so it can be exercised by the unit tests in tests/ - this is
    // the part most likely to be quietly wrong, and the hardest to verify
    // by playing.
    // ------------------------------------------------------------------
    public static class RunCompare
    {
        /// Index of the reference sample spatially closest to `position`.
        ///
        /// `hint` is the previously matched index. Progress along a run is
        /// almost always monotonic, so the search starts there and only
        /// widens if nothing better is near - that turns an O(n) scan per
        /// frame into a short local one. Pass 0 for a cold search.
        ///
        /// Returns -1 when the reference has no samples.
        public static int ClosestIndex(IList<RunSample> reference, Vector3 position, int hint, int window)
        {
            if (reference == null || reference.Count == 0) return -1;
            if (window <= 0) window = reference.Count;

            int lo = Mathf.Max(0, hint - window);
            int hi = Mathf.Min(reference.Count - 1, hint + window);

            int best = lo;
            float bestSqr = float.MaxValue;

            for (int i = lo; i <= hi; i++)
            {
                float d = (reference[i].P - position).sqrMagnitude;
                if (d >= bestSqr) continue;
                bestSqr = d;
                best = i;
            }

            return best;
        }

        /// Seconds ahead (negative) or behind (positive) the reference at
        /// the current position. This is the ghost delta: "at the point
        /// you are standing, the reference run had taken N seconds".
        ///
        /// Returns false when no meaningful comparison exists.
        public static bool Delta(IList<RunSample> reference, Vector3 position, float elapsed,
                                 ref int hint, out float delta)
        {
            delta = 0f;

            int i = ClosestIndex(reference, position, hint, 120);
            if (i < 0) return false;

            hint = i;
            delta = elapsed - reference[i].T;
            return true;
        }

        /// Even split points through an attempt, for a splits-style
        /// readout when no explicit checkpoints exist. Returns the sample
        /// times at each fraction of the path length.
        public static float[] EvenSplits(Attempt attempt, int count)
        {
            if (attempt == null || count <= 0 || attempt.Samples.Count < 2)
                return new float[0];

            float total = attempt.PathLength;
            if (total <= 0f) return new float[0];

            float[] result = new float[count];
            float travelled = 0f;
            int next = 0;

            for (int i = 1; i < attempt.Samples.Count && next < count; i++)
            {
                travelled += Vector3.Distance(attempt.Samples[i - 1].P, attempt.Samples[i].P);

                while (next < count && travelled >= total * (next + 1) / (count + 1f))
                {
                    result[next] = attempt.Samples[i].T;
                    next++;
                }
            }

            // Any trailing entries get the final time rather than zero,
            // which would otherwise read as an impossibly fast split.
            for (int i = next; i < count; i++)
                result[i] = attempt.Duration;

            return result;
        }

        public static Attempt Best(IList<Attempt> attempts)
        {
            Attempt best = null;
            for (int i = 0; i < attempts.Count; i++)
            {
                if (!attempts[i].Completed) continue;
                if (best == null || attempts[i].Duration < best.Duration) best = attempts[i];
            }
            return best;
        }

        public static float AverageDuration(IList<Attempt> attempts)
        {
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < attempts.Count; i++)
            {
                if (!attempts[i].Completed) continue;
                sum += attempts[i].Duration;
                n++;
            }
            return n == 0 ? 0f : sum / n;
        }
    }

    // ------------------------------------------------------------------
    // Drives one attempt.
    //
    // The state machine is the Momentum/KSF shape: being placed at the
    // anchor ARMS a run, and the clock starts when you actually move,
    // not when you teleport. That way lining yourself up at the start
    // costs nothing, which is the whole point of a practice timer.
    // ------------------------------------------------------------------
    public sealed class RunRecorder
    {
        public enum RunState { Idle, Armed, Running }

        /// Distance from the anchor that counts as "you have started",
        /// used only when AutoStartOnLeavingRadius is set.
        public float StartRadius = 0.5f;

        /// When false the run waits for ForceStart. Segments drive the
        /// clock from their own start trigger, so leaving a radius is
        /// not the right signal for them.
        public bool AutoStartOnLeavingRadius;

        /// Position sampling rate. 30 Hz is enough to draw a smooth line
        /// and to place a ghost without storing a point per frame.
        public float SampleInterval = 1f / 30f;

        /// State sampling rate. Deliberately slower - see StateSample.
        public float StateInterval = 0.2f;

        public RunState State { get; private set; }
        public float Elapsed { get; private set; }
        public Attempt Current { get; private set; }

        private Vector3 _anchor;
        private string _anchorLabel = "";
        private float _nextSampleTime;
        private float _nextStateTime;

        /// Channel names for the run being recorded. Set before Arm.
        public string[] StateChannels = new string[0];

        /// Route fingerprint stamped onto the attempt. Set before Arm.
        public string Route = "";

        public void Arm(Vector3 anchor, string label)
        {
            _anchor = anchor;
            _anchorLabel = label;
            State = RunState.Armed;
            Elapsed = 0f;
            Current = null;
        }

        public void Abort()
        {
            State = RunState.Idle;
            Elapsed = 0f;
            Current = null;
        }

        /// Advance. `dt` should be unscaled: the game changes timeScale
        /// during its own sequences and a practice timer must not drift
        /// with it.
        ///
        /// `state` is the current channel values, or null when state
        /// capture is unavailable. It is COPIED when sampled, because the
        /// reader reuses its buffer.
        public void Tick(Vector3 position, float horizontalSpeed, float dt, float[] state)
        {
            // The radius rule is only a FALLBACK, for a run with no
            // start trigger. A segment starts when its trigger fires,
            // which the caller signals with ForceStart.
            if (State == RunState.Armed)
            {
                if (!AutoStartOnLeavingRadius) return;
                if ((position - _anchor).sqrMagnitude < StartRadius * StartRadius) return;
                BeginRun();
            }

            if (State != RunState.Running) return;

            Elapsed += dt;

            if (Elapsed < _nextSampleTime) return;
            _nextSampleTime = Elapsed + SampleInterval;

            RunSample s;
            s.T = Elapsed;
            s.P = position;
            s.Speed = horizontalSpeed;
            Current.Samples.Add(s);

            SampleState(state);
        }

        private void SampleState(float[] state)
        {
            if (state == null || state.Length == 0) return;
            if (Elapsed < _nextStateTime) return;
            _nextStateTime = Elapsed + StateInterval;

            StateSample ss;
            ss.T = Elapsed;

            // Copy: the reader hands back a reused buffer.
            ss.Values = new float[state.Length];
            Array.Copy(state, ss.Values, state.Length);

            Current.States.Add(ss);
        }

        /// Overload for callers with no state source (and for tests).
        public void Tick(Vector3 position, float horizontalSpeed, float dt)
        {
            Tick(position, horizontalSpeed, dt, null);
        }

        /// Start the clock now, from `position`. Used when a start
        /// trigger fires.
        public void ForceStart(Vector3 position)
        {
            if (State != RunState.Armed) return;

            _anchor = position;
            BeginRun();
        }

        private void BeginRun()
        {
            State = RunState.Running;
            Elapsed = 0f;
            _nextSampleTime = 0f;
            _nextStateTime = 0f;

            Current = new Attempt();
            Current.AnchorLabel = _anchorLabel;
            Current.RecordedUtc = DateTime.UtcNow;
            Current.Channels = StateChannels ?? new string[0];
            Current.Route = Route ?? "";

            RunSample s;
            s.T = 0f;
            s.P = _anchor;
            s.Speed = 0f;
            Current.Samples.Add(s);
        }

        /// Ends the attempt and returns it, or null if none was running.
        public Attempt Finish()
        {
            if (State != RunState.Running || Current == null) { Abort(); return null; }

            Current.Duration = Elapsed;
            Current.Completed = true;

            Attempt done = Current;
            State = RunState.Idle;
            Current = null;
            Elapsed = 0f;
            return done;
        }
    }
}
