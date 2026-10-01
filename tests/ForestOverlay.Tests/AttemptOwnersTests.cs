using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class AttemptOwnersTests
    {
        private static Attempt Run(string runner, string name, float duration, bool done = true, float[] splits = null)
        {
            Attempt a = new Attempt();
            a.RunnerId = runner;
            a.RunnerName = name;
            a.Duration = duration;
            a.Completed = done;
            if (splits != null) a.Splits = splits;
            return a;
        }

        [Fact]
        public void OwnIsMyIdOrNoIdAtAll()
        {
            Assert.True(AttemptOwners.IsOwn("r-me", "r-me"));
            Assert.True(AttemptOwners.IsOwn("", "r-me"));       // before v0.24.146
            Assert.True(AttemptOwners.IsOwn(null, "r-me"));
            Assert.False(AttemptOwners.IsOwn("r-maks", "r-me"));
        }

        [Fact]
        public void EachOtherRunnersBestFastestFirst()
        {
            List<Attempt> others = new List<Attempt>
            {
                Run("r-maks", "maks", 50f, true, new[] { 10f, 30f }),
                Run("r-tom", "Tom", 45f),
                Run("r-maks", "maks", 47f, true, new[] { 9f, 28f }),
                Run("r-maks", "maks", 40f, false),                    // not finished
                Run("r-sx", "", 60f, true, new[] { 1f }),             // wrong split count
            };
            List<Attempt> runs = new List<Attempt>();
            List<BoardEntry> list = AttemptOwners.OthersBest(others, 2, runs);

            Assert.Equal(3, list.Count);
            Assert.Equal(new[] { "r-tom", "r-maks", "r-sx" }, new[] { list[0].RunnerId, list[1].RunnerId, list[2].RunnerId });
            Assert.Equal(-1, list[0].RunId);
            Assert.Equal(-2, list[1].RunId);
            Assert.Equal(47f, list[1].Duration);
            Assert.Equal(new[] { 9f, 28f, 47f }, list[1].Splits);
            Assert.True(float.IsNaN(list[0].Splits[0]));
            Assert.Equal(45f, list[0].Splits[2]);
            Assert.Equal("a runner", list[2].Name);
            Assert.Same(others[2], runs[1]);
        }

        [Fact]
        public void MergeKeepsTheFasterEntryPerRunner()
        {
            List<BoardEntry> site = new List<BoardEntry>
            {
                new BoardEntry { RunId = 7, RunnerId = "r-maks", Name = "maks", Duration = 44f },
                new BoardEntry { RunId = 9, RunnerId = "r-tom", Name = "Tom", Duration = 52f },
            };
            List<BoardEntry> local = new List<BoardEntry>
            {
                new BoardEntry { RunId = -1, RunnerId = "r-tom", Name = "Tom", Duration = 45f },
                new BoardEntry { RunId = -2, RunnerId = "r-maks", Name = "maks", Duration = 47f },
                new BoardEntry { RunId = -3, RunnerId = "r-sx", Name = "sx", Duration = 60f },
            };
            List<BoardEntry> m = AttemptOwners.Merge(site, local);
            Assert.Equal(3, m.Count);
            Assert.Equal(7, m[0].RunId);      // maks: the site's 44 beats the file's 47
            Assert.Equal(-1, m[1].RunId);     // Tom: the file's 45 beats the site's 52
            Assert.Equal(-3, m[2].RunId);
            Assert.Equal(2, AttemptOwners.Merge(null, local.GetRange(0, 2)).Count);
        }

        [Fact]
        public void RunnerIdComesFromTheHeader()
        {
            Assert.Equal("r-abc", AttemptOwners.RunnerIdOf("forest-run 1\r\nroute|x\r\nrunner|r-abc|maks\r\ns|0|1,2,3\r\n"));
            Assert.Equal("", AttemptOwners.RunnerIdOf("forest-run 1\nroute|x\ns|0|1,2,3\nrunner|late|x\n"));
            Assert.Equal("", AttemptOwners.RunnerIdOf(null));
        }
    }
}
