using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // T-0371: the marker times, which show, and the label text.
    public class CheckpointMarksTests
    {
        [Fact]
        public void Times_AreTheSplitsThenTheEnd()
        {
            float[] into = new float[CheckpointMarks.Max];
            int n = CheckpointMarks.Times(new[] { 2.333f, 2.820f }, 6.929f, into);
            Assert.Equal(3, n);
            Assert.Equal(2.333f, into[0]);
            Assert.Equal(2.820f, into[1]);
            Assert.Equal(6.929f, into[2]);
        }

        [Fact]
        public void Times_NoSplitsIsOnlyTheEnd()
        {
            float[] into = new float[CheckpointMarks.Max];
            Assert.Equal(1, CheckpointMarks.Times(null, 5f, into));
            Assert.Equal(5f, into[0]);
            Assert.Equal(1, CheckpointMarks.Times(new float[0], 5f, into));
        }

        [Fact]
        public void Times_NeverPastTheArray()
        {
            float[] into = new float[4];
            float[] many = new float[10];
            Assert.Equal(4, CheckpointMarks.Times(many, 9f, into));
            Assert.Equal(9f, into[3]);
        }

        [Fact]
        public void Shown_WindowOnlyWhenOn()
        {
            Assert.True(CheckpointMarks.Shown(10f, false, 0f, 5f));
            Assert.False(CheckpointMarks.Shown(10f, true, 0f, 5f));
            Assert.True(CheckpointMarks.Shown(4f, true, 0f, 5f));
            Assert.False(CheckpointMarks.Shown(float.NaN, false, 0f, 5f));
        }

        [Fact]
        public void Row_EndIsTheLastRow_CheckpointsNeedMatchingRows()
        {
            Assert.Equal(2, CheckpointMarks.Row(2, 3, 3));
            Assert.Equal(1, CheckpointMarks.Row(1, 3, 3));
            Assert.Equal(2, CheckpointMarks.Row(0, 1, 3));    // no splits kept: only the end
            Assert.Equal(-1, CheckpointMarks.Row(0, 2, 3));
            Assert.Equal(-1, CheckpointMarks.Row(0, 3, 0));
        }

        [Fact]
        public void Text_TimeAndDelta()
        {
            Assert.Equal("2.333", CheckpointMarks.Text(2.333f, float.NaN, 3, 2));
            Assert.Equal("2.333  +0.12", CheckpointMarks.Text(2.333f, 0.12f, 3, 2));
            Assert.Equal("2.82  -0.5", CheckpointMarks.Text(2.82f, -0.5f, 2, 1));
        }

        [Fact]
        public void Fade_FullThenDownToZero()
        {
            Assert.Equal(1f, CheckpointMarks.Fade(100f, 300f, 0.25f));
            Assert.Equal(0f, CheckpointMarks.Fade(300f, 300f, 0.25f));
            Assert.Equal(0.5f, CheckpointMarks.Fade(262.5f, 300f, 0.25f), 3);
        }
    }
}
