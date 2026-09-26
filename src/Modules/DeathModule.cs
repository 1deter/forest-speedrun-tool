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
    //      A current spot WITH A START STATE revives even with practice
    //      mode off: the restart restores the state, the segment's way
    //      (author, 2026-09-23: practising from a savestate should reload
    //      it entirely on death - v0.21.1 quick-loaded the slot instead).
    //   2. Otherwise, quick-load on (default)   -> QUICK-LOAD the save.
    //      Every death. The capture (first death) and the boss-fight
    //      wake-up each have their own toggle, both on by default - no
    //      current route relies on being captured (author's call), but a
    //      future one might; the boss toggle was the author's request
    //      (2026-09-23) so the game's own wake-up can be kept. The author rules quick-load allowed in normal runs: it
    //      skips the death animation and the menu but loads through the
    //      title screen's own path, so the loaded game is identical.
    //   3. Otherwise                            -> the game's own death.
    //
    // Never: permadeath (the game deletes the save on death, nothing to
    // load) or multiplayer.
    //
    // SKIPPING THE MENU (default since v0.20.3, author's call 2026-09-23:
    // "faster load with no compromise"). The death is skipped and the slot
    // is loaded from in game with LevelSerializer.Resume() - exactly what
    // LoadSave.Awake does once the menu has loaded the game scene, so the
    // loaded game is the same; the title scene and one of the two game-scene
    // loads are cut. Measured 5.2 s vs ~7 s through the menu. Off, or if
    // Resume cannot start, the menu path below is used.
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

        private static readonly GUIContent QuickLoadText = new GUIContent(
            "A death loads your save at once, with the game's own load. " +
            "Not permadeath (the game deletes the save) or multiplayer.");
        private static readonly GUIContent ReviveText = new GUIContent(
            "Practice mode on + a spot selected: a death revives you at the spot instead " +
            "(health and blood reset, no reload). A spot with a start state does this " +
            "even with practice mode off, and restores the start state. Marks the session as practice.");

        private DeathHooks _hooks;
        private ConfigEntry<bool> _quickLoadCfg;
        private ConfigEntry<bool> _quickLoadCaptureCfg;
        private ConfigEntry<bool> _quickLoadBossCfg;
        private ConfigEntry<bool> _skipMenuCfg;
        private ConfigEntry<bool> _noBloodCfg;
        private ConfigEntry<bool> _noStaggerCfg;
        private ConfigEntry<bool> _godModeCfg;
        // True while Cheats.GodMode is on because of our toggle, so turning
        // it off hands back only what we took (gotcha 23).
        private bool _godModeOurs;
        private bool _extrasMarked;
        private SavestateBridge _loader;
        private bool _pendingInGameLoad;

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

        // Built in Tick when a source changes, never in DrawTab.
        private readonly GUIContent _hooksText = new GUIContent("");
        private readonly GUIContent _lastDeathText = new GUIContent("");
        private readonly GUIContent _statusText = new GUIContent("");
        private string _hooksShown, _lastDeathShown, _statusShown;

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
                "Reload the save also on the first death, which the game otherwise turns into the capture " +
                "(waking up in a cave). Off keeps the capture.");

            _quickLoadBossCfg = Ctx.Config.Bind("Deaths", "QuickLoadInBossFight", true,
                "Reload the save also on a death in the endgame boss fight, which the game otherwise turns into " +
                "waking up in the boss room. Off keeps the game's wake-up.");

            _skipMenuCfg = Ctx.Config.Bind("Deaths", "QuickLoadSkipMenu", true,
                "Reload the save from in game (LevelSerializer.Resume) instead of through the title screen: " +
                "the same load, one scene load fewer. Off uses the menu path.");
            // Practice toggles, separate from what a death does (author,
            // 2026-09-23): off by default, so survival keeps the game's
            // own feel; they also cover Creative, where nobody dies.
            _noBloodCfg = Ctx.Config.Bind("Deaths", "NoBlood", false,
                "Practice: never show the blood overlay - it is cleared continuously while this is on, not only on death.");
            _noStaggerCfg = Ctx.Config.Bind("Deaths", "NoStagger", false,
                "Practice: skip the hard-landing stagger and its aftermath (frozen input, slow look, no jump, arms down) " +
                "on every hard landing, as the fall revive does.");
            _godModeCfg = Ctx.Config.Bind("Deaths", "GodMode", false,
                "Practice: the game's own god mode (Cheats.GodMode, console _godmode) - no damage taken.");
            _loader = new SavestateBridge(ctx.Log);

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
                (kind != DeathKind.Capture || _quickLoadCaptureCfg.Value) &&
                (kind != DeathKind.BossWake || _quickLoadBossCfg.Value))
            {
                // Read the slot now, while the game that owns it is alive.
                _quickLoadSlot = ReadSlot();
                if (_quickLoadSlot >= 0)
                    return _skipMenuCfg.Value ? DeathAction.QuickLoadInGame : DeathAction.QuickLoad;
            }

            return DeathAction.Normal;
        }

        private bool ReviveApplies()
        {
            if (_practice == null || !_practice.HasSpot) return false;
            return (_runs != null && _runs.Enabled) || _practice.CurrentHasStartState;
        }

        private void OnHandled(DeathKind kind, DeathAction action)
        {
            _lastDeath = kind + " -> " + action + " at " + DateTime.Now.ToString("HH:mm:ss");

            if (action == DeathAction.Revive) _pendingRevive = true;
            if (action == DeathAction.QuickLoadInGame)
            {
                _pendingInGameLoad = true;
                _status = "reloading slot " + _quickLoadSlot + " without the menu...";
            }
            if (action == DeathAction.QuickLoad)
            {
                _pendingQuickLoad = true;
                _quickLoadStarted = Time.unscaledTime;
                _titleSeenFrame = -1;
                _status = "reloading slot " + _quickLoadSlot + "...";
            }
        }

        // ------------------------------------------------------------------
        public override void Tick()
        {
            RefreshText();

            DeathHooks.NoStagger = _noStaggerCfg.Value;
            if (_noBloodCfg.Value) DeathHooks.ClearBlood();
            if ((_noBloodCfg.Value || _noStaggerCfg.Value) && !_extrasMarked)
            {
                _extrasMarked = true;
                Ctx.Practice.Mark(_noBloodCfg.Value && _noStaggerCfg.Value ? "no blood, no stagger"
                                  : _noBloodCfg.Value ? "no blood" : "no stagger");
                Ctx.Log.LogInfo("Deaths: practice toggles on -" + (_noBloodCfg.Value ? " no blood" : "") +
                                (_noStaggerCfg.Value ? " no stagger" : "") + ".");
            }
            if (!_noBloodCfg.Value && !_noStaggerCfg.Value) _extrasMarked = false;

            // God mode: kept on while the toggle is (a load or the console may
            // reset the flag); switched off only if we switched it on.
            if (_godModeCfg.Value && !PlayerRef.AtTitleScreen && !DeathHooks.IsGodMode())
            {
                if (DeathHooks.SetGodMode(true))
                {
                    if (!_godModeOurs)
                    {
                        Ctx.Practice.Mark("god mode");
                        Ctx.Log.LogInfo("Deaths: god mode on (Cheats.GodMode).");
                    }
                    _godModeOurs = true;
                }
            }
            else if (!_godModeCfg.Value && _godModeOurs)
            {
                _godModeOurs = false;
                DeathHooks.SetGodMode(false);
                Ctx.Log.LogInfo("Deaths: god mode off.");
            }

            if (_pendingRevive)
            {
                _pendingRevive = false;
                Ctx.Practice.Mark("death revive");
                if (_practice != null) _practice.ReturnToSpot();
                _status = "revived at '" + (_practice != null ? _practice.SpotLabel : "?") + "'";
            }

            if (_pendingInGameLoad) LoadInGame();
            if (_pendingQuickLoad) DriveTitleScreen();
        }

        private void LoadInGame()
        {
            _pendingInGameLoad = false;
            string err = _loader.LoadSlotWithoutMenu();
            if (err == null)
            {
                _status = "reloaded slot " + _quickLoadSlot + " without the menu";
                Ctx.Log.LogInfo("Quick-load: loading slot " + _quickLoadSlot + " from in game (no menu).");
                return;
            }

            // Fall back to the menu path: the death was skipped, so end it
            // the game's way and let the title screen load the slot.
            Ctx.Log.LogWarning("Quick-load without the menu failed (" + err + ") - using the menu.");
            if (DeathHooks.GameOverNow())
            {
                _pendingQuickLoad = true;
                _quickLoadStarted = Time.unscaledTime;
                _titleSeenFrame = -1;
                _status = "reloading slot " + _quickLoadSlot + " via the menu (in-game load failed)...";
            }
            else
            {
                _status = "reload failed: " + err + " - load from the menu";
            }
        }

        private void DriveTitleScreen()
        {
            if (Time.unscaledTime - _quickLoadStarted > TitleTimeout)
            {
                _pendingQuickLoad = false;
                _status = "reload gave up: title screen never appeared - load from the menu";
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
                _status = "reloaded slot " + _quickLoadSlot;
                Ctx.Log.LogInfo("Quick-load: loading slot " + _quickLoadSlot + " via the title screen.");
            }
            catch (Exception ex)
            {
                _status = "reload failed: " + ex.Message + " - load from the menu";
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
                _status = "reload unavailable: TitleScreen methods not found";
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

        private void RefreshText()
        {
            string hooks = _hooks.Status;
            if (!ReferenceEquals(hooks, _hooksShown)) { _hooksShown = hooks; _hooksText.text = "Hooks: " + hooks; }
            if (!ReferenceEquals(_lastDeath, _lastDeathShown)) { _lastDeathShown = _lastDeath; _lastDeathText.text = "Last death: " + _lastDeath; }
            if (!ReferenceEquals(_status, _statusShown)) { _statusShown = _status; _statusText.text = _status; }
        }

        // ------------------------------------------------------------------
        public override void DrawTab(Rect area)
        {
            float w = area.width;
            float y = 4f;

            bool ql = GUI.Toggle(new Rect(0, y, w, 22), _quickLoadCfg.Value, " Reload save on death");
            if (ql != _quickLoadCfg.Value) _quickLoadCfg.Value = ql;
            y += 26f;

            if (_quickLoadCfg.Value)
            {
                bool cap = GUI.Toggle(new Rect(20, y, w - 20, 22), _quickLoadCaptureCfg.Value,
                                      " Also on the first death (instead of being captured)");
                if (cap != _quickLoadCaptureCfg.Value) _quickLoadCaptureCfg.Value = cap;
                y += 26f;

                bool boss = GUI.Toggle(new Rect(20, y, w - 20, 22), _quickLoadBossCfg.Value,
                                       " Also in the boss fight (instead of waking up in the boss room)");
                if (boss != _quickLoadBossCfg.Value) _quickLoadBossCfg.Value = boss;
                y += 26f;

                bool skip = GUI.Toggle(new Rect(20, y, w - 20, 22), _skipMenuCfg.Value,
                                       " Skip the title screen (faster; off = load through the menu)");
                if (skip != _skipMenuCfg.Value) _skipMenuCfg.Value = skip;
                y += 26f;
            }

            y += UiText.Draw(0, y, w, QuickLoadText) + 4f;
            y += UiText.Draw(0, y, w, ReviveText) + 4f;

            // Practice toggles - not tied to dying, so they work in Creative.
            bool noBlood = GUI.Toggle(new Rect(0, y, w, 22), _noBloodCfg.Value,
                                      " No blood: keep the blood overlay off (practice)");
            if (noBlood != _noBloodCfg.Value) _noBloodCfg.Value = noBlood;
            y += 26f;
            bool noStagger = GUI.Toggle(new Rect(0, y, w, 22), _noStaggerCfg.Value,
                                        " No stagger: skip the hard-landing stagger on every landing (practice)");
            if (noStagger != _noStaggerCfg.Value) _noStaggerCfg.Value = noStagger;
            y += 26f;
            bool god = GUI.Toggle(new Rect(0, y, w, 22), _godModeCfg.Value,
                                  " God mode: take no damage - the game's own cheat (practice)");
            if (god != _godModeCfg.Value) _godModeCfg.Value = god;
            y += 26f;
            // What the button above (or the last death) just did, under it.
            y += UiText.Draw(0, y, w, _statusText) + 4f;

            y += UiText.DrawDim(0, y, w, _hooksText);
            UiText.DrawDim(0, y, w, _lastDeathText);
        }
    }
}
