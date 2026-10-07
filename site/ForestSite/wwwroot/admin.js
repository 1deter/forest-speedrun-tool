// forest.deter.cloud: the author's page, /admin (not in the nav).
// Spot submissions (approve / reject), runs flagged as much faster than the
// route's best, spots (delete an accidental upload), runners (ban, reset a
// lost token), the activity log, and - the owner only - other admins' tokens.
// Every call carries this admin's token, kept in this browser only.
// Uses el / time / date / show / loading from app.js.
"use strict";

const ADMIN_KEY = "forest-admin-token";
function adminToken() { try { return localStorage.getItem(ADMIN_KEY) || ""; } catch { return ""; } }
function setAdminToken(t) { try { if (t) localStorage.setItem(ADMIN_KEY, t); else localStorage.removeItem(ADMIN_KEY); } catch { } }

async function adminCall(method, path, body) {
  const r = await fetch("/api/admin" + path, { method, headers: { "X-Admin-Token": adminToken() }, body });
  if (r.status === 403) { const e = new Error("the admin token was refused"); e.denied = true; throw e; }
  if (!r.ok) throw new Error((await r.json().catch(() => ({}))).error || r.statusText);
  const type = r.headers.get("content-type") || "";
  return type.includes("json") ? r.json() : type.startsWith("text/") ? r.text() : null;
}

/// "Plane crash dash" -> "plane-crash-dash" (a community/ file name).
function slug(name) {
  const s = (name || "").toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "");
  return s || "spot";
}

/// A button whose first click only arms it ("Click again to delete").
function confirmButton(label, armedLabel, action) {
  let armed = false, timer = 0;
  const b = el("button", { class: "chip" }, label);
  b.addEventListener("click", () => {
    if (!armed) {
      armed = true; b.textContent = armedLabel; b.classList.add("danger");
      timer = setTimeout(() => { armed = false; b.textContent = label; b.classList.remove("danger"); }, 4000);
      return;
    }
    clearTimeout(timer); armed = false; b.textContent = label; b.classList.remove("danger");
    action();
  });
  return b;
}

async function adminPage(tab) {
  document.title = "Admin · Forest Practice Runs";
  if (!adminToken()) return adminSignIn("");
  loading();
  let me;
  try { me = await adminCall("GET", "/check"); }
  catch (e) { return adminSignIn(e.denied ? "That token was refused." : "Could not reach the site: " + e.message); }

  let subs, flagged, runners, spots;
  try {
    [subs, flagged, runners, spots] = await Promise.all([adminCall("GET", "/submissions"), adminCall("GET", "/flagged"),
      adminCall("GET", "/runners"), api("/spots")]);
  } catch (e) { return failed(e); }

  const tabs = [
    ["submissions", "Submissions", subs.filter(s => s.status === "open").length],
    ["flagged", "Under review", flagged.filter(f => !f.hidden).length],
    ["spots", "Spots", spots.length],
    ["runners", "Runners", runners.length],
    ["categories", "Categories", 0],
    ["allowed", "Allowed mods", 0],
    ["activity", "Activity", 0],
  ];
  if (me.owner) tabs.push(["bot", "Bot", 0], ["admins", "Admins", 0]);
  tab = tabs.some(t => t[0] === tab) ? tab : "submissions";
  let body;
  try {
    body = tab === "flagged" ? flaggedView(flagged) : tab === "runners" ? runnersView(runners)
      : tab === "spots" ? spotsView(spots) : tab === "activity" ? activityView(await adminCall("GET", "/log"))
      : tab === "bot" ? botView(await adminCall("GET", "/bot"))
      : tab === "admins" ? adminsView(await adminCall("GET", "/admins"))
      : tab === "allowed" ? allowedView(await adminCall("GET", "/allowed"))
      : tab === "categories" ? categoriesView(await adminCall("GET", "/categories"), spots) : submissionsView(subs);
  } catch (e) { return failed(e); }

  show(
    el("div", { class: "adminbar" },
      el("h1", null, "Admin"),
      el("div", { class: "btnrow" }, el("span", { class: "sub" }, "Signed in as " + me.name),
        el("button", { class: "chip", onclick: () => { setAdminToken(""); adminSignIn("Signed out."); } }, "Sign out"))),
    el("div", { class: "routes" }, tabs.map(([id, label, n]) =>
      el("a", { class: "chip" + (id === tab ? " on" : ""), href: "/admin/" + id }, label + (n ? " · " + n : "")))),
    body);
}

function adminSignIn(message) {
  const input = el("input", { class: "search", type: "password", placeholder: "Admin token", "aria-label": "Admin token", autocomplete: "current-password" });
  const go = () => { if (input.value.trim()) { setAdminToken(input.value.trim()); adminPage(); } };
  input.addEventListener("keydown", e => { if (e.key === "Enter") go(); });
  show(
    el("h1", null, "Admin"),
    el("p", { class: "note" }, "The site's admin token (FOREST_ADMIN_TOKEN on the server). It stays in this browser."),
    el("div", { class: "signin" }, input, el("button", { class: "chip", onclick: go }, "Sign in")),
    message ? el("p", { class: "sub" }, message) : null);
  input.focus();
}

