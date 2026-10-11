using System;
using System.Diagnostics;
using System.IO;
using BepInEx.Configuration;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Run mode's anti-splice codes and receipts (docs/run-mode.md, phase 2).
    //
    // Each attempt gets an id (made here, so its link exists offline) and a
    // hash chain (Data/AttemptChain): a step a second - real time, the
    // timer, the player's position - and the four-character code after it,
    // drawn big on screen (also with F5's overlay hidden: a recording needs
    // it). The site's nonce is folded in when it arrives; about once a
    // minute the head goes to the site as a checkpoint; at the end
    // (finished, reset, run mode ended, the game closed) the whole log,
    // with the run report after it, goes to the outbox
    // (Modules/RunUploadModule.Attempts) and to run-reports/ beside the
    // report. While it runs the log is rewritten every 30 s (open/ and
    // run-reports/), so a crash still leaves it.
    // ------------------------------------------------------------------
    public sealed partial class RunModeModule
    {
        private const long StepMs = 1000;
        private const long CheckpointMs = 60000;
        private const long SaveMs = 30000;

        private RunUploadModule _upload;
        private Segment _runSpot;              // the run spot of this run mode; null = by hand

        private AttemptChain _chain;
        private string _attemptId = "";
        private readonly Stopwatch _clock = new Stopwatch();
        private long _nextStepMs, _nextCheckpointMs, _nextSaveMs;
        private int _lastStepN;
        private string _lastStepHead = "";
        private int _flagsFolded;
        private long _finalTimerMs = -1;
        private bool _online;
        private bool _offline;                 // the start code will not come

        /// The running timer in ms, -1 when none (set by the Runs module).
        public Func<long> TimerMs;

        /// Every ms of load the timer ever counted (the Runs module's
        /// LoadClock.Ever): a `load` line's timer ms is its growth over the load.
        public Func<long> TimedLoadMs;

        // The game's loads in real time: a `load` line each (Data/LoadTimes).
        private readonly LoadSpan _loadSpan = new LoadSpan();
        private long _loadTimedBefore;
        private int _loadsLogged;

        // --- the code on screen ---
        private ConfigEntry<float> _codeX, _codeY, _codeSize;
        private readonly GUIContent _codeText = new GUIContent(AttemptChain.NoCode);
        private readonly GUIContent _codeSub = new GUIContent("");
        private int _subAttempt = -1, _subFlags = -1, _subState = -1;
        private MainWindowModule _main;
        private GUIStyle _codeStyle, _subStyle, _boxStyle;
        private int _styleSize;
        private bool _dragging;
        private Vector2 _dragOffset;
        private float _dragX, _dragY;

        private void InitCodes(ModuleContext ctx)
        {
            _codeX = ctx.Config.Bind("RunMode", "CodeX", -1f, "The run code's position from the left, in pixels (-1 = centred).");
            _codeY = ctx.Config.Bind("RunMode", "CodeY", 6f, "The run code's position from the top, in pixels.");
            _codeSize = ctx.Config.Bind("RunMode", "CodeSize", 40f,
                "The run code's text size in pixels. Big enough to read on a 720p video.");
        }

        public string AttemptId { get { return _attemptId; } }

        // --- the chain ----------------------------------------------------------

        private void BeginChain(string label)
        {
            _chain = new AttemptChain();
            _attemptId = AttemptChain.NewAttemptId();
            _online = false;
            _offline = _upload == null || !_upload.AttemptsOn;
            _flagsFolded = 0;
            _finalTimerMs = -1;
            _loadSpan.Reset();
            _loadsLogged = 0;
            _loadTimedBefore = TimedLoadMs != null ? TimedLoadMs() : 0;
            _clock.Reset();
            _clock.Start();
            _nextStepMs = 0;
            _nextCheckpointMs = CheckpointMs;
            _nextSaveMs = SaveMs;

            string runnerId = _upload != null && _upload.RunnerIdNow != null ? _upload.RunnerIdNow() : "";
            string runnerName = _upload != null && _upload.RunnerNameNow != null ? _upload.RunnerNameNow() : "";
            // The site's category id when known (its page shows the version's rules).
            string category = Ctx.Run.Category != null ? Ctx.Run.Category.Id : _runSpot != null ? _runSpot.RunCategory : label;
            string spot = _runSpot != null ? _runSpot.Id : "";
            _chain.Header(_attemptId, runnerId, runnerName, OverlayPlugin.PluginVersion, category, spot,
                          _runSpot != null ? _runSpot.StartState : "", AttemptChain.RandomHex(16), DateTime.UtcNow);
            if (_report != null) { _report.AttemptId = _attemptId; _reportDirty = true; }
            StepNow(0);

            string id = _attemptId;
            if (_upload != null)
                _upload.AttemptStart(id, category, spot, delegate(string nonce) { OnNonce(id, nonce); });
            Ctx.Log.LogInfo("Run mode: attempt " + Ctx.Run.Attempt + " is " + id + " (" +
                            (_offline ? "not sent - sending attempts is off" : "asking the site for its start code") + ").");
        }

        private void OnNonce(string id, string nonce)
        {
            if (_chain == null || id != _attemptId || _chain.Ended) return;
            if (nonce == null) { _offline = true; return; }
            _chain.Nonce(_clock.ElapsedMilliseconds, nonce);
            _online = true;
            Ctx.Log.LogInfo("Run mode: " + id + " got the site's start code after " + _clock.ElapsedMilliseconds + " ms.");
        }

        private void TickChain()
        {
            if (Ctx.Run.Active) RebuildSub();
            if (_chain == null || _chain.Ended) return;
            long ms = _clock.ElapsedMilliseconds;

            for (; _flagsFolded < Ctx.Run.Flags.Count; _flagsFolded++) _chain.Flag(ms, Ctx.Run.Flags[_flagsFolded]);
            TickLoads(ms);

            if (ms >= _nextStepMs) StepNow(ms);

            if (_online && ms >= _nextCheckpointMs && _upload != null)
            {
                _nextCheckpointMs = ms + CheckpointMs;
                _upload.AttemptCheckpoint(_attemptId, _lastStepN, _lastStepHead);
            }
            if (ms >= _nextSaveMs)
            {
                _nextSaveMs = ms + SaveMs;
                SaveOpen();
            }
        }

        // A load's start and end by the game's own state (Game/GameLoading).
        // The timer's load count only grows during a load, so its value on
        // the last frame before one is the value at its start, whichever
        // module ticks first.
        private void TickLoads(long ms)
        {
            LoadSpanInfo span;
            if (_loadSpan.Update(GameLoading.Now, ms, out span)) WriteLoad(span);
            if (!_loadSpan.InLoad && TimedLoadMs != null) _loadTimedBefore = TimedLoadMs();
        }

        private void WriteLoad(LoadSpanInfo span)
        {
            long timed = TimedLoadMs != null ? Math.Max(0L, TimedLoadMs() - _loadTimedBefore) : 0;
            long end = Math.Max(span.EndMs, _chain.LastMs);   // the chain's times never go back
            _chain.Load(end, span.LengthMs, timed);
            _loadsLogged++;
            Ctx.Log.LogInfo("Run mode: load " + _loadsLogged + " of " + _attemptId + ": " + span.LengthMs + " ms real time, " +
                            timed + " ms of it on the timer (load-removed time).");
        }

        private void StepNow(long ms)
        {
            bool hasPos = Ctx.Player.Found && !PlayerRef.AtTitleScreen;
            Vector3 p = hasPos ? Ctx.Player.Transform.position : Vector3.zero;
            long timer = TimerMs != null ? TimerMs() : -1;
            _codeText.text = _chain.Step(ms, timer, hasPos, p.x, p.y, p.z);
            _lastStepN = _chain.Steps;
            _lastStepHead = _chain.Head;
            // A second a step; after a freeze (a load), from now on.
            _nextStepMs += StepMs;
            if (_nextStepMs <= ms) _nextStepMs = ms + StepMs;
        }

        public void TimerSplit(int row, float seconds)
        {
            if (_chain == null || _chain.Ended || !_attemptOpen) return;
            _chain.Split(_clock.ElapsedMilliseconds, row, (long)Math.Round(seconds * 1000.0));
        }

        /// The timed run finished during the attempt: the attempt ends with
        /// it. Returns what the results panel shows of it (null = no attempt
        /// was open).
        public RunModeOutcome TimerFinished(Segment segment, float seconds)
        {
            if (!_attemptOpen || _chain == null) return null;
            _finalTimerMs = (long)Math.Round(seconds * 1000.0);
            _endedOutcome = null;
            EndAttempt("finished" + (segment != null ? " '" + segment.Name + "'" : "") + " in " + seconds.ToString("0.00") + " s", "finished");
            return _endedOutcome;
        }

        private RunModeOutcome _endedOutcome;

        // The attempt as it ended: its last code, online or not, the report.
        private RunModeOutcome Outcome()
        {
            RunModeOutcome o = new RunModeOutcome();
            o.Attempt = Ctx.Run.Attempt;
            o.Label = Ctx.Run.Label ?? "";
            o.AttemptId = _attemptId;
            o.Code = _chain.Code ?? "";
            o.Online = _online;
            o.SendOn = _upload != null && _upload.AttemptsOn;
            o.AntiSplice = Ctx.Run.Category == null || Ctx.Run.Category.AntiSplice;
            o.Report = _report != null ? _report.Summary() : "";
            return o;
        }

        private void EndChain(string reason)
        {
            if (_chain == null || _chain.Ended) return;
            long ms = _clock.ElapsedMilliseconds;
            for (; _flagsFolded < Ctx.Run.Flags.Count; _flagsFolded++) _chain.Flag(ms, Ctx.Run.Flags[_flagsFolded]);
            LoadSpanInfo cut;
            if (_loadSpan.Finish(ms, out cut)) WriteLoad(cut);   // ended during a load: up to now
            StepNow(ms);   // the last position, and a code for the end
            long timer = _finalTimerMs >= 0 ? _finalTimerMs : (TimerMs != null ? TimerMs() : -1);
            _chain.End(_clock.ElapsedMilliseconds, reason, timer);
            _clock.Stop();

            string text = FullLog();
            WriteLocal(text);
            if (_upload != null)
            {
                _upload.ClearOpenAttempt(_attemptId);
                if (_upload.AttemptsOn) _upload.QueueAttemptLog(_attemptId, text);
            }
            Ctx.Log.LogInfo("Run mode: " + _attemptId + " ended (" + reason + ") after " + _chain.Steps + " step(s), " +
                            (_online ? "online" : "offline") + "; last code " + _chain.Code + ".");
            _endedOutcome = Outcome();
            _codeText.text = AttemptChain.NoCode;
        }

        private string FullLog()
        {
            return _chain.Text + AttemptChain.ReportMarker + "\n" + (_report != null ? _report.Format() : "");
        }

        private void SaveOpen()
        {
            string text = FullLog();
            WriteLocal(text);
            if (_upload != null && _upload.AttemptsOn) _upload.WriteOpenAttempt(_attemptId, text);
        }

        private void WriteLocal(string text)
        {
            if (_reportPath == null) return;
            try { File.WriteAllText(Path.ChangeExtension(_reportPath, ".log"), text, new System.Text.UTF8Encoding(false)); }
            catch (Exception ex) { Ctx.Log.LogWarning("Run mode: attempt log not written: " + ex.Message); }
        }

        public override void Shutdown()
        {
            MoveWatch.Uninstall();
            AuditWatch.Uninstall();
            if (!_attemptOpen) return;
            try { EndAttempt("the game closed", "game closed"); }
            catch (Exception ex) { Ctx.Log.LogWarning("Run mode: attempt not closed on shutdown: " + ex.Message); }
        }

        // --- the code on screen ------------------------------------------------------

        // Without allocating unless it changed (called every frame in run mode).
        private void RebuildSub()
        {
            int bits = (_attemptOpen ? 1 : 0) | (_online ? 2 : 0) | (_offline ? 4 : 0) | (_byHand ? 8 : 0);
            if (bits == _subState && Ctx.Run.Attempt == _subAttempt && Ctx.Run.Flags.Count == _subFlags) return;
            _subState = bits;
            _subAttempt = Ctx.Run.Attempt;
            _subFlags = Ctx.Run.Flags.Count;
            if (!_attemptOpen) { _codeSub.text = _byHand ? "RUN MODE - next attempt: load a game" : "RUN MODE - next attempt: F7 on the run spot"; return; }
            string state = Ctx.Run.Flags.Count > 0 ? "NOT VALID"
                         : _online ? "online"
                         : _offline ? "offline - video codes only"
                         : "connecting...";
            _codeSub.text = "RUN " + Ctx.Run.Attempt + " - " + state;
        }

        private void EnsureCodeStyles()
        {
            int size = Mathf.Clamp(Mathf.RoundToInt(_codeSize.Value), 16, 120);
            if (_codeStyle != null && _styleSize == size) return;
            _styleSize = size;
            if (_codeStyle == null)
            {
                // Built once (UiKit.Style keeps every style it makes); a size change only resizes.
                _codeStyle = UiKit.Style(GUI.skin.label);
                _codeStyle.fontStyle = FontStyle.Bold;
                _codeStyle.alignment = TextAnchor.MiddleCenter;
                _codeStyle.normal.textColor = Color.white;
                _subStyle = UiKit.Style(GUI.skin.label);
                _subStyle.alignment = TextAnchor.MiddleCenter;
                _subStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            }
            _codeStyle.fontSize = size;
            _subStyle.fontSize = Mathf.Max(11, size / 3);
            if (_boxStyle == null)
            {
                _boxStyle = UiKit.Style(UiKit.WidgetCard);   // the redesign's card
            }
        }

        public override void DrawScreenAlways()
        {
            if (!Ctx.Run.Active) return;
            // A category without the anti-splice codes: none on screen
            // (author, 2026-10-02: optional per category). The chain still runs.
            if (Ctx.Run.Category != null && !Ctx.Run.Category.AntiSplice) return;
            EnsureCodeStyles();
            float size = _styleSize;
            float w = Mathf.Max(size * 3.4f + 24f, _subStyle.CalcSize(_codeSub).x + 16f);
            float h = size * 1.25f + _subStyle.fontSize * 1.6f + 8f;
            float px = _dragging ? _dragX : _codeX.Value;
            float py = _dragging ? _dragY : _codeY.Value;
            float x = px < 0f && !_dragging ? (Screen.width - w) * 0.5f : Mathf.Clamp(px, 0f, Mathf.Max(0f, Screen.width - w));
            float y = Mathf.Clamp(py, 0f, Mathf.Max(0f, Screen.height - h));
            Rect box = new Rect(x, y, w, h);
            HandleCodeDrag(box, w);

            GUI.Box(box, GUIContent.none, _boxStyle);
            if (_dragging) GUI.Box(box, GUIContent.none, UiKit.Outline);
            GUI.Label(new Rect(x, y + 2f, w, size * 1.25f), _codeText, _codeStyle);
            GUI.Label(new Rect(x, y + size * 1.25f + 2f, w, _subStyle.fontSize * 1.6f), _codeSub, _subStyle);
        }

        // Moved like the splits panel: drag it while the F2 window is open.
        private void HandleCodeDrag(Rect box, float w)
        {
            Event e = Event.current;
            if (e == null) return;
            if (_main == null) _main = Host.Find<MainWindowModule>();
            MainWindowModule main = _main;
            if (main == null || !main.PanelOpen)
            {
                if (_dragging) EndCodeDrag(w);
                return;
            }
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button != 0 || !box.Contains(e.mousePosition) || main.ScreenRect.Contains(e.mousePosition)) return;
                    _dragging = true;
                    _dragOffset = e.mousePosition - new Vector2(box.x, box.y);
                    _dragX = box.x;
                    _dragY = box.y;
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (!_dragging) return;
                    _dragX = Mathf.Max(0f, e.mousePosition.x - _dragOffset.x);
                    _dragY = Mathf.Max(0f, e.mousePosition.y - _dragOffset.y);
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (!_dragging) return;
                    EndCodeDrag(w);
                    e.Use();
                    break;
            }
        }

        private void EndCodeDrag(float w)
        {
            _dragging = false;
            _codeX.Value = Mathf.Clamp(_dragX, 0f, Mathf.Max(0f, Screen.width - w));
            _codeY.Value = Mathf.Max(0f, _dragY);
            Ctx.Log.LogInfo("Run code moved to (" + Mathf.RoundToInt(_codeX.Value) + ", " + Mathf.RoundToInt(_codeY.Value) + ").");
        }
    }
}
