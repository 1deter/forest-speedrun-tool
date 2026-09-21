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
        public override bool HasPanel { get { return true; } }

        private UpdateChecker _checker;
        private Rect _windowRect;
        private bool _windowPlaced;
        private bool _autoOpened;

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);

            _checker = new UpdateChecker(ctx.Log, OverlayPlugin.PluginVersion);

            if (ctx.Runner != null)
                ctx.Runner.StartCoroutine(_checker.Check());
            else
                ctx.Log.LogWarning("No coroutine runner - update check skipped.");
        }

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("panel.update", KeyCode.End, "Updates panel", TogglePanel);
        }

        public override void Tick()
        {
            // Open the panel once, unprompted, when an update exists. This
            // is the whole point: a runner who never opens a menu should
            // still find out.
            if (_autoOpened) return;
            if (_checker.State != UpdateChecker.Status.UpdateAvailable) return;

            _autoOpened = true;
            if (!PanelOpen) TogglePanel();
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

        public override void DrawPanel(int windowId)
        {
            if (!_windowPlaced)
            {
                _windowRect = new Rect(Screen.width * 0.5f - 210f, 60f, 420f, 190f);
                _windowPlaced = true;
            }

            _windowRect = GUI.Window(windowId, _windowRect, DrawContents, "ForestOverlay updates");
        }

        private void DrawContents(int id)
        {
            float w = _windowRect.width;

            GUI.Label(new Rect(12, 28, w - 24, 20), "Installed: v" + OverlayPlugin.PluginVersion);
            GUI.Label(new Rect(12, 48, w - 24, 20), "Status: " + _checker.Message);

            bool canDownload = _checker.State == UpdateChecker.Status.UpdateAvailable;

            GUI.enabled = canDownload;
            if (GUI.Button(new Rect(12, 76, 190, 26), "Download v" + (_checker.LatestVersion ?? "?")))
            {
                if (Ctx.Runner != null)
                    Ctx.Runner.StartCoroutine(_checker.Download(Ctx.PluginPath));
            }
            GUI.enabled = true;

            if (GUI.Button(new Rect(210, 76, 130, 26), "Check again"))
            {
                if (Ctx.Runner != null) Ctx.Runner.StartCoroutine(_checker.Check());
            }

            if (_checker.State == UpdateChecker.Status.Staged)
            {
                GUI.Label(new Rect(12, 110, w - 24, 40),
                          "Downloaded. Restart the game to finish updating.");
            }
            else
            {
                GUI.Label(new Rect(12, 110, w - 24, 40),
                          "Updates install on the next game start.");
            }

            GUI.DragWindow(new Rect(0, 0, w, 22));
        }
    }
}
