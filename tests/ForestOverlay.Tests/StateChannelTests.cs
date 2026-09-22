using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // State channels are the "record everything, filter later" track.
    // Lookup by name and value-at-time are what the viewer will lean on,
    // so they are pinned here.
    // ------------------------------------------------------------------
    public class StateChannelTests
    {
        private static Attempt WithStates()
        {
            Attempt a = new Attempt();
            a.Channels = new string[] { "Health", "Stamina", "Cold" };

            a.States.Add(Sample(0f, 100f, 50f, 0f));
            a.States.Add(Sample(1f, 90f, 40f, 0f));
            a.States.Add(Sample(2f, 80f, 30f, 1f));
            return a;
        }

        private static StateSample Sample(float t, params float[] values)
        {
            StateSample s;
            s.T = t;
            s.Values = values;
            return s;
        }

        [Fact]
        public void ChannelIndexFindsByNameIgnoringCase()
        {
            Attempt a = WithStates();

            Assert.Equal(0, a.ChannelIndex("Health"));
            Assert.Equal(1, a.ChannelIndex("stamina"));
            Assert.Equal(2, a.ChannelIndex("COLD"));
            Assert.Equal(-1, a.ChannelIndex("NotAChannel"));
        }

        [Fact]
        public void StateAtReturnsTheMostRecentSnapshot()
        {
            Attempt a = WithStates();
            int health = a.ChannelIndex("Health");
            float v;

            Assert.True(a.TryStateAt(health, 0f, out v));
            Assert.Equal(100f, v, 3);

            // Between snapshots: takes the earlier one, it does not
            // interpolate. Several channels are booleans and interpolating
            // those would invent states that never happened.
            Assert.True(a.TryStateAt(health, 1.7f, out v));
            Assert.Equal(90f, v, 3);

            Assert.True(a.TryStateAt(health, 2f, out v));
            Assert.Equal(80f, v, 3);
        }

        [Fact]
        public void BooleanChannelsStayExact()
        {
            Attempt a = WithStates();
            int cold = a.ChannelIndex("Cold");
            float v;

            a.TryStateAt(cold, 1.9f, out v);
            Assert.Equal(0f, v, 3);     // not 0.9 - no interpolation

            a.TryStateAt(cold, 2.0f, out v);
            Assert.Equal(1f, v, 3);
        }

        [Fact]
        public void StateAtClampsToTheLastSnapshot()
        {
            Attempt a = WithStates();
            float v;

            Assert.True(a.TryStateAt(0, 99f, out v));
            Assert.Equal(80f, v, 3);
        }

        [Fact]
        public void StateAtFailsBeforeTheFirstSnapshotAndOnBadInput()
        {
            Attempt a = WithStates();
            float v;

            // The first snapshot is at t=0, so nothing precedes it.
            Attempt later = new Attempt();
            later.Channels = a.Channels;
            later.States.Add(Sample(5f, 1f, 2f, 3f));
            Assert.False(later.TryStateAt(0, 1f, out v));

            Assert.False(a.TryStateAt(-1, 1f, out v));       // bad channel
            Assert.False(a.TryStateAt(99, 1f, out v));       // out of range
            Assert.False(new Attempt().TryStateAt(0, 1f, out v));  // no states
        }

        [Fact]
        public void RecorderCopiesStateRatherThanAliasingTheBuffer()
        {
            // PlayerStateReader reuses one array between reads, so the
            // recorder must copy. Aliasing would make every stored sample
            // show the newest values.
            RunRecorder r = new RunRecorder();
            r.SampleInterval = 0f;
            r.StateInterval = 0f;
            r.StateChannels = new string[] { "Health" };
            r.Arm(Vector3.zero, "test");
            r.ForceStart(Vector3.zero);

            float[] shared = new float[] { 100f };

            r.Tick(new Vector3(2f, 0f, 0f), 1f, 0.1f, shared);
            shared[0] = 50f;
            r.Tick(new Vector3(3f, 0f, 0f), 1f, 0.1f, shared);

            Attempt done = r.Finish();

            Assert.Equal(2, done.States.Count);
            Assert.Equal(100f, done.States[0].Values[0], 3);
            Assert.Equal(50f, done.States[1].Values[0], 3);
        }

        [Fact]
        public void ChannelsAreCarriedOntoTheAttempt()
        {
            RunRecorder r = new RunRecorder();
            r.SampleInterval = 0f;
            r.StateChannels = new string[] { "Health", "Stamina" };
            r.Arm(Vector3.zero, "test");
            r.ForceStart(Vector3.zero);

            r.Tick(new Vector3(2f, 0f, 0f), 1f, 0.1f, new float[] { 1f, 2f });

            Assert.Equal(new string[] { "Health", "Stamina" }, r.Current.Channels);
        }

        [Fact]
        public void NoStateSourceIsHarmless()
        {
            RunRecorder r = new RunRecorder();
            r.SampleInterval = 0f;
            r.Arm(Vector3.zero, "test");
            r.ForceStart(Vector3.zero);

            r.Tick(new Vector3(2f, 0f, 0f), 1f, 0.1f, null);
            Attempt done = r.Finish();

            Assert.Empty(done.States);
            Assert.NotNull(done.Channels);
        }
    }
}
