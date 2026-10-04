// forest.deter.cloud: /compare - two YouTube runs side by side (maks, QA
// 1554074251831672943: "view two segments / runs playing next to each
// other and see the differences the runners make"). Each side is a video
// with its frame rate, a start, an end and splits set frame by frame (the
// retiming tools' way: step +-1 / 10 / 100 frames, "set current time as
// start"); the table times every segment of both and the difference; Play
// both lines the two up at the start or any split and plays them together.
//
// Everything lives in the page's address (?a=...&b=...&n=...), so the link
// is the share - nothing is stored on the server.
//
// The players are youtube-nocookie.com embeds driven by the IFrame API's
// own postMessage protocol - no YouTube script on this page (the CSP allows
// the frame and nothing else of YouTube's: docs/website.md *Security*).
// Uses el / time / delta / show from app.js.
"use strict";

const YT_ORIGIN = "https://www.youtube-nocookie.com";
const YT_ID = /^[A-Za-z0-9_-]{11}$/;
const CMP_STEPS = [-10000, -1000, -100, -10, -1, 1, 10, 100, 1000, 10000];
const CMP_MAX_SPLITS = 100;

/// A YouTube link (youtube.com/watch?v=, youtu.be/, /shorts/, /live/,
/// /embed/, or a bare 11-character id) -> { id, t } (t: the link's own
/// start time in seconds), or null for anything else.
function youtubeId(text) {
  text = (text || "").trim();
  if (YT_ID.test(text)) return { id: text, t: 0 };
  let u;
  try { u = new URL(/^[a-z][a-z0-9+.-]*:/i.test(text) ? text : "https://" + text); } catch (e) { return null; }
  if (u.protocol !== "https:" && u.protocol !== "http:") return null;
  const host = u.hostname.toLowerCase().replace(/^(www|m|music)\./, "");
  const parts = u.pathname.split("/").filter(Boolean);
  let id = null;
  if (host === "youtu.be") id = parts[0];
  else if (host === "youtube.com" || host === "youtube-nocookie.com") {
    if (parts[0] === "watch") id = u.searchParams.get("v");
    else if (["embed", "shorts", "live", "v"].includes(parts[0])) id = parts[1];
  }
  if (!id || !YT_ID.test(id)) return null;
  return { id, t: linkSeconds(u.searchParams.get("t") || u.searchParams.get("start") || "") };
}
/// "90", "90s", "1m30s", "1h2m3s" -> seconds; anything else 0.
function linkSeconds(s) {
  const m = /^(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s?)?$/.exec(s);
  return s && m ? (+(m[1] || 0)) * 3600 + (+(m[2] || 0)) * 60 + (+(m[3] || 0)) : 0;
}

const YT_ERRORS = {
  2: "YouTube says that is not a valid video.",
  5: "The browser's player could not play this video.",
  100: "The video was not found - removed or private.",
  101: "The uploader does not allow this video on other sites. It only plays on YouTube itself.",
  150: "The uploader does not allow this video on other sites. It only plays on YouTube itself.",
  153: "YouTube refused the player (no referrer reached it - a browser setting or an extension).",
};

