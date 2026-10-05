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
        public void UnknownBest_ReadsAsNaN()
        {
            List<SiteSpot> l = SiteSpots.Parse(SiteSpots.Header + "\nspot|s-1|0|-||n\n");
            Assert.True(float.IsNaN(l[0].Best));
        }
    }
}