/// A row's action buttons with its own message line under them.
function actions(...buttons) {
  const msg = el("div", { class: "rowmsg" });
  return { box: el("div", { class: "actions" }, el("div", { class: "btnrow" }, buttons), msg), say: t => { msg.textContent = t; } };
}

async function act(say, method, path, done) {
  say("…");
  try { await adminCall(method, path); say(""); done(); }
  catch (e) { say("Failed: " + e.message); }
}

// --- spot submissions -------------------------------------------------------------

function submissionsView(subs) {
  if (!subs.length) return el("p", { class: "empty" },
    "No submissions yet. Runners send spots from the game: Practice → select a spot → Share → Submit to community.");
  const open = new Set();
  const list = el("div");
  function render() {
    list.replaceChildren(el("div", { class: "tablewrap" }, el("table", null,
      el("thead", null, el("tr", null, el("th", null, "Spot"), el("th", null, "Runner"), el("th", { class: "col-date" }, "Sent"), el("th", { class: "r" }, "Status"))),
      el("tbody", null, subs.flatMap(s => {
        const row = el("tr", { class: open.has(s.id) ? "focus" : "", onclick: () => { open.has(s.id) ? open.delete(s.id) : open.add(s.id); render(); } },
          el("td", { class: "name" }, s.name, s.community ? el("span", { class: "tag" }, "update of a community spot") : null,
            s.startState ? null : el("span", { class: "tag" }, "no start state")),
          el("td", null, s.runnerName || s.runner),
          el("td", { class: "date col-date" }, date(s.uploaded)),
          el("td", { class: "r status-" + s.status }, s.status));
        return open.has(s.id) ? [row, el("tr", { class: "detail" }, el("td", { colspan: 4 }, submissionDetail(s, render)))] : [row];
      })))));
  }
  render();
  return el("section", null,
    el("p", { class: "note" }, "Spots runners want everyone to get. Approving marks it here; publishing is still a commit to community/ (the steps show after Approve)."),
    list);
}

function submissionDetail(s, rerender) {
  const box = el("div", { class: "subdetail" }, el("div", { class: "sub" }, "Loading the file…"));
  let text = null;
  const file = slug(s.name) + ".foseg";
  const a = actions(
    el("button", { class: "chip", onclick: () => {
      if (text === null) return;
      const url = URL.createObjectURL(new Blob([text], { type: "text/plain" }));
      el("a", { href: url, download: file }).click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } }, "Download " + file),
    s.status !== "approved" ? el("button", { class: "chip", onclick: () => act(a.say, "POST", "/submissions/" + s.id + "/approved", () => { s.status = "approved"; rerender(); }) }, "Approve") : null,
    s.status !== "rejected" ? el("button", { class: "chip", onclick: () => act(a.say, "POST", "/submissions/" + s.id + "/rejected", () => { s.status = "rejected"; rerender(); }) }, "Reject") : null,
    s.status !== "open" ? el("button", { class: "chip", onclick: () => act(a.say, "POST", "/submissions/" + s.id + "/open", () => { s.status = "open"; rerender(); }) }, "Back to open") : null);

  adminCall("GET", "/submissions/" + s.id).then(t => {
    text = t;
    const cut = t.indexOf("\n[startstate]");
    const segment = (cut >= 0 ? t.slice(0, cut) : t).split("\n").filter(l => !l.startsWith("#")).join("\n").trim();
    const kb = cut >= 0 ? Math.round((t.length - cut) / 1024) : 0;
    box.replaceChildren(...[
      el("div", { class: "sub" }, "Segment " + s.segment + " · from " + (s.runnerName || s.runner) + " (" + s.runner + ") · " +
        (cut >= 0 ? "start state " + kb + " KB" : "no start state")),
      el("pre", { class: "foseg" }, segment),
      s.status === "approved" ? el("ol", { class: "steps" },
        el("li", null, "Download the file into community/ as ", el("code", null, file), "."),
        el("li", null, "Run ", el("code", null, "python scripts/community-index.py"), "."),
        el("li", null, "Commit and push: CI checks it, the site redeploys, every plugin gets it on its next start.")) : null,
      a.box].filter(Boolean));
  }).catch(e => box.replaceChildren(el("p", { class: "error" }, "Could not load the file: " + e.message), a.box));
  return box;
}

// --- runs under review --------------------------------------------------------------

function flaggedView(list) {
  if (!list.length) return el("p", { class: "empty" }, "Nothing under review. A run is flagged when it is under 0.8 × the route's best (3+ runs).");
  const rows = el("tbody");
  function render() {
    rows.replaceChildren(...list.map(f => {
      const a = actions(
        el("button", { class: "chip", title: "Clear the flag: it counts on the board again", onclick: () => act(a.say, "POST", "/runs/" + f.id + "/unflag", () => { list.splice(list.indexOf(f), 1); render(); }) }, "Looks fine"),
        el("button", { class: "chip", onclick: () => act(a.say, "POST", "/runs/" + f.id + (f.hidden ? "/show" : "/hide"), () => { f.hidden = !f.hidden; render(); }) }, f.hidden ? "Show" : "Hide"),
        confirmButton("Delete", "Click again to delete", () => act(a.say, "DELETE", "/runs/" + f.id, () => { list.splice(list.indexOf(f), 1); render(); })));
      return el("tr", null,
        el("td", { class: "name" }, el("a", { href: "/spot/" + encodeURIComponent(f.segment) + "/" + f.route }, f.spot || f.segment),
          f.hidden ? el("span", { class: "tag" }, "hidden") : null, a.box),
        el("td", null, f.name || f.runner),
        el("td", { class: "r" }, time(f.duration)),
        el("td", { class: "r date col-date" }, date(f.uploaded)));
    }));
  }
  render();
  return el("section", null, el("div", { class: "tablewrap" }, el("table", { class: "admin" },
    el("thead", null, el("tr", null, el("th", null, "Spot"), el("th", null, "Runner"), el("th", { class: "r" }, "Time"), el("th", { class: "r col-date" }, "Sent"))),
    rows)));
}

