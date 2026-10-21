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
        private SavestateModule _savestates;
        private bool _runSpeedFlagged, _runSpeedWarned;   // the run / swim speed flag is once per attempt; the missing-field warning once
        private readonly TimeScaleWatch _timeScale = new TimeScaleWatch();   // the time scale episodes of this attempt
        private readonly ClockWatch _clockWatch = new ClockWatch();   // game time ahead of real time (T-0414 F1)
        private bool _captureFlagged, _moveFlagged, _moveWarned;   // capture frame rate / movement constants: once per attempt
        private FieldInfo _finishLoad;      // Scene.FinishGameLoad (static)
        private bool _loaded;               // last frame: in a loaded game
        private bool _attemptOpen;          // an attempt is running (not reset yet)
        private bool _spotStarting;         // a run spot's Full load is under way
        private bool _byHand;               // run mode started with Start run mode
        private bool _byHandClick;          // the attempt being started is the Start run mode click's (T-0414 D3)
        private string _startHash = "";     // the start state the run spot really loaded (T-0414 F4)
        private RunReport _report;
        private string _reportPath;
        private float _nextCheck;
        private ModCounts _modCounts = new ModCounts();   // when to gather the mods again (T-0414)
        private bool _reportDirty;

        private float _confirmUntil;        // End run mode: the second click's window
        private readonly GUIContent _stateText = new GUIContent("");
        private readonly GUIContent _findingsText = new GUIContent("");
        // What the text was last built for, field by field (no key string:
        // see _catBuilt in RunModeModule.Categories).
        private bool _built;
        private bool _builtActive, _builtConfirming, _builtReport;
        private int _builtAttempt, _builtFlags, _builtHash, _builtForeign, _builtCheats, _builtMoves;
        private string _builtEnded, _builtWatch;
        private float _nextText;

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);
            _bridge = Host.Find<BridgeModule>();
            _death = Host.Find<DeathModule>();
            _savestates = Host.Find<SavestateModule>();
            _upload = Host.Find<RunUploadModule>();
            InitCodes(ctx);
            InitCategories(ctx);
            InitMoves(ctx);
            InitAudit(ctx);
            Type scene = GameBridge.FindGameType("TheForest.Utils.Scene");
            if (scene != null) _finishLoad = scene.GetField("FinishGameLoad", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            _finishLoadGet = FastField.Static<bool>(_finishLoad);
            RunIntegrity.StartHashing(ctx.Log);
            RebuildText();
        }

        private Func<bool> _finishLoadGet;   // once a frame: no boxing (FastField)

        private bool InLoadedGame()
        {
            if (!Ctx.Player.Found || PlayerRef.AtTitleScreen) return false;
            if (_finishLoadGet == null) return true;
            try { return _finishLoadGet(); }
            catch (Exception) { return true; }
        }

        public override void Tick()
        {
            bool loaded = InLoadedGame();
            TickMoves(loaded);
            if (loaded && !_loaded) OnGameLoaded();
            bool reloading = _death != null && _death.ReloadPending;
            if (PlayerRef.AtTitleScreen && _attemptOpen && !reloading) EndAttempt("back to the title screen (a reset)", "title screen");
            TickAudit(loaded);
            _loaded = loaded;

            // Only while the attempt plays: a save loading after a reset is
            // not the attempt (v0.24.208 flagged Slot 1's Creative cheats).
            if (Ctx.Run.Active && _attemptOpen && _report != null && loaded && Time.unscaledTime >= _nextCheck)
            {
                _nextCheck = Time.unscaledTime + 1f;
                Watch();
            }
            // Every frame, not in Watch(): a menu close sets 1 again, so a
            // short cheat would slip between the once-a-second checks. Not
            // while the plugin drives timeScale itself (savestate action,
            // cutscene fast-forward).
            if (Ctx.Run.Active && _attemptOpen)
            {
                bool excused = !loaded || (_savestates != null && (_savestates.Busy || _savestates.FastForwarding));
                string speedEnd;
                float scale = Time.timeScale;
                bool zeroOk = scale != 0f || MenuClose.AnyOpen();   // a 0 is vanilla in the pause menu / inventory (reflection only when 0)
                string speed = _timeScale.Frame(scale, excused, zeroOk, Time.unscaledDeltaTime, out speedEnd);
                if (speed != null) Ctx.Run.Flag(speed);
                if (speedEnd != null) Ctx.Log.LogInfo("Run mode: attempt " + Ctx.Run.Attempt + " - " + speedEnd);
                // The game clock against real time (T-0414 F1): only while the
                // time scale reads 1 or less, and not the frame after one
                // that did not (ClockWatch) - a plain 2 is the time-scale
                // watch's one flag, not two.
                string clockEnd;
                string clock = _clockWatch.Frame(Time.deltaTime, Time.unscaledDeltaTime, scale, excused, out clockEnd);
                if (clock != null) Ctx.Run.Flag(clock);
                if (clockEnd != null) Ctx.Log.LogInfo("Run mode: attempt " + Ctx.Run.Attempt + " - " + clockEnd);
                // Time.captureFramerate has no game writer (F2).
                if (!_captureFlagged)
                {
                    string cap = SpeedCheck.CaptureRateFlag(Time.captureFramerate);
                    if (cap != null) { _captureFlagged = true; Ctx.Run.Flag(cap); }
                }
                if (!excused && !_moveFlagged) CheckMovement();
                // The player's run / swim speed (_speedyrun writes 60 / 20): first time per attempt.
                if (!excused && !_runSpeedFlagged)
                {
                    float run, swim;
                    if (SpeedRead.TryRead(out run, out swim))
                    {
                        string rs = SpeedCheck.RunSpeedFlag(run, swim);
                        if (rs != null) { _runSpeedFlagged = true; Ctx.Run.Flag(rs); }
                    }
                    else if (SpeedRead.Missing && !_runSpeedWarned)
                    {
                        _runSpeedWarned = true;
                        Ctx.Log.LogWarning("Run mode: the player's run / swim speed fields were not found - that watch is off.");
                    }
                }
            }
            if (_reportDirty) { _reportDirty = false; WriteReport(); }
            TickChain();
            TickCategories();
            RebuildText();
            RebuildCategoryText();
        }

        // The player's movement constants and the physics settings (T-0414
        // F3, D1): first time per attempt.
        private void CheckMovement()
        {
            float walk, strafe, crouch, jump, gravity;
            if (SpeedRead.TryReadMovement(out walk, out strafe, out crouch, out jump, out gravity))
            {
                Vector3 g = Physics.gravity;
                string mv = SpeedCheck.MovementFlag(walk, strafe, crouch, jump, gravity, g.x, g.y, g.z,
                                                    Time.fixedDeltaTime, _report != null && _report.Multiplayer);   // read at the start: no reflection a frame
                if (mv != null) { _moveFlagged = true; Ctx.Run.Flag(mv); }
            }
            else if (SpeedRead.MovementMissing && !_moveWarned)
            {
                _moveWarned = true;
                Ctx.Log.LogWarning("Run mode: the player's movement fields were not found - that watch is off.");
            }
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
                    AuditReloaded();
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
            // The state the spot really loaded, not the one it names (T-0414 F4).
            _startHash = _savestates != null ? _savestates.LastStartStateHash : "";
            string from = s.RunCategory + " from '" + s.Name + "' (" +
                          (SegmentLibrary.IsCommunity(s) ? "community" : "own") + " spot " + s.Id +
                          ", start state " + (_startHash.Length > 0 ? _startHash : "not hashed") + ")";
            RunCategory cat = CategoryFor(s);
            if (cat == null) Ctx.Log.LogWarning("Run mode: the run spot's category '" + s.RunCategory + "' is not one of the site's " +
                                                _categories.Count + " - the defaults apply (everything locked).");
            StartAttempt(from + ", " + RunIntegrity.Describe(), cat != null ? cat.Name : s.RunCategory);
            string mismatch = RunStartCheck.StartStateFlag(_startHash, _savestates != null ? _savestates.LastStartStateLegacy : "", s.StartState);
            if (mismatch != null) Ctx.Run.Flag(mismatch);
        }

        /// The fallback for a category with no run spot: run mode on now,
        /// the attempt starting in the game as it is.
        public void StartByHand()
        {
            if (Ctx.Run.Active) return;
            if (!InLoadedGame()) { Ctx.Notice.Show("Run mode: load a game first.", 6f); return; }
            _byHand = true;
            _byHandClick = true;   // practice before this click is a flag (D3)
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
            if (_runSpot == null) _startHash = "";
            // A run by hand says on the HUD that it did not start from a
            // category spot (author, 2026-10-21, T-0414 D3); the chain keeps the plain label.
            Ctx.Run.Begin(started, _byHand ? RunStartCheck.ByHandLabel(label) : label, cat);
            bool click = _byHandClick;
            _byHandClick = false;
            // Practice, then Start run mode: a savestate just before could
            // start the run anywhere - a flag, not a note (D3).
            string afterPractice = click ? RunStartCheck.ByHandAfterPracticeFlag(before) : null;
            if (afterPractice != null) Ctx.Run.Flag(afterPractice);
            ResetMoves();
            ResetAudit();
            _timeScale.Reset();
            _clockWatch.Reset();
            _runSpeedFlagged = false;
            _captureFlagged = false;
            _moveFlagged = false;
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
            _modCounts = new ModCounts();
            _modCounts.Due(RunIntegrity.PatchedCount(), RunIntegrity.DetourCount(), RunIntegrity.AssemblySignature(), false);
            _reportPath = Path.Combine(Path.Combine(Ctx.ConfigDirectory, "run-reports"),
                                       "attempt-" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
            Watch();
            WriteReport();
            BeginChain(label);

            Ctx.Log.LogInfo("Run mode: attempt " + _report.Attempt + " started (" + _report.Started + ") - practice locked; " +
                            _report.Summary() + ".");
            if (!Host.AnyPanelOpen())
                Ctx.Notice.Show("Run mode: attempt " + _report.Attempt + " (" + Ctx.Run.Label + ") - practice features are locked. End run mode in the Runs tab to practise.", 7f);
        }

        /// `reason`: the log's short word (reset, finished, title screen, ...).
        private void EndAttempt(string why, string reason)
        {
            FlushMoves();   // a boost still being measured goes into this attempt's log
            FlushAudit();   // items / bursts still being merged, likewise
            string speedLeft = _timeScale.End();   // a speed change still running when the attempt ends
            if (speedLeft != null) Ctx.Log.LogInfo("Run mode: attempt " + Ctx.Run.Attempt + " - " + speedLeft);
            string clockLeft = _clockWatch.End();
            if (clockLeft != null) Ctx.Log.LogInfo("Run mode: attempt " + Ctx.Run.Attempt + " - " + clockLeft);
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
                _report.GameFileHash = RunIntegrity.GameFileHash;
                _report.GameFrom = RunIntegrity.GameFrom;
                _report.TypeHashes.Clear();
                if (RunIntegrity.TypeHashes != null) _report.TypeHashes.AddRange(RunIntegrity.TypeHashes);
                _reportDirty = true;
            }

            // Patches, MonoMod detours (T-0414 F6) and loaded code: the
            // report's lists themselves follow, not only the session's flag
            // (live, v0.24.398: OtherCode stayed empty mid-attempt).
            if (_modCounts.Due(RunIntegrity.PatchedCount(), RunIntegrity.DetourCount(), RunIntegrity.AssemblySignature(), false))
            {
                List<string> patchesBefore = new List<string>(_report.ForeignPatches);
                List<string> codeBefore = new List<string>(_report.OtherCode);
                codeBefore.AddRange(_report.OtherPlugins);
                codeBefore.AddRange(_report.OtherPatchers);
                RunIntegrity.GatherMods(_report, OverlayPlugin.PluginGuid, Ctx.PluginPath);
                if (ExternalMods.Added(patchesBefore, _report.ForeignPatches).Count > 0)
                    Ctx.Log.LogWarning("Run mode: another mod patched the game during the attempt: " +
                                       string.Join(", ", _report.ForeignPatches.ToArray()) + ".");
                List<string> codeNow = new List<string>(_report.OtherCode);
                codeNow.AddRange(_report.OtherPlugins);
                codeNow.AddRange(_report.OtherPatchers);
                // A new entry, or one that went from its file to memory (T-0414).
                string codeChange = ExternalMods.ChangeText(codeBefore, codeNow);
                if (codeChange != null) Ctx.Log.LogWarning("Run mode: " + codeChange + ".");
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
            bool active = Ctx.Run.Active, hasReport = _report != null;
            int attempt = Ctx.Run.Attempt, flags = Ctx.Run.Flags.Count, moveCount = _attemptMoves.Count;
            int hash = hasReport ? _report.GameHash.Length : 0;
            int foreign = hasReport ? _report.ForeignPatches.Count : 0;
            int cheats = hasReport ? _report.Cheats.Count : 0;
            string ended = Ctx.Run.EndedWhy, watch = MoveWatch.Status;
            if (_built && active == _builtActive && attempt == _builtAttempt && flags == _builtFlags &&
                string.Equals(ended, _builtEnded) && hasReport == _builtReport && hash == _builtHash &&
                foreign == _builtForeign && cheats == _builtCheats && confirming == _builtConfirming &&
                moveCount == _builtMoves && string.Equals(watch, _builtWatch)) return;
            _built = true;
            _builtActive = active; _builtAttempt = attempt; _builtFlags = flags; _builtEnded = ended;
            _builtReport = hasReport; _builtHash = hash; _builtForeign = foreign; _builtCheats = cheats;
            _builtConfirming = confirming; _builtMoves = moveCount; _builtWatch = watch;

            string state;
            if (Ctx.Run.Active)
                state = "Run mode: ON - attempt " + Ctx.Run.Attempt + (Ctx.Run.Started.Length > 0 ? " (" + Ctx.Run.Started + ")" : "");
            else
                state = "Run mode: off" + (Ctx.Run.EndedWhy.Length > 0 ? " (ended: " + Ctx.Run.EndedWhy + ")" : "");
            if (confirming) state += "\nClick End run mode again to unlock practice (a run in progress stops counting).";
            _stateText.text = state;

            string moves = MovesText();
            if (_report == null) { _findingsText.text = moves; return; }
            List<string> lines = _report.Findings();
            _findingsText.text = "Attempt " + _report.Attempt + " report:\n" + string.Join("\n", lines.ToArray()) +
                                 (moves.Length > 0 ? "\n" + moves : "");
        }

        // The explanations live in tooltips (author, 2026-10-05: name + toggle, the rest on hover).
        private static readonly GUIContent TipOff = new GUIContent(
            "Restart on a run spot (a spot with a run category) starts a run. For a category with no run spot: Start run mode, then reset as usual.");
        private static readonly GUIContent TipOn = new GUIContent(
            "Practice features are locked until the run ends. During a run the window opens over the pause menu (ESC) only.");

        /// The Runs tab's section; returns the new y.
        public float DrawSection(float y, float w)
        {
            float h = UiText.Draw(0, y, w, _stateText);
            UiKit.Hint(new Rect(0, y, w, h), Ctx.Run.Active ? TipOn : TipOff);
            y += h;
            y = DrawCategories(y + 2f, w);
            if (Ctx.Run.Active)
            {
                bool confirming = Time.unscaledTime < _confirmUntil;
                if (WinPx.Button(new Rect(0, y + 2, 200, 22), confirming ? "Click again to end" : "End run mode"))
                {
                    if (confirming) { _confirmUntil = 0f; EndRunMode(); }
                    else { _confirmUntil = Time.unscaledTime + 4f; _nextText = 0f; }
                }
                y += 28f;
            }
            else
            {
                if (WinPx.Button(new Rect(0, y + 2, 200, 22), "Start run mode")) StartByHand();
                y += 28f;
            }
            if (_findingsText.text.Length > 0) y += UiText.Draw(0, y, w, _findingsText) + 4f;
            y = DrawAudit(y, w);
            if (_upload != null) y = _upload.DrawAttempts(y, w);
            return y;
        }
    }
}
