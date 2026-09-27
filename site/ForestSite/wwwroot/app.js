// forest.deter.cloud: the spot list, a spot (map, board, splits), about,
// the author's admin page (admin.js).
// Paths: /, /spot/<id>, /spot/<id>/<route>, /about, /admin[/<tab>] - the
// server answers each with this page; links move by history.pushState.
// Old hash links (#/spot/<id>, plugins before v0.24.158) are rewritten.
"use strict";

const view = document.getElementById("view");
const COLORS = ["#e5c501", "#5fb3ff", "#ff7a59", "#7ddc7a", "#d58cff", "#eaeaea", "#46d9d0", "#ff5fa8"];

// --- helpers --------------------------------------------------------------------

function el(tag, attrs, ...kids) {
  const e = document.createElement(tag);
  for (const k in attrs || {}) {
    if (k === "class") e.className = attrs[k];
    else if (k.startsWith("on")) e.addEventListener(k.slice(2), attrs[k]);
    else if (attrs[k] !== null && attrs[k] !== undefined && attrs[k] !== false) e.setAttribute(k, attrs[k]);
  }
  for (const k of kids.flat()) if (k !== null && k !== undefined) e.append(k.nodeType ? k : String(k));
  return e;
}

async function api(path) {
  const r = await fetch("/api" + path);
  if (!r.ok) throw new Error((await r.json().catch(() => ({}))).error || r.statusText);
  return r.json();
}

/// Data/SplitTable.Time: "12.345", "1:02.345", "1:02:03.456"; "-" unknown.
/// Three decimals everywhere on the site (author, 2026-09-27).
function time(t, decimals = 3) {
  if (t === null || t === undefined || !isFinite(t)) return "-";
  const neg = t < 0; t = Math.abs(t);
  const scale = 10 ** decimals, u = Math.round(t * scale);
  const whole = Math.floor(u / scale), frac = u % scale;
  const h = Math.floor(whole / 3600), m = Math.floor(whole / 60) % 60, s = whole % 60;
  let text = h > 0 ? h + ":" + String(m).padStart(2, "0") + ":" + String(s).padStart(2, "0")
           : m > 0 ? m + ":" + String(s).padStart(2, "0") : String(s);
  if (decimals > 0) text += "." + String(frac).padStart(decimals, "0");
  return (neg ? "-" : "") + text;
}
function delta(d) { return d === null || !isFinite(d) ? "" : (d < 0 ? "-" : "+") + time(Math.abs(d)); }

/// Data/SplitTable.ColourOf.
function colour(d, segDelta, seg, best) {
  if (d === null || !isFinite(d)) return "";
  if (isFinite(seg) && isFinite(best) && seg < best - 0.0005) return "c-gold";
  const ahead = d < 0, gaining = isFinite(segDelta) ? segDelta < 0 : ahead;
  return ahead ? (gaining ? "c-ahead-gain" : "c-ahead-lose") : (gaining ? "c-behind-gain" : "c-behind-lose");
}

function num(x) { return x === null || x === undefined ? NaN : x; }
function segAt(cum, i) { return num(cum[i]) - (i === 0 ? 0 : num(cum[i - 1])); }
/// "27 Sep" this year, "27 Sep 2025" before.
function date(iso) {
  const d = new Date(iso);
  if (isNaN(d)) return "";
  const opts = { month: "short", day: "numeric" };
  if (d.getFullYear() !== new Date().getFullYear()) opts.year = "numeric";
  return d.toLocaleDateString(undefined, opts);
}

// --- router -------------------------------------------------------------------------

let cleanup = null;
function route() {
  if (cleanup) { cleanup(); cleanup = null; }
  if (location.hash.startsWith("#/")) history.replaceState(null, "", location.hash.slice(1));
  const parts = location.pathname.replace(/^\/+|\/+$/g, "").split("/").map(decodeURIComponent);
  document.querySelectorAll(".top nav a").forEach(a => a.classList.toggle("on",
    (a.getAttribute("href") === "/about") === (parts[0] === "about")));
  window.scrollTo(0, 0);
  if (parts[0] === "spot" && parts[1]) return spotPage(parts[1], parts[2]);
  if (parts[0] === "about") return aboutPage();
  if (parts[0] === "admin") return adminPage(parts[1]);
  return homePage();
}
window.addEventListener("popstate", route);
window.addEventListener("hashchange", route);

