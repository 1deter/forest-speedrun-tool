using System.Collections.Generic;
using System.Text;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    public class InputTracksTests
    {
        private static readonly string[] Ends = { "Vertical+", "Vertical-", "Run", "Jump" };

        private static KeyTrack Track()
        {
            KeyTrack k = new KeyTrack(Ends);
            k.Add(0f, 0, true);
            k.Add(0.5f, 2, true);
            k.Add(1f, 3, true);
            k.Add(1.2f, 3, false);
            k.Add(2f, 0, false);
            k.Add(2f, 2, false);
            return k;
        }

        private static string Label(string end)
        {
            int sign;
            return InputActionTable.Label(KeyTrack.ActionOf(end, out sign), sign);
        }

        [Fact]
        public void HeldEndsInPressOrderWithTheOverlayWords()
        {
            KeyTrack k = Track();
            KeyTrack.Cache c = new KeyTrack.Cache();
            System.Func<string, string> label = Label;
            Assert.Equal("Keys: Forward", c.Text(k, 0.2f, label));
            Assert.Equal("Keys: Forward  Run  Jump", c.Text(k, 1.1f, label));
            Assert.Equal("Keys: Forward  Run", c.Text(k, 1.5f, label));
            Assert.Equal(KeyTrack.KeysNone, c.Text(k, 3f, label));
            Assert.Equal(KeyTrack.KeysNone, c.Text(k, -1f, label));
        }

        [Fact]
        public void HeldAtTheEdges()
        {
            KeyTrack k = Track();
            KeyTrack.Cache c = new KeyTrack.Cache();
            c.Seek(k, -0.1f);
            Assert.False(c.Held(0));
            c.Seek(k, 0f);                    // exactly at the press
            Assert.True(c.Held(0));
            c.Seek(k, 1.2f);                  // exactly at the release
            Assert.False(c.Held(3));
            Assert.True(c.Held(2));
            c.Seek(k, 0.6f);                  // back: starts over
            Assert.True(c.Held(2));
            Assert.False(c.Held(3));
            Assert.False(c.Held(99));
        }

        [Fact]
        public void TextIsRebuiltOnlyWhenTheSetChanges()
        {
            KeyTrack k = Track();
            KeyTrack.Cache c = new KeyTrack.Cache();
            c.Text(k, 0.1f, null); c.Text(k, 0.2f, null); c.Text(k, 0.4f, null);
            Assert.Equal(1, c.Builds);
            Assert.Equal("Keys: Vertical+  Run", c.Text(k, 0.6f, null));
            Assert.Equal(2, c.Builds);
        }

        [Fact]
        public void ActionOfSplitsTheSign()
        {
            int s;
            Assert.Equal("Vertical", KeyTrack.ActionOf("Vertical-", out s));
            Assert.Equal(-1, s);
            Assert.Equal("Jump", KeyTrack.ActionOf("Jump", out s));
            Assert.Equal(1, s);
        }

        [Fact]
        public void AReleaseWithNoPressChangesNothing()
        {
            KeyTrack k = new KeyTrack(Ends);
            k.Add(0f, 3, false);
            k.Add(0.1f, 2, true);
            Assert.Equal("Keys: Run", new KeyTrack.Cache().Text(k, 1f, null));
        }

        [Fact]
        public void ParseSkipsMalformedEntries()
        {
            KeyTrack k = new KeyTrack(Ends);
            k.ParseChange("kc|x|0=1".Split('|'));
            k.ParseChange("kc|1|0|=1|2=7|9=1|3=1|1=10".Split('|'));
            Assert.Single(k.Changes);
            Assert.Equal(3, k.Changes[0].Index);
        }

        [Fact]
        public void AFullTrackKeepsNoMore()
        {
            KeyTrack k = new KeyTrack(Ends);
            for (int i = 0; i < KeyTrack.MaxChanges; i++) k.Add(i, 0, i % 2 == 0);
            Assert.False(k.Add(1e6f, 0, true));
            Assert.Equal(KeyTrack.MaxChanges, k.Changes.Count);
        }

        [Fact]
        public void QuickSlotsKeptOnlyWhenTheyDiffer()
        {
            QuickSlotTrack q = new QuickSlotTrack();
            Assert.True(q.Add(0f, new[] { 80, 0, 0, 0 }));
            Assert.False(q.Add(0.2f, new[] { 80, 0, 0, 0 }));
            Assert.False(q.Add(0.4f, new[] { 80, -1, 0, 0 }));   // -1 and 0 are both empty
            Assert.True(q.Add(0.6f, new[] { 80, 0, 53, 0 }));
            Assert.Equal(2, q.Samples.Count);
            Assert.Equal(-1, q.IndexAt(-0.1f));
            Assert.Equal(0, q.IndexAt(0.59f));
            Assert.Equal(1, q.IndexAt(0.6f));
        }

        [Fact]
        public void QuickSlotTextNamesTheItems()
        {
            QuickSlotTrack q = new QuickSlotTrack();
            q.Add(0f, new[] { 80, 0, 53, 7 });
            System.Func<int, string> name = id => id == 80 ? "Axe Plane" : id == 53 ? "Rock" : null;
            Assert.Equal("Slots: 1 Axe Plane  2 -  3 Rock  4 #7", q.Text(0, name));
            Assert.Equal("Slots: none recorded yet", q.Text(-1, name));
        }

        [Fact]
        public void TheRunFileCarriesTheTracks()
        {
            Attempt a = new Attempt();
            a.Samples.Add(new RunSample { T = 0f });
            a.Keys = Track();
            a.QuickSlots = new QuickSlotTrack();
            a.QuickSlots.Add(0f, new[] { 80, 0, 0, 0 });
            a.QuickSlots.Add(1.4f, new[] { 80, 0, 53, 0 });
            string text = AttemptFormat.Write(a);
            Assert.Contains("keys|Vertical+|Vertical-|Run|Jump\n", text);
            Assert.Contains("kc|2|0=0|2=0\n", text);
            Assert.Contains("qs|1.4|80|0|53|0\n", text);

            Attempt back = AttemptFormat.Parse(text.Split('\n'));
            Assert.Equal(Ends, back.Keys.Names);
            Assert.Equal(6, back.Keys.Changes.Count);
            Assert.Equal(2, back.QuickSlots.Samples.Count);
            Assert.Equal(53, back.QuickSlots.Samples[1].S3);
        }

        [Fact]
        public void AnOldFileHasNoTracks()
        {
            Attempt back = AttemptFormat.Parse("duration|1\ns|0|0|0|0|0\ns|1|1|0|0|1".Split('\n'));
            Assert.Null(back.Keys);
            Assert.Null(back.QuickSlots);
        }

        [Fact]
        public void WithRunnerGoesBeforeTheInputLines()
        {
            string text = "anchor|recordings\nduration|1\nkeys|Jump\nkc|0|0=1\nqs|0|1|0|0|0\n";
            string named = AttemptFormat.WithRunner(text, "abc", "Runner");
            Assert.True(named.IndexOf("runner|abc|Runner", System.StringComparison.Ordinal) < named.IndexOf("keys|", System.StringComparison.Ordinal));
        }

        [Fact]
        public void TheRecorderKeepsAOneFramePress()
        {
            RunRecorder r = new RunRecorder();
            int frame = 0;
            r.InputNames = () => new[] { "Jump", "Run" };
            r.InputSource = held => { held[0] = frame == 2; held[1] = frame >= 1; return true; };
            r.Arm(Vector3.zero, "x");
            r.ForceStart(Vector3.zero);
            for (frame = 0; frame < 6; frame++) r.Tick(Vector3.zero, 0f, 0.01f);
            List<KeyChange> c = r.Current.Keys.Changes;
            Assert.Equal(3, c.Count);                        // Run on, Jump on, Jump off: changes only
            Assert.Equal(1, c[0].Index);
            Assert.True(c[1].Down && c[1].Index == 0);
            Assert.True(!c[2].Down && c[2].Index == 0);
        }

        [Fact]
        public void TheRecorderWaitsForTheActions()
        {
            RunRecorder r = new RunRecorder();
            bool ready = false;
            r.InputNames = () => ready ? new[] { "Jump" } : new string[0];
            r.InputSource = held => { held[0] = true; return true; };
            r.Arm(Vector3.zero, "x");
            r.ForceStart(Vector3.zero);
            r.Tick(Vector3.zero, 0f, 0.01f);
            Assert.Null(r.Current.Keys);
            ready = true;
            r.Tick(Vector3.zero, 0f, 0.01f);
            Assert.Single(r.Current.Keys.Changes);
        }

        [Fact]
        public void TheRecorderTakesQuickSlotsOnTheStateCadence()
        {
            RunRecorder r = new RunRecorder();
            int id = 80;
            r.QuickSlotSource = ids => { ids[0] = id; ids[1] = 0; ids[2] = 0; ids[3] = 0; return true; };
            r.Arm(Vector3.zero, "x");
            r.ForceStart(Vector3.zero);
            for (int i = 0; i < 10; i++) r.Tick(Vector3.zero, 0f, 0.1f);
            id = 53;
            for (int i = 0; i < 10; i++) r.Tick(Vector3.zero, 0f, 0.1f);
            Assert.Equal(2, r.Current.QuickSlots.Samples.Count);
        }

        [Fact]
        public void TheTableNamesTheEnds()
        {
            InputActionTable t = new InputActionTable();
            int v = t.AddAction("Vertical", 0);
            int j = t.AddAction("Jump", 0);
            t.AddAction("Mouse X", 0);
            t.AddBinding("W", v, 1);
            t.AddBinding("S", v, -1);
            t.AddBinding("Space", j, 1);
            List<int> a = new List<int>(), s = new List<int>();
            string[] ends = t.Ends(new[] { "Mouse X" }, a, s);
            Assert.Equal(new[] { "Vertical+", "Vertical-", "Jump" }, ends);
            Assert.Equal(new[] { 0, 0, 1 }, a.ToArray());
            Assert.Equal(new[] { 1, -1, 1 }, s.ToArray());
        }
    }
}
