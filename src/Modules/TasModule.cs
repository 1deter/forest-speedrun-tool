using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
    // TAS input record / replay (Experimental; CLAUDE.md Next up 5,
    // "TAS - exploratory only, on savestates and the recorder").
    //
    // RECORD (Runs tab, "Record from a restart"; or every timed run with
    // "Record timed runs"): the current spot is restarted (its start state,
    // Quick load by default) and from the frame after the player is placed
    // every value the game reads through TheForest.Utils.Input is stored
    // frame by frame (Game/TasInput, Data/TasRecording), with the player's
    // position and look at 30 Hz. F7 / Go while recording starts it over
    // at the new placement. Stops on Stop (key `]`, unbound-able), when the
    // timed run finishes, or at the cap; written to
    // runs/<id>/inputs/<stamp>.tas.
    //
    // REPLAY: restarts the recording's spot the same way, blocks the
    // player's input from the click on, and from the frame after the
    // placement feeds the recorded values back on the same frame numbers.
    // HUD: "TAS replay frame n / N". At the end (or Stop) the replay's
    // 30 Hz positions are compared with the recording's on the same frames
    // and the log / screen say "max drift X m at t". "Lock the frame rate"
    // (on by default) replays every frame with its recorded delta time
    // (Time.captureFramerate) - without it a replay at another frame rate
    // drifts at once. A run the replay times is never saved (not the
    // runner's attempt).
    //
    // Refused in run mode (feature "tas", unknown to the categories = always
    // locked). The replay marks practice. No tab: the Runs tab draws the
    // section (DrawSection).
    // ------------------------------------------------------------------
    public sealed class TasModule : OverlayModule
    {
        public override string Id { get { return "tas"; } }
        public override string DisplayName { get { return "TAS (experimental)"; } }
        public override bool IsPracticeOnly { get { return true; } }

        private const string Feature = "tas";
        private const int MaxFrames = 200000;      // ~23 min at 144 fps, ~55 at 60
        private const int ListMax = 10;

        private enum Pending { None, Record, Replay }

        private PracticeModule _practice;
        private PracticeRunModule _runs;
        private string _root;

        private ConfigEntry<bool> _autoRecord;
        private ConfigEntry<bool> _lockReplay;
        private ConfigEntry<bool> _record60;

        private Pending _pending = Pending.None;
        private bool _manual;
        private int _finishSeen;
        private bool _t0Set;
        private float _t0;
        // Set when the run finishes: the frame it finished on is committed
        // at its end, then the recording is saved (the replay must hold the
        // finishing frame's input too).
        private string _stopAfterFrame;

        // replay
        private TasRecording _playRec;
        private string _playFile = "";
        private readonly List<TasSample> _replaySamples = new List<TasSample>(4096);
        private int _sampleCursor;
        private string _replayRun = "";

        private bool _loopOn;
        private readonly WaitForEndOfFrame _eof = new WaitForEndOfFrame();
        private readonly List<string> _seenB = new List<string>();
        private readonly List<string> _seenA = new List<string>();

        // --- text (rebuilt in Tick / on events, never in DrawSection) ------
        private string _status = "";
        private string _drift = "";
        private readonly GUIContent _header = new GUIContent("TAS (experimental) - record the game's inputs frame by frame and replay them");
        private readonly GUIContent _note = new GUIContent(
            "Records every button and axis the game reads (mouse included) from the frame after a Restart places you, " +
            "and replays them on the same frames after the same Restart - your own input is blocked during a replay " +
            "(Stop: the ] key or the button). Not deterministic: physics and loading can still differ, so the end says " +
            "how far the replay drifted from the recording. Practice only; never in run mode.");
        private readonly GUIContent _statusText = new GUIContent("");
        private readonly GUIContent _driftText = new GUIContent("");
        private readonly GUIContent _listHeader = new GUIContent("");
        private readonly GUIContent _replayButton = new GUIContent("Replay");
        private readonly GUIContent _deleteButton = new GUIContent("Delete");
        private readonly GUIContent _sureButton = new GUIContent("Sure?");

        private sealed class Row
        {
            public string Path;
            public readonly GUIContent Label = new GUIContent("");
        }
        private readonly List<Row> _rows = new List<Row>();
        private int _rowCount;
        private bool _listDirty = true;
        private string _listFor = "";
        private float _nextText;
        private int _deleteArmed = -1;
        private float _deleteArmedAt;

        /// The last finished line (for the bridge: `get ... LastLine`).
        public string LastLine = "";

        public bool Replaying
        {
            get { return _pending == Pending.Replay || TasInput.Current == TasInput.Mode.Blocking || TasInput.Current == TasInput.Mode.Playing; }
        }

        public bool Recording { get { return TasInput.Current == TasInput.Mode.Recording; } }

        // ------------------------------------------------------------------
        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);
            _root = Path.Combine(ctx.ConfigDirectory, "runs");
            _practice = Host.Find<PracticeModule>();
            _runs = Host.Find<PracticeRunModule>();
            if (_practice != null)
            {
                // After the run module's handlers (it is registered first):
                // a placement has armed the run by the time Placed runs.
                _practice.OnPlacedAtSpot += Placed;
                _practice.OnRestartStarting += RestartStarting;
            }
            else ctx.Log.LogWarning("TAS: no PracticeModule - record / replay unavailable.");

            _autoRecord = ctx.Config.Bind("TAS", "RecordTimedRuns", false,
                "Experimental: record the inputs of every practice timed run from its Restart; saved when the run finishes.");
            _lockReplay = ctx.Config.Bind("TAS", "LockFrameRateOnReplay", true,
                "Experimental: replay every frame with the delta time it was recorded with (Time.captureFramerate), put back after.");
            _record60 = ctx.Config.Bind("TAS", "RecordAtFixed60", false,
                "Experimental: record at a fixed 60 fps game step (Time.captureFramerate + targetFrameRate 60), put back after.");
        }

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("tas.stop", KeyCode.RightBracket, "TAS: stop recording / replay", StopNow);
            map.Add("tas.record", KeyCode.None, "TAS: record from a restart of the current spot", Record);
            map.Add("tas.replayLatest", KeyCode.None, "TAS: replay the current spot's newest recording", ReplayLatestKey);
        }

        public override void Shutdown()
        {
            try
            {
                TasInput.Stop();
                TasInput.UnlockFrameRate();
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------
        // Record
        // ------------------------------------------------------------------

        /// Restarts the current spot and records from its placement.
        public void Record()
        {
            if (Ctx.Run.Refuse(Feature, "TAS recording")) { SetStatus(Ctx.Run.RefusedText("TAS recording")); return; }
            if (_practice == null) { SetStatus("No Practice module."); return; }
            if (TasInput.Current != TasInput.Mode.Off || _pending != Pending.None) { SetStatus("A recording or replay is already going - Stop it first."); return; }
            Segment s = _practice.CurrentSegment;
            if (s == null || !s.HasSpawn) { SetStatus("Go to a spot in the Practice tab first - a recording starts from its Restart."); return; }
            string err = InputInject.Install(Ctx.Log, OverlayPlugin.PluginGuid);
            if (err != null) { SetStatus("TAS unavailable: " + err); return; }

            _pending = Pending.Record;
            _manual = true;
            if (_record60.Value) TasInput.LockFrameRate(60);
            SetStatus("Restarting '" + s.Name + "' - recording starts when you are placed.");
            err = _practice.BridgeRestart(null);
            if (err != null)
            {
                _pending = Pending.None;
                TasInput.UnlockFrameRate();
                SetStatus("Could not restart: " + err.TrimEnd('.') + ".");
            }
        }

        private void BeginRecording(Segment s, bool manual)
        {
            string err = InputInject.Install(Ctx.Log, OverlayPlugin.PluginGuid);
            if (err != null) { SetStatus("TAS unavailable: " + err); _pending = Pending.None; return; }

            TasRecording rec = new TasRecording(16384, 4096);
            rec.SegmentId = s.Id;
            rec.SegmentName = s.Name;
            rec.RecordedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            rec.LockFps = _record60.Value ? 60 : 0;
            if (rec.LockFps > 0) TasInput.LockFrameRate(rec.LockFps);

            InputInject.SeenNames(_seenB, _seenA);
            TasInput.StartRecording(rec, _seenB, _seenA);
            _stopAfterFrame = null;
            _pending = Pending.None;
            _manual = manual;
            _t0Set = false;
            _finishSeen = _runs != null ? _runs.FinishedRuns : 0;
            StartLoop();
            SetStatus("Recording '" + s.Name + "' from frame " + Time.frameCount + " - Stop (]) or finish the run to save.");
            Ctx.Log.LogInfo("TAS: recording '" + s.Id + "' (" + s.Name + ") from frame " + (Time.frameCount + 1) +
                            (manual ? ", by hand" : ", a timed run") + (rec.LockFps > 0 ? ", fixed " + rec.LockFps + " fps" : "") +
                            "; " + _seenB.Count + " button(s) / " + _seenA.Count + " axis(es) known so far, Rewired " + TasInput.RewiredStatus + ".");
        }

        private void StopRecording(bool save, string note)
        {
            TasRecording rec = TasInput.Recording;
            TasInput.Stop();
            string unlock = TasInput.UnlockFrameRate();
            if (unlock.Length > 0) Ctx.Log.LogInfo("TAS: " + unlock + ".");
            if (rec == null) return;
            if (!save || rec.Frames == 0) { SetStatus("Recording dropped (" + note + ")."); Ctx.Log.LogInfo("TAS: recording dropped - " + note + "."); return; }

            rec.Note = note;
            string path = PathFor(rec.SegmentId, TasRecording.Stamp(DateTime.UtcNow));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string text = rec.Write();
                File.WriteAllText(path, text, new UTF8Encoding(false));
                string line = "Recorded " + rec.Frames + " frames (" + rec.Seconds.ToString("0.00", CultureInfo.InvariantCulture) + " s) of '" +
                              rec.SegmentName + "' - " + note + ".";
                SetStatus(line);
                Ctx.Log.LogInfo("TAS: " + line + " " + rec.Buttons.Count + " button(s), " + rec.Axes.Count + " axis(es), " +
                                rec.Changes.Count + " change(s), " + rec.Samples.Count + " position sample(s), " +
                                (text.Length / 1024) + " KB -> " + path);
                if (!Host.AnyPanelOpen()) Ctx.Notice.Show("TAS: " + line, 5f);
            }
            catch (Exception ex)
            {
                SetStatus("Could not write the recording: " + ex.Message);
                Ctx.Log.LogWarning("TAS: could not write " + path + ": " + ex.Message);
            }
            _listDirty = true;
        }

        // ------------------------------------------------------------------
        // Replay
        // ------------------------------------------------------------------

        /// Replays a .tas file (a path, or a file name in the current spot's
        /// inputs folder). Null, or why not.
        public string Replay(string file)
        {
            if (Ctx.Run.Refuse(Feature, "TAS replay")) return Fail(Ctx.Run.RefusedText("TAS replay"));
            if (_practice == null) return Fail("No Practice module.");
            if (TasInput.Current != TasInput.Mode.Off || _pending != Pending.None) return Fail("A recording or replay is already going - Stop it first.");
            if (string.IsNullOrEmpty(file)) return Fail("No recording given.");
            string path = file;
            if (!File.Exists(path) && _practice.CurrentSegment != null) path = Path.Combine(FolderFor(_practice.CurrentSegment.Id), file);
            if (!File.Exists(path)) return Fail("No recording at " + file + ".");

            TasRecording rec;
            string err;
            try { rec = TasRecording.Parse(File.ReadAllText(path), out err); }
            catch (Exception ex) { rec = null; err = ex.Message; }
            if (rec == null) return Fail("Could not read " + Path.GetFileName(path) + ": " + err + ".");
            if (rec.Frames == 0) return Fail("That recording is empty.");
            if (_practice.Library.ById(rec.SegmentId) == null) return Fail("Its spot ('" + rec.SegmentName + "') is not in your Practice list any more.");
            err = InputInject.Install(Ctx.Log, OverlayPlugin.PluginGuid);
            if (err != null) return Fail("TAS unavailable: " + err);

            _playRec = rec;
            _playFile = Path.GetFileName(path);
            _replaySamples.Clear();
            _sampleCursor = 0;
            _replayRun = "";
            TasInput.Block(new TasPlayback(rec));
            if (_lockReplay.Value) TasInput.LockFrameRate(TasInput.Playback.Fps(0));
            _pending = Pending.Replay;
            StartLoop();
            Ctx.Practice.Mark("TAS replay");
            SetStatus("Replaying " + _playFile + " - restarting '" + rec.SegmentName + "' first; your input is blocked (Stop: ]).");
            Ctx.Log.LogInfo("TAS: replay of " + path + " ('" + rec.SegmentId + "', " + rec.Frames + " frames, " +
                            (rec.LockFps > 0 ? "recorded at fixed " + rec.LockFps + " fps" : "recorded at the game's rate") +
                            ", frame lock " + (_lockReplay.Value ? "on" : "off") + ") - restarting the spot.");

            err = _practice.BridgeRestart(rec.SegmentId);
            if (err != null) { EndReplay("could not restart: " + err); return LastLine; }
            return null;
        }

        /// The current spot's newest recording. Null, or why not.
        public string ReplayLatest()
        {
            RebuildList();
            if (_rowCount == 0) return Fail("No recordings for the current spot.");
            return Replay(_rows[0].Path);
        }

        private void ReplayLatestKey() { ReplayLatest(); }

        private string Fail(string why)
        {
            SetStatus(why);
            if (!Host.AnyPanelOpen()) Ctx.Notice.Show("TAS: " + why, 5f);
            return why;
        }

        /// From PracticeRunModule: a run timed during the replay finished
        /// (not saved).
        public void ReplayRunFinished(float seconds)
        {
            _replayRun = "the replay's run finished in " + PracticeRunModule.Format(seconds);
        }

        private void EndReplay(string why)
        {
            int frame = TasInput.Current == TasInput.Mode.Playing ? TasInput.FrameIndex : -1;
            bool locked = TasInput.FrameRateLocked;
            TasInput.Stop();
            _pending = Pending.None;
            string unlock = TasInput.UnlockFrameRate();
            if (_playRec == null) return;

            TasDrift d = TasDrift.Compare(_playRec.Samples, _replaySamples);
            string line = "TAS replay of " + _playFile + ": " + why + " at frame " + Math.Max(0, Math.Min(frame + 1, _playRec.Frames)) + " / " +
                          _playRec.Frames + " - " + d.Describe() + (locked ? " (frame rate locked)" : " (frame rate not locked)") +
                          (_replayRun.Length > 0 ? "; " + _replayRun : "") +
                          (_playRec.Note.Length > 0 ? "; recorded: " + _playRec.Note : "") + ".";
            LastLine = line;
            _drift = line;
            SetStatus("");
            Ctx.Log.LogInfo(line + (unlock.Length > 0 ? " TAS: " + unlock + "." : ""));   // log: TAS replay of
            Ctx.Notice.Show(d.Compared > 0 ? "TAS replay: " + why + " - " + d.Describe() : "TAS replay: " + why, 8f);
            _playRec = null;
        }

        // ------------------------------------------------------------------
        public void StopNow()
        {
            if (TasInput.Current == TasInput.Mode.Recording) { StopRecording(true, "stopped by hand"); return; }
            if (Replaying) { EndReplay("stopped by hand"); return; }
            if (_pending == Pending.Record)
            {
                _pending = Pending.None;
                TasInput.UnlockFrameRate();
                SetStatus("Recording cancelled.");
            }
        }

        // ------------------------------------------------------------------
        // The practice module's placements
        // ------------------------------------------------------------------
        private void Placed()
        {
            try
            {
                Segment s = _practice.CurrentSegment;
                if (_pending == Pending.Replay)
                {
                    if (s == null || _playRec == null || s.Id != _playRec.SegmentId) { EndReplay("placed at another spot"); return; }
                    _pending = Pending.None;
                    TasInput.StartPlaying();
                    SetStatus("Replaying " + _playFile + " (" + _playRec.Frames + " frames) - Stop: ].");
                    Ctx.Log.LogInfo("TAS: replay of " + _playFile + " starts on frame " + (Time.frameCount + 1) + ".");
                    return;
                }
                if (TasInput.Current == TasInput.Mode.Playing || TasInput.Current == TasInput.Mode.Blocking)
                {
                    EndReplay("the player was placed at a spot");
                    return;
                }
                if (s == null || !s.HasSpawn) { if (_pending == Pending.Record) { _pending = Pending.None; TasInput.UnlockFrameRate(); } return; }
                if (_pending == Pending.Record) { BeginRecording(s, _manual); return; }
                if (TasInput.Current == TasInput.Mode.Recording)
                {
                    // F7 / Go mid-recording: start over from here.
                    TasInput.Stop();
                    Ctx.Log.LogInfo("TAS: placed again - the recording starts over.");
                    BeginRecording(s, _manual);
                    return;
                }
                if (_autoRecord.Value && !Ctx.Run.Locks(Feature) && _runs != null && _runs.TimedRunArmed && s.IsTimed)
                    BeginRecording(s, false);
            }
            catch (Exception ex)
            {
                Ctx.Log.LogWarning("TAS: placement hook threw: " + ex);
            }
        }

        private void RestartStarting()
        {
            try
            {
                if (TasInput.Current == TasInput.Mode.Recording)
                {
                    // The restore runs frames; the recording starts over at the placement.
                    TasInput.Stop();
                    _pending = Pending.Record;
                    SetStatus("Restarting - the recording starts over when you are placed.");
                }
                else if (TasInput.Current == TasInput.Mode.Playing) EndReplay("a restart started");
            }
            catch (Exception ex)
            {
                Ctx.Log.LogWarning("TAS: restart hook threw: " + ex);
            }
        }

        // ------------------------------------------------------------------
        public override void Tick()
        {
            if (TasInput.Current != TasInput.Mode.Off || _pending != Pending.None)
            {
                if (Ctx.Run.Locks(Feature)) { StopAll("run mode started"); return; }
                if (PlayerRef.AtTitleScreen) { StopAll("left the level"); return; }
            }

            if (TasInput.Current == TasInput.Mode.Recording)
            {
                TasRecording rec = TasInput.Recording;
                if (_runs != null && _runs.FinishedRuns != _finishSeen)
                {
                    _finishSeen = _runs.FinishedRuns;
                    _stopAfterFrame = "run finished in " + PracticeRunModule.Format(_runs.LastFinishSeconds);
                }
                else if (rec != null && rec.Frames >= MaxFrames)
                    StopRecording(true, "stopped at the " + MaxFrames + "-frame cap");
            }

            if (Time.unscaledTime >= _nextText)
            {
                _nextText = Time.unscaledTime + 0.25f;
                _statusText.text = _status;
                _driftText.text = _drift;
                string seg = _practice != null && _practice.CurrentSegment != null ? _practice.CurrentSegment.Id : "";
                if (seg != _listFor) _listDirty = true;
                if (_deleteArmed >= 0 && Time.unscaledTime - _deleteArmedAt > 4f) _deleteArmed = -1;
            }
            // Only when something changed (a save, a delete, another spot):
            // a folder listing and a few headers - never in DrawSection.
            if (_listDirty) RebuildList();
        }

        private void StopAll(string why)
        {
            if (TasInput.Current == TasInput.Mode.Recording) { StopRecording(false, why); return; }
            if (Replaying) { EndReplay(why); return; }
            _pending = Pending.None;
            TasInput.UnlockFrameRate();
        }

        public override void ContributeHud(HudBuilder hud)
        {
            switch (TasInput.Current)
            {
                case TasInput.Mode.Recording:
                    hud.Pair("TAS", "recording frame " + Math.Max(0, TasInput.FrameIndex + 1) + "   (] stops)");
                    break;
                case TasInput.Mode.Blocking:
                    hud.Pair("TAS", "replay waiting for the restart   (] stops)");
                    break;
                case TasInput.Mode.Playing:
                    hud.Pair("TAS", "replay frame " + Math.Max(0, TasInput.FrameIndex + 1) + " / " +
                                    (TasInput.Playback != null ? TasInput.Playback.Frames : 0) + "   (] stops)");
                    break;
                default:
                    if (_pending == Pending.Record) hud.Pair("TAS", "recording starts after the restart");
                    break;
            }
        }

        // ------------------------------------------------------------------
        // End of every frame while recording / replaying.
        // ------------------------------------------------------------------
        private void StartLoop()
        {
            if (_loopOn || Ctx.Runner == null) return;
            _loopOn = true;
            Ctx.Runner.StartCoroutine(EndOfFrameLoop());
        }

        private IEnumerator EndOfFrameLoop()
        {
            while (TasInput.Current != TasInput.Mode.Off || _pending != Pending.None)
            {
                yield return _eof;
                try { EndOfFrame(); }
                catch (Exception ex)
                {
                    Ctx.Log.LogWarning("TAS: end of frame threw - stopping: " + ex);
                    try { StopAll("an error (see the log)"); } catch (Exception) { }
                }
            }
            _loopOn = false;
        }

        private void EndOfFrame()
        {
            if (TasInput.Current == TasInput.Mode.Recording)
            {
                int i = TasInput.CommitRecording(InputInject.State);
                if (i < 0) return;
                TasRecording rec = TasInput.Recording;
                if (!_t0Set) { _t0Set = true; _t0 = Time.time; }
                float t = Time.time - _t0;
                rec.Seconds = t;
                if (rec.SampleDue(t)) rec.AddSample(SampleNow(i, t));
                if (_stopAfterFrame != null)
                {
                    string note = _stopAfterFrame;
                    _stopAfterFrame = null;
                    StopRecording(true, note);
                }
                return;
            }

            if (TasInput.Current == TasInput.Mode.Playing && _playRec != null)
            {
                int i = TasInput.FrameIndex;
                if (i < 0) return;
                List<TasSample> rs = _playRec.Samples;
                while (_sampleCursor < rs.Count && rs[_sampleCursor].Frame < i) _sampleCursor++;
                if (_sampleCursor < rs.Count && rs[_sampleCursor].Frame == i) _replaySamples.Add(SampleNow(i, rs[_sampleCursor].T));

                TasPlayback p = TasInput.Playback;
                if (i + 1 >= p.Frames) { EndReplay("finished"); return; }
                if (_lockReplay.Value) TasInput.LockFrameRate(p.Fps(i + 1));
            }
        }

        private TasSample SampleNow(int frame, float t)
        {
            TasSample s = default(TasSample);
            s.Frame = frame;
            s.T = t;
            if (Ctx.Player.Found)
            {
                Vector3 pos = Ctx.Player.Transform.position;
                s.X = pos.x; s.Y = pos.y; s.Z = pos.z;
                s.Yaw = Ctx.Player.Transform.eulerAngles.y;
                s.Pitch = Ctx.Bridge.GetLookPitch();
            }
            return s;
        }

        // ------------------------------------------------------------------
        // Files
        // ------------------------------------------------------------------
        private string FolderFor(string segmentId)
        {
            return Path.Combine(Path.Combine(_root, SafeName(segmentId)), "inputs");
        }

        private string PathFor(string segmentId, string stamp)
        {
            string dir = FolderFor(segmentId);
            string path = Path.Combine(dir, stamp + TasRecording.Extension);
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(dir, stamp + "-" + n + TasRecording.Extension);
            return path;
        }

        private static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unnamed";
            char[] bad = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++) sb.Append(Array.IndexOf(bad, s[i]) >= 0 ? '_' : s[i]);
            return sb.ToString();
        }

        private void RebuildList()
        {
            _listDirty = false;
            _rowCount = 0;
            Segment seg = _practice != null ? _practice.CurrentSegment : null;
            _listFor = seg != null ? seg.Id : "";
            if (seg == null) { _listHeader.text = "Recordings: go to a spot in the Practice tab."; return; }

            string[] files;
            try
            {
                string dir = FolderFor(seg.Id);
                files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*" + TasRecording.Extension) : new string[0];
            }
            catch (Exception ex) { _listHeader.text = "Recordings: could not list - " + ex.Message; return; }

            Array.Sort(files, StringComparer.Ordinal);
            Array.Reverse(files);   // stamps sort by time: newest first
            int shown = Math.Min(files.Length, ListMax);
            for (int i = 0; i < shown; i++)
            {
                if (_rows.Count <= i) _rows.Add(new Row());
                Row r = _rows[i];
                r.Path = files[i];
                r.Label.text = RowText(files[i]);
            }
            _rowCount = shown;
            _listHeader.text = files.Length == 0
                ? "No recordings for '" + seg.Name + "' yet."
                : "Recordings for '" + seg.Name + "' (" + files.Length + (files.Length > ListMax ? ", newest " + ListMax + " shown" : "") + "):";
        }

        private static string RowText(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            try
            {
                string err;
                TasRecording h;
                using (StreamReader r = new StreamReader(path)) h = TasRecording.ParseHeader(r, out err);
                if (h == null) return name + " - unreadable: " + err;
                return name + "   " + h.Frames + " frames, " + h.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s" +
                       (h.LockFps > 0 ? ", fixed " + h.LockFps + " fps" : "") + (h.Note.Length > 0 ? " - " + h.Note : "");
            }
            catch (Exception ex) { return name + " - " + ex.Message; }
        }

        private void Delete(int i)
        {
            if (i < 0 || i >= _rowCount) return;
            string path = _rows[i].Path;
            try
            {
                File.Delete(path);
                SetStatus("Deleted " + Path.GetFileName(path) + ".");
                Ctx.Log.LogInfo("TAS: deleted " + path + ".");
            }
            catch (Exception ex) { SetStatus("Could not delete: " + ex.Message); }
            _deleteArmed = -1;
            _listDirty = true;
        }

        private void SetStatus(string s)
        {
            _status = s ?? "";
            _statusText.text = _status;   // answers a click at once
        }

        // ------------------------------------------------------------------
        // The Runs tab's section.
        // ------------------------------------------------------------------
        public float DrawSection(float y, float w)
        {
            y += UiText.Draw(0, y, w, _header);
            y += UiText.DrawDim(0, y, w, _note);

            bool auto = GUI.Toggle(new Rect(0, y, w, 20), _autoRecord.Value, " Record timed runs (from their Restart; saved when the run finishes)");
            if (auto != _autoRecord.Value) _autoRecord.Value = auto;
            y += 22f;
            bool lockOn = GUI.Toggle(new Rect(0, y, w, 20), _lockReplay.Value, " Lock the frame rate during a replay (the recorded frame times)");
            if (lockOn != _lockReplay.Value) _lockReplay.Value = lockOn;
            y += 22f;
            bool r60 = GUI.Toggle(new Rect(0, y, w, 20), _record60.Value, " Record at a fixed 60 fps game step");
            if (r60 != _record60.Value) _record60.Value = r60;
            y += 24f;

            bool was = GUI.enabled;
            bool idle = TasInput.Current == TasInput.Mode.Off && _pending == Pending.None;
            GUI.enabled = was && idle;
            if (GUI.Button(new Rect(0, y, 170, 24), "Record from a restart")) Record();
            GUI.enabled = was && !idle;
            if (GUI.Button(new Rect(176, y, 90, 24), "Stop")) StopNow();
            GUI.enabled = was;
            y += 28f;

            y += UiText.Draw(0, y, w, _statusText);
            y += UiText.Draw(0, y, w, _driftText);
            y += UiText.Draw(0, y, w, _listHeader);

            for (int i = 0; i < _rowCount; i++)
            {
                GUI.enabled = was && idle;
                if (GUI.Button(new Rect(0, y, 70, 22), _replayButton)) Replay(_rows[i].Path);
                GUI.enabled = was;
                if (GUI.Button(new Rect(76, y, 64, 22), _deleteArmed == i ? _sureButton : _deleteButton))
                {
                    if (_deleteArmed == i) Delete(i);
                    else { _deleteArmed = i; _deleteArmedAt = Time.unscaledTime; }
                }
                float h = UiText.Draw(146, y + 2, w - 146, _rows[i].Label);
                y += Mathf.Max(24f, h + 4f);
            }
            GUI.enabled = was;
            return y + 6f;
        }
    }
}
