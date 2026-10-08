# Area: how a session works

The working rules decided with the author: starting a session, the task
file, the handoff, subagents, when to switch session, where docs go, and
editing. Read it once per session you plan to end with a handoff, and
before spawning agents.

## Session start

The steps: skill `session-start` (`.claude/skills/session-start/`).
The SessionStart hook (`.claude/settings.json`, startup + /clear) prints
`scripts/session-start.py`'s report into the session: git, worktrees,
baseline (CI badges, or local tests when HEAD is not origin/main),
release, site, VPS, tasks, quality, and when the recurring reviews are
due: the weekly cleanup (skill `weekly-cleanup`: `scripts/audit.py`'s
dead paths / code / files become P4 tasks, stale quality rows are
re-graded), the monthly harness review (skill `harness-review`: one
component off for 5 tasks, the author decides) and the bot review.

## Gates (machine checks, docs/harness.md 5a, 5b, 7b)

Every failure says WHAT / WHY / FIX; follow the FIX line.
- **`scripts/lint.py`** - the csproj version = `Plugin.PluginVersion` = a
  `CHANGELOG.md` section; every top-level folder with C# in the csproj's
  three `Remove` lines (gotcha 92); a fixed 20 px `GUI.Label` with
  variable text and allocations in `OnGUI` / `DrawTab` bodies; one check
  per gotcha that can be checked (mojibake, deploy copies the DLL only,
  `FindObjectsOfTypeAll` sites, `OnRenderObject` draw target, web request
  `responseCode`, `MoveTo` callers, config writes per keystroke, the bot's
  globalization, lifecycle wrappers) - each gotcha's index line names its
  check (heuristics:
  the hits that existed on 2026-10-07 / -08 sit in `scripts/lint-baseline.txt`,
  only new ones fail; `--update-baseline` accepts a false positive or drops
  fixed ones). Runs in CI, before every commit (`.githooks/pre-commit`) and
  before a `v*` tag is pushed (`.githooks/pre-push`: the tag's commit
  carries that version). Every gotcha has one index line in an area doc,
  ending with `[check: <name>]`, `[check: T-n]` (an open task) or
  `[judgement]` (5d, T-0009). `session-start.py` turns the git hooks on
  (`core.hooksPath .githooks`). The community index: `CommunityPacksTests`.
  The log catalogue (9a, T-0012: `scripts/log-catalogue.py --check`):
  every log call in `src/` / `patcher/` starts with a literal prefix (or
  names it in a `// log: Name` comment when the message is built
  elsewhere) and `docs/log-lines.md` is current with a meaning per prefix
  - after changing a log line, `python scripts/log-catalogue.py`.
  The quality document (10d, T-0013: `docs/quality.md`): grades A-D, an
  area's grade the worst of its four, a C / D row names an open task,
  and every tracked file sits in some area's *Paths* - a new folder
  fails until it is graded.
- **PreToolUse** (`scripts/hooks/pre_tool.py`, Bash / PowerShell /
  WebFetch): refuses `api.github.com` fetches and a forced push to main;
  **asks** before a deploy into the author's install (`deploy.ps1` with
  no / the `FOREST_ROOT` game root, or a copy into its `BepInEx/plugins`
  - author 2026-10-07: "just ask me"; a test install passes; bridge tests
  use `update_game` and never hit it); refuses a search over a whole
  drive, `/` or the home folder deeper than 2 (`find /`, `Get-ChildItem C:\
  -Recurse`, ...) and names the real paths (gotcha 99); warns on
  `Get-Content | Set-Content`.
- **Stop** (`scripts/hooks/stop.py`): at the end of a turn, lists
  uncommitted / unpushed work, a csproj version with no (pushed) tag,
  in-progress tasks with no `tasks/notes/` file, and an unpushed changelog
  line with a number and no "measured" (gotcha 44). It blocks the stop
  once; finish them, or end the turn saying why each one stays.

## The task file

