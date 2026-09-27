using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // The run's item track (v0.24.161): only count changes are recorded,
    // the source is asked on the state cadence and may answer "unchanged",
    // and the `i|` lines round-trip.
    // ------------------------------------------------------------------
    public class ItemTrackTests
    {
        private sealed class FakeBag
        {
            public readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
            public bool Dirty = true;
            public int Asked, Filled;

            public bool Fill(Dictionary<string, int> into, bool force)
            {
                Asked++;
                if (!force && !Dirty) return false;
                Dirty = false;
                Filled++;
                foreach (KeyValuePair<string, int> kv in Counts) into[kv.Key] = kv.Value;
                return true;
            }
        }

        private static void Run(RunRecorder r, float seconds)
        {
            for (float t = 0; t < seconds; t += 1f / 30f) r.Tick(Vector3.zero, 0f, 1f / 30f);
        }

        [Fact]
        public void RecordsTheStartingBagThenOnlyChanges()
        {
            var bag = new FakeBag();
            bag.Counts["Soda"] = 3;
            bag.Counts["Coins"] = 0;   // owned at 0: not worth a line
            var r = new RunRecorder { ItemSource = bag.Fill };
            r.Arm(Vector3.zero, "s-test");
            r.ForceStart(Vector3.zero);

            Run(r, 1f);                                    // unchanged: nothing new
            bag.Counts["Soda"] = 2; bag.Counts["EnergyMix"] = 1; bag.Dirty = true;
            Run(r, 1f);
            bag.Counts.Remove("EnergyMix"); bag.Dirty = true;   // used up: gone from the list
            Run(r, 1f);

            Attempt a = r.Finish();
            Assert.Equal(4, a.Items.Count);
            Assert.Equal("Soda", a.Items[0].Name); Assert.Equal(3, a.Items[0].Count); Assert.True(a.Items[0].T < 0.1f);   // the first frame
            Assert.Equal(2, a.ItemCountAt("Soda", 1.5f));
            Assert.Equal(1, a.ItemCountAt("EnergyMix", 1.5f));
            Assert.Equal(0, a.ItemCountAt("EnergyMix", 2.9f));
            Assert.Equal(3, bag.Filled);                   // start + two changes, never the quiet samples
            Assert.True(bag.Asked > 10);
        }

        [Fact]
        public void EachRunStartsFromAFullRead()
        {
            var bag = new FakeBag();
            bag.Counts["Rock"] = 5;
            var r = new RunRecorder { ItemSource = bag.Fill };
            r.Arm(Vector3.zero, "s-test"); r.ForceStart(Vector3.zero); Run(r, 0.5f); r.Finish();

            r.Arm(Vector3.zero, "s-test"); r.ForceStart(Vector3.zero); Run(r, 0.5f);
            Attempt second = r.Finish();
            Assert.Single(second.Items);                   // the bag again, though nothing changed
            Assert.Equal(5, second.Items[0].Count);
        }

        [Fact]
        public void ItemLinesRoundTrip()
        {
            var a = new Attempt { AnchorLabel = "s-test", Duration = 2f };
            a.Samples.Add(new RunSample { T = 0f, P = Vector3.zero });
            a.Items.Add(new ItemChange { T = 0f, Name = "Soda", Count = 3 });
            a.Items.Add(new ItemChange { T = 0f, Name = "Odd:Name", Count = 1 });
            a.Items.Add(new ItemChange { T = 1.4f, Name = "Soda", Count = 2 });

            string text = AttemptFormat.Write(a);
            Assert.Contains("i|0.000|Soda:3|Odd:Name:1", text);
            Assert.Contains("i|1.400|Soda:2", text);

            Attempt back = AttemptFormat.Parse(text.Split('\n'));
            Assert.Equal(3, back.Items.Count);
            Assert.Equal("Odd:Name", back.Items[1].Name);
            Assert.Equal(2, back.ItemCountAt("Soda", 2f));
        }
    }
}
