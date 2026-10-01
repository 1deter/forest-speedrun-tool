using System.Collections;
using System.Collections.Generic;
using ForestOverlay.Core;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Other runners' PBs as comparisons (v0.24.155; website Next 1): the
    // site's board for the armed segment + route (Data/SiteBoard) adds each
    // other runner's best to "Compare to". Picked, their .run is fetched
    // once and becomes the reference - table, delta, ghost and lines race
    // it as they race your own PB.
    //
    // Kept apart (author, 2026-09-27): their runs never join _attempts, so
    // your PB, golds and sum of best stay your own. Only the same route
    // compares - the board is asked for by route fingerprint.
    // ------------------------------------------------------------------
    public sealed partial class PracticeRunModule
    {
        private const float BoardMaxAge = 120f;   // re-read on an arm after this

        private readonly List<BoardEntry> _others = new List<BoardEntry>();   // the website's + local, merged
        private readonly List<BoardEntry> _siteOthers = new List<BoardEntry>();
        // Other runners' bests from imported .foseg files (v0.24.190):
        // offline, RunId negative, their run already here.
        private List<BoardEntry> _localOthers = new List<BoardEntry>();
        private readonly List<Attempt> _localRuns = new List<Attempt>();
        private string _boardKey = "";            // segment|route the list is for
        private string _boardFetching;            // key of the request in flight
        private float _boardFetchedAt = -999f;
        private string _boardState = "";

        private int _runnerPick = -1;              // index into _others
        private string _runnerPickId;              // kept across a re-read
        private readonly Dictionary<long, Attempt> _runnerRuns = new Dictionary<long, Attempt>();
        private long _runnerRunFetching = -1;

        private readonly GUIContent _boardText = new GUIContent("");
        private readonly GUIContent _runnerPickText = new GUIContent("");

        private string SiteUrl { get { return _upload != null ? _upload.SiteUrl : null; } }

        /// On every arm: a new segment / route reads the board at once, the
        /// same one again after BoardMaxAge (new PBs by others).
        private void MaybeFetchBoard()
        {
            if (_segment == null) return;
            string key = _segment.Id + "|" + _armedRoute;
            if (key != _boardKey) ClearBoard(key);
            else if (Time.unscaledTime < _boardFetchedAt + BoardMaxAge) return;
            FetchBoard();
        }

        private void FetchBoard()
        {
            if (_segment == null || _boardFetching == _boardKey) return;
            if (string.IsNullOrEmpty(SiteUrl)) { _boardState = "Other runners: no website address set."; return; }
            _boardFetching = _boardKey;
            _boardFetchedAt = Time.unscaledTime;
            if (_others.Count == 0) _boardState = "Other runners: asking the website...";
            Ctx.Runner.StartCoroutine(FetchBoardRoutine(_boardKey, SiteBoard.Url(SiteUrl, _segment.Id, _armedRoute), SplitRows));
        }

        private IEnumerator FetchBoardRoutine(string key, string url, int rows)
        {
            long code = 0;
            string body = null, error = null;
            yield return Ctx.Runner.StartCoroutine(WebRequest.Send("GET", url, null, null, null, 20f,
                delegate(long c, string b, string e) { code = c; body = b; error = e; }));
            if (_boardFetching == key) _boardFetching = null;
            if (key != _boardKey) yield break;   // moved on meanwhile

            List<BoardEntry> board = code == 200 ? SiteBoard.Parse(body) : null;
            if (board == null)
            {
                string why = code == 0 ? (error ?? "no answer") : "answer " + code;
                _boardState = "Other runners: could not read the website (" + why + ")" +
                              (_others.Count > 0 ? " - showing the last list." : ".");
                Ctx.Log.LogWarning("Site board '" + key + "': " + why + ".");
                yield break;
            }

            _siteOthers.Clear();
            _siteOthers.AddRange(SiteBoard.Others(board, RunnerIdNow(), rows));
            Ctx.Log.LogInfo("Site board '" + key + "': " + board.Count + " runner(s), " + _siteOthers.Count + " to compare with.");
            _boardState = _siteOthers.Count == 0
                ? (board.Count == 0 ? "Other runners: no runs of this version of the spot on the website yet."
                                    : "Other runners: only your own runs of this version so far.")
                : "Other runners: " + _siteOthers.Count + " on this version of the spot.";
            MergeOthers();
        }

        /// Imported runners for the segment just loaded (LoadAttemptsFor).
        private void SetLocalOthers(List<Attempt> others, int checkpoints)
        {
            _localOthers = AttemptOwners.OthersBest(others, checkpoints, _localRuns);
            MergeOthers();
        }

        /// _others = the website's list + the imported ones; the picked
        /// runner kept through it.
        private void MergeOthers()
        {
            _others.Clear();
            _others.AddRange(AttemptOwners.Merge(_siteOthers, _localOthers));
            for (int i = 0; i < _localOthers.Count && i < _localRuns.Count; i++) _runnerRuns[_localOthers[i].RunId] = _localRuns[i];

            _runnerPick = -1;
            for (int i = 0; i < _others.Count; i++)
                if (_others[i].RunnerId == _runnerPickId) _runnerPick = i;
            if (_referenceKind == Reference.Runner && _runnerPick < 0) _referenceKind = Reference.Best;
            RefreshRunnerPickText();
            SelectReference();
            _splitsDirty = true;
        }

        private void ClearBoard(string key)
        {
            _boardKey = key;
            _siteOthers.Clear();
            _runnerRuns.Clear();
            _boardState = "";
            MergeOthers();   // the imported runners stay
        }

        private BoardEntry PickedRunner
        {
            get { return _runnerPick >= 0 && _runnerPick < _others.Count ? _others[_runnerPick] : null; }
        }

        /// The picked runner's run, or null while it downloads.
        private Attempt RunnerReference()
        {
            BoardEntry e = PickedRunner;
            if (e == null) return null;
            Attempt a;
            if (_runnerRuns.TryGetValue(e.RunId, out a)) return a;
            if (e.RunId > 0 && _runnerRunFetching != e.RunId && !string.IsNullOrEmpty(SiteUrl))
            {
                _runnerRunFetching = e.RunId;
                Ctx.Runner.StartCoroutine(FetchRunRoutine(_boardKey, e));
            }
            return null;
        }

        private IEnumerator FetchRunRoutine(string key, BoardEntry e)
        {
            long code = 0;
            string body = null, error = null;
            yield return Ctx.Runner.StartCoroutine(WebRequest.Send("GET", SiteBoard.RunFileUrl(SiteUrl, e.RunId), null, null, null, 30f,
                delegate(long c, string b, string err) { code = c; body = b; error = err; }));
            if (_runnerRunFetching == e.RunId) _runnerRunFetching = -1;
            if (key != _boardKey) yield break;

            Attempt a = code == 200 && body != null ? AttemptFormat.Parse(body.Replace("\r\n", "\n").Split('\n')) : null;
            if (a == null || a.Samples.Count == 0)
            {
                string why = code == 0 ? (error ?? "no answer") : code == 200 ? "not a run file" : "answer " + code;
                _boardState = "Could not fetch " + e.Name + "'s run (" + why + ") - the splits compare, the ghost does not.";
                Ctx.Log.LogWarning("Site run " + e.RunId + " (" + e.Name + "): " + why + ".");
                yield break;
            }

            _runnerRuns[e.RunId] = a;
            Ctx.Log.LogInfo("Site run " + e.RunId + " (" + e.Name + ", " + Format(a.Duration) + "): " + a.Samples.Count +
                            " samples - the reference for '" + key + "'.");
            if (ReferenceEquals(PickedRunner, e) && _referenceKind == Reference.Runner) SelectReference();
        }

        private void PickRunner(int index)
        {
            if (_others.Count == 0) return;
            _runnerPick = (index % _others.Count + _others.Count) % _others.Count;
            _runnerPickId = _others[_runnerPick].RunnerId;
            _referenceKind = Reference.Runner;
            RefreshRunnerPickText();
            SelectReference();
            _splitsDirty = true;
        }

        private void RefreshRunnerPickText()
        {
            BoardEntry e = PickedRunner ?? (_others.Count > 0 ? _others[0] : null);
            _runnerPickText.text = e == null ? "" :
                (PickedRunner == null ? 1 : _runnerPick + 1) + "/" + _others.Count + "  " + e.Name +
                (e.RunId < 0 ? " (file)" : "") + "  " + Format(e.Duration);
            _boardText.text = _boardState;
        }

        /// The Runs tab's row under "Compare to"; returns the new y.
        private float DrawRunnersSection(float y, float w)
        {
            if (_segment == null) return y;
            if (!ReferenceEquals(_boardText.text, _boardState)) _boardText.text = _boardState;

            if (_others.Count > 0)
            {
                bool on = GUI.Toggle(new Rect(0, y, 130, 20), _referenceKind == Reference.Runner, " another runner");
                if (on && _referenceKind != Reference.Runner) PickRunner(_runnerPick < 0 ? 0 : _runnerPick);
                if (GUI.Button(new Rect(134, y, 24, 20), "<")) PickRunner(_runnerPick < 0 ? _others.Count - 1 : _runnerPick - 1);
                if (GUI.Button(new Rect(162, y, 24, 20), ">")) PickRunner(_runnerPick < 0 ? 0 : _runnerPick + 1);
                GUI.Label(new Rect(192, y, Mathf.Max(40f, w - 282f), 20), _runnerPickText);
                if (GUI.Button(new Rect(w - 80, y, 80, 20), "Refresh")) FetchBoard();
                y += 24f;
            }
            else if (_boardFetching == null && !string.IsNullOrEmpty(_boardState))
            {
                if (GUI.Button(new Rect(w - 80, y, 80, 20), "Refresh")) FetchBoard();
                y += UiText.Draw(0, y, w - 86f, _boardText);
                return y;
            }
            if (_boardState.Length > 0) y += UiText.Draw(0, y, w, _boardText);
            return y;
        }
    }
}
