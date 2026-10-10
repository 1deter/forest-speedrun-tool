using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // The Practice autosave's invalid-entry report (T-0217): once per entry.
    public class HeldReportTests
    {
        [Fact]
        public void ReportedOncePerEntry()
        {
            HeldReport r = new HeldReport();
            Assert.True(r.First("a"));
            Assert.False(r.First("a"));
            Assert.True(r.First("b"));
        }

        [Fact]
        public void AnEntryNoLongerHeldIsReportedAgainNextTime()
        {
            HeldReport r = new HeldReport();
            r.First("a");
            r.First("b");
            r.Keep(new List<string> { "b" });
            Assert.True(r.First("a"));
            Assert.False(r.First("b"));
        }
    }
}
