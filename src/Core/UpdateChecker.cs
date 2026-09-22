using System;
using System.Collections;
using System.IO;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // Checks GitHub for a newer release and stages the download.
    //
    // WHY UnityWebRequest AND NOT WebClient/HttpWebRequest
    // This runs on Unity 5.6's Mono, which is the .NET 3.5 profile. Its
    // TLS stack predates TLS 1.2, and GitHub requires TLS 1.2 - so
    // HttpWebRequest to api.github.com fails at the handshake, usually
    // with a misleading "The request was aborted: Could not create
    // SSL/TLS secure channel". UnityWebRequest goes through Unity's
    // native HTTP stack, which uses the OS TLS and does support 1.2.
    // Confirmed present in this build's UnityEngine.dll (Get/Send/
    // isDone/downloadHandler).
    //
    // WHY THIS ONLY STAGES THE FILE
    // Windows will not let a loaded assembly be overwritten, and our own
    // DLL is loaded by definition. So the download lands beside the
    // plugin as a .pending file and something that runs BEFORE plugins
    // load has to move it into place. See docs - that part is a BepInEx
    // preloader patcher and is deliberately separate, because a
    // self-replacing updater that goes wrong leaves a runner with a
    // broken install and no way to fix it.
    // ------------------------------------------------------------------
    public sealed class UpdateChecker
    {
        public const string Repo = "1deter/forest-speedrun-tool";
        private const string LatestUrl = "https://api.github.com/repos/" + Repo + "/releases/latest";
        public const string PendingSuffix = ".pending";

        public enum Status { Idle, Checking, UpToDate, UpdateAvailable, Downloading, Staged, Failed }

        private readonly ManualLogSource _log;
        private readonly string _currentVersion;

        public Status State { get; private set; }
        public string LatestVersion { get; private set; }
        public string Message { get; private set; }
        public string DownloadUrl { get; private set; }

        public UpdateChecker(ManualLogSource log, string currentVersion)
        {
            _log = log;
            _currentVersion = currentVersion;
            State = Status.Idle;
            Message = "not checked";
        }

        public IEnumerator Check()
        {
            State = Status.Checking;
            Message = "checking...";

            string json = null;
            IEnumerator fetch = Fetch(LatestUrl, delegate(string body) { json = body; });
            while (fetch.MoveNext()) yield return fetch.Current;

            if (json == null)
            {
                State = Status.Failed;
                yield break;
            }

            string tag = ExtractJsonString(json, "tag_name");
            if (string.IsNullOrEmpty(tag))
            {
                State = Status.Failed;
                Message = "could not read latest release";
                yield break;
            }

            LatestVersion = tag.TrimStart('v', 'V');
            DownloadUrl = ExtractAssetUrl(json, "ForestOverlay.dll");

            int cmp = CompareVersions(LatestVersion, _currentVersion);

            if (cmp <= 0)
            {
                State = Status.UpToDate;
                Message = "up to date (v" + _currentVersion + ")";
            }
            else if (string.IsNullOrEmpty(DownloadUrl))
            {
                State = Status.Failed;
                Message = "v" + LatestVersion + " exists but has no ForestOverlay.dll asset";
            }
            else
            {
                State = Status.UpdateAvailable;
                Message = "v" + LatestVersion + " available (you have " + _currentVersion + ")";
            }

            _log.LogInfo("Update check: " + Message);
        }

        public IEnumerator Download(string pluginDllPath)
        {
            if (string.IsNullOrEmpty(DownloadUrl))
            {
                State = Status.Failed;
                Message = "no download url";
                yield break;
            }

            State = Status.Downloading;
            Message = "downloading v" + LatestVersion + "...";

            byte[] data = null;
            IEnumerator fetch = FetchBytes(DownloadUrl, delegate(byte[] body) { data = body; });
            while (fetch.MoveNext()) yield return fetch.Current;

            if (data == null || data.Length == 0)
            {
                State = Status.Failed;
                yield break;
            }

            // Sanity check before writing anything: a managed DLL starts
            // with "MZ". Writing whatever came back would otherwise turn a
            // captive-portal HTML page into a corrupt plugin.
            if (data.Length < 2 || data[0] != 0x4D || data[1] != 0x5A)
            {
                State = Status.Failed;
                Message = "downloaded file is not a DLL - ignoring";
                _log.LogWarning(Message + " (" + data.Length + " bytes)");
                yield break;
            }

            try
            {
                File.WriteAllBytes(pluginDllPath + PendingSuffix, data);
                State = Status.Staged;
                // Nothing installs a staged file yet (the preloader patcher
                // is still to be written), so say what to do by hand -
                // "restart to apply" left runners on the old version.
                Message = "v" + LatestVersion + " downloaded - close the game, delete ForestOverlay.dll, " +
                          "rename ForestOverlay.dll" + PendingSuffix + " to ForestOverlay.dll";
                _log.LogInfo(Message);
            }
            catch (Exception ex)
            {
                State = Status.Failed;
                Message = "could not write update: " + ex.Message;
                _log.LogWarning(Message);
            }
        }

        // ------------------------------------------------------------------
        private IEnumerator Fetch(string url, Action<string> onDone)
        {
            byte[] bytes = null;
            IEnumerator e = FetchBytes(url, delegate(byte[] b) { bytes = b; });
            while (e.MoveNext()) yield return e.Current;

            if (bytes != null) onDone(System.Text.Encoding.UTF8.GetString(bytes));
        }

        // Reflection rather than a direct reference, so a build against the
        // CI stub assembly (which may not expose UnityWebRequest) still
        // compiles, and a future Unity API rename degrades to a logged
        // warning instead of a hard failure.
        private IEnumerator FetchBytes(string url, Action<byte[]> onDone)
        {
            Type requestType = Type.GetType("UnityEngine.Networking.UnityWebRequest, UnityEngine");
            if (requestType == null)
            {
                State = Status.Failed;
                Message = "UnityWebRequest unavailable in this build";
                _log.LogWarning(Message);
                yield break;
            }

            object request = null;
            try
            {
                System.Reflection.MethodInfo get = requestType.GetMethod(
                    "Get", new Type[] { typeof(string) });
                request = get.Invoke(null, new object[] { url });

                // GitHub rejects requests without a User-Agent.
                System.Reflection.MethodInfo setHeader = requestType.GetMethod("SetRequestHeader");
                if (setHeader != null)
                    setHeader.Invoke(request, new object[] { "User-Agent", "ForestOverlay" });

                System.Reflection.MethodInfo send = requestType.GetMethod("Send")
                                                 ?? requestType.GetMethod("SendWebRequest");
                send.Invoke(request, null);
            }
            catch (Exception ex)
            {
                State = Status.Failed;
                Message = "request failed: " + ex.Message;
                _log.LogWarning(Message);
                yield break;
            }

            System.Reflection.PropertyInfo isDone = requestType.GetProperty("isDone");
            float timeout = Time.unscaledTime + 30f;

            while (!(bool)isDone.GetValue(request, null))
            {
                if (Time.unscaledTime > timeout)
                {
                    State = Status.Failed;
                    Message = "timed out";
                    _log.LogWarning("Update request timed out: " + url);
                    yield break;
                }
                yield return null;
            }

            try
            {
                System.Reflection.PropertyInfo errorProp = requestType.GetProperty("error");
                string error = errorProp != null ? errorProp.GetValue(request, null) as string : null;

                if (!string.IsNullOrEmpty(error))
                {
                    State = Status.Failed;
                    Message = "network error: " + error;
                    _log.LogWarning(Message + "  (" + url + ")");
                    yield break;
                }

                object handler = requestType.GetProperty("downloadHandler").GetValue(request, null);
                byte[] data = handler.GetType().GetProperty("data").GetValue(handler, null) as byte[];
                onDone(data);
            }
            catch (Exception ex)
            {
                State = Status.Failed;
                Message = "response unreadable: " + ex.Message;
                _log.LogWarning(Message);
            }
        }

        // ------------------------------------------------------------------
        // Minimal JSON scraping. A real parser is not worth a dependency on
        // net35 for two fields, and the shape of the GitHub release payload
        // is stable.
        public static string ExtractJsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;

            string needle = "\"" + key + "\"";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;

            i = json.IndexOf(':', i + needle.Length);
            if (i < 0) return null;

            int start = json.IndexOf('"', i + 1);
            if (start < 0) return null;

            int end = start + 1;
            while (end < json.Length && json[end] != '"')
            {
                if (json[end] == '\\') end++;   // skip escaped char
                end++;
            }
            if (end >= json.Length) return null;

            return json.Substring(start + 1, end - start - 1);
        }

        /// Finds browser_download_url for the asset with the given name.
        public static string ExtractAssetUrl(string json, string assetName)
        {
            if (string.IsNullOrEmpty(json)) return null;

            int i = json.IndexOf("\"name\":\"" + assetName + "\"", StringComparison.Ordinal);
            if (i < 0) return null;

            int u = json.IndexOf("browser_download_url", i, StringComparison.Ordinal);
            if (u < 0) return null;

            return ExtractJsonString(json.Substring(u - 1), "browser_download_url");
        }

        /// Returns >0 when `a` is newer than `b`. Numeric, dot-separated,
        /// tolerant of differing part counts and of trailing suffixes.
        public static int CompareVersions(string a, string b)
        {
            if (a == null) a = "";
            if (b == null) b = "";

            string[] pa = a.Split('.');
            string[] pb = b.Split('.');
            int n = Math.Max(pa.Length, pb.Length);

            for (int i = 0; i < n; i++)
            {
                int va = i < pa.Length ? ParseLeadingInt(pa[i]) : 0;
                int vb = i < pb.Length ? ParseLeadingInt(pb[i]) : 0;
                if (va != vb) return va > vb ? 1 : -1;
            }
            return 0;
        }

        private static int ParseLeadingInt(string s)
        {
            int value = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] < '0' || s[i] > '9') break;
                value = value * 10 + (s[i] - '0');
            }
            return value;
        }
    }
}
