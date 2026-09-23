using ForestOverlay.Core;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Surfaces the update check.
    //
    // Runners are not expected to visit GitHub, so this checks on startup
    // and says so on the HUD when something newer exists. The download is
    // one click, and applying it happens on the next launch - see
    // UpdateChecker for why it cannot overwrite itself in place.
    //
    // The check is opt-out via the config file rather than opt-in: a
    // silent stale install is the exact problem this is meant to solve.
    // ------------------------------------------------------------------
    public sealed class UpdateModule : OverlayModule
    {
        public override string Id { get { return "update"; } }
        public override string DisplayName { get { return "Updates"; } }
        public override bool HasTab { get { return true; } }
        public override string TabTitle { get { return "Updates"; } }
        public override int TabOrder { get { return 70; } }

        private UpdateChecker _checker;
        private bool _autoOpened;

        // Built in Tick when the checker's text changes, never in DrawTab.
        private readonly GUIContent _statusText = new GUIContent("");
        private readonly GUIContent _downloadText = new GUIContent("Download");
        private string _messageShown, _latestShown;

        // The latest release's changelog: what an update brings, or - once
        // installed - what this version changed (author's request).
        private readonly GUIContent _notesHeading = new GUIContent("");
        private readonly GUIContent _notesText = new GUIContent("");
        private string _notesShown;
        private Vector2 _notesScroll;
        private float _notesHeight;
        private string _autoInstallLabel = "";

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);

            _checker = new UpdateChecker(ctx.Log, OverlayPlugin.PluginVersion);

            // Fixed for the session: the installer ran before any module.
            _autoInstallLabel = "Auto-install: " + UpdaterInstaller.Status;

            if (ctx.Runner != null)
                ctx.Runner.StartCoroutine(_checker.Check());
            else
                ctx.Log.LogWarning("No coroutine runner - update check skipped.");
        }

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("tab.update", KeyCode.None, "Open Updates tab", OpenMyTab);
        }

        private const float PublishRetrySeconds = 60f;
        private const int PublishRetryLimit = 15;
        private float _nextPublishRetry;
        private int _publishRetries;

        // The DLL is listed but still 404s for a short while after a
        // release; the download retries itself rather than dead-ending.
        private const float DownloadRetrySeconds = 30f;
        private const int DownloadRetryLimit = 10;
        private float _nextDownloadRetry;
        private int _downloadRetries;

        public override void Tick()
        {
            if (!ReferenceEquals(_checker.Message, _messageShown))
            {
                _messageShown = _checker.Message;
                _statusText.text = "Status: " + _messageShown;
            }
            if (!ReferenceEquals(_checker.LatestVersion, _latestShown) || !ReferenceEquals(_checker.ReleaseNotes, _notesShown))
            {
                _latestShown = _checker.LatestVersion;
                _notesShown = _checker.ReleaseNotes;
                _downloadText.text = "Download v" + (_latestShown ?? "?");

                bool installed = _latestShown != null &&
                                 Data.ReleaseJson.CompareVersions(_latestShown, OverlayPlugin.PluginVersion) <= 0;
                _notesHeading.text = _latestShown == null ? ""
                    : installed ? "What's new in v" + _latestShown + " (installed)"
                                : "What's new in v" + _latestShown;
                _notesText.text = _notesShown ?? (_latestShown != null ? "No changelog for this version." : "");
            }

            // A release caught mid-publish has no DLL yet; ask again rather
            // than leaving the runner to guess that "Check again" will work.
            if (_checker.State == UpdateChecker.Status.Publishing && Ctx.Runner != null &&
                _publishRetries < PublishRetryLimit)
            {
                if (_nextPublishRetry == 0f) _nextPublishRetry = Time.unscaledTime + PublishRetrySeconds;
                else if (Time.unscaledTime >= _nextPublishRetry)
                {
                    _nextPublishRetry = 0f;
                    _publishRetries++;
                    Ctx.Runner.StartCoroutine(_checker.Check());
                }
            }

            if (_checker.State == UpdateChecker.Status.DownloadRetry && Ctx.Runner != null)
            {
                if (_downloadRetries >= DownloadRetryLimit)
                {
                    // Leave it clickable: Download again is always allowed.
                    _checker.GiveUpRetrying("GitHub still is not serving the file - try Download again later");
                }
                else if (_nextDownloadRetry == 0f) _nextDownloadRetry = Time.unscaledTime + DownloadRetrySeconds;
                else if (Time.unscaledTime >= _nextDownloadRetry)
                {
                    _nextDownloadRetry = 0f;
                    _downloadRetries++;
                    Ctx.Runner.StartCoroutine(_checker.Download(Ctx.PluginPath));
                }
            }

            // Open the panel once, unprompted, when an update exists. This
            // is the whole point: a runner who never opens a menu should
            // still find out.
            if (_autoOpened) return;
            if (_checker.State != UpdateChecker.Status.UpdateAvailable) return;

            _autoOpened = true;
            OpenMyTab();
        }

        public override void ContributeHud(HudBuilder hud)
        {
            // Always shown, including "up to date". The check is the only
            // evidence that the network call worked at all - Unity 5.6's
            // Mono predates TLS 1.2, so a silent absence here is
            // indistinguishable from a handshake failure. Seeing
            // "up to date (v0.7.0)" is what confirms it.
            hud.Pair("Update", _checker.Message + "   [End]");
        }

        private float _tabW;
        private float _tabH;

        public override void DrawTab(Rect area)
        {
            _tabW = area.width;
            _tabH = area.height;
            float w = _tabW;

            float y = 28f;
            y += UiText.Draw(12, y, w - 24, "Installed: v" + OverlayPlugin.PluginVersion);
            y += UiText.Draw(12, y, w - 24, _statusText);
            y += UiText.Draw(12, y, w - 24, _autoInstallLabel) + 6f;

            bool canDownload = _checker.State == UpdateChecker.Status.UpdateAvailable ||
                               _checker.State == UpdateChecker.Status.DownloadRetry;

            GUI.enabled = canDownload;
            if (GUI.Button(new Rect(12, y, 190, 26), _downloadText))
            {
                if (Ctx.Runner != null)
                {
                    _downloadRetries = 0;
                    _nextDownloadRetry = 0f;
                    Ctx.Runner.StartCoroutine(_checker.Download(Ctx.PluginPath));
                }
            }
            GUI.enabled = true;

            if (GUI.Button(new Rect(210, y, 130, 26), "Check again"))
            {
                if (Ctx.Runner != null) Ctx.Runner.StartCoroutine(_checker.Check());
            }
            y += 34f;

            if (_checker.State == UpdateChecker.Status.Staged)
                y += UiText.Draw(12, y, w - 24, _checker.Message);
            else
                y += UiText.Draw(12, y, w - 24,
                                 UpdaterInstaller.Installed ? "Downloaded updates install on the next game start."
                                                            : "Auto-install is unavailable - downloads must be swapped in by hand.");

            // The changelog, scrolled: it can be longer than the tab.
            if (_notesHeading.text.Length == 0) return;
            y += 8f;
            GUI.Label(new Rect(12, y, w - 24, 20), _notesHeading);
            y += 22f;

            Rect view = new Rect(12, y, w - 24, Mathf.Max(60f, _tabH - y - 8f));
            float inner = view.width - 20f;
            _notesScroll = GUI.BeginScrollView(view, _notesScroll, new Rect(0, 0, inner, Mathf.Max(_notesHeight, 20f)));
            _notesHeight = UiText.DrawDim(0, 0, inner, _notesText);
            GUI.EndScrollView();

        }
    }
}
