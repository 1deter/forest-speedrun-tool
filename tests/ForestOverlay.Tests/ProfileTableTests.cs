using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class ProfileTableTests
    {
        // 1000 ticks a second: one tick = 1 ms.
        private const long Freq = 1000;

        [Fact]
        public void ReportRanksByTimeAndAllocation()
        {
            ProfileTable t = new ProfileTable();
            int a = t.Add("A.Update");
            int b = t.Add("B.LateUpdate");
            for (int i = 0; i < 10; i++) t.Record(a, 2, 0);
            t.Record(b, 50, 4096);

            var lines = t.Report(10f, 10, Freq, 5);
            Assert.Equal(3, lines.Count);
            Assert.StartsWith("Game profile (10 s, 10 frames, 2 methods): hooked 7.00 ms/frame, 1 calls/frame, heap +0 KB/s", lines[0]);
            Assert.Equal("time: B.LateUpdate 5.00 ms/f (x0.1/f, max 50.0 ms), A.Update 2.00 ms/f (x1/f, max 2.0 ms)", lines[1]);
            Assert.Equal("alloc: B.LateUpdate 0.4 KB/s (x0.1/f)", lines[2]);
        }

        [Fact]
        public void HeapDropIsAGcHitNotAllocation()
        {
            ProfileTable t = new ProfileTable();
            int a = t.Add("A.Update");
            t.Record(a, 90, -5000000);
            var lines = t.Report(1f, 1, Freq, 5);
            Assert.Equal(0, t.TotalBytes());
            Assert.Contains("GC in: A.Update x1 (max 90 ms)", lines);
        }

        [Fact]
        public void ResetClearsCountsButKeepsSlots()
        {
            ProfileTable t = new ProfileTable();
            int a = t.Add("A.Update");
            t.Record(a, 5, 10);
            t.Reset();
            Assert.Equal(1, t.Count);
            Assert.Equal(0, t.TotalCalls());
            Assert.Empty(t.Report(1f, 1, Freq, 5));
        }

        [Fact]
        public void GrowsPastInitialCapacityAndIgnoresBadSlots()
        {
            ProfileTable t = new ProfileTable();
            for (int i = 0; i < 200; i++) t.Add("M" + i);
            t.Record(199, 1, 0);
            t.Record(500, 1, 0);
            t.Record(-1, 1, 0);
            Assert.Equal(1, t.TotalCalls());
            Assert.Equal("M199", t.Name(199));
        }

        [Fact]
        public void TopLimitsEachList()
        {
            ProfileTable t = new ProfileTable();
            for (int i = 0; i < 5; i++) t.Record(t.Add("M" + i), i + 1, 0);
            var lines = t.Report(1f, 1, Freq, 2);
            Assert.Equal("time: M4 5.00 ms/f (x1/f, max 5.0 ms), M3 4.00 ms/f (x1/f, max 4.0 ms)", lines[1]);
        }

        [Fact]
        public void ParseExtraReadsTypeAndMethod()
        {
            var l = ProfileTable.ParseExtra(" mutantAI::Update, TheForest.World.WorkScheduler::* ;bad; ::X;Y::\nA+B::MoveNext");
            Assert.Equal(3, l.Count);
            Assert.Equal(new[] { "mutantAI", "Update" }, l[0]);
            Assert.Equal(new[] { "TheForest.World.WorkScheduler", "*" }, l[1]);
            Assert.Equal(new[] { "A+B", "MoveNext" }, l[2]);
            Assert.Empty(ProfileTable.ParseExtra(null));
            Assert.Equal(new[] { "*", "MoveNext" }, ProfileTable.ParseExtra("*::MoveNext")[0]);
        }
    }
}