/// One embedded player, driven by postMessage. `time` is the last time the
/// player reported (or we seeked to), `at` when; while playing, now()
/// runs on from there at the playback rate.
let ytSeq = 0;
class YtPlayer {
  constructor(box, onChange) {
    this.box = box; this.onChange = onChange; this.frame = null; this.uid = ++ytSeq; this.knock = 0;
    this.reset();
  }
  reset() {
    this.state = -1; this.time = 0; this.at = performance.now(); this.rate = 1; this.duration = 0;
    this.heard = false; this.error = null; this.hold = 0; this.pending = 0; this.cueing = false; this.cued = false;
  }
  /// t: show the video at this time once the player is up (paused there).
  load(id, t) {
    clearInterval(this.knock);
    this.reset();
    this.pending = t || 0;
    const q = new URLSearchParams({ enablejsapi: "1", origin: location.origin, playsinline: "1", rel: "0", iv_load_policy: "3" });
    const f = el("iframe", {
      src: YT_ORIGIN + "/embed/" + id + "?" + q, title: "YouTube video player",
      allow: "autoplay; encrypted-media; picture-in-picture; fullscreen", allowfullscreen: "",
      referrerpolicy: "strict-origin-when-cross-origin",
    });
    f.addEventListener("load", () => this.listen());
    this.frame = f;
    this.box.replaceChildren(f);
  }
  /// The API's handshake: say "listening" until the player answers.
  listen() {
    let tries = 0;
    clearInterval(this.knock);
    this.knock = setInterval(() => {
      if (this.heard || !this.frame) { clearInterval(this.knock); return; }
      if (++tries > 40) {
        clearInterval(this.knock);
        this.error = "The player is not answering - an extension may be blocking youtube-nocookie.com.";
        this.onChange();
        return;
      }
      this.post({ event: "listening", id: this.uid, channel: "widget" });
    }, 250);
  }
  dispose() { clearInterval(this.knock); this.frame = null; }
  post(msg) { if (this.frame && this.frame.contentWindow) this.frame.contentWindow.postMessage(JSON.stringify(msg), YT_ORIGIN); }
  cmd(func, ...args) { this.post({ event: "command", func, args, id: this.uid, channel: "widget" }); }
  receive(data) {
    let m;
    try { m = typeof data === "string" ? JSON.parse(data) : data; } catch (e) { return; }
    if (!m || typeof m !== "object") return;
    if (!this.heard) {
      this.heard = true; this.error = null;
      for (const ev of ["onReady", "onStateChange", "onPlaybackRateChange", "onError"]) this.cmd("addEventListener", ev);
    }
    const info = m.info;
    if (m.event === "onReady" && this.pending > 0) {
      // A cued video shows no frame until it has played: play it muted
      // from there (browsers allow that) and pause on the first frame.
      this.cmd("mute"); this.seek(this.pending); this.cueing = this.pending; this.pending = 0;
      this.cueUntil = performance.now() + 5000;
    }
    if (m.event === "onError") this.error = YT_ERRORS[info] || "YouTube could not play this video (error " + info + ").";
    else if (m.event === "onStateChange" && typeof info === "number") this.setState(info);
    else if ((m.event === "infoDelivery" || m.event === "initialDelivery") && info && typeof info === "object") {
      if (typeof info.playerState === "number") this.setState(info.playerState);
      if (typeof info.playbackRate === "number" && info.playbackRate > 0) this.rate = info.playbackRate;
      if (typeof info.duration === "number") this.duration = info.duration;
      // Right after a seek the player can still report the old place.
      if (typeof info.currentTime === "number" && isFinite(info.currentTime) && performance.now() > this.hold) {
        // currentTimeLastUpdated_: when the player read it (epoch seconds).
        const age = typeof info.currentTimeLastUpdated_ === "number" ? Date.now() - info.currentTimeLastUpdated_ * 1000 : 0;
        this.time = info.currentTime; this.at = performance.now() - (age > 0 && age < 1000 ? age : 0);
      }
    }
    this.onChange();
  }
  setState(s) {
    if (s === this.state) return;
    this.time = this.now(); this.at = performance.now(); this.state = s;
    // Cueing: paused once it played, then back to the exact frame.
    // (Not when it was blocked and the viewer pressed play much later.)
    if (this.cueing !== false && !this.cued && performance.now() > this.cueUntil) { this.cueing = false; this.cmd("unMute"); }
    if (s === 1 && this.cueing !== false && !this.cued) { this.cued = true; this.pause(); }
    else if (s === 2 && this.cued) {
      this.seek(this.cueing); this.cmd("unMute"); this.cueing = false; this.cued = false;
    }
  }
  get ready() { return this.heard && !this.error; }
  get playing() { return this.state === 1; }
  now() { return this.state === 1 ? this.time + (performance.now() - this.at) / 1000 * this.rate : this.time; }
  seek(t) {
    t = Math.max(0, this.duration > 0 ? Math.min(t, this.duration) : t);
    this.time = t; this.at = performance.now(); this.hold = this.at + 600;
    this.cmd("seekTo", t, true);
  }
  play() { this.cmd("playVideo"); }
  pause() { this.cmd("pauseVideo"); }
  setRate(r) { this.cmd("setPlaybackRate", r); }
}

// --- the address: the whole comparison -------------------------------------------------

