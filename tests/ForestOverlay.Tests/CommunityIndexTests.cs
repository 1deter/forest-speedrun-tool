using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // Community packs come off the network: file names must never leave
    // the cache folder, the index hash must agree with
    // scripts/community-index.py, and a runner's own ids always win.
    // ------------------------------------------------------------------
    public class CommunityIndexTests
    {
        [Fact]
        public void HashMatchesTheIndexScript()
        {
            // python scripts/community-index.py --hash "<text>"
            Assert.Equal("6b7b51ef", CommunityIndex.HashOf("ForestOverlay segment 1\nid = x"));
            Assert.Equal("6b7b51ef", CommunityIndex.HashOf("﻿ForestOverlay segment 1\r\nid = x"));
            Assert.Equal("a1f9f539", CommunityIndex.HashOf("Café"));
        }

        [Fact]
        public void ParsesEntriesAndSkipsUnsafeOnes()
        {
            int bad;
            List<CommunityIndex.Entry> list = CommunityIndex.Parse(
                "# comment\n" +
                "cave5-practice.foseg 1A2B3C4D\n" +
                "\n" +
                "../evil.foseg 1a2b3c4d\n" +
                "sub/dir.foseg 1a2b3c4d\n" +
                "notes.txt 1a2b3c4d\n" +
                "short-hash.foseg 1a2b\n" +
                "cave5-practice.foseg 1a2b3c4d\n" +
                "red_elevator.v2.foseg 00ff00ff\n", out bad);

            Assert.Equal(2, list.Count);
            Assert.Equal("cave5-practice.foseg", list[0].File);
            Assert.Equal("1a2b3c4d", list[0].Hash);
            Assert.Equal("red_elevator.v2.foseg", list[1].File);
            Assert.Equal(5, bad);
        }

        [Theory]
        [InlineData("a.foseg", true)]
        [InlineData("A-b_c.1.foseg", true)]
        [InlineData("..foseg", false)]
        [InlineData(".hidden.foseg", false)]
        [InlineData("a..b.foseg", false)]
        [InlineData("C:x.foseg", false)]
        [InlineData("a\\b.foseg", false)]
        [InlineData("a.fosave", false)]
        [InlineData("", false)]
        public void SafeNames(string name, bool safe)
        {
            Assert.Equal(safe, CommunityIndex.SafeName(name));
        }

        [Fact]
        public void PlansDownloadsAndDrops()
        {
            int bad;
            List<CommunityIndex.Entry> index = CommunityIndex.Parse("a.foseg 11111111\nb.foseg 22222222\nc.foseg 33333333\n", out bad);
            Dictionary<string, string> cached = new Dictionary<string, string>
            {
                { "a.foseg", "11111111" },   // current
                { "b.foseg", "99999999" },   // changed upstream
                { "old.foseg", "44444444" }, // no longer listed
            };
            List<CommunityIndex.Entry> fetch = new List<CommunityIndex.Entry>();
            List<string> drop = new List<string>();
            CommunityIndex.Plan(index, new List<string>(cached.Keys),
                                delegate(string f) { string h; return cached.TryGetValue(f, out h) ? h : null; }, fetch, drop);

            Assert.Equal(new[] { "b.foseg", "c.foseg" }, fetch.ConvertAll(e => e.File));
            Assert.Equal(new[] { "old.foseg" }, drop);
        }

        private static SegmentBundle Bundle(string id, string category)
        {
            SegmentBundle b = new SegmentBundle();
            b.Segment = new Segment();
            b.Segment.Id = id;
            b.Segment.Name = id;
            b.Segment.Category = category;
            b.Segment.HasSpawn = true;
            b.Segment.SpawnPosition = new Vector3(1f, 2f, 3f);
            return b;
        }

        [Fact]
        public void SegmentFileIsCommunityAndYieldsToTheRunnersIds()
        {
            List<SegmentBundle> bundles = new List<SegmentBundle>
            {
                Bundle("deter/cave5", "Caves"),
                Bundle("deter/mine", "Caves"),
                Bundle("deter/cave5", "Caves"),   // listed twice
            };
            List<string> skipped = new List<string>();
            string text = CommunityIndex.SegmentFileText(bundles, new HashSet<string> { "deter/mine" }, skipped);

            List<Segment> back = SegmentFormat.ParseAll(text.Split('\n'), null);
            Assert.Single(back);
            Assert.Equal("deter/cave5", back[0].Id);
            Assert.Equal(CommunityIndex.Category, back[0].Category);
            Assert.Equal(new[] { "deter/mine", "deter/cave5" }, skipped);
            // The bundle keeps its own category for sub-categories later.
            Assert.Equal("Caves", bundles[0].Segment.Category);
        }
    }
}
