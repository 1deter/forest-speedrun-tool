using System.Collections.Generic;
using BepInEx.Configuration;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Checkpoint states and "Restart from checkpoint" ("saveloc", like KSF
    // surf; author, QA Discord 2026-09-26 - full-run practice per category).
    //
    // - Capture at checkpoints (Runs tab, OFF by default): as a practice run
    //   fires a checkpoint, a Quick-load state is captured for it
    //   (SavestateModule.CaptureCheckpointState: no memory clean-up, the
    //   file written on a worker thread). Still a hitch - the level is
    //   serialized on the main thread - so the option says so. Never in run
    //   mode, never with a menu open, and a capture still running skips the
    //   next one. Newest wins (Data/CheckpointStates says why).
    // - Restart from checkpoint N (a button per checkpoint, and a hotkey,
    //   unbound): Quick-loads the state and resumes the run with the clock
    //   at the moment it was captured, the earlier checkpoints fired
    //   (SplitSequence.Resume) and the split times of the run that captured
    //   it.
    // - A resumed run is practice: never saved as an attempt (no PB, last,
    //   average, PB chance, upload). The segments it runs live are kept as
    //   gold candidates (runs/<id>/checkpoint-segments.txt) and lower the
    //   best segments / sum of best.
    // ------------------------------------------------------------------
    public sealed partial class PracticeRunModule
    {
        private SavestateModule _savestates;
        private ConfigEntry<bool> _cpCapture;

        // -1 = a full run; else the checkpoint (0-based) this run resumed
        // after.
        private int _resumedFrom = -1;
        // The checkpoint restarted from last (the hotkey's).
        private int _cpLast = -1;
        private bool _cpRestoring;
        // The first frame after a resume: its delta time holds the
        // restore's own frame, not the runner's.
        private bool _resumeSkipDt;
        // Auto-restart after a resumed run: back to its checkpoint.
        private int _autoRestartCheckpoint = -1;

        private List<PracticeSegment> _practiceSegs = new List<PracticeSegment>();

        // The section's text, rebuilt in Tick when dirty.
        private bool _cpDirty = true;
        private float _cpNextRefresh;
        private readonly List<int> _cpUsable = new List<int>();
        private readonly List<GUIContent> _cpButtons = new List<GUIContent>();
        private readonly List<GUIContent> _cpLines = new List<GUIContent>();
        private bool[] _cpRowUsable = new bool[0];
        private int _cpRows;
        private string _cpBuiltFor = "";
        private readonly GUIContent _cpNote = new GUIContent(
            "Captures a Quick-load state each time a practice run reaches a checkpoint, for Restart from checkpoint. " +
            "Each capture is a short hitch in the run that takes it (tens to hundreds of ms; the log line says how long) - " +
            "leave it off for attempts you care about. Never in run mode. A run restarted from a checkpoint is practice: " +
            "never a PB, but the segments it runs can be golds.");
        private readonly GUIContent _cpStatus = new GUIContent("");
        private readonly GUIContent _cpHeader = new GUIContent("");

        private void InitCheckpoints(ModuleContext ctx)
        {
            _savestates = Host.Find<SavestateModule>();
            _cpCapture = ctx.Config.Bind("Runs", "CaptureAtCheckpoints", false,
                "Capture a Quick-load savestate as each checkpoint of a practice run fires, for Restart from checkpoint. " +
                "Each capture is a short hitch in that run. Never in run mode.");
        }

        private bool Resumed { get { return _resumedFrom >= 0; } }

        // --- capture -----------------------------------------------------------

        /// A checkpoint fired by its trigger (not F12) during a running run.
        private void OnCheckpointSplit(int row)
        {
            if (_segment == null) return;
            RecordPracticeSegment(row);

            if (_cpCapture == null || !_cpCapture.Value || _savestates == null) return;
            if (Ctx.Run.Active) return;   // never in run mode (it locks savestates anyway)

            Segment seg = _segment;
            int index = row;
            float askedElapsed = _recorder.Elapsed;
            float askedAt = Time.realtimeSinceStartup;
            float[] splits = _splits.ToArray();
            string route = _armedRoute;
            List<KeyValuePair<int, int>> baseline = new List<KeyValuePair<int, int>>(_referencedItemIds.Count);
            for (int i = 0; i < _referencedItemIds.Count; i++)
                baseline.Add(new KeyValuePair<int, int>(_referencedItemIds[i], _baseline.AmountOf(_referencedItemIds[i])));

            _savestates.CaptureCheckpointState(seg, index, delegate(SavestateBridge.Result r)
            {
                CheckpointState st = new CheckpointState();
                st.Index = index;
                st.Route = route;
                st.CapturedUtc = System.DateTime.UtcNow;
                // The clock when the level was serialized: the frames the
                // capture took count for the resumed run too.
                float late = r.SerializedAt >= 0f ? r.SerializedAt - askedAt : 0f;
                st.ResumeAt = askedElapsed + Mathf.Max(0f, late);
                st.Splits = splits;
                st.Baseline.AddRange(baseline);
                return st.Write();
            }, delegate(string error)
            {
                _cpDirty = true;
                if (error == null) return;
                Ctx.Log.LogInfo("Run '" + seg.Id + "': checkpoint " + (index + 1) + " state not captured - " + error + ".");
                _cpStatus.text = "Checkpoint " + (index + 1) + " not captured: " + error + ".";
            });
        }

        /// A segment run live after a resume: a gold candidate.
        private void RecordPracticeSegment(int row)
        {
            if (!Resumed || _segment == null) return;
            float seconds = PracticeGolds.LiveSegment(_times, _resumedFrom, row);
            if (float.IsNaN(seconds)) return;
            PracticeSegment p = new PracticeSegment();
            p.Route = _armedRoute;
            p.Utc = System.DateTime.UtcNow;
            p.Row = row;
            p.Seconds = seconds;
            _practiceSegs.Add(p);
            _store.AddPracticeSegment(_segment.Id, p);
        }

        private void LoadPracticeSegments(string id)
        {
            _practiceSegs = _store.LoadPracticeSegments(id);
            _cpDirty = true;
        }

        /// Golds from checkpoint practice, merged into freshly built stats.
        private void ApplyPracticeGolds(SplitStats st)
        {
            if (st == null || _practiceSegs.Count == 0) return;
            PracticeGolds.Apply(st, _practiceSegs, _armedRoute);
        }

        // --- restart from a checkpoint -----------------------------------------

        private void RestartFromCheckpointKey()
        {
            if (_cpDirty) RefreshCheckpointRows();
            int pick = CheckpointFiles.PickForHotkey(_cpUsable, _cpLast);
            if (pick < 0)
            {
                Ctx.Notice.Show(_segment == null ? "Restart from checkpoint: go to a timed segment first."
                                                 : "Restart from checkpoint: no checkpoint state yet - tick Capture at checkpoints in the Runs tab and run it.", 6f);
                return;
            }
            RestartFromCheckpoint(pick);
        }

        private void CheckpointMessage(string text)
        {
            _cpStatus.text = text;
            if (!TabShowing) Ctx.Notice.Show(text, 6f);
        }

        private void RestartFromCheckpoint(int index)
        {
            if (Ctx.Run.Refuse("savestates", "restart from checkpoint")) { CheckpointMessage(Ctx.Run.RefusedText("Restart from checkpoint")); return; }
            if (!Enabled) { CheckpointMessage("Practice mode is off - turn it on (F9) to restart from a checkpoint."); return; }
            if (_segment == null) { CheckpointMessage("Go to a timed segment first."); return; }
            if (_savestates == null) { CheckpointMessage("No savestate module."); return; }
            if (_cpRestoring || _savestates.Busy) { CheckpointMessage("A savestate action is still running."); return; }

            string error;
            CheckpointState st = _savestates.ReadCheckpointState(_segment, index, out error);
            string why = st == null ? error : st.Check(_segment.RouteFingerprint(), _segment.Checkpoints.Count);
            if (why != null) { CheckpointMessage("Checkpoint " + (index + 1) + ": " + why + "."); _cpDirty = true; return; }

            // The run in progress ends here, as a restart's does.
            _autoRestartAt = 0f;
            if (_recorder.State == RunRecorder.RunState.Running)
                Ctx.Log.LogInfo("Run '" + _segment.Id + "': aborted - restarting from checkpoint " + (index + 1) + ".");
            KeepFailed();
            RecordUnfinished("restart from checkpoint");
            _recorder.Abort();
            _hasDelta = false;
            ClearRunPreview();
            _splitsDirty = true;

            Segment seg = _segment;
            _cpRestoring = true;
            _cpLast = index;
            _cpStatus.text = "Restoring checkpoint " + (index + 1) + "...";
            Ctx.Log.LogInfo("Run '" + seg.Id + "': restoring checkpoint " + (index + 1) + "/" + seg.Checkpoints.Count + "'s state (captured " +
                            st.CapturedUtc.ToString("yyyy-MM-dd HH:mm:ss") + " UTC, clock " + Format(st.ResumeAt) + ").");
            _savestates.RestoreCheckpointState(seg, index, delegate(string err)
            {
                _cpRestoring = false;
                if (!ReferenceEquals(_segment, seg) || !Enabled) return;
                if (err != null)
                {
                    Ctx.Log.LogWarning("Run '" + seg.Id + "': checkpoint " + (index + 1) + " not restored - " + err + ".");
                    CheckpointMessage("Checkpoint " + (index + 1) + " not restored: " + err + ".");
                    ArmRun("checkpoint restore failed");
                    return;
                }
                ResumeFrom(st);
            });
        }

        private void ResumeFrom(CheckpointState st)
        {
            ArmRun("checkpoint restore");   // a fresh attempt on this segment and route

            _baseline.Load(st.Baseline);
            _splits.Clear();
            _splits.AddRange(st.Splits);
            _auto.Begin(_live);
            Vector3 pos = PlayerPosition();
            _recorder.Resume(pos, st.ResumeAt);
            ReplayRunStarted();
            _sequence.Resume(_segment.Checkpoints, _segment.End, st.Index + 1);
            ResetSplits();
            for (int i = 0; i < st.Splits.Length && i < _times.Length; i++) { _times[i] = st.Splits[i]; _loadsAt[i] = 0f; }
            _resumedFrom = st.Index;
            _resumeSkipDt = true;
            // The delta's search starts where the comparison was at this time.
            _deltaHint = 0;
            if (_reference != null)
                while (_deltaHint + 1 < _reference.Samples.Count && _reference.Samples[_deltaHint + 1].T <= st.ResumeAt) _deltaHint++;
            _ghostHint = 0;

            _status = "running from checkpoint " + (st.Index + 1) + "/" + _segment.Checkpoints.Count + " at " + Format(st.ResumeAt);
            _cpStatus.text = "Running from checkpoint " + (st.Index + 1) + " (clock " + Format(st.ResumeAt) + ") - practice, never a PB.";
            Ctx.Log.LogInfo("Run '" + _segment.Id + "': resumed from checkpoint " + (st.Index + 1) + "/" + _segment.Checkpoints.Count +
                            " at " + Format(st.ResumeAt) + " - practice: not saved as a run, its live segments can be golds.");
        }

        /// The end of a resumed run: its last segment may be a gold; the run
        /// itself is not kept.
        private void FinishResumedRun(Attempt done)
        {
            FinishSplits(done);
            RecordPracticeSegment(_times.Length - 1);
            int from = _resumedFrom;
            _resumedFrom = -1;
            _status = "finished from checkpoint " + (from + 1) + ": " + Format(done.Duration) + " (practice - not a PB)";
            SelectReference();
            ClearRunPreview();
            Ctx.Log.LogInfo("Run '" + done.AnchorLabel + "': finished from checkpoint " + (from + 1) + " at " + Format(done.Duration) +
                            " - practice, not saved as a run.");
            if (_autoRestart.Value && _segment != null)
            {
                Ctx.Notice.Show(Format(done.Duration) + "   (from checkpoint " + (from + 1) + ")", 1.2f);
                _autoRestartAt = Time.unscaledTime + AutoRestartDelay;
                _autoRestartCheckpoint = from;
            }
        }

        // --- the Runs tab section ----------------------------------------------

        private void RefreshCheckpointText()
        {
            if (!_cpDirty && !(TabShowing && Time.unscaledTime >= _cpNextRefresh)) return;
            if (!_cpDirty && !TabShowing) return;
            RefreshCheckpointRows();
        }

        private void RefreshCheckpointRows()
        {
            _cpDirty = false;
            _cpNextRefresh = Time.unscaledTime + 5f;   // "today" / files captured by another run
            _cpUsable.Clear();

            Segment s = _segment;
            int cps = s != null ? s.Checkpoints.Count : 0;
            _cpRows = cps;
            string key = s != null ? s.Id + "|" + _armedRoute : "";
            if (key != _cpBuiltFor) { _cpBuiltFor = key; _cpStatus.text = ""; }

            if (s == null) { _cpHeader.text = ""; return; }
            if (cps == 0) { _cpHeader.text = "Checkpoint states: this segment has no checkpoints."; return; }

            List<int> onDisk = _savestates != null ? _savestates.CheckpointStatesOf(s) : new List<int>();
            if (_cpRowUsable.Length != cps) _cpRowUsable = new bool[cps];
            while (_cpButtons.Count < cps) { _cpButtons.Add(new GUIContent("")); _cpLines.Add(new GUIContent("")); }

            string route = s.RouteFingerprint();
            System.DateTime now = System.DateTime.Now;
            for (int i = 0; i < cps; i++)
            {
                _cpButtons[i].text = "Restart from " + (i + 1);
                _cpRowUsable[i] = false;
                string name = s.SplitName(i);
                if (!onDisk.Contains(i)) { _cpLines[i].text = name + ": no state yet"; continue; }

                string error;
                CheckpointState st = _savestates.ReadCheckpointState(s, i, out error);
                string why = st == null ? error : st.Check(route, cps);
                if (why != null) { _cpLines[i].text = name + ": " + why; continue; }

                _cpRowUsable[i] = true;
                _cpUsable.Add(i);
                string when = RunDates.WhenUtc(st.CapturedUtc, now);
                _cpLines[i].text = name + ": clock " + Format(st.ResumeAt) + (when.Length > 0 ? ", captured " + when : "") +
                                   (i == _cpLast ? "  (the hotkey's)" : "");
            }
            _cpHeader.text = "Checkpoint states: " + _cpUsable.Count + " of " + cps + " captured" +
                             (_cpUsable.Count == 0 && !_cpCapture.Value ? " - tick Capture at checkpoints and run the segment." : ".");
        }

        /// Below the run mode section; returns the new y.
        private float DrawCheckpointSection(float y, float w)
        {
            if (_segment == null || _cpCapture == null) return y;
            // No checkpoints, nothing to capture: one line says why.
            if (_cpRows == 0) return y + UiText.Draw(0, y, w, _cpHeader) + 4f;

            bool cap = GUI.Toggle(new Rect(0, y, w, 20), _cpCapture.Value, " Capture at checkpoints (practice runs)");
            if (cap != _cpCapture.Value) { _cpCapture.Value = cap; _cpDirty = true; }
            y += 22f;
            y += UiText.DrawDim(0, y, w, _cpNote);
            y += UiText.Draw(0, y, w, _cpHeader);

            int rows = Mathf.Min(_cpRows, Mathf.Min(_cpButtons.Count, _cpRowUsable.Length));
            for (int i = 0; i < rows; i++)
            {
                bool was = GUI.enabled;
                GUI.enabled = was && _cpRowUsable[i] && !_cpRestoring;
                if (GUI.Button(new Rect(0, y, 120, 22), _cpButtons[i])) RestartFromCheckpoint(i);
                GUI.enabled = was;
                float h = UiText.Draw(126, y + 2, w - 126, _cpLines[i]);
                y += Mathf.Max(24f, h + 4f);
            }

            if (_cpUsable.Count > 0)
            {
                if (GUI.Button(new Rect(0, y, 200, 22), "Delete checkpoint states"))
                {
                    int n = _savestates != null ? _savestates.DeleteCheckpointStates(_segment) : 0;
                    _cpStatus.text = n + " checkpoint state file(s) deleted.";
                    _cpLast = -1;
                    _cpDirty = true;
                }
                y += 24f;
            }
            y += UiText.Draw(0, y, w, _cpStatus);
            return y + 6f;
        }
    }
}
