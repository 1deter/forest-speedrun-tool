using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // The QA report is sent by testers; a zip Windows cannot open would
    // lose their session. Read back with the framework's own reader.
    public class ZipWriterTests
    {
        [Fact]
        public void Crc32MatchesKnownValue()
        {
            Assert.Equal(0xCBF43926u, ZipWriter.Crc32(Encoding.ASCII.GetBytes("123456789")));
        }

        [Fact]
        public void RoundTripsThroughZipArchive()
        {
            byte[] binary = new byte[70000];
            new Random(1).NextBytes(binary);

            MemoryStream ms = new MemoryStream();
            using (ZipWriter zip = new ZipWriter(ms))
            {
                zip.Add("report.txt", "Tester: maks\n1) [pass] ...\n", new DateTime(2026, 9, 25, 17, 30, 12));
                zip.Add("logs\\LogOutput-2026-09-25_17-00-00.log", binary, DateTime.Now);
                zip.Add("savestates/segments/spot.é.fosave", new byte[0], DateTime.Now);
            }

            ms.Position = 0;
            using (ZipArchive a = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                Assert.Equal(3, a.Entries.Count);
                Assert.Equal("report.txt", a.Entries[0].FullName);
                Assert.Equal(new DateTime(2026, 9, 25, 17, 30, 12), a.Entries[0].LastWriteTime.DateTime);
                using (StreamReader r = new StreamReader(a.Entries[0].Open()))
                    Assert.Equal("Tester: maks\n1) [pass] ...\n", r.ReadToEnd());

                Assert.Equal("logs/LogOutput-2026-09-25_17-00-00.log", a.Entries[1].FullName);
                MemoryStream back = new MemoryStream();
                using (Stream s = a.Entries[1].Open()) s.CopyTo(back);
                Assert.Equal(binary, back.ToArray());

                Assert.Equal("savestates/segments/spot.é.fosave", a.Entries[2].FullName);
                Assert.Equal(0, a.Entries[2].Length);
            }
        }
    }
}
