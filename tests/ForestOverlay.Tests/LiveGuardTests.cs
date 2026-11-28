using System;
using System.Diagnostics;
using System.IO;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // The live session's member guard (T-0490): a remote session drives
    // the runner's game, never the runner's machine. A wrong "allowed"
    // here is a remote file delete or a process start on a stranger's PC.
    // ------------------------------------------------------------------
    public class LiveGuardTests
    {
        private static bool Allows(Type t, string member)
        {
            return LiveGuard.AllowsMember(t.Assembly.GetName().Name, t.FullName, member);
        }

        [Fact]
        public void ProcessStart_Refused()
        {
            Assert.False(Allows(typeof(Process), "Start"));
            Assert.False(LiveGuard.AllowsType(typeof(Process).Assembly.GetName().Name, typeof(Process).FullName));
        }

        [Fact]
        public void FileDelete_Refused()
        {
            Assert.False(Allows(typeof(File), "Delete"));
            Assert.False(Allows(typeof(Directory), "Delete"));
        }

        [Fact]
        public void ReflectionAndAppDomain_Refused()
        {
            Assert.False(Allows(typeof(AppDomain), "CurrentDomain"));
            Assert.False(Allows(typeof(Type), "GetType"));
            Assert.False(Allows(typeof(System.Reflection.Assembly), "LoadFrom"));
            Assert.False(LiveGuard.AllowsAssembly("mscorlib"));
            Assert.False(LiveGuard.AllowsAssembly("System.Core"));
        }

        [Fact]
        public void ApplicationOpenUrl_Refused()
        {
            Assert.False(LiveGuard.AllowsMember("UnityEngine", "UnityEngine.Application", "OpenURL"));
            Assert.False(LiveGuard.AllowsMember("UnityEngine", "UnityEngine.Application", "CaptureScreenshot"));
            Assert.True(LiveGuard.AllowsMember("UnityEngine", "UnityEngine.Application", "isPlaying"));
        }

        [Fact]
        public void EngineNetworkAndCapture_Refused()
        {
            Assert.False(LiveGuard.AllowsType("UnityEngine", "UnityEngine.WWW"));
            Assert.False(LiveGuard.AllowsType("UnityEngine", "UnityEngine.WWWForm"));
            Assert.False(LiveGuard.AllowsType("UnityEngine", "UnityEngine.Networking.UnityWebRequest"));
            Assert.False(LiveGuard.AllowsType("UnityEngine.Networking", "UnityEngine.Networking.NetworkManager"));
            Assert.False(LiveGuard.AllowsType("UnityEngine", "UnityEngine.ScreenCapture"));
            Assert.False(LiveGuard.AllowsMember("UnityEngine", "UnityEngine.AssetBundle", "LoadFromFile"));
            Assert.False(LiveGuard.AllowsMember("UnityEngine", "UnityEngine.AssetBundle", "LoadFromMemoryAsync"));
            Assert.False(LiveGuard.AllowsMember("UnityEngine", "UnityEngine.GameObject", "SendMessage"));
            Assert.False(LiveGuard.AllowsMember("UnityEngine", "UnityEngine.Component", "BroadcastMessage"));
        }

        [Fact]
        public void OverlayAndLoaders_Refused()
        {
            Assert.False(LiveGuard.AllowsAssembly("ForestOverlay"));
            Assert.False(LiveGuard.AllowsAssembly("ForestOverlay.Updater"));
            Assert.False(LiveGuard.AllowsAssembly("BepInEx"));
            Assert.False(LiveGuard.AllowsAssembly("0Harmony"));
            Assert.False(LiveGuard.AllowsAssembly(null));
            Assert.False(LiveGuard.AllowsMember("ForestOverlay", "ForestOverlay.Modules.BridgeModule", "Enabled"));
        }

        [Fact]
        public void TransformMember_Allowed()
        {
            Assert.True(LiveGuard.AllowsMember("UnityEngine", "UnityEngine.Transform", "position"));
            Assert.True(LiveGuard.AllowsMember("UnityEngine", "UnityEngine.Transform", "SetParent"));
            Assert.True(LiveGuard.AllowsType("UnityEngine", "UnityEngine.Transform"));
        }

        [Fact]
        public void GameAndItsLibraries_Allowed()
        {
            Assert.True(LiveGuard.AllowsMember("Assembly-CSharp", "TheForest.Utils.LocalPlayer", "Transform"));
            Assert.True(LiveGuard.AllowsMember("Assembly-CSharp-firstpass", "SomeType", "field"));
            Assert.True(LiveGuard.AllowsMember("Rewired_Core", "Rewired.Player", "GetButton"));
            Assert.True(LiveGuard.AllowsMember("PlayMaker", "HutongGames.PlayMaker.Fsm", "ActiveStateName"));
            Assert.True(LiveGuard.AllowsMember("UnityEngine.UI", "UnityEngine.UI.Text", "text"));
            // A game behaviour's own SendMessage-named method is the game's.
            Assert.True(LiveGuard.AllowsMember("Assembly-CSharp", "SomeGameType", "SendMessage"));
        }
    }

    public class LiveLinkTests
    {
        [Fact]
        public void ParseOpen_ReadsTheSiteAnswer()
        {
            string code, secret, why; int idle;
            Assert.True(LiveLink.ParseOpen("{\"code\":\"ABC123\",\"secret\":\"" + new string('a', 48) + "\",\"idle\":1800}",
                                           out code, out secret, out idle, out why));
            Assert.Equal("ABC123", code);
            Assert.Equal(48, secret.Length);
            Assert.Equal(1800, idle);
            Assert.Null(why);
        }

        [Fact]
        public void ParseOpen_RefusesAnAnswerWithoutCodeOrKey()
        {
            string code, secret, why; int idle;
            Assert.False(LiveLink.ParseOpen("{\"secret\":\"" + new string('a', 48) + "\"}", out code, out secret, out idle, out why));
            Assert.NotNull(why);
            Assert.False(LiveLink.ParseOpen("{\"code\":\"ABC123\",\"secret\":\"nothex!\"}", out code, out secret, out idle, out why));
            Assert.Null(secret);
            Assert.False(LiveLink.ParseOpen(null, out code, out secret, out idle, out why));
            Assert.Equal(LiveLink.DefaultIdleSeconds, idle);
        }

        [Fact]
        public void Classify_EndsOnGoneAndRetriesOnTrouble()
        {
            Assert.Equal(LiveLink.Answer.Ok, LiveLink.Classify(200));
            Assert.Equal(LiveLink.Answer.Ok, LiveLink.Classify(204));
            Assert.Equal(LiveLink.Answer.Closed, LiveLink.Classify(410));
            Assert.Equal(LiveLink.Answer.Closed, LiveLink.Classify(404));
            Assert.Equal(LiveLink.Answer.Retry, LiveLink.Classify(0));
            Assert.Equal(LiveLink.Answer.Retry, LiveLink.Classify(503));
            Assert.Equal(LiveLink.Answer.Retry, LiveLink.Classify(429));
            Assert.Equal(LiveLink.Answer.Refused, LiveLink.Classify(413));
        }

        [Fact]
        public void FileNames_KeepToTheSitesAlphabet()
        {
            Assert.Equal("https://forest.deter.cloud/api/live/file?name=shot-1", LiveLink.FileUrl("https://forest.deter.cloud/", "shot-1"));
            Assert.Null(LiveLink.FileUrl("https://x", "a/b"));
            Assert.Null(LiveLink.FileUrl("https://x", new string('a', 65)));
            Assert.Equal("shot-120355", LiveLink.ShotName("shot-120355.png"));
            Assert.Equal("my_shot__1", LiveLink.ShotName("my shot (1"));
            Assert.Null(LiveLink.ShotName(".png"));
        }

        [Fact]
        public void IdleOver_AfterTheSitesIdle()
        {
            Assert.False(LiveLink.IdleOver(100f, 0f, 1800));
            Assert.True(LiveLink.IdleOver(1800f, 0f, 1800));
            Assert.False(LiveLink.IdleOver(5000f, 0f, 0));
        }
    }
}
