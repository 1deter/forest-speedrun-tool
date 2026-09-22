using System;
using System.Reflection;
using BepInEx.Configuration;
using ForestOverlay.Core;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // What happens when the player dies.
    //
    //   1. Practice mode on and a current spot  -> REVIVE at the spot.
    //      Every death, the capture included. Health and blood reset, no
    //      reload, then teleported back. Writes state -> practice marker.
    //   2. Otherwise, quick-load on (default)   -> QUICK-LOAD the save.
    //      Every death, the boss-fight wake-up included. The capture (first
    //      death) has its own toggle, on by default - no current route
    //      relies on being captured (author's call), but a future one
    //      might. The author rules quick-load allowed in normal runs: it
    //      skips the death animation and the menu but loads through the
    //      title screen's own path, so the loaded game is identical.
    //   3. Otherwise                            -> the game's own death.
    //
    // Never: permadeath (the game deletes the save on death, nothing to
    // load) or multiplayer.
    //
    // QUICK-LOAD MECHANICS (IL): PlayerStats.GameOver loads "TitleScene".
    // There, TitleScreen.OnSinglePlayer / OnLoad / OnSlotSelection(slot)
    // are what the menu buttons call: SetPlayerMode(SP), SetInitType
    // (Continue), SetSlot, LoadSave.ShouldLoad = true, and MyLoader
    // activated. This module calls the same three once the title screen
    // is up, with the slot read at the moment of death.
    // ------------------------------------------------------------------
    public sealed class DeathModule : OverlayModule
    {
        private const float TitleTimeout = 60f;

        public override string Id { get { return "deaths"; } }
        public override string DisplayName { get { return "Deaths"; } }
        public override bool HasTab { get { return true; } }
        public override string TabTitle { get { return "Deaths"; } }
        public override int TabOrder { get { return 45; } }

        private DeathHooks _hooks;
        private ConfigEntry<bool> _quickLoadCfg;
        private ConfigEntry<bool> _quickLoadCaptureCfg;

        private PracticeModule _practice;
        private PracticeRunModule _runs;

        // Set from inside the game's death check, acted on in Tick.
        private bool _pendingRevive;
        private bool _pendingQuickLoad;
        private int _quickLoadSlot = -1;
        private float _quickLoadStarted;
        private int _titleSeenFrame = -1;

        private string _lastDeath = "none this session";
        private string _status = "";

        // Title screen reflection.
        private FieldInfo _titleInstance;
        private MethodInfo _onSinglePlayer;
        private MethodInfo _onLoad;
        private MethodInfo _onSlotSelection;
        private PropertyInfo _slotProp;
        private bool _titleResolved;

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);

            _quickLoadCfg = Ctx.Config.Bind("Deaths", "QuickLoadOnDeath", true,
                "On death, load the current save straight away through the title screen's own load path " +
                "instead of playing the death animation and returning to the menu.");

            _quickLoadCaptureCfg = Ctx.Config.Bind("Deaths", "QuickLoadOnCapture", true,
                "Quick-load also on the first death, which the game otherwise turns into the capture " +
                "(waking up in a cave). Off keeps the capture.");

            _practice = Host.Find<PracticeModule>();
            _runs = Host.Find<PracticeRunModule>();

            _hooks = new DeathHooks(ctx.Log);
            DeathHooks.Decide = Decide;
            DeathHooks.Handled = OnHandled;
            _hooks.Install(OverlayPlugin.PluginGuid);
        }

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("tab.deaths", KeyCode.None, "Open Deaths tab", OpenMyTab);
            map.Add("death.clearBlood", KeyCode.None, "Clear the blood overlay (practice)", ClearBlood);
        }

        public override void Shutdown()
        {
            DeathHooks.Decide = null;
            DeathHooks.Handled = null;
            if (_hooks != null) _hooks.Uninstall();
        }

        // ------------------------------------------------------------------
        // Called from inside PlayerStats.CheckDeath / Fell. Cheap, no throw.
        private DeathAction Decide(DeathKind kind)
        {
            if (kind == DeathKind.Multiplayer) return DeathAction.Normal;

            if (ReviveApplies()) return DeathAction.Revive;

            if (_quickLoadCfg.Value && kind != DeathKind.PermaDeath &&
                (kind != DeathKind.Capture || _quickLoadCaptureCfg.Value))
            {
                // Read the slot now, while the game that owns it is alive.
                _quickLoadSlot = ReadSlot();
                if (_quickLoadSlot >= 0) return DeathAction.QuickLoad;
            }

            return DeathAction.Normal;
        }

        private bool ReviveApplies()
        {
            return _runs != null && _runs.Enabled && _practice != null && _practice.HasSpot;
        }

        private void OnHandled(DeathKind kind, DeathAction action)
        {
            _lastDeath = kind + " -> " + action + " at " + DateTime.Now.ToString("HH:mm:ss");

            if (action == DeathAction.Revive) _pendingRevive = true;
            if (action == DeathAction.QuickLoad)
            {
                _pendingQuickLoad = true;
                _quickLoadStarted = Time.unscaledTime;
                _titleSeenFrame = -1;
                _status = "quick-loading slot " + _quickLoadSlot + "...";
            }
        }

        // ------------------------------------------------------------------
        public override void Tick()
        {
            if (_pendingRevive)
            {
                _pendingRevive = false;
                Ctx.Practice.Mark("death revive");
                if (_practice != null) _practice.ReturnToSpot();
                _status = "revived at '" + (_practice != null ? _practice.SpotLabel : "?") + "'";
            }

            if (_pendingQuickLoad) DriveTitleScreen();
        }

        private void DriveTitleScreen()
        {
            if (Time.unscaledTime - _quickLoadStarted > TitleTimeout)
            {
                _pendingQuickLoad = false;
                _status = "quick-load gave up: title screen never appeared - load from the menu";
                Ctx.Log.LogWarning("Quick-load: " + _status);
                return;
            }

            ResolveTitle();
            if (_titleInstance == null) return;

            UnityEngine.Object title = _titleInstance.GetValue(null) as UnityEngine.Object;
            if (title == null) return;

            // Give the title screen a frame after it appears, so its own
            // Awake/Start/OnEnable have run before we press its buttons.
            if (_titleSeenFrame < 0) { _titleSeenFrame = Time.frameCount; return; }
            if (Time.frameCount < _titleSeenFrame + 2) return;

            _pendingQuickLoad = false;
            try
            {
                _onSinglePlayer.Invoke(title, null);
                _onLoad.Invoke(title, null);
                _onSlotSelection.Invoke(title, new object[] { _quickLoadSlot });
                _status = "quick-loaded slot " + _quickLoadSlot;
                Ctx.Log.LogInfo("Quick-load: loading slot " + _quickLoadSlot + " via the title screen.");
            }
            catch (Exception ex)
            {
                _status = "quick-load failed: " + ex.Message + " - load from the menu";
                Ctx.Log.LogWarning("Quick-load: " + ex);
            }
        }

        private void ResolveTitle()
        {
            if (_titleResolved) return;
            _titleResolved = true;

            BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type t = GameBridge.FindGameType("TitleScreen");
            if (t != null)
            {
                _titleInstance = t.GetField("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                _onSinglePlayer = t.GetMethod("OnSinglePlayer", inst, null, Type.EmptyTypes, null);
                _onLoad = t.GetMethod("OnLoad", inst, null, Type.EmptyTypes, null);
                _onSlotSelection = t.GetMethod("OnSlotSelection", inst, null, new Type[] { typeof(int) }, null);
            }

            if (_titleInstance == null || _onSinglePlayer == null || _onLoad == null || _onSlotSelection == null)
            {
                _titleInstance = null;
                _status = "quick-load unavailable: TitleScreen methods not found";
                Ctx.Log.LogWarning("Quick-load: " + _status);
            }
        }

        private int ReadSlot()
        {
            try
            {
                if (_slotProp == null)
                {
                    Type setup = GameBridge.FindGameType("TheForest.Utils.GameSetup");
                    if (setup != null)
                        _slotProp = setup.GetProperty("Slot", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                }
                if (_slotProp == null) return -1;
                return Convert.ToInt32(_slotProp.GetValue(null, null));
            }
            catch (Exception) { return -1; }
        }

        private void ClearBlood()
        {
            DeathHooks.ClearBlood();
            Ctx.Practice.Mark("clear blood");
            _status = "blood overlay cleared";
        }

        // ------------------------------------------------------------------
        public override void DrawTab(Rect area)
        {
            float w = area.width;
            float y = 4f;

            bool ql = GUI.Toggle(new Rect(0, y, w, 22), _quickLoadCfg.Value, " Quick-load on death");
            if (ql != _quickLoadCfg.Value) _quickLoadCfg.Value = ql;
            y += 26f;

            if (_quickLoadCfg.Value)
            {
                bool cap = GUI.Toggle(new Rect(20, y, w - 20, 22), _quickLoadCaptureCfg.Value,
                                      " Also on the first death (instead of being captured)");
                if (cap != _quickLoadCaptureCfg.Value) _quickLoadCaptureCfg.Value = cap;
                y += 26f;
            }

            GUI.Label(new Rect(0, y, w, 40),
                      "A death loads your save at once, through the menu's own load. " +
                      "Not permadeath (the game deletes the save) or multiplayer.");
            y += 44f;

            GUI.Label(new Rect(0, y, w, 40),
                      "Practice mode on + a spot selected: a death revives you at the spot instead " +
                      "(health and blood reset, no reload). Marks the session as practice.");
            y += 48f;

            if (GUI.Button(new Rect(0, y, 160, 24), "Clear blood overlay")) ClearBlood();
            y += 32f;

            GUI.Label(new Rect(0, y, w, 20), "Hooks: " + _hooks.Status);
            y += 20f;
            GUI.Label(new Rect(0, y, w, 20), "Last death: " + _lastDeath);
            y += 20f;
            if (_status.Length > 0) GUI.Label(new Rect(0, y, w, 20), _status);
        }
    }
}