**Open work lives in the task file** ([`docs/harness.md`](../harness.md) 6, built
2026-10-07): `python scripts/tasks.py list --open`, `next`, `show T-n`;
the generated view is [`docs/tasks.md`](../tasks.md), parked questions
for the author at its top. Detail stays in the docs each task's `notes`
names (backlog.md, investigations.md, run-mode.md ...). Start a task with
`tasks.py start T-n --by <name>` (it asks for the contract), record proof
with `tasks.py evidence`; behaviour changes are confirmed by a checker,
never their maker (author, 2026-10-07; *The checker* below). New requests become tasks
(`tasks.py add`), with their detail in backlog.md when it is long; the
author's feature list (*Next up* until 2026-10-07) is P2-P4 there.
Multi-session notes: `tasks.py note T-n "..."` (`tasks/notes/`).
A release moves its tasks by itself (`bump.py` -> `released`, T-0007; a plugin task whose commits touch no plugin path stays `built` and confirms on test / lint evidence, T-0196);
a check only a tester can do is a `needs: tester` task with a `qa` line,
which is all the #qa-todo-list message shows (docs/bridge.md).

**Closing** (T-0114, 2026-10-07): harness and docs tasks have no release
and no checker - they go `built` -> `confirmed` once each verify step has
evidence (the machine gates are their check). Work shipped and confirmed
in game before the checker existed (2026-10-07) is closed with its
`docs/confirmed.md` entry as evidence and `--no-checker`, noted as such.
Before starting a migrated task, check git, the code and `confirmed.md`
for it first - the migration copied lists that were partly stale.

## The checker (docs/harness.md 7d, 9d; T-0006)

Every behaviour change (`checker: true`: plugin, site, bot tasks) is
reviewed by **`forest-checker`** (Sonnet, read + Bash, never edits) once
it is built and **before it is pushed or released** (author,
2026-10-07). Spawn it with only `Check T-n`; it reads `tasks.py brief
T-n` (contract, evidence, earlier reviews, the diff without the task
files, the suites to re-run), re-runs those suites, scores the rubric
(correctness, verification, scope, restart, legible, handoff: 0-2 or
n/a) and records `tasks.py review`:
- **accept** - the release / push may go. A plugin task still needs
  in-game evidence to be `confirmed` (`--by forest-tester`, `e2e`,
  `author` or `qa:<tester>`); a site / bot task is confirmed by the
  accept plus deploy-watch's live check.
- **revise** - the task goes back to its maker (`in-progress`; `todo` if
  the maker is busy) with the faults; fix them, commit, and check again.
- **block** - the fix needs the author (intent, design, wording, scope):
  the task is parked with the checker's question.

Gates: `released` and `confirmed` refuse a checker task whose last review
is not an accept covering all its commits; the Stop hook names a checker
task with unpushed commits and no accept. The agent list loads at session
start - in the session that adds or renames it, use a general-purpose
Sonnet agent told to follow `.claude/agents/forest-checker.md`.

## The loop (docs/harness.md 12 Stage A; T-0015)

The assisted loop: the author says "run the loop" and one main session
works up to **5 rounds** back to back (skill `work-loop`). Each round is
one task from `tasks.py next` (needs none; + bridge with `--bridge`),
and `scripts/loop.py next` names its one next action from the task's
state - contract, the work, the checker, release / ship / evidence,
end. Author's calls (2026-10-07, harness.md *Decisions* 7):
- **Main orchestrates, subagents work**: each round's work goes to a
  fresh agent for its area (tiny docs / harness edits main does itself),
  so the main context grows by one summary per round, not by the work.
  A loop run is the exception to *one session, one task* below.
- **Stops by machine check**: 5 rounds done, nothing left in the pool,
  or 3 rounds in a row without progress. A task's 3rd checker revise in
  its round parks it (`needs: author-decision`, the faults as its
  question) and the loop moves on.
- **The pool is `needs: none`** (+ bridge when the game is up):
  author-present / author-decision tasks are skipped; `loop.py begin`
  lists the parked questions for the author first.
- Every round ends with `loop.py end --summary` (one paragraph; an
  in-progress task is refused - pass or park first); a human fix is
  logged with `loop.py intervene`. `loop.py report` is the run, `tasks.py
  stats` its *loop* line. State: `tasks/loop.jsonl` (append-only).

