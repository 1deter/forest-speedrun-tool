using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // Lists that follow their folder (T-0254): the stamp must change with
    // any added / removed / renamed / rewritten file and only then, and the
    // folder is looked at no more often than asked.
    public class FolderStampTests
    {
        private static long H(params object[] files)
        {
            int n = files.Length / 3;
            string[] names = new string[n];
            long[] sizes = new long[n], times = new long[n];
            for (int i = 0; i < n; i++)
            {
                names[i] = (string)files[i * 3];
                sizes[i] = (long)files[i * 3 + 1];
                times[i] = (long)files[i * 3 + 2];
            }
            return FolderStamp.Hash(names, sizes, times);
        }

        [Fact]
        public void OrderAndCaseDoNotMatter()
        {
            long a = H("a.txt", 10L, 5L, "b.txt", 20L, 6L);
            Assert.Equal(a, H("b.txt", 20L, 6L, "a.txt", 10L, 5L));
            Assert.Equal(a, H("A.TXT", 10L, 5L, "b.txt", 20L, 6L));
        }

        [Fact]
        public void AnyChangeChangesIt()
        {
            long a = H("a.txt", 10L, 5L, "b.txt", 20L, 6L);
            Assert.NotEqual(a, H("a.txt", 11L, 5L, "b.txt", 20L, 6L));   // size
            Assert.NotEqual(a, H("a.txt", 10L, 7L, "b.txt", 20L, 6L));   // written
            Assert.NotEqual(a, H("c.txt", 10L, 5L, "b.txt", 20L, 6L));   // renamed
            Assert.NotEqual(a, H("a.txt", 10L, 5L));                     // removed
            Assert.NotEqual(a, H("a.txt", 10L, 5L, "b.txt", 20L, 6L, "c.txt", 0L, 0L));   // added
            // The sizes / times stay with their file, not their place.
            Assert.NotEqual(a, H("a.txt", 20L, 6L, "b.txt", 10L, 5L));
        }

        [Fact]
        public void EmptyFolderIsNotMissingOrUnreadable()
        {
            long empty = H();
            Assert.NotEqual(0L, empty);
            Assert.NotEqual(-1L, empty);
        }

        [Fact]
        public void ChangedOnlyWhenTheFilesChange()
        {
            long now = 1L;
            FolderStamp s = new FolderStamp(() => now, 2f);
            s.Take(0f);
            Assert.False(s.Changed(5f));
            now = 2L;
            Assert.True(s.Changed(10f));
            Assert.False(s.Changed(20f));   // the new stamp was kept
        }

        [Fact]
        public void LooksNoMoreOftenThanAsked()
        {
            int reads = 0;
            FolderStamp s = new FolderStamp(() => { reads++; return reads; }, 2f);
            s.Take(0f);
            Assert.Equal(1, reads);
            Assert.False(s.Changed(1.9f));
            Assert.Equal(1, reads);
            Assert.True(s.Changed(2f));
            Assert.Equal(2, reads);
            Assert.False(s.Changed(3f));
            s.CheckNow();
            Assert.True(s.Changed(3f));
            Assert.Equal(3, reads);
        }

        [Fact]
        public void NeverTakenReadsOnce()
        {
            FolderStamp s = new FolderStamp(() => 7L, 2f);
            Assert.True(s.Changed(0f));
            Assert.False(s.Changed(5f));
        }
    }
}
