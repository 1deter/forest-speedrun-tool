using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class DroppedPosesTests
    {
        private static DroppedPoses.Pose P(int item, float x)
        {
            DroppedPoses.Pose p = new DroppedPoses.Pose();
            p.Item = item; p.X = x; p.Y = 76.08f; p.Z = -4.12f;
            p.Qx = 0f; p.Qy = 0.7071f; p.Qz = 0f; p.Qw = 0.7071f;
            return p;
        }

        [Fact]
        public void RoundTripKeepsNegativeIdsAndIgnoresCulture()
        {
            var old = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            try
            {
                var list = new List<KeyValuePair<int, DroppedPoses.Pose>>();
                list.Add(new KeyValuePair<int, DroppedPoses.Pose>(-197640, P(53, 430.11f)));
                list.Add(new KeyValuePair<int, DroppedPoses.Pose>(-5, P(7, -1.5f)));
                string line = DroppedPoses.Format(list);
                Assert.DoesNotContain("430,11", line);
                var map = DroppedPoses.Parse(line);
                Assert.Equal(2, map.Count);
                Assert.Equal(53, map[-197640].Item);
                Assert.Equal(430.11f, map[-197640].X);
                Assert.Equal(-1.5f, map[-5].X);
                Assert.Equal(0.7071f, map[-5].Qw);
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = old; }
        }

        [Fact]
        public void GarbageAndEmptyGiveAnEmptyMap()
        {
            Assert.Empty(DroppedPoses.Parse(""));
            Assert.Empty(DroppedPoses.Parse(null));
            Assert.Empty(DroppedPoses.Parse("nonsense;1:2;x:1@a,b,c@1,2,3,4;3:4@1,2@1,2,3,4"));
            Assert.Equal("", DroppedPoses.Format(new List<KeyValuePair<int, DroppedPoses.Pose>>()));
        }

        [Fact]
        public void DiffersAtThresholds()
        {
            DroppedPoses.Pose p = P(1, 10f);
            Assert.False(DroppedPoses.Differs(p, 10.01f, 76.08f, -4.12f, 0f, 0.7071f, 0f, 0.7071f));
            Assert.True(DroppedPoses.Differs(p, 10.2f, 76.08f, -4.12f, 0f, 0.7071f, 0f, 0.7071f));
            Assert.True(DroppedPoses.Differs(p, 10f, 76.08f, -4.12f, 0f, 0f, 0f, 1f));
        }

        [Fact]
        public void ThrownKindLabels()
        {
            Assert.Equal("bomb", DroppedPoses.ThrownKind(true, false, false, false, false, false));
            Assert.Equal("explosion", DroppedPoses.ThrownKind(false, true, false, false, false, true));
            Assert.Equal("molotov", DroppedPoses.ThrownKind(false, false, true, false, false, true));
            Assert.Equal("flare", DroppedPoses.ThrownKind(false, false, false, true, false, false));
            Assert.Equal("flare", DroppedPoses.ThrownKind(false, false, false, false, true, true));
            Assert.Equal("effect", DroppedPoses.ThrownKind(false, false, false, false, false, true));
            Assert.Equal("", DroppedPoses.ThrownKind(false, false, false, false, false, false));
        }

        [Fact]
        public void TagsRoundTrip()
        {
            var old = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            try
            {
                DroppedPoses.Pose both = P(57, 431.7f);
                both.Remake = true; both.Timer = DroppedPoses.TimerKind.Running; both.Age = 37.25f;
                DroppedPoses.Pose timerOnly = P(53, 1f);
                timerOnly.Timer = DroppedPoses.TimerKind.Running; timerOnly.Age = 0.5f;
                DroppedPoses.Pose remakeOnly = P(53, 2f);
                remakeOnly.Remake = true;
                DroppedPoses.Pose stopped = P(53, 3f);
                stopped.Remake = true; stopped.Timer = DroppedPoses.TimerKind.Stopped;
                DroppedPoses.Pose plain = P(7, 4f);
                var list = new List<KeyValuePair<int, DroppedPoses.Pose>>();
                list.Add(new KeyValuePair<int, DroppedPoses.Pose>(-125484, both));
                list.Add(new KeyValuePair<int, DroppedPoses.Pose>(-2, timerOnly));
                list.Add(new KeyValuePair<int, DroppedPoses.Pose>(-3, remakeOnly));
                list.Add(new KeyValuePair<int, DroppedPoses.Pose>(-4, stopped));
                list.Add(new KeyValuePair<int, DroppedPoses.Pose>(-5, plain));
                string line = DroppedPoses.Format(list);
                Assert.Contains("@p,t37.25;", line);
                Assert.Contains("@t0.5;", line);
                Assert.Contains("@p,s;", line);
                Assert.EndsWith(",0.7071", line);          // no tags: the old 3-segment form
                var map = DroppedPoses.Parse(line);
                Assert.Equal(5, map.Count);
                Assert.True(map[-125484].Remake);
                Assert.Equal(DroppedPoses.TimerKind.Running, map[-125484].Timer);
                Assert.Equal(37.25f, map[-125484].Age);
                Assert.Equal(431.7f, map[-125484].X);
                Assert.False(map[-2].Remake);
                Assert.Equal(DroppedPoses.TimerKind.Running, map[-2].Timer);
                Assert.Equal(0.5f, map[-2].Age);
                Assert.True(map[-3].Remake);
                Assert.Equal(DroppedPoses.TimerKind.Unknown, map[-3].Timer);
                Assert.Equal(DroppedPoses.TimerKind.Stopped, map[-4].Timer);
                Assert.False(map[-5].Remake);
                Assert.Equal(DroppedPoses.TimerKind.Unknown, map[-5].Timer);
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = old; }
        }

        [Fact]
        public void ThreeSegmentsParseUntaggedAndUnknownTagsKeepThePose()
        {
            var map = DroppedPoses.Parse("-1:57@1,2,3@0,0,0,1;-2:53@4,5,6@0,0,0,1@zz,q9;-3:53@7,8,9@0,0,0,1@p,future,tx");
            Assert.Equal(3, map.Count);
            Assert.False(map[-1].Remake);
            Assert.Equal(DroppedPoses.TimerKind.Unknown, map[-1].Timer);
            Assert.False(map[-2].Remake);
            Assert.Equal(DroppedPoses.TimerKind.Unknown, map[-2].Timer);
            Assert.Equal(4f, map[-2].X);
            Assert.True(map[-3].Remake);
            Assert.Equal(DroppedPoses.TimerKind.Unknown, map[-3].Timer);   // "tx" is not an age
            Assert.Empty(DroppedPoses.Parse("-1:57@1,2,3@0,0,0,1@p@p"));    // five segments: not a pose
        }

        [Fact]
        public void ToRemakeSkipsPositiveIdsItemZeroPresentAndUnflagged()
        {
            var poses = new Dictionary<int, DroppedPoses.Pose>();
            DroppedPoses.Pose flagged = P(57, 1f); flagged.Remake = true;
            DroppedPoses.Pose noItem = P(0, 1f); noItem.Remake = true;
            poses[-10] = flagged;                 // gone: re-made
            poses[-11] = flagged;                 // still there
            poses[12] = flagged;                  // a scene object, not made at runtime
            poses[-13] = noItem;
            poses[-14] = P(57, 1f);               // not flagged
            var present = new HashSet<int> { -11 };
            List<int> ids = DroppedPoses.ToRemake(poses, present);
            Assert.Equal(new[] { -10 }, ids.ToArray());
            Assert.Equal(2, DroppedPoses.ToRemake(poses, null).Count);
            Assert.Empty(DroppedPoses.ToRemake(null, present));
        }

        [Fact]
        public void DecideOldFileGivesFreshTimerOnlyToReshownCopies()
        {
            Assert.Equal(DespawnTimer.Plan.Fresh, DespawnTimer.Decide(DroppedPoses.TimerKind.Unknown, true));
            Assert.Equal(DespawnTimer.Plan.Leave, DespawnTimer.Decide(DroppedPoses.TimerKind.Unknown, false));
        }

        [Fact]
        public void DecideNewFileUnreadIsLeftAndRecordedAgesResume()
        {
            Assert.Equal(DespawnTimer.Plan.Leave, DespawnTimer.Decide(DroppedPoses.TimerKind.Unread, true));
            Assert.Equal(DespawnTimer.Plan.Resume, DespawnTimer.Decide(DroppedPoses.TimerKind.Running, false));
            Assert.Equal(DespawnTimer.Plan.Stop, DespawnTimer.Decide(DroppedPoses.TimerKind.Stopped, true));
        }

        [Fact]
        public void UnreadTagRoundTrips()
        {
            var list = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int, DroppedPoses.Pose>>();
            DroppedPoses.Pose p = new DroppedPoses.Pose(); p.Item = 5; p.Timer = DroppedPoses.TimerKind.Unread;
            list.Add(new System.Collections.Generic.KeyValuePair<int, DroppedPoses.Pose>(-4, p));
            Assert.Equal(DroppedPoses.TimerKind.Unread, DroppedPoses.Parse(DroppedPoses.Format(list))[-4].Timer);
        }

        [Fact]
        public void NextCheckResumesTheGamesSchedule()
        {
            // destroyAfter on Stick_Dynamic: 60 s, then every 10 s (repeating).
            Assert.Equal(40f, DespawnTimer.NextCheck(20f, 60f, true));
            Assert.Equal(60f, DespawnTimer.NextCheck(0f, 60f, true));
            Assert.Equal(60f, DespawnTimer.NextCheck(-3f, 60f, true));
            Assert.Equal(10f, DespawnTimer.NextCheck(60f, 60f, true));     // on the boundary: that check ran
            Assert.Equal(7f, DespawnTimer.NextCheck(63f, 60f, true), 3);
            Assert.Equal(2.5f, DespawnTimer.NextCheck(97.5f, 60f, true), 3);
            Assert.Equal(25f, DespawnTimer.NextCheck(35f, 60f, false));
            Assert.Equal(0f, DespawnTimer.NextCheck(75f, 60f, false));
        }
    }
}
