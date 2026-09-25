using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // A shared segment file carries a route between players: its three
    // parts must come back exactly, and the start state byte for byte,
    // or the imported route would not be the route that was timed.
    // ------------------------------------------------------------------
    public class SegmentBundleTests
    {
        private const string Fosave =
            "ForestOverlay savestate 1\n" +
            "name = Cave 5 [start]\n" +
            "position = 772.54 80.53 841.15\n" +
            "greebles = 501.23,76.37,90.30:11525:fdfdfdfd\n" +
            "data = QUJDRA==\n";

        private const string Run =
            "anchor|deter/route.cave5\n" +
            "recorded|2026-09-25T21:04:11.0000000Z\n" +
            "duration|45.123\n" +
            "route|0ccbffa8\n" +
            "channels|Health|Stamina\n" +
            "s|0.000|772.540|80.530|841.150|0.000\n" +
            "v|0.000|100.000|90.000\n";

        private static SegmentBundle Sample()
        {
            Segment s = new Segment();
            s.Id = "deter/route.cave5";
            s.Name = "Cave 5 Practice Run";
            s.Category = "Practice Runs";
            s.HasSpawn = true;
            s.SpawnPosition = new Vector3(772.54f, 80.53f, 841.15f);
            s.SpawnYaw = 240.46f;
            TriggerParser.Parse("zone 772.54 80.53 841.15 5.37", out s.Start);
            TriggerParser.Parse("box 799.25 94.49 771.87 3.00 3.00 3.00", out s.End);
            Trigger c;
            TriggerParser.Parse("item 57 >= +1", out c);
            s.Checkpoints.Add(c);
            s.StartState = "0ccbffa8";

            SegmentBundle b = new SegmentBundle();
            b.Exported = "2026-09-25 23:40:00";
            b.PluginVersion = "0.24.71";
            b.Segment = s;
            b.StartState = Fosave;
            b.Attempts.Add(Run);
            b.Attempts.Add(Run.Replace("45.123", "44.000"));
            return b;
        }

        private static SegmentBundle RoundTrip(SegmentBundle b)
        {
            string error;
            SegmentBundle back = SegmentBundle.Parse(b.Write(), out error, null);
            Assert.Null(error);
            return back;
        }

        [Fact]
        public void EveryPartRoundTrips()
        {
            SegmentBundle b = Sample();
            SegmentBundle back = RoundTrip(b);

            Assert.Equal("2026-09-25 23:40:00", back.Exported);
            Assert.Equal("0.24.71", back.PluginVersion);
            Assert.Equal(b.Segment.Id, back.Segment.Id);
            Assert.Equal(b.Segment.Name, back.Segment.Name);
            Assert.Equal(b.Segment.RouteFingerprint(), back.Segment.RouteFingerprint());
            Assert.Equal(Fosave, back.StartState);
            Assert.Equal(b.Attempts, back.Attempts);
        }

        [Fact]
        public void TheStartStateStillParsesAndHashesTheSame()
        {
            SegmentBundle back = RoundTrip(Sample());
            string error;
            SavestateFile f = SavestateFile.Parse(back.StartState, out error);
            Assert.Null(error);
            Assert.Equal("Cave 5 [start]", f.Name);
            Assert.Equal(Segment.HashText("QUJDRA=="), Segment.HashText(f.Data));
        }

        [Fact]
        public void ASpotWithNothingElseRoundTrips()
        {
            SegmentBundle b = Sample();
            b.StartState = null;
            b.Attempts.Clear();
            b.Segment.Start = new Trigger();
            b.Segment.End = new Trigger();
            b.Segment.Checkpoints.Clear();

            SegmentBundle back = RoundTrip(b);
            Assert.Null(back.StartState);
            Assert.Empty(back.Attempts);
            Assert.False(back.Segment.IsTimed);
            Assert.True(back.Segment.HasSpawn);
        }

        [Fact]
        public void WindowsLineEndingsAndABomAreRead()
        {
            string text = "﻿" + Sample().Write().Replace("\n", "\r\n");
            string error;
            SegmentBundle back = SegmentBundle.Parse(text, out error, null);
            Assert.Null(error);
            Assert.Equal(Fosave, back.StartState);
            Assert.Equal(2, back.Attempts.Count);
        }

        [Fact]
        public void ALineStartingWithABracketCannotOpenASection()
        {
            SegmentBundle b = Sample();
            b.StartState = Fosave + "[attempt]\n";
            SegmentBundle back = RoundTrip(b);
            Assert.Equal(2, back.Attempts.Count);
            Assert.Contains(" [attempt]", back.StartState);
        }

        [Theory]
        [InlineData("")]
        [InlineData("ForestOverlay savestate 1\ndata = x\n")]
        [InlineData("ForestOverlay segment 1\nexported = now\n")]
        [InlineData("ForestOverlay segment 1\n[segment]\nname = no id\n")]
        public void RejectsWhatIsNotABundle(string text)
        {
            string error;
            Assert.Null(SegmentBundle.Parse(text, out error, null));
            Assert.NotNull(error);
        }

        [Fact]
        public void BadSegmentLinesAreWarningsNotErrors()
        {
            string text = Sample().Write().Replace("[segment]\n", "[segment]\nstart = nonsense\n");
            List<string> warnings = new List<string>();
            string error;
            Assert.NotNull(SegmentBundle.Parse(text, out error, warnings));
            Assert.NotEmpty(warnings);
        }

        [Fact]
        public void AttemptFileNamesMatchTheStore()
        {
            Assert.Equal("20260925_210411_45.123.run", SegmentBundle.AttemptFileName(Run));
            Assert.Null(SegmentBundle.AttemptFileName("anchor|x\ns|0|0|0|0|0\n"));
        }
    }
}
