using System;
using System.Collections.Generic;
using BepInEx.Logging;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // Owns the module list and fans the Unity lifecycle out to it.
    //
    // Every module is individually try/caught at every hook. That is a
    // hard lesson already recorded in CLAUDE.md: a single throwing
    // lifecycle method takes the whole plugin down silently while still
    // logging "loaded". Here the blast radius is one module - it gets
    // disabled, the failure is logged once, and everything else keeps
    // running.
    // ------------------------------------------------------------------
    public sealed class ModuleHost
    {
        private const int BaseWindowId = 60_000;
        private const float HudRefreshInterval = 0.1f;

        private readonly List<OverlayModule> _modules = new List<OverlayModule>();
        private readonly List<OverlayModule> _failed = new List<OverlayModule>();
        private readonly ModuleContext _ctx;
        private readonly HotkeyMap _hotkeys;
        private readonly HudBuilder _hud = new HudBuilder();
        private readonly CursorController _cursor;
        private readonly GameInput _input;
        private readonly PerfMonitor _perf;

        private float _nextHudRefresh;

        // Stutter watch. A module that takes longer than this in one Tick
        // is logged by name, at most once per interval per module, so a
        // "the game hitches every second" report comes with a culprit.
        private const double SlowTickMs = 5.0;
        private const float SlowReportInterval = 10f;
        private readonly Dictionary<OverlayModule, float> _nextSlowReport = new Dictionary<OverlayModule, float>();

        public HotkeyMap Hotkeys { get { return _hotkeys; } }
        public PerfMonitor Perf { get { return _perf; } }
        public HudBuilder Hud { get { return _hud; } }
        public ModuleContext Context { get { return _ctx; } }

        /// Whether game input is being blocked, for the Settings tab.
        public string InputStatus { get { return _input.Status; } }
        /// The info box in the corner.
        public bool HudVisible = true;

        /// Master switch: hides EVERYTHING this plugin draws, including
        /// windows and overlays. "Hide HUD" meaning "hide one box" was
        /// surprising - if a runner wants the screen clean for a recording
        /// they mean all of it.
        public bool UiVisible = true;

        /// Practice-only. Uses the game's own FirstPersonCharacter.Locked,
        /// because Time.timeScale is re-asserted by the game every frame
        /// and an external write to it does nothing. Also switches the
        /// game's key map to Menu (Game/GameInput), so clicks in the window
        /// stop reaching the game.
        public bool LockPlayerWhilePanelOpen = true;
        private bool _playerLockApplied;
        private bool _gameHeldLock;   // the game had the player locked when we took over

        public ModuleHost(ModuleContext ctx)
        {
            _ctx = ctx;
            _cursor = new CursorController(ctx.Log);
            _input = new GameInput(ctx.Log);
            _perf = new PerfMonitor(ctx.Log);
            _hotkeys = new HotkeyMap(ctx.Config);
        }

        public void Register(OverlayModule module)
        {
            module.Host = this;
            _modules.Add(module);
        }

        public int Count { get { return _modules.Count; } }

        /// Modules contributing tabs, in display order. Cached: the window
        /// asks on every OnGUI pass, and the list only changes when a
        /// module is disabled. Callers must not modify it.
        public List<OverlayModule> Tabs()
        {
            if (_tabs != null && _tabsBuiltFailed == _failed.Count) return _tabs;

            _tabs = new List<OverlayModule>();
            for (int i = 0; i < _modules.Count; i++)
                if (_modules[i].HasTab && IsLive(_modules[i])) _tabs.Add(_modules[i]);

            _tabs.Sort(CompareTabs);
            _tabsBuiltFailed = _failed.Count;
            return _tabs;
        }

        private List<OverlayModule> _tabs;
        private int _tabsBuiltFailed = -1;

        private static int CompareTabs(OverlayModule a, OverlayModule b)
        {
            if (a.TabOrder != b.TabOrder) return a.TabOrder.CompareTo(b.TabOrder);
            return string.Compare(a.TabTitle, b.TabTitle, StringComparison.OrdinalIgnoreCase);
        }

        /// Locate a sibling module. Used sparingly - modules are meant to
        /// be independent - but a couple of them genuinely collaborate
        /// (practice runs need to know where the anchor is), and an
        /// explicit lookup beats a static.
        public T Find<T>() where T : OverlayModule
        {
            for (int i = 0; i < _modules.Count; i++)
            {
                T typed = _modules[i] as T;
                if (typed != null) return typed;
            }
            return null;
        }

        public IList<OverlayModule> Modules { get { return _modules; } }

        public void InitialiseAll()
        {
            for (int i = 0; i < _modules.Count; i++)
            {
                OverlayModule m = _modules[i];
                try
                {
                    m.Initialise(_ctx);
                    m.RegisterHotkeys(_hotkeys);
                    _ctx.Log.LogInfo("  module ready: " + m.Id);
                }
                catch (Exception ex)
                {
                    Disable(m, "Initialise", ex);
                }
            }
        }

        private void Disable(OverlayModule m, string where, Exception ex)
        {
            if (!_failed.Contains(m)) _failed.Add(m);
            _ctx.Log.LogError("Module '" + m.Id + "' disabled after throwing in " + where + ": " + ex);
        }

        private void ReportSlowTick(OverlayModule m, double ms)
        {
            float next;
            if (_nextSlowReport.TryGetValue(m, out next) && Time.unscaledTime < next) return;
            _nextSlowReport[m] = Time.unscaledTime + SlowReportInterval;

            _ctx.Log.LogWarning("Slow tick: '" + m.Id + "' took " + ms.ToString("0.0") +
                                " ms (a visible hitch if it repeats).");
        }

        private bool IsLive(OverlayModule m)
        {
            return !_failed.Contains(m);
        }

        // ------------------------------------------------------------------
        public void Tick()
        {
            long allocStart = _perf.BeginAlloc();
            _hotkeys.Dispatch();

            double tickTotal = 0.0;
            for (int i = 0; i < _modules.Count; i++)
            {
                OverlayModule m = _modules[i];
                if (!IsLive(m)) continue;

                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                try { m.Tick(); }
                catch (Exception ex) { Disable(m, "Tick", ex); }

                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 /
                            System.Diagnostics.Stopwatch.Frequency;
                tickTotal += ms;
                if (ms >= SlowTickMs) ReportSlowTick(m, ms);
            }

            _perf.Frame(tickTotal, _ctx.Player.Found);

            // What the window (or freecam) needs from the game this frame.
            // Only while a player exists: at the title screen there is no
            // one to hold, and the menu owns the input states itself.
            bool panel = AnyPanelOpen();
            bool hold = _ctx.Player.Found &&
                        ((panel && LockPlayerWhilePanelOpen && AnyPanelWantsPlayerLock()) ||
                         AnyModuleHoldsPlayer());

            // Player lock FIRST, cursor second. The game's LockView /
            // UnLockView also call Input.LockMouse/UnLockMouse, so if the
            // cursor were asserted first, changing the player lock in the
            // same tick would undo it behind us.
            if (hold) ApplyPlayerLock();
            else ReleasePlayerLock();

            // Cursor is asserted from Update (not LateUpdate) so that
            // VirtualCursor.LateUpdate is guaranteed to observe it - Unity
            // runs all Updates before any LateUpdate, while the order
            // between two LateUpdates is undefined.
            if (panel)
            {
                _cursor.Acquire();
            }
            else
            {
                _cursor.Release();
                // Freecam with the window closed: LockView freed the mouse,
                // but the view is steered with it.
                if (hold) _cursor.EnsureLocked();
            }

            // Clicks and keys go to the overlay, not the game - otherwise a
            // click on a button swings whatever the player is holding.
            if (hold) _input.Hold();
            else _input.Release();

            RefreshHudIfDue();
            _perf.EndAlloc(allocStart);
        }

        private bool AnyModuleHoldsPlayer()
        {
            for (int i = 0; i < _modules.Count; i++)
                if (IsLive(_modules[i]) && _modules[i].HoldsPlayer) return true;
            return false;
        }

        private bool AnyPanelWantsPlayerLock()
        {
            for (int i = 0; i < _modules.Count; i++)
            {
                OverlayModule m = _modules[i];
                if (m.PanelOpen && IsLive(m) && m.WantsPlayerLock) return true;
            }
            return false;
        }

        public bool AnyPanelOpen()
        {
            for (int i = 0; i < _modules.Count; i++)
                if (_modules[i].PanelOpen && IsLive(_modules[i])) return true;
            return false;
        }

        public void TogglePanel(OverlayModule m)
        {
            m.PanelOpen = !m.PanelOpen;

            // Opening a window while "hide all UI" is on freed the mouse
            // and drew nothing - the window looked like it vanished (maks,
            // v0.24.68: his open key F4 sits next to hide-all F5).
            if (m.PanelOpen && !UiVisible)
            {
                UiVisible = true;
                _ctx.Log.LogInfo("UI shown again: a window was opened while all UI was hidden.");
            }

            try { m.OnPanelToggled(m.PanelOpen); }
            catch (Exception ex) { Disable(m, "OnPanelToggled", ex); }
        }

        private void RefreshHudIfDue()
        {
            if (Time.unscaledTime < _nextHudRefresh) return;
            _nextHudRefresh = Time.unscaledTime + HudRefreshInterval;

            _hud.Begin();
            for (int i = 0; i < _modules.Count; i++)
            {
                OverlayModule m = _modules[i];
                if (!IsLive(m)) continue;
                try { m.ContributeHud(_hud); }
                catch (Exception ex) { Disable(m, "ContributeHud", ex); }
            }
        }

        // ------------------------------------------------------------------
        public void DrawPanels()
        {
            for (int i = 0; i < _modules.Count; i++)
            {
                OverlayModule m = _modules[i];
                if (!m.HasPanel || !m.PanelOpen || !IsLive(m)) continue;
                try { m.DrawPanel(BaseWindowId + i); }
                catch (Exception ex)
                {
                    m.PanelOpen = false;
                    Disable(m, "DrawPanel", ex);
                }
            }
        }

        // ------------------------------------------------------------------
        private void ApplyPlayerLock()
        {
            if (_ctx.Bridge == null) return;

            // Re-assert if the game cleared it behind us. Opening and
            // closing the ESC menu calls UnLockView, which silently freed
            // the player while one of our panels was still open. Checking
            // the flag is a cheap field read; SetPlayerLocked is only
            // called when it has actually been lost, so LockView's
            // rigidbody work does not run every frame.
            if (_playerLockApplied && _ctx.Bridge.IsPlayerLocked()) return;

            _ctx.Bridge.ResolvePlayerController(_ctx.Player.Transform);

            // Whose lock is it? Opened over the ESC menu (or the book, the
            // inventory) the game already holds the player with LockView,
            // and it releases him itself when that menu closes. Taking the
            // lock over and releasing it on window close called UnLockView
            // under the pause menu - LockMouse hid the cursor and the menu
            // could not be used (author, v0.22.x). Lost while we held it =
            // the game let go, so from then on the lock is ours.
            _gameHeldLock = !_playerLockApplied && _ctx.Bridge.IsPlayerLocked();

            _ctx.Bridge.SetPlayerLocked(true);

            if (!_playerLockApplied)
            {
                _playerLockApplied = true;
                // Holding the player writes to the game, so it counts.
                _ctx.Practice.Mark("player lock");
            }
        }

        private void ReleasePlayerLock()
        {
            if (!_playerLockApplied || _ctx.Bridge == null) return;
            _playerLockApplied = false;

            // The game's own menu still holds him: leave the lock (and the
            // cursor it freed) to the game.
            if (_gameHeldLock) { _gameHeldLock = false; return; }

            _ctx.Bridge.SetPlayerLocked(false);

            // Rebase before input resumes, so the view stays where the
            // player is actually looking rather than snapping back to
            // whatever the rotator held when the panel opened.
            _ctx.Bridge.RebaseLookAngles();
        }

        /// Tick reconciles the lock and the input block on the next frame.
        public void SetLockPlayer(bool enabled)
        {
            LockPlayerWhilePanelOpen = enabled;
        }

        public void Shutdown()
        {
            _input.Release();
            _cursor.Release();
            ReleasePlayerLock();

            for (int i = 0; i < _modules.Count; i++)
            {
                try { _modules[i].Shutdown(); }
                catch (Exception ex) { _ctx.Log.LogWarning("Shutdown of " + _modules[i].Id + ": " + ex.Message); }
            }
        }
    }
}
