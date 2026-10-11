using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // Other mods beside the game (T-0308): the names, the HUD marker, the .run flag.
    public class ExternalModsTests
    {
        [Fact]
        public void NamesComeFromAllFourLists()
        {
            var into = new List<string>();
            ExternalMods.Collect(
                new[] { "Cool Cheats 1.2.3 (CoolCheats.dll)", "could not list the plugins: x" },
                new[] { "Patcher.dll" },
                new[] { "Helper.dll", "Patcher.dll" },
                new[] { "Player.Update (by com.other.mod)", "Foo.Bar (by com.other.mod)" }, into);
            Assert.Equal(new[] { "Cool Cheats", "Patcher.dll", "Helper.dll", "com.other.mod" }, into.ToArray());
        }

        [Fact]
        public void NothingExternalNamesNothing()
        {
            var into = new List<string>();
            ExternalMods.Collect(new string[0], new string[0], new string[0], new string[0], into);
            Assert.Empty(into);
            Assert.Equal("", ExternalMods.Join(into));
        }

        [Fact]
        public void JoinCapsTheNames()
        {
            Assert.Equal("a, b, c, d +2 more", ExternalMods.Join(new[] { "a", "b", "c", "d", "e", "f" }));
            Assert.Equal("a, b", ExternalMods.Join(new[] { "a", "b" }));
        }

        [Fact]
        public void FlagRoundTripsThroughTheFlagList()
        {
            Assert.Equal("A, B", ExternalMods.FromFlags(new[] { "x", ExternalMods.Flag("A, B") }));
            Assert.Equal("", ExternalMods.FromFlags(new[] { "ModAPI Detected" }));
            Assert.Equal(ExternalMods.FlagPrefix, RunReport.ModsFlagPrefix);
            Assert.Equal(ExternalMods.FlagPrefix, AttemptFormat.ModsFlagPrefix);
        }

        [Fact]
        public void ALaterLongerFlagWins()
        {
            var flags = new[] { ExternalMods.Flag("A"), "other", ExternalMods.Flag("A, B") };
            Assert.Equal("A, B", ExternalMods.FromFlags(flags));
            Assert.Equal("A, B", RunReport.ModsOf(flags));
        }

        [Fact]
        public void MarkerPutsModsUnderTheModApiFlag()
        {
            Assert.Equal("", HudLines.Marker(false, "", null, null));
            Assert.Equal("Mods: A", HudLines.Marker(false, "A", null, null));
            Assert.Equal("ModAPI\nMods: A", HudLines.Marker(true, "A", null, null));
            Assert.Equal("ModAPI\nMods: A\nteleport", HudLines.Marker(true, "A", null, "teleport: Cave 5 (top)"));
            Assert.Equal("RUN MODE - attempt 1", HudLines.Marker(true, "A", "RUN MODE - attempt 1", null));
        }

        private static Attempt OneSample()
        {
            var a = new Attempt { AnchorLabel = "s-1", Duration = 1f };
            RunSample s;
            s.T = 0f; s.P = new Vector3(1, 2, 3); s.Speed = 0f;
            a.Samples.Add(s);
            return a;
        }

        [Fact]
        public void RunFileRoundTripsAlongsideModApi()
        {
            Attempt a = OneSample();
            a.Mods = "A, B +1 more";
            string only = AttemptFormat.Write(a);
            Assert.Contains("flags|Mods Detected: A, B +1 more", only);
            Attempt back = AttemptFormat.Parse(only.Split((char)10));
            Assert.Equal("A, B +1 more", back.Mods);
            Assert.False(back.ModApi);

            a.ModApi = true;
            back = AttemptFormat.Parse(AttemptFormat.Write(a).Split((char)10));
            Assert.True(back.ModApi);
            Assert.Equal("A, B +1 more", back.Mods);
        }

        [Fact]
        public void OldRunsHaveNoMods()
        {
            Assert.Equal("", AttemptFormat.Parse(AttemptFormat.Write(OneSample()).Split((char)10)).Mods);
        }
    }
}
