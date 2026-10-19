using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // The recorder (T-0344; QA ask, author 2026-10-19: all three, before
    // v1): Record / Done records any stretch of play - no spot, no timed
    // segment, practice mode on or off - and the replay camera plays it
    // back (Camera.cs: chase / first person / trajectory / free, the bar
    // with its timeline). It records what a timed run records (position
    // and speed at 30 Hz, the look, the arms, the player's state at 5 Hz,
    // item changes) through a second RunRecorder with the same readers,
    // plus the keys pressed (Data/KeyTrack: the physical keys, as the
    // input overlay's Raw input). Only reads the game: not practice
    // marked, allowed in run mode (watching is the replay camera's, which
    // marks practice and is locked there).
    //
    // One recording is kept - the last - in memory and in
    // `recordings/last.run` (Data/AttemptFormat, `k|` lines for the keys),
    // read back at launch. A recording stops by itself at an hour.
    //
    // The bar's values (Data/ReplayValues) are the runner's pick for every
    // replay, the recording's and the comparison run's: `[Runs]
    // ReplayValues`, default the site's Basic list, set in Ghost and replay.
    // ------------------------------------------------------------------
    public sealed partial class PracticeRunModule
    {
        private const float MaxTakeSeconds = 3600f;
        private const float ChoiceW = 160f, ChoiceH = 20f;

        private readonly RunRecorder _take = new RunRecorder();
        private ItemCounter _takeItems;
        private Attempt _takeAttempt;   // the last recording
        private bool _watchTake;        // the replay camera plays _takeAttempt (only while _camOn)
        private bool _camAskedTake;     // the camera's messages go under the recorder's button
        private string _takePath;
        private bool _keysFull;
        private int _takeShownState = -1;
        private float _nextTakeText;

        private readonly GUIContent _takeStatus = new GUIContent("");
        private readonly GUIContent _takeInfo = new GUIContent("");
        private readonly GUIContent _recorderSummary = new GUIContent("");
        private float _tabViewH = 400f;

        // The keys the recorder watches: letters, digits, the modifiers and
        // the usual others, the mouse buttons, F1-F12. Names are the bar's words.
        private static readonly KeyCode[] RecKeys =
        {
            KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.Space, KeyCode.LeftShift, KeyCode.LeftControl,
            KeyCode.Mouse0, KeyCode.Mouse1, KeyCode.Mouse2, KeyCode.Mouse3, KeyCode.Mouse4,
            KeyCode.E, KeyCode.R, KeyCode.Q, KeyCode.F, KeyCode.C, KeyCode.Tab, KeyCode.LeftAlt,
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
            KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9, KeyCode.Alpha0,
            KeyCode.B, KeyCode.G, KeyCode.H, KeyCode.I, KeyCode.J, KeyCode.K, KeyCode.L, KeyCode.M,
            KeyCode.N, KeyCode.O, KeyCode.P, KeyCode.T, KeyCode.U, KeyCode.V, KeyCode.X, KeyCode.Y, KeyCode.Z,
            KeyCode.RightShift, KeyCode.RightControl, KeyCode.RightAlt, KeyCode.Return, KeyCode.Backspace,
            KeyCode.CapsLock, KeyCode.Escape, KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
            KeyCode.F1, KeyCode.F2, KeyCode.F3, KeyCode.F4, KeyCode.F5, KeyCode.F6,
            KeyCode.F7, KeyCode.F8, KeyCode.F9, KeyCode.F10, KeyCode.F11, KeyCode.F12,
        };
        private static readonly string[] RecKeyNames =
        {
            "W", "A", "S", "D", "Space", "Shift", "Ctrl",
            "M1", "M2", "M3", "M4", "M5",
            "E", "R", "Q", "F", "C", "Tab", "Alt",
            "1", "2", "3", "4", "5",
            "6", "7", "8", "9", "0",
            "B", "G", "H", "I", "J", "K", "L", "M",
            "N", "O", "P", "T", "U", "V", "X", "Y", "Z",
            "R Shift", "R Ctrl", "R Alt", "Enter", "Backspace",
            "Caps", "Esc", "Up", "Down", "Left", "Right",
            "F1", "F2", "F3", "F4", "F5", "F6",
            "F7", "F8", "F9", "F10", "F11", "F12",
        };
        private readonly bool[] _keyDown = new bool[RecKeys.Length];

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

        private static readonly GUIContent TextRecorder = new GUIContent("Recorder");
        private static readonly GUIContent TipRecorder = new GUIContent("Record any stretch of play, no spot needed, and watch it back with the replay camera.");
        private static readonly GUIContent TextRecord = new GUIContent("Record");
        private static readonly GUIContent RecorderHelp = new GUIContent(
            "Record anything you play - no spot or timed segment needed: Record, play, Done. Then Watch it back with the replay camera " +
            "(chase, first person, trajectory or free - fly around it with W A S D). The bar under it shows the keys you pressed and the values " +
            "picked in Ghost and replay. Only the last recording is kept.");
        private static readonly GUIContent ValuesHeading = new GUIContent(
            "Replay bar values - what the bar under any replay shows at each moment (Basic = the website's list):");

        private GUIContent RecorderSummary { get { return _recorderSummary.text.Length > 0 ? _recorderSummary : null; } }

        /// The run the replay camera plays: the recording while it is watched, else the comparison run.
        private Attempt Watched { get { return _watchTake ? _takeAttempt : _reference; } }

        /// The replay camera plays the comparison run (its splits drive the panel).
        private bool WatchingComparison { get { return _camOn && !_watchTake && _reference != null; } }

        /// The recorder is recording (the bridge reads this).
        public bool Recording { get { return _take.State == RunRecorder.RunState.Running; } }

        private void InitRecorder(ModuleContext ctx)
        {
            _take.StateSource = ReadState;
            _take.LookSource = ReadLook;
            _take.AnimSource = ReadAnim;
            _takeItems = new ItemCounter(ctx.Log, ctx.Inventory);   // its own: Fill keeps what it last saw
            _take.ItemSource = _takeItems.Fill;
            _takePath = Path.Combine(Path.Combine(ctx.ConfigDirectory, "recordings"), "last.run");

            _valuesCfg = ctx.Config.Bind("Runs", "ReplayValues", ReplayValues.BasicConfig,
                "The values the replay bar shows at each moment, comma separated (Speed and the player's state channels). " +
                "Default: the website's Basic list. Set in the Runs tab, Ghost and replay.");
            _valuesChosen = ReplayValues.Parse(_valuesCfg.Value);
            LoadTake();
            RefreshChoices();   // the tab may draw before the first Tick
        }

        private void ShutdownRecorder()
        {
            if (_take.State == RunRecorder.RunState.Running) FinishTake("the game closed");
        }

        // --- record / done ---------------------------------------------------------

        /// Record, or Done while recording (the tab's button, its hotkey).
        public void ToggleRecording()
        {
            if (_take.State == RunRecorder.RunState.Running) { FinishTake("Done"); return; }
            if (_camOn) { SetTakeStatus("The replay camera is on - stop watching first."); return; }
            if (!Ctx.Player.Found) { SetTakeStatus("No player yet - load a game first."); return; }

            Ctx.PlayerState.Resolve();
            _take.StateChannels = Ctx.PlayerState.Channels;
            _take.ChannelSource = delegate { return Ctx.PlayerState.Channels; };
            _take.Route = "";
            Vector3 pos = PlayerPosition();
            _take.Arm(pos, "recording");
            _take.ForceStart(pos);
            _take.Current.Keys = new KeyTrack();
            for (int i = 0; i < _keyDown.Length; i++) _keyDown[i] = false;
            _keysFull = false;
            SetTakeStatus("Recording - press Done (Runs tab, Recorder) to stop.");
            Ctx.Log.LogInfo("Recorder: recording from (" + pos.x.ToString("0.0") + ", " + pos.y.ToString("0.0") + ", " + pos.z.ToString("0.0") + ").");
        }

        /// The recording in the replay camera, or the camera off (the tab's button, its hotkey).
        public void ToggleWatchRecording()
        {
            if (_camOn) { EndReplayCamera("turned off"); return; }
            StartReplayCamera(true);
            // Refused from the hotkey with the tab closed: say why on screen.
            if (!_camOn && !TabShowing && _takeStatus.text.Length > 0) Ctx.Notice.Show(_takeStatus.text, 5f);
        }

        private void FinishTake(string why)
        {
            Attempt a = _take.Finish();
            if (a == null || a.Samples.Count < 2)
            {
                SetTakeStatus("That recording was too short to watch - nothing kept.");
                Ctx.Log.LogInfo("Recorder: stopped (" + why + ") - too short, nothing kept.");
                return;
            }
            a.AnchorLabel = "recording";
            _takeAttempt = a;
            string saved = SaveTake(a);
            int keys = a.Keys != null ? a.Keys.Changes.Count : 0;
            SetTakeStatus("Recorded " + Format(a.Duration) + (why == "Done" ? "" : " (stopped: " + why + ")") + " - Watch the recording to see it.");
            Ctx.Log.LogInfo("Recorder: done (" + why + ") - " + a.Duration.ToString("0.00") + " s, " + a.Samples.Count + " position(s), " +
                            a.States.Count + " state sample(s), " + keys + " key change(s)" + (_keysFull ? " (key track full)" : "") + ", " + saved + ".");
            _takeShownState = -1;
        }

        /// Every frame, before Tick's early returns.
        private void TickTake()
        {
            RefreshChoices();
            RefreshTakeText();
            if (_take.State != RunRecorder.RunState.Running) return;
            // No player (a load, the menu): the clock holds until there is one again.
            if (!Ctx.Player.Found) return;

            Ctx.PlayerState.Resolve();
            _take.LoadingNow = GameLoading.Now;
            _take.Tick(Ctx.Player.Transform.position, Ctx.Player.HorizontalSpeed, Time.unscaledDeltaTime, null);
            SampleKeys();
            if (_take.Elapsed >= MaxTakeSeconds) FinishTake("an hour is the longest recording");
        }

        // One Input.GetKey per watched key; a change goes on the track.
        private void SampleKeys()
        {
            Attempt cur = _take.Current;
            if (cur == null || cur.Keys == null) return;
            float t = _take.Elapsed;
            for (int i = 0; i < RecKeys.Length; i++)
            {
                bool down = Input.GetKey(RecKeys[i]);
                if (down == _keyDown[i]) continue;
                _keyDown[i] = down;
                if (!cur.Keys.Add(t, RecKeyNames[i], down)) _keysFull = true;
            }
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

        // The section's summary and the line about the kept recording: rebuilt when they change.
        private void RefreshTakeText()
        {
            bool rec = _take.State == RunRecorder.RunState.Running;
            int state = rec ? 1 : (_takeAttempt != null ? 2 : 0);
            if (state == _takeShownState && !(rec && Time.unscaledTime >= _nextTakeText)) return;
            _takeShownState = state;
            _nextTakeText = Time.unscaledTime + 0.5f;
            if (rec)
            {
                _recorderSummary.text = "recording";
                _takeInfo.text = "Recording: " + ClockText.Clock(_take.Elapsed);
                return;
            }
            _recorderSummary.text = "";
            if (_takeAttempt == null) { _takeInfo.text = ""; return; }
            Attempt a = _takeAttempt;
            int keys = a.Keys != null ? a.Keys.Changes.Count : 0;
            _takeInfo.text = "Last recording: " + Format(a.Duration) + ", made " + a.RecordedUtc.ToLocalTime().ToString("d MMM HH:mm") +
                             (keys > 0 ? " - keys and values recorded." : " - values recorded, no keys pressed.");
        }

        // --- the file --------------------------------------------------------------

        /// "saved to ..." or why not.
        private string SaveTake(Attempt a)
        {
            try
            {
                string dir = Path.GetDirectoryName(_takePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                SafeFile.WriteAllText(_takePath, AttemptFormat.Write(a), Encoding.UTF8);
                return "saved to recordings/last.run";
            }
            catch (Exception ex)
            {
                Ctx.Log.LogWarning("Recorder: could not save the recording - " + ex.Message);
                return "not saved (" + ex.GetType().Name + ")";
            }
        }

        private void LoadTake()
        {
            try
            {
                if (!File.Exists(_takePath)) return;
                Attempt a = AttemptFormat.Parse(File.ReadAllLines(_takePath));
                if (a == null || a.Samples.Count < 2) return;
                _takeAttempt = a;
                Ctx.Log.LogInfo("Recorder: last recording read back (" + a.Duration.ToString("0.00") + " s).");
            }
            catch (Exception ex)
            {
                Ctx.Log.LogWarning("Recorder: could not read the last recording - " + ex.Message);
            }
        }

        // --- the Runs tab: the recorder ----------------------------------------------

        private float DrawRecorderSection(float y, float w)
        {
            y += UiText.Draw(0, y, w, RecorderHelp);
            bool rec = _take.State == RunRecorder.RunState.Running;
            Rect r = new Rect(0, y, 110, 26);
            if (rec ? WinPx.Button(r, "Done") : UiKit.PrimaryButton(r, TextRecord)) ToggleRecording();
            Rect wr = new Rect(116, y, 190, 26);
            if (WinPx.Button(wr, _camOn && _watchTake ? "Stop watching" : "Watch the recording")) ToggleWatchRecording();
            y += 30f;
            if (_takeStatus.text.Length > 0) y += UiText.Draw(0, y, w, _takeStatus);
            if (_takeInfo.text.Length > 0) y += UiText.Draw(0, y, w, _takeInfo);
            return y + 6f;
        }

        // --- the Runs tab: the bar's values --------------------------------------------

        /// The choices: Basic, then every other channel the player's state
        /// has (or the last recording's, before a game is loaded). Rebuilt
        /// only when that channel list is another one.
        private void RefreshChoices()
        {
            string[] ch = Ctx.PlayerState.Channels;
            if ((ch == null || ch.Length == 0) && _takeAttempt != null) ch = _takeAttempt.Channels;
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
            Ctx.Log.LogInfo("Recorder: replay bar values " + what + " (" + _valuesChosen.Count + " shown).");
        }
    }
}