// --- spots ---------------------------------------------------------------------------

function spotsView(list) {
  if (!list.length) return el("p", { class: "empty" }, "No spots on the site yet.");
  const rows = el("tbody");
  function render() {
    rows.replaceChildren(...list.map(s => {
      const a = actions(s.community
        ? el("span", { class: "sub" }, "A community spot: remove its file from community/ to take it down.")
        : confirmButton("Delete spot", "Click again: the spot and all " + s.runs + " run(s) go",
            () => act(a.say, "DELETE", "/spots/" + encodeURIComponent(s.id), () => { list.splice(list.indexOf(s), 1); render(); })));
      return el("tr", null,
        el("td", { class: "name" }, el("a", { href: "/spot/" + encodeURIComponent(s.id) }, s.name),
          el("span", { class: "tag" }, s.community ? "community" : "by " + (s.by || "?")), a.box),
        el("td", { class: "r" }, s.runs),
        el("td", { class: "r date col-date" }, s.lastRun ? date(s.lastRun) : "-"));
    }));
  }
  render();
  return el("section", null,
    el("p", { class: "note" }, "Deleting a runner's spot removes it and every run on it. It comes back if its owner uploads a run on it again - ban the runner if it keeps happening."),
    el("div", { class: "tablewrap" }, el("table", { class: "admin" },
      el("thead", null, el("tr", null, el("th", null, "Spot"), el("th", { class: "r" }, "Runs"), el("th", { class: "r col-date" }, "Last run"))),
      rows)));
}

// --- allowed mods (run mode phase 3) ---------------------------------------------------

const ALLOW_KINDS = { mod: "Mod", patcher: "Patcher", code: "Code", patches: "Patches by" };

/// Every mod, patcher, outside code and patch owner a run report named:
/// allowing one turns its "NOT OK" into "allowed by the moderators" on
/// every attempt's page (the exact entry: a new version is a new entry).
function allowedView(list) {
  if (!list.length) return el("p", { class: "empty" }, "No run report has named another mod yet.");
  const rows = el("tbody");
  function render() {
    rows.replaceChildren(...list.map(x => {
      const q = "/allowed?kind=" + encodeURIComponent(x.kind) + "&text=" + encodeURIComponent(x.text);
      const a = actions(x.allowed
        ? confirmButton("Stop allowing", "Click again: attempts with it go red", () => act(a.say, "DELETE", q, () => { x.allowed = false; render(); }))
        : el("button", { class: "chip", onclick: () => act(a.say, "POST", q, () => { x.allowed = true; render(); }) }, "Allow"));
      return el("tr", null,
        el("td", { class: "name" }, el("span", { class: "tag" }, ALLOW_KINDS[x.kind] || x.kind), " ", x.text,
          x.allowed ? el("span", { class: "tag" }, "allowed" + (x.by ? " by " + x.by : "")) : null, a.box),
        el("td", { class: "r" }, x.attempts));
    }));
  }
  render();
  return el("section", null,
    el("p", { class: "note" }, "Everything run reports named besides ForestOverlay. Allow only what is known to be harmless: allowed entries show as " +
      "allowed on every attempt's page instead of making it red. The entry is exact, so a new version of a mod needs allowing again."),
    el("div", { class: "tablewrap" }, el("table", { class: "admin" },
      el("thead", null, el("tr", null, el("th", null, "Named in reports"), el("th", { class: "r" }, "Attempts"))),
      rows)));
}

// --- run categories (run mode phase 4) ---------------------------------------------------

const POLICY_LABEL = { locked: "Locked", allowed: "Runner's choice", forced: "Forced on" };

/// A category as src/Data/RunCategory's text (the server parses it back;
/// the version is the server's).
function categoryText(c) {
  const one = s => String(s ?? "").replace(/[\u0000-\u001f\u007f]/g, " ");
  const lines = ["[category]", "id = " + c.id, "name = " + one(c.name), "version = 0", "status = " + c.status,
    "difficulty = " + c.difficulty, "creative = " + c.creative, "multiplayer = " + c.multiplayer];
  if (c.spot) lines.push("spot = " + one(c.spot));
  lines.push("antisplice = " + (c.antisplice ? "on" : "off"), "amber = " + (c.amber ? "accepted" : "not accepted"));
  for (const f of c.features) lines.push("feature " + f.key + " = " + f.policy);
  if (c.logcap > 0) lines.push("logcap = " + c.logcap);
  for (const k of c.caps || []) lines.push("cap " + one(k.name).replace(/=/g, "").trim() + " = " + k.cap);
  for (const b of c.banned) if (b.trim()) lines.push("banned = " + one(b.trim()));
  for (const r of c.rules) lines.push("rule = " + one(r));
  if (c.src) lines.push("src = " + c.src);
  return lines.join("\n") + "\n";
}

