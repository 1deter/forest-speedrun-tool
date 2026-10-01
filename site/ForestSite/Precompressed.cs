using System.IO.Compression;

namespace ForestSite;

/// The 3D world's .bin / .json gzipped once, on upload, beside the file
/// (`x.bin.gz`), and served in its place to a client that accepts gzip:
/// Cloudflare does not compress application/octet-stream, and per request
/// would cost the VPS the same work every time.
public static class Precompressed
{
    /// Whether a world file is worth a .gz (the textures are JPEG / PNG already).
    public static bool Compressible(string path) =>
        path.EndsWith(".bin", StringComparison.Ordinal) || path.EndsWith(".json", StringComparison.Ordinal);

    /// Writes `path.gz` when it comes out smaller, else removes a stale one
    /// (an upload over an older file must never leave its old .gz served).
    /// Returns whether a .gz is there now.
    public static bool Write(string path)
    {
        string gz = path + ".gz";
        using (var from = File.OpenRead(path))
        using (var to = File.Create(gz))
        using (var z = new GZipStream(to, CompressionLevel.SmallestSize))
            from.CopyTo(z);
        if (new FileInfo(gz).Length < new FileInfo(path).Length) return true;
        File.Delete(gz);
        return false;
    }

    /// An Accept-Encoding header that takes gzip: named (not with q=0), or
    /// "*" when gzip is not named.
    public static bool AcceptsGzip(string header)
    {
        if (string.IsNullOrEmpty(header)) return false;
        bool? gzip = null, star = null;
        foreach (string part in header.Split(','))
        {
            string[] p = part.Split(';');
            string name = p[0].Trim();
            bool taken = true;
            for (int i = 1; i < p.Length; i++)
            {
                string q = p[i].Trim();
                if (q.StartsWith("q=", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(q.Substring(2), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double v) && v <= 0)
                    taken = false;
            }
            if (name.Equals("gzip", StringComparison.OrdinalIgnoreCase)) gzip = taken;
            else if (name == "*") star = taken;
        }
        return gzip ?? star ?? false;
    }
}