**Awaiting an in-game check:** tasks with `needs: bridge` / `author-eyes`
(`tasks.py list --open --needs bridge`). Confirmed features, by version
and by whom: [`docs/confirmed.md`](../confirmed.md) (check it before
re-testing something; add each new confirmation there).

## How a session goes

The author tests in game and reports back with the `LogOutput.log` path; they
answer design questions quickly and mid-turn, and often send several
messages while a turn runs. Releases: [`release.md`](release.md) *Releasing*.
Do not deploy into the game folder. Record decisions made with the author
in [`docs/decisions.md`](../decisions.md), with who decided and when. The log is
replaced on every game launch — read it before the author starts the game
again. **Keep the handoff current without being asked** (author,
2026-09-25: "so i don't have to keep asking before i switch session"):
after every release or finished piece of work, in the same push,
update the router's *Where we are* and the task file, re-grade the
[`docs/quality.md`](../quality.md) row of each area the work changed
(evidence, grades, *Reviewed*; session start lists stale rows), move confirmed items, add any lesson as a
gotcha (`docs/gotchas.md` + its area doc's index line) - with its check
when a lint, test or log assertion can catch it (or a task for one), else `[judgement]`. The author may switch session at any moment;
the docs on `main` must always be ready for it. With sessions running side
by side, `qa_read new_only` is shared - a message one session reads
is gone from the other's new list (tell the author what belongs where).

**Subagents (author, 2026-10-04/05: maximise usage).** Project agents in
`.claude/agents/`, each with its model, effort, a turn cap, a trimmed tool
list and a short brief naming the docs to read and the report to return:
`forest-dev` (Sonnet: plugin features / fixes whose game side is known),
`forest-researcher` (Opus, high: game internals, live research, hard
restore / physics / render bugs), `forest-tester` (Sonnet: in-game checks
over the bridge, writes docs/confirmed.md), `forest-site` (Sonnet: site/),
`forest-knowledge` (Sonnet: bot cards + the 👎 queue), `forest-qa` (Haiku:
the QA Discord), `forest-checker` (Sonnet, high - author, 2026-10-07: the
only independent review; reviews a built task, *The checker* above).
Run 2-3 at a time (5+ Opus agents emptied a 5-hour window in under 15
minutes), one driving the game at a time; give the task in a few lines -
the agent file holds the rules. The code agents (dev, researcher, site,
knowledge) set `isolation: worktree` themselves (worktrees in
`.claude/worktrees/`, outside the compile globs; a fresh branch from main,
or the existing worktree the task names).

**How a brief is kept short (T-0113, 2026-10-07).** A subagent already
loads the root `CLAUDE.md` and the memory index (seen in its transcript),
and a folder `CLAUDE.md` when it opens a file there with Read (not
`cat`); so a brief never repeats the hard rules, the build commands or a
folder's rules - it names what to read, the boundaries of its job (what
the main session does instead) and the report. A procedure is preloaded
(`skills:` - the tester gets `bridge-test`), not restated. Turn caps
(`maxTurns`, author, 2026-10-07: about 2-3x the longest measured run -
checker 30, qa 30, tester 100, dev / site / knowledge 150, researcher 250)
stop a stuck agent with partial work, which can be resumed. Measure a
change with `python scripts/agent-cost.py [--since D] [--runs]` (per
agent: calls, the start context, cache reads, output, active minutes);
on 2026-10-07 a check cost ~5 calls from a 14k start, a dev run ~22 calls
/ 1.5M cache reads, a tester run ~47 / 3.3M, the old general-purpose Opus
runs ~60 / 10M. The main session merges (`scripts/merge-keepboth.py` for
add/add conflicts), releases (`scripts/bump.py`), keeps the handoff, and
says in one line what each agent is doing when it starts it. Do the small
things yourself: an agent costs a cold start (CLAUDE.md + reading),
worth it only for work bigger than that.

