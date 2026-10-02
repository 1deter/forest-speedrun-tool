using System;
using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class RunHistoryTests
    {
        private const float N = float.NaN;

        private static HistoryRun Done(params float[] times)
        {
            HistoryRun h;
            h.StartedUtc = DateTime.UtcNow;
            h.Times = times;
            h.Finished = true;
            return h;
        }

        private static HistoryRun Reset(params float[] times)
        {
            HistoryRun h = Done(times);
            h.Finished = false;
            return h;
        }

        private static string Chance(List<HistoryRun> history, float[] times, float pb)
        {
            return PbChance.Compute(history, times, pb, new Random(1));
        }

        // --- unfinished.txt -------------------------------------------------

        [Fact]
        public void UnfinishedLineRoundTrips()
        {
            UnfinishedAttempt u = new UnfinishedAttempt();
            u.StartedUtc = new DateTime(2026, 10, 2, 13, 4, 5, DateTimeKind.Utc);
            u.Route = "a1b2c3d4";
            u.Duration = 41.2345f;
            u.Splits = new[] { 10.5f, 20.25f };

            string line = u.Write();
            Assert.Equal("20261002T130405Z\ta1b2c3d4\t41.235\t10.500,20.250", line);

            UnfinishedAttempt back = UnfinishedAttempt.Parse(line);
            Assert.Equal(u.StartedUtc, back.StartedUtc);
            Assert.Equal(DateTimeKind.Utc, back.StartedUtc.Kind);
            Assert.Equal("a1b2c3d4", back.Route);
            Assert.Equal(41.235f, back.Duration, 3);
            Assert.Equal(new[] { 10.5f, 20.25f }, back.Splits);
        }

        [Fact]
        public void UnfinishedWithoutSplitsOrRouteAndDamagedLines()
        {
            UnfinishedAttempt u = new UnfinishedAttempt();
            u.StartedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            u.Duration = 3f;
            string text = "# comment\r\n" + u.Write() + "\r\nnot a line\r\n20260101T000000Z\t\tabc\t\n\n";

            List<UnfinishedAttempt> all = UnfinishedAttempt.ParseAll(text);
            Assert.Single(all);
            Assert.Equal("", all[0].Route);
            Assert.Empty(all[0].Splits);
            Assert.Equal(3f, all[0].Duration);
        }

        // --- PB chance --------------------------------------------------------

        [Fact]
        public void NoPbMeansEveryRunPbs()
        {
            Assert.Equal("100%", Chance(new List<HistoryRun>(), new[] { N, N }, N));
        }

        [Fact]
        public void FinishedRunComparesWithThePb()
        {
            List<HistoryRun> h = new List<HistoryRun> { Done(10f, 20f) };
            Assert.Equal("100% (Congrats!)", Chance(h, new[] { 9f, 19.5f }, 20f));
            Assert.Equal("0%", Chance(h, new[] { 9f, 20.5f }, 20f));
            Assert.Equal("- (Tied)", Chance(h, new[] { 9f, 20.0001f }, 20f));
        }

        [Fact]
        public void CertainOutcomesFromOneHistory()
        {
            // One run: segments 10 + 10 = 20. Against a PB of 21 every
            // simulation succeeds; against 20 none does (not strictly under)
            // but the golds tie it, so "< 0.01%".
            List<HistoryRun> h = new List<HistoryRun> { Done(10f, 20f) };
            Assert.Equal("100%", Chance(h, new[] { N, N }, 21f));
            Assert.Equal("< 0.01%", Chance(h, new[] { N, N }, 20f));
            Assert.Equal("0%", Chance(h, new[] { N, N }, 19f));
            // From the split: 12 so far + 10 to come.
            Assert.Equal("0%", Chance(h, new[] { 12f, N }, 21f));
            Assert.Equal("100%", Chance(h, new[] { 9f, N }, 21f));
        }

        [Fact]
        public void HalfAndHalfComesOutNearFifty()
        {
            // Last row 5 or 15, equally: a PB of 21 needs the 5.
            List<HistoryRun> h = new List<HistoryRun> { Done(10f, 15f), Done(10f, 25f) };
            string s = Chance(h, new[] { 10f, N }, 21f);
            float pct = float.Parse(s.TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(pct, 47f, 53f);
        }

        [Fact]
        public void ResetsCountAsFailuresOnTheRowAfterTheirLastSplit()
        {
            // A finished 10 + 10 and a run that stopped after its first
            // split: row 1's pool is {10, reset}, row 0's {10, 10}.
            List<HistoryRun> h = new List<HistoryRun> { Done(10f, 20f), Reset(10f, N) };
            List<float>[] pools = PbChance.BuildPools(h, 2);
            Assert.Equal(new[] { 10f, 10f }, pools[0].ToArray());
            Assert.Equal(2, pools[1].Count);
            Assert.Contains(pools[1], float.IsNaN);

            string s = Chance(h, new[] { 10f, N }, 25f);
            float pct = float.Parse(s.TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(pct, 47f, 53f);

            // A run that reached nothing resets row 0.
            pools = PbChance.BuildPools(new List<HistoryRun> { Reset(N, N) }, 2);
            Assert.True(float.IsNaN(pools[0][0]));
            Assert.Empty(pools[1]);
        }

        [Fact]
        public void EmptyPoolForARemainingRowIsUnknown()
        {
            // The only attempt reset before row 1: row 1 has no times.
            List<HistoryRun> h = new List<HistoryRun> { Reset(N, N) };
            Assert.Equal("-", Chance(h, new[] { 5f, N }, 20f));
        }

        [Fact]
        public void PoolUsesTheMostRecentHalfAsTheComponentDoes()
        {
            // 10 attempts -> attempts 5..10 (6 of them). Old fast ones drop out.
            List<HistoryRun> h = new List<HistoryRun>();
            for (int i = 0; i < 4; i++) h.Add(Done(1f));
            for (int i = 0; i < 6; i++) h.Add(Done(30f));
            List<float>[] pools = PbChance.BuildPools(h, 1);
            Assert.Equal(6, pools[0].Count);
            Assert.All(pools[0], v => Assert.Equal(30f, v));

            // 1 attempt and 2 attempts: all of them.
            Assert.Single(PbChance.BuildPools(new List<HistoryRun> { Done(5f) }, 1)[0]);
            Assert.Equal(2, PbChance.BuildPools(new List<HistoryRun> { Done(5f), Done(6f) }, 1)[0].Count);
            Assert.Equal(2, PbChance.BuildPools(new List<HistoryRun> { Done(5f), Done(6f), Done(7f) }, 1)[0].Count);
        }

        [Fact]
        public void OldFinishedRunsWithOnlyATotalStayOutOfThePool()
        {
            List<HistoryRun> h = new List<HistoryRun> { Done(N, 25f), Done(10f, 20f) };
            List<float>[] pools = PbChance.BuildPools(h, 2);
            Assert.Equal(new[] { 10f }, pools[0].ToArray());
            Assert.Equal(new[] { 10f }, pools[1].ToArray());
        }

        // --- total playtime ---------------------------------------------------

        [Fact]
        public void PlaytimeSumsFinishedUnfinishedAndTheRunningOne()
        {
            Attempt a = new Attempt { Duration = 30f, Completed = true };
            Attempt b = new Attempt { Duration = 99f, Completed = false };
            UnfinishedAttempt u = new UnfinishedAttempt { Duration = 12.5f };
            Assert.Equal(52.5f, Playtime.Total(new List<Attempt> { a, b }, new List<UnfinishedAttempt> { u }, 10f), 3);
            Assert.Equal(0f, Playtime.Total(null, null, 0f));
        }

        [Fact]
        public void PlaytimeFormatsLikeLiveSplit()
        {
            Assert.Equal("0:00", Playtime.Format(0f));
            Assert.Equal("0:59", Playtime.Format(59.99f));
            Assert.Equal("12:34", Playtime.Format(754f));
            Assert.Equal("1:00:00", Playtime.Format(3600f));
            Assert.Equal("23:59:59", Playtime.Format(86399f));
            Assert.Equal("1d 0:00:01", Playtime.Format(86401f));
            Assert.Equal("2d 3:04:05", Playtime.Format(2 * 86400 + 3 * 3600 + 4 * 60 + 5));
        }
    }
}
