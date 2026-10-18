using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class AnimalSpawnsTests
    {
        private static AnimalSpawns.Pose P(int id, string prefab, int zone, float x)
        {
            AnimalSpawns.Pose p = new AnimalSpawns.Pose { Id = id, Prefab = prefab, Zone = zone };
            p.Pos = new[] { x, 78.5f, -4.25f };
            p.Rot = new[] { 0f, 0.5f, 0f, 0.866f };
            return p;
        }

        [Fact]
        public void PosesRoundTripUnderACommaCulture()
        {
            var old = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            try
            {
                string line = AnimalSpawns.FormatPoses(new[] { P(-12, "rabbitGo", 9, 428.5f), P(7, "deerGo", -1, -1f) });
                Assert.DoesNotContain("428,5", line);
                List<AnimalSpawns.Pose> back = AnimalSpawns.ParsePoses(line);
                Assert.Equal(2, back.Count);
                Assert.Equal(-12, back[0].Id);
                Assert.Equal("rabbitGo", back[0].Prefab);
                Assert.Equal(9, back[0].Zone);
                Assert.Equal(428.5f, back[0].Pos[0]);
                Assert.Equal(0.866f, back[0].Rot[3]);
                Assert.Equal(-1, back[1].Zone);
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = old; }
        }

        [Fact]
        public void BadPosesAreDropped()
        {
            Assert.Equal("", AnimalSpawns.FormatPoses(new[] { P(1, "a@b", 0, 0), P(2, "a,b", 0, 0), P(3, "", 0, 0) }));
            Assert.Empty(AnimalSpawns.ParsePoses(""));
            Assert.Empty(AnimalSpawns.ParsePoses(null));
            Assert.Empty(AnimalSpawns.ParsePoses("garbage;1:x@2@1,2@0,0,0,1;:a@1@1,2,3@0,0,0,1;4:a@z@1,2,3@0,0,0,1"));
            Assert.Single(AnimalSpawns.ParsePoses("garbage;4:rabbitGo@1@1,2,3@0,0,0,1"));
        }

        [Fact]
        public void ZonesRoundTripWithEmptyListsAndNegativeBacks()
        {
            AnimalSpawns.ZoneState a = new AnimalSpawns.ZoneState { Index = 0, Max = 8 };
            a.Ids.Add(-5); a.Ids.Add(6); a.Backs.Add(12.5f); a.Backs.Add(-3f);
            AnimalSpawns.ZoneState b = new AnimalSpawns.ZoneState { Index = 130, Max = 0 };
            string line = AnimalSpawns.FormatZones(new[] { a, b });
            List<AnimalSpawns.ZoneState> back = AnimalSpawns.ParseZones(line);
            Assert.Equal(2, back.Count);
            Assert.Equal(new[] { -5, 6 }, back[0].Ids.ToArray());
            Assert.Equal(new[] { 12.5f, -3f }, back[0].Backs.ToArray());
            Assert.Equal(8, back[0].Max);
            Assert.Empty(back[1].Ids);
            Assert.Empty(back[1].Backs);
            Assert.Equal(130, back[1].Index);
            Assert.Empty(AnimalSpawns.ParseZones("junk;1:2;x:1@a@b"));
        }

        [Fact]
        public void CountsRoundTrip()
        {
            string line = AnimalSpawns.FormatCounts(new[]
            {
                new KeyValuePair<string, int>("currRabbitAmount", -2),
                new KeyValuePair<string, int>("maxRabbitAmount", 12),
            });
            List<KeyValuePair<string, int>> back = AnimalSpawns.ParseCounts(line);
            Assert.Equal(2, back.Count);
            Assert.Equal("currRabbitAmount", back[0].Key);
            Assert.Equal(-2, back[0].Value);
            Assert.Empty(AnimalSpawns.ParseCounts("x;=3;y=z"));
        }

        [Fact]
        public void PlanSplitsDespawnRespawnAndMove()
        {
            AnimalSpawns.Plan plan = AnimalSpawns.MakePlan(new[] { 1, 2, 3 }, new[] { 2, 3, 9 }, null);
            Assert.Equal(new[] { 9 }, plan.Despawn.ToArray());
            Assert.Equal(new[] { 1 }, plan.Respawn.ToArray());
            Assert.Equal(new[] { 2, 3 }, plan.Move.ToArray());
        }

        [Fact]
        public void PlanReadsAStandInAsTheCapturedAnimal()
        {
            Dictionary<int, int> map = new Dictionary<int, int> { { 50, 1 } };
            AnimalSpawns.Plan plan = AnimalSpawns.MakePlan(new[] { 1, 2 }, new[] { 50, 2 }, map);
            Assert.Empty(plan.Despawn);
            Assert.Empty(plan.Respawn);
            Assert.Equal(new[] { 1, 2 }, plan.Move.ToArray());
            // two live roots for one captured animal: the second goes
            plan = AnimalSpawns.MakePlan(new[] { 1 }, new[] { 1, 50 }, map);
            Assert.Equal(new[] { 50 }, plan.Despawn.ToArray());
        }

        [Fact]
        public void DescribeNamesEachPartAndIsEmptyWhenNothingHappened()
        {
            Assert.Equal("", AnimalSpawns.Describe("", 0, 0, 0, 0));
            Assert.Equal("animals: 1 brought back, 1 removed (spawned since), 3 moved back, 2 zone(s) reset",
                AnimalSpawns.Describe("", 1, 1, 3, 2));
            Assert.Equal("animals: 2 put back (health x1), 1 brought back",
                AnimalSpawns.Describe("animals: 2 put back (health x1)", 1, 0, 0, 0));
        }
    }
}
