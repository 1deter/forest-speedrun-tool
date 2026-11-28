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
        public void AllowedMemberOnAnOverlayObject_Refused()
        {
            // Behaviour.enabled is UnityEngine's, but the receiver is the overlay's.
            Assert.True(LiveGuard.AllowsMember("UnityEngine", "UnityEngine.Behaviour", "enabled"));
            Assert.False(LiveGuard.AllowsReceiver("ForestOverlay", "ForestOverlay.OverlayPlugin"));
            Assert.False(LiveGuard.AllowsReceiver("BepInEx", "BepInEx.Bootstrap.Chainloader"));
            Assert.False(LiveGuard.AllowsReceiver("mscorlib", typeof(Type).FullName));
            Assert.True(LiveGuard.AllowsReceiver("UnityEngine", "UnityEngine.Transform"));
            Assert.True(LiveGuard.AllowsReceiver("Assembly-CSharp", "FirstPersonCharacter"));
        }

        [Fact]
        public void SafeSystemTypes_AllowedAsReceivers()
        {
            string ms = "mscorlib";   // the game's Mono (.NET 8 names it System.Private.CoreLib)
            Assert.True(LiveGuard.AllowsMember(ms, typeof(string).FullName, "Length"));
            Assert.True(LiveGuard.AllowsReceiver(ms, typeof(System.Collections.Generic.List<int>).FullName));
            Assert.True(LiveGuard.AllowsReceiver(ms, typeof(System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<string>>).FullName));
            Assert.True(LiveGuard.AllowsReceiver(ms, typeof(int[]).FullName));
            Assert.True(LiveGuard.AllowsMember(ms, typeof(System.Collections.Generic.List<int>).FullName, "Count"));
            Assert.True(LiveGuard.AllowsMember(ms, typeof(System.Collections.Generic.List<int>).FullName, "get_Item"));
        }

        [Fact]
        public void OtherSystemTypes_StillRefused()
        {
            string ms = "mscorlib";   // the game's Mono (.NET 8 names it System.Private.CoreLib)
            foreach (Type t in new[] { typeof(Type), typeof(Delegate), typeof(Environment), typeof(Activator), typeof(StringComparer),
                                       typeof(System.Text.StringBuilder), typeof(System.IO.Stream), typeof(object), typeof(Convert),
                                       typeof(System.Collections.ArrayList) })
            {
                Assert.False(LiveGuard.AllowsReceiver(ms, t.FullName), t.FullName);
                Assert.False(LiveGuard.AllowsType(ms, t.FullName), t.FullName);
            }
            Assert.False(LiveGuard.AllowsType("System.Core", "System.String"));
            Assert.False(LiveGuard.AllowsType("ForestOverlay", "System.String"));
            Assert.False(LiveGuard.AllowsAssembly("mscorlib"));   // FindType by name stays shut
            // naming them (static:System.String, types) stays shut too: only as a receiver
            foreach (string n in new[] { "System.String", "System.Math", "System.Array", "System.Int32",
                                         "System.Collections.Generic.List`1" })
                Assert.False(LiveGuard.AllowsType(ms, n), n);
        }

        [Fact]
        public void RunnerFolders_Refused()
        {
            foreach (string m in new[] { "dataPath", "persistentDataPath", "streamingAssetsPath", "temporaryCachePath", "consoleLogPath", "get_persistentDataPath" })
                Assert.False(LiveGuard.AllowsMember("UnityEngine", "UnityEngine.Application", m), m);
            Assert.True(LiveGuard.AllowsMember("UnityEngine", "UnityEngine.Application", "version"));
        }

        [Fact]
        public void InvokeAndMessages_RefusedOnAnyType()
        {
            foreach (string n in new[] { "Invoke", "InvokeRepeating", "CancelInvoke", "StartCoroutine", "StartCoroutine_Auto",
                                         "SendMessage", "SendMessageUpwards", "BroadcastMessage" })
            {
                Assert.False(LiveGuard.AllowsMember("UnityEngine", "UnityEngine.MonoBehaviour", n));
                Assert.False(LiveGuard.AllowsMember("Assembly-CSharp", "SomeGameType", n));
            }
            Assert.True(LiveGuard.AllowsMember("UnityEngine", "UnityEngine.MonoBehaviour", "IsInvoking"));
        }

        [Fact]
        public void LegacyNetworkPingClipboardAndWindowsFiles_Refused()
        {
            foreach (string t in new[] { "UnityEngine.Network", "UnityEngine.NetworkView", "UnityEngine.MasterServer", "UnityEngine.Ping",
                                         "UnityEngine.Windows.File", "UnityEngine.Windows.Directory", "UnityEngine.Profiling.Profiler",
                                         "UnityEngine.Microphone", "UnityEngine.WebCamTexture", "UnityEngine.Security",
                                         "UnityEngine.Caching", "UnityEngine.AndroidJavaObject" })
                Assert.False(LiveGuard.AllowsType("UnityEngine", t), t);
            Assert.False(LiveGuard.AllowsMember("UnityEngine", "UnityEngine.GUIUtility", "systemCopyBuffer"));
            Assert.False(LiveGuard.AllowsMember("UnityEngine", "UnityEngine.GUIUtility", "get_systemCopyBuffer"));
            Assert.False(LiveGuard.AllowsMember("UnityEngine", "UnityEngine.GUIUtility", "set_systemCopyBuffer"));
            Assert.False(LiveGuard.AllowsMember("UnityEngine", "UnityEngine.TextEditor", "Paste"));
            Assert.True(LiveGuard.AllowsMember("UnityEngine", "UnityEngine.GUIUtility", "hotControl"));
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
            Assert.True(LiveGuard.AllowsReceiver("Assembly-CSharp", "TheForest.Utils.LocalPlayer"));
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
            Assert.Equal(LiveLink.Answer.Refused, LiveLink.Classify(400));
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
        public void TooLarge_IsNotTheEnd()
        {
            Assert.Equal(LiveLink.Answer.TooLarge, LiveLink.Classify(413));
            Assert.Equal(LiveLink.Answer.Refused, LiveLink.Classify(401));
        }

        [Fact]
        public void Utf8Bytes_CountsBytesNotChars()
        {
            Assert.Equal(3, LiveLink.Utf8Bytes("abc"));
            Assert.Equal(2, LiveLink.Utf8Bytes("\u00e9"));
            Assert.Equal(3, LiveLink.Utf8Bytes("\u20ac"));
            Assert.Equal(4, LiveLink.Utf8Bytes("\ud83d\ude00"));
            string mix = "a\u00e9\u20ac\ud83d\ude00\n";
            Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(mix), LiveLink.Utf8Bytes(mix));
        }

        [Fact]
        public void NextChunk_FitsTheCapAtALine()
        {
            string rest;
            Assert.Equal("small\n", LiveLink.NextChunk("small\n", 100, out rest));
            Assert.Null(rest);

            string text = "line one\nline two\nline three\n";
            string first = LiveLink.NextChunk(text, 20, out rest);
            Assert.Equal("line one\nline two\n", first);
            Assert.Equal("line three\n", rest);

            // One line longer than the cap: cut between characters, never in a pair.
            string emoji = new string('a', 5) + "\ud83d\ude00" + "bbb";
            first = LiveLink.NextChunk(emoji, 7, out rest);
            Assert.Equal("aaaaa", first);
            Assert.Equal(emoji, first + rest);
        }

        [Fact]
        public void NextChunk_EveryChunkUnderTheSitesCap()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 20000; i++) sb.Append("#").Append(i).Append(" \u00e9\u20ac output line\n");
            string text = sb.ToString(), rest = text, all = "";
            int chunks = 0;
            while (rest != null)
            {
                string c = LiveLink.NextChunk(rest, LiveLink.MaxPostBytes, out rest);
                Assert.True(System.Text.Encoding.UTF8.GetByteCount(c) <= LiveLink.MaxPostBytes);
                Assert.True(LiveLink.MaxPostBytes < 256 * 1024);
                all += c;
                chunks++;
            }
            Assert.True(chunks > 1);
            Assert.Equal(text, all);
        }

        [Fact]
        public void KeepNewest_CapsBytesAndKeepsWholeLines()
        {
            Assert.Equal("short\n", LiveLink.KeepNewest("short\n", 100));
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 100; i++) sb.Append("\u20ac\u20ac line ").Append(i).Append('\n');
            string kept = LiveLink.KeepNewest(sb.ToString(), 400);
            Assert.StartsWith(LiveLink.DroppedNote, kept);
            string tail = kept.Substring(LiveLink.DroppedNote.Length);
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(tail) <= 200);
            Assert.StartsWith("\u20ac", tail);
            Assert.EndsWith("line 99\n", tail);
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
