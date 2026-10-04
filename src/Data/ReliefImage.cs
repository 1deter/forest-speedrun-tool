using System;
using System.IO;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The Map tab's picture of the island: a shaded relief made from the
    // terrain's heights, and the PNG it is cached as.
    //
    // Heights are world metres, row-major, row 0 = the south edge (the
    // terrain's and a Texture2D's order). The RGB buffer is in the same
    // order (bottom-up), so it goes straight into a texture.
    //
    // The PNG is written "stored" (deflate without compression): ~3 MB for
    // 1024 px, but encoding and decoding are a copy and a checksum, done on
    // a background thread with no Unity call - EncodeToPNG / LoadImage
    // are main-thread only and a hitch at this size. Any viewer opens it.
    // TryDecode reads only what Encode writes (8-bit RGB, stored blocks,
    // filter 0); anything else is "not our cache" and the map is rebuilt.
    //
    // Pure: linked into the tests.
    // ------------------------------------------------------------------
    public static class ReliefImage
    {
        /// The ocean's level (Ceto `Ocean.level`, docs/website.md).
        public const float SeaLevel = 41.5f;

        /// Fills rgb (w * h * 3, bottom-up) with a hill-shaded, height-
        /// coloured relief. mppX / mppZ: metres per pixel.
        public static void Shade(float[] heights, int w, int h, float mppX, float mppZ,
                                 float seaLevel, byte[] rgb)
        {
            if (heights == null || rgb == null || w < 1 || h < 1) return;
            if (heights.Length < w * h || rgb.Length < w * h * 3) return;

            // Light from the north-west, 45 degrees up (the usual relief
            // convention: slopes facing the top-left are lit).
            float lx = -1f, ly = 1.41421f, lz = 1f;
            float ll = (float)Math.Sqrt(lx * lx + ly * ly + lz * lz);
            lx /= ll; ly /= ll; lz /= ll;

            for (int z = 0; z < h; z++)
            {
                int zs = z > 0 ? z - 1 : z, zn = z < h - 1 ? z + 1 : z;
                for (int x = 0; x < w; x++)
                {
                    int xw = x > 0 ? x - 1 : x, xe = x < w - 1 ? x + 1 : x;
                    float y = heights[z * w + x];
                    float dx = (heights[z * w + xe] - heights[z * w + xw]) / (Math.Max(1, xe - xw) * mppX);
                    float dz = (heights[zn * w + x] - heights[zs * w + x]) / (Math.Max(1, zn - zs) * mppZ);

                    // Normal of y = f(x, z): (-dx, 1, -dz), normalised.
                    float nl = (float)Math.Sqrt(dx * dx + 1f + dz * dz);
                    float lit = (-dx * lx + ly - dz * lz) / nl;
                    if (lit < 0f) lit = 0f;

                    float r, g, b;
                    HeightColour(y, seaLevel, out r, out g, out b);
                    float k = y < seaLevel ? 1f : 0.35f + 0.85f * lit;

                    int o = (z * w + x) * 3;
                    rgb[o] = ToByte(r * k);
                    rgb[o + 1] = ToByte(g * k);
                    rgb[o + 2] = ToByte(b * k);
                }
            }
        }

        /// A colour for a height: sea blues by depth, then sand, forest
        /// greens, rock and snow on the peaks (values 0..1).
        public static void HeightColour(float y, float seaLevel, out float r, out float g, out float b)
        {
            if (y < seaLevel)
            {
                float d = Math.Min(1f, (seaLevel - y) / 40f);
                r = Lerp(0.20f, 0.06f, d); g = Lerp(0.42f, 0.18f, d); b = Lerp(0.58f, 0.36f, d);
                return;
            }

            float a = y - seaLevel;
            if (a < 4f) { r = 0.78f; g = 0.72f; b = 0.52f; return; }            // shore
            if (a < 90f)                                                         // forest
            {
                float t = (a - 4f) / 86f;
                r = Lerp(0.36f, 0.42f, t); g = Lerp(0.52f, 0.48f, t); b = Lerp(0.28f, 0.30f, t);
                return;
            }
            if (a < 150f)                                                        // rock
            {
                float t = (a - 90f) / 60f;
                r = Lerp(0.42f, 0.55f, t); g = Lerp(0.48f, 0.52f, t); b = Lerp(0.30f, 0.48f, t);
                return;
            }
            float s = Math.Min(1f, (a - 150f) / 25f);                            // snow
            r = Lerp(0.55f, 0.93f, s); g = Lerp(0.52f, 0.94f, s); b = Lerp(0.48f, 0.96f, s);
        }

        private static float Lerp(float a, float b, float t) { return a + (b - a) * t; }

        private static byte ToByte(float v)
        {
            int i = (int)(v * 255f + 0.5f);
            return (byte)(i < 0 ? 0 : i > 255 ? 255 : i);
        }

        // --- PNG (8-bit RGB, stored deflate) -----------------------------

        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        public static byte[] EncodePng(int w, int h, byte[] rgbBottomUp)
        {
            if (w < 1 || h < 1 || rgbBottomUp == null || rgbBottomUp.Length < w * h * 3)
                throw new ArgumentException("bad image");

            int row = w * 3;
            // Raw scanlines, top row first, each led by filter 0.
            byte[] raw = new byte[(row + 1) * h];
            for (int y = 0; y < h; y++)
            {
                int src = (h - 1 - y) * row;
                int dst = y * (row + 1);
                raw[dst] = 0;
                Buffer.BlockCopy(rgbBottomUp, src, raw, dst + 1, row);
            }

            MemoryStream z = new MemoryStream(raw.Length + raw.Length / 65535 * 5 + 16);
            z.WriteByte(0x78); z.WriteByte(0x01);
            int at = 0;
            do
            {
                int len = Math.Min(65535, raw.Length - at);
                bool final = at + len >= raw.Length;
                z.WriteByte((byte)(final ? 1 : 0));
                z.WriteByte((byte)(len & 0xFF)); z.WriteByte((byte)(len >> 8));
                z.WriteByte((byte)(~len & 0xFF)); z.WriteByte((byte)((~len >> 8) & 0xFF));
                z.Write(raw, at, len);
                at += len;
            } while (at < raw.Length);
            uint adler = Adler32(raw, 0, raw.Length);
            WriteBE(z, adler);

            MemoryStream png = new MemoryStream((int)z.Length + 64);
            png.Write(Signature, 0, Signature.Length);
            byte[] ihdr = new byte[13];
            PutBE(ihdr, 0, (uint)w); PutBE(ihdr, 4, (uint)h);
            ihdr[8] = 8; ihdr[9] = 2;   // 8 bits, RGB; compression / filter / interlace 0
            Chunk(png, "IHDR", ihdr, 0, ihdr.Length);
            byte[] zdata = z.ToArray();
            Chunk(png, "IDAT", zdata, 0, zdata.Length);
            Chunk(png, "IEND", new byte[0], 0, 0);
            return png.ToArray();
        }

        /// Reads a PNG EncodePng wrote; false for anything else.
        public static bool TryDecodePng(byte[] png, out int w, out int h, out byte[] rgbBottomUp)
        {
            w = 0; h = 0; rgbBottomUp = null;
            try
            {
                if (png == null || png.Length < 8 + 25) return false;
                for (int i = 0; i < 8; i++) if (png[i] != Signature[i]) return false;

                MemoryStream idat = new MemoryStream();
                int at = 8;
                bool header = false;
                while (at + 12 <= png.Length)
                {
                    int len = (int)GetBE(png, at);
                    if (len < 0 || at + 12 + len > png.Length) return false;
                    string type = "" + (char)png[at + 4] + (char)png[at + 5] + (char)png[at + 6] + (char)png[at + 7];
                    int data = at + 8;
                    if (Crc(png, at + 4, len + 4) != GetBE(png, data + len)) return false;
                    if (type == "IHDR")
                    {
                        if (len != 13) return false;
                        w = (int)GetBE(png, data); h = (int)GetBE(png, data + 4);
                        if (png[data + 8] != 8 || png[data + 9] != 2 || png[data + 12] != 0) return false;
                        header = true;
                    }
                    else if (type == "IDAT") idat.Write(png, data, len);
                    else if (type == "IEND") break;
                    at = data + len + 4;
                }
                if (!header || w < 1 || h < 1 || w > 8192 || h > 8192) return false;

                byte[] zd = idat.ToArray();
                int row = w * 3;
                byte[] raw = new byte[(row + 1) * h];
                if (zd.Length < 6 || zd[0] != 0x78) return false;
                int p = 2, outAt = 0;
                bool final = false;
                while (!final)
                {
                    if (p + 5 > zd.Length) return false;
                    byte bh = zd[p];
                    if ((bh & 6) != 0) return false;                 // not a stored block
                    final = (bh & 1) != 0;
                    int len = zd[p + 1] | (zd[p + 2] << 8);
                    int nlen = zd[p + 3] | (zd[p + 4] << 8);
                    if ((len ^ 0xFFFF) != nlen) return false;
                    p += 5;
                    if (p + len > zd.Length || outAt + len > raw.Length) return false;
                    Buffer.BlockCopy(zd, p, raw, outAt, len);
                    p += len; outAt += len;
                }
                if (outAt != raw.Length || p + 4 > zd.Length) return false;
                if (Adler32(raw, 0, raw.Length) != GetBE(zd, p)) return false;

                rgbBottomUp = new byte[row * h];
                for (int y = 0; y < h; y++)
                {
                    int src = y * (row + 1);
                    if (raw[src] != 0) { rgbBottomUp = null; return false; }
                    Buffer.BlockCopy(raw, src + 1, rgbBottomUp, (h - 1 - y) * row, row);
                }
                return true;
            }
            catch (Exception)
            {
                rgbBottomUp = null;
                return false;
            }
        }

        private static void Chunk(Stream s, string type, byte[] data, int offset, int len)
        {
            byte[] head = new byte[8];
            PutBE(head, 0, (uint)len);
            for (int i = 0; i < 4; i++) head[4 + i] = (byte)type[i];
            s.Write(head, 0, 8);
            s.Write(data, offset, len);
            uint crc = Crc(head, 4, 4, 0xFFFFFFFFu);
            crc = Crc(data, offset, len, crc);
            WriteBE(s, crc ^ 0xFFFFFFFFu);
        }

        private static uint[] _crcTable;

        private static uint Crc(byte[] b, int offset, int len)
        {
            return Crc(b, offset, len, 0xFFFFFFFFu) ^ 0xFFFFFFFFu;
        }

        private static uint Crc(byte[] b, int offset, int len, uint c)
        {
            uint[] t = _crcTable;
            if (t == null)
            {
                t = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint v = n;
                    for (int k = 0; k < 8; k++) v = (v & 1) != 0 ? 0xEDB88320u ^ (v >> 1) : v >> 1;
                    t[n] = v;
                }
                _crcTable = t;
            }
            for (int i = 0; i < len; i++) c = t[(c ^ b[offset + i]) & 0xFF] ^ (c >> 8);
            return c;
        }

        public static uint Adler32(byte[] b, int offset, int len)
        {
            uint a = 1, s = 0;
            int i = 0;
            while (i < len)
            {
                int n = Math.Min(5552, len - i);
                for (int k = 0; k < n; k++) { a += b[offset + i + k]; s += a; }
                a %= 65521; s %= 65521;
                i += n;
            }
            return (s << 16) | a;
        }

        private static void PutBE(byte[] b, int at, uint v)
        {
            b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v;
        }

        private static uint GetBE(byte[] b, int at)
        {
            return ((uint)b[at] << 24) | ((uint)b[at + 1] << 16) | ((uint)b[at + 2] << 8) | b[at + 3];
        }

        private static void WriteBE(Stream s, uint v)
        {
            s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v);
        }
    }
}
