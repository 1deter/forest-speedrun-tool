using System.Text.RegularExpressions;

namespace ForestSite;

/// What an aerial tile upload may contain: `<layer>/<level>/<x>_<y>.jpg`
/// and `aerial.json` - never a path out of the folder.
public static class AerialPath
{
    private static readonly Regex Tile = new(@"^(canopy|ground)(-dry)?/\d{1,2}/\d{1,5}_\d{1,5}\.jpg$");

    public static bool IsTile(string path) => path == "aerial.json" || Tile.IsMatch(path);
}
