using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // The replay's marker labels: markers at one spot share a label (each
    // distinct line once, "x2" for repeats), and boxes that overlap on
    // screen are stacked upwards, the nearest keeping its place.
    // ------------------------------------------------------------------
    public class ReplayLabelsTests
    {
        private static List<RunEvent> Events(params float[] xs)
        {
            var ev = new List<RunEvent>();
            foreach (float x in xs)
            {
                RunEvent e = default(RunEvent);
                e.P = new Vector3(x, 0f, 0f);
                ev.Add(e);
            }
            return ev;
        }

        [Fact]
        public void MarkersAtOneSpotShareAGroup()
        {
            // Events 0..3 at x 0, 0.5, 10, 1.2; picked nearest first: 0, 1, 3, 2.
            List<RunEvent> ev = Events(0f, 0.5f, 10f, 1.2f);
            int[] idx = { 0, 1, 3, 2 };
            int[] groupOf = new int[4];
            int[] first = new int[4];
            int groups = ReplayLabels.Group(ev, idx, 4, ReplayLabels.SameSpot, groupOf, first);
            Assert.Equal(2, groups);
            Assert.Equal(new[] { 0, 0, 0, 1 }, groupOf);
            Assert.Equal(0, first[0]);
            Assert.Equal(3, first[1]);
        }

        [Fact]
        public void GroupsJoinOnTheFirstMemberNotAChain()
        {
            // 0, 1.4, 2.8: the third is 1.4 from the second but 2.8 from the
            // group's first - a new group, so a long walk never becomes one label.
            List<RunEvent> ev = Events(0f, 1.4f, 2.8f);
            int[] idx = { 0, 1, 2 };
            int[] groupOf = new int[3];
            int[] first = new int[3];
            Assert.Equal(2, ReplayLabels.Group(ev, idx, 3, ReplayLabels.SameSpot, groupOf, first));
            Assert.Equal(new[] { 0, 0, 1 }, groupOf);
        }

        [Fact]
        public void TextCountsRepeatsInTimeOrder()
        {
            string[] labels = { "Crafted: Bomb", "Used: Soda", "Crafted: Bomb", "Pause" };
            int[] idx = { 3, 0, 2, 1 };        // nearest first, not time order
            int[] groupOf = { 0, 0, 0, 0 };
            int lines, longest;
            string text = ReplayLabels.Text(labels, idx, groupOf, 4, 0, ReplayLabels.MaxLines, out lines, out longest);
            Assert.Equal("Crafted: Bomb x2\nUsed: Soda\nPause", text);
            Assert.Equal(3, lines);
            Assert.Equal("Crafted: Bomb x2".Length, longest);
        }

        [Fact]
        public void TextCapsLinesAndCountsTheRest()
        {
            string[] labels = { "A", "B", "C", "D", "E", "E" };
            int[] idx = { 0, 1, 2, 3, 4, 5 };
            int[] groupOf = { 0, 0, 0, 0, 0, 0 };
            int lines, longest;
            string text = ReplayLabels.Text(labels, idx, groupOf, 6, 0, 4, out lines, out longest);
            Assert.Equal("A\nB\nC\n+3 more", text);   // D once, E twice
            Assert.Equal(4, lines);
        }

        [Fact]
        public void TextOnlyTakesItsOwnGroup()
        {
            string[] labels = { "A", "B" };
            int[] idx = { 0, 1 };
            int[] groupOf = { 0, 1 };
            int lines, longest;
            Assert.Equal("B", ReplayLabels.Text(labels, idx, groupOf, 2, 1, 4, out lines, out longest));
            Assert.Equal(1, lines);
        }

        [Fact]
        public void OverlappingBoxesStackUpwardsNearestKeepsItsPlace()
        {
            float[] cx = { 100f, 110f, 400f };
            float[] top = { 200f, 205f, 205f };
            float[] w = { 120f, 120f, 120f };
            float[] h = { 16f, 32f, 16f };
            bool[] shown = { true, true, true };
            ReplayLabels.Stack(cx, top, w, h, shown, 3, 2f);
            Assert.Equal(200f, top[0]);
            Assert.Equal(200f - 32f - 2f, top[1]);   // above the first
            Assert.Equal(205f, top[2]);              // far to the side: unchanged
        }

        [Fact]
        public void StackingClearsEveryEarlierBox()
        {
            // Three at the same point: each ends above the one before.
            float[] cx = { 0f, 0f, 0f };
            float[] top = { 100f, 100f, 100f };
            float[] w = { 50f, 50f, 50f };
            float[] h = { 16f, 16f, 16f };
            bool[] shown = { true, true, true };
            ReplayLabels.Stack(cx, top, w, h, shown, 3, 0f);
            Assert.Equal(new[] { 100f, 84f, 68f }, top);
        }

        [Fact]
        public void HiddenBoxesNeitherMoveNorBlock()
        {
            float[] cx = { 0f, 0f, 0f };
            float[] top = { 100f, 100f, 100f };
            float[] w = { 50f, 50f, 50f };
            float[] h = { 16f, 16f, 16f };
            bool[] shown = { false, true, true };   // the first is behind the camera
            ReplayLabels.Stack(cx, top, w, h, shown, 3, 0f);
            Assert.Equal(new[] { 100f, 100f, 84f }, top);
        }

        [Fact]
        public void SizesFollowTheText()
        {
            Assert.Equal(ReplayLabels.LineHeight * 3, ReplayLabels.Height(3));
            Assert.Equal(ReplayLabels.LineHeight, ReplayLabels.Height(0));
            Assert.Equal(10 * ReplayLabels.CharWidth + ReplayLabels.Padding, ReplayLabels.Width(10));
        }
    }
}
