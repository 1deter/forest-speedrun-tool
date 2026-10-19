using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Recordings (T-0344; QA ask, author 2026-10-19: all three, before v1;
    // build plan tasks/notes/T-0344.md): Record / Done records any stretch
    // of play - no spot, no timed segment, practice mode on or off - like a
    // run: path and speed (30 Hz), the look, the arms, the player's state
    // (5 Hz), item changes, the interactions (the replay markers), the
    // game's actions held (Data/InputTracks, Game/ActionRecorder) and the
    // backpack's 1-4 hotkey items. A second RunRecorder with the same
    // readers; the spot recorder gets the inputs and the quick slots too.
    //
    // Saved by AttemptStore under `runs/recordings/` (never uploaded, never
    // a run mode attempt), listed in the Runs tab to watch (the replay
    // camera: chase / first person / trajectory / free, the bar) or remove.
    // Refused while a timed run is running. Practice labelled
    // (Ctx.Practice.Mark), gated only as the replay camera is (rule 12).
    //
    // The bar's values (Data/ReplayValues) are the runner's pick for every
    // replay: `[Runs] ReplayValues`, default Basic (the site's list + speed
    // + quick slots), set in Ghost and replay.
    // ------------------------------------------------------------------
    public sealed partial class PracticeRunModule
    {
        private const string RecordingsAnchor = "recordings";
        private const float ChoiceW = 160f, ChoiceH = 20f, RecRowH = 24f;

        private readonly RunRecorder _take = new RunRecorder();
        private ItemCounter _takeItems;
        private readonly List<Attempt> _recs = new List<Attempt>();
        private readonly List<string> _recFiles = new List<string>();
        private readonly List<GUIContent> _recRows = new List<GUIContent>();
        private Attempt _watchRun;      // the recording the replay camera plays
        private bool _watchTake;        // the replay camera plays _watchRun (only while _camOn)
        private bool _camAskedTake;     // the camera's messages go under the recordings' buttons
        private int _takeEventsSeen;
        private int _takeShownState = -1;
        private float _nextTakeText;
        private int _removeArmed = -1;
        private float _removeArmedUntil;

        private readonly GUIContent _takeStatus = new GUIContent("");
        private readonly GUIContent _takeInfo = new GUIContent("");
        private readonly GUIContent _recorderSummary = new GUIContent("");
        private float _tabViewH = 400f;

        // The bar's lookups, made once (no delegate per refresh).
        private Func<int, string> _itemNameFn;
        private Func<string, string> _keyLabelFn;
        private Func<int, int, bool> _replayHeldFn;
        private readonly Dictionary<int, string> _itemNames = new Dictionary<int, string>();
        private int[] _replayEndOf = new int[0];   // (action * 2 + negative) -> the watched run's end, -1 none

        // The value chooser.
        private ConfigEntry<string> _valuesCfg;
        private List<string> _valuesChosen = new List<string>();
        private int _valuesVersion;
        private List<string> _choices = new List<string>(ReplayValues.Basic);
        private string[] _choicesFor;
        private bool[] _choiceOn = new bool[0];
        private GUIContent[] _choiceText = new GUIContent[0];
        private bool _valuesAllOpen;
        private readonly GUIContent _valuesMoreText = new GUIContent("");

        private static readonly GUIContent TextRecordings = new GUIContent("Recordings");
        private static readonly GUIContent TipRecordings = new GUIContent("Record any stretch of play, no spot needed, and watch it back with the replay camera.");
        private static readonly GUIContent TextRecord = new GUIContent("Record");
        private static readonly GUIContent RecordingsHelp = new GUIContent(
            "Record anything you play - no spot or timed segment needed: Record, play, Done. Watch a recording back with the replay camera " +
            "(chase, first person, trajectory or free - fly around it with W A S D). The bar under it shows the keys you pressed and the values " +
            "picked in Ghost and replay.");
        private static readonly GUIContent ValuesHeading = new GUIContent(
            "Replay bar values - what the bar under any replay shows at each moment (Basic = the website's list, the speed and the 1-4 hotkey items):");

        private GUIContent RecorderSummary { get { return _recorderSummary.text.Length > 0 ? _recorderSummary : null; } }

        /// The run the replay camera plays: a recording while one is watched, else the comparison run.
        private Attempt Watched { get { return _watchTake ? _watchRun : _reference; } }

        /// The replay camera plays the comparison run (its splits drive the panel).
        private bool WatchingComparison { get { return _camOn && !_watchTake && _reference != null; } }

        /// A recording is being made (the bridge reads this).
        public bool Recording { get { return _take.State == RunRecorder.RunState.Running; } }

        private void InitRecorder(ModuleContext ctx)
        {
            _take.StateSource = ReadState;
            _take.LookSource = ReadLook;
            _take.AnimSource = ReadAnim;
            _takeItems = new ItemCounter(ctx.Log, ctx.Inventory);   // its own: Fill keeps what it last saw
            _take.ItemSource = _takeItems.Fill;
            // The inputs and the quick slots, for recordings and spot runs alike.
            Func<string[]> names = ReadInputNames;
            Func<bool[], bool> held = ActionRecorder.Read;
            Func<int[], bool> slots = ctx.Inventory.QuickSlotIds;
            _take.InputNames = names; _take.InputSource = held; _take.QuickSlotSource = slots;
            _recorder.InputNames = names; _recorder.InputSource = held; _recorder.QuickSlotSource = slots;

            _itemNameFn = ItemName;
            _keyLabelFn = KeyLabel;
            _replayHeldFn = ReplayHeld;

            _valuesCfg = ctx.Config.Bind("Runs", "ReplayValues", ReplayValues.BasicWord,
                "The values the replay bar shows at each moment: Basic (the website's list, the speed and the 1-4 hotkey items), " +
                "none, or a comma list of Speed, Quick slots and the player's state channels. Set in the Runs tab, Ghost and replay.");
            _valuesChosen = ReplayValues.Parse(_valuesCfg.Value);
            LoadRecordings();
            RefreshChoices();   // the tab may draw before the first Tick
        }

        private string[] ReadInputNames() { return ActionRecorder.Names(Ctx.Log); }

        private void ShutdownRecorder()
        {
            GameActions.ReplayHeld = null;
            if (_take.State == RunRecorder.RunState.Running) FinishTake("the game closed");
        }

        // --- record / done ---------------------------------------------------------

        /// Record, or Done while recording (the tab's button, `rec.toggle`).
        public void ToggleRecording()
        {
            if (_take.State == RunRecorder.RunState.Running) { FinishTake("Done"); return; }
            if (_camOn) { SetTakeStatus("The replay camera is on - stop watching first."); return; }
            if (_recorder.State == RunRecorder.RunState.Running) { SetTakeStatus("A timed run is going - finish or abort it first."); return; }
            if (!Ctx.Player.Found) { SetTakeStatus("No player yet - load a game first."); return; }

            Ctx.PlayerState.Resolve();
            _take.StateChannels = Ctx.PlayerState.Channels;
            _take.ChannelSource = delegate { return Ctx.PlayerState.Channels; };
            _take.Route = "";
            if (_recorder.State != RunRecorder.RunState.Running) BuildWatch.Reset();
            _takeWatch.Sync();
            _takeEventsSeen = Ctx.Events.Count;
            Vector3 pos = PlayerPosition();
            _take.Arm(pos, RecordingsAnchor);
            _take.ForceStart(pos);
            Ctx.Practice.Mark("recording");
            SetTakeStatus("Recording - press Done (Runs tab, Recordings) to stop.");
            Ctx.Log.LogInfo("Recording: started at (" + pos.x.ToString("0.0") + ", " + pos.y.ToString("0.0") + ", " + pos.z.ToString("0.0") + ").");
        }

        private void FinishTake(string why)
        {
            Attempt a = _take.Finish();
            if (a == null || a.Samples.Count < 2)
            {
                SetTakeStatus("That recording was too short to watch - nothing kept.");
                Ctx.Log.LogInfo("Recording: stopped (" + why + ") - too short, nothing kept.");
                return;
            }
            a.AnchorLabel = RecordingsAnchor;
            bool saved = _store.Save(a);
            _recs.Add(a);
            _recFiles.Add(_store.FileFor(a));
            RebuildRecRows();
            int keys = a.Keys != null ? a.Keys.Changes.Count : 0;
            SetTakeStatus("Recorded " + Format(a.Duration) + (why == "Done" ? "" : " (stopped: " + why + ")") +
                          (saved ? " - Watch it in the list." : " - it could not be saved (see the log); watch it before you quit."));
            Ctx.Log.LogInfo("Recording: done (" + why + ") - " + a.Duration.ToString("0.00") + " s, " + a.Samples.Count + " position(s), " +
                            a.States.Count + " state sample(s), " + a.Events.Count + " interaction(s), " + keys + " key change(s), " +
                            (a.QuickSlots != null ? a.QuickSlots.Samples.Count : 0) + " quick-slot change(s), " + (saved ? "saved" : "NOT saved") + ".");
            _takeShownState = -1;
        }

        /// Watch recording `i` (the list's button), or stop watching it.
        private void WatchRecording(int i)
        {
            if (_camOn && _watchTake && ReferenceEquals(_watchRun, _recs[i])) { EndReplayCamera("turned off"); return; }
            if (_camOn) EndReplayCamera("another run picked");
            _watchRun = _recs[i];
            StartReplayCamera(true);
            if (_camOn) Ctx.Log.LogInfo("Recording: watching the one made " + _watchRun.RecordedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + ".");
            else _watchRun = null;
        }

        private void RemoveRecording(int i)
        {
            if (_removeArmed != i || Time.unscaledTime > _removeArmedUntil)
            {
                _removeArmed = i;
                _removeArmedUntil = Time.unscaledTime + 3f;
                SetTakeStatus("Click Sure? within 3 s to remove that recording (no undo).");
                return;
            }
            _removeArmed = -1;
            Attempt a = _recs[i];
            if (_camOn && _watchTake && ReferenceEquals(_watchRun, a)) EndReplayCamera("its recording was removed");
            bool ok = _store.DeleteFile(_recFiles[i]);
            _recs.RemoveAt(i);
            _recFiles.RemoveAt(i);
            RebuildRecRows();
            SetTakeStatus(ok ? "Recording removed." : "The file could not be removed (see the log); it is gone from the list until the next launch.");
            Ctx.Log.LogInfo("Recording: removed the one made " + a.RecordedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + (ok ? "." : " - its file could not be deleted."));
            _takeShownState = -1;
        }

        /// Every frame, before Tick's early returns.
        private void TickTake()
        {
            RefreshChoices();
            RefreshTakeText();
            if (_take.State != RunRecorder.RunState.Running) return;
            // No player (a load, the menu): the clock holds until there is one again.
            if (!Ctx.Player.Found) { _takeEventsSeen = Ctx.Events.Count; return; }

            Vector3 pos = Ctx.Player.Transform.position;
            int events = Ctx.Events.Count;
            if (_takeEventsSeen > events) _takeEventsSeen = events;
            while (_takeEventsSeen < events) RecordReplayEvent(_take, _takeEventsSeen++, pos);

            Ctx.PlayerState.Resolve();
            _take.LoadingNow = GameLoading.Now;
            _take.Tick(pos, Ctx.Player.HorizontalSpeed, Time.unscaledDeltaTime, null);
        }

        private void SetTakeStatus(string text)
        {
            if (text != _takeStatus.text) _takeStatus.text = text;
            if (!TabShowing && text.Length > 0 && !_camOn) Ctx.Notice.Show(text, 4f);
        }

        /// The HUD's Recording value (a string a refresh while recording).
        private string TakeClock()
        {
            return ClockText.Clock(_take.Elapsed);
        }

        // The section's summary and the line under the buttons: rebuilt when they change.
        private void RefreshTakeText()
        {
            bool rec = _take.State == RunRecorder.RunState.Running;
            int state = rec ? -2 : _recs.Count;
            if (state == _takeShownState && !(rec && Time.unscaledTime >= _nextTakeText)) return;
            _takeShownState = state;
            _nextTakeText = Time.unscaledTime + 0.5f;
            if (rec)
            {
                _recorderSummary.text = "recording";
                _takeInfo.text = "Recording: " + ClockText.Clock(_take.Elapsed);
                return;
            }
            _recorderSummary.text = _recs.Count > 0 ? _recs.Count.ToString() : "";
            _takeInfo.text = _recs.Count == 0 ? "No recordings yet." : "";
        }

        // --- the files -------------------------------------------------------------

        private void LoadRecordings()
        {
            _recs.Clear();
            _recs.AddRange(_store.LoadAll(RecordingsAnchor, _recFiles));
            RebuildRecRows();
            if (_recs.Count > 0) Ctx.Log.LogInfo("Recording: " + _recs.Count + " recording(s) on file.");
        }

        private void RebuildRecRows()
        {
            _recRows.Clear();
            for (int i = 0; i < _recs.Count; i++)
                _recRows.Add(new GUIContent(_recs[i].RecordedUtc.ToLocalTime().ToString("d MMM HH:mm:ss") + "   " + Format(_recs[i].Duration)));
        }

        // --- the bar's lookups -----------------------------------------------------

        private string ItemName(int id)
        {
            string n;
            if (_itemNames.TryGetValue(id, out n)) return n;
            n = Ctx.Inventory.NameForId(id);
            if (!string.IsNullOrEmpty(n) && Ctx.Inventory.CatalogReady) _itemNames[id] = n;   // keep once the database is read
            return n;
        }

        private static string KeyLabel(string end)
        {
            int sign;
            string action = KeyTrack.ActionOf(end, out sign);
            return InputActionTable.Label(action, sign);
        }

        /// The replay camera starts on `run`: the input overlay plays its keys
        /// (GameActions.ReplayHeld), when it has them and the actions are read.
        private void StartReplayKeys(Attempt run)
        {
            GameActions.ReplayHeld = null;
            InputActionTable table = GameActions.Table;
            if (run == null || run.Keys == null || table == null) return;
            int n = table.ActionCount * 2;
            if (_replayEndOf.Length != n) _replayEndOf = new int[n];
            for (int i = 0; i < n; i++) _replayEndOf[i] = -1;
            for (int e = 0; e < run.Keys.Names.Length; e++)
            {
                int sign;
                int a = table.IndexOf(KeyTrack.ActionOf(run.Keys.Names[e], out sign));
                if (a >= 0) _replayEndOf[a * 2 + (sign < 0 ? 1 : 0)] = e;
            }
            _keysCache.Seek(run.Keys, _clock.T);
            GameActions.ReplayHeld = _replayHeldFn;
        }

        private bool ReplayHeld(int action, int sign)
        {
            int k = action * 2 + (sign < 0 ? 1 : 0);
            return k >= 0 && k < _replayEndOf.Length && _keysCache.Held(_replayEndOf[k]);
        }

        // --- the Runs tab: recordings -------------------------------------------------

        private float DrawRecorderSection(float y, float w)
        {
            y += UiText.Draw(0, y, w, RecordingsHelp);
            bool rec = _take.State == RunRecorder.RunState.Running;
            Rect r = new Rect(0, y, 110, 26);
            if (rec ? WinPx.Button(r, "Done") : UiKit.PrimaryButton(r, TextRecord)) ToggleRecording();
            y += 30f;
            if (_takeStatus.text.Length > 0) y += UiText.Draw(0, y, w, _takeStatus);
            if (_takeInfo.text.Length > 0) y += UiText.Draw(0, y, w, _takeInfo);

            // Newest first; rows out of the tab's view are not drawn.
            float viewTop = _pageScroll.y, viewBottom = _pageScroll.y + _tabViewH;
            float bw = 70f, gap = 4f;
            float textW = Mathf.Max(80f, w - 2f * (bw + gap));
            for (int k = 0; k < _recs.Count; k++)
            {
                int i = _recs.Count - 1 - k;
                float ry = y + k * RecRowH;
                if (ry + RecRowH < viewTop || ry > viewBottom) continue;
                WinPx.Label(new Rect(0, ry + 2f, textW, 20), _recRows[i]);
                bool watching = _camOn && _watchTake && ReferenceEquals(_watchRun, _recs[i]);
                if (WinPx.Button(new Rect(textW + gap, ry, bw, 22), watching ? "Stop" : "Watch")) { WatchRecording(i); break; }
                bool armed = _removeArmed == i && Time.unscaledTime <= _removeArmedUntil;
                if (WinPx.Button(new Rect(textW + gap + bw + gap, ry, bw, 22), armed ? "Sure?" : "Remove")) { RemoveRecording(i); break; }
            }
            y += _recs.Count * RecRowH;
            return y + 6f;
        }

        // --- the Runs tab: the bar's values --------------------------------------------

        /// The choices: Basic, then every other channel the player's state
        /// has (or the newest recording's, before a game is loaded). Rebuilt
        /// only when that channel list is another one.
        private void RefreshChoices()
        {
            string[] ch = Ctx.PlayerState.Channels;
            if ((ch == null || ch.Length == 0) && _recs.Count > 0) ch = _recs[_recs.Count - 1].Channels;
            if (ReferenceEquals(ch, _choicesFor) && _choiceOn.Length == _choices.Count) return;
            _choicesFor = ch;
            _choices = ReplayValues.Choices(ch);
            _choiceOn = new bool[_choices.Count];
            _choiceText = new GUIContent[_choices.Count];
            for (int i = 0; i < _choices.Count; i++)
            {
                _choiceOn[i] = _valuesChosen.Contains(_choices[i]);
                string label = ReplayValues.Label(_choices[i]);
                _choiceText[i] = new GUIContent(" " + label, label == _choices[i] ? null : _choices[i]);
            }
            int more = _choices.Count - ReplayValues.Basic.Length;
            _valuesMoreText.text = more > 0 ? " Every recorded value (" + more + " more)" : " Every recorded value (load a game to list them)";
        }

        private float DrawValueChooser(float y, float w)
        {
            y += 4f;
            y += UiText.Draw(0, y, w, ValuesHeading);
            if (WinPx.Button(new Rect(0, y, 70, 22), "Basic")) SetValues(new List<string>(ReplayValues.Basic), "Basic");
            if (WinPx.Button(new Rect(76, y, 70, 22), "Clear")) SetValues(new List<string>(), "cleared");
            y += 26f;

            int basic = Mathf.Min(ReplayValues.Basic.Length, _choices.Count);
            y = DrawChoiceGrid(0, basic, y, w);
            bool all = UiKit.Toggle(new Rect(0, y, w, 20), _valuesAllOpen, _valuesMoreText);
            if (all != _valuesAllOpen) _valuesAllOpen = all;
            y += 22f;
            if (_valuesAllOpen) y = DrawChoiceGrid(basic, _choices.Count, y, w);
            return y + 6f;
        }

        // Choices from..to-1 in columns; rows out of the tab's view are not drawn.
        private float DrawChoiceGrid(int from, int to, float y, float w)
        {
            int cols = Mathf.Max(1, (int)(w / ChoiceW));
            int n = to - from;
            int rows = (n + cols - 1) / cols;
            float viewTop = _pageScroll.y, viewBottom = _pageScroll.y + _tabViewH;
            for (int k = 0; k < n; k++)
            {
                float ry = y + (k / cols) * ChoiceH;
                if (ry + ChoiceH < viewTop || ry > viewBottom) continue;
                int i = from + k;
                bool on = UiKit.Toggle(new Rect((k % cols) * ChoiceW, ry, ChoiceW - 4f, ChoiceH), _choiceOn[i], _choiceText[i]);
                if (on != _choiceOn[i]) ToggleValue(i, on);
            }
            return y + rows * ChoiceH + 2f;
        }

        private void ToggleValue(int i, bool on)
        {
            _choiceOn[i] = on;
            List<string> chosen = new List<string>();
            for (int k = 0; k < _choices.Count; k++) if (_choiceOn[k]) chosen.Add(_choices[k]);
            // Picks not in today's list (a channel this game version lacks) are kept.
            for (int k = 0; k < _valuesChosen.Count; k++)
                if (!_choices.Contains(_valuesChosen[k]) && !chosen.Contains(_valuesChosen[k])) chosen.Add(_valuesChosen[k]);
            SetValues(chosen, _choices[i] + (on ? " on" : " off"));
        }

        private void SetValues(List<string> chosen, string what)
        {
            _valuesChosen = ReplayValues.Ordered(chosen, _choices);
            _valuesVersion++;
            string cfg = ReplayValues.Join(_valuesChosen);
            if (_valuesCfg.Value != cfg) _valuesCfg.Value = cfg;
            for (int k = 0; k < _choices.Count; k++) _choiceOn[k] = _valuesChosen.Contains(_choices[k]);
            Ctx.Log.LogInfo("Recording: replay bar values " + what + " (" + _valuesChosen.Count + " shown).");
        }
    }
}