/// Every category: speedrun.com's (drafts until published), the presets
/// and the moderators' own. Each opens into an editor; Save makes a new
/// version (attempts keep the version they ran under).
function categoriesView(data, spots) {
  const list = data.list.slice().sort((a, b) =>
    (a.status === "published" ? 0 : a.status === "draft" ? 1 : 2) - (b.status === "published" ? 0 : b.status === "draft" ? 1 : 2) ||
    a.name.localeCompare(b.name));
  const open = new Set();
  const rows = el("tbody");
  function render() {
    rows.replaceChildren(...list.flatMap(x => {
      const row = el("tr", { class: open.has(x.id) ? "focus" : "", onclick: () => { open.has(x.id) ? open.delete(x.id) : open.add(x.id); render(); } },
        el("td", { class: "name" }, x.name, x.changed ? el("span", { class: "tag bad" }, "speedrun.com changed this") : null),
        el("td", { class: "r status-" + (x.status === "published" ? "approved" : x.status === "draft" ? "open" : "rejected") }, x.status),
        el("td", { class: "r" }, "v" + x.version));
      return open.has(x.id) ? [row, el("tr", { class: "detail" }, el("td", { colspan: 3 }, categoryEditor(x, data.features, spots, render)))] : [row];
    }));
  }
  render();

  const sync = actions(el("button", { class: "chip", onclick: async () => {
    sync.say("Reading speedrun.com…");
    try {
      const r = await adminCall("POST", "/categories/sync");
      sync.say(r.done.length ? r.done.length + " change(s) - reloading" : "No changes on speedrun.com.");
      if (r.done.length) setTimeout(() => adminPage("categories"), 800);
    } catch (e) { sync.say("Failed: " + e.message); }
  } }, "Check speedrun.com now"), el("button", { class: "chip", onclick: () => newCategory() }, "New category"));

  function newCategory() {
    const name = prompt("The new category's name (e.g. Manhunt - Hard):");
    if (!name || !name.trim()) return;
    const id = slug(name.trim());
    if (list.some(x => x.id === id)) { sync.say("A category with that id exists: " + id); return; }
    const c = { id, name: name.trim(), status: "draft", difficulty: "any", creative: "any", multiplayer: "any", spot: "", antisplice: true,
      amber: true, banned: [], rules: [], src: "", logcap: 0, caps: [], features: data.features.map(f => ({ key: f.key, label: f.label, policy: f.def })) };
    adminCall("PUT", "/categories/" + id, categoryText(c)).then(() => adminPage("categories"), e => sync.say("Failed: " + e.message));
  }

  return el("section", null,
    el("p", { class: "note" }, "What a run allows. Categories come from speedrun.com (checked daily; new ones arrive as drafts) - " +
      "publish one and the game offers it in the Runs tab. Speedrun.com's changes apply by themselves until a category is edited here; " +
      "after that they wait for Accept. Every save is a new version: an attempt is judged by the version it ran under. " +
      "Last check: " + (data.lastSync || "not yet") + "."),
    sync.box,
    el("div", { class: "tablewrap" }, el("table", { class: "admin" },
      el("thead", null, el("tr", null, el("th", null, "Category"), el("th", { class: "r" }, "Status"), el("th", { class: "r" }, "Version"))),
      rows)));
}

