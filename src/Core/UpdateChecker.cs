using System;
using ForestOverlay.Data;
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
    // plugin as a .pending file, and the preloader patcher
    // (patcher/UpdaterPatcher.cs, installed by Core/UpdaterInstaller)
    // moves it into place on the next launch, before plugins load.
    // ------------------------------------------------------------------
    public sealed class UpdateChecker
    {
        public const string Repo = "1deter/forest-speedrun-tool";
        private const string LatestUrl = "https://api.github.com/repos/" + Repo + "/releases/latest";
        public const string PendingSuffix = ".pending";

        // DownloadRetry: the release lists the DLL but it is not downloadable
        // yet (404 for a short while after publishing) - UpdateModule retries.
        public enum Status { Idle, Checking, UpToDate, UpdateAvailable, Publishing, Downloading, DownloadRetry, Staged, Failed }

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

            string tag = ReleaseJson.ExtractString(json, "tag_name");
            if (string.IsNullOrEmpty(tag))
            {
                State = Status.Failed;
                Message = ReleaseJson.DescribeError(json);
                _log.LogWarning("Update check: " + Message);
                yield break;
            }

            LatestVersion = tag.TrimStart('v', 'V');
            DownloadUrl = ReleaseJson.ExtractAssetUrl(json, "ForestOverlay.dll");

            int cmp = ReleaseJson.CompareVersions(LatestVersion, _currentVersion);

            if (cmp <= 0)
            {
                State = Status.UpToDate;
                Message = "up to date (v" + _currentVersion + ")";
            }
            else if (string.IsNullOrEmpty(DownloadUrl))
            {
                // GitHub publishes the release before CI attaches the DLL,
                // and caches the API answer for about a minute, so a check
                // just after a release sees it empty. UpdateModule retries.
                State = Status.Publishing;
                Message = "v" + LatestVersion + " is still being published - checking again shortly";
            }
            else
            {
                State = Status.UpdateAvailable;
                Message = "v" + LatestVersion + " available (you have " + _currentVersion + ")";
            }

            _log.LogInfo("Update check: " + Message);
        }

        /// Stop the automatic download retries, leaving the update
        /// available to try again by hand.
        public void GiveUpRetrying(string message)
        {
            State = Status.UpdateAvailable;
            Message = message;
            _log.LogWarning("Update download: " + message);
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

            if (State == Status.Failed) yield break;

            // NOT PUBLISHED YET. Right after a release, the API can list the
            // DLL while the download URL still answers 404 with the 9-byte
            // body "Not Found" - Unity 5.6's UnityWebRequest does not treat
            // a 404 as an error. That used to surface as "not a DLL" and
            // stop; it is a wait, so say so and let UpdateModule retry.
            if (_lastResponseCode == 404 || data == null || data.Length == 0 ||
                (!IsDll(data) && data.Length < 4096))
            {
                State = Status.DownloadRetry;
                Message = "v" + LatestVersion + " is not downloadable yet (GitHub answered " +
                          (_lastResponseCode > 0 ? _lastResponseCode.ToString() : "empty") +
                          ") - retrying shortly";
                _log.LogInfo("Update download: " + Message);
                yield break;
            }

            // Sanity check before writing anything: a managed DLL starts
            // with "MZ". Writing whatever came back would otherwise turn a
            // captive-portal HTML page into a corrupt plugin.
            if (!IsDll(data))
            {
                State = Status.Failed;
                Message = "downloaded file is not a DLL - ignoring";
                _log.LogWarning(Message + " (" + data.Length + " bytes, HTTP " + _lastResponseCode + ")");
                yield break;
            }

            try
            {
                File.WriteAllBytes(pluginDllPath + PendingSuffix, data);
                State = Status.Staged;
                // Only promise "restart" when the patcher that does the
                // install is really there - an unconditional "restart to
                // apply" once left runners on the old version.
                Message = UpdaterInstaller.Installed
                    ? "v" + LatestVersion + " downloaded - restart the game to install it"
                    : "v" + LatestVersion + " downloaded - close the game, delete ForestOverlay.dll, " +
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

        private static bool IsDll(byte[] data)
        {
            return data.Length >= 2 && data[0] == 0x4D && data[1] == 0x5A;
        }

        // HTTP status of the last FetchBytes, or 0 when unknown.
        private long _lastResponseCode;

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
            _lastResponseCode = 0;
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

                System.Reflection.PropertyInfo codeProp = requestType.GetProperty("responseCode");
                if (codeProp != null) _lastResponseCode = Convert.ToInt64(codeProp.GetValue(request, null));

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
    }
}
