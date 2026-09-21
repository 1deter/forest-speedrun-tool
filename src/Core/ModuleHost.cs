using System;
using System.Collections.Generic;
using BepInEx.Logging;
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
        private readonly HotkeyMap _hotkeys = new HotkeyMap();
        private readonly HudBuilder _hud = new HudBuilder();
        private readonly CursorController _cursor;

        private float _nextHudRefresh;

        public HotkeyMap Hotkeys { get { return _hotkeys; } }
        public HudBuilder Hud { get { return _hud; } }
        public ModuleContext Context { get { return _ctx; } }
        public bool HudVisible = true;

        /// Practice-only. Uses the game's own FirstPersonCharacter.Locked,
        /// because Time.timeScale is re-asserted by the game every frame
        /// and an external write to it does nothing.
        public bool LockPlayerWhilePanelOpen = true;
        private bool _playerLockApplied;

        public ModuleHost(ModuleContext ctx)
        {
            _ctx = ctx;
            _cursor = new CursorController(ctx.Log);
        }

        public void Register(OverlayModule module)
        {
            module.Host = this;
            _modules.Add(module);
        }

        public int Count { get { return _modules.Count; } }

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

        private bool IsLive(OverlayModule m)
        {
            return !_failed.Contains(m);
        }

        // ------------------------------------------------------------------
        public void Tick()
        {
            _hotkeys.Dispatch();

            for (int i = 0; i < _modules.Count; i++)
            {
                OverlayModule m = _modules[i];
                if (!IsLive(m)) continue;
                try { m.Tick(); }
                catch (Exception ex) { Disable(m, "Tick", ex); }
            }

            // Cursor is asserted from Update (not LateUpdate) so that
            // VirtualCursor.LateUpdate is guaranteed to observe it - Unity
            // runs all Updates before any LateUpdate, while the order
            // between two LateUpdates is undefined.
            if (AnyPanelOpen())
            {
                // Player lock FIRST, cursor second. The game's LockView /
                // UnLockView also call Input.LockMouse/UnLockMouse, so if
                // the cursor were asserted first, releasing the player lock
                // in the same tick would re-lock the pointer behind us.
                if (AnyPanelWantsPlayerLock()) ApplyPlayerLock();
                else ReleasePlayerLock();

                _cursor.Acquire();
            }
            else
            {
                // On close the order is reversed: let the lock release
                // restore the game's own cursor state, then put back
                // whatever we saved.
                ReleasePlayerLock();
                _cursor.Release();
            }

            RefreshHudIfDue();
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
            if (!LockPlayerWhilePanelOpen || _playerLockApplied) return;
            if (_ctx.Bridge == null) return;

            _ctx.Bridge.ResolvePlayerController(_ctx.Player.Transform);
            _ctx.Bridge.SetPlayerLocked(true);
            _playerLockApplied = true;

            // Holding the player writes to the game, so it counts.
            _ctx.Practice.Mark("player lock");
        }

        private void ReleasePlayerLock()
        {
            if (!_playerLockApplied || _ctx.Bridge == null) return;
            _ctx.Bridge.SetPlayerLocked(false);
            _playerLockApplied = false;
        }

        public void SetLockPlayer(bool enabled)
        {
            LockPlayerWhilePanelOpen = enabled;
            if (!enabled) ReleasePlayerLock();
            else if (AnyPanelOpen() && AnyPanelWantsPlayerLock()) ApplyPlayerLock();
        }

        public void Shutdown()
        {
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
