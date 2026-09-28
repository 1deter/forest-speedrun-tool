using System.Text.RegularExpressions;

namespace ForestSite;

/// What an owner's map upload may contain - never a path out of its folder.
public static class UploadPath
{
    private static readonly Regex Tile = new(@"^(canopy|ground)(-dry)?/\d{1,2}/\d{1,5}_\d{1,5}\.jpg$");
    private static readonly Regex World = new(@"^(m/\d{1,6}\.bin|t/\d{1,6}\.(jpg|png)|c/(surface|caves|endgame)_-?\d{1,3}_-?\d{1,3}\.bin)$");

    /// Aerial photo tiles: `<layer>/<level>/<x>_<y>.jpg` and `aerial.json`.
    public static bool IsTile(string path) => path == "aerial.json" || Tile.IsMatch(path);

    /// The 3D world (scripts/world-extract.py): `world.json`, meshes
    /// `m/<i>.bin`, textures `t/<i>.jpg` (+ `.png` with alpha), instance chunks `c/<area>_<x>_<z>.bin`.
    public static bool IsWorld(string path) => path == "world.json" || World.IsMatch(path);
}
