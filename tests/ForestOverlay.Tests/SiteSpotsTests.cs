using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class SiteSpotsTests
    {
        [Fact]
        public void RoundTrip_KeepsABarInTheName_SkipsABarInTheId()
        {
            List<SiteSpot> list = new List<SiteSpot>
            {
                new SiteSpot { Id = "s-1", Name = "Cave | 5", By = "a|b", Runs = 3, Best = 12.5f },
                new SiteSpot { Id = "bad|id", Name = "x" },
            };
            List<SiteSpot> back = SiteSpots.Parse(SiteSpots.Write(list));
            Assert.Single(back);
            Assert.Equal("Cave | 5", back[0].Name);
            Assert.Equal("a b", back[0].By);
            Assert.Equal(3, back[0].Runs);
            Assert.Equal(12.5f, back[0].Best);
        }

        [Fact]
        public void NotAList_IsNull()
        {
            Assert.Null(SiteSpots.Parse("<html>"));
            Assert.Null(SiteSpots.Parse(""));
            Assert.Empty(SiteSpots.Parse(SiteSpots.Header + "\n"));
        }

        [Fact]
        public void Owner_RoundTrips_AndAnOlderListHasNone()
        {
            List<SiteSpot> list = new List<SiteSpot>
            {
                new SiteSpot { Id = "s-1", Name = "Mine", Owner = "r-00000000000000aa" },
                new SiteSpot { Id = "s-2", Name = "Theirs" },
            };
            string text = SiteSpots.Write(list);
            List<SiteSpot> back = SiteSpots.Parse(text);
            Assert.Equal(2, back.Count);
            Assert.Equal("r-00000000000000aa", back[0].Owner);
            Assert.Equal("", back[1].Owner);
            Assert.Equal("Mine", back[0].Name);   // the owner line is not part of the name

            // A list from a site before owners: nobody's.
            Assert.Equal("", SiteSpots.Parse(SiteSpots.Header + "\nspot|s-1|0|-||n\n")[0].Owner);
        }

        [Fact]
        public void IsOwner_OnlyTheSameRunnerId()
        {
            SiteSpot s = new SiteSpot { Id = "s-1", Owner = "r-00000000000000aa" };
            Assert.True(SiteSpots.IsOwner(s, "r-00000000000000aa"));
            Assert.False(SiteSpots.IsOwner(s, "r-00000000000000bb"));
            Assert.False(SiteSpots.IsOwner(s, null));
            Assert.False(SiteSpots.IsOwner(new SiteSpot { Id = "s-1" }, ""));
            Assert.False(SiteSpots.IsOwner(null, "r-00000000000000aa"));
        }

        [Fact]
        public void SameAsOwn_IgnoresTheStartStateHash()
        {
            Segment site = Timed(); site.StartState = "abc";
            Segment own = Timed(); own.StartState = "xyz";
            Assert.True(SiteSpots.SameAsOwn(site, own));
        }

        private static Segment Timed()
        {
            Segment s = new Segment { Id = "s-0123456789ab", Name = "Dash", Category = "Mine" };
            TriggerParser.Parse("zone 0 0 0 3", out s.Start);
            TriggerParser.Parse("zone 0 0 40 3", out s.End);
            return s;
        }

        [Fact]
        public void SameAsOwn_AsTheSiteKeepsIt()
        {
            Segment own = Timed(), site = Timed();
            Assert.True(SiteSpots.SameAsOwn(site, own));

            // The start state is the runner's either way: not a difference.
            own.StartState = "1a2b3c4d";
            Assert.True(SiteSpots.SameAsOwn(site, own));
            Assert.Equal("1a2b3c4d", own.StartState);

            // The site trims and clips the name and category.
            own.Name = "  " + new string('n', 90);
            site.Name = new string('n', SiteSpots.MaxName);
            Assert.True(SiteSpots.SameAsOwn(site, own));
            Assert.Equal(92, own.Name.Length);

            // A moved zone, a description or a rename is a difference.
            Segment moved = Timed();
            TriggerParser.Parse("zone 0 0 45 3", out moved.End);
            Assert.False(SiteSpots.SameAsOwn(moved, Timed()));
            Segment noted = Timed();
            noted.Notes = "jump at the rock";
            Assert.False(SiteSpots.SameAsOwn(noted, Timed()));
            Segment renamed = Timed();
            renamed.Name = "Dash 2";
            Assert.False(SiteSpots.SameAsOwn(renamed, Timed()));
            Assert.False(SiteSpots.SameAsOwn(null, Timed()));
        }

        [Fact]
        public void UnknownBest_ReadsAsNaN()
        {
            List<SiteSpot> l = SiteSpots.Parse(SiteSpots.Header + "\nspot|s-1|0|-||n\n");
            Assert.True(float.IsNaN(l[0].Best));
        }
    }
}
