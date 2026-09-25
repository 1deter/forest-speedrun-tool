using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // Turned boxes (v0.24.75). An unturned box must write and fingerprint
    // exactly as before - otherwise every existing route's times would
    // retire on the update - and a turned one must test containment in
    // its own frame, the frame the preview draws.
    // ------------------------------------------------------------------
    public class BoxYawTests
    {
        private static Trigger Parse(string text)
        {
            Trigger t;
            Assert.True(TriggerParser.Parse(text, out t));
            return t;
        }

        [Fact]
        public void AnOldBoxReadsAndWritesUnchanged()
        {
            const string old = "box 745.10 83.38 808.58 3.00 3.00 3.00";
            Trigger t = Parse(old);
            Assert.Equal(0f, t.Yaw);
            Assert.Equal(old, TriggerParser.Write(t));
        }

        [Fact]
        public void AnUnturnedBoxKeepsTheRouteFingerprint()
        {
            Segment a = new Segment();
            a.Id = "s-1";
            a.Start = Parse("zone 0 0 0 5");
            a.End = Parse("box 10 0 10 3 3 3");
            string before = a.RouteFingerprint();

            a.End = Parse(TriggerParser.Write(a.End));   // a round trip through the new writer
            Assert.Equal(before, a.RouteFingerprint());

            a.End = Parse("box 10 0 10 3 3 3 45");
            Assert.NotEqual(before, a.RouteFingerprint());
        }

        [Theory]
        [InlineData("box 1 2 3 4 5 6 45", 45f)]
        [InlineData("box 1 2 3 4 5 6 -90", 270f)]
        [InlineData("box 1 2 3 4 5 6 720.5", 0.5f)]
        [InlineData("box 1 2 3 4 5 6 360", 0f)]
        public void YawIsReadAndNormalized(string text, float yaw)
        {
            Trigger t = Parse(text);
            Assert.Equal(yaw, t.Yaw, 2);
            Assert.Equal(t.Yaw, Parse(TriggerParser.Write(t)).Yaw);
        }

        [Fact]
        public void ABadYawIsRejected()
        {
            Trigger t;
            Assert.False(TriggerParser.Parse("box 1 2 3 4 5 6 north", out t));
        }

        [Fact]
        public void ContainmentFollowsTheTurn()
        {
            // 20 m deep along its heading, 2 m wide across it.
            Trigger t = Parse("box 0 0 0 1 2 10");
            Vector3 alongZ = new Vector3(0f, 0f, 8f);
            Vector3 alongX = new Vector3(8f, 0f, 0f);
            Assert.True(TriggerEvaluator.IsSatisfied(t, alongZ, null, null, null));
            Assert.False(TriggerEvaluator.IsSatisfied(t, alongX, null, null, null));

            // Heading 90 (facing +X in Unity): the long side now runs along X.
            t.Yaw = 90f;
            Assert.False(TriggerEvaluator.IsSatisfied(t, alongZ, null, null, null));
            Assert.True(TriggerEvaluator.IsSatisfied(t, alongX, null, null, null));
            Assert.True(TriggerEvaluator.IsSatisfied(t, new Vector3(-8f, 0f, 0f), null, null, null));

            // 45: a diagonal 8 m out along (1, 0, 1) is inside, across it is not.
            t.Yaw = 45f;
            Assert.True(TriggerEvaluator.IsSatisfied(t, new Vector3(5.6f, 0f, 5.6f), null, null, null));
            Assert.False(TriggerEvaluator.IsSatisfied(t, new Vector3(5.6f, 0f, -5.6f), null, null, null));
            // Height is never turned.
            Assert.False(TriggerEvaluator.IsSatisfied(t, new Vector3(0f, 2.5f, 0f), null, null, null));
        }
    }
}
