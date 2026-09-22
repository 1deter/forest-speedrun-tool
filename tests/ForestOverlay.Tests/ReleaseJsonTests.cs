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