function categoryEditor(x, features, spots, rerender) {
  const c = x.category;
  const stop = e => e.stopPropagation();
  const field = (label, input, hint) => el("label", { class: "field" }, el("span", null, label), input, hint ? el("span", { class: "sub" }, hint) : null);
  const select = (value, options) => {
    const s = el("select", null, options.map(([v, l]) => el("option", { value: v, selected: v === value ? "" : null }, l)));
    s.value = value;
    return s;
  };
  const name = el("input", { class: "search", value: c.name, maxlength: 80 });
  const status = select(c.status, [["draft", "Draft (not offered in game)"], ["published", "Published"], ["hidden", "Hidden"]]);
  const difficulty = select(c.difficulty, [["any", "Any"], ["peaceful", "Peaceful"], ["normal", "Normal"], ["hard", "Hard"]]);
  const creative = select(c.creative, [["any", "Either"], ["no", "Survival only"], ["yes", "Creative only"]]);
  const multiplayer = select(c.multiplayer, [["any", "Either"], ["no", "Single player"], ["yes", "Multiplayer"]]);
  const runSpots = spots.filter(s => s.community);
  const spot = select(c.spot || "", [["", "None"], ...runSpots.map(s => [s.id, s.name])]);
  if (c.spot && !runSpots.some(s => s.id === c.spot)) spot.append(el("option", { value: c.spot, selected: "" }, c.spot + " (not a community spot now)"));
  spot.value = c.spot || "";
  const anti = el("input", { type: "checkbox", checked: c.antisplice ? "" : null });
  const amber = el("input", { type: "checkbox", checked: c.amber ? "" : null });
  const banned = el("textarea", { rows: 4 }, c.banned.join("\n"));
  // The numbers a forced feature applies (a manhunt host sets them).
  const logcap = el("input", { class: "search", type: "number", min: 1, max: 99, value: c.logcap > 0 ? c.logcap : "", placeholder: "5" });
  const caps = itemCapsPicker(c.caps || []);
  const rules = el("textarea", { rows: 8 }, c.rules.join("\n"));
  const policies = {};
  const featureRows = features.map(f => {
    const now = (c.features.find(y => y.key === f.key) || {}).policy || f.def;
    const s = select(now, [["locked", POLICY_LABEL.locked], ["allowed", POLICY_LABEL.allowed], ...(f.toggle ? [["forced", POLICY_LABEL.forced]] : [])]);
    policies[f.key] = s;
    return el("tr", null, el("td", null, f.label, f.def !== "locked" ? el("span", { class: "sub" }, " (default: " + POLICY_LABEL[f.def].toLowerCase() + ")") : null), el("td", null, s));
  });

  const save = actions(el("button", { class: "chip", onclick: async () => {
    const out = {
      id: x.id, name: name.value.trim(), status: status.value, difficulty: difficulty.value, creative: creative.value, multiplayer: multiplayer.value,
      spot: spot.value, antisplice: anti.checked, amber: amber.checked, src: c.src,
      banned: banned.value.split("\n").map(s => s.trim()).filter(Boolean), rules: rules.value.split("\n").map(s => s.trimEnd()).filter(s => s.trim()),
      features: features.map(f => ({ key: f.key, policy: policies[f.key].value })),
    };
    if (!out.name) { save.say("A name, please."); return; }
    const lc = logcap.value.trim() ? parseInt(logcap.value, 10) : 0;
    if (logcap.value.trim() && !(lc >= 1 && lc <= 99)) { save.say("The log cap is 1-99 (empty: 5)."); return; }
    out.logcap = lc;
    const bad = caps.list.find(k => !(k.cap >= 1 && k.cap <= 9999));
    if (bad) { save.say("Item caps are 1-9999 - " + bad.name + " is not."); return; }
    out.caps = caps.list.map(k => ({ name: k.name, cap: k.cap }));
    save.say("…");
    try { const r = await adminCall("PUT", "/categories/" + x.id, categoryText(out)); save.say("Saved as version " + r.version + "."); setTimeout(() => adminPage("categories"), 700); }
    catch (e) { save.say("Failed: " + e.message); }
  } }, "Save (new version)"));

  let changed = null;
  if (x.changed) {
    const a = actions(
      el("button", { class: "chip", onclick: () => act(a.say, "POST", "/categories/" + x.id + "/accept", () => adminPage("categories")) },
        x.changed.name === "(removed from speedrun.com)" ? "Hide this category" : "Accept speedrun.com's version"),
      el("button", { class: "chip", onclick: () => act(a.say, "POST", "/categories/" + x.id + "/dismiss", () => adminPage("categories")) }, "Keep ours"));
    changed = el("div", { class: "srcdiff" },
      el("h3", null, "Speedrun.com changed this category"),
      x.changed.name === "(removed from speedrun.com)" ? el("p", null, "It is no longer on speedrun.com.")
        : el("div", { class: "cols" },
          el("div", null, el("div", { class: "sub" }, "Before"), el("strong", null, x.srcName), el("pre", null, x.srcRules)),
          el("div", null, el("div", { class: "sub" }, "Now"), el("strong", null, x.changed.name), el("pre", null, x.changed.rules))),
      el("p", { class: "sub" }, "Accept takes the name and rules; the settings here stay as they are."),
      a.box);
  }

  return el("div", { class: "catform", onclick: stop },
    changed,
    field("Name", name),
    field("Status", status, "Published categories are offered in the game's Runs tab."),
    el("h3", null, "The game"),
    field("Difficulty", difficulty),
    field("Creative", creative),
    field("Players", multiplayer),
    field("Run spot", spot, "The community spot whose Restart starts this category's runs (its start state is the preset save)."),
    el("h3", null, "Checks"),
    el("label", { class: "check" }, anti, " Anti-splice codes on screen, and the recording's timing checked"),
    el("label", { class: "check" }, amber, " Accept attempts checked by the video's codes only (offline, gaps)"),
    el("h3", null, "Overlay features in a run"),
    el("p", { class: "sub" }, "Locked: unusable during a run. Runner's choice: usable, named on the attempt's page. Forced on: on for everyone, unchangeable (e.g. a manhunt)."),
    el("div", { class: "tablewrap" }, el("table", { class: "admin" }, el("tbody", null, featureRows))),
    field("Log cap (when Logs in the inventory is forced on)", logcap, "How many logs everyone's inventory holds; empty = 5."),
    field("Item caps (when Item caps is forced on)", caps.box,
      "Type part of an item's name and pick it from the game's items (as the game's Inventory tab does); it starts at the game's own cap. Logs have the log cap above."),
    field("Banned moves (one per line)", banned, "Shown on every attempt's page. Detecting them automatically comes later."),
    field("Rules (one per line)", rules),
    c.src ? el("p", { class: "sub" }, "From speedrun.com (" + c.src + "). Last saved by " + x.by + ", " + date(x.at) + ".") : el("p", { class: "sub" }, "Last saved by " + x.by + ", " + date(x.at) + "."),
    save.box);
}

