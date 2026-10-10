using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // The paint tool's dots (T-0219): strokes, spacing, joins, erase,
    // undo, clear and the file (author, 2026-10-10: freehand like KSF
    // paint, removable - not append-only).
    // ------------------------------------------------------------------
    public class PaintSetTests
    {
        private static readonly Vector3 Up = new Vector3(0f, 1f, 0f);

        private static Vector3 At(float x) { return new Vector3(x, 0f, 0f); }

        private static PaintSet Line(int dots, float step, float size = 0.1f)
        {
            PaintSet s = new PaintSet();
            s.BeginStroke(2, size);
            for (int i = 0; i < dots; i++) s.AddPoint(At(i * step), Up);
            s.EndStroke();
            return s;
        }

        [Fact]
        public void NothingIsAddedWithoutAStroke()
        {
            PaintSet s = new PaintSet();
            Assert.False(s.AddPoint(At(0), Up));
            Assert.Equal(0, s.Count);
        }

        [Fact]
        public void AStillCrosshairAddsOneDot()
        {
            PaintSet s = new PaintSet();
            s.BeginStroke(0, 0.1f);
            Assert.True(s.AddPoint(At(0), Up));
            Assert.False(s.AddPoint(At(0.01f), Up));
            Assert.False(s.AddPoint(At(0.04f), Up));
            Assert.True(s.AddPoint(At(0.06f), Up));
            Assert.Equal(2, s.EndStroke());
        }

        [Fact]
        public void TheFirstDotOfAStrokeIsNotJoinedAndTheRestAre()
        {
            PaintSet s = Line(3, 0.2f);
            Assert.False(s[0].Joined);
            Assert.True(s[1].Joined);
            Assert.True(s[2].Joined);
            Assert.Equal(2, s[1].Colour);
        }

        [Fact]
        public void AFarJumpStartsANewPieceInTheSameStroke()
        {
            PaintSet s = new PaintSet();
            s.BeginStroke(0, 0.1f);
            s.AddPoint(At(0), Up);
            s.AddPoint(At(PaintSet.JoinMax + 1f), Up);
            s.EndStroke();
            Assert.False(s[1].Joined);
            Assert.Equal(1, s.StrokeCount);
        }

        [Fact]
        public void ANewStrokeDoesNotJoinTheLastOne()
        {
            PaintSet s = Line(2, 0.2f);
            s.BeginStroke(1, 0.1f);
            s.AddPoint(At(0.5f), Up);
            s.EndStroke();
            Assert.False(s[2].Joined);
            Assert.Equal(2, s.StrokeCount);
        }

        [Fact]
        public void EraseRemovesNearDotsAndCutsTheJoinAfterThem()
        {
            PaintSet s = Line(5, 0.2f);          // x = 0, .2, .4, .6, .8
            int removed = s.EraseNear(At(0.4f), 0.05f);
            Assert.Equal(1, removed);
            Assert.Equal(4, s.Count);
            Assert.True(s[1].Joined);            // .2 still joins 0
            Assert.False(s[2].Joined);           // .6 no longer joins across the gap
            Assert.True(s[3].Joined);
        }

        [Fact]
        public void EraseCountsTheDotsOwnSize()
        {
            PaintSet s = Line(1, 0.2f, 0.4f);    // radius 0.2 at x = 0
            Assert.Equal(1, s.EraseNear(At(0.25f), 0.1f));
        }

        [Fact]
        public void EraseFarAwayChangesNothing()
        {
            PaintSet s = Line(3, 0.2f);
            int v = s.Version;
            Assert.Equal(0, s.EraseNear(At(50f), 0.5f));
            Assert.Equal(v, s.Version);
        }

        [Fact]
        public void UndoRemovesOnlyTheLastStroke()
        {
            PaintSet s = Line(3, 0.2f);
            s.BeginStroke(1, 0.1f);
            s.AddPoint(At(5f), Up);
            s.AddPoint(At(5.2f), Up);
            s.EndStroke();
            Assert.Equal(2, s.Undo());
            Assert.Equal(3, s.Count);
            Assert.Equal(3, s.Undo());
            Assert.Equal(0, s.Undo());
        }

        [Fact]
        public void ClearEmptiesTheSet()
        {
            PaintSet s = Line(4, 0.2f);
            Assert.Equal(4, s.Clear());
            Assert.Equal(0, s.Count);
        }

        [Fact]
        public void DirtyFromIsTheFirstChangedDot()
        {
            PaintSet s = Line(5, 0.2f);
            s.TakeDirty();
            s.EraseNear(At(0.6f), 0.05f);
            Assert.Equal(3, s.TakeDirty());
            Assert.Equal(int.MaxValue, s.TakeDirty());
            s.BeginStroke(0, 0.1f);
            s.AddPoint(At(9f), Up);
            Assert.Equal(4, s.TakeDirty());
        }

        [Fact]
        public void TheSetStopsAtItsCap()
        {
            PaintSet s = new PaintSet();
            s.BeginStroke(0, 0.02f);
            for (int i = 0; i < PaintSet.MaxDots + 10; i++) s.AddPoint(At(i * 0.05f), Up);
            Assert.Equal(PaintSet.MaxDots, s.Count);
            Assert.True(s.Full);
        }

        [Fact]
        public void SizeIsClamped()
        {
            PaintSet s = Line(1, 0.2f, 50f);
            Assert.Equal(PaintSet.MaxSize, s[0].Size);
        }

        [Fact]
        public void TheFileRoundTrips()
        {
            PaintSet s = Line(3, 0.25f, 0.15f);
            s.BeginStroke(5, 0.3f);
            s.AddPoint(new Vector3(1.5f, -2.25f, 3f), new Vector3(1f, 0f, 0f));
            s.AddPoint(new Vector3(9f, -2.25f, 3f), new Vector3(1f, 0f, 0f));   // a far jump
            s.EndStroke();

            int bad;
            PaintSet back = PaintSet.Parse(s.ToText(), out bad);
            Assert.Equal(0, bad);
            Assert.Equal(s.Count, back.Count);
            Assert.Equal(2, back.StrokeCount);
            for (int i = 0; i < s.Count; i++)
            {
                Assert.Equal(s[i].P, back[i].P);
                Assert.Equal(s[i].N, back[i].N);
                Assert.Equal(s[i].Colour, back[i].Colour);
                Assert.Equal(s[i].Size, back[i].Size);
                Assert.Equal(s[i].Joined, back[i].Joined);
            }
            Assert.Equal(2, back.Undo());   // the second stroke, both its pieces
        }

        [Fact]
        public void AFileWithBadLinesKeepsTheGoodOnes()
        {
            string text = "1 2 3 0 1 0\n" +        // before any stroke
                          "stroke x 0.1\n" +        // bad colour: its dots are bad too
                          "4 5 6 0 1 0\n" +
                          "stroke 3 0.1\n" +
                          "7 8 9 0 1 0\n" +
                          "7 8 nope 0 1 0\n" +
                          "\n# a comment\n" +
                          "7.5 8 9 0 1 0\n";
            int bad;
            PaintSet s = PaintSet.Parse(text, out bad);
            Assert.Equal(4, bad);
            Assert.Equal(2, s.Count);
            Assert.Equal(3, s[0].Colour);
            Assert.True(s[1].Joined);
        }

        [Fact]
        public void ParsedPaintIsAllDirty()
        {
            int bad;
            PaintSet s = PaintSet.Parse(Line(2, 0.2f).ToText(), out bad);
            Assert.Equal(0, s.TakeDirty());
        }

        [Fact]
        public void PaintingAfterALoadStartsANewStroke()
        {
            int bad;
            PaintSet s = PaintSet.Parse(Line(2, 0.2f).ToText(), out bad);
            s.BeginStroke(0, 0.1f);
            s.AddPoint(At(0.6f), Up);
            s.EndStroke();
            Assert.Equal(2, s.StrokeCount);
            Assert.Equal(1, s.Undo());
        }

        [Fact]
        public void SpotIdsBecomeSafeFileNames()
        {
            Assert.Equal("plane-to-cave5.txt", PaintSet.FileNameFor("plane-to-cave5"));
            Assert.Equal("a_b_c.txt", PaintSet.FileNameFor("a/b:c"));
            Assert.Equal("_._x.txt", PaintSet.FileNameFor("..\\x"));
        }
    }
}
