using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // The line under Practice's Share row: on the site or why not (T-0495).
    public class SpotUploadStatusTests
    {
        private static string T(bool timed = true, bool on = true, bool bad = false, bool known = true,
                                SiteSpot site = null, int pending = 0, int saved = 0)
        {
            return SpotUploadStatus.Text(true, timed, on, bad, known, site, "me", pending, saved);
        }

        [Fact]
        public void OnSite_ShowsRunsOwnerAndWaiting()
        {
            var s = new SiteSpot { Id = "s-1", Owner = "me", Runs = 3 };
            Assert.Equal("On the website (yours), 3 runs.", T(site: s));
            s.Owner = "other"; s.Runs = 1;
            Assert.Equal("On the website, 1 run; 2 upload files waiting.", T(site: s, pending: 2));
        }

        [Fact]
        public void NotOnSite_ReasonsInOrder()
        {
            Assert.Contains("no start and end", T(timed: false, on: false));
            Assert.Contains("is off", T(on: false, bad: true));
            Assert.Contains("token", T(bad: true, pending: 2));
            Assert.Contains("2 upload files waiting", T(pending: 2, saved: 5));
            Assert.Contains("4 saved runs", T(saved: 4));
            Assert.Contains("no run of it", T());
        }

        [Fact]
        public void ListUnknown_SaysSo_AndUnsavedIsOwnCase()
        {
            Assert.StartsWith("Could not check", T(known: false));
            Assert.Contains("not saved yet", SpotUploadStatus.Text(false, true, true, false, true, null, "me", 0, 0));
        }
    }
}
