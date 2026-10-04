// forest.deter.cloud: the spot list, a spot (map, board, splits), about,
// the author's admin page (admin.js).
// Paths: /, /spot/<id>, /spot/<id>/<route>, /attempt/<id> (attempt.js), /compare (compare.js), /about, /admin[/<tab>] - the
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
    // Through the CSSOM: a style="" attribute is blocked by the CSP.
    else if (k === "style") { if (attrs[k]) e.style.cssText = attrs[k]; }
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

/// A spot's category as the list groups it: the plugin's defaults ("My
/// spots", "Segments", "Spots") say nothing and group as "Other".
const PLAIN_CATEGORIES = new Set(["", "my spots", "spots", "segments"]);
function categoryOf(s) {
  const c = (s.category || "").trim();
  return PLAIN_CATEGORIES.has(c.toLowerCase()) ? "" : c;
}

/// The spot list's folded groups, kept in this browser only.
const FOLDED_KEY = "forest.folded";
function readFolded() {
  try {
    const v = JSON.parse(localStorage.getItem(FOLDED_KEY) || "{}");
    return v && typeof v === "object" && !Array.isArray(v) ? v : {};
  } catch (e) { return {}; }
}
function writeFolded(v) { try { localStorage.setItem(FOLDED_KEY, JSON.stringify(v)); } catch (e) { /* not kept: fine */ } }

// --- router -------------------------------------------------------------------------

