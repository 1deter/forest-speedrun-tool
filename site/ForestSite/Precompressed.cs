using System.IO.Compression;

namespace ForestSite;

/// The 3D world's .bin / .json compressed once, on upload, beside the file
/// (`x.bin.br`, `x.bin.gz`), and served in its place to a client that takes
/// it - Brotli first (~10% smaller meshes than gzip), gzip otherwise:
/// Cloudflare does not compress application/octet-stream, and per request
/// would cost the VPS the same work every time.
public static class Precompressed
{
    /// The copies, in the order they are preferred: (file suffix, Content-Encoding).
    public static readonly (string Suffix, string Coding)[] Codings = { (".br", "br"), (".gz", "gzip") };

    /// Whether a world file is worth a copy (the textures are JPEG / PNG already).
    public static bool Compressible(string path) =>
        path.EndsWith(".bin", StringComparison.Ordinal) || path.EndsWith(".json", StringComparison.Ordinal);

    /// Writes `path.br` and `path.gz` where each comes out smaller, else
    /// removes a stale one (an upload over an older file must never leave its
    /// old copy served). Each is written to a temporary name and moved into
    /// place, so a request never gets half a file. Returns how many copies
    /// are there now.
    public static int Write(string path)
    {
        int n = 0;
        foreach (var (suffix, _) in Codings)
            if (WriteOne(path, suffix)) n++;
        return n;
    }

    /// The upload before Brotli wrote only `.gz`: the missing `.br` copies of
    /// the files in dir (once, at startup, in the background). Returns how
    /// many were written.
    public static int Backfill(string dir)
    {
        int n = 0;
        if (!Directory.Exists(dir)) return 0;
        foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            if (Compressible(f) && !File.Exists(f + ".br") && WriteOne(f, ".br")) n++;
        return n;
    }

    static bool WriteOne(string path, string suffix)
    {
        string to = path + suffix, tmp = to + ".tmp";
        using (var from = File.OpenRead(path))
        using (var file = File.Create(tmp))
        using (Stream z = suffix == ".br"
            ? new BrotliStream(file, CompressionLevel.SmallestSize)
            : new GZipStream(file, CompressionLevel.SmallestSize))
            from.CopyTo(z);
        if (new FileInfo(tmp).Length < new FileInfo(path).Length)
        {
            File.Move(tmp, to, true);
            return true;
        }
        File.Delete(tmp);
        if (File.Exists(to)) File.Delete(to);
        return false;
    }

    /// An Accept-Encoding header that takes gzip.
    public static bool AcceptsGzip(string header) => Accepts(header, "gzip");

    /// An Accept-Encoding header that takes coding: named (not with q=0), or
    /// "*" when it is not named.
    public static bool Accepts(string header, string coding)
    {
        if (string.IsNullOrEmpty(header)) return false;
        bool? named = null, star = null;
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
            if (name.Equals(coding, StringComparison.OrdinalIgnoreCase)) named = taken;
            else if (name == "*") star = taken;
        }
        return named ?? star ?? false;
    }
}
