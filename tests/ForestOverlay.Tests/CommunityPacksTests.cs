using System.Collections.Generic;
using System.IO;
using System.Text;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // The repo's community/ folder is downloaded by every install: a pack
    // that does not parse, or an index that is out of date (forgot to run
    // scripts/community-index.py), fails CI here instead of in game.
    // ------------------------------------------------------------------
    public class CommunityPacksTests
    {
        private static string Folder()
        {
            string dir = Directory.GetCurrentDirectory();
            while (dir != null)
            {
                string c = Path.Combine(dir, "community");
                if (File.Exists(Path.Combine(c, CommunityIndex.IndexFile))) return c;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        [Fact]
        public void EveryPackParsesAndTheIndexIsCurrent()
        {
            string folder = Folder();
            Assert.NotNull(folder);

            int bad;
            List<CommunityIndex.Entry> index = CommunityIndex.Parse(
                File.ReadAllText(Path.Combine(folder, CommunityIndex.IndexFile), Encoding.UTF8), out bad);
            Assert.Equal(0, bad);

            string[] files = Directory.GetFiles(folder, "*" + SegmentBundle.Extension);
            Assert.Equal(files.Length, index.Count);

            HashSet<string> ids = new HashSet<string>();
            foreach (CommunityIndex.Entry e in index)
            {
                string path = Path.Combine(folder, e.File);
                Assert.True(File.Exists(path), e.File + " is in the index but not in the folder");
                string text = File.ReadAllText(path, Encoding.UTF8);
                Assert.True(CommunityIndex.HashOf(text) == e.Hash, e.File + ": index hash is stale - run scripts/community-index.py");

                string error;
                SegmentBundle b = SegmentBundle.Parse(text, out error, null);
                Assert.True(b != null, e.File + ": " + error);
                Assert.True(ids.Add(b.Segment.Id), e.File + ": id " + b.Segment.Id + " is used by another pack");
                Assert.True(b.Segment.IsValid, e.File + ": needs a spawn, or a start and an end");
                Assert.Empty(b.Attempts);
            }
        }
    }
}
