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
    // Finished runs to forest.deter.cloud (docs/website.md; the author,
    // 2026-09-27: "once the site is live i don't see a reason not to
    // submit the runs automatically").
    //
    // Every finished attempt of a timed segment is written as a small .foseg
    // (the segment + that attempt) to config/ForestOverlay/uploads/pending/
    // first, so a closed game or a site that is down loses nothing; a
    // coroutine sends the pending files oldest first. The first upload
    // registers this install (runner id -> a token kept in the config).
    // A file the site refuses (another route version, ...) moves to
    // uploads/refused/ with the reason beside it; network trouble retries
    // with a growing delay. Answers are sorted by Data/SiteProtocol.
    //
    // No tab of its own: the Runs tab draws its section (DrawSection).
    // Writes no game state - not practice-only.
    // ------------------------------------------------------------------
    public sealed partial class RunUploadModule : OverlayModule
    {
        public override string Id { get { return "upload"; } }
        public override string DisplayName { get { return "Run uploads"; } }

        private const float RequestTimeout = 30f;
        private const int MaxBundleBytes = 3 * 1024 * 1024;

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<string> _url;
        private ConfigEntry<string> _token;

        private string _pendingDir, _refusedDir;
        private bool _busy;
        private float _nextTry;
        private int _failures;
        private bool _tokenBad;
        private int _uploaded;
        private string _state = "";
        private string _runnerId = "", _runnerName = "";
        private int _seq;

        /// The Runs module's current segment and its saved .run texts (for
        /// "Upload saved runs"); set by it.
        /// The website's address (Practice runs reads other runners' PBs there).
        public string SiteUrl { get { return _url != null ? _url.Value : null; } }

        public Func<Segment> CurrentSegment;
        public Func<List<string>> SavedRunTexts;
        /// Who this install uploads as (the Runs module's runner id / name).
        public Func<string> RunnerIdNow;
        public Func<string> RunnerNameNow;

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);
            _enabled = ctx.Config.Bind("Site", "UploadRuns", true,
                "Upload each finished timed run to the website (forest.deter.cloud): its path, split times and your runner name.");
            _url = ctx.Config.Bind("Site", "Url", SiteProtocol.DefaultUrl, "The website's address.");
            _token = ctx.Config.Bind("Site", "Token", "",
                "Given by the website on the first upload; it proves the runs are yours. Keep it private.");

            string root = Path.Combine(ctx.ConfigDirectory, "uploads");
            _pendingDir = Path.Combine(root, "pending");
            _refusedDir = Path.Combine(root, "refused");
            _deletesFile = Path.Combine(root, "deletes.txt");
            _state = _enabled.Value ? "on" : "off";
            _nextTry = Time.unscaledTime + 8f;   // not in the startup rush
            InitAttempts(ctx.Config, root);
        }

        // --- queueing ---------------------------------------------------------

        /// A finished attempt of `segment` (its .run text).
        public void Enqueue(Segment segment, string runText, string runnerId, string runnerName)
        {
            if (!_enabled.Value || segment == null || !segment.IsTimed || string.IsNullOrEmpty(segment.Id)) return;
            List<string> one = new List<string>(1);
            one.Add(runText);
            Write(segment, one, runnerId, runnerName);
        }

        /// Every saved run of the current segment (the button). The site
        /// keeps one copy of each, so pressing twice uploads nothing twice.
        public void EnqueueSaved()
        {
            Segment seg = CurrentSegment != null ? CurrentSegment() : null;
            if (seg == null || !seg.IsTimed) { _state = "pick a timed segment first (Practice tab, Go)"; return; }
            List<string> texts = SavedRunTexts != null ? SavedRunTexts() : null;
            if (texts == null || texts.Count == 0) { _state = "no saved runs for '" + seg.Name + "'"; return; }

            // In bundles of a few, so one big file never hits the size cap.
            int files = 0;
            for (int i = 0; i < texts.Count; i += 20)
            {
                Write(seg, texts.GetRange(i, Math.Min(20, texts.Count - i)), null, null);
                files++;
            }
            _nextTry = 0f;
            _state = texts.Count + " saved run(s) of '" + seg.Name + "' queued";
            Ctx.Log.LogInfo("Upload: " + texts.Count + " saved run(s) of '" + seg.Id + "' queued in " + files + " file(s).");
        }

        private void Write(Segment segment, List<string> runTexts, string runnerId, string runnerName)
        {
            if (!string.IsNullOrEmpty(runnerId)) { _runnerId = runnerId; _runnerName = runnerName ?? ""; }
            try
            {
                SegmentBundle b = new SegmentBundle();
                b.Segment = segment;
                b.Exported = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                b.PluginVersion = OverlayPlugin.PluginVersion;
                b.Attempts.AddRange(runTexts);
                Directory.CreateDirectory(_pendingDir);
                string name = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + (_seq++).ToString("000") + "_" +
                              Safe(segment.Id) + SegmentBundle.Extension;
                File.WriteAllText(Path.Combine(_pendingDir, name), b.Write(), new UTF8Encoding(false));
                _nextTry = 0f;
            }
            catch (Exception ex)
            {
                Ctx.Log.LogWarning("Upload: could not queue a run of '" + segment.Id + "': " + ex.Message);
            }
        }

        // --- sending ------------------------------------------------------------

        private const float EmptyQueueRecheck = 60f;

        public override void Tick()
        {
            TickAttempts();
            TickDeletes();
            if (_busy || !_enabled.Value || _tokenBad || Time.unscaledTime < _nextTry) return;
            _nextTry = Time.unscaledTime + 5f;
            string next = Oldest();
            // An empty queue is listed again in a minute, not every 5 s (a
            // folder listing is garbage): every file written here sets
            // _nextTry back to 0, so a new run still goes at once.
            if (next == null) { _nextTry = Time.unscaledTime + EmptyQueueRecheck; return; }
            _busy = true;
            Ctx.Runner.StartCoroutine(Pump(next));
        }

        private string Oldest()
        {
            if (!Directory.Exists(_pendingDir)) return null;
            string[] files = Directory.GetFiles(_pendingDir, "*" + SegmentBundle.Extension);
            if (files.Length == 0) return null;
            Array.Sort(files, StringComparer.Ordinal);
            return files[0];
        }

        public int PendingCount
        {
            get { return Directory.Exists(_pendingDir) ? Directory.GetFiles(_pendingDir, "*" + SegmentBundle.Extension).Length : 0; }
        }

        private IEnumerator Pump(string path)
        {
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception ex) { Ctx.Log.LogWarning("Upload: cannot read " + Path.GetFileName(path) + ": " + ex.Message); _busy = false; yield break; }

            string baseUrl = SiteProtocol.TrimUrl(_url.Value);

            if (string.IsNullOrEmpty(_token.Value))
            {
                bool ok = false;
                yield return Ctx.Runner.StartCoroutine(Register(baseUrl, text, delegate(bool r) { ok = r; }));
                if (!ok) { _busy = false; yield break; }
            }

            if (text.Length > MaxBundleBytes) { Refuse(path, "larger than " + (MaxBundleBytes >> 20) + " MB"); _busy = false; yield break; }

            long code = 0; string body = null, error = null;
            yield return Ctx.Runner.StartCoroutine(WebRequest.Send("POST", baseUrl + "/api/runs", Encoding.UTF8.GetBytes(text),
                "text/plain; charset=utf-8", _token.Value, RequestTimeout,
                delegate(long c, string b, string e) { code = c; body = b; error = e; }));

            string name = Path.GetFileName(path);
            switch (SiteProtocol.Classify(code))
            {
                case UploadOutcome.Done:
                    int added = SiteProtocol.Ids(body, "added").Count, existing = SiteProtocol.Ids(body, "existing").Count;
                    TryDelete(path);
                    _failures = 0;
                    _uploaded += added;
                    _nextTry = 0f;
                    _state = "uploaded " + _uploaded + " run(s) this session (" + DateTime.Now.ToString("HH:mm") + ")";
                    Ctx.Log.LogInfo("Upload: " + name + " -> " + added + " new, " + existing + " already on the site" +
                                    Skipped(body) + ".");
                    if (SiteProtocol.Field(body, "startstate") == "wanted") QueueStartState(text);
                    break;
                case UploadOutcome.Refused:
                    Refuse(path, "HTTP " + code + ": " + (SiteProtocol.Field(body, "error") ?? body));
                    _nextTry = 0f;
                    break;
                case UploadOutcome.TokenBad:
                    _tokenBad = true;
                    _state = "the site does not know this install's token - clear Token in the config to register again";
                    Ctx.Log.LogWarning("Upload: token refused (401); uploads paused. " + PendingCount + " run file(s) wait.");
                    break;
                default:
                    _failures++;
                    float wait = SiteProtocol.RetryDelay(_failures);
                    _nextTry = Time.unscaledTime + wait;
                    _state = "site not reachable (" + (error ?? "HTTP " + code) + ") - retrying in " + Mathf.RoundToInt(wait) + " s; " +
                             PendingCount + " run file(s) wait";
                    Ctx.Log.LogWarning("Upload: " + name + " not sent: " + (error ?? "HTTP " + code) + "; retry in " + Mathf.RoundToInt(wait) + " s.");
                    break;
            }
            _busy = false;
        }

        /// The site keeps a runner spot's start state so a download restores
        /// it (T-0194); it asks for one its route has a hash for but no data.
        /// The same bundle goes again with the state - its runs come back
        /// "already on the site" - once: a bundle that carried one is never
        /// re-sent (SiteProtocol.StartStateResend).
        private void QueueStartState(string bundleText)
        {
            try
            {
                string why;
                SegmentBundle b = SegmentBundle.Parse(bundleText, out why, null);
                if (b == null) return;
                SavestateModule savestates = Host.Find<SavestateModule>();
                string state = savestates != null ? savestates.ReadStartStateText(b.Segment) : null;
                string again = SiteProtocol.StartStateResend(bundleText, state, out why);
                if (again == null)
                {
                    Ctx.Log.LogInfo("Upload: the site has no start state for '" + b.Segment.Id + "' - not sent: " + why + ".");
                    return;
                }
                Directory.CreateDirectory(_pendingDir);
                string name = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + (_seq++).ToString("000") + "_" +
                              Safe(b.Segment.Id) + SegmentBundle.Extension;
                File.WriteAllText(Path.Combine(_pendingDir, name), again, new UTF8Encoding(false));
                _nextTry = 0f;
                Ctx.Log.LogInfo("Upload: the site has no start state for '" + b.Segment.Id + "' - queued it.");
            }
            catch (Exception ex)
            {
                Ctx.Log.LogWarning("Upload: start state not queued: " + ex.Message);
            }
        }

        private IEnumerator Register(string baseUrl, string bundleText, Action<bool> done)
        {
            string id = RunnerIdNow != null ? RunnerIdNow() : _runnerId;
            string name = RunnerNameNow != null ? RunnerNameNow() : _runnerName;
            if (string.IsNullOrEmpty(id)) { id = _runnerId; name = _runnerName; }
            if (string.IsNullOrEmpty(id))
            {
                // After a restart: the runner stamped on the queued run.
                int a = bundleText.IndexOf("\n[attempt]", StringComparison.Ordinal);
                if (a < 0 || !SiteProtocol.RunnerOf(bundleText.Substring(a), out id, out name))
                {
                    _state = "waiting for a finished run to register with";
                    done(false);
                    yield break;
                }
            }

            long code = 0; string body = null, error = null;
            yield return Ctx.Runner.StartCoroutine(WebRequest.Send("POST", baseUrl + "/api/register",
                Encoding.UTF8.GetBytes(SiteProtocol.RegisterBody(id, name)), "application/json", null, RequestTimeout,
                delegate(long c, string b, string e) { code = c; body = b; error = e; }));

            string token = code == 200 ? SiteProtocol.Field(body, "token") : null;
            if (!string.IsNullOrEmpty(token))
            {
                _token.Value = token;
                Ctx.Log.LogInfo("Upload: registered on " + baseUrl + " as '" + name + "' (" + id + ").");
                done(true);
                yield break;
            }

            if (code == 409)
            {
                _tokenBad = true;
                _state = "this runner is registered on the site already (a lost token?) - the site's admin can reset it";
                Ctx.Log.LogWarning("Upload: runner " + id + " is registered already (409); uploads paused.");
            }
            else
            {
                _failures++;
                float wait = SiteProtocol.RetryDelay(_failures);
                _nextTry = Time.unscaledTime + wait;
                _state = "could not register (" + (error ?? "HTTP " + code) + ") - retrying in " + Mathf.RoundToInt(wait) + " s";
                Ctx.Log.LogWarning("Upload: register failed: " + (error ?? "HTTP " + code + " " + body) + ".");
            }
            done(false);
        }

        private void Refuse(string path, string why)
        {
            string name = Path.GetFileName(path);
            try
            {
                Directory.CreateDirectory(_refusedDir);
                string to = Path.Combine(_refusedDir, name);
                if (File.Exists(to)) File.Delete(to);
                File.Move(path, to);
                File.WriteAllText(to + ".txt", why + "\n");
            }
            catch (Exception) { TryDelete(path); }
            _state = "the site refused a run: " + why;
            Ctx.Log.LogWarning("Upload: " + name + " refused - " + why + " (moved to uploads/refused).");
        }

        private static string Skipped(string body)
        {
            int k = body != null ? body.IndexOf("\"skipped\":[\"", StringComparison.Ordinal) : -1;
            return k < 0 ? "" : ", some skipped: " + body.Substring(k + 11, Math.Min(200, body.Length - k - 11));
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch (Exception) { }
        }

        private static string Safe(string id)
        {
            StringBuilder sb = new StringBuilder(id.Length);
            for (int i = 0; i < id.Length; i++) sb.Append(char.IsLetterOrDigit(id[i]) || id[i] == '-' ? id[i] : '_');
            return sb.ToString();
        }

        // --- spot submissions (Practice's Share row) -----------------------------------

        private bool _submitting;
        public bool Submitting { get { return _submitting; } }

        /// Sends `segment` (+ its start state, no attempts) to the site for the
        /// author to approve as a community spot. `report` gets each status
        /// line (the Practice tab shows it under the button).
        public void Submit(Segment segment, string startState, Action<string> report)
        {
            string why = SiteProtocol.SubmitRefusal(segment.Id, false);
            if (why != null) { report(why); return; }
            if (_submitting) { report("A submission is on its way already."); return; }

            SegmentBundle b = new SegmentBundle();
            b.Segment = segment;
            b.StartState = startState;
            b.Exported = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            b.PluginVersion = OverlayPlugin.PluginVersion;
            string text = b.Write();
            if (text.Length > MaxBundleBytes) { report("Too large to send (over " + (MaxBundleBytes >> 20) + " MB)."); return; }

            _submitting = true;
            report("Sending '" + segment.Name + "' to " + HostName() + "...");
            Ctx.Runner.StartCoroutine(SendSubmission(segment, text, startState != null, report));
        }

        private IEnumerator SendSubmission(Segment segment, string text, bool withStart, Action<string> report)
        {
            string baseUrl = SiteProtocol.TrimUrl(_url.Value);
            if (string.IsNullOrEmpty(_token.Value))
            {
                bool ok = false;
                yield return Ctx.Runner.StartCoroutine(Register(baseUrl, "", delegate(bool r) { ok = r; }));
                if (!ok) { _submitting = false; report("Not sent: " + _state + "."); yield break; }
            }

            long code = 0; string body = null, error = null;
            yield return Ctx.Runner.StartCoroutine(WebRequest.Send("POST", baseUrl + "/api/submissions", Encoding.UTF8.GetBytes(text),
                "text/plain; charset=utf-8", _token.Value, RequestTimeout,
                delegate(long c, string b, string e) { code = c; body = b; error = e; }));
            _submitting = false;

            string what = withStart ? "with its start state" : "no start state";
            switch (SiteProtocol.Classify(code))
            {
                case UploadOutcome.Done:
                    bool replaced = body != null && body.Contains("\"replaced\":true");
                    report((replaced ? "Sent again - it replaces your earlier submission" : "Submitted") + " (" + what +
                           "). The author looks at it before it goes out to everyone; until then a new submit replaces this one.");
                    Ctx.Log.LogInfo("Submit: '" + segment.Id + "' sent to " + baseUrl + " as submission " +
                                    SiteProtocol.Number(body, "id") + (replaced ? " (replaced the waiting one)" : "") + ", " + what + ".");
                    break;
                case UploadOutcome.TokenBad:
                    _tokenBad = true;
                    report("Not sent: the site does not know this install's token - clear Token in the config to register again.");
                    Ctx.Log.LogWarning("Submit: token refused (401).");
                    break;
                case UploadOutcome.Refused:
                    string msg = SiteProtocol.Field(body, "error") ?? body;
                    report("The site refused it: " + msg);
                    Ctx.Log.LogWarning("Submit: '" + segment.Id + "' refused - HTTP " + code + ": " + msg);
                    break;
                default:
                    report("Not sent - the site is not reachable (" + (error ?? "HTTP " + code) + "). Try again later.");
                    Ctx.Log.LogWarning("Submit: '" + segment.Id + "' not sent: " + (error ?? "HTTP " + code) + ".");
                    break;
            }
        }

        // --- deleting the runner's own spot from the site (Practice's Share row) ------

        private bool _deleting;
        public bool Deleting { get { return _deleting; } }

        /// Asks the site to remove `segment` - only its owner's token can
        /// (the runner who first uploaded on it). Its queued uploads are
        /// dropped first, or the next send would bring it straight back.
        public void DeleteFromSite(Segment segment, Action<string> report)
        {
            if (_deleting) { report("A delete is on its way already."); return; }
            if (string.IsNullOrEmpty(_token.Value)) { report("This install has never uploaded - none of its spots are on the website."); return; }
            int dropped = DropPending(segment.Id);
            _deleting = true;
            report("Deleting '" + segment.Name + "' from " + HostName() + "...");
            Ctx.Runner.StartCoroutine(SendDelete(segment, dropped, report));
        }

        private IEnumerator SendDelete(Segment segment, int dropped, Action<string> report)
        {
            string baseUrl = SiteProtocol.TrimUrl(_url.Value);
            long code = 0; string body = null, error = null;
            yield return Ctx.Runner.StartCoroutine(WebRequest.Send("DELETE", SiteProtocol.DeleteSpotUrl(baseUrl, segment.Id), null,
                null, _token.Value, RequestTimeout, delegate(long c, string b, string e) { code = c; body = b; error = e; }));
            _deleting = false;
            if (code == 401) _tokenBad = true;
            string text = SiteProtocol.DeleteSpotMessage(code, body, error);
            if (dropped > 0) text += " " + dropped + " queued upload file(s) of it were dropped.";
            report(text);
            Ctx.Log.LogInfo("Delete: '" + segment.Id + "' on " + baseUrl + " - " + (code == 0 ? error : "HTTP " + code) +
                            (dropped > 0 ? ", " + dropped + " queued file(s) dropped" : "") + ": " + text);
        }

        // --- a spot deleted in game goes off the site by itself ----------------------

        private string _deletesFile;
        private float _nextDelete;
        private int _deleteFailures;
        private Action<string> _deleteReport;   // the answer for the delete just made (one only)

        /// Practice deleted `segment`: drop its queued uploads and, when
        /// uploads are on and this install has a token, queue a DELETE of it
        /// on the site (sent at once, retried while the site is out of
        /// reach). Returns a line for the runner when nothing will be sent
        /// or it waits; null when the answer comes through `report` (and
        /// nothing at all when the site had nothing of it).
        public string DeleteSpotQuietly(Segment segment, Action<string> report)
        {
            int dropped = DropPending(segment.Id);
            if (!_enabled.Value || string.IsNullOrEmpty(_token.Value)) return null;
            try
            {
                string text = File.Exists(_deletesFile) ? File.ReadAllText(_deletesFile) : "";
                Directory.CreateDirectory(Path.GetDirectoryName(_deletesFile));
                File.WriteAllText(_deletesFile, SiteProtocol.QueueAdd(text, segment.Id), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Ctx.Log.LogWarning("Delete: could not queue '" + segment.Id + "' for the website: " + ex.Message);
                return null;
            }
            _deleteReport = report;
            _nextDelete = 0f;
            Ctx.Log.LogInfo("Delete: '" + segment.Id + "' queued to come off " + HostName() +
                            (dropped > 0 ? " (" + dropped + " queued upload file(s) dropped)" : "") + ".");
            return null;
        }

        private void TickDeletes()
        {
            if (_deleting || !_enabled.Value || _tokenBad || Time.unscaledTime < _nextDelete) return;
            if (_deletesFile == null || !File.Exists(_deletesFile)) { _nextDelete = Time.unscaledTime + EmptyQueueRecheck; return; }
            string id;
            try { id = SiteProtocol.QueueFirst(File.ReadAllText(_deletesFile)); }
            catch (Exception) { _nextDelete = Time.unscaledTime + EmptyQueueRecheck; return; }
            if (id == null) { _nextDelete = Time.unscaledTime + EmptyQueueRecheck; return; }
            _nextDelete = Time.unscaledTime + 5f;
            _deleting = true;
            Ctx.Runner.StartCoroutine(PumpDelete(id));
        }

        private IEnumerator PumpDelete(string id)
        {
            string baseUrl = SiteProtocol.TrimUrl(_url.Value);
            long code = 0; string body = null, error = null;
            yield return Ctx.Runner.StartCoroutine(WebRequest.Send("DELETE", SiteProtocol.DeleteSpotUrl(baseUrl, id), null,
                null, _token.Value, RequestTimeout, delegate(long c, string b, string e) { code = c; body = b; error = e; }));
            _deleting = false;
            Action<string> report = _deleteReport;
            _deleteReport = null;
            if (code == 401) _tokenBad = true;
            if (SiteProtocol.DeleteSettled(code))
            {
                _deleteFailures = 0;
                try { File.WriteAllText(_deletesFile, SiteProtocol.QueueRemove(File.ReadAllText(_deletesFile), id), new UTF8Encoding(false)); }
                catch (Exception ex) { Ctx.Log.LogWarning("Delete: could not update the queue: " + ex.Message); }
                _nextDelete = 0f;
                bool quiet = SiteProtocol.DeleteQuiet(code, body);
                Ctx.Log.LogInfo("Delete: '" + id + "' on " + baseUrl + " - HTTP " + code + (quiet ? " (nothing on the site)" : "") + ".");
                if (!quiet && report != null) report(SiteProtocol.DeleteSpotMessage(code, body, error));
                yield break;
            }
            _deleteFailures++;
            float wait = Math.Min(300f, 10f * (1 << Math.Min(_deleteFailures, 5)));
            _nextDelete = Time.unscaledTime + wait;
            Ctx.Log.LogWarning("Delete: '" + id + "' not sent (" + (code == 0 ? error ?? "no answer" : "HTTP " + code) +
                               "); queued, retrying in " + (int)wait + " s.");
            if (report != null && _deleteFailures == 1)
                report("The website could not be reached (" + (code == 0 ? error ?? "no answer" : "HTTP " + code) +
                       ") - the spot comes off it by itself once it can.");
        }

        /// Deletes the queued upload files of one segment; how many.
        private int DropPending(string segmentId)
        {
            int n = 0;
            try
            {
                if (!Directory.Exists(_pendingDir)) return 0;
                string tail = "_" + Safe(segmentId) + SegmentBundle.Extension;
                foreach (string path in Directory.GetFiles(_pendingDir, "*" + SegmentBundle.Extension))
                    if (Path.GetFileName(path).EndsWith(tail, StringComparison.Ordinal)) { TryDelete(path); n++; }
            }
            catch (Exception ex) { Ctx.Log.LogWarning("Delete: could not clear the upload queue of '" + segmentId + "': " + ex.Message); }
            return n;
        }

        // --- the Runs tab's section ---------------------------------------------------

        private readonly GUIContent _stateText = new GUIContent("");
        private readonly GUIContent _toggleText = new GUIContent(" Upload finished runs to the website");
        private float _nextText;

        /// Called from the Runs tab's Tick-side refresh; never allocates in draw.
        public void RefreshText()
        {
            if (Time.unscaledTime < _nextText) return;
            _nextText = Time.unscaledTime + 0.5f;
            int pending = PendingCount;
            string s = !_enabled.Value ? "Website uploads are off."
                     : "Website: " + _state + (pending > 0 && !_state.Contains("wait") ? " - " + pending + " run file(s) to send" : "");
            if (_stateText.text != s) _stateText.text = s;
            string t = " Upload finished runs to " + HostName();
            if (_toggleText.text != t) _toggleText.text = t;
        }

        /// The section under the Runs tab's status lines; returns the new y.
        public float DrawSection(float y, float w)
        {
            bool on = UiKit.Toggle(new Rect(0, y, w, 20), _enabled.Value, _toggleText);
            if (on != _enabled.Value)
            {
                _enabled.Value = on;
                _tokenBad = false;
                _nextTry = 0f;
                _nextText = 0f;
                if (on) _state = "on";
            }
            y += 22f;
            y += UiText.Draw(0, y, w, _stateText);

            if (GUI.Button(new Rect(0, y + 2, 200, 22), "Upload this spot's saved runs")) { EnqueueSaved(); _nextText = 0f; }
            if (GUI.Button(new Rect(206, y + 2, 150, 22), "Open on the website"))
            {
                Segment seg = CurrentSegment != null ? CurrentSegment() : null;
                Application.OpenURL(seg != null ? SiteProtocol.SpotUrl(_url.Value, seg.Id) : SiteProtocol.TrimUrl(_url.Value));
            }
            return y + 28f;
        }

        private string _hostName, _hostFor;
        private string HostName()
        {
            if (_hostFor != _url.Value)
            {
                _hostFor = _url.Value;
                string u = SiteProtocol.TrimUrl(_url.Value);
                int s = u.IndexOf("://", StringComparison.Ordinal);
                _hostName = s >= 0 ? u.Substring(s + 3) : u;
            }
            return _hostName;
        }
    }
}
