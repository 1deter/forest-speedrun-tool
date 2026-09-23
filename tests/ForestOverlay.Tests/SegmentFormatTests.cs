using System.Collections.Generic;
using System.Text;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // The editor writes segments back out as text. If write and parse ever
    // disagree, saving silently corrupts a route file - and because these
    // files are shared, one person's bad save spreads. So the round trip
    // is pinned rather than trusted.
    // ------------------------------------------------------------------
    public class SegmentFormatTests
    {
        private const string NL = "\n";

        private static Segment Sample()
        {
            Segment s = new Segment();
            s.Id = "deter/route.plane-to-cave5";
            s.Name = "Plane Crash -> Cave 5";
            s.Category = "Main route";
            s.Notes = "drop down on the left";

            s.HasSpawn = true;
            s.SpawnPosition = new Vector3(-1000.5f, 90.25f, 550.125f);
            s.SpawnYaw = 180f;
            s.SpawnPitch = -12.5f;

            TriggerParser.Parse("zone -1000.50 90.25 550.13 3.00", out s.Start);
            TriggerParser.Parse("zone 123.00 45.00 678.00 5.00", out s.End);

            Trigger c1, c2;
            TriggerParser.Parse("item 78 >= 1", out c1);
            TriggerParser.Parse("event endgame.timmy", out c2);
            s.Checkpoints.Add(c1);
            s.Checkpoints.Add(c2);

            return s;
        }

        private static string Write(params Segment[] segments)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Segment s in segments) SegmentFormat.WriteSegment(sb, s, NL);
            return sb.ToString();
        }

        private static List<Segment> Parse(string text)
        {
            return SegmentFormat.ParseAll(text.Split('\n'), null);
        }

        [Fact]
        public void RoundTripsEveryField()
        {
            Segment original = Sample();
            List<Segment> back = Parse(Write(original));

            Assert.Single(back);
            Segment r = back[0];

            Assert.Equal(original.Id, r.Id);
            Assert.Equal(original.Name, r.Name);
            Assert.Equal(original.Category, r.Category);
            Assert.Equal(original.Notes, r.Notes);

            Assert.True(r.HasSpawn);
            Assert.Equal(original.SpawnPosition.x, r.SpawnPosition.x, 2);
            Assert.Equal(original.SpawnPosition.y, r.SpawnPosition.y, 2);
            Assert.Equal(original.SpawnPosition.z, r.SpawnPosition.z, 2);
            Assert.Equal(original.SpawnYaw, r.SpawnYaw, 2);
            Assert.Equal(original.SpawnPitch, r.SpawnPitch, 2);

            Assert.Equal(TriggerParser.Write(original.Start), TriggerParser.Write(r.Start));
            Assert.Equal(TriggerParser.Write(original.End), TriggerParser.Write(r.End));

            Assert.Equal(2, r.Checkpoints.Count);
            Assert.Equal("item 78 >= 1", TriggerParser.Write(r.Checkpoints[0]));
            Assert.Equal("event endgame.timmy", TriggerParser.Write(r.Checkpoints[1]));
        }

        [Fact]
        public void RoundTripIsStableAcrossTwoPasses()
        {
            // A save-load-save cycle must not drift, or a file would churn
            // in git every time anyone opened it.
            string first = Write(Sample());
            string second = Write(Parse(first).ToArray());

            Assert.Equal(first, second);
        }

        [Fact]
        public void HandlesSeveralSegmentsInOneFile()
        {
            Segment a = Sample();

            Segment b = Sample();
            b.Id = "deter/cave5.sinkhole-drop";
            b.Name = "Sinkhole drop";
            b.Checkpoints.Clear();
            b.HasSpawn = false;

            List<Segment> back = Parse(Write(a, b));

            Assert.Equal(2, back.Count);
            Assert.Equal(a.Id, back[0].Id);
            Assert.Equal(b.Id, back[1].Id);
            Assert.False(back[1].HasSpawn);
            Assert.Empty(back[1].Checkpoints);
        }

        [Fact]
        public void SkipsCommentsAndBlankLines()
        {
            string text =
                "# a comment" + NL +
                NL +
                "[segment]" + NL +
                "id = x" + NL +
                "# another comment" + NL +
                "start = manual" + NL +
                "end = manual" + NL;

            List<Segment> back = Parse(text);

            Assert.Single(back);
            Assert.Equal("x", back[0].Id);
        }

        [Fact]
        public void ReportsBadLinesWithoutLosingTheSegment()
        {
            // One malformed line must not discard a whole shared route set.
            string text =
                "[segment]" + NL +
                "id = x" + NL +
                "start = zone bad bad bad bad" + NL +
                "wat = 3" + NL +
                "no separator here" + NL +
                "end = manual" + NL;

            List<string> warnings = new List<string>();
            List<Segment> back = SegmentFormat.ParseAll(
                text.Split('\n'),
                delegate(int line, string message) { warnings.Add(line + ": " + message); });

            Assert.Single(back);
            Assert.Equal("x", back[0].Id);
            Assert.Equal(3, warnings.Count);

            // The good lines still applied.
            Assert.Equal(TriggerKind.Manual, back[0].End.Kind);
            // The bad start did not.
            Assert.False(back[0].Start.IsSet);
        }

        [Fact]
        public void IgnoresLinesBeforeAnyHeader()
        {
            List<Segment> back = Parse("id = orphan" + NL + "start = manual" + NL);
            Assert.Empty(back);
        }

        [Fact]
        public void ATimedSegmentNeedsIdStartAndEnd()
        {
            Segment s = new Segment();
            Assert.False(s.IsValid);

            s.Id = "x";
            Assert.False(s.IsValid);

            TriggerParser.Parse("manual", out s.Start);
            Assert.False(s.IsValid);
            Assert.False(s.IsTimed);

            TriggerParser.Parse("manual", out s.End);
            Assert.True(s.IsTimed);
            Assert.True(s.IsValid);
        }

        [Fact]
        public void ASpawnOnlyEntryIsValidButNotTimed()
        {
            // The unification: a spot is a segment with somewhere to stand
            // and no triggers. It must be storable and loadable on its own.
            Segment s = new Segment();
            s.Id = "spot.my.ledge";
            s.Name = "Ledge";
            s.HasSpawn = true;
            s.SpawnPosition = new Vector3(1f, 2f, 3f);

            Assert.True(s.IsValid);
            Assert.False(s.IsTimed);
        }

        [Fact]
        public void ASpawnOnlyEntryRoundTrips()
        {
            Segment s = new Segment();
            s.Id = "spot.my.ledge";
            s.Name = "Ledge";
            s.Category = "My spots";
            s.HasSpawn = true;
            s.SpawnPosition = new Vector3(1.25f, 2.5f, 3.75f);
            s.SpawnYaw = 90f;
            s.SpawnPitch = -5f;

            System.Collections.Generic.List<Segment> back = Parse(Write(s));

            Assert.Single(back);
            Assert.True(back[0].HasSpawn);
            Assert.False(back[0].IsTimed);
            Assert.Equal(1.25f, back[0].SpawnPosition.x, 2);
            Assert.Equal(-5f, back[0].SpawnPitch, 2);
        }

        [Fact]
        public void NotesAreOmittedWhenEmpty()
        {
            Segment s = Sample();
            s.Notes = "";

            Assert.DoesNotContain("notes", Write(s));
        }

        // The start-state restore method: in place is the default and is not
        // written, so existing shared files stay byte-identical on save.
        [Fact]
        public void RestoreInPlaceIsTheDefaultAndOmitted()
        {
            Segment s = Sample();
            Assert.False(s.StartRestoreWithLoad);
            Assert.DoesNotContain("restore", Write(s));
            Assert.False(Parse(Write(s))[0].StartRestoreWithLoad);
        }

        [Fact]
        public void RestoreWithLoadRoundTrips()
        {
            Segment s = Sample();
            s.StartRestoreWithLoad = true;
            string text = Write(s);
            Assert.Contains("restore  = load", text);
            Assert.True(Parse(text)[0].StartRestoreWithLoad);
        }

        [Fact]
        public void BadRestoreValueWarnsAndKeepsTheSegment()
        {
            string text = Write(Sample()).Replace("notes", "restore  = sideways" + NL + "notes");
            int warnings = 0;
            List<Segment> back = SegmentFormat.ParseAll(text.Split('\n'), delegate(int line, string m) { warnings++; });
            Assert.Single(back);
            Assert.Equal(1, warnings);
            Assert.False(back[0].StartRestoreWithLoad);
        }
    }
}
