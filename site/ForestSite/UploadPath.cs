using System.Text.RegularExpressions;

namespace ForestSite;

/// What an owner's map upload may contain - never a path out of its folder.
public static class UploadPath
{
    // \z, not $ ($ also matches before a final "\n"); [0-9], not \d (\d is
    // every Unicode digit). Each name is a whole relative path, never "..".
    private static readonly Regex Tile = new(@"^(canopy|ground)(-dry)?/[0-9]{1,2}/[0-9]{1,5}_[0-9]{1,5}\.jpg\z");
    private static readonly Regex World = new(@"^((m|p|q|b)/[0-9]{1,6}\.bin|t/[0-9]{1,6}\.(jpg|png)|c/(surface|caves|endgame)_-?[0-9]{1,3}_-?[0-9]{1,3}(_L)?\.bin)\z");

    /// Aerial photo tiles: `<layer>/<level>/<x>_<y>.jpg` and `aerial.json`.
    public static bool IsTile(string path) => path == "aerial.json" || Tile.IsMatch(path);

    /// The 3D world (scripts/world-extract.py): `world.json`, meshes
    /// `m/<i>.bin` (or packs of them, `p/<i>.bin`), textures `t/<i>.jpg` (+ `.png` with alpha;
    /// or packs of them, `q/<i>.bin`),
    /// instance chunks `c/<area>_<x>_<z>.bin`; since version 3 all of them in
    /// `b/<i>.bin` (scripts/world_pack.py). Never a `.gz`: the server makes those.
    public static bool IsWorld(string path) => path == "world.json" || World.IsMatch(path);
}