**When to switch session (author, 2026-09-26: "add those as rules").**
Switch at a task boundary, not by habit or by a context number alone:
- **Small, self-contained items**: one per session, then hand off (the
  author's usual plan).
  **One session, one task** (author, 2026-10-07): once the task is done
  and handed off, stop - name the next task for a new session, never
  offer to carry on with it in this one. The exception is a loop run the
  author started (*The loop* above): its rounds go on until `loop.py`
  says STOP, then the handoff and stop.
- **An investigation stays in one session** (a performance item, a
  heap / physics lead, a multi-release bug): what has been read - IL,
  log lines, a dropped theory - is worth more than a fresh start reading
  a summary. Keep releasing and updating the handoff as usual on the way.
- **Suggest a switch** when the session has been auto-compacted once,
  has wandered across unrelated areas, or starts getting wrong what it
  knew earlier - say so in chat; the author decides.
- The handoff discipline (docs current after every release) holds either
  way - it is what makes a switch cheap.

## Where docs go

**Keep `CLAUDE.md` a router** (author, 2026-10-07: ~100-200 lines). It is
loaded into every session and carried in every turn, so it holds only what
every session needs: the hard rules, the commands, the layout and a map of
the docs with when to read each. Everything else is one hop away: an area
doc in `docs/areas/`, [`docs/decisions.md`](../decisions.md), a folder `CLAUDE.md`
(`src/`, `tests/`, `site/`, `bot/` - loaded only when work touches that
folder) or a reference doc. Before adding a block to the router, ask whether
a session needs it on every turn or only when working on that area - the
latter goes to the area.

**Documentation standard (author, 2026-09-24: "so it doesn't clog up
documentation any further").**
- **One home per fact.** Game internals -> `docs/game-notes.md`;
  runner-facing changes -> `CHANGELOG.md`; the why of a change -> its
  commit message; decisions -> `docs/decisions.md`; open work -> the task
  file; stable rules -> the router, an area doc or a folder `CLAUDE.md`.
  Link, never copy.
- **Finished work shrinks to one line** (what, version, where the
  detail lives); no "old notes" kept beside a fix.
- **Confirmed -> moved, not marked:** delete the item from every to-test
  list and add a few words to `docs/confirmed.md`; never leave a
  ~~struck~~ or "confirmed" entry in a to-do list.
- **The router's *Where we are* is replaced at each handoff**, never appended to.
- **Size:** the router stays ~100-200 lines (author, 2026-10-07; it was
  "aim for under ~1,000 lines, but never trim for the number" before -
  author, 2026-09-24: "don't trim claude.md pointlessly, if there's
  genuinely only useful information in there don't mind keeping it").
  Nothing useful is cut to get there: it moves one hop away.

## Editing

Editing tip: for multi-line changes, write a Python script **with the Write
tool** to the scratchpad, using a small `edit(path, [(old, new), ...])`
helper that asserts each `old` occurs once and **preserves the file's BOM
and line endings** (several `.cs` files are CRLF, others LF; a mismatch
makes `old` not match). Run it with `python <file>`. Heredocs in the Bash
tool break on quoting (a long Python heredoc failed again this session); a
`git commit -F - <<'EOF'` message is fine.

## Gotchas

One line each, numbered as in [`docs/gotchas.md`](../gotchas.md) (full story, version and fix - read the entry before working near it). A new lesson gets the next number there and its one line here, in the area it belongs to, ending with its marker: `[check: <lint / test>]`, `[check: T-n]` (the task building it) or `[judgement]` (`lint.py` checks it; T-0009).

8. **Test against real payloads** - a trimmed real response, not a remembered one. [check: ReleaseJsonTests]
9. **Never round-trip text through PowerShell 5.1** - it garbles UTF-8 (`â€”`). [check: pre_tool.py warning, lint.py check_mojibake]
19. **Multi-line edits go through a script file** - Write a Python helper, raw strings; no long heredocs. [judgement]
28. **Compare both sides the same way** - same dedup and filters before pairing lists. [judgement]
95. **A child of the MCP server inherits its never-closing stdin** - close it (git hung; the tool said "cancelled"). [check: ChildProcessTests]
97. **Push main only after the checker accepts** - a merged branch rides along with any push, and a push deploys. [judgement]
98. **Commit the contract before spawning the maker** - a worktree branches from HEAD. [judgement]
99. **Never search a whole drive or the home folder** - give agents absolute paths; a `find /` crawled 22 min after its agent finished. [check: pre_tool.py wide_search]