// The game's item list (wwwroot/items.json, read from the game's database):
// loaded once, when an editor first needs it.
let itemsPromise = null;
function gameItems() {
  itemsPromise ??= fetch("/items.json").then(r => r.ok ? r.json() : Promise.reject(new Error(r.statusText))).then(j => j.items);
  return itemsPromise;
}

/// Item caps as rows (name, cap, remove) plus a search that only adds the
/// game's own item names - no free text, so a name cannot be mistyped.
/// `list` is what Save reads.
function itemCapsPicker(initial) {
  const list = initial.map(k => ({ name: k.name, cap: k.cap }));
  let items = null;
  const rows = el("div", { class: "caprows" });
  const query = el("input", { class: "search", placeholder: "Find an item to cap (e.g. rock)", "aria-label": "Find an item to cap",
    autocomplete: "off", role: "combobox", "aria-expanded": "false" });
  const hints = el("div", { class: "caphints", role: "listbox" });
  const msg = el("div", { class: "sub" });
  let shown = [], active = 0;

  const known = name => !items || items.some(i => i.name === name);
  function renderRows() {
    rows.replaceChildren(...(list.length ? list.map((k, n) => {
      const cap = el("input", { class: "search capnum", type: "number", min: 1, max: 9999, value: k.cap, "aria-label": "Cap for " + k.name,
        oninput: () => { k.cap = parseInt(cap.value, 10); } });
      return el("div", { class: "caprow" },
        el("span", { class: "name" }, k.name, known(k.name) ? null : el("span", { class: "tag bad" }, "not a game item")),
        cap,
        el("button", { class: "chip", type: "button", "aria-label": "Remove " + k.name, onclick: () => { list.splice(n, 1); renderRows(); } }, "Remove"));
    }) : [el("div", { class: "sub" }, "No item caps - the game's own apply.")]));
  }
  function add(item) {
    if (item.id === 78) { msg.textContent = "Logs have their own cap (Log cap, above)."; return; }
    if (list.some(k => k.name === item.name)) { msg.textContent = item.name + " is in the list already."; return; }
    list.push({ name: item.name, cap: item.cap > 0 && item.cap < 9999 ? item.cap : 10 });
    msg.textContent = "Added " + item.name + " at " + (item.cap > 0 ? "the game's cap (" + item.cap + ")" : "10") + " - set the cap you want.";
    query.value = "";
    renderHints();
    renderRows();
    rows.lastChild?.querySelector("input")?.focus();
  }
  // Names starting with the query first, then containing it (the game's search).
  function renderHints() {
    const q = query.value.trim().toLowerCase();
    const free = items ? items.filter(i => i.id !== 78 && !list.some(k => k.name === i.name)) : [];   // not logs, not listed yet
    shown = !q ? [] : [...free.filter(i => i.name.toLowerCase().startsWith(q)),
      ...free.filter(i => !i.name.toLowerCase().startsWith(q) && i.name.toLowerCase().includes(q))].slice(0, 8);
    active = 0;
    hints.replaceChildren(...shown.map((i, n) => el("button", { type: "button", class: "caphint" + (n === active ? " on" : ""), role: "option",
      onmousedown: e => e.preventDefault(), onclick: () => add(i) }, i.name, i.cap > 0 ? el("span", { class: "sub" }, " game cap " + i.cap) : null)));
    if (q && items && !shown.length) hints.replaceChildren(el("div", { class: "sub" }, "No game item matches \"" + query.value.trim() + "\" (or it is listed already)."));
    query.setAttribute("aria-expanded", shown.length ? "true" : "false");
  }
  function move(step) {
    if (!shown.length) return;
    active = (active + step + shown.length) % shown.length;
    [...hints.children].forEach((b, n) => b.classList.toggle("on", n === active));
  }
  query.addEventListener("input", renderHints);
  query.addEventListener("keydown", e => {
    if (e.key === "ArrowDown") { e.preventDefault(); move(1); }
    else if (e.key === "ArrowUp") { e.preventDefault(); move(-1); }
    else if ((e.key === "Enter" || e.key === "Tab") && shown.length && query.value.trim()) { e.preventDefault(); add(shown[active]); }
    else if (e.key === "Escape") { query.value = ""; renderHints(); }
  });
  query.disabled = true;
  query.placeholder = "Loading the game's items…";
  gameItems().then(all => { items = all; query.disabled = false; query.placeholder = "Find an item to cap (e.g. rock)"; renderRows(); },
    e => { query.placeholder = "The item list did not load"; msg.textContent = "The game's item list did not load (" + e.message + ") - reload the page."; });
  renderRows();
  return { box: el("div", { class: "cappicker" }, rows, el("div", { class: "capsearch" }, query, hints), msg), list };
}

// --- the activity log ------------------------------------------------------------------

