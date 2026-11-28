using System;
using System.IO;
using System.Text;
using ForestOverlay.Core;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // The live connection (T-0490, author 2026-11-28). A runner who wants
    // deter to look at their game presses "Let deter see my game" in the
    // Practice tab; the site gives a 6-character code, the runner tells it
    // to deter, and deter's Claude session drives this game over the test
    // bridge (Modules/BridgeModule), relayed through forest.deter.cloud
    // (Data/LiveLink has the contract). The runner never runs a script:
    // the dev drives, with consent, and the runner can stop it any time.
    //
    // While open, every 0.5 s: GET /in -> the bridge's queue; the bridge's
    // new output -> POST /out; a finished `shot` -> POST /file. The bridge
    // is guarded meanwhile (Data/LiveGuard: the game and its engine only,
    // nothing of the machine). A banner with Stop shows on screen the whole
    // time, F2 window open or not, F5 or not.
    //
    // Ends on Stop (the button, the banner, the optional hotkey), the game
    // closing, 30 min with no command (the site's idle), the site saying
    // the session is gone (410 / 404), or run mode starting. Practice only:
    // run mode refuses it, and it marks the session as practice.
    // ------------------------------------------------------------------
    public sealed class LiveLinkModule : OverlayModule
    {
        public override string Id { get { return "livelink"; } }
        public override string DisplayName { get { return "Live session"; } }
        public override bool IsPracticeOnly { get { return true; } }

        private const float PollEvery = 0.5f;
        private const float RequestTimeout = 20f;

        private enum Phase { Closed, Opening, Open }

        private Phase _phase = Phase.Closed;
        private string _code;
        private string _secret;
        private string _inUrl, _outUrl;     // built once a session
        private int _idleSeconds = LiveLink.DefaultIdleSeconds;
        private float _lastCommand;
        private float _nextPoll;
        private int _gen;                  // a new session = a new generation; late answers of an old one are dropped
        private int _commands;

        private bool _inFlight, _outFlight, _fileFlight;
        private int _inGen, _outGen, _fileGen, _openGen;
        private string _unsent;            // output the site has not taken yet
        private string _sending;           // the output in the request in flight
        private int _retries;

        private BridgeModule _bridge;
        private RunUploadModule _upload;

        private WebRequest.Done _onOpen, _onIn, _onOut, _onFile, _onClose;

        // UI text, rebuilt when the state changes (never in OnGUI).
        private static readonly GUIContent TextStart = new GUIContent("Let deter see my game");
        private static readonly GUIContent TipStart = new GUIContent(
            "Opens a live session: deter's tools drive this game through forest.deter.cloud - move you, read and change game " +
            "values, press keys, take screenshots, read the overlay's log. Only the game: nothing else on your PC. You get a " +
            "code to give deter; it ends when you press Stop, close the game, or after 30 min with nothing. Marks the session as practice.");
        private static readonly GUIContent TextStop = new GUIContent("Stop");
        private static readonly GUIContent TextOpening = new GUIContent("Opening...");
        private readonly GUIContent _rowText = new GUIContent("");
        private readonly GUIContent _statusText = new GUIContent("");
        private readonly GUIContent _bannerText = new GUIContent("");
        private float _bannerW = -1f;      // measured in OnGUI when the text changes
        private GUIStyle _bannerStyle;

        public bool IsOpen { get { return _phase == Phase.Open; } }

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);
            _bridge = Host.Find<BridgeModule>();
            _upload = Host.Find<RunUploadModule>();
            _onOpen = OnOpen;
            _onIn = OnIn;
            _onOut = OnOut;
            _onFile = OnFile;
            _onClose = OnClose;
        }

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("live.stop", KeyCode.None, "Stop the live session with deter", StopKey);
        }

        private void StopKey()
        {
            if (_phase != Phase.Closed) Stop("Stop key");
        }

        private string BaseUrl
        {
            get
            {
                string u = _upload != null ? _upload.SiteUrl : null;
                return string.IsNullOrEmpty(u) ? SiteProtocol.DefaultUrl : u;
            }
        }

        // ------------------------------------------------------------------
        // Start and stop

        private void OpenSession()
        {
            if (_phase != Phase.Closed) return;
            if (Ctx.Run.Refuse("bridge", "a live session"))
            {
                SetStatus(Ctx.Run.RefusedText("A live session"));
                return;
            }
            if (_bridge == null) { SetStatus("The test bridge is missing, so a live session cannot run."); return; }
            _phase = Phase.Opening;
            _gen++;
            _openGen = _gen;
            SetStatus("");
            Ctx.Log.LogInfo("Live: opening a session at " + BaseUrl + ".");
            Ctx.Runner.StartCoroutine(WebRequest.Send("POST", LiveLink.OpenUrl(BaseUrl), null, null, null, RequestTimeout, _onOpen));
            RebuildText();
        }

        private void OnOpen(long code, string body, string error)
        {
            if (_openGen != _gen || _phase != Phase.Opening) return;
            string c, s, why;
            int idle;
            if (LiveLink.Classify(code) != LiveLink.Answer.Ok)
            {
                _phase = Phase.Closed;
                string reason = code == 0 ? (error ?? "no answer") : "the site answered " + code;
                Ctx.Log.LogInfo("Live: could not open a session - " + reason + ".");
                SetStatus("Could not open a live session: " + reason + ". Try again in a moment.");
                RebuildText();
                return;
            }
            if (!LiveLink.ParseOpen(body, out c, out s, out idle, out why))
            {
                _phase = Phase.Closed;
                Ctx.Log.LogInfo("Live: could not open a session - " + why + ".");
                SetStatus("Could not open a live session: " + why + ".");
                RebuildText();
                return;
            }

            _code = c;
            _secret = s;
            _idleSeconds = idle;
            _inUrl = LiveLink.InUrl(BaseUrl);
            _outUrl = LiveLink.OutUrl(BaseUrl);
            _phase = Phase.Open;
            _lastCommand = Time.unscaledTime;
            _nextPoll = 0f;
            _commands = 0;
            _retries = 0;
            _unsent = _sending = null;
            _inFlight = _outFlight = _fileFlight = false;
            Ctx.Practice.Mark("live session");
            _bridge.RemoteActive = true;
            Ctx.Log.LogInfo("Live: session open, code " + _code + ", ends after " + (_idleSeconds / 60) + " min with no command.");
            Ctx.Notice.Show("Live session open - give deter the code " + _code + ". Stop ends it.", 8f);
            RebuildText();
        }

        /// Ends the session; `why` goes in the log and on screen.
        private void Stop(string why)
        {
            End(why, true);
        }

        private void End(string why, bool tellSite)
        {
            if (_phase == Phase.Closed) return;
            Phase was = _phase;
            string secret = _secret;
            _phase = Phase.Closed;
            _gen++;
            if (_bridge != null) _bridge.RemoteActive = false;
            _code = _secret = null;
            _unsent = _sending = null;
            if (was == Phase.Open && tellSite && secret != null)
                Ctx.Runner.StartCoroutine(WebRequest.Send("POST", LiveLink.CloseUrl(BaseUrl), null, null, secret, RequestTimeout, _onClose));
            Ctx.Log.LogInfo("Live: session ended (" + why + "), " + _commands + " command(s) run.");
            SetStatus("The live session ended: " + why + ".");
            Ctx.Notice.Show("Live session with deter ended (" + why + ").", 6f);
            RebuildText();
        }

        private void OnClose(long code, string body, string error)
        {
            // Nothing to do: the session is over here either way, and the
            // site drops it after its idle time if this did not arrive.
        }

        public override void Shutdown()
        {
            if (_phase != Phase.Open) return;
            // The game is closing: no frame will run a coroutine, so the
            // close request is sent by hand (its first step sends it).
            try
            {
                System.Collections.IEnumerator e = WebRequest.Send("POST", LiveLink.CloseUrl(BaseUrl), null, null, _secret, 5f, _onClose);
                e.MoveNext();
            }
            catch (Exception) { }
            Ctx.Log.LogInfo("Live: session ended (the game closed), " + _commands + " command(s) run.");
            _phase = Phase.Closed;
            if (_bridge != null) _bridge.RemoteActive = false;
        }

        // ------------------------------------------------------------------
        // The relay

        public override void Tick()
        {
            if (_phase != Phase.Open) return;
            float now = Time.unscaledTime;

            if (Ctx.Run.Active) { Stop("run mode started"); return; }
            if (LiveLink.IdleOver(now, _lastCommand, _idleSeconds) && !_bridge.RemoteBusy)
            {
                Stop((_idleSeconds / 60) + " min with no command");
                return;
            }

            if (now < _nextPoll) return;
            _nextPoll = now + PollEvery;

            if (!_inFlight)
            {
                _inFlight = true;
                _inGen = _gen;
                Ctx.Runner.StartCoroutine(WebRequest.Send("GET", _inUrl, null, null, _secret, RequestTimeout, _onIn));
            }

            // A finished screenshot goes up before the output that names
            // it is done, so the reply's close line means the file is there.
            if (!_fileFlight)
            {
                string shot = _bridge.TakeReadyShot();
                if (shot != null) SendShot(shot);
            }

            string fresh = _bridge.TakeRemoteOutput();
            if (fresh != null)
            {
                _unsent = _unsent == null ? fresh : _unsent + fresh;
                _unsent = LiveLink.KeepNewest(_unsent, LiveLink.MaxUnsentBytes);   // bytes, not chars
            }
            if (!_outFlight && !_fileFlight && _unsent != null)
            {
                // At most MaxPostBytes a post (the site caps one at 256 KB);
                // the rest goes on the next polls.
                string rest;
                _sending = LiveLink.NextChunk(_unsent, LiveLink.MaxPostBytes, out rest);
                _unsent = rest;
                _outFlight = true;
                _outGen = _gen;
                Ctx.Runner.StartCoroutine(WebRequest.Send("POST", _outUrl, Encoding.UTF8.GetBytes(_sending),
                                                          "text/plain; charset=utf-8", _secret, RequestTimeout, _onOut));
            }
        }

        private void OnIn(long code, string body, string error)
        {
            if (_inGen != _gen) return;
            _inFlight = false;
            if (_phase != Phase.Open) return;
            if (!Answered(code, error, "reading commands")) return;
            if (string.IsNullOrEmpty(body)) return;
            int n = _bridge.RemoteEnqueue(body);
            if (n > 0)
            {
                _commands += n;
                _lastCommand = Time.unscaledTime;
                RebuildText();
            }
        }

        private void OnOut(long code, string body, string error)
        {
            if (_outGen != _gen) return;
            _outFlight = false;
            if (_phase != Phase.Open) return;
            LiveLink.Answer a = LiveLink.Classify(code);
            if (a == LiveLink.Answer.Retry)
            {
                // Sent again next poll, ahead of anything newer.
                _unsent = _unsent == null ? _sending : _sending + _unsent;
            }
            _sending = null;
            Answered(code, error, "sending output");
        }

        private void SendShot(string path)
        {
            string name = LiveLink.ShotName(Path.GetFileName(path));
            string url = LiveLink.FileUrl(BaseUrl, name);
            byte[] png;
            try
            {
                FileInfo fi = new FileInfo(path);
                if (url == null || fi.Length > LiveLink.MaxFileBytes)
                {
                    Ctx.Log.LogInfo("Live: screenshot " + Path.GetFileName(path) + " not sent (" +
                                    (url == null ? "its name" : "over 4 MB") + ").");
                    return;
                }
                png = File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                Ctx.Log.LogInfo("Live: screenshot " + Path.GetFileName(path) + " not sent (" + ex.Message + ").");
                return;
            }
            _fileFlight = true;
            _fileGen = _gen;
            Ctx.Log.LogInfo("Live: sending screenshot " + name + " (" + (png.Length / 1024) + " KB).");
            Ctx.Runner.StartCoroutine(WebRequest.Send("POST", url, png, "image/png", _secret, 60f, _onFile));
        }

        private void OnFile(long code, string body, string error)
        {
            if (_fileGen != _gen) return;
            _fileFlight = false;
            if (_phase != Phase.Open) return;
            Answered(code, error, "sending a screenshot");
        }

        /// True for a 2xx. Ends the session when the site says it is gone
        /// or will never take this; counts the rest as a retry.
        private bool Answered(long code, string error, string what)
        {
            LiveLink.Answer a = LiveLink.Classify(code);
            if (a == LiveLink.Answer.Ok)
            {
                if (_retries > 0)
                {
                    Ctx.Log.LogInfo("Live: the site answers again after " + _retries + " failed request(s).");
                    _retries = 0;
                    SetStatus("");
                }
                return true;
            }
            if (a == LiveLink.Answer.Closed) { End("the site closed it", false); return false; }
            if (a == LiveLink.Answer.TooLarge)
            {
                // That one body is dropped; the session goes on.
                Ctx.Log.LogInfo("Live: " + what + " was too large for the site (413), dropped it.");
                return false;
            }
            if (a == LiveLink.Answer.Refused) { Stop("the site refused " + what + " (" + code + ")"); return false; }
            if (_retries++ == 0)
            {
                Ctx.Log.LogInfo("Live: " + what + " failed (" + (code == 0 ? error ?? "no answer" : "answer " + code) + "), retrying.");
                SetStatus("The site is not answering - retrying.");
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Text

        private void SetStatus(string text)
        {
            _statusText.text = text ?? "";
        }

        private void RebuildText()
        {
            if (_phase == Phase.Open)
            {
                _rowText.text = "Live session with deter - code " + _code + ". Give deter this code; Stop ends it. " +
                                _commands + " command(s) so far.";
                _bannerText.text = "Live session with deter (code " + _code + ")";
            }
            else
            {
                _rowText.text = "";
                _bannerText.text = "";
            }
            _bannerW = -1f;
        }

        // ------------------------------------------------------------------
        // Drawing

        /// The Practice tab's row (drawn by PracticeModule under its start
        /// strip); returns the y after it.
        public float DrawRow(float y, float w)
        {
            if (_phase == Phase.Open)
            {
                Rect stop = new Rect(0, y, 80, 24);
                if (UiKit.PrimaryButton(stop, TextStop)) Stop("Stop pressed");
                float h = Mathf.Max(26f, UiText.Draw(88, y + 2, w - 88, _rowText));
                y += h;
            }
            else
            {
                Rect start = new Rect(0, y, 190, 24);
                GUI.enabled = _phase == Phase.Closed;
                if (WinPx.Button(start, _phase == Phase.Opening ? TextOpening : TextStart)) OpenSession();
                GUI.enabled = true;
                UiKit.Hint(start, TipStart);
                y += 26f;
            }
            y += UiText.Draw(0, y, w, _statusText);
            return y + 4f;
        }

        // The banner: on screen for the whole session, under F5 too (the
        // runner always sees that someone is driving).
        public override void DrawScreenAlways()
        {
            if (_phase != Phase.Open) return;
            Matrix4x4 outer = Host.Hud.Settings.BeginScaled();
            try { DrawBanner(); }
            finally { GUI.matrix = outer; }
        }

        private void DrawBanner()
        {
            if (_bannerStyle == null)
            {
                _bannerStyle = UiKit.Style(GUI.skin.label);
                _bannerStyle.alignment = TextAnchor.MiddleLeft;
                _bannerStyle.normal.textColor = Color.white;
            }
            if (_bannerW < 0f) _bannerW = CrispText.CalcSize(_bannerStyle, _bannerText).x;
            UiScale ui = Host.Hud.Settings.Scale();
            const float stopW = 60f, pad = 8f, h = 30f;
            float w = _bannerW + stopW + pad * 3f;
            Rect box = new Rect(Mathf.Max(0f, (ui.VW - w) * 0.5f), 4f, w, h);
            WinPx.Box(box, GUIContent.none, UiKit.WidgetCard);
            WinPx.Box(box, GUIContent.none, UiKit.Outline);
            CrispText.Label(new Rect(box.x + pad, box.y, _bannerW + 2f, h), _bannerText, _bannerStyle);
            if (UiKit.PrimaryButton(new Rect(box.xMax - pad - stopW, box.y + 4f, stopW, h - 8f), TextStop)) Stop("Stop pressed on the banner");
        }
    }
}