// Same-site page links stay in the page: no reload, the address changes.
document.addEventListener("click", e => {
  if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
  const a = e.target.closest("a[href^='/']");
  if (!a || a.hasAttribute("download") || a.target || a.getAttribute("href").startsWith("/api/")) return;
  e.preventDefault();
  if (a.getAttribute("href") !== location.pathname) history.pushState(null, "", a.getAttribute("href"));
  route();
});

/// replaceChildren without the nulls (it would print them).
function show(...kids) { view.replaceChildren(...kids.flat().filter(k => k !== null && k !== undefined)); }

function loading() { view.replaceChildren(el("div", { class: "loading" }, "Loading")); }
function failed(e) { view.replaceChildren(el("p", { class: "error" }, "Could not load: " + e.message)); }

// --- the spot list ---------------------------------------------------------------

async function homePage() {
  document.title = "Forest Practice Runs";
  loading();
  let spots;
  try { spots = await api("/spots"); } catch (e) { return failed(e); }

  const list = el("div");
  const search = el("input", { class: "search", type: "search", placeholder: "Search spots", "aria-label": "Search spots" });
  function render() {
    const q = search.value.trim().toLowerCase();
    const shown = spots.filter(s => !q || (s.name + " " + s.category).toLowerCase().includes(q));
    const groups = [["Community spots", shown.filter(s => s.community)], ["Runners' spots", shown.filter(s => !s.community)]];
    list.replaceChildren(...groups.filter(([, g]) => g.length || !q).map(([title, g]) => el("section", null,
      el("h2", null, title),
      g.length ? el("ul", { class: "spots" }, g.map(s => el("li", null,
        el("a", { href: "/spot/" + encodeURIComponent(s.id) },
          el("span", { class: "name" }, s.name, el("span", { class: "sub" }, "  " + s.category + (s.by ? " · by " + s.by : ""))),
          el("span", { class: "meta" }, s.runners ? s.runners + (s.runners === 1 ? " runner" : " runners") : "no runs yet"),
          el("span", { class: "best" }, time(s.best)))))) :
        el("p", { class: "empty" }, title === "Community spots" ? "None published yet." :
          "No runs uploaded yet. Runs appear here once runners upload them from the game."))));
    if (q && !shown.length) list.append(el("p", { class: "empty" }, "Nothing matches “" + search.value + "”."));
  }
  search.addEventListener("input", render);
  render();
  view.replaceChildren(
    el("h1", null, "Spots"),
    el("p", { class: "note" }, "Practice segments and everyone's runs of them: lines, ghosts and splits, recorded by ForestOverlay in game."),
    search, list);
  search.focus({ preventScroll: true });
}

// --- a spot -------------------------------------------------------------------------