/// "?a=<id>~<fps>~<start ms>~<end ms>~<split ms>...&b=...&n=<name>|<name>&an=<label>&bn=<label>"
function cmpRead(params) {
  const ms = s => { const v = /^\d{1,8}$/.test(s || "") ? +s : NaN; return isFinite(v) ? v / 1000 : null; };
  const sides = ["a", "b"].map(k => {
    const s = { id: null, fps: 60, start: null, end: null, marks: [], label: (params.get(k + "n") || "").slice(0, 40) };
    const p = (params.get(k) || "").split("~");
    if (!YT_ID.test(p[0])) return s;
    s.id = p[0];
    const fps = parseFloat(p[1]);
    if (fps >= 1 && fps <= 240) s.fps = fps;
    s.start = cmpSnap(s, ms(p[2])); s.end = cmpSnap(s, ms(p[3]));
    s.marks = p.slice(4, 4 + CMP_MAX_SPLITS).map(x => cmpSnap(s, ms(x)));
    return s;
  });
  const n = params.get("n");
  const names = n ? n.split("|").slice(0, CMP_MAX_SPLITS).map(x => x.slice(0, 40)) : [];
  const rows = Math.min(CMP_MAX_SPLITS, Math.max(names.length, sides[0].marks.length, sides[1].marks.length));
  while (names.length < rows) names.push("Split " + (names.length + 1));
  for (const s of sides) { while (s.marks.length < rows) s.marks.push(null); s.marks.length = rows; }
  return { sides, names };
}
function cmpWrite(c) {
  const q = new URLSearchParams();
  const ms = t => t === null || t === undefined ? "" : String(Math.round(t * 1000));
  c.sides.forEach((s, i) => {
    const k = "ab"[i];
    if (s.id) {
      let parts = [s.id, String(+s.fps.toFixed(3)), ms(s.start), ms(s.end), ...s.marks.map(ms)];
      while (parts.length > 4 && parts[parts.length - 1] === "") parts.pop();
      q.set(k, parts.join("~"));
    }
    if (s.label) q.set(k + "n", s.label);
  });
  if (c.names.length) q.set("n", c.names.map(x => x.replace(/\|/g, "/")).join("|"));
  // encodeURIComponent keeps ~ readable (URLSearchParams writes %7E).
  const text = [...q].map(([k, v]) => k + "=" + encodeURIComponent(v)).join("&");
  return "/compare" + (text ? "?" + text : "");
}

/// Times are frame-exact: the frame shown at t is floor(t * fps).
function cmpFrame(s, t) { return Math.floor(t * s.fps + 1e-4); }
function cmpSnap(s, t) { return t === null || t === undefined ? null : cmpFrame(s, t) / s.fps; }

// --- the page ----------------------------------------------------------------------------

