namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // A feature of the overlay.
    //
    // Adding one means: write a class here, override what you need,
    // and add a single line to ModuleHost.BuildDefaultModules. Nothing
    // else in the codebase has to know it exists.
    //
    // An abstract class rather than an interface because net35 has no
    // default interface members, and most modules only care about one or
    // two of these hooks.
    // ------------------------------------------------------------------
    public abstract class OverlayModule
    {
        protected ModuleContext Ctx;

        /// Stable identifier. Used in logs and for hotkey descriptions.
        public abstract string Id { get; }

        public abstract string DisplayName { get; }

        /// True if this module owns a window. Panels get a cursor and a
        /// window id automatically.
        public virtual bool HasPanel { get { return false; } }

        /// True when this module writes game state and so must not be used
        /// in a submitted run. Surfaced to the user by the HUD.
        public virtual bool IsPracticeOnly { get { return false; } }

        /// True if this panel wants the player held still while it is
        /// open. Defaults to "any panel", because clicking around a window
        /// while the camera drifts is unusable - the lock also stops
        /// camera look, since SimpleMouseRotator.Update reads
        /// FirstPersonCharacter.Locked.
        ///
        /// This does write game state, so it is reported by the practice
        /// marker; ModuleHost.LockPlayerWhilePanelOpen is the master
        /// switch if you want panels that leave you free to move.
        public virtual bool WantsPlayerLock { get { return HasPanel; } }

        public bool PanelOpen;

        /// Set by ModuleHost at registration so a module can open or close
        /// its own panel from a hotkey without reaching for a global.
        public ModuleHost Host;

        protected void TogglePanel()
        {
            if (Host != null) Host.TogglePanel(this);
        }

        public virtual void Initialise(ModuleContext ctx) { Ctx = ctx; }

        /// Called once per frame from Update. Do reflection and string
        /// building here, never in DrawHud/DrawPanel.
        public virtual void Tick() { }

        /// Called on the throttled HUD refresh (10 Hz), not per frame.
        public virtual void ContributeHud(HudBuilder hud) { }

        /// Declare hotkeys. Called once at startup.
        public virtual void RegisterHotkeys(HotkeyMap map) { }

        public virtual void DrawPanel(int windowId) { }

        public virtual void OnPanelToggled(bool open) { }

        public virtual void Shutdown() { }
    }
}
