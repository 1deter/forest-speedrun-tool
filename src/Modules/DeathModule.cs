using System;
using System.Reflection;
using BepInEx.Configuration;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // What happens when the player dies.
    //
    // "When I die" (runner feedback: the revive was confusing, worse with
    // practice mode on and another spot current) picks one of: Automatic
    // (the rules below, the default), Reload the save, Restart the current
    // spot (F7: its start state, Quick / Full as the spot says), Revive at
    // the current spot (a teleport, nothing restored), the game's own
    // death. The decision is Data/DeathPlan (pure, tested); the Deaths tab
    // shows "Next death: ..." - what it will do and why - so the choice is
    // never a guess. Automatic:
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
    // RELOAD IN PLACE (the author's idea, 2026-10-04; off by default - the
    // game's own load stays the default): "Reload the save: in place" reads
    // the slot's save and restores it like a start state's Quick load
    // (SavestateModule.ReloadSlotInPlace) - no scene load, ~1 s instead of
    // ~6 s (bridge, Slot 1). Not the game's load: never in run mode
    // (docs/run-mode.md), marks practice, and falls back to the load below
    // when it cannot apply (Data/DeathPlan.InPlaceRefusal: the endgame on
    // one side only, the save unreadable, a savestate action running) or
    // fails; the log line says which and why.
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
            "Reloading loads your save at once, with the game's own load. " +
            "Not permadeath (the game deletes the save) or multiplayer.");
        private static readonly GUIContent ReloadFallbackText = new GUIContent(
            "Reload save on death: used when the spot choice cannot apply (no current spot, run mode).");
        private static readonly GUIContent ChoiceTitle = new GUIContent("When I die:");
        private static readonly GUIContent ReloadHowTitle = new GUIContent("Reload the save:");
        private static readonly GUIContent ReloadWithLoad = new GUIContent(" with a load (as the game does)");
        private static readonly GUIContent ReloadInPlaceLabel = new GUIContent(" in place (fast)");
        private static readonly GUIContent ReloadInPlaceText = new GUIContent(
            "In place restores the save without a scene load, the way a start state's Quick load does (about 1 s " +
            "instead of 5-8 s). It is not the game's own load: it marks practice and is never used in run mode. " +
            "The game's load is used instead in run mode, when only one of you and the save is past the vault door, " +
            "or when the save cannot be read - the log line says why.");
        private static readonly GUIContent[] ChoiceLabels =
        {
            new GUIContent(" Automatic (default)"),
            new GUIContent(" Reload the save"),
            new GUIContent(" Restart the current spot (F7)"),
            new GUIContent(" Revive at the current spot (teleport only)"),
            new GUIContent(" The game's own death"),
        };
        private static readonly GUIContent[] ChoiceHints =
        {
            new GUIContent("Practice mode on, or a current spot with a start state: a death restarts that spot (F7). " +
                           "Otherwise Reload save on death below, if on; else the game's own death."),
            new GUIContent("Every death reloads your save (the toggles below for the first death and the boss fight still apply)."),
            new GUIContent("Health back, then the current spot's start state is restored - Quick or Full load as the spot says; " +
                           "a spot with no start state is a teleport there. The current spot is the last one you went to, " +
                           "restarted or saved. Marks practice."),
            new GUIContent("Health back and a teleport to the current spot. Nothing is restored, even when the spot has a " +
                           "start state. Marks practice."),
            new GUIContent("Nothing changes: the dead cam and the menu, the capture on a game's first death, " +
                           "the boss-room wake-up."),
        };
        private static readonly GUIContent RunModeChoiceText = new GUIContent(
            "Run mode: a restart or revive on death is locked during a run unless the run's category allows the " +
            "practice revive; Reload save on death follows the category. The line below says what happens instead.");

        private DeathHooks _hooks;
        private ConfigEntry<DeathChoice> _choiceCfg;
        private ConfigEntry<bool> _quickLoadCfg;
        private ConfigEntry<bool> _quickLoadCaptureCfg;
        private ConfigEntry<bool> _quickLoadBossCfg;
        private ConfigEntry<bool> _skipMenuCfg;
        private ConfigEntry<bool> _inPlaceCfg;
        private SavestateModule _savestates;
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
        private DeathOutcome _reviveOutcome;
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
        private readonly GUIContent _nextDeathText = new GUIContent("Next death: ...");
        private string _hooksShown, _lastDeathShown, _statusShown, _nextDeathShown;
        private float _nextDeathAt;

        // Title screen reflection.
        private FieldInfo _titleInstance;
        private Type _titleType;
        private MethodInfo _onSinglePlayer;
        private MethodInfo _onLoad;
        private MethodInfo _onSlotSelection;
        private PropertyInfo _slotProp;
        private bool _titleResolved;

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);

            _choiceCfg = Ctx.Config.Bind("Deaths", "OnDeath", DeathChoice.Automatic,
                "What a death does. Automatic: the rules from before this choice (practice mode on, or a current " +
                "spot with a start state, restarts the current spot; otherwise QuickLoadOnDeath). ReloadSave: " +
                "reload the save. RestartSpot: the current spot's restart (F7). ReviveAtSpot: health back and a " +
                "teleport to the current spot. GameDeath: the game's own death. The QuickLoad* keys still apply " +
                "wherever a reload happens; never permadeath or multiplayer.");

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
            _inPlaceCfg = Ctx.Config.Bind("Deaths", "ReloadInPlace", false,
                "Reload the save in place: the slot's save restored like a savestate's Quick load, no scene load " +
                "(fast; marks practice). Never in run mode; falls back to the load when it cannot apply. Off = " +
                "the game's own load (QuickLoadSkipMenu decides how).");
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
            _savestates = Host.Find<SavestateModule>();

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
        // Run mode: no revive unless the category allows it; Reload save on
        // death follows the category - by default the runner's setting, the
        // game's own load, a QoL saving menuing (author, 2026-10-02).
        private DeathAction Decide(DeathKind kind)
        {
            if (kind == DeathKind.Multiplayer) return DeathAction.Normal;
            try
            {
                // Read the slot now, while the game that owns it is alive.
                _quickLoadSlot = ReadSlot();
                DeathDecision d = DeathPlan.Decide(_choiceCfg.Value, Situation(kind, _quickLoadSlot >= 0));
                _lastDeath = DateTime.Now.ToString("HH:mm:ss") + " (" + kind + "): " + d.Text;
                Ctx.Log.LogInfo("Death (" + kind + ", " + _choiceCfg.Value + "): " + d.Text + ".");
                switch (d.Outcome)
                {
                    case DeathOutcome.RestartSpot:
                    case DeathOutcome.ReviveAtSpot:
                        _reviveOutcome = d.Outcome;
                        return DeathAction.Revive;
                    case DeathOutcome.ReloadSave:
                        // In place starts from in game too (death skipped).
                        return _skipMenuCfg.Value || _inPlaceCfg.Value ? DeathAction.QuickLoadInGame : DeathAction.QuickLoad;
                    default:
                        return DeathAction.Normal;
                }
            }
            catch (Exception ex)
            {
                Ctx.Log.LogWarning("Death: deciding failed, the game's own death - " + ex.Message);
                return DeathAction.Normal;
            }
        }

        /// What a death's decision reads (DeathPlan). The start state is a
        /// file check: per death, and once a second for the open tab.
        private DeathSituation Situation(DeathKind kind, bool slotKnown)
        {
            DeathSituation s = new DeathSituation();
            s.Kind = kind == DeathKind.Capture ? DeathCase.Capture
                   : kind == DeathKind.BossWake ? DeathCase.BossWake
                   : kind == DeathKind.PermaDeath ? DeathCase.PermaDeath
                   : kind == DeathKind.Multiplayer ? DeathCase.Multiplayer
                   : DeathCase.Real;

            Segment cur = _practice != null ? _practice.CurrentSegment : null;
            s.HasSpot = cur != null && _practice.HasSpot;
            s.SpotName = s.HasSpot ? cur.Name : "";
            s.HasStartState = s.HasSpot && _practice.CurrentHasStartState;
            // A run spot's restart always loads (PracticeModule.Restart).
            s.FullLoad = s.HasStartState && (cur.StartRestoreWithLoad || cur.RunCategory.Length > 0);
            s.PracticeOn = _runs != null && _runs.Enabled;

            s.ReloadOn = _quickLoadCfg.Value;
            s.ReloadOnCapture = _quickLoadCaptureCfg.Value;
            s.ReloadInBoss = _quickLoadBossCfg.Value;
            s.SlotKnown = slotKnown;

            s.ReviveLocked = Ctx.Run.Locks("revive");
            // Restart refuses every spot but the run's own during a run.
            s.RestartLocked = Ctx.Run.Locks("restart") && !(_practice != null && _practice.CurrentIsRunSpot);
            s.GoLocked = Ctx.Run.Locks("go");
            s.ReloadLocked = Ctx.Run.Locks("reload");
            s.ReloadForced = Ctx.Run.Forces("reload");
            s.ReloadInPlace = _inPlaceCfg.Value;
            s.RunActive = Ctx.Run.Active;
            return s;
        }

        /// A Reload save on death is under way: run mode keeps the attempt
        /// through its title screen / load (RunModeModule consumes it).
        public bool ReloadPending { get; private set; }

        public void ConsumeReload() { ReloadPending = false; }

        private void OnHandled(DeathKind kind, DeathAction action)
        {
            if (action == DeathAction.QuickLoad || action == DeathAction.QuickLoadInGame) ReloadPending = true;

            if (action == DeathAction.Revive) _pendingRevive = true;
            if (action == DeathAction.QuickLoadInGame)
            {
                _pendingInGameLoad = true;
                _status = "reloading slot " + _quickLoadSlot + (_inPlaceCfg.Value ? "..." : " without the menu...");
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
            RefreshNextDeath();

            // Run mode: the toggles follow the category (locked: they keep
            // their saved value but do nothing; forced: on for the run).
            bool noStagger = Ctx.Run.Forces("nostagger") || (_noStaggerCfg.Value && !Ctx.Run.Locks("nostagger"));
            bool noBlood = Ctx.Run.Forces("nostagger") || (_noBloodCfg.Value && !Ctx.Run.Locks("nostagger"));
            bool godMode = Ctx.Run.Forces("godmode") || (_godModeCfg.Value && !Ctx.Run.Locks("godmode"));

            DeathHooks.NoStagger = noStagger;
            Ctx.Practice.SetOn("no stagger", noStagger);
            Ctx.Practice.SetOn("no blood", noBlood);
            Ctx.Practice.SetOn("god mode", godMode);
            if (noBlood) DeathHooks.ClearBlood();
            if ((noBlood || noStagger) && !_extrasMarked)
            {
                _extrasMarked = true;
                Ctx.Practice.Mark(noBlood && noStagger ? "no blood, no stagger"
                                  : noBlood ? "no blood" : "no stagger");
                Ctx.Log.LogInfo("Deaths: practice toggles on -" + (noBlood ? " no blood" : "") +
                                (noStagger ? " no stagger" : "") + ".");
            }
            if (!noBlood && !noStagger) _extrasMarked = false;

            // God mode: kept on while the toggle is (a load or the console may
            // reset the flag); switched off only if we switched it on.
            if (godMode && !PlayerRef.AtTitleScreen && !DeathHooks.IsGodMode())
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
            else if (!godMode && _godModeOurs)
            {
                _godModeOurs = false;
                DeathHooks.SetGodMode(false);
                Ctx.Log.LogInfo("Deaths: god mode off.");
            }

            if (_pendingRevive)
            {
                _pendingRevive = false;
                Ctx.Practice.Mark("death revive");
                bool teleportOnly = _reviveOutcome == DeathOutcome.ReviveAtSpot;
                if (_practice != null)
                {
                    if (teleportOnly) _practice.TeleportToCurrent();
                    else _practice.ReturnToSpot();
                }
                _status = (teleportOnly ? "revived at '" : "restarted '") +
                          (_practice != null ? _practice.SpotLabel : "?") + "'";
            }

            if (_pendingInGameLoad) LoadInGame();
            if (_pendingQuickLoad) DriveTitleScreen();
        }

        private void LoadInGame()
        {
            _pendingInGameLoad = false;
            string note = "";
            if (_inPlaceCfg.Value)
            {
                int slot = _quickLoadSlot;
                float start = Time.realtimeSinceStartup;
                string why = _savestates == null ? "the savestate engine is missing"
                           : _savestates.ReloadSlotInPlace(slot, delegate(string error) { InPlaceDone(slot, start, error); });
                if (why == null)
                {
                    // A restore can finish (or fail) before it returns.
                    if (_status.StartsWith("reloading", StringComparison.Ordinal)) _status = "reloading slot " + slot + " in place...";
                    return;
                }
                note = " - in place cannot apply: " + why;
            }
            LoadWithLoad(note);
        }

        // One log line per reload in place: done (and how long, death to
        // playable), or failed and loading instead.
        private void InPlaceDone(int slot, float start, string error)
        {
            float took = Time.realtimeSinceStartup - start;
            if (error == null)
            {
                // No load follows: nothing for run mode to carry over (and
                // run mode never reloads in place).
                ReloadPending = false;
                _status = "reloaded slot " + slot + " in place (" + took.ToString("0.0") + " s)";
                Ctx.Log.LogInfo("Reload save on death: slot " + slot + " reloaded in place in " + took.ToString("0.00") +
                                " s (a Quick load of the slot's save).");
                return;
            }
            LoadWithLoad(" - in place failed after " + took.ToString("0.0") + " s: " + error);
        }

        /// The game's own load of the slot: from in game (Resume) or, with
        /// Skip the title screen off, through the menu. `note`: why not in place.
        private void LoadWithLoad(string note)
        {
            if (!_skipMenuCfg.Value)
            {
                Ctx.Log.LogInfo("Quick-load: loading slot " + _quickLoadSlot + " via the title screen" + note + ".");
                if (DeathHooks.GameOverNow()) StartMenuLoad("reloading slot " + _quickLoadSlot + "...");
                else _status = "reload failed: the death's game is gone - load from the menu";
                return;
            }

            string err = _loader.LoadSlotWithoutMenu();
            if (err == null)
            {
                _status = "reloaded slot " + _quickLoadSlot + " without the menu";
                Ctx.Log.LogInfo("Quick-load: loading slot " + _quickLoadSlot + " from in game (no menu)" + note + ".");
                return;
            }

            // Fall back to the menu path: the death was skipped, so end it
            // the game's way and let the title screen load the slot.
            Ctx.Log.LogWarning("Quick-load without the menu failed (" + err + ") - using the menu" + note + ".");
            if (DeathHooks.GameOverNow())
                StartMenuLoad("reloading slot " + _quickLoadSlot + " via the menu (in-game load failed)...");
            else
                _status = "reload failed: " + err + " - load from the menu";
        }

        private void StartMenuLoad(string status)
        {
            _pendingQuickLoad = true;
            _quickLoadStarted = Time.unscaledTime;
            _titleSeenFrame = -1;
            _status = status;
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

            // Instance is null on a return to the title (T-0247).
            UnityEngine.Object title = TitleLoad.FindTitle(_titleType);
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
                _titleType = t;
                _titleInstance =t.GetField("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
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

        // "Next death: ..." - only while the tab shows, once a second (the
        // start state is a file check), rebuilt only when it changes.
        private void RefreshNextDeath()
        {
            if (!TabShowing || Time.unscaledTime < _nextDeathAt) return;
            _nextDeathAt = Time.unscaledTime + 1f;

            string text;
            if (PlayerRef.AtTitleScreen || !Ctx.Player.Found)
                text = "Next death: no game loaded.";
            else
            {
                DeathKind kind = DeathHooks.PredictKind();
                if (kind == DeathKind.Multiplayer)
                    text = "Next death: the game's own - multiplayer is never changed.";
                else
                    text = "Next death: " + DeathPlan.Decide(_choiceCfg.Value, Situation(kind, ReadSlot() >= 0)).Text + ".";
            }
            if (text != _nextDeathShown) { _nextDeathShown = text; _nextDeathText.text = text; }
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

            // The one choice: what a death does.
            GUI.Label(new Rect(0, y, w, 20), ChoiceTitle);
            y += 22f;
            DeathChoice choice = _choiceCfg.Value;
            int ci = (int)choice;
            if (ci < 0 || ci >= ChoiceLabels.Length) ci = 0;
            for (int i = 0; i < ChoiceLabels.Length; i++)
            {
                bool on = GUI.Toggle(new Rect(10, y, w - 10, 22), i == ci, ChoiceLabels[i]);
                if (on && i != ci)
                {
                    _choiceCfg.Value = (DeathChoice)i;
                    _nextDeathAt = 0f;   // the line follows on the next tick
                }
                y += 24f;
            }
            y += UiText.DrawDim(10, y, w - 10, ChoiceHints[ci]) + 4f;
            if (Ctx.Run.Active) y += UiText.Draw(10, y, w - 10, RunModeChoiceText) + 4f;
            y += UiText.Draw(0, y, w, _nextDeathText) + 10f;

            // Reloading: not shown when a death is always the game's own.
            bool reloadPicked = choice == DeathChoice.ReloadSave;
            if (choice != DeathChoice.GameDeath && !reloadPicked)
            {
                bool ql = GUI.Toggle(new Rect(0, y, w, 22), _quickLoadCfg.Value, " Reload save on death");
                if (ql != _quickLoadCfg.Value) { _quickLoadCfg.Value = ql; _nextDeathAt = 0f; }
                y += 26f;
                if (choice != DeathChoice.Automatic) y += UiText.DrawDim(20, y, w - 20, ReloadFallbackText) + 4f;
            }

            if (choice != DeathChoice.GameDeath && (_quickLoadCfg.Value || reloadPicked))
            {
                bool cap = GUI.Toggle(new Rect(20, y, w - 20, 22), _quickLoadCaptureCfg.Value,
                                      " Also on the first death (instead of being captured)");
                if (cap != _quickLoadCaptureCfg.Value) _quickLoadCaptureCfg.Value = cap;
                y += 26f;

                bool boss = GUI.Toggle(new Rect(20, y, w - 20, 22), _quickLoadBossCfg.Value,
                                       " Also in the boss fight (instead of waking up in the boss room)");
                if (boss != _quickLoadBossCfg.Value) _quickLoadBossCfg.Value = boss;
                y += 26f;

                GUI.Label(new Rect(20, y, w - 20, 20), ReloadHowTitle);
                y += 22f;
                bool withLoad = GUI.Toggle(new Rect(30, y, w - 30, 22), !_inPlaceCfg.Value, ReloadWithLoad);
                if (withLoad && _inPlaceCfg.Value) { _inPlaceCfg.Value = false; _nextDeathAt = 0f; }
                y += 24f;
                bool skip = GUI.Toggle(new Rect(50, y, w - 50, 22), _skipMenuCfg.Value,
                                       " Skip the title screen (faster; off = load through the menu)");
                if (skip != _skipMenuCfg.Value) _skipMenuCfg.Value = skip;
                y += 24f;
                bool inPlace = GUI.Toggle(new Rect(30, y, w - 30, 22), _inPlaceCfg.Value, ReloadInPlaceLabel);
                if (inPlace && !_inPlaceCfg.Value) { _inPlaceCfg.Value = true; _nextDeathAt = 0f; }
                y += 24f;
                y += UiText.DrawDim(30, y, w - 30, ReloadInPlaceText) + 6f;
            }

            if (choice != DeathChoice.GameDeath) y += UiText.DrawDim(0, y, w, QuickLoadText) + 8f;

            // Practice toggles - not tied to dying, so they work in Creative.
            bool guiWas = GUI.enabled;
            if (Ctx.Run.Active)
            {
                y += UiText.Draw(0, y, w, "Run mode: these three follow the run's category (Runs tab) and cannot be changed during a run.") + 2f;
                GUI.enabled = false;
            }
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
            GUI.enabled = guiWas;
            // What the button above (or the last death) just did, under it.
            y += UiText.Draw(0, y, w, _statusText) + 4f;

            y += UiText.DrawDim(0, y, w, _hooksText);
            UiText.DrawDim(0, y, w, _lastDeathText);
        }
    }
}
