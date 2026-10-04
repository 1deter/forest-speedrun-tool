using System;
using System.Collections.Generic;
using System.Text;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // Runner requests for timed runs (backlog *Runs*): which zones are
    // drawn (all / next only / off, checkpoints hidden one by one - display
    // only, never part of the route), and when each time was set.
    // ------------------------------------------------------------------
    public class ZoneDisplayTests
    {
        private const int S = ZoneDisplay.StartSlot;

        private static int[] Pick(ZoneMode mode, int checkpoints, int next, bool[] hidden, bool showHidden)
        {
            int[] into = new int[checkpoints + 2];
            int n = ZoneDisplay.Pick(mode, checkpoints, next, hidden, showHidden, into);
            int[] got = new int[n];
            Array.Copy(into, got, n);
            return got;
        }

        [Fact]
        public void OffDrawsNothingEver()
        {
            Assert.Empty(Pick(ZoneMode.Off, 3, -1, null, true));
            Assert.Empty(Pick(ZoneMode.Off, 3, 1, null, false));
        }

        [Fact]
        public void WithNoRunTheWholeRouteIsDrawn()
        {
            Assert.Equal(new[] { S, 0, 1, 2, 3 }, Pick(ZoneMode.NextOnly, 3, -1, null, false));
            Assert.Equal(new[] { S, 0, 1, 2, 3 }, Pick(ZoneMode.All, 3, -1, null, false));
            Assert.Equal(new[] { S, 0 }, Pick(ZoneMode.All, 0, -1, null, false));   // start + end
        }

        [Fact]
        public void NextOnlyDrawsTheNextCheckpointThenTheEnd()
        {
            Assert.Equal(new[] { 0 }, Pick(ZoneMode.NextOnly, 3, 0, null, false));
            Assert.Equal(new[] { 2 }, Pick(ZoneMode.NextOnly, 3, 2, null, false));
            Assert.Equal(new[] { 3 }, Pick(ZoneMode.NextOnly, 3, 3, null, false));   // only the end left
            Assert.Equal(new[] { 0 }, Pick(ZoneMode.NextOnly, 0, 0, null, false));   // no checkpoints: the end
            // A checkpoint removed mid-run: the end, never past it.
            Assert.Equal(new[] { 2 }, Pick(ZoneMode.NextOnly, 2, 5, null, false));
        }

        [Fact]
        public void AllDrawsTheRouteDuringARun()
        {
            Assert.Equal(new[] { S, 0, 1, 2 }, Pick(ZoneMode.All, 2, 1, null, false));
        }

        [Fact]
        public void HiddenCheckpointsAreLeftOutButShownWhereEdited()
        {
            bool[] hidden = { false, true };   // shorter than the checkpoints: the rest shown
            Assert.Equal(new[] { S, 0, 2, 3 }, Pick(ZoneMode.All, 3, 0, hidden, false));
            Assert.Equal(new[] { S, 0, 2, 3 }, Pick(ZoneMode.NextOnly, 3, -1, hidden, false));
            // The Practice tab open with no run: drawn so it can be edited.
            Assert.Equal(new[] { S, 0, 1, 2, 3 }, Pick(ZoneMode.All, 3, -1, hidden, true));
            // ... but never during a run.
            Assert.Equal(new[] { S, 0, 2, 3 }, Pick(ZoneMode.All, 3, 2, hidden, true));
            // Next only, the next one hidden: nothing until it fires.
            Assert.Empty(Pick(ZoneMode.NextOnly, 3, 1, hidden, true));
            Assert.Equal(new[] { 2 }, Pick(ZoneMode.NextOnly, 3, 2, hidden, false));
        }

        [Fact]
        public void KindsFollowTheSlot()
        {
            Assert.Equal(0, ZoneDisplay.KindOf(S, 2));
            Assert.Equal(1, ZoneDisplay.KindOf(1, 2));
            Assert.Equal(2, ZoneDisplay.KindOf(2, 2));
        }

        [Fact]
        public void ModeParsesFromConfigText()
        {
            Assert.Equal(ZoneMode.All, ZoneDisplay.Parse("All"));
            Assert.Equal(ZoneMode.NextOnly, ZoneDisplay.Parse("NextOnly"));
            Assert.Equal(ZoneMode.Off, ZoneDisplay.Parse(" off "));
            Assert.Null(ZoneDisplay.Parse(""));
            Assert.Null(ZoneDisplay.Parse("sometimes"));
        }

        // --- the hide flag in the segment file ------------------------------

        private static Segment Sample()
        {
            Segment s = new Segment();
            s.Id = "s-0123456789ab";
            s.Name = "flag test";
            TriggerParser.Parse("zone 0 0 0 3", out s.Start);
            TriggerParser.Parse("zone 100 0 0 3", out s.End);
            for (int i = 0; i < 3; i++)
            {
                Trigger c;
                TriggerParser.Parse("zone " + (20 * (i + 1)) + " 0 0 3", out c);
                s.Checkpoints.Add(c);
            }
            return s;
        }

        private static Segment RoundTrip(Segment s)
        {
            StringBuilder sb = new StringBuilder();
            SegmentFormat.WriteSegment(sb, s, "\n");
            return SegmentFormat.ParseAll(sb.ToString().Split('\n'), null)[0];
        }

        [Fact]
        public void HideRoundTripsAndStaysOutOfTheRoute()
        {
            Segment s = Sample();
            string route = s.RouteFingerprint();
            s.SetCheckpointName(1, "Cave 5");
            s.SetCheckpointHidden(1, true);
            s.SetCheckpointHidden(2, true);
            Assert.Equal(route, s.RouteFingerprint());

            Segment r = RoundTrip(s);
            Assert.False(r.IsCheckpointHidden(0));
            Assert.True(r.IsCheckpointHidden(1));
            Assert.True(r.IsCheckpointHidden(2));
            Assert.Equal("Cave 5", r.SplitName(1));
            Assert.Equal(route, r.RouteFingerprint());

            // Removing a checkpoint takes its flag along.
            r.RemoveCheckpoint(1);
            Assert.True(r.IsCheckpointHidden(1));   // the old third
            Assert.False(r.IsCheckpointHidden(0));
        }

        [Fact]
        public void UnhiddenSegmentsWriteNoHideLines()
        {
            Segment s = Sample();
            s.SetCheckpointHidden(0, false);
            StringBuilder sb = new StringBuilder();
            SegmentFormat.WriteSegment(sb, s, "\n");
            Assert.DoesNotContain("hide ", sb.ToString());
        }

        [Fact]
        public void HideBeforeAnyCheckpointIsReported()
        {
            List<string> warnings = new List<string>();
            SegmentFormat.ParseAll(new[] { "[segment]", "id = x", "hide = yes", "start = zone 0 0 0 3", "end = zone 1 0 0 3", "hide = yes" },
                                   (line, msg) => warnings.Add(msg));
            Assert.Equal(2, warnings.Count);
        }

        // --- when each time was set -----------------------------------------

        [Fact]
        public void DatesReadAsTodayYesterdayOrTheDate()
        {
            DateTime now = new DateTime(2026, 10, 4, 15, 0, 0);
            Assert.Equal("today 09:05", RunDates.When(new DateTime(2026, 10, 4, 9, 5, 0), now));
            Assert.Equal("yesterday 23:59", RunDates.When(new DateTime(2026, 10, 3, 23, 59, 0), now));
            Assert.Equal("1 Oct 14:32", RunDates.When(new DateTime(2026, 10, 1, 14, 32, 0), now));
            Assert.Equal("31 Dec 2025 08:00", RunDates.When(new DateTime(2025, 12, 31, 8, 0, 0), now));
            Assert.Equal("", RunDates.When(DateTime.MinValue, now));
            Assert.Equal("", RunDates.WhenUtc(default(DateTime), now));
        }

        [Fact]
        public void YesterdayAcrossNewYear()
        {
            Assert.Equal("yesterday 22:00", RunDates.When(new DateTime(2025, 12, 31, 22, 0, 0), new DateTime(2026, 1, 1, 0, 10, 0)));
        }

        [Fact]
        public void RecordedTimeSurvivesTheRunFile()
        {
            // The .run format has always carried `recorded|` - nothing new
            // is stored for the dates.
            Attempt a = new Attempt();
            a.AnchorLabel = "s-0123456789ab";
            a.RecordedUtc = new DateTime(2026, 10, 3, 18, 30, 15, DateTimeKind.Utc);
            a.Duration = 12.5f;
            RunSample sm;
            sm.T = 0f; sm.P = Vector3.zero; sm.Speed = 0f;
            a.Samples.Add(sm);
            Attempt b = AttemptFormat.Parse(AttemptFormat.Write(a).Split('\n'));
            Assert.Equal(a.RecordedUtc, b.RecordedUtc.ToUniversalTime());
        }

        [Fact]
        public void PbAndGoldsKnowWhenTheyWereSet()
        {
            DateTime d1 = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
            DateTime d2 = new DateTime(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc);
            DateTime d3 = new DateTime(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc);
            List<Attempt> runs = new List<Attempt>
            {
                Run(30f, d1, 10f),   // segments 10, 20
                Run(28f, d2, 12f),   // 12, 16  <- PB, gold on row 1
                Run(29f, d3, 10f),   // 10, 19  (ties row 0's gold: not a new one)
            };
            SplitStats st = SplitStats.Build(runs, 1);
            Assert.Equal(d2, st.PbSetUtc);
            Assert.Equal(d1, st.BestSegmentSetUtc[0]);
            Assert.Equal(d2, st.BestSegmentSetUtc[1]);

            SplitStats none = SplitStats.Build(new List<Attempt>(), 1);
            Assert.Equal(default(DateTime), none.PbSetUtc);
            Assert.Equal(default(DateTime), none.BestSegmentSetUtc[0]);
        }

        private static Attempt Run(float duration, DateTime when, params float[] splits)
        {
            Attempt a = new Attempt();
            a.Duration = duration;
            a.Completed = true;
            a.Splits = splits;
            a.RecordedUtc = when;
            return a;
        }
    }
}
