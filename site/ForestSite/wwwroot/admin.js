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

async function adminCall(method, path) {
  const r = await fetch("/api/admin" + path, { method, headers: { "X-Admin-Token": adminToken() } });
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
    ["activity", "Activity", 0],
  ];
  if (me.owner) tabs.push(["admins", "Admins", 0]);
  tab = tabs.some(t => t[0] === tab) ? tab : "submissions";
  let body;
  try {
    body = tab === "flagged" ? flaggedView(flagged) : tab === "runners" ? runnersView(runners)
      : tab === "spots" ? spotsView(spots) : tab === "activity" ? activityView(await adminCall("GET", "/log"))
      : tab === "admins" ? adminsView(await adminCall("GET", "/admins")) : submissionsView(subs);
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
