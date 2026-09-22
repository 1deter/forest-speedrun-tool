using UnityEngine;
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

        /// True if this module owns its OWN floating window. Reserved for
        /// things that genuinely need the space (the type explorer); most
        /// features should be a tab instead.
        public virtual bool HasPanel { get { return false; } }

        /// True if this module contributes a tab to the main window.
        ///
        /// Tabs exist because a hotkey per panel does not scale: past a
        /// handful of features the user is memorising keys to find things,
        /// which is worse than one window they can navigate.
        public virtual bool HasTab { get { return false; } }

        public virtual string TabTitle { get { return DisplayName; } }

        /// Lower sorts first in the tab strip.
        public virtual int TabOrder { get { return 100; } }

        /// Draw the tab body. `area` is already a local coordinate space -
        /// the host has opened a GUI group - so draw from 0,0.
        public virtual void DrawTab(Rect area) { }

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

        /// True while this module needs the player held still whether or
        /// not any window is open - freecam, which steers a detached view
        /// with keys the body would otherwise also act on. The host holds
        /// the lock and blocks game input for as long as any module says
        /// so. Unlike WantsPlayerLock it ignores the "hold player while a
        /// panel is open" setting: that is about windows, this is not.
        public virtual bool HoldsPlayer { get { return false; } }

        public bool PanelOpen;

        /// Set by ModuleHost at registration so a module can open or close
        /// its own panel from a hotkey without reaching for a global.
        public ModuleHost Host;

        protected void TogglePanel()
        {
            if (Host != null) Host.TogglePanel(this);
        }

        /// Opens the main window focused on this module's tab. Used by the
        /// optional per-feature hotkeys, which are unbound by default -
        /// one key for the window is enough for most people.
        protected void OpenMyTab()
        {
            if (Host == null) return;

            Modules.MainWindowModule main = Host.Find<Modules.MainWindowModule>();
            if (main != null) main.OpenAt(this);
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