async function spotPage(id, routeId) {
  loading();
  let spot;
  try { spot = await api("/spots/" + encodeURIComponent(id)); } catch (e) { return failed(e); }
  document.title = spot.name + " · Forest Practice Runs";
  const r = spot.routes.find(x => x.route === routeId) || spot.routes[0];

  // What is shown: the top three on the map, the best one's splits.
  const state = { shown: new Map(), focus: null, compare: "first", runs: new Map(), all: new Map(), showAll: false, time: 0, playing: false, speed: 1 };
  r.board.slice(0, 3).forEach((b, i) => state.shown.set(b.id, COLORS[i]));
  state.focus = r.board.length ? r.board[0].id : null;

  const canvas = el("canvas", { "aria-label": "Map of the spot's zones and the runs shown" });
  const map = new RunMap(canvas);
  const zones = [];
  const addZone = (t, role, label) => { if (t && (t.kind === "zone" || t.kind === "box")) zones.push(Object.assign({ role, label }, t)); };
  addZone(r.start, "start", "Start");
  r.checks.forEach((t, i) => addZone(t, "check", r.splitNames[i] || "Checkpoint " + (i + 1)));
  addZone(r.end, "end", r.splitNames[r.splitNames.length - 1] || "End");
  map.setZones(zones);

  const mapEmpty = el("div", { class: "mapempty" });
  const clock = el("span", { class: "clock" }, time(0));
  const slider = el("input", { type: "range", min: 0, max: 1000, value: 0, step: 1, "aria-label": "Time" });
  const play = el("button", { title: "Play / pause (space)", "aria-label": "Play" }, "▶");
  const speed = el("select", { "aria-label": "Playback speed" }, [0.5, 1, 2, 4].map(s => el("option", { value: s, selected: s === 1 }, s + "×")));
  const board = el("tbody");
  const splits = el("div");
  const statePanel = el("section", { class: "statepanel" });

  function maxTime() {
    let m = 0;
    for (const id of state.shown.keys()) { const p = pathOf(id); if (p.length) m = Math.max(m, p[p.length - 1][0]); }
    return m;
  }
  function setTime(t) {
    state.time = Math.max(0, Math.min(maxTime(), t));
    clock.textContent = time(state.time);
    const m = maxTime();
    slider.value = m ? Math.round(state.time / m * 1000) : 0;
    map.setTime(state.time);
    renderState();
  }

  function pathOf(id) { const run = state.runs.get(id); return run && run.path || []; }
  /// A run's JSON (path + state), fetched once; all = every state channel.
  async function load(id, all) {
    const cache = all ? state.all : state.runs;
    if (!cache.has(id)) {
      let run;
      try { run = await api("/runs/" + id + (all ? "?all=1" : "")); } catch { run = { path: [], state: null, failed: true }; }
      cache.set(id, run);
    }
    return cache.get(id);
  }

  // The player's state at the scrub time, from the .run's 5 Hz channels.
  // A preset of what runners read (author, 2026-09-27); "Show all" fetches
  // every channel the game records.
  const pct = v => v * 100;
  const SHOWN = {
    Health: { bar: 100 }, Stamina: { bar: 100 }, Energy: { bar: 100 },
    Fullness: { bar: 100, of: pct, unit: "%" }, Thirst: { bar: 100, of: pct, unit: "%" },
    Armor: { label: "Armour" }, ColdArmor: { label: "Cold armour" },
    BatteryCharge: { label: "Battery", bar: 100, unit: "%" },
    BodyTemp: { label: "Body temp", unit: " °C", decimals: 1 }, Stealth: {},
    Cold: { yesNo: true }, IsLit: { label: "Light on", yesNo: true },
  };
  // Items by the game's database name: the run's `items` track (plugin
  // v0.24.161+, every item, as changes) or v0.24.160's "item:<name>"
  // channels. Names not listed here are spaced out ("CamCorderTape").
  const ITEMS = {
    EnergyMix: "Energy mix", Stick: "Sticks", Rock: "Rocks", Log: "Logs", Molotov: "Molotovs",
    BombTimed: "Bombs", Flare: "Flares", Battery: "Batteries",
  };
  const itemLabel = n => ITEMS[n] || n.replace(/([a-z])([A-Z])/g, "$1 $2");
  /// Each item's count at t from the change track: [{ name, n, ever }].
  function bagAt(changes, t) {
    const bag = new Map();
    for (const [ct, name, n] of changes) {
      const e = bag.get(name) || { name, n: 0, ever: false };
      if (ct <= t) e.n = n;
      if (n > 0) e.ever = true;
      bag.set(name, e);
    }
    return [...bag.values()].filter(e => e.ever).sort((a, b) => itemLabel(a.name).localeCompare(itemLabel(b.name)));
  }
  /// Item channels the run ever carried (a run's bag, not every item at 0).
  function carried(st) {
    if (!st.carried) st.carried = st.channels.map((name, i) =>
      !name.startsWith("item:") || st.samples.some(row => row[i + 1] > 0));
    return st.carried;
  }
  /// The last sample at or before t (state is stepped, not blended).
  function sampleAt(samples, t) {
    if (!samples || !samples.length) return null;
    let lo = 0, hi = samples.length - 1;
    while (hi > lo) { const mid = (lo + hi + 1) >> 1; if (samples[mid][0] <= t) lo = mid; else hi = mid - 1; }
    return samples[lo];
  }
  function fmt(v, d) {
    if (v === null || v === undefined) return "-";
    if (d.yesNo) return v ? "yes" : "no";
    const x = d.of ? d.of(v) : v;
    const text = Math.abs(x) >= 1e5 ? x.toExponential(2) : x.toFixed(d.decimals ?? (Number.isInteger(x) ? 0 : 1));
    return text + (d.unit || "");
  }
  let stateKey = "";
  function renderState() {
    const run = r.board.find(b => b.id === state.focus);
    if (!run) { statePanel.replaceChildren(); stateKey = ""; return; }
    const data = (state.showAll ? state.all : state.runs).get(run.id);
    if (!data) { load(run.id, state.showAll).then(renderState); return; }
    const st = data.state, s = st && sampleAt(st.samples, state.time);
    const pos = RunMap.at(data.path || [], state.time);
    // Rebuilt only when what it shows changes (the scrub fires every frame).
    const key = run.id + "|" + state.showAll + "|" + (s ? s[0] : "") + "|" + (pos ? pos[4].toFixed(1) : "") + "|" + time(state.time);
    if (key === stateKey) return;
    stateKey = key;
    const items = [];
    if (pos) items.push(el("div", { class: "stat" }, el("span", { class: "k" }, "Speed"), el("span", { class: "v" }, pos[4].toFixed(1) + " m/s")));
    const bag = [];
    if (s) st.channels.forEach((name, i) => {
      const v = s[i + 1];
      if (name.startsWith("item:")) {
        if (data.items && data.items.length) return;   // the change track wins
        if (!state.showAll && !carried(st)[i]) return;
        const key = name.slice(5);
        bag.push(el("div", { class: "stat" + (v > 0 ? "" : " zero") },
          el("span", { class: "k" }, state.showAll ? name : itemLabel(key)), el("span", { class: "v" }, fmt(v, {}))));
        return;
      }
      const d = SHOWN[name] || {};
      const bar = d.bar && v !== null ? el("span", { class: "bar" },
        el("span", { style: "width:" + Math.max(0, Math.min(100, (d.of ? d.of(v) : v) / d.bar * 100)) + "%" })) : null;
      items.push(el("div", { class: "stat" },
        el("span", { class: "k" }, state.showAll ? name : d.label || name), el("span", { class: "v" }, fmt(v, d)), bar));
    });
    if (data.items && data.items.length)
      for (const e of bagAt(data.items, state.time))
        bag.push(el("div", { class: "stat" + (e.n > 0 ? "" : " zero") },
          el("span", { class: "k" }, itemLabel(e.name)), el("span", { class: "v" }, e.n)));
    const more = st || state.showAll ? el("button", { class: "linkbtn", onclick: () => { state.showAll = !state.showAll; renderState(); } },
      state.showAll ? "Show fewer" : "Show all") : null;
    statePanel.replaceChildren(...[
      el("div", { class: "splitsbar" }, el("h2", null, "State · " + (run.name || run.runner) + " at " + time(state.time)), more),
      el("div", { class: state.showAll ? "stats all" : "stats" }, items),
      bag.length ? el("h3", { class: "bagtitle" }, "Carrying") : null,
      bag.length ? el("div", { class: state.showAll ? "stats all" : "stats bag" }, bag) : null,
      s ? null : el("p", { class: "empty" }, data.failed ? "Could not load this run." : "This run has no player state recorded.")].filter(Boolean));
  }

  async function refreshMap(refit) {
    const ids = [...state.shown.keys()];
    await Promise.all(ids.map(id => load(id, false)));
    map.setRuns(ids.filter(id => state.shown.has(id)).map(id => ({ id, color: state.shown.get(id), path: pathOf(id), plane: (state.runs.get(id) || {}).plane })), refit);
    mapEmpty.textContent = ids.length ? "" : r.board.length ? "Tick a run to show its line." : "No runs on this route yet.";
    setTime(state.time);
  }

  function nextColor() {
    const used = new Set(state.shown.values());
    return COLORS.find(c => !used.has(c)) || COLORS[state.shown.size % COLORS.length];
  }

  function renderBoard() {
    board.replaceChildren(...r.board.map((b, i) => {
      const on = state.shown.has(b.id);
      const sw = el("span", { class: "swatch" + (on ? " on" : ""), style: on ? "background:" + state.shown.get(b.id) : null });
      const toggle = el("button", {
        class: "chip", style: "padding:2px 6px;border:0", title: on ? "Hide from the map" : "Show on the map",
        "aria-pressed": on ? "true" : "false",
        onclick: e => { e.stopPropagation(); if (on) state.shown.delete(b.id); else state.shown.set(b.id, nextColor()); renderBoard(); refreshMap(false); },
      }, sw);
      return el("tr", { class: b.id === state.focus ? "focus" : "", onclick: () => { state.focus = b.id; renderBoard(); renderSplits(); renderState(); } },
        el("td", { class: "rank" }, i + 1),
        el("td", null, toggle),
        el("td", { class: "name" }, b.name || b.runner, b.flagged ? el("span", { class: "flag", title: "Much faster than the route's best so far: waiting for a look" }, "under review") : null),
        el("td", { class: "r" }, time(b.duration)),
        el("td", { class: "r date col-date" }, date(b.recorded)));
    }));
    if (!r.board.length) board.append(el("tr", null, el("td", { colspan: 5, class: "empty" },
      "No runs yet. Runs upload from the game when a runner finishes this spot.")));
  }

  function renderSplits() {
    const run = r.board.find(b => b.id === state.focus);
    if (!run) { splits.replaceChildren(); return; }
    const first = r.board[0];
    let cmp, cmpName;
    if (state.compare === "golds") {
      let t = 0; cmp = r.bestSegments.map(s => (t += num(s)));
      cmpName = "best segments";
    } else { cmp = first.splits.map(num); cmpName = first.name || first.runner; }

    const rows = r.splitNames.map((name, i) => {
      const t = num(run.splits[i]), c = num(cmp[i]);
      const seg = segAt(run.splits, i), cseg = segAt(cmp, i), gold = num(r.bestSegments[i]);
      const d = t - c, sd = seg - cseg;
      const cls = colour(d, sd, seg, gold);
      return el("tr", null,
        el("td", { class: "name" }, name),
        el("td", { class: "r " + cls }, run.id === first.id && state.compare === "first" ? "" : delta(d)),
        el("td", { class: "r" }, time(seg)),
        el("td", { class: "r" }, time(t)));
    });
    const compareSel = el("select", { "aria-label": "Compare to", onchange: e => { state.compare = e.target.value; renderSplits(); } },
      el("option", { value: "first", selected: state.compare === "first" }, "#1 (" + (first.name || first.runner) + ")"),
      el("option", { value: "golds", selected: state.compare === "golds" }, "Best segments"));
    splits.replaceChildren(
      el("div", { class: "splitsbar" },
        el("h2", null, "Splits · " + (run.name || run.runner)),
        el("label", { class: "sub" }, "Compare to ", compareSel)),
      el("div", { class: "tablewrap" }, el("table", null,
        el("thead", null, el("tr", null, el("th", null, "Split"), el("th", { class: "r" }, "+/-"), el("th", { class: "r" }, "Segment"), el("th", { class: "r" }, "Time"))),
        el("tbody", null, rows),
        el("tbody", { class: "summary" },
          el("tr", null, el("td", null, "Sum of best"), el("td"), el("td"), el("td", { class: "r" }, time(r.sumOfBest))),
          el("tr", null, el("td", null, "Compared to"), el("td", { colspan: 3, class: "r" }, cmpName))))),
      el("a", { class: "btn", href: "/api/runs/" + run.id + "/file", download: "" }, "Download .run"));
  }

  // Playback: real time scaled by the speed setting.
  let raf = 0, last = 0;
  function tick(now) {
    if (!state.playing) return;
    if (last) setTime(state.time + (now - last) / 1000 * state.speed);
    last = now;
    if (state.time >= maxTime()) { state.playing = false; play.textContent = "▶"; return; }
    raf = requestAnimationFrame(tick);
  }
  function toggle() {
    state.playing = !state.playing;
    play.textContent = state.playing ? "❚❚" : "▶";
    play.setAttribute("aria-label", state.playing ? "Pause" : "Play");
    if (state.playing) { if (state.time >= maxTime()) setTime(0); last = 0; raf = requestAnimationFrame(tick); }
  }
  // A click on a line: that run, at that point.
  map.onPick = (run, t) => {
    if (state.playing) toggle();
    if (state.focus !== run.id) { state.focus = run.id; renderBoard(); renderSplits(); }
    setTime(t);
  };
  play.addEventListener("click", toggle);
  slider.addEventListener("input", () => { setTime(slider.value / 1000 * maxTime()); });
  speed.addEventListener("change", () => { state.speed = +speed.value; });
  const onKey = e => { if (e.code === "Space" && e.target.tagName !== "INPUT" && e.target.tagName !== "SELECT") { e.preventDefault(); toggle(); } };
  document.addEventListener("keydown", onKey);
  cleanup = () => { state.playing = false; cancelAnimationFrame(raf); document.removeEventListener("keydown", onKey); };

  const routeChips = spot.routes.length > 1 ? el("div", { class: "routes" }, spot.routes.map((x, i) =>
    el("a", { class: "chip" + (x === r ? " on" : ""), href: "/spot/" + encodeURIComponent(spot.id) + "/" + x.route },
      i === 0 ? "Current route" : "Older version · " + date(x.firstSeen)))) : null;

  show(
    el("h1", null, spot.name),
    el("p", { class: "sub" }, spot.category + (spot.community ? " · community spot" : spot.by ? " · by " + spot.by : "") + " · " +
      r.runs + (r.runs === 1 ? " run" : " runs") + (r === spot.routes[0] ? "" : " · an older version of this route")),
    spot.notes ? el("p", { class: "note" }, spot.notes) : null,
    routeChips,
    el("div", { class: "mapwrap" }, canvas, mapEmpty,
      el("div", { class: "maphint" }, "click a line · drag · scroll to zoom · double-click to fit"),
      el("div", { class: "scrub" }, play, slider, clock, speed)),
    statePanel,
    el("div", { class: "cols" },
      el("section", null, el("h2", null, "Runners"),
        el("div", { class: "tablewrap" }, el("table", null,
          el("thead", null, el("tr", null, el("th", null, "#"), el("th", { title: "Show on the map" }, ""), el("th", null, "Runner"), el("th", { class: "r" }, "Time"), el("th", { class: "r col-date" }, "Date"))),
          board))),
      el("section", null, splits)));

  renderBoard();
  renderSplits();
  renderState();
  refreshMap(true);
}

// --- about -----------------------------------------------------------------------------

function aboutPage() {
  document.title = "About · Forest Practice Runs";
  view.replaceChildren(el("div", { class: "about" },
    el("h1", null, "About"),
    el("p", null, "Runs here are recorded in game by ForestOverlay, a practice tool for The Forest speedrunning: " +
      "timed segments with checkpoints, savestates, ghosts and splits."),
    el("h2", null, "How runs get here"),
    el("ol", null,
      el("li", null, "Install ForestOverlay (BepInEx plugin) and practise a timed spot."),
      el("li", null, "Finished attempts upload by themselves once uploads are switched on in the game."),
      el("li", null, "Each runner shows under their Steam name; the id behind it is a hash, never the Steam id.")),
    el("h2", null, "What the numbers mean"),
    el("p", null, "Times only compare on the same version of a spot. When a spot's zones or start state change, " +
      "older runs stay under “Older version”. This is a comparison board, not a verified leaderboard."),
    el("p", null, el("a", { href: "https://github.com/1deter/forest-speedrun-tool" }, "ForestOverlay on GitHub →"))));
}

route();