function activityView(log) {
  if (!log.length) return el("p", { class: "empty" }, "No admin changes yet.");
  return el("section", null,
    el("p", { class: "note" }, "Every change made on this page, newest first, by who made it."),
    el("div", { class: "tablewrap" }, el("table", { class: "admin" },
      el("thead", null, el("tr", null, el("th", null, "Admin"), el("th", null, "Change"), el("th", { class: "r" }, "Answer"), el("th", { class: "r" }, "When"))),
      el("tbody", null, log.map(l => el("tr", null,
        el("td", null, l.admin),
        el("td", { class: "name mono" }, l.action.replace(/^(\w+) \/api\/admin/, "$1 ")),
        el("td", { class: "r" + (l.status >= 400 ? " status-rejected" : "") }, l.status),
        el("td", { class: "r date" }, new Date(l.at).toLocaleString(undefined, { month: "short", day: "numeric", hour: "2-digit", minute: "2-digit" }))))))));
}

// --- admins (the owner only) ---------------------------------------------------------

function adminsView(list) {
  const name = el("input", { class: "search", placeholder: "New admin's name", "aria-label": "New admin's name", maxlength: 40 });
  const made = el("div");
  const rows = el("tbody");
  function render() {
    rows.replaceChildren(...list.map(x => {
      const a = actions(x.revoked ? null : confirmButton("Revoke", "Click again: their token stops working",
        () => act(a.say, "DELETE", "/admins/" + x.id, () => { x.revoked = true; render(); })));
      return el("tr", null,
        el("td", { class: "name" }, x.name, x.revoked ? el("span", { class: "tag bad" }, "revoked") : null, a.box),
        el("td", { class: "r date" }, date(x.created)));
    }));
    if (!list.length) rows.append(el("tr", null, el("td", { colspan: 2, class: "empty" }, "No other admins yet.")));
  }
  async function create() {
    const n = name.value.trim();
    if (!n) { made.replaceChildren(el("p", { class: "sub" }, "Type their name first.")); return; }
    made.replaceChildren(el("p", { class: "sub" }, "…"));
    try {
      const r = await fetch("/api/admin/admins", { method: "POST", headers: { "X-Admin-Token": adminToken() }, body: n });
      const j = await r.json();
      if (!r.ok) throw new Error(j.error || r.statusText);
      list.push({ id: j.id, name: j.name, created: new Date().toISOString(), revoked: false });
      name.value = "";
      render();
      const tok = el("code", { class: "token" }, j.token);
      const copy = el("button", { class: "chip", onclick: () => navigator.clipboard.writeText(j.token).then(() => { copy.textContent = "Copied"; }, () => { copy.textContent = "Select it and copy"; }) }, "Copy");
      made.replaceChildren(el("div", { class: "made" },
        el("p", null, "Token for " + j.name + " - shown this once. Send it to them privately; they sign in at /admin with it."),
        el("div", { class: "btnrow" }, tok, copy)));
    } catch (e) { made.replaceChildren(el("p", { class: "error" }, "Not made: " + e.message)); }
  }
  name.addEventListener("keydown", e => { if (e.key === "Enter") create(); });
  render();
  return el("section", null,
    el("p", { class: "note" }, "Each admin gets their own token: they can do everything on this page except manage admins, and their changes show under their name in Activity. Revoke a token to take access away."),
    el("div", { class: "signin" }, name, el("button", { class: "chip", onclick: create }, "Make a token")),
    made,
    el("div", { class: "tablewrap" }, el("table", { class: "admin" },
      el("thead", null, el("tr", null, el("th", null, "Admin"), el("th", { class: "r" }, "Added"))),
      rows)));
}

// --- the knowledge bot (the owner only) -------------------------------------------------

