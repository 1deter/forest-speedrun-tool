using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // The ModAPI mark on a .run (T-0295): written only when set, read back, older runs have none.
    public class RunModApiFlagTests
    {
        private static Attempt OneSample()
        {
            var a = new Attempt { AnchorLabel = "s-1", Duration = 1f };
            RunSample s;
            s.T = 0f; s.P = new Vector3(1, 2, 3); s.Speed = 0f;
            a.Samples.Add(s);
            return a;
        }

        [Fact]
        public void RoundTrips()
        {
            Attempt a = OneSample();
            a.ModApi = true;
            string text = AttemptFormat.Write(a);
            Assert.Contains("flags|ModAPI Detected", text);
            Assert.True(AttemptFormat.Parse(text.Split((char)10)).ModApi);
        }

        [Fact]
        public void CleanRunHasNoLine()
        {
            string text = AttemptFormat.Write(OneSample());
            Assert.DoesNotContain("flags|", text);
            Assert.False(AttemptFormat.Parse(text.Split((char)10)).ModApi);
        }

        [Fact]
        public void AnUnknownFlagIsIgnored()
        {
            string text = AttemptFormat.Write(OneSample()).Replace("duration|", "flags|something else" + (char)10 + "duration|");
            Assert.False(AttemptFormat.Parse(text.Split((char)10)).ModApi);
        }
    }
}
