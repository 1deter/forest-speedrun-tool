using System.Text.RegularExpressions;

namespace ForestSite;

// ------------------------------------------------------------------
// The Steam build's game code, type by type (run mode phase 3,
// docs/run-mode.md). A run report from a changed game carries a hash per
// top-level type (Game/RunIntegrity.HashTypes); comparing them with the
// Steam build's table names what was changed, by area in plain words.
//
//   GameCode/steam-types.txt  "<type> <16 hex>" a line, made in game by
//                             RunIntegrity.WriteTypeHashes (bridge) on a
//                             clean install; "#" lines are comments
//   GameCode/areas.txt        "<regex> = <area>", first match wins
// ------------------------------------------------------------------
public sealed class GameCode
{
    public const string Other = "other game code";

    private readonly Dictionary<string, string> _known;
    private readonly List<(Regex pattern, string area)> _areas;

    public GameCode(IEnumerable<string> table, IEnumerable<string> areas)
    {
        _known = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in table)
            if (Split(line) is { } t) _known[t.type] = t.hash;
        _areas = new List<(Regex, string)>();
        foreach (string raw in areas)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int eq = line.LastIndexOf(" = ", StringComparison.Ordinal);
            if (eq < 0) continue;
            _areas.Add((new Regex(line.Substring(0, eq), RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)), line.Substring(eq + 3).Trim()));
        }
    }

    private static readonly Lazy<GameCode> _steam = new(() =>
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "GameCode");
        string Read(string f) => File.Exists(Path.Combine(dir, f)) ? File.ReadAllText(Path.Combine(dir, f)) : "";
        return new GameCode(Read("steam-types.txt").Split('\n'), Read("areas.txt").Split('\n'));
    });

    /// The shipped table (empty if its file is missing: nothing compares).
    public static GameCode Steam => _steam.Value;

    public int Count => _known.Count;

    public string Area(string type)
    {
        foreach (var (pattern, area) in _areas)
            try { if (pattern.IsMatch(type)) return area; }
            catch (RegexMatchTimeoutException) { }
        return Other;
    }

    public sealed record Diff(List<string> Changed, List<string> Added, List<string> Missing);

    /// The report's type hashes against the table.
    public Diff Compare(IEnumerable<string> reported)
    {
        var changed = new List<string>();
        var added = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in reported)
        {
            if (Split(line) is not { } t) continue;
            seen.Add(t.type);
            if (!_known.TryGetValue(t.type, out string hash)) added.Add(t.type);
            else if (!string.Equals(hash, t.hash, StringComparison.OrdinalIgnoreCase)) changed.Add(t.type);
        }
        var missing = _known.Keys.Where(k => !seen.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        return new Diff(changed, added, missing);
    }

    /// Types grouped by area, the biggest area first: "area: A, B, C".
    public List<(string area, List<string> types)> ByArea(IEnumerable<string> types) =>
        types.GroupBy(Area).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
             .Select(g => (g.Key, g.OrderBy(x => x, StringComparer.Ordinal).ToList())).ToList();

    private static (string type, string hash)? Split(string line)
    {
        line = line.Trim();
        if (line.Length == 0 || line.StartsWith('#')) return null;
        int sp = line.LastIndexOf(' ');
        if (sp <= 0 || sp == line.Length - 1) return null;
        return (line.Substring(0, sp), line.Substring(sp + 1));
    }
}
