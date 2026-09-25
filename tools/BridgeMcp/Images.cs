using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ForestOverlay.BridgeMcp
{
    // ------------------------------------------------------------------
    // Screenshots for the model: the game writes a full-size PNG (a few
    // MB at 1440p); what goes back is a scaled JPEG, optionally a crop of
    // the original first (to read small HUD text at full resolution).
    // ------------------------------------------------------------------
    internal static class Images
    {
        public sealed class Prepared
        {
            public byte[] Data;
            public string Mime;
            public int SourceWidth, SourceHeight, Width, Height;
        }

        /// The file's bytes once it decodes - Unity writes it at the end of
        /// a frame, so a first read can find it missing or half written.
        public static async Task<byte[]> ReadWhenComplete(string path, TimeSpan within, CancellationToken ct)
        {
            DateTime until = DateTime.UtcNow + within;
            Exception last = null;
            while (true)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        byte[] bytes = await File.ReadAllBytesAsync(path, ct);
                        using (MemoryStream ms = new MemoryStream(bytes))
                        using (Image probe = Image.FromStream(ms)) { }
                        return bytes;
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException)
                {
                    last = ex;
                }
                if (DateTime.UtcNow > until)
                    throw new IOException("screenshot never appeared complete at " + path +
                                          (last != null ? " (" + last.Message + ")" : ""));
                await Task.Delay(200, ct);
            }
        }

        /// Crops (source pixels; null = whole image), then scales down to
        /// maxWidth (never up) and encodes a JPEG, or a PNG when asked.
        public static Prepared Prepare(byte[] png, int maxWidth, Rectangle? region, bool asPng, int quality)
        {
            using MemoryStream ms = new MemoryStream(png);
            using Bitmap src = new Bitmap(ms);
            Rectangle crop = new Rectangle(0, 0, src.Width, src.Height);
            if (region.HasValue)
            {
                crop = Rectangle.Intersect(crop, region.Value);
                if (crop.Width <= 0 || crop.Height <= 0)
                    throw new ArgumentException("region is outside the " + src.Width + "x" + src.Height + " screenshot");
            }

            int w = crop.Width, h = crop.Height;
            if (maxWidth > 0 && w > maxWidth)
            {
                h = Math.Max(1, (int)Math.Round(h * (double)maxWidth / w));
                w = maxWidth;
            }

            using Bitmap dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, w, h), crop, GraphicsUnit.Pixel);
            }

            using MemoryStream outMs = new MemoryStream();
            if (asPng) dst.Save(outMs, ImageFormat.Png);
            else
            {
                ImageCodecInfo jpeg = Array.Find(ImageCodecInfo.GetImageEncoders(), c => c.FormatID == ImageFormat.Jpeg.Guid);
                using EncoderParameters ep = new EncoderParameters(1);
                ep.Param[0] = new EncoderParameter(Encoder.Quality, (long)Math.Clamp(quality, 20, 100));
                dst.Save(outMs, jpeg, ep);
            }
            return new Prepared
            {
                Data = outMs.ToArray(),
                Mime = asPng ? "image/png" : "image/jpeg",
                SourceWidth = src.Width, SourceHeight = src.Height, Width = w, Height = h,
            };
        }
    }
}
