// forest.deter.cloud: a run mode attempt's page, /attempt/<id> (run mode
// phase 3, docs/run-mode.md). The link the game copies (Runs tab, Copy
// link). The verdict in plain words, the two halves it comes from - what
// the site saw during the run (the codes' chain and its checkpoints) and
// what ran in the game (the report) - and a "check a code" box for a
// verifier with the video open. Public: reports are never editable.
// Uses el / api / time / show / loading / failed from app.js.
"use strict";

const VERDICTS = {
  green: ["Checked", "The recording matches what the site saw while the run happened, and nothing else ran in the game."],
  amber: ["Partly checked", "Nothing contradicts it, but some of it can only be checked against the codes in the video."],
  red: ["Problems found", "Something below contradicts the run, or something ran that a run does not allow."],
  running: ["In progress", "The attempt is still running, or its log has not reached the site yet."],
};
const MARKS = { ok: "✓", bad: "✗", pending: "…", warn: "!", allowed: "✓", note: "i", green: "✓", amber: "…", red: "✗" };

/// ms -> "1:23" (the attempt's real time, as the log's lines say it).
function clock(ms) {
  const s = Math.floor(ms / 1000);
  return s >= 3600 ? Math.floor(s / 3600) + ":" + String(Math.floor(s / 60) % 60).padStart(2, "0") + ":" + String(s % 60).padStart(2, "0")
                   : Math.floor(s / 60) + ":" + String(s % 60).padStart(2, "0");
}

/// [level, text, details?]: details (internal names) behind a fold.
function lineList(items) {
  return el("ul", { class: "findings" }, items.map(([level, text, details]) =>
    el("li", { class: "f-" + level }, el("span", { class: "mark", "aria-hidden": "true" }, MARKS[level] || "·"),
      el("div", null, text, details && details.length ? el("details", { class: "raw" }, el("summary", null, "Which parts (the game's own names)"),
        el("ul", { class: "partlist" }, details.map(d => el("li", null, d)))) : null))));
}

