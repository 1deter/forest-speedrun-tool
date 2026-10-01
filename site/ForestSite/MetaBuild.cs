using System.Text.Json;

namespace ForestSite;

/// The "build" stamp of an uploaded map's json (aerial.json / world.json:
/// the export time, which every file URL carries as ?v=), re-read when the
/// file changes. Null while there is none - nothing uploaded, an older
/// export without a stamp, or not JSON.
public sealed class MetaBuild
{
    private readonly string _path;
    private readonly object _gate = new();
    private DateTime _stamp = DateTime.MinValue;
    private long _length = -1;
    private string _build;

    public MetaBuild(string path) { _path = path; }

    /// Whether a file asked for with ?v=<v> belongs to what is uploaded: the
    /// same build, or a json without a stamp (an older export). No json at
    /// all (an upload under way: it goes last) accepts none.
    public bool Accepts(string v)
    {
        if (!File.Exists(_path)) return false;
        string now = Current();
        return now == null || now == v;
    }

    public string Current()
    {
        var f = new FileInfo(_path);
        if (!f.Exists) return null;
        lock (_gate)
        {
            if (f.LastWriteTimeUtc == _stamp && f.Length == _length) return _build;
            _stamp = f.LastWriteTimeUtc;
            _length = f.Length;
            _build = Read(_path);
            return _build;
        }
    }

    private static string Read(string path)
    {
        try
        {
            using var s = File.OpenRead(path);
            using var doc = JsonDocument.Parse(s);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("build", out var b)
                && b.ValueKind == JsonValueKind.Number ? b.GetRawText() : null;
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return null;
        }
    }
}
