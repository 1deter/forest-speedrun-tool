using System.Text.RegularExpressions;
using Xunit;

namespace ForestSite.Tests;

// Gotcha 66 (T-0130): uploaded sets (the aerial tiles, the 3D world) are cached for
// a day, so every file URL the pages build carries ?v=<build>. Only the set's own
// json (fetched no-cache, it holds the build) may go without.
public sealed class UploadedSetUrlTests
{
    private static string WwwRoot()
    {
        string root = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(root, "ForestSite", "wwwroot"))) root = Path.GetDirectoryName(root);
        return Path.Combine(root, "ForestSite", "wwwroot");
    }

    // A string literal that starts an uploaded-set path: "/aerial/..." or "/world/...".
    private static readonly Regex SetUrl = new(@"[""'`]/(aerial|world)/[^""'`]*[""'`]", RegexOptions.Compiled);
    private static readonly Regex Versioned = new(@"\?v=|\bthis\.v\b|\.meta\.v\b|\.v\b\s*[,;)]", RegexOptions.Compiled);

    [Fact]
    public void EveryUploadedSetFileUrlCarriesTheBuild()
    {
        var bad = new List<string>();
        int seen = 0;
        foreach (string js in Directory.GetFiles(WwwRoot(), "*.js"))
        {
            string[] lines = File.ReadAllLines(js);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.TrimStart().StartsWith("//")) continue;
                foreach (Match m in SetUrl.Matches(line))
                {
                    if (m.Value.EndsWith(".json\"") || m.Value.EndsWith(".json'")) continue;   // the meta: no-cache
                    seen++;
                    if (!Versioned.IsMatch(line.Substring(m.Index)))
                        bad.Add(Path.GetFileName(js) + ":" + (i + 1) + "  " + line.Trim());
                }
            }
        }
        Assert.True(seen >= 3, "the scan found only " + seen + " uploaded-set URLs - did the pattern stop matching? (aerial tiles in map.js and map3d.js, world files in world3d.js)");
        Assert.True(bad.Count == 0,
            "WHAT: an uploaded-set file URL without ?v=<build>.\nWHY: browsers and Cloudflare keep it a day (gotcha 66), so a re-upload shows old tiles.\nFIX: append the build (meta.build -> '?v=' + build) like map.js / world3d.js do.\n" + string.Join("\n", bad));
    }

    [Fact]
    public void TheScanSeesAnUnversionedUrl()
    {
        Assert.Matches(SetUrl, "img.src = \"/aerial/\" + layer + \"/0.jpg\";");
        Assert.DoesNotMatch(Versioned, "img.src = \"/aerial/\" + layer + \"/0.jpg\";");
        Assert.Matches(Versioned, "fetch(\"/world/\" + file + this.v)");
    }
}
