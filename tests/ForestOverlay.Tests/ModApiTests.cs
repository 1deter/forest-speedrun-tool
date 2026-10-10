using System.Reflection;
using ForestOverlay.Game;
using Xunit;

namespace ForestOverlay.Tests
{
    // T-0246: whether the plugin runs under ModAPI this launch.
    public class ModApiTests
    {
        [Fact]
        public void NoModApi_Runs_WhateverTheKey()
        {
            Assert.Equal(ModApi.Repair.NoModApi, ModApi.Decide(false, null));
            Assert.Equal(ModApi.Repair.NoModApi, ModApi.Decide(false, -1));
        }

        [Fact]
        public void ModApi_PatcherNotRun_NeedsRestart()
        {
            Assert.Equal(ModApi.Repair.NeedsRestart, ModApi.Decide(true, null));
        }

        [Fact]
        public void ModApi_Repaired_Runs()
        {
            Assert.Equal(ModApi.Repair.Repaired, ModApi.Decide(true, 3));
            Assert.Equal(ModApi.Repair.Repaired, ModApi.Decide(true, 0));
        }

        [Fact]
        public void ModApi_RepairFailed_StaysOff()
        {
            Assert.Equal(ModApi.Repair.Failed, ModApi.Decide(true, -1));
        }

        [Fact]
        public void IsModApiBuild_ByBaseModLibReference()
        {
            Assert.True(ModApi.IsModApiBuild(new[] { new AssemblyName("UnityEngine"), new AssemblyName("BaseModLib") }));
            Assert.False(ModApi.IsModApiBuild(new[] { new AssemblyName("UnityEngine"), new AssemblyName("mscorlib") }));
            Assert.False(ModApi.IsModApiBuild(new AssemblyName[0]));
        }
    }
}
