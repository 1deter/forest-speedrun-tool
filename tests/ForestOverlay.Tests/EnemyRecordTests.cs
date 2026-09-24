using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // Cannibals recorded at a capture are put back by moving the game's
    // own respawned ones. A wrong match splits a family (a leader without
    // its followers) or moves the wrong kind of enemy.
    // ------------------------------------------------------------------
    public class EnemyRecordTests
    {
        private static EnemyRecord Cap(int family, string type)
        {
            EnemyRecord r = new EnemyRecord();
            r.Family = family;
            r.Type = type;
            r.Position = new Vector3(1f, 2f, 3f);
            r.Health = 100;
            return r;
        }

        private static EnemyRecord.Live L(int family, string type)
        {
            EnemyRecord.Live l;
            l.Family = family;
            l.Type = type;
            return l;
        }

        [Fact]
        public void RoundTrip()
        {
            EnemyRecord r = new EnemyRecord();
            r.Family = 3;
            r.Type = "regularMale";
            r.Position = new Vector3(524.13f, -54.4f, 0.2f);
            r.Yaw = 90.5f;
            r.Health = 130;
            string text = r.Encode();
            Assert.Equal("3:regularMale@524.13,-54.4,0.2/90.5/130", text);

            EnemyRecord back;
            Assert.True(EnemyRecord.TryDecode(text, out back));
            Assert.Equal(3, back.Family);
            Assert.Equal("regularMale", back.Type);
            Assert.Equal(-54.4f, back.Position.y, 3);
            Assert.Equal(90.5f, back.Yaw, 3);
            Assert.Equal(130, back.Health);
        }

        [Fact]
        public void AsleepAndKindWithSlashesRoundTrip()
        {
            EnemyRecord r = new EnemyRecord();
            r.Family = 0;
            r.Type = "mutant_male/0/L";
            r.Position = new Vector3(1f, 2f, 3f);
            r.Health = 130;
            r.Asleep = true;
            string text = r.Encode();
            Assert.Equal("0:mutant_male/0/L@1,2,3/0/130/s", text);

            EnemyRecord back;
            Assert.True(EnemyRecord.TryDecode(text, out back));
            Assert.Equal("mutant_male/0/L", back.Type);
            Assert.True(back.Asleep);
            Assert.Equal(130, back.Health);

            Assert.True(EnemyRecord.TryDecode("0:mutant_male/0@1,2,3/0/91", out back));
            Assert.False(back.Asleep);
        }

        [Theory]
        [InlineData("")]
        [InlineData("1:regularMale@1,2,3/0/1/x")]
        [InlineData("regularMale@1,2,3/0/1")]
        [InlineData("x:regularMale@1,2,3/0/1")]
        [InlineData("1:@1,2,3/0/1")]
        [InlineData("1:regularMale@1,2/0/1")]
        [InlineData("1:regularMale@1,2,3/0")]
        public void BadEntriesAreRefused(string text)
        {
            EnemyRecord r;
            Assert.False(EnemyRecord.TryDecode(text, out r));
        }

        [Fact]
        public void FamilyRoundTrip()
        {
            FamilyRecord f = new FamilyRecord();
            f.Index = 2;
            f.Position = new Vector3(522.9f, 56.74f, -10.7f);
            f.Yaw = 45f;
            f.List = "allRegularSpawns";
            f.Settings.Add(new KeyValuePair<string, string>("amount_male", "2"));
            f.Settings.Add(new KeyValuePair<string, string>("leader", "True"));
            f.Settings.Add(new KeyValuePair<string, string>("range", "1.5"));
            string text = f.Encode();
            Assert.Equal("2|522.9,56.74,-10.7|45|allRegularSpawns|amount_male=2,leader=True,range=1.5", text);

            FamilyRecord back;
            Assert.True(FamilyRecord.TryDecode(text, out back));
            Assert.Equal(2, back.Index);
            Assert.Equal(-10.7f, back.Position.z, 3);
            Assert.Equal("allRegularSpawns", back.List);
            Assert.Equal(3, back.Settings.Count);
            Assert.Equal("leader", back.Settings[1].Key);
            Assert.Equal("True", back.Settings[1].Value);
        }

        [Theory]
        [InlineData("")]
        [InlineData("0|1,2,3|0|list")]
        [InlineData("x|1,2,3|0|list|a=1")]
        [InlineData("0|1,2|0|list|a=1")]
        [InlineData("0|1,2,3|0|list|=1")]
        public void BadFamiliesAreRefused(string text)
        {
            FamilyRecord f;
            Assert.False(FamilyRecord.TryDecode(text, out f));
        }

        [Fact]
        public void FamilyWithNoSettingsIsValid()
        {
            FamilyRecord f;
            Assert.True(FamilyRecord.TryDecode("0|1,2,3|0|allCaveSpawns|", out f));
            Assert.Empty(f.Settings);
        }

        [Fact]
        public void WholeFamiliesKeepTheirMembersTogether()
        {
            // Captured: family 0 = two males, family 1 = male + female.
            List<EnemyRecord> cap = new List<EnemyRecord> { Cap(0, "m"), Cap(1, "m"), Cap(0, "m"), Cap(1, "f") };
            // Live: family 7 = male + female, family 9 = two males.
            List<EnemyRecord.Live> live = new List<EnemyRecord.Live> { L(9, "m"), L(7, "f"), L(9, "m"), L(7, "m") };

            int whole;
            int[] m = EnemyRecord.Match(cap, live, out whole);
            Assert.Equal(2, whole);
            Assert.Equal(9, live[m[0]].Family);
            Assert.Equal(9, live[m[2]].Family);
            Assert.Equal(7, live[m[1]].Family);
            Assert.Equal("m", live[m[1]].Type);
            Assert.Equal(1, m[3]);
            Assert.NotEqual(m[0], m[2]);
        }

        [Fact]
        public void LeftoversMatchByTypeAndNeverReuse()
        {
            // No live family has the captured make-up; take by type.
            List<EnemyRecord> cap = new List<EnemyRecord> { Cap(0, "m"), Cap(0, "f"), Cap(0, "pale") };
            List<EnemyRecord.Live> live = new List<EnemyRecord.Live> { L(1, "m"), L(2, "f"), L(2, "m") };

            int whole;
            int[] m = EnemyRecord.Match(cap, live, out whole);
            Assert.Equal(0, whole);
            Assert.Equal(0, m[0]);
            Assert.Equal(1, m[1]);
            Assert.Equal(-1, m[2]);
        }

        [Fact]
        public void LiveFamiliesMatchedWholeAreNotRaidedForLeftovers()
        {
            List<EnemyRecord> cap = new List<EnemyRecord> { Cap(0, "m"), Cap(1, "m") };
            List<EnemyRecord.Live> live = new List<EnemyRecord.Live> { L(5, "m") };

            int whole;
            int[] m = EnemyRecord.Match(cap, live, out whole);
            Assert.Equal(1, whole);
            Assert.Equal(0, m[0]);
            Assert.Equal(-1, m[1]);
        }
    }
}