async function attemptPage(id) {
  document.title = "Run attempt · Forest Practice Runs";
  loading();
  let a;
  try { a = await api("/attempts/" + encodeURIComponent(id)); } catch (e) { return failed(e); }
  const [head, says] = VERDICTS[a.verdict] || VERDICTS.running;
  const catName = a.rules ? a.rules.name : a.category;
  document.title = (catName || "Run") + " attempt" + (a.runnerName ? " by " + a.runnerName : "") + " · Forest Practice Runs";

  const when = a.issued || a.received;
  const cat = a.rules;   // the category version the report names (phase 4)
  const facts = [
    ["Category", cat ? cat.name + " (version " + cat.version + ")" : a.category || "-"],
    ["Runner", a.runnerName || a.runner],
    ["Spot", a.spot ? el("a", { href: "/spot/" + encodeURIComponent(a.spot) }, a.spot) : "none (started by hand)"],
    ["Game", a.mode || "-"],
    ["Started", a.startedAt ? a.startedAt + " (the runner's clock)" : a.started || (when ? new Date(when).toLocaleString() : "-")],
    ["Timer", a.finalTimerMs > 0 ? time(a.finalTimerMs / 1000) : "-"],
    ["Length", a.durationMs > 0 ? clock(a.durationMs) + " real time" : "-"],
    ["Ended", a.ended ? a.endReason || "-" : "not yet"],
    ["Online", a.online ? "yes - " + a.checkpoints + " checkpoint(s) reached the site during the run" : "no - started offline"],
    ["Plugin", a.plugin || "-"],
  ];

  // Check a code: the box a verifier types a code from the video into.
  const input = el("input", { class: "search code", maxlength: 4, placeholder: "Code", "aria-label": "Code from the video",
    autocomplete: "off", spellcheck: "false" });
  const result = el("div", { class: "coderesult", "aria-live": "polite" });
  async function check() {
    const code = input.value.trim();
    if (code.length !== 4) { result.replaceChildren(el("p", { class: "sub" }, "A code is four characters.")); return; }
    result.replaceChildren(el("p", { class: "sub" }, "Checking…"));
    let f;
    try { f = await api("/attempts/" + encodeURIComponent(id) + "/code/" + encodeURIComponent(code)); }
    catch (e) { result.replaceChildren(el("p", { class: "error" }, "Could not check: " + e.message)); return; }
    if (!a.ended) { result.replaceChildren(el("p", { class: "sub" }, "The attempt's log has not reached the site yet, so there is nothing to check against.")); return; }
    if (!f.matches.length) {
      result.replaceChildren(el("p", { class: "f-bad" }, "“" + f.code + "” is not in this attempt's log. Check you read it right " +
        "(O and 0 count as the same, and so do I, L and 1). A video showing codes that are not here is not this attempt."));
      return;
    }
    result.replaceChildren(el("p", { class: "f-ok" }, "“" + f.code + "” shows " + f.matches.map(m =>
      "at " + clock(m.realMs) + " into the attempt" + (m.timerMs >= 0 ? " (timer " + time(m.timerMs / 1000) + ")" : "")).join(", ") + "."));
  }
  input.addEventListener("keydown", e => { if (e.key === "Enter") check(); });

  // The category's rules as they were for this attempt: what the overlay
  // allowed, the banned moves (checked by eye for now) and the rules.
  // The numbers a forced feature ran with (a manhunt's log / item caps).
  function forcedNumbers(c) {
    const parts = [];
    if (c.features.some(f => f.key === "logs" && f.policy === "forced")) parts.push("logs held " + (c.logcap > 0 ? c.logcap : 5));
    if (c.features.some(f => f.key === "itemcaps" && f.policy === "forced"))
      parts.push((c.caps || []).length ? "item caps " + c.caps.map(k => k.name + " " + k.cap).join(", ") : "no item caps set");
    return parts.length ? el("p", null, "Set for everyone: " + parts.join("; ") + ".") : null;
  }
  let rules = null;
  if (cat) {
    const special = cat.features.filter(f => f.policy !== "locked");
    rules = el("section", null,
      el("h2", null, "The category's rules"),
      el("p", { class: "note" }, cat.name + ", version " + cat.version + " - as the moderators had it when this attempt ran." +
        (cat.antisplice ? "" : " This category does not use the anti-splice codes.")),
      special.length ? el("p", null, "Overlay features: " + special.map(f => f.label + " (" + (f.policy === "forced" ? "on for everyone" : "runner's choice") + ")").join(", ") +
        ". Everything else is locked.") : el("p", null, "Every overlay feature that changes the game is locked."),
      forcedNumbers(cat),
      cat.banned.length ? el("div", null, el("h3", null, "Banned moves (check the video)"), el("ul", null, cat.banned.map(b => el("li", null, b)))) : null,
      cat.rules.length ? el("details", { class: "raw" }, el("summary", null, "Rules"), el("ul", null, cat.rules.map(r => el("li", null, r)))) : null);
  }

  const recording = a.recording && a.recording.judged === false && a.recording.verdict !== "red"
    ? el("p", { class: "sub" }, "Not judged: " + (cat ? cat.name : "this category") + " does not use the anti-splice codes. A log that contradicts the site would still show here.")
    : a.recording ? lineList(a.recording.why.map(t => [a.recording.verdict, t]))
    : el("p", { class: "sub" }, a.online ? a.checkpoints + " checkpoint(s) so far. The verdict comes when the log arrives." : "Nothing yet.");
  const findings = a.findings ? lineList(a.findings.map(f => [f.level, f.text, f.details]))
    : el("p", { class: "sub" }, "The game's report comes with the log, at the attempt's end.");

  show(
    el("h1", null, "Run attempt"),
    el("p", { class: "sub" }, [catName, a.runnerName || a.runner, when ? date(when) : null].filter(Boolean).join(" · ")),
    el("section", { class: "verdict v-" + (VERDICTS[a.verdict] ? a.verdict : "running") },
      el("div", { class: "vhead" }, el("span", { class: "mark", "aria-hidden": "true" }, MARKS[a.verdict] || "…"), head),
      el("p", null, says)),
    el("div", { class: "tablewrap" }, el("table", { class: "facts" }, el("tbody", null,
      facts.map(([k, v]) => el("tr", null, el("th", null, k), el("td", null, v)))))),
    rules,
    cat && !cat.antisplice ? null : el("section", null,
      el("h2", null, "Check a code"),
      el("p", { class: "note" }, "In run mode the game shows a four-letter code at the top of the screen that changes every second. " +
        "Pause the video at a few moments, type the code you see, and compare where it shows: the moments should be as far apart here " +
        "as in the video. A spliced-in piece shows codes from another attempt, which are not in this log."),
      el("div", { class: "signin" }, input, el("button", { class: "chip", onclick: check }, "Check")),
      result),
    el("section", null,
      el("h2", null, "What the site saw during the run"),
      recording),
    el("section", null,
      el("h2", null, "What ran in the game"),
      findings,
      a.report ? el("details", { class: "raw" }, el("summary", null, "The report as the game wrote it"), el("pre", null, a.report)) : null),
    el("p", { class: "btnrow" },
      a.ended ? el("a", { class: "btn", href: "/api/attempts/" + encodeURIComponent(id) + "/log", download: id + ".log" }, "Download the log") : null,
      el("span", { class: "sub" }, "Attempt " + id)));
}
