using System.Collections.Generic;
using System.Text;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class KeyTrackTests
    {
        private static KeyTrack Track()
        {
            KeyTrack k = new KeyTrack();
            k.Add(0f, "W", true);
            k.Add(0.5f, "Shift", true);
            k.Add(1f, "Space", true);
            k.Add(1.2f, "Space", false);
            k.Add(2f, "W", false);
            k.Add(2f, "Shift", false);
            return k;
        }

        [Fact]
        public void HeldKeysInPressOrder()
        {
            KeyTrack k = Track();
            KeyTrack.Cache c = new KeyTrack.Cache();
            Assert.Equal("Keys: W", c.Get(k, 0.2f));
            Assert.Equal("Keys: W  Shift  Space", c.Get(k, 1.1f));
            Assert.Equal("Keys: W  Shift", c.Get(k, 1.5f));
            Assert.Equal(KeyTrack.KeysNone, c.Get(k, 3f));
            Assert.Equal(KeyTrack.KeysNone, c.Get(k, -1f));
        }

        [Fact]
        public void CacheWalksForwardAndStartsOverAfterASeekBack()
        {
            KeyTrack k = Track();
            KeyTrack.Cache c = new KeyTrack.Cache();
            c.Get(k, 0.1f); c.Get(k, 0.2f);
            Assert.Equal(1, c.Builds);
            c.Get(k, 1.1f);
            Assert.Equal("Keys: W", c.Get(k, 0.3f));
            Assert.Equal(3, c.Builds);
        }

        [Fact]
        public void AReleaseWithNoPressChangesNothing()
        {
            KeyTrack k = new KeyTrack();
            k.Add(0f, "M1", false);
            k.Add(0.1f, "A", true);
            Assert.Equal("Keys: A", new KeyTrack.Cache().Get(k, 1f));
        }

        [Fact]
        public void WriteAndParseRoundTrip()
        {
            StringBuilder sb = new StringBuilder();
            Track().Write(sb, '\n');
            string text = sb.ToString();
            Assert.Contains("k|2|-W|-Shift\n", text);
            KeyTrack back = new KeyTrack();
            foreach (string line in text.Split('\n'))
                if (line.Length > 0) back.Parse(line.Split('|'));
            Assert.Equal(6, back.Changes.Count);
            Assert.Equal("Keys: W  Shift  Space", new KeyTrack.Cache().Get(back, 1.1f));
        }

        [Fact]
        public void TheRunFileCarriesTheTrack()
        {
            Attempt a = new Attempt();
            a.Samples.Add(new RunSample { T = 0f });
            a.Keys = Track();
            Attempt back = AttemptFormat.Parse(AttemptFormat.Write(a).Split('\n'));
            Assert.NotNull(back.Keys);
            Assert.Equal(6, back.Keys.Changes.Count);

            Attempt timed = new Attempt();
            timed.Samples.Add(new RunSample { T = 0f });
            string text = AttemptFormat.Write(timed);
            Assert.DoesNotContain("k|", text);
            Assert.Null(AttemptFormat.Parse(text.Split('\n')).Keys);
        }

        [Fact]
        public void ParseSkipsMalformedEntries()
        {
            KeyTrack k = new KeyTrack();
            k.Parse("k|x|+W".Split('|'));
            k.Parse("k|1|W|+|+E".Split('|'));
            Assert.Single(k.Changes);
            Assert.Equal("E", k.Changes[0].Key);
        }

        [Fact]
        public void AFullTrackKeepsNoMore()
        {
            KeyTrack k = new KeyTrack();
            for (int i = 0; i < KeyTrack.MaxChanges; i++) k.Add(i, "W", i % 2 == 0);
            Assert.False(k.Add(1e6f, "W", true));
            Assert.Equal(KeyTrack.MaxChanges, k.Changes.Count);
        }
    }
}