/// Channels the bot answers in (by name, from what it reports seeing), DMs, limits,
/// models, thinking, the research-queue channel. A field left empty = the bot's .env default.
function botView(data) {
  const s = data.settings || {}, report = data.report;
  const seen = (report && report.channels) || [];
  const chosen = new Set((s.channels || []).map(String));
  const known = new Set(seen.map(c => c.id));
  const label = c => (c.guild ? c.guild + " / " : "") + "#" + c.name;

  const boxes = seen.map(c => {
    const box = el("input", { type: "checkbox", value: c.id });
    box.checked = chosen.has(c.id);
    return el("label", { class: "check" }, box, " " + label(c));
  });
  // Saved ids the bot no longer lists stay visible, so saving does not drop them silently.
  for (const id of chosen) if (!known.has(id)) {
    const box = el("input", { type: "checkbox", value: id });
    box.checked = true;
    boxes.push(el("label", { class: "check" }, box, " (unknown channel " + id + ")"));
  }

  const dms = el("input", { type: "checkbox" });
  dms.checked = s.dms !== false;
  const perHour = el("input", { class: "search", type: "number", min: 1, max: 1000, placeholder: "default 15", "aria-label": "Questions per user per hour" });
  perHour.value = s.perHour || "";
  const perDay = el("input", { class: "search", type: "number", min: 1, max: 10000, placeholder: "default 60", "aria-label": "Questions per user per day" });
  perDay.value = s.perDay || "";
  const models = el("input", { class: "search", maxlength: 300, placeholder: "provider:model,provider:model (empty = .env)", "aria-label": "Model order" });
  models.value = s.models || "";
  const thinking = el("select", { "aria-label": "Thinking level" },
    ["", "low", "high"].map(v => el("option", { value: v }, v || "model default")));
  thinking.value = s.thinking || "";
  const queue = el("select", { "aria-label": "Research queue channel" },
    el("option", { value: "" }, "none (or the .env one)"),
    seen.map(c => el("option", { value: c.id }, label(c))));
  if (s.queueChannel && !known.has(s.queueChannel)) queue.append(el("option", { value: s.queueChannel }, "(unknown channel " + s.queueChannel + ")"));
  queue.value = s.queueChannel || "";

  const save = actions(el("button", { class: "chip", onclick: async () => {
    // With no boxes (the bot has not listed its channels) the channels stay as stored, or absent:
    // sending [] would silence the bot.
    const body = {
      channels: boxes.length ? boxes.map(l => l.firstChild).filter(b => b.checked).map(b => b.value) : s.channels, dms: dms.checked,
      perHour: perHour.value ? Number(perHour.value) : null, perDay: perDay.value ? Number(perDay.value) : null,
      models: models.value.trim(), thinking: thinking.value, queueChannel: queue.value,
    };
    save.say("…");
    try { const r = await adminCall("PUT", "/bot", JSON.stringify(body)); save.say("Saved as revision " + r.rev + ". The bot applies it within a minute."); setTimeout(() => adminPage("bot"), 900); }
    catch (e) { save.say("Not saved: " + e.message); }
  } }, "Save"));

  const field = (label, input, hint) => el("label", { class: "field" }, el("span", null, label), input, hint ? el("span", { class: "sub" }, hint) : null);

  let status;
  if (!data.botToken) status = "The site has no FOREST_BOT_TOKEN, so the bot cannot read these settings (set it in /opt/forest-site/.env and the bot's .env, then restart both).";
  else if (!report) status = "The bot has not reported yet.";
  else {
    const hhmm = t => t ? new Date(t).toLocaleTimeString(undefined, { hour: "2-digit", minute: "2-digit" }) : null;
    const runs = "bot " + (report.version || "?");
    if (data.rev === 0 && report.rev === 0) status = "No settings saved yet - the bot runs its .env values (" + runs + ", last reported " + date(report.at) + ").";
    else if (report.rev === data.rev) status = "Applied " + (hhmm(report.appliedAt) || "(time unknown)") + ", " + runs + " - saved rev " + data.rev + " is running.";
    else status = "Saved rev " + data.rev + ", bot runs rev " + report.rev + " (" + (report.appliedAt ? "applied " + hhmm(report.appliedAt) + ", " : "") + runs + ", last reported " + date(report.at) + ") - not applied yet.";
  }

  return el("section", null,
    el("p", { class: "note" }, "The knowledge bot's settings. It reads them about once a minute, no restart. Secrets (the Discord token, model keys) stay in the bot's .env and never pass through here. " +
      "A field left empty uses the bot's .env value."),
    el("p", { class: report && report.rev === data.rev ? "sub" : "error" }, status),
    el("div", { class: "catform" },
    el("h3", null, "Channels it answers in"),
    boxes.length ? el("div", null, boxes) : el("p", { class: "sub" }, "The bot has not listed its channels yet."),
    el("p", { class: "sub" }, "Once saved here, the ticked channels are the whole list: none ticked = the bot answers in no channel (direct messages follow the setting below). Before the first save the bot uses its .env channels."),
    el("label", { class: "check" }, dms, " Answer direct messages"),
    field("Questions per user per hour", perHour),
    field("Questions per user per day", perDay),
    field("Model order", models, "First is preferred; the next answers when it fails."),
    field("Thinking level", thinking),
    field("Research-queue channel", queue),
    save.box));
}

// --- runners -------------------------------------------------------------------------

function runnersView(list) {
  if (!list.length) return el("p", { class: "empty" }, "No runners registered yet.");
  const rows = el("tbody");
  function render() {
    rows.replaceChildren(...list.map(r => {
      const a = actions(
        el("button", { class: "chip", onclick: () => act(a.say, "POST", "/runners/" + r.id + (r.banned ? "/unban" : "/ban"), () => { r.banned = !r.banned; render(); }) }, r.banned ? "Unban" : "Ban"),
        r.hasToken ? confirmButton("Reset token", "Click again: they re-register on their next upload",
          () => act(a.say, "POST", "/runners/" + r.id + "/reset-token", () => { r.hasToken = false; render(); })) : null);
      return el("tr", null,
        el("td", { class: "name" }, r.name || "(no name)", el("span", { class: "tag mono" }, r.id),
          r.banned ? el("span", { class: "tag bad" }, "banned") : null, r.hasToken ? null : el("span", { class: "tag" }, "token reset"), a.box),
        el("td", { class: "r" }, r.runs),
        el("td", { class: "r date col-date" }, r.lastUpload ? date(r.lastUpload) : "-"),
        el("td", { class: "r date col-date" }, date(r.created)));
    }));
  }
  render();
  return el("section", null, el("div", { class: "tablewrap" }, el("table", { class: "admin" },
    el("thead", null, el("tr", null, el("th", null, "Runner"), el("th", { class: "r" }, "Runs"), el("th", { class: "r col-date" }, "Last run"), el("th", { class: "r col-date" }, "Joined"))),
    rows)));
}
