using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Run mode (Core/RunMode; author, 2026-10-02).
    //
    // A run starts on a run spot (author, 2026-10-02: runs start from
    // preset category saves, not new games): Restart on a spot with a run
    // category restores its start state with a Full load (Modules/Practice
    // calls SpotRunStarting / SpotRunReady), and the attempt starts there.
    // Run mode then locks every practice feature (they ask
    // Ctx.Run.Refuse). A reset is the next attempt: Restart on the run's
    // spot again, or the title screen (then that Restart starts the next).
    // A game loaded any other way ends run mode. The fallback for a
    // category with no run spot: Start run mode by hand (Runs tab) - the
    // attempt starts in the game as it is, and every later load (new game
    // or save) after a reset is the next attempt. End run mode (two clicks,
    // Runs tab) unlocks practice.
    //
    // Each attempt gets a report (Data/RunReport): the game's code, other
    // mods, other code, foreign Harmony patches, the game's cheats, run
    // mode's flags - written to config/ForestOverlay/run-reports/ and
    // summarised in the log and the Runs tab. The patches and cheats are
    // re-read during the attempt (a mod can patch late, the console can
    // switch a cheat on).
    //
    // No tab of its own: the Runs tab draws its section (DrawSection).
    // ------------------------------------------------------------------
    public sealed partial class RunModeModule : OverlayModule
    {
        public override string Id { get { return "runmode"; } }
        public override string DisplayName { get { return "Run mode"; } }

        private BridgeModule _bridge;
        private DeathModule _death;
        private FieldInfo _finishLoad;      // Scene.FinishGameLoad (static)
        private bool _loaded;               // last frame: in a loaded game
        private bool _attemptOpen;          // an attempt is running (not reset yet)
        private bool _spotStarting;         // a run spot's Full load is under way
        private bool _byHand;               // run mode started with Start run mode
        private RunReport _report;
        private string _reportPath;
        private float _nextCheck;
        private int _patchCount = -1;
        private bool _reportDirty;

        private float _confirmUntil;        // End run mode: the second click's window
        private readonly GUIContent _stateText = new GUIContent("");
        private readonly GUIContent _findingsText = new GUIContent("");
        private string _builtFor;
        private float _nextText;

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);
            _bridge = Host.Find<BridgeModule>();
            _death = Host.Find<DeathModule>();
            _upload = Host.Find<RunUploadModule>();
            InitCodes(ctx);
            InitCategories(ctx);
            Type scene = GameBridge.FindGameType("TheForest.Utils.Scene");
            if (scene != null) _finishLoad = scene.GetField("FinishGameLoad", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            RunIntegrity.StartHashing(ctx.Log);
            RebuildText();
        }

        private bool InLoadedGame()
        {
            if (!Ctx.Player.Found || PlayerRef.AtTitleScreen) return false;
            if (_finishLoad == null) return true;
            try { return (bool)_finishLoad.GetValue(null); }
            catch (Exception) { return true; }
        }

        public override void Tick()
        {
            bool loaded = InLoadedGame();
            if (loaded && !_loaded) OnGameLoaded();
            bool reloading = _death != null && _death.ReloadPending;
            if (PlayerRef.AtTitleScreen && _attemptOpen && !reloading) EndAttempt("back to the title screen (a reset)", "title screen");
            _loaded = loaded;

            // Only while the attempt plays: a save loading after a reset is
            // not the attempt (v0.24.208 flagged Slot 1's Creative cheats).
            if (Ctx.Run.Active && _attemptOpen && _report != null && loaded && Time.unscaledTime >= _nextCheck)
            {
                _nextCheck = Time.unscaledTime + 1f;
                Watch();
            }
            if (_reportDirty) { _reportDirty = false; WriteReport(); }
            TickChain();
            TickCategories();
            RebuildText();
            RebuildCategoryText();
        }

        private void OnGameLoaded()
        {
            // A run spot's own load: SpotRunReady starts the attempt.
            if (_spotStarting) return;

            // Reload save on death: the same attempt goes on.
            if (_death != null && _death.ReloadPending)
            {
                _death.ConsumeReload();
                if (Ctx.Run.Active)
                {
                    Ctx.Log.LogInfo("Run mode: attempt " + Ctx.Run.Attempt + " goes on after Reload save on death.");
                    return;
                }
            }
            if (!Ctx.Run.Active) return;
            // Started by hand: the next game after a reset is the next attempt.
            if (_byHand)
            {
                StartAttempt(RunIntegrity.Describe() + ", run mode started by hand", ByHandLabel());
                return;
            }
            Ctx.Run.End("a game was loaded outside the run's spot - Restart on a run spot starts the next run");
        }

        /// Practice: Restart on a run spot is about to restore its start
        /// state. A running attempt ends here - a reset.
        public void SpotRunStarting(Segment s)
        {
            if (_attemptOpen) EndAttempt("reset - Restart on the run's spot", "reset");
            _spotStarting = true;
            CheckCategoriesSoon();   // usually back before the restore ends
            Ctx.Log.LogInfo("Run mode: '" + s.Name + "' (" + s.RunCategory + ") - starting a run.");
        }

        /// Practice: the run spot's restore has finished (error null) and the
        /// player stands at the spot - the attempt starts now.
        public void SpotRunReady(Segment s, string error)
        {
            _spotStarting = false;
            if (error != null)
            {
                Ctx.Log.LogWarning("Run mode: the run's spot '" + s.Name + "' did not load - no attempt: " + error);
                if (Ctx.Run.Active) Ctx.Run.End("the run's spot did not load: " + error);
                return;
            }
            _byHand = false;
            _runSpot = s;
            string from = s.RunCategory + " from '" + s.Name + "' (" +
                          (SegmentLibrary.IsCommunity(s) ? "community" : "own") + " spot " + s.Id +
                          ", start state " + (s.StartState.Length > 0 ? s.StartState : "not hashed") + ")";
            RunCategory cat = CategoryFor(s);
            if (cat == null) Ctx.Log.LogWarning("Run mode: the run spot's category '" + s.RunCategory + "' is not one of the site's " +
                                                _categories.Count + " - the defaults apply (everything locked).");
            StartAttempt(from + ", " + RunIntegrity.Describe(), cat != null ? cat.Name : s.RunCategory);
        }

        /// The fallback for a category with no run spot: run mode on now,
        /// the attempt starting in the game as it is.
        public void StartByHand()
        {
            if (Ctx.Run.Active) return;
            if (!InLoadedGame()) { Ctx.Notice.Show("Run mode: load a game first.", 6f); return; }
            _byHand = true;
            _runSpot = null;
            StartAttempt(RunIntegrity.Describe() + ", run mode started by hand", ByHandLabel());
        }

        // The HUD's label for a run started by hand: the category, else the game.
        private string ByHandLabel()
        {
            RunCategory cat = CategoryFor(null);
            return cat != null ? cat.Name : RunIntegrity.Describe();
        }

        private void StartAttempt(string started, string label)
        {
            string before = Ctx.Practice.Used ? Ctx.Practice.Reason : "";
            Ctx.Practice.Reset();   // an attempt starts clean; the report keeps what came before
            RunCategory cat = CategoryFor(_runSpot);
            Ctx.Run.Begin(started, label, cat);
            _attemptOpen = true;

            _report = new RunReport();
            _report.Attempt = Ctx.Run.Attempt;
            _report.Started = Ctx.Run.Started;
            _report.StartedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _report.PluginVersion = OverlayPlugin.PluginVersion;
            _report.PracticeBefore = before;
            // The category it runs under (the site judges against this
            // version), or the run spot's name for one the site lacks.
            _report.Category = cat != null ? cat.Id : _runSpot != null ? RunCategory.Slug(_runSpot.RunCategory) : "";
            _report.CategoryVersion = cat != null ? cat.Version : 0;
            _report.Difficulty = RunIntegrity.Difficulty();
            _report.Creative = RunIntegrity.IsCreative();
            _report.Multiplayer = RunIntegrity.IsMultiplayer();
            RunIntegrity.Gather(_report, OverlayPlugin.PluginGuid, Ctx.PluginPath);
            _patchCount = RunIntegrity.PatchedCount();
            _reportPath = Path.Combine(Path.Combine(Ctx.ConfigDirectory, "run-reports"),
                                       "attempt-" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
            Watch();
            WriteReport();
            BeginChain(label);

            Ctx.Log.LogInfo("Run mode: attempt " + _report.Attempt + " started (" + _report.Started + ") - practice locked; " +
                            _report.Summary() + ".");
            if (!Host.AnyPanelOpen())
                Ctx.Notice.Show("Run mode: attempt " + _report.Attempt + " (" + label + ") - practice features are locked. End run mode in the Runs tab to practise.", 7f);
        }

        /// `reason`: the log's short word (reset, finished, title screen, ...).
        private void EndAttempt(string why, string reason)
        {
            _attemptOpen = false;
            Ctx.Run.Reset();
            CheckCategoriesSoon();   // a new version is ready for the next attempt
            if (_report != null)
            {
                Watch();   // the last flags into the report and the chain
                WriteReport();
                EndChain(reason);
                Ctx.Log.LogInfo("Run mode: attempt " + _report.Attempt + " ended - " + why + "; " + _report.Summary() + ".");
            }
        }

        // Once a second during an attempt: what can change while it runs.
        private void Watch()
        {
            if (_bridge != null && _bridge.Enabled)
            {
                if (Ctx.Run.Locks("bridge")) Ctx.Run.Flag("the test bridge is on");
                else Ctx.Run.Use("bridge");
            }
            if (_report.Used.Count != Ctx.Run.Used.Count)
            {
                _report.Used.Clear();
                _report.Used.AddRange(Ctx.Run.Used);
                _reportDirty = true;
            }

            int cheats = _report.Cheats.Count;
            RunIntegrity.ReadCheats(_report.Cheats, RunIntegrity.IsCreative());
            for (int i = cheats; i < _report.Cheats.Count; i++) Ctx.Run.Flag("a game cheat is on: " + _report.Cheats[i]);

            // The game's hash is read on a worker thread at startup.
            if (_report.GameHash.Length == 0 && RunIntegrity.GameHash.Length > 0)
            {
                _report.GameHash = RunIntegrity.GameHash;
                _report.TypeHashes.Clear();
                if (RunIntegrity.TypeHashes != null) _report.TypeHashes.AddRange(RunIntegrity.TypeHashes);
                _reportDirty = true;
            }

            int patches = RunIntegrity.PatchedCount();
            if (patches != _patchCount)
            {
                _patchCount = patches;
                int foreign = _report.ForeignPatches.Count;
                RunIntegrity.GatherPatches(_report, OverlayPlugin.PluginGuid);
                if (_report.ForeignPatches.Count > foreign)
                    Ctx.Log.LogWarning("Run mode: another mod patched the game during the attempt: " +
                                       string.Join(", ", _report.ForeignPatches.ToArray()) + ".");
                _reportDirty = true;
            }

            if (_report.Flags.Count != Ctx.Run.Flags.Count)
            {
                _report.Flags.Clear();
                _report.Flags.AddRange(Ctx.Run.Flags);
                _reportDirty = true;
            }
            if (cheats != _report.Cheats.Count) _reportDirty = true;
        }

        private void WriteReport()
        {
            if (_report == null || _reportPath == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_reportPath));
                File.WriteAllText(_reportPath, _report.Format(), new System.Text.UTF8Encoding(false));
            }
            catch (Exception ex) { Ctx.Log.LogWarning("Run mode: report not written: " + ex.Message); }
        }

        /// End run mode: practice unlocks until the next run start.
        public void EndRunMode()
        {
            if (!Ctx.Run.Active) return;
            if (_attemptOpen) EndAttempt("run mode ended by the runner", "run mode ended");
            _byHand = false;
            Ctx.Run.End("ended by you");
        }

        // ------------------------------------------------------------------
        // Text, rebuilt when it changes (never in OnGUI).
        private void RebuildText()
        {
            if (Time.unscaledTime < _nextText) return;
            _nextText = Time.unscaledTime + 0.25f;
            bool confirming = Time.unscaledTime < _confirmUntil;
            string key = Ctx.Run.Active + "|" + Ctx.Run.Attempt + "|" + Ctx.Run.Flags.Count + "|" + Ctx.Run.EndedWhy + "|" +
                         (_report != null ? _report.GameHash.Length + "|" + _report.ForeignPatches.Count + "|" + _report.Cheats.Count : "") + "|" + confirming;
            if (key == _builtFor) return;
            _builtFor = key;

            string state;
            if (Ctx.Run.Active)
                state = "Run mode: ON - attempt " + Ctx.Run.Attempt + (Ctx.Run.Started.Length > 0 ? " (" + Ctx.Run.Started + ")" : "") +
                        ". Practice features are locked until the run ends. During a run the window opens over the pause menu (ESC) only.";
            else
                state = "Run mode: off - Restart on a run spot (a spot with a run category) starts a run. " +
                        "For a category with no run spot: Start run mode, then reset as usual." +
                        (Ctx.Run.EndedWhy.Length > 0 ? " Last run mode ended: " + Ctx.Run.EndedWhy + "." : "");
            if (confirming) state += "\nClick End run mode again to unlock practice (a run in progress stops counting).";
            _stateText.text = state;

            if (_report == null) { _findingsText.text = ""; return; }
            List<string> lines = _report.Findings();
            _findingsText.text = "Attempt " + _report.Attempt + " report:\n" + string.Join("\n", lines.ToArray());
        }

        /// The Runs tab's section; returns the new y.
        public float DrawSection(float y, float w)
        {
            y += UiText.Draw(0, y, w, _stateText);
            y = DrawCategories(y + 2f, w);
            if (Ctx.Run.Active)
            {
                bool confirming = Time.unscaledTime < _confirmUntil;
                if (GUI.Button(new Rect(0, y + 2, 200, 22), confirming ? "Click again to end" : "End run mode"))
                {
                    if (confirming) { _confirmUntil = 0f; EndRunMode(); }
                    else { _confirmUntil = Time.unscaledTime + 4f; _nextText = 0f; }
                }
                y += 28f;
            }
            else
            {
                if (GUI.Button(new Rect(0, y + 2, 200, 22), "Start run mode")) StartByHand();
                y += 28f;
            }
            if (_findingsText.text.Length > 0) y += UiText.Draw(0, y, w, _findingsText) + 4f;
            if (_upload != null) y = _upload.DrawAttempts(y, w);
            return y;
        }
    }
}