function comparePage() {
  document.title = "Compare runs · Forest Practice Runs";
  const c = cmpRead(new URLSearchParams(location.search));
  // The two run up together from refs (video times that line up); waiting
  // = the side being waited for while the other is held for it.
  c.link = null;
  c.active = 0;
  let raf = 0, urlTimer = 0;

  function changed(rebuild) {
    clearTimeout(urlTimer);
    urlTimer = setTimeout(() => history.replaceState(null, "", cmpWrite(c)), 300);
    if (rebuild) renderTable();
  }
  function say(box, text) { box.textContent = text; }

  // --- a side ---------------------------------------------------------------------
  const panes = c.sides.map((s, i) => {
    const name = "Run " + "AB"[i];
    const video = el("div", { class: "cmpvideo" }, el("div", { class: "cmpempty" }, "Paste a YouTube link above."));
    const status = el("div", { class: "rowmsg" });
    const player = new YtPlayer(video, () => {});
    s.player = player;
    const url = el("input", { class: "search", type: "url", placeholder: "YouTube link", "aria-label": name + ": YouTube link",
      value: s.id ? "https://youtu.be/" + s.id : "" });
    const urlMsg = el("div", { class: "rowmsg" });
    const label = el("input", { class: "search cmplabel", type: "text", maxlength: "40", placeholder: "Runner (optional)",
      "aria-label": name + ": runner", value: s.label });
    label.addEventListener("input", () => { s.label = label.value.trim(); renderHeads(); changed(false); });
    function loadUrl() {
      const v = youtubeId(url.value);
      if (!v) { say(urlMsg, "That is not a YouTube link (youtube.com or youtu.be)."); return; }
      say(urlMsg, "");
      if (s.id && v.id !== s.id && (s.start !== null || s.end !== null || s.marks.some(m => m !== null))) {
        s.start = s.end = null; s.marks = s.marks.map(() => null);
        say(urlMsg, "A different video: its start, end and splits were cleared.");
      }
      s.id = v.id;
      player.load(v.id, v.t);
      changed(true);
    }
    url.addEventListener("keydown", e => { if (e.key === "Enter") loadUrl(); });
    url.addEventListener("change", loadUrl);

    const fps = el("input", { class: "search cmpnum", type: "number", min: "1", max: "240", step: "any", value: String(s.fps),
      "aria-label": name + ": frame rate" });
    fps.addEventListener("change", () => {
      const v = parseFloat(fps.value);
      if (v >= 1 && v <= 240) { s.fps = v; changed(true); } else fps.value = String(s.fps);
    });

    const steps = el("div", { class: "cmpsteps" }, CMP_STEPS.map(n =>
      el("button", { class: "chip", type: "button", title: (n > 0 ? "Forward " : "Back ") + Math.abs(n) + (Math.abs(n) === 1 ? " frame" : " frames"),
        onclick: () => { c.active = i; step(s, n); } },
        (n > 0 ? "+" : "−") + (Math.abs(n) >= 1000 ? Math.abs(n) / 1000 + "k" : Math.abs(n)))));

    const clock = el("div", { class: "cmpclock" });
    const runClock = el("div", { class: "cmprun" });
    const markMsg = el("div", { class: "rowmsg" });
    function pointRow(label, key) {
      const value = el("span", { class: "cmpval" });
      return {
        value,
        row: el("div", { class: "cmppoint" }, el("span", { class: "k" }, label), value,
          el("button", { class: "chip", type: "button", onclick: () => setPoint(s, key, markMsg) }, "Set to current"),
          el("button", { class: "chip", type: "button", onclick: () => goTo(s, s[key], markMsg, label.toLowerCase()) }, "Go")),
      };
    }
    const start = pointRow("Start", "start"), end = pointRow("End", "end");
    const total = el("span", { class: "cmpval" });
    const splitBtn = el("button", { class: "chip", type: "button", onclick: () => splitHere(i, markMsg) }, "Split here");

    const head = el("h2", { class: "cmphead" });
    const pane = el("section", { class: "cmpside" },
      head, label, el("div", { class: "cmpurl" }, url, el("button", { class: "chip", type: "button", onclick: loadUrl }, "Load")), urlMsg,
      video, status,
      el("div", { class: "cmprow" }, el("label", { class: "k" }, "Frame rate ", fps), el("span", { class: "sub" }, "the video's fps (YouTube: 60 or 30)")),
      steps, clock, runClock,
      start.row, end.row,
      el("div", { class: "cmppoint" }, el("span", { class: "k" }, "Total"), total, splitBtn),
      markMsg);
    pane.addEventListener("pointerdown", () => { c.active = i; });
    if (s.id) player.load(s.id, s.start !== null ? s.start : 0);
    return { s, pane, head, status, clock, runClock, start: start.value, end: end.value, total, last: {} };
  });

  function renderHeads() {
    panes.forEach((p, i) => { p.head.textContent = "Run " + "AB"[i] + (p.s.label ? " · " + p.s.label : ""); });
    if (table) renderTable();
  }

  // --- actions --------------------------------------------------------------------
  function cur(s) { return cmpSnap(s, s.player.now()); }
  function seekFrame(s, f) { s.player.seek((Math.max(0, f) + 0.5) / s.fps); }
  function step(s, n) {
    if (!s.id) return;
    if (s.player.playing) { s.player.pause(); if (c.link) c.link.playing = false; }
    seekFrame(s, cmpFrame(s, s.player.now()) + n);
  }
  function needVideo(s, msg) {
    if (!s.id) { say(msg, "Load a video first."); return false; }
    if (!s.player.ready) { say(msg, s.player.error || "The player is still starting."); return false; }
    return true;
  }
  function setPoint(s, key, msg) {
    if (!needVideo(s, msg)) return;
    s[key] = cur(s); say(msg, "");
    changed(true);
  }
  function goTo(s, t, msg, what) {
    if (t === null || t === undefined) { say(msg, "No " + what + " set yet."); return; }
    if (!needVideo(s, msg)) return;
    say(msg, "");
    s.player.seek(t + 0.5 / s.fps);
  }
  function splitHere(i, msg) {
    const s = c.sides[i];
    if (!needVideo(s, msg)) return;
    let k = s.marks.indexOf(null);
    if (k < 0) {
      if (c.names.length >= CMP_MAX_SPLITS) { say(msg, "That is the most splits a comparison holds (" + CMP_MAX_SPLITS + ")."); return; }
      addRow(); k = c.names.length - 1;
    }
    s.marks[k] = cur(s);
    say(msg, "Set " + c.names[k] + ".");
    changed(true);
  }
  function addRow() {
    c.names.push("Split " + (c.names.length + 1));
    for (const s of c.sides) s.marks.push(null);
  }

  // --- both -----------------------------------------------------------------------
  const from = el("select", { "aria-label": "Play both from" });
  const rate = el("select", { "aria-label": "Speed" }, [0.25, 0.5, 1, 1.5, 2].map(r =>
    el("option", { value: String(r), selected: r === 1 ? "" : null }, r + "×")));
  rate.addEventListener("change", () => { for (const s of c.sides) if (s.id) s.player.setRate(+rate.value); });
  const bothMsg = el("div", { class: "rowmsg" });
  function refOf(s, k) { return k === "start" ? s.start : k === "here" ? cur(s) : s.marks[+k]; }
  function playBoth() {
    const k = from.value;
    for (const [i, s] of c.sides.entries()) {
      if (!needVideo(s, bothMsg)) { bothMsg.textContent = "Run " + "AB"[i] + ": " + bothMsg.textContent; return; }
      if (refOf(s, k) === null) {
        say(bothMsg, "Run " + "AB"[i] + " has no " + (k === "start" ? "start" : c.names[+k]) + " set yet.");
        return;
      }
    }
    say(bothMsg, "");
    const refs = c.sides.map(s => refOf(s, k));
    c.sides.forEach((s, i) => { s.player.pause(); s.player.seek(refs[i] + 0.5 / s.fps); });
    c.link = { refs, playing: true, waiting: -1, fixedAt: performance.now() + 1500 };
    setTimeout(() => { if (c.link && c.link.playing) for (const s of c.sides) s.player.play(); }, 350);
  }
  function pauseBoth() {
    if (c.link) c.link.playing = false;
    for (const s of c.sides) if (s.id) s.player.pause();
  }
  function stepBoth(n) {
    if (c.link) c.link.playing = false;
    for (const s of c.sides) step(s, n);
  }
  function fillFrom() {
    const keep = from.value;
    const opts = [el("option", { value: "start" }, "Start"),
      ...c.names.map((n, k) => el("option", { value: String(k) }, n || "Split " + (k + 1))),
      el("option", { value: "here" }, "Where each is now")];
    from.replaceChildren(...opts);
    from.value = opts.some(o => o.value === keep) ? keep : "start";
  }
  const copyMsg = el("div", { class: "rowmsg" });
  async function copyLink() {
    history.replaceState(null, "", cmpWrite(c));
    try { await navigator.clipboard.writeText(location.href); say(copyMsg, "Link copied: it holds both videos, their times and the split names."); }
    catch (e) { say(copyMsg, "Copy the address bar: it holds the whole comparison."); }
  }

  const bar = el("div", { class: "cmpbar" },
    el("div", { class: "btnrow" },
      el("label", { class: "k" }, "Play both from ", from),
      el("button", { class: "chip on", type: "button", onclick: playBoth }, "▶ Play both"),
      el("button", { class: "chip", type: "button", onclick: pauseBoth }, "Pause both"),
      el("button", { class: "chip", type: "button", title: "Both back one frame", onclick: () => stepBoth(-1) }, "−1 frame"),
      el("button", { class: "chip", type: "button", title: "Both forward one frame", onclick: () => stepBoth(1) }, "+1 frame"),
      el("label", { class: "k" }, "Speed ", rate),
      el("button", { class: "chip", type: "button", onclick: copyLink }, "Copy link")),
    bothMsg, copyMsg);

  // --- the splits table -----------------------------------------------------------
  let table = null;
  const tableBox = el("div", { class: "tablewrap" });
  function renderTable() {
    fillFrom();
    const [A, B] = c.sides;
    const rows = c.names.map((n, k) => ({ k, name: n }));
    rows.push({ k: -1, name: "End" });
    const at = (s, r) => r.k < 0 ? s.end : s.marks[r.k];
    const segs = s => {
      let prev = s.start;
      return rows.map(r => {
        const t = at(s, r);
        const out = { cum: t !== null && s.start !== null ? t - s.start : null, seg: t !== null && prev !== null ? t - prev : null };
        if (t !== null) prev = t;
        return out;
      });
    };
    const sa = segs(A), sb = segs(B);
    function cell(i, s, x, r) {
      const t = at(s, r), msg = el("div", { class: "rowmsg" });
      return el("td", { class: "r" },
        el("div", { class: "cmpcell" },
          t === null ? el("span", { class: "muted" }, "-")
            : el("button", { class: "linkbtn cmptime" + (x.seg < 0 ? " c-behind-lose" : ""), type: "button",
              title: x.seg < 0 ? "Earlier in the video than the split above - set out of order?" : "Show this moment in the video",
              onclick: () => { c.active = i; goTo(s, t, msg, "time"); } }, time(x.seg)),
          el("button", { class: "chip small", type: "button", title: "Set to the video's current time",
            onclick: () => {
              if (!needVideo(s, msg)) return;
              if (r.k < 0) s.end = cur(s); else s.marks[r.k] = cur(s);
              changed(true);
            } }, "Set"),
          t !== null ? el("button", { class: "chip small", type: "button", title: "Clear", "aria-label": "Clear",
            onclick: () => { if (r.k < 0) s.end = null; else s.marks[r.k] = null; changed(true); } }, "×") : null),
        x.cum !== null ? el("div", { class: "cmpcum" }, time(x.cum)) : null, msg);
    }
    function diff(a, b, d) {
      if (a.seg === null || b.seg === null) return el("td", { class: "r muted" }, "-");
      const ds = b.seg - a.seg, dc = a.cum !== null && b.cum !== null ? b.cum - a.cum : null;
      return el("td", { class: "r" },
        el("div", { class: ds < 0 ? "c-ahead-gain" : ds > 0 ? "c-behind-lose" : "" }, delta(ds)),
        dc !== null ? el("div", { class: "cmpcum " + (dc < 0 ? "c-ahead-lose" : dc > 0 ? "c-behind-gain" : "") }, delta(dc)) : null);
    }
    const nameOf = (i) => "Run " + "AB"[i] + (c.sides[i].label ? " · " + c.sides[i].label : "");
    table = el("table", { class: "cmptable" },
      el("thead", null, el("tr", null, el("th", null, "Split"), el("th", { class: "r" }, nameOf(0)), el("th", { class: "r" }, nameOf(1)),
        el("th", { class: "r", title: "Run B's time minus Run A's: negative = Run B was faster" }, "B − A"))),
      el("tbody", null, rows.map((r, j) => el("tr", null,
        el("td", { class: "name" }, r.k < 0 ? el("span", null, "End") : el("div", { class: "cmpname" },
          el("input", { class: "search", type: "text", maxlength: "40", value: r.name, "aria-label": "Split name",
            oninput: e => { c.names[r.k] = e.target.value; fillFrom(); changed(false); } }),
          el("button", { class: "chip small", type: "button", title: "Remove this split", "aria-label": "Remove this split",
            onclick: () => { c.names.splice(r.k, 1); for (const s of c.sides) s.marks.splice(r.k, 1); changed(true); } }, "×"))),
        cell(0, A, sa[j], r), cell(1, B, sb[j], r), diff(sa[j], sb[j])))));
    tableBox.replaceChildren(table);
  }

  const addBtn = el("button", { class: "chip", type: "button", onclick: () => {
    if (c.names.length >= CMP_MAX_SPLITS) return;
    addRow(); changed(true);
  } }, "+ Add a split");

  show(el("h1", null, "Compare runs"),
    el("p", { class: "note" }, "Two YouTube videos side by side. Set each run's start and end frame (splits too, if you like) - " +
      "the table times every segment of both. Play both lines them up at the start or any split. " +
      "The address holds the whole comparison: copy it to share."),
    bar,
    el("div", { class: "cmpgrid" }, panes.map(p => p.pane)),
    el("h2", null, "Splits"),
    tableBox, addBtn,
    el("p", { class: "note small" }, "Times are frame-exact for the frame rate given: the frame on screen at a time t is t × fps, rounded down. " +
      "Keys (outside the videos): , and . step the last-used video one frame, with Shift ten."));
  renderHeads();
  renderTable();

  // --- every frame: clocks, and keeping the two together ---------------------------
  function tick() {
    raf = requestAnimationFrame(tick);
    for (const p of panes) {
      const s = p.s, pl = s.player, last = p.last;
      const status = !s.id ? "" : pl.error ? pl.error : !pl.heard ? "Starting the player…" : "";
      if (last.status !== status) p.status.textContent = last.status = status;
      const t = s.id ? cur(s) : null;
      const clock = t === null ? "" : "Video " + time(t) + "  ·  frame " + cmpFrame(s, pl.now());
      if (last.clock !== clock) p.clock.textContent = last.clock = clock;
      const run = t === null || s.start === null ? "" : "Run time " + time(t - s.start);
      if (last.run !== run) p.runClock.textContent = last.run = run;
      const st = s.start === null ? "-" : time(s.start), en = s.end === null ? "-" : time(s.end);
      const tot = s.start !== null && s.end !== null ? time(s.end - s.start) : "-";
      if (last.st !== st) p.start.textContent = last.st = st;
      if (last.en !== en) p.end.textContent = last.en = en;
      if (last.tot !== tot) p.total.textContent = last.tot = tot;
    }
    const L = c.link;
    if (!L || !L.playing) return;
    const P = c.sides.map(s => s.player);
    if (P.some(p => p.state === 0)) { L.playing = false; return; }
    // One buffering: hold the other until it plays again.
    if (L.waiting < 0) {
      const w = P.findIndex(p => p.state === 3);
      if (w >= 0 && P[1 - w].playing) { L.waiting = w; P[1 - w].pause(); return; }
      // Paused in the video itself: pause the other too.
      if (performance.now() > L.fixedAt && P.some(p => p.state === 2)) { pauseBoth(); return; }
    } else {
      const w = L.waiting;
      if (P[w].playing) {
        const e = P[w].now() - L.refs[w];
        P[1 - w].seek(L.refs[1 - w] + e); P[1 - w].play();
        L.waiting = -1; L.fixedAt = performance.now() + 1500;
      }
      return;
    }
    // Drifted apart (a stall shorter than a buffering state): pull the one
    // ahead back to the other - it has that part already.
    if (P[0].playing && P[1].playing && performance.now() > L.fixedAt) {
      const e0 = P[0].now() - L.refs[0], e1 = P[1].now() - L.refs[1];
      if (Math.abs(e0 - e1) > 0.25) {
        const ahead = e0 > e1 ? 0 : 1;
        P[ahead].seek(L.refs[ahead] + Math.min(e0, e1));
        L.fixedAt = performance.now() + 1500;
      }
    }
  }
  raf = requestAnimationFrame(tick);

  function onMessage(e) {
    if (e.origin !== YT_ORIGIN) return;
    for (const s of c.sides) if (s.player.frame && e.source === s.player.frame.contentWindow) s.player.receive(e.data);
  }
  function onKey(e) {
    if (e.target.closest && e.target.closest("input, select, textarea")) return;
    if (e.ctrlKey || e.metaKey || e.altKey) return;
    const n = e.key === "," || e.key === "<" ? -1 : e.key === "." || e.key === ">" ? 1 : 0;
    if (!n) return;
    e.preventDefault();
    step(c.sides[c.active], e.shiftKey ? n * 10 : n);
  }
  window.addEventListener("message", onMessage);
  document.addEventListener("keydown", onKey);
  cleanup = () => {
    cancelAnimationFrame(raf); clearTimeout(urlTimer);
    window.removeEventListener("message", onMessage);
    document.removeEventListener("keydown", onKey);
    for (const s of c.sides) s.player.dispose();
  };
}