let cleanup = null;
function route() {
  if (cleanup) { cleanup(); cleanup = null; }
  if (location.hash.startsWith("#/")) history.replaceState(null, "", location.hash.slice(1));
  const parts = location.pathname.replace(/^\/+|\/+$/g, "").split("/").map(decodeURIComponent);
  const here = parts[0] === "about" || parts[0] === "compare" ? "/" + parts[0] : "/";
  document.querySelectorAll(".top nav a").forEach(a => a.classList.toggle("on", a.getAttribute("href") === here));
  window.scrollTo(0, 0);
  if (parts[0] === "spot" && parts[1]) return spotPage(parts[1], parts[2]);
  if (parts[0] === "attempt" && parts[1]) return attemptPage(parts[1]);
  if (parts[0] === "about") return aboutPage();
  if (parts[0] === "compare") return comparePage();
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
  // Folded groups, per viewer (this browser): { key: true / false }; a key
  // missing = the group's default. Searching opens everything it finds.
  const folded = readFolded();
  function render() {
    const q = search.value.trim().toLowerCase();
    const shown = spots.filter(s => !q || (s.name + " " + s.category + " " + (s.by || "")).toLowerCase().includes(q));
    // Runners' spots are the timed ones with runs - the site's point - and
    // come first; community spots are mostly teleports (author, 2026-09-27),
    // folded by default while none of them has a run.
    const runners = shown.filter(s => !s.community), community = shown.filter(s => s.community);
    const sections = [
      { key: "runners", title: "Runners' spots", spots: runners, open: true,
        empty: "No runs uploaded yet. Runs appear here once runners upload them from the game." },
      { key: "community", title: "Community spots", spots: community, open: spots.some(s => s.community && s.runs > 0),
        empty: "None published yet." },
    ];
    list.replaceChildren(...sections.filter(x => x.spots.length || !q).map(x => {
      const open = q ? true : isOpen(x.key, x.open);
      const body = !open ? null : x.spots.length ? spotGroups(x.key, x.spots, q) : el("p", { class: "empty" }, x.empty);
      return el("section", { class: "group" + (open ? "" : " folded") },
        foldHeading("h2", x.key, x.title, x.spots.length, open, !q), body);
    }));
    if (q && !shown.length) list.append(el("p", { class: "empty" }, "Nothing matches “" + search.value + "”."));
  }
  function isOpen(key, def) { return key in folded ? !folded[key] : def; }
  function foldHeading(tag, key, title, count, open, foldable) {
    return el(tag, { class: "fold" }, el("button", {
      type: "button", "aria-expanded": String(open), disabled: !foldable,
      title: foldable ? (open ? "Hide" : "Show") + " " + title : null,
      onclick: () => { folded[key] = open; writeFolded(folded); render(); },
    }, el("span", { class: "caret", "aria-hidden": "true" }, open ? "▾" : "▸"), title, el("span", { class: "count" }, String(count))));
  }
  /// A section's spots by category (the runner's own, set in the game's
  /// editor); one list when they all share one.
  function spotGroups(section, list, q) {
    const byCat = new Map();
    for (const s of list) {
      const c = categoryOf(s);
      if (!byCat.has(c)) byCat.set(c, []);
      byCat.get(c).push(s);
    }
    if (byCat.size < 2) return spotList(list, true);
    const cats = [...byCat.keys()].sort((a, b) => (a === "") - (b === "") || a.localeCompare(b, undefined, { sensitivity: "base" }));
    return cats.map(c => {
      const key = section + "/" + c, open = q ? true : isOpen(key, true);
      return el("div", { class: "subgroup" + (open ? "" : " folded") },
        foldHeading("h3", key, c || "Other", byCat.get(c).length, open, !q), open ? spotList(byCat.get(c), false) : null);
    });
  }
  function spotList(list, withCategory) {
    return el("ul", { class: "spots" }, list.map(s => el("li", null,
      el("a", { href: "/spot/" + encodeURIComponent(s.id) },
        el("span", { class: "name" }, s.name, el("span", { class: "sub" },
          "  " + [withCategory ? s.category : "", s.by ? "by " + s.by : ""].filter(Boolean).join(" · "))),
        el("span", { class: "meta" }, s.runners ? s.runners + (s.runners === 1 ? " runner" : " runners") : s.timed === false ? "teleport" : "no runs yet"),
        el("span", { class: "best" }, time(s.best))))));
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

/// The map's background: Photo (the aerial capture), Ground (the same with
/// the trees removed) or Relief, and Water: the sea on or off (the photo
/// layers' "-dry" twins, captured with the game's ocean hidden). Shown only
/// when aerial tiles are uploaded; the choice is remembered in this
/// browser. onChange: the 3D view follows.
function mapLayers(map, onChange) {
  const KEY = "forest.mapLayer", WATER = "forest.mapWater";
  let saved = null, water = true, meta = null;
  try { saved = localStorage.getItem(KEY); water = localStorage.getItem(WATER) !== "off"; } catch (e) { /* private window: the default */ }
  let base = saved || "canopy";
  map.layer = base;
  const buttons = [["canopy", "Photo"], ["ground", "Ground"], ["relief", "Relief"]].map(([layer, label]) =>
    el("button", { type: "button", "data-layer": layer, onclick: () => pick(layer, true) }, label));
  const waterButton = el("button", { type: "button", hidden: true, title: "Show or hide the sea", onclick: () => {
    water = !water;
    try { localStorage.setItem(WATER, water ? "on" : "off"); } catch (e) { /* not kept: fine */ }
    pick(base, false);
  } }, "Water");
  const box = el("div", { class: "maplayers", role: "group", "aria-label": "Map background", hidden: true }, [...buttons, waterButton]);
  function pick(layer, remember) {
    base = layer;
    const dry = layer + "-dry";
    const shown = !water && meta && meta.layers.includes(dry) ? dry : layer;
    map.setLayer(shown);
    if (onChange) onChange(shown);
    for (const b of buttons) { const on = b.dataset.layer === layer; b.classList.toggle("on", on); b.setAttribute("aria-pressed", on); }
    waterButton.hidden = !meta || !meta.layers.includes(dry);
    waterButton.classList.toggle("on", water); waterButton.setAttribute("aria-pressed", water);
    if (remember) try { localStorage.setItem(KEY, layer); } catch (e) { /* not kept: fine */ }
  }
  RunMap.aerialReady.then(m => {
    if (!m) return;
    meta = m;
    for (const b of buttons) b.hidden = b.dataset.layer !== "relief" && !meta.layers.includes(b.dataset.layer);
    const usable = buttons.some(b => !b.hidden && b.dataset.layer === base);
    pick(usable ? base : meta.layers[0], false);
    box.hidden = false;
  });
  return box;
}

/// The Buildings / Markers switches: the focused run's structures and
/// interaction markers on both maps (plugin replays). Each is shown only
/// when that run has some; the choice is remembered in this browser.
/// onChange(buildings, markers).
function markSwitches(onChange) {
  const KEYS = { buildings: "forest.mapBuildings", markers: "forest.mapMarkers" };
  const on = { buildings: true, markers: true };
  for (const k in KEYS) try { on[k] = localStorage.getItem(KEYS[k]) !== "off"; } catch (e) { /* private window: on */ }
  const make = (k, label, title) => el("button", { type: "button", hidden: true, title, onclick: () => {
    on[k] = !on[k];
    try { localStorage.setItem(KEYS[k], on[k] ? "on" : "off"); } catch (e) { /* not kept: fine */ }
    mark(); onChange(on.buildings, on.markers);
  } }, label);
  const buttons = {
    buildings: make("buildings", "Buildings", "Show or hide the focused run's blueprints and structures"),
    markers: make("markers", "Markers", "Show or hide what the focused run did along its line (crafting, caves, pause menu...)"),
  };
  const box = el("div", { class: "maplayers", role: "group", "aria-label": "Run details on the map", hidden: true }, buttons.buildings, buttons.markers);
  function mark() { for (const k in buttons) { buttons[k].classList.toggle("on", on[k]); buttons[k].setAttribute("aria-pressed", on[k]); } }
  mark();
  return {
    box, get buildings() { return on.buildings; }, get markers() { return on.markers; },
    /// Which switches the focused run needs (it has buildings / events).
    show(hasBuildings, hasMarkers) {
      buttons.buildings.hidden = !hasBuildings; buttons.markers.hidden = !hasMarkers;
      box.hidden = !hasBuildings && !hasMarkers;
    },
  };
}

/// The map's 2D / 3D switch and, in 3D, Follow (a camera behind the focused
/// run's ghost). 2D is the default and not remembered. map3d.js (three.js) is
/// loaded the first time 3D opens; its URL carries a version like the other
/// scripts (index.html's data-map3d-src, stamped by the server).
function mapViews(canvas, map, hint, empty, hooks) {
  const canvas3d = el("canvas", { class: "map3d", hidden: true, "aria-label": "3D view of the spot's zones and the runs shown" });
  const HINT2 = hint.textContent;
  const HINT3 = "click a line · drag to turn · right-drag to move · scroll to zoom · double-click to fit";
  let view = null, loading = null, on = false, gone = false;
  const b2 = el("button", { type: "button", onclick: () => show(false) }, "2D");
  const b3 = el("button", { type: "button", onclick: () => show(true) }, "3D");
  const follow = el("button", { type: "button", hidden: true, title: "Ride behind the focused run's ghost", onclick: () => setFollow(!follow.classList.contains("on")) }, "Follow");
  const box = el("div", { class: "maplayers", role: "group", "aria-label": "Map view" }, b2, b3, follow);
  function mark() {
    for (const [b, v] of [[b2, !on], [b3, on]]) { b.classList.toggle("on", v); b.setAttribute("aria-pressed", v); }
    follow.hidden = !on;
    hint.textContent = on ? HINT3 : HINT2;
  }
  function setFollow(v) {
    follow.classList.toggle("on", v); follow.setAttribute("aria-pressed", v);
    if (view) view.setMode(v ? "follow" : "orbit");
  }
  async function show(want) {
    if (want === on) return;
    if (want && !view) {
      if (!loading) {
        const src = (document.querySelector("[data-map3d-src]") || { dataset: {} }).dataset.map3dSrc || "/map3d.js";
        empty.dataset.busy = "Loading 3D…"; empty.textContent = empty.dataset.busy;
        loading = import(src).then(mod => mod.create(canvas3d, hooks)).finally(() => { delete empty.dataset.busy; });
      }
      try { view = await loading; } catch (e) {
        loading = null;
        empty.textContent = "3D could not start: " + (e && e.message || e);
        return;
      }
      if (gone) { view.dispose(); return; }
      canvas.hidden = true; canvas3d.hidden = false;   // shown first: the first fit needs its size
      hooks.sync(view);
    }
    on = want;
    canvas.hidden = on; canvas3d.hidden = !on;
    if (view) view.setVisible(on);
    if (!on) map.draw();
    hooks.empty();
    mark();
  }
  mark();
  return {
    box, canvas: canvas3d,
    get view() { return view; },
    dispose() { gone = true; if (view) view.dispose(); },
  };
}

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
  // A link to one run (?run=<id>, the Discord PB posts): shown and focused.
  const linked = Number(new URLSearchParams(location.search).get("run"));
  if (linked && r.board.some(b => b.id === linked)) {
    if (!state.shown.has(linked)) state.shown.set(linked, COLORS[state.shown.size % COLORS.length]);
    state.focus = linked;
  }

  const canvas = el("canvas", { "aria-label": "Map of the spot's zones and the runs shown" });
  const map = new RunMap(canvas);
  const layerCtl = mapLayers(map, layer => { if (views && views.view) views.view.setLayer(layer); });
  let views = null;
  const zones = [];
  const addZone = (t, role, label) => { if (t && (t.kind === "zone" || t.kind === "box" || t.kind === "poly")) zones.push(Object.assign({ role, label }, t)); };
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
    if (views && views.view) views.view.setTime(state.time);
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
  // Playback calls this every frame. The panel is rebuilt only when its
  // structure or a 5 Hz sample changes; the clock and the speed (30 Hz) are
  // written into their spans in place - rebuilding every frame made playback
  // stutter (author, 2026-10-01).
  let stateKey = "", stateLive = null;
  function renderState() {
    const run = r.board.find(b => b.id === state.focus);
    if (!run) { statePanel.replaceChildren(); stateKey = ""; stateLive = null; return; }
    const data = (state.showAll ? state.all : state.runs).get(run.id);
    if (!data) { load(run.id, state.showAll).then(renderState); return; }
    const st = data.state, s = st && sampleAt(st.samples, state.time);
    const pos = RunMap.at(data.path || [], state.time);
    const bagKey = data.items && data.items.length ? data.items.filter(c => c[0] <= state.time).length : 0;
    // What the run did so far (plugin replays: [t, kind, label, x, y, z]).
    const doneKey = data.events && data.events.length ? data.events.filter(e => e[0] <= state.time).length : 0;
    const key = run.id + "|" + state.showAll + "|" + (s ? s[0] : "") + "|" + !!pos + "|" + bagKey + "|" + doneKey;
    if (key === stateKey && stateLive) {
      const clockText = "State · " + (run.name || run.runner) + " at " + time(state.time);
      if (stateLive.title.textContent !== clockText) stateLive.title.textContent = clockText;
      if (pos && stateLive.speed) {
        const v = pos[4].toFixed(1) + " m/s";
        if (stateLive.speed.textContent !== v) stateLive.speed.textContent = v;
      }
      return;
    }
    stateKey = key;
    const items = [];
    const speed = pos ? el("span", { class: "v" }, pos[4].toFixed(1) + " m/s") : null;
    if (pos) items.push(el("div", { class: "stat" }, el("span", { class: "k" }, "Speed"), speed));
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
    const done = doneKey ? data.events.slice(Math.max(0, doneKey - 5), doneKey).reverse().map(e =>
      el("div", { class: "stat" }, el("span", { class: "k" }, time(e[0])), el("span", { class: "v" }, e[2]))) : [];
    const more = st || state.showAll ? el("button", { class: "linkbtn", onclick: () => { state.showAll = !state.showAll; renderState(); } },
      state.showAll ? "Show fewer" : "Show all") : null;
    const title = el("h2", null, "State · " + (run.name || run.runner) + " at " + time(state.time));
    stateLive = { title, speed };
    statePanel.replaceChildren(...[
      el("div", { class: "splitsbar" }, title, more),
      el("div", { class: state.showAll ? "stats all" : "stats" }, items),
      bag.length ? el("h3", { class: "bagtitle" }, "Carrying") : null,
      bag.length ? el("div", { class: state.showAll ? "stats all" : "stats bag" }, bag) : null,
      done.length ? el("h3", { class: "bagtitle" }, "Last done") : null,
      done.length ? el("div", { class: "stats all" }, done) : null,
      s ? null : el("p", { class: "empty" }, data.failed ? "Could not load this run." : "This run has no player state recorded.")].filter(Boolean));
  }

  let shownRuns = [], emptyText = "";
  function showEmpty() { if (!mapEmpty.dataset.busy) mapEmpty.textContent = emptyText; }
  function setFocus(id) { state.focus = id; if (views && views.view) views.view.setFocus(id); updateMarks(); }

  // The focused run's buildings and markers - that run only, and only while
  // its line is on the map, to keep the map readable.
  const marks = markSwitches((b, m) => {
    map.setMarkOptions(b, m);
    if (views && views.view) views.view.setMarkOptions(b, m);
  });
  map.setMarkOptions(marks.buildings, marks.markers);
  let shownMarks = null;
  function marksNow() {
    const run = shownRuns.find(x => x.id === state.focus), data = run && state.runs.get(run.id);
    if (!data) return null;
    const events = data.events || [], buildings = data.buildings || [];
    if (!events.length && !buildings.length) return null;
    if (shownMarks && shownMarks.run === run && shownMarks.events === events) return shownMarks;
    return { run, events, buildings };
  }
  function updateMarks() {
    shownMarks = marksNow();
    marks.show(!!(shownMarks && shownMarks.buildings.length), !!(shownMarks && shownMarks.events.length));
    map.setMarks(shownMarks);
    if (views && views.view) views.view.setMarks(shownMarks);
  }

  async function refreshMap(refit) {
    const ids = [...state.shown.keys()];
    await Promise.all(ids.map(id => load(id, false)));
    shownRuns = ids.filter(id => state.shown.has(id)).map(id => ({ id, color: state.shown.get(id), path: pathOf(id), plane: (state.runs.get(id) || {}).plane }));
    map.setRuns(shownRuns, refit);
    if (views && views.view) views.view.setRuns(shownRuns, refit);
    updateMarks();
    emptyText = ids.length ? "" : r.board.length ? "Tick a run to show its line." : "No runs on this route yet.";
    showEmpty();
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
      return el("tr", { class: b.id === state.focus ? "focus" : "", onclick: () => { setFocus(b.id); renderBoard(); renderSplits(); renderState(); } },
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
    const det = state.runs.get(run.id) || {};
    if (!state.runs.has(run.id)) load(run.id, false).then(renderSplits);   // once: a failed fetch is cached too
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
          // Load-removed time (runs that tracked loads, from the run's own
          // JSON once fetched): shown when it differs from the time.
          det.loads > 0 && det.lrt != null ? el("tr", null, el("td", null, "Load-removed"), el("td"),
            el("td", { class: "r" }, det.loads + (det.loads === 1 ? " load" : " loads")), el("td", { class: "r" }, time(det.lrt))) : null,
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
    if (state.focus !== run.id) { setFocus(run.id); renderBoard(); renderSplits(); }
    setTime(t);
  };
  play.addEventListener("click", toggle);
  slider.addEventListener("input", () => { setTime(slider.value / 1000 * maxTime()); });
  speed.addEventListener("change", () => { state.speed = +speed.value; });
  const onKey = e => { if (e.code === "Space" && e.target.tagName !== "INPUT" && e.target.tagName !== "SELECT") { e.preventDefault(); toggle(); } };
  document.addEventListener("keydown", onKey);
  cleanup = () => { state.playing = false; cancelAnimationFrame(raf); document.removeEventListener("keydown", onKey); if (views) views.dispose(); };

  const hint = el("div", { class: "maphint" }, "click a line · drag · scroll to zoom · double-click to fit");
  views = mapViews(canvas, map, hint, mapEmpty, {
    onPick: (run, t) => map.onPick(run, t),
    empty: showEmpty,
    /// A new 3D view gets what the 2D map shows.
    sync(v) {
      v.setLayer(map.layer); v.setZones(zones); v.setFocus(state.focus); v.setRuns(shownRuns, true);
      v.setMarkOptions(marks.buildings, marks.markers); v.setMarks(shownMarks); v.setTime(state.time);
    },
  });

  const routeChips = spot.routes.length > 1 ? el("div", { class: "routes" }, spot.routes.map((x, i) =>
    el("a", { class: "chip" + (x === r ? " on" : ""), href: "/spot/" + encodeURIComponent(spot.id) + "/" + x.route },
      i === 0 ? "Current route" : "Older version · " + date(x.firstSeen)))) : null;

  show(
    el("h1", null, spot.name),
    el("p", { class: "sub" }, spot.category + (spot.community ? " · community spot" : spot.by ? " · by " + spot.by : "") + " · " +
      r.runs + (r.runs === 1 ? " run" : " runs") + (r === spot.routes[0] ? "" : " · an older version of this route")),
    spot.notes ? el("p", { class: "note" }, spot.notes) : null,
    routeChips,
    el("div", { class: "mapwrap" }, canvas, views.canvas, mapEmpty,
      el("div", { class: "maptools" }, views.box, layerCtl, marks.box, hint),
      el("div", { class: "scrub" }, play, slider, clock, speed)),
    statePanel,
    el("div", { class: "cols" },
      el("section", null, el("h2", null, "Runners"),
        el("div", { class: "tablewrap" }, el("table", null,
          el("thead", null, el("tr", null, el("th", null, "#"), el("th", { title: "Show on the map" }, ""), el("th", null, "Runner"), el("th", { class: "r" }, "Time"), el("th", { class: "r col-date" }, "Date"))),
          board))),
      el("section", null, splits)),
    spot.community ? null : el("p", { class: "note owner" },
      "Your spot? Delete it from the game: Practice → select it → Share → Delete from the website. " +
      "Only the runner who uploaded it first can, and only while nobody else has runs on it."));

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
      el("li", null, "Each runner shows under their Steam name (changeable in the game). The runner id behind it is " +
        "made from your Steam account and is not anonymous: someone determined could match it to your Steam profile.")),
    el("h2", null, "What the numbers mean"),
    el("p", null, "Times only compare on the same version of a spot. When a spot's zones or start state change, " +
      "older runs stay under “Older version”. This is a comparison board, not a verified leaderboard."),
    el("p", null, el("a", { href: "https://github.com/1deter/forest-speedrun-tool" }, "ForestOverlay on GitHub →"))));
}

route();
