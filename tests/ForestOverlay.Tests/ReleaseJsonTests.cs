using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class ReleaseJsonTests
    {
        // Trimmed from a real /releases/latest response. Note the spaces
        // after every colon, and that "name" appears for the release
        // (after the assets here) as well as for the asset.
        private const string Pretty = @"{
  ""url"": ""https://api.github.com/repos/1deter/forest-speedrun-tool/releases/394084553"",
  ""tag_name"": ""v0.16.1"",
  ""assets"": [
    {
      ""url"": ""https://api.github.com/repos/1deter/forest-speedrun-tool/releases/assets/582162976"",
      ""id"": 582162976,
      ""name"": ""ForestOverlay.dll"",
      ""label"": """",
      ""uploader"": {
        ""login"": ""github-actions[bot]""
      },
      ""size"": 164864,
      ""browser_download_url"": ""https://github.com/1deter/forest-speedrun-tool/releases/download/v0.16.1/ForestOverlay.dll""
    }
  ],
  ""name"": ""v0.16.1"",
  ""body"": ""**Full Changelog**: https://github.com/1deter/forest-speedrun-tool/compare/v0.16.0...v0.16.1""
}";

        private const string Url = "https://github.com/1deter/forest-speedrun-tool/releases/download/v0.16.1/ForestOverlay.dll";

        [Fact]
        public void FindsTheAssetInPrettyPrintedJson()
        {
            Assert.Equal(Url, ReleaseJson.ExtractAssetUrl(Pretty, "ForestOverlay.dll"));
        }

        // The asset from a real v0.19.1 response (compact, as the API sends
        // it), field order intact: the uploader object sits between the
        // name and "state", and "state" comes before the url.
        private const string RealAsset =
            "{\"tag_name\":\"v0.19.1\",\"assets\":[{\"url\":\"https://api.github.com/repos/1deter/forest-speedrun-tool/releases/assets/582428867\"," +
            "\"id\":582428867,\"node_id\":\"RA_kwDOUkfAHs4ityjD\",\"name\":\"ForestOverlay.dll\",\"label\":\"\"," +
            "\"uploader\":{\"login\":\"github-actions[bot]\",\"id\":41898282,\"type\":\"Bot\",\"site_admin\":false}," +
            "\"content_type\":\"application/x-msdownload\",\"state\":\"STATE\",\"size\":199680," +
            "\"download_count\":0,\"browser_download_url\":\"https://github.com/1deter/forest-speedrun-tool/releases/download/v0.19.1/ForestOverlay.dll\"}]," +
            "\"name\":\"v0.19.1\"}";

        [Fact]
        public void AnUploadedAssetIsDownloadable()
        {
            Assert.Equal("https://github.com/1deter/forest-speedrun-tool/releases/download/v0.19.1/ForestOverlay.dll",
                         ReleaseJson.ExtractAssetUrl(RealAsset.Replace("STATE", "uploaded"), "ForestOverlay.dll"));
        }

        [Fact]
        public void AnAssetStillUploadingIsNotDownloadable()
        {
            Assert.Null(ReleaseJson.ExtractAssetUrl(RealAsset.Replace("STATE", "starter"), "ForestOverlay.dll"));
        }

        [Fact]
        public void FindsTheAssetInCompactJson()
        {
            string compact = "{\"tag_name\":\"v1\",\"name\":\"v1\",\"assets\":[{\"name\":\"ForestOverlay.dll\"," +
                             "\"browser_download_url\":\"https://x/ForestOverlay.dll\"}]}";

            Assert.Equal("https://x/ForestOverlay.dll", ReleaseJson.ExtractAssetUrl(compact, "ForestOverlay.dll"));
        }

        [Fact]
        public void SkipsOtherAssetsAndTheReleaseName()
        {
            string json = "{\"name\": \"ForestOverlay\", \"assets\": [" +
                          "{\"name\": \"Other.dll\", \"browser_download_url\": \"https://x/Other.dll\"}," +
                          "{\"name\": \"ForestOverlay.dll\", \"browser_download_url\": \"https://x/ForestOverlay.dll\"}]}";

            Assert.Equal("https://x/ForestOverlay.dll", ReleaseJson.ExtractAssetUrl(json, "ForestOverlay.dll"));
        }

        [Fact]
        public void NoAssetMeansNull()
        {
            string json = "{\"tag_name\": \"v1\", \"name\": \"v1\", \"assets\": []}";

            Assert.Null(ReleaseJson.ExtractAssetUrl(json, "ForestOverlay.dll"));
            Assert.Null(ReleaseJson.ExtractAssetUrl(null, "ForestOverlay.dll"));
        }

        [Fact]
        public void ReadsTheTag()
        {
            Assert.Equal("v0.16.1", ReleaseJson.ExtractString(Pretty, "tag_name"));
            Assert.Null(ReleaseJson.ExtractString(Pretty, "missing"));
        }

        [Fact]
        public void NonStringValuesAreNotStrings()
        {
            Assert.Null(ReleaseJson.ExtractString(Pretty, "id"));
        }

        [Fact]
        public void ExplainsARateLimit()
        {
            // Verbatim shape of the 403 body GitHub sends.
            string json = "{\"message\":\"API rate limit exceeded for 1.2.3.4. (But here's the good news: " +
                          "Authenticated requests get a higher rate limit.)\",\"documentation_url\":\"https://docs.github.com\"}";

            Assert.Contains("hourly limit", ReleaseJson.DescribeError(json));
        }

        [Fact]
        public void ExplainsOtherErrors()
        {
            Assert.Equal("no release published yet", ReleaseJson.DescribeError("{\"message\": \"Not Found\"}"));
            Assert.Equal("GitHub said: Server Error", ReleaseJson.DescribeError("{\"message\": \"Server Error\"}"));
            Assert.Equal("could not read latest release", ReleaseJson.DescribeError("<html>captive portal</html>"));
        }

        [Theory]
        [InlineData("0.16.1", "0.16.0", 1)]
        [InlineData("0.16.0", "0.16.1", -1)]
        [InlineData("0.16", "0.16.0", 0)]
        [InlineData("0.10.0", "0.9.9", 1)]
        [InlineData("1.0.0-beta", "0.99.0", 1)]
        public void ComparesVersionsNumerically(string a, string b, int expected)
        {
            Assert.Equal(expected, ReleaseJson.CompareVersions(a, b));
        }
    }
}
