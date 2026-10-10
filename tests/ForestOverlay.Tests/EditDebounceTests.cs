using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // The Practice autosave (T-0217): one write once the edits pause, and
    // a long drag still written every MaxWait.
    public class EditDebounceTests
    {
        [Fact]
        public void NothingEditedIsNeverDue()
        {
            EditDebounce d = new EditDebounce(1f, 5f);
            Assert.False(d.Pending);
            Assert.False(d.Due(100f));
        }

        [Fact]
        public void DueOnceAfterTheQuietTime()
        {
            EditDebounce d = new EditDebounce(1f, 5f);
            d.Edit(10f);

            Assert.True(d.Pending);
            Assert.False(d.Due(10.5f));
            Assert.True(d.Due(11f));
            Assert.False(d.Pending);
            Assert.False(d.Due(12f));
        }

        [Fact]
        public void EachEditPushesTheWriteOut()
        {
            EditDebounce d = new EditDebounce(1f, 5f);
            d.Edit(10f);
            d.Edit(10.8f);

            Assert.False(d.Due(11.5f));
            Assert.True(d.Due(11.8f));
        }

        [Fact]
        public void AnEditEveryFrameIsWrittenAtMaxWait()
        {
            EditDebounce d = new EditDebounce(1f, 5f);
            float t = 10f;
            int writes = 0;
            for (int frame = 0; frame < 720; frame++, t += 1f / 60f)   // a 12 s drag
            {
                d.Edit(t);
                if (d.Due(t)) writes++;
            }

            Assert.Equal(2, writes);   // at 15 s and about 20 s, not 720
        }

        [Fact]
        public void ClearDropsTheWait()
        {
            EditDebounce d = new EditDebounce(1f, 5f);
            d.Edit(10f);
            d.Clear();

            Assert.False(d.Pending);
            Assert.False(d.Due(20f));
        }

        [Fact]
        public void MaxWaitIsNeverShorterThanQuiet()
        {
            EditDebounce d = new EditDebounce(2f, 1f);
            d.Edit(10f);

            Assert.False(d.Due(11.5f));
            Assert.True(d.Due(12f));
        }
    }
}
