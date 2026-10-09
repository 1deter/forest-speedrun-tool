using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using ForestOverlay.Core;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Run mode attempts to the website (docs/run-mode.md, phase 2: codes and
    // receipts). Modules/RunModeModule makes the chain; this sends it:
    //
    //   - the start: asks the site for a nonce (tried for ~25 s - a later
    //     nonce leaves the start to the video's codes);
    //   - a checkpoint about once a minute: the head after a step, which
    //     the site times by its own clock (dropped when the site is not
    //     reachable - the log carries every step anyway);
    //   - the log when the attempt ends (finished, reset, closed), through
    //     an outbox (uploads/attempts/) so an offline attempt goes up later,
    //     in order. The link exists at once - the id is made here.
    //
    // Sent attempts are listed in uploads/attempts/sent.txt (id, verdict,
    // when) for the Runs tab's links; one deleted on the site leaves it
    // when the tab next opens (Data/SentAttempts, T-0144).
    // ------------------------------------------------------------------
    public sealed partial class RunUploadModule
    {
        private static readonly GUIContent SendTip = new GUIContent("Each run mode attempt's codes and log go to forest.deter.cloud, where moderators check runs.");
        private const string AttemptExt = ".attempt";
        private const int RecentShown = 5;

        private ConfigEntry<bool> _attemptsOn;
        private string _attemptDir, _attemptRefusedDir, _sentList;
        private bool _attemptBusy, _checkpointBusy;
        private float _nextAttemptTry;
        private int _attemptFailures;
        private int _attemptSeq;

        /// The last few attempts, newest first: id + what became of it.
        private readonly List<string> _recentIds = new List<string>();
        private readonly List<GUIContent> _recentText = new List<GUIContent>();
        private bool _recentDirty = true;
        private int _recentWaiting;   // the first n of _recentIds are still in the outbox
        private string _attemptState = "";

        /// Run mode's attempts reach the site (nonce, checkpoints, logs).
        public bool AttemptsOn { get { return _attemptsOn != null && _attemptsOn.Value && !_tokenBad; } }

        // What became of each attempt's log this session, for the results
        // panel (Modules/PracticeRunModule.Results): words per id, and a
        // count that moves on every change so the panel rebuilds only then.
        private readonly Dictionary<string, string> _attemptWords = new Dictionary<string, string>();

        public int AttemptWordsVersion { get; private set; }

        /// "" when this session has not queued that attempt's log.
        public string AttemptUploadWords(string attemptId)
        {
            string w;
            return attemptId != null && _attemptWords.TryGetValue(attemptId, out w) ? w : "";
        }

        /// The attempt's page on the site (exists once its log is in).
        public string AttemptLink(string attemptId)
        {
            return SiteProtocol.AttemptUrl(_url.Value, attemptId);
        }

        private void SetAttemptWords(string attemptId, AttemptUpload state, string detail)
        {
            _attemptWords[attemptId] = RunResults.UploadWords(state, detail);
            AttemptWordsVersion++;
        }

        private void InitAttempts(ConfigFile config, string root)
        {
            _attemptsOn = config.Bind("Site", "SendAttempts", true,
                "Run mode: send each attempt's codes and log to the website (a start code, a checkpoint a minute, the log " +
                "when it ends), so anyone can check a run's video against it. Off = attempts are checked by the video only.");
            _attemptDir = Path.Combine(root, "attempts");
            _attemptRefusedDir = Path.Combine(_attemptDir, "refused");
            _sentList = Path.Combine(_attemptDir, "sent.txt");
            _openDir = Path.Combine(_attemptDir, "open");
            _nextAttemptTry = Time.unscaledTime + 10f;
            RecoverOpenAttempts();
        }

        // --- an attempt in progress, on disk (the game can die mid-run) ----------------

        private string _openDir;

        /// The log so far, rewritten every few seconds while the attempt runs.
        public void WriteOpenAttempt(string attemptId, string text)
        {
            try
            {
                Directory.CreateDirectory(_openDir);
                File.WriteAllText(Path.Combine(_openDir, attemptId + AttemptExt), text, new UTF8Encoding(false));
            }
            catch (Exception ex) { Ctx.Log.LogWarning("Attempts: could not write the open log of " + attemptId + ": " + ex.Message); }
        }

        public void ClearOpenAttempt(string attemptId)
        {
            TryDelete(Path.Combine(_openDir, attemptId + AttemptExt));
        }

        // An attempt the game never ended (a crash, a killed process): its
        // log goes out as it is - the site reads "no end".
        private void RecoverOpenAttempts()
        {
            try
            {
                if (!Directory.Exists(_openDir)) return;
                string[] files = Directory.GetFiles(_openDir, "*" + AttemptExt);
                for (int i = 0; i < files.Length; i++)
                {
                    string id = Path.GetFileNameWithoutExtension(files[i]);
                    if (AttemptsOn) QueueAttemptLog(id, File.ReadAllText(files[i]));
                    TryDelete(files[i]);
                    Ctx.Log.LogInfo("Attempts: " + id + " never ended (the game closed during it) - its log is " +
                                    (AttemptsOn ? "queued as it is." : "not sent (sending attempts is off; run-reports keeps it)."));
                }
            }
            catch (Exception ex) { Ctx.Log.LogWarning("Attempts: open logs not recovered: " + ex.Message); }
        }

        // --- the start and the checkpoints ---------------------------------------

        /// Asks for the attempt's nonce; `done` gets it, or null when the site
        /// could not be reached in time (the attempt goes on offline).
        public void AttemptStart(string attemptId, string category, string spot, Action<string> done)
        {
            if (!AttemptsOn) { done(null); return; }
            Ctx.Runner.StartCoroutine(StartAttempt(attemptId, category, spot, done));
        }

        private IEnumerator StartAttempt(string attemptId, string category, string spot, Action<string> done)
        {
            string baseUrl = SiteProtocol.TrimUrl(_url.Value);
            float giveUp = Time.unscaledTime + 25f;
            byte[] body = Encoding.UTF8.GetBytes(SiteProtocol.AttemptStartBody(attemptId, category, spot));
            while (true)
            {
                if (string.IsNullOrEmpty(_token.Value))
                {
                    bool ok = false;
                    yield return Ctx.Runner.StartCoroutine(Register(baseUrl, "", delegate(bool r) { ok = r; }));
                    if (!ok) { done(null); yield break; }
                }

                long code = 0; string answer = null, error = null;
                yield return Ctx.Runner.StartCoroutine(WebRequest.Send("POST", baseUrl + "/api/attempts", body, "application/json",
                    _token.Value, 10f, delegate(long c, string b, string e) { code = c; answer = b; error = e; }));

                string nonce = code == 200 ? SiteProtocol.Field(answer, "nonce") : null;
                if (!string.IsNullOrEmpty(nonce)) { done(nonce); yield break; }
                if (code == 401) _tokenBad = true;
                string why = error ?? "HTTP " + code + " " + (SiteProtocol.Field(answer, "error") ?? "");
                if (SiteProtocol.Classify(code) != UploadOutcome.RetryLater || Time.unscaledTime + 5f > giveUp)
                {
                    _attemptState = "attempt " + attemptId + " started offline (" + why.Trim() + ")";
                    Ctx.Log.LogWarning("Attempts: no start code for " + attemptId + " - " + why.Trim() + "; the attempt is offline.");
                    done(null);
                    yield break;
                }
                float until = Time.unscaledTime + 5f;
                while (Time.unscaledTime < until) yield return null;
            }
        }

        /// The head after a step, during the attempt. One at a time; a
        /// failed one is not retried (the next minute's covers it).
        public void AttemptCheckpoint(string attemptId, int step, string head)
        {
            if (!AttemptsOn || _checkpointBusy) return;
            _checkpointBusy = true;
            Ctx.Runner.StartCoroutine(SendCheckpoint(attemptId, step, head));
        }

        private IEnumerator SendCheckpoint(string attemptId, int step, string head)
        {
            long code = 0; string answer = null, error = null;
            yield return Ctx.Runner.StartCoroutine(WebRequest.Send("POST",
                SiteProtocol.TrimUrl(_url.Value) + "/api/attempts/" + attemptId + "/checkpoints",
                Encoding.UTF8.GetBytes(SiteProtocol.CheckpointBody(step, head)), "application/json", _token.Value, 20f,
                delegate(long c, string b, string e) { code = c; answer = b; error = e; }));
            _checkpointBusy = false;
            if (code == 200) Ctx.Log.LogInfo("Attempts: checkpoint " + attemptId + " step " + step + " sent.");
            else Ctx.Log.LogWarning("Attempts: checkpoint " + attemptId + " step " + step + " not taken: " +
                                    (error ?? "HTTP " + code + " " + (SiteProtocol.Field(answer, "error") ?? "")) + ".");
        }

        // --- the outbox -------------------------------------------------------------

        /// The attempt's finished log, to send now or later.
        public void QueueAttemptLog(string attemptId, string text)
        {
            try
            {
                Directory.CreateDirectory(_attemptDir);
                string name = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + (_attemptSeq++).ToString("000") + "_" + attemptId + AttemptExt;
                File.WriteAllText(Path.Combine(_attemptDir, name), text, new UTF8Encoding(false));
                _nextAttemptTry = 0f;
                _recentDirty = true;
                SetAttemptWords(attemptId, AttemptUpload.Waiting, null);
            }
            catch (Exception ex) { Ctx.Log.LogWarning("Attempts: could not queue " + attemptId + ": " + ex.Message); }
        }

        private void TickAttempts()
        {
            if (_recentDirty) RebuildRecent();
            if (_recentCheckDue && !_recentCheckBusy && Time.frameCount <= _recentDrawnFrame + 2) CheckRecent();
            if (_attemptBusy || !AttemptsOn || Time.unscaledTime < _nextAttemptTry) return;
            _nextAttemptTry = Time.unscaledTime + 5f;
            string next = OldestAttempt();
            // Empty: again in a minute (QueueAttemptLog sets it back to 0).
            if (next == null) { _nextAttemptTry = Time.unscaledTime + EmptyQueueRecheck; return; }
            _attemptBusy = true;
            Ctx.Runner.StartCoroutine(PumpAttempt(next));
        }

        private string OldestAttempt()
        {
            if (!Directory.Exists(_attemptDir)) return null;
            string[] files = Directory.GetFiles(_attemptDir, "*" + AttemptExt);
            if (files.Length == 0) return null;
            Array.Sort(files, StringComparer.Ordinal);
            return files[0];
        }

        private static string IdOfFile(string path)
        {
            string n = Path.GetFileNameWithoutExtension(path);
            int k = n.LastIndexOf("_a-", StringComparison.Ordinal);
            return k >= 0 ? n.Substring(k + 1) : n;
        }

        private IEnumerator PumpAttempt(string path)
        {
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception ex) { Ctx.Log.LogWarning("Attempts: cannot read " + Path.GetFileName(path) + ": " + ex.Message); _attemptBusy = false; yield break; }
            string id = IdOfFile(path);
            string baseUrl = SiteProtocol.TrimUrl(_url.Value);

            if (string.IsNullOrEmpty(_token.Value))
            {
                bool ok = false;
                yield return Ctx.Runner.StartCoroutine(Register(baseUrl, "", delegate(bool r) { ok = r; }));
                if (!ok) { _attemptBusy = false; yield break; }
            }

            long code = 0; string answer = null, error = null;
            yield return Ctx.Runner.StartCoroutine(WebRequest.Send("POST", baseUrl + "/api/attempts/" + id + "/log",
                Encoding.UTF8.GetBytes(text), "text/plain; charset=utf-8", _token.Value, RequestTimeout,
                delegate(long c, string b, string e) { code = c; answer = b; error = e; }));

            switch (SiteProtocol.Classify(code))
            {
                case UploadOutcome.Done:
                    string verdict = SiteProtocol.Field(answer, "verdict") ?? "?";
                    TryDelete(path);
                    AppendSent(id, verdict);
                    _sentThere.Add(id);
                    _attemptFailures = 0;
                    _nextAttemptTry = 0f;
                    _attemptState = "attempt " + id + " sent (" + verdict + ")";
                    SetAttemptWords(id, AttemptUpload.Sent, verdict);
                    Ctx.Log.LogInfo("Attempts: " + id + " log sent - " + verdict + Why(answer) + ".");
                    break;
                case UploadOutcome.TokenBad:
                    _tokenBad = true;
                    _attemptState = "the site does not know this install's token - clear Token in the config to register again";
                    Ctx.Log.LogWarning("Attempts: token refused (401); attempt logs wait.");
                    SetAttemptWords(id, AttemptUpload.TokenBad, null);
                    break;
                case UploadOutcome.RetryLater:
                    _attemptFailures++;
                    float wait = SiteProtocol.RetryDelay(_attemptFailures);
                    _nextAttemptTry = Time.unscaledTime + wait;
                    _attemptState = "site not reachable (" + (error ?? "HTTP " + code) + ") - attempt logs wait, retrying in " + Mathf.RoundToInt(wait) + " s";
                    Ctx.Log.LogWarning("Attempts: " + id + " log not sent: " + (error ?? "HTTP " + code) + "; retry in " + Mathf.RoundToInt(wait) + " s.");
                    SetAttemptWords(id, AttemptUpload.Retrying, "site not reachable (" + (error ?? "HTTP " + code) + "), retrying");
                    break;
                default:
                    // 409 = another log is in for this id; 400 = it does not read.
                    string msg = "HTTP " + code + ": " + (SiteProtocol.Field(answer, "error") ?? answer);
                    try
                    {
                        Directory.CreateDirectory(_attemptRefusedDir);
                        string to = Path.Combine(_attemptRefusedDir, Path.GetFileName(path));
                        if (File.Exists(to)) File.Delete(to);
                        File.Move(path, to);
                        File.WriteAllText(to + ".txt", msg + "\n");
                    }
                    catch (Exception) { TryDelete(path); }
                    AppendSent(id, "refused");
                    _nextAttemptTry = 0f;
                    _attemptState = "the site refused attempt " + id + ": " + msg;
                    SetAttemptWords(id, AttemptUpload.Refused, msg);
                    Ctx.Log.LogWarning("Attempts: " + id + " log refused - " + msg + " (moved to uploads/attempts/refused).");
                    break;
            }
            _recentDirty = true;
            _attemptBusy = false;
        }

        private static string Why(string answer)
        {
            int k = answer != null ? answer.IndexOf("\"why\":[", StringComparison.Ordinal) : -1;
            if (k < 0) return "";
            int end = answer.IndexOf(']', k);
            return end < 0 ? "" : " " + answer.Substring(k + 6, end - k - 5);
        }

        private void AppendSent(string id, string verdict)
        {
            try { File.AppendAllText(_sentList, id + "|" + verdict + "|" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n"); }
            catch (Exception) { }
        }

        // --- the Runs tab: recent attempts with their links ----------------------------

        private void RebuildRecent()
        {
            _recentDirty = false;
            _recentCheckDue = true;
            _recentIds.Clear();
            List<string> rows = new List<string>();

            // Waiting first (newest first), then sent.
            if (Directory.Exists(_attemptDir))
            {
                string[] files = Directory.GetFiles(_attemptDir, "*" + AttemptExt);
                Array.Sort(files, StringComparer.Ordinal);
                for (int i = files.Length - 1; i >= 0 && _recentIds.Count < RecentShown; i--)
                {
                    _recentIds.Add(IdOfFile(files[i]));
                    rows.Add(IdOfFile(files[i]) + " - waiting to be sent");
                }
            }
            _recentWaiting = _recentIds.Count;
            try
            {
                if (File.Exists(_sentList))
                {
                    string[] lines = File.ReadAllLines(_sentList);
                    for (int i = lines.Length - 1; i >= 0 && _recentIds.Count < RecentShown; i--)
                    {
                        string[] p = lines[i].Split('|');
                        if (p.Length < 3 || _recentIds.Contains(p[0])) continue;
                        _recentIds.Add(p[0]);
                        rows.Add(p[0] + " - " + VerdictWords(p[1]) + " (" + p[2] + ")");
                    }
                }
            }
            catch (Exception) { }

            while (_recentText.Count < rows.Count) _recentText.Add(new GUIContent(""));
            for (int i = 0; i < rows.Count; i++) _recentText[i].text = rows[i];
        }

        private static string VerdictWords(string v)
        {
            return RunResults.VerdictWords(v);
        }

        /// Run mode's section: the switch, the state and the recent attempts'
        /// links. Returns the new y.
        public float DrawAttempts(float y, float w)
        {
            // Not drawn for a few frames = the Runs tab (or the window) just
            // opened: the listed attempts are asked about again.
            if (Time.frameCount > _recentDrawnFrame + 2) { _askedThisOpen.Clear(); _recentCheckDue = true; }
            _recentDrawnFrame = Time.frameCount;
            bool on = GUI.Toggle(new Rect(0, y, w, 20), _attemptsOn.Value, " Send run mode attempts to the website");
            UiKit.Hint(new Rect(0, y, w, 20), SendTip);
            if (on != _attemptsOn.Value) { _attemptsOn.Value = on; _tokenBad = false; _nextAttemptTry = 0f; }
            y += 22f;
            if (_attemptState.Length > 0)
            {
                if (_attemptStateText.text != _attemptState) _attemptStateText.text = _attemptState;
                y += UiText.Draw(0, y, w, _attemptStateText);
            }
            for (int i = 0; i < _recentIds.Count && i < _recentText.Count; i++)
            {
                if (GUI.Button(new Rect(0, y + 1, 80, 20), "Copy link"))
                {
                    GUIUtility.systemCopyBuffer = SiteProtocol.AttemptUrl(_url.Value, _recentIds[i]);
                    _attemptState = "copied the link to attempt " + _recentIds[i];
                }
                y += Mathf.Max(22f, UiText.Draw(86, y + 2, w - 86, _recentText[i]) + 4f);
            }
            return y + 4f;
        }

        private readonly GUIContent _attemptStateText = new GUIContent("");

        // --- attempts deleted on the site (T-0144) ---------------------------------------

        private readonly HashSet<string> _sentThere = new HashSet<string>();      // 200 this session
        private readonly HashSet<string> _askedThisOpen = new HashSet<string>();  // since the tab opened
        private int _recentDrawnFrame = -100;
        private bool _recentCheckDue, _recentCheckBusy;

        /// While the Runs tab shows the list: one GET per listed attempt not
        /// known to be on the site (at most RecentShown), one at a time.
        private void CheckRecent()
        {
            _recentCheckDue = false;
            List<string> ids = SentAttempts.ToCheck(_recentIds, _recentWaiting, _sentThere, _askedThisOpen);
            if (ids.Count == 0) return;
            for (int i = 0; i < ids.Count; i++) _askedThisOpen.Add(ids[i]);
            _recentCheckBusy = true;
            Ctx.Runner.StartCoroutine(CheckSent(ids));
        }

        private IEnumerator CheckSent(List<string> ids)
        {
            string baseUrl = SiteProtocol.TrimUrl(_url.Value);
            for (int i = 0; i < ids.Count; i++)
            {
                long code = 0; string answer = null;
                yield return Ctx.Runner.StartCoroutine(WebRequest.Send("GET", baseUrl + "/api/attempts/" + ids[i], null, null,
                    null, 10f, delegate(long c, string b, string e) { code = c; answer = b; }));
                SentCheck meaning = SentAttempts.Meaning(code, answer);
                if (meaning == SentCheck.There) _sentThere.Add(ids[i]);
                else if (meaning == SentCheck.Gone) DropSent(ids[i]);
                // Offline or the site in trouble: the rest stay listed too,
                // asked again the next time the tab opens.
                else break;
            }
            _recentCheckBusy = false;
        }

        private void DropSent(string id)
        {
            try
            {
                if (!File.Exists(_sentList)) return;
                string kept = SentAttempts.Without(File.ReadAllText(_sentList), id);
                if (kept == null) return;
                File.WriteAllText(_sentList, kept, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Ctx.Log.LogWarning("Attempts: " + id + " is deleted on the site but could not be taken off sent.txt: " + ex.Message);
                return;
            }
            _recentDirty = true;
            _attemptState = "attempt " + id + " was deleted on the website";
            Ctx.Log.LogInfo("Attempts: " + id + " was deleted on the site (404) - taken off the Runs tab's list (sent.txt).");
        }
    }
}
