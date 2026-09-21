using ForestOverlay.Core;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Hosts the runtime type explorer as a module.
    //
    // TypeExplorer itself is unchanged - it already solves the hard part
    // (virtualised rows, cached GUIContent) and there was no reason to
    // disturb working code to make it fit the module shape. This is a
    // thin adapter: lifecycle in, panel out.
    //
    // Reading types does not write game state, so the explorer is
    // info-only. The player lock it offers is not, which is why that
    // toggle is delegated to the host rather than owned here.
    // ------------------------------------------------------------------
    public sealed class ExplorerModule : OverlayModule
    {
        public override string Id { get { return "explorer"; } }
        public override string DisplayName { get { return "Type explorer"; } }
        public override bool HasPanel { get { return true; } }
        // Mirrors the explorer's own in-window toggle, so the checkbox
        // actually controls the lock rather than just reporting it.
        public override bool WantsPlayerLock
        {
            get { return _explorer != null && _explorer.LockPlayer; }
        }

        private TypeExplorer _explorer;

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);

            _explorer = new TypeExplorer(ctx.Log);
            _explorer.OnLockPlayerChanged = OnLockToggled;
            _explorer.LockPlayer = true;
            _explorer.Rescan();
        }

        private void OnLockToggled(bool enabled)
        {
            if (Host != null) Host.SetLockPlayer(enabled);
        }

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("panel.explorer", KeyCode.F10, "Type explorer panel", TogglePanel);
        }

        public override void OnPanelToggled(bool open)
        {
            Ctx.Log.LogInfo("Explorer -> " + open);

            // The explorer's own toggle is the source of truth for the
            // player lock while it is the panel being opened.
            if (open && Host != null && _explorer != null)
                Host.SetLockPlayer(_explorer.LockPlayer);
        }

        public override void DrawPanel(int windowId)
        {
            if (_explorer != null) _explorer.Draw(windowId);
        }
    }
}
