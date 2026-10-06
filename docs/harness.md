# Harness engineering: where the project stands and the plan to full autonomy

Written 2026-10-06 (author's request). This doc measures the project against
the twelve principles of *Learn Harness Engineering*
(https://walkinglabs.github.io/learn-harness-engineering/en/) and plans how
to take each one as far as it can go.

**Goal (author, 2026-10-06):** "all of these categories at the best possible
level ... so the project is truly fully autonomous, and requires minimal
human input anymore aside from when new features are being added and built
out."

**Source caveat:** this first pass read only the course's index page, i.e.
the twelve principles in summary. It did not read the lectures, the
projects, the resource templates (`AGENTS.md`, `feature_list.json`,
`claude-progress.md`) or the frontier design breakdowns. **The author plans
a deeper pass with more usage.** That pass should read every lecture and
template, correct this doc where the course says something different, and
add what it teaches that this summary missed. Mark each change "from lecture
N". The scores below are a baseline, not a verdict.

## Ground rule: no guessing (author, 2026-10-06)

"The model should absolutely minimise (to ideally 0) the amount of things it
guesses. If it cannot infer something that's already obvious from the given
context and instructions, it should ask me."

The loop works around this rule, not against it:
- **An unknown is a question, never a default.** That covers intent, a
  design choice, a runner-facing word, scope, and which of two readings of
  a request is meant. A fact the code, the docs, a log or the bridge can
  settle is not a question: look it up (gotcha 25 still holds - a live
  read beats an IL theory). This rule is about what only the author knows.
- **Unattended, a question parks the task, not the work.** The task gets
  `status: blocked`, `needs: author-decision` and the question written out
  in full, with the options seen and what each would change. The loop
  takes the next task that needs no answer. A half-built guess is never
  committed as if it were decided.
- **With the author present, questions come first.** A session the author
  is in starts by listing the parked questions (`tasks.py list --needs
  author-decision`). Each one asked is a chance to build further: the
  author has said a question can spark an idea. So record the answer
  *and* any idea it led to as new tasks, with "author, date" on the
  decision (as `CLAUDE.md` already does).
- **Feature design happens with the author.** New features, and any task
  whose spec leaves choices open, are `needs: author-present` and are not
  started unattended. Autonomous work is the well-specified rest: fixes
  with a clear expected behaviour, checks, tests, docs, cleanup and
  research that reports back.
- **A guess that slipped through is a defect.** When the author corrects
  something that was assumed, the correction becomes a decision in the
  docs, and the gap that allowed the guess (a missing rule, a vague task)
  is fixed too.

The author notes the course covers this too: the deeper pass should merge what it says into this section, not add a second copy.

This changes the roadmap in two places: the task record gets a
`question` field and an `author-present` value for `needs` (6a), and
Stage B (12) only ever takes tasks with no open question.

---

## Scorecard (2026-10-06)

| # | Principle | Now | Target | Biggest gap |
|---|---|---|---|---|
| 1 | Closed-loop systems | Strong | Strong+ | The loop closes per task. Nothing loops *across* tasks unattended |
| 2 | Repository as system of record | Strong | Strong | Work state is prose spread over 5 files; see 6 |
| 3 | Modular instructions | Partial | Strong | `CLAUDE.md` is 1,021 lines and loaded every turn, by every agent |
| 4 | Initialisation phase | Partial | Strong | The session-start ritual is prose, not a script |
| 5 | Behavioural constraints | Strong | Strong+ | Rules are prose only; few are checked by a machine |
| 6 | Feature lists as primitives | Weak | Strong | There is no structured task list with status and checks |
| 7 | Early-victory prevention | Good | Strong | "Done" depends on discipline; no gate refuses an unverified "done" |
| 8 | End-to-end verification | Partial | Strong | No scripted in-game run of the main paths |
| 9 | Built-in observability | Strong | Strong | Already beyond the course. Keep it |
| 10 | State cleanup | Partial | Strong | Merged worktrees, test uploads and slot backups are cleaned only by memory |
| 11 | Long-running context | Strong | Strong | The handoff works; it would shrink if 6 existed |
| 12 | Progressive automation | Midway | Loop, then graph | No agent picks, does, verifies and records work by itself |

---

## Per principle: today, gap, work

Each **Work** item has an acceptance check, so "done" is testable
(principle 7 applied to this plan).

### 1. Closed-loop systems

**Today:** the bridge plus the `forest` MCP server let an agent build,
update the game, drive it, take screenshots and read log lines (`docs/bridge.md`).
Gotcha 16 says every mechanism logs one line naming what it acted on. That
is what lets a check read the result instead of guessing it. CI builds and
tests every push and releases from tags. The site and bot deploy on push.

**Gap:** each loop is opened by a person choosing the task. A failed check
does not route back into work by itself.

**Work:**
- 1a. Every verification produces a machine-readable result (pass, fail,
  why, log excerpt) that is written to the task record (see 6), not only
  to chat. *Check:* a failed bridge check shows up as the task's status
  with its evidence, with no human relaying it.
- 1b. A failed check after a release opens a task automatically. It is
  marked `regression` and linked to the release. *Check:* break something
  on purpose in a branch and see the task appear.

### 2. Repository as system of record

**Today:** strong. The handoff, `docs/confirmed.md`, `investigations.md`,
`backlog.md`, the changelog, the gotchas and commit messages that carry
the why.

**Gap:** the state of the *work* lives in prose in five places: *Next up*,
*Pick up here*, `backlog.md`, *Awaiting an in-game check* and the QA to-do
message. They drift. On 2026-10-06 the author's QA notes from the day
before were in Discord only until a session copied them in.

**Work:** covered by 6 (one task file), and by 4 (session start files new
QA messages as tasks).

### 3. Modular instruction architecture

**Today:** some of it is split. There are 14 `docs/*.md`, six agent files
in `.claude/agents/` and memories that hold one fact each. But `CLAUDE.md`
is 1,021 lines. It holds the feature-to-file table, a 94-line gotcha index,
release lore, people, standing decisions and a long *Pick up here*. Every
session and subagent pays for all of it on every turn, and 9 of the 12
principles are about keeping attention on the task.

**Target:** the always-loaded file is about 150-250 lines: hard runtime
facts, commands, architecture in one table, the rules that apply
everywhere, and pointers. Everything else is loaded by the area it
concerns.

**Work:**
- 3a. Split `CLAUDE.md` into an always-loaded core plus area docs:
  `docs/areas/plugin.md`, `site.md`, `bot.md`, `release.md` and
  `people-and-decisions.md`. Claude Code also supports `CLAUDE.md` files
  in subfolders (`site/CLAUDE.md`, `bot/CLAUDE.md`, `src/CLAUDE.md`),
  which load only when work touches that folder. Use them for the
  site / bot / plugin rules. *Check:* root `CLAUDE.md` is under 250
  lines, and no fact appears in two files.
- 3b. Gotchas by area. Keep `docs/gotchas.md` as the full text, but put
  each one-line index entry in the area doc it belongs to (render ones
  with the site, restore ones in `savestates.md`). *Check:* the root has
  no gotcha list, only a pointer.
- 3c. The agent files name the area docs to read, so a subagent loads
  its area and no more. *Check:* read each `.claude/agents/*.md` brief
  and confirm it lists only its area.
- 3d. *Pick up here* is replaced by the task file's "in progress" view
  (see 6), leaving at most 10 lines of free text.

### 4. Dedicated initialisation phase

**Today:** the rules exist as prose: `qa_read new_only` first;
`git fetch` and check `HEAD..origin/main` before a bump; check
`git worktree list` for unmerged work; read the log before the author
launches the game. Sessions skip steps.

**Work:**
- 4a. `scripts/session-start.py`, run first every session. It does the
  following:
  - `git fetch` and report anything behind or ahead.
  - List worktrees and branches, split into unmerged and merged (merged
    ones are offered for cleanup; see 10).
  - Check that the latest tag's DLL is attached (asset URL, never
    `api.github.com`).
  - Check that the site is up (a new query string each time).
  - Report the bot container's health, if reachable without a prompt.
  - Show the open tasks by priority (from 6), and anything marked
    `in-progress` by a session that ended.

  QA messages need the MCP tool, so the script prints a reminder and the
  session runs `qa_read new_only` and files each message as a task.
  *Check:* a fresh session's first tool call is the script, and its
  output is enough to pick the next task without reading `CLAUDE.md`'s
  history.
- 4b. Wire it as a Claude Code **SessionStart hook** (settings.json,
  via the `update-config` skill) so it runs without anyone remembering.
  *Check:* open a new session and see the report appear unprompted.

### 5. Explicit behavioural constraints

**Today:** many good rules, but almost all are prose that a model must
remember. Some failures were rules that were broken anyway: an empty
release (gotcha 65), the docs in the wrong place, a deploy by hand.

**Work:** turn the rules a machine can check into checks.
- 5a. **Pre-commit / CI lints:**
  - The csproj version equals `Plugin.PluginVersion`.
  - A `CHANGELOG.md` section exists for the tag. CI already checks this
    at release; move it earlier, to before the tag.
  - Every new top-level project folder is in the csproj's `Remove` list
    (gotcha 92).
  - No `GUI.Label(new Rect(...,20), <non-constant>)` in tabs (the UiText
    rule).
  - No `new`, string concatenation or LINQ inside `OnGUI` / `DrawTab`
    bodies, as a heuristic regex.
  - `community/index.txt` is current (exists).
  - Every log line in `src/Game` keeps its prefix format.

  *Check:* each lint fails on a planted violation.
- 5b. **PreToolUse hooks** for the dangerous ones:
  - Block copying the DLL into the game folder (no hand deploys).
  - Block `api.github.com` polling.
  - Block `git push --force` to main.
  - Warn on `Get-Content | Set-Content` (gotcha 9).

  *Check:* each blocked command is refused with the rule's reason.
- 5c. Keep the prose for the judgement rules (one button one job, plain
  words, legality labels). Those stay with the model.

### 6. Feature lists as primitives (the keystone)

**Today:** there is none. Work state is spread over prose. A session has
to read about 1,000 lines to learn what to do next, and "confirmed" is a
move between files done by hand.

**Target:** one structured file, `tasks/tasks.jsonl` (one JSON object per
line, easy to merge), owned by scripts. Each record looks like this:

```json
{"id":"T-0142","title":"Run lines off leaves the line drawn","area":"plugin",
 "source":"qa:1556808081755603048","priority":2,
 "status":"built",          // todo | in-progress | built | released | confirmed | wontfix | blocked
 "needs":"bridge",          // none | bridge | author-eyes | tester | moderators | author-decision | author-present
 "question":null,           // set when parked: the question, the options, what each changes
 "verify":"bridge: run lines off + marker up -> no RunLine draw (shot + log)",
 "commits":["706ac88"],"release":null,"evidence":null,
 "blocked_by":[],"notes":"docs/backlog.md"}
```

**Work:**
- 6a. `scripts/tasks.py`: `add`, `list` (filters: status / area / needs),
  `next` (the highest-priority item an agent can do alone, i.e. `needs` is
  `none` or `bridge`), `set`, `evidence`, and views rendered into markdown
  (`docs/tasks.md`, generated, never hand-edited). *Check:* there are
  tests for its parsing and the `next` choice.
- 6b. Migrate *Next up*, `backlog.md`, *Awaiting an in-game check*,
  investigations' open items and the author's 2026-10-05/06 QA notes into
  it. `confirmed.md` stays as history, and the tasks link to it. *Check:*
  the prose lists in `CLAUDE.md` are replaced by one line: "open work:
  `python scripts/tasks.py list`".
- 6c. `bump.py` marks `built` tasks whose commits are in the release as
  `released`. `forest-tester` marks them `confirmed` with evidence.
  *Check:* one release moves its tasks without anyone editing by hand.
- 6d. The QA to-do Discord message (`qa_todo`) is rendered from the tasks
  marked `needs: tester`, so the two never drift.

### 7. Early-victory prevention

**Today:** good culture. There is the *Awaiting an in-game check* list,
gotchas 25 / 44 / 51 / 68 and the "look before claiming a render fix"
memory. But it is enforced by discipline. On 2026-10-06 a fix was
committed with build + tests only. It was honestly marked "not checked in
game", but nothing would have stopped a "done".

**Work:**
- 7a. A task cannot reach `confirmed` without `evidence` (a log excerpt,
  a shot path, a test name) that matches its `verify` field. `tasks.py`
  refuses it otherwise. *Check:* `tasks.py set T-x confirmed` without
  evidence fails.
- 7b. A **Stop hook** (runs when a turn ends) checks for the following:
  - Uncommitted changes.
  - A version bump without a pushed tag.
  - Tasks left `in-progress` with no note.
  - A changelog claim with a number and no measurement (gotcha 44). This
    one is a heuristic.

  It prints what is unfinished, so the model sees it before it stops.
  *Check:* end a turn with a dirty tree and see the warning.
- 7c. For releases: a release is not announced until the asset URL
  returns 200 and the post-release smoke check (8b) passes.

### 8. End-to-end verification

**Today:** 909 plugin unit tests, plus the site and bot tests, and CI on
every push. In-game checks are done by `forest-tester` or the author ad
hoc. No scripted check covers a whole flow.

**Work:**
- 8a. `tests/e2e/` holds bridge scripts (the `run` command already takes
  raw lines) with expectations on log lines. Each script is short and has
  timeouts on every wait (memory `validate-scripts-first`). The first set:
  - Launch, load a known save, then check the plugin version and that
    there are no `Exception` lines.
  - Go to a test spot, F7 with a start state, then check the
    `Restart '<id>':` line and position.
  - Run a timed test segment across its zones, then check the
    split / finish lines and that **uploads are off** (docs/bridge.md
    *Test spots*).
  - Quick load and Full load of a reference savestate, then check the
    keepers' summary lines.
  - Open each tab with `shot` and check the shots are made. Visual
    judgement stays with 8d.

  *Check:* `python scripts/e2e.py` runs the set unattended in a few
  minutes and writes a pass/fail report.
- 8b. Post-release smoke: after the asset is attached, `update_game`,
  then run 8a's first two scripts. The result goes to the release's
  tasks. *Check:* runs from the release step without being asked.
- 8c. The site: a Playwright-style smoke over the local site via the
  `forest-site` preview (pages load, an attempt page renders, the API
  answers). The bot: an eval subset in CI on a cheap model, if the quota
  allows (gotcha 94).
- 8d. **What cannot be automated, batched:** visual judgement (gotcha 51)
  and game feel. These go to a `needs: author-eyes` queue with the shot
  or clip attached. The author clears it in one sitting, instead of being
  asked mid-task.

### 9. Built-in observability

**Today:** beyond the course. Perf lines, load timing, the memory census,
the profiler, the allocation tracker, `Move seen:` lines, QA report zips,
crash symbolisation and stack sampling.

**Work (small):**
- 9a. A log-line catalogue (prefix, the module that writes it, meaning)
  so e2e checks and agents grep the right prefix. Generate it by scanning
  `src/` for log calls. *Check:* `scripts/log-catalogue.py` writes
  `docs/log-lines.md`.
- 9b. A report-zip reader: `scripts/read-report.py <zip>` summarises a
  tester's zip (version, exceptions, slow ticks, perf lines, the last
  actions). Then a 13 MB zip costs a few hundred tokens instead of
  thousands. Two of the author's zips (2026-10-06) are waiting for it.

### 10. State cleanup protocols

**Today:** the rules exist (slot backups, delete test uploads, god mode
back, stop the MCP before building). But on 2026-10-06 three merged
worktrees (`spot-delete-fixes`, `site-spots-in-game`,
`worktree-agent-af0fc369729bc5c67`) were still on disk.

**Work:**
- 10a. `session-start.py` (4a) lists merged worktrees and branches.
  `scripts/cleanup.py` removes them, plus `__pycache__` and stale
  scratch. *Check:* it runs after a merge, and `git worktree list` shows
  only live work.
- 10b. Test hygiene as code: e2e scripts (8a) turn uploads off at the
  start and restore them at the end. They back up a save slot and put it
  back with a size check. They put god mode back. Each step is logged.
  *Check:* after `e2e.py`, `git status`, the slot sizes and the config
  match the before state.
- 10c. Site test data: `tasks.py` or the e2e report lists the test
  attempts it uploaded, and cleanup deletes them through the admin API
  (`FOREST_SITE_ADMIN_TOKEN`, never printed).

### 11. Long-running context management

**Today:** strong. The handoff always current, the session-switching
rules, investigations staying in one session, `docs/session-log.md` for
history.

**Work:** mostly follows from 3 and 6. *Pick up here* becomes a short note
plus `tasks.py list --status in-progress`, and investigations become
tasks with a running `notes` file. One addition:
- 11a. A per-task notes file (`tasks/notes/T-xxxx.md`) for anything
  multi-session: what was read, theories dropped, the next step. A cold
  session continues from it instead of from chat memory.

### 12. Progressive automation (manual, then loop, then graph)

**Today:** midway. There are role subagents with a model and effort each,
worktree isolation, cloud offload via routines, `/loop` and scheduled
tasks. The main session still picks every task.

**Target, in stages:**
- **Stage A, assisted loop** (needs 4 + 6 + 7a): one session runs
  `tasks.py next`, does it, verifies it, records the evidence and takes
  the next, asking only on `needs: author-*`.
- **Stage B, unattended loop** (needs 8 + 10): a `/loop` or scheduled
  routine works through `needs: none` tasks (docs, site, tests, bot
  cards) without the game. With the game up and the bridge on, it also
  takes `needs: bridge` tasks. Each release passes the 8b smoke before
  the next task.
- **Stage C, graph:** a dispatcher fans independent tasks out to the
  role agents (`forest-dev` / `-site` / `-knowledge` in worktrees, at most
  2-3 at once). It merges with `merge-keepboth.py`, releases with
  `bump.py`, and routes failures back as tasks. Only one agent drives the
  game at a time; this needs a lock file.

**What stays human, by design:**
- New feature direction.
- UI taste and anything visual (the `author-eyes` queue, batched).
- Legality and publishing categories (the moderators).
- Testers' in-game feel.
- Decisions recorded as `needs: author-decision`.

Everything else is meant to need no prompt.

---

## Roadmap (suggested order, one per session)

1. **Tasks file + `tasks.py` + migration** (6a, 6b). This is the keystone;
   the rest builds on it.
2. **Session start + SessionStart hook + cleanup** (4a, 4b, 10a).
3. **Slim `CLAUDE.md` into core + area docs** (3a-3d, 11a). Do this after
   1, so the prose lists are already gone.
4. **Gates:** evidence-required `confirmed`, Stop hook, lints,
   PreToolUse blocks (7a, 7b, 5a, 5b).
5. **E2E bridge suite + post-release smoke + test hygiene** (8a, 8b, 10b,
   10c). Needs the game; this is `forest-tester` / `forest-researcher`
   work.
6. **Report reader + log catalogue** (9a, 9b).
7. **Stage A loop**, then **Stage B**, then **Stage C** (12). Promote one
   stage at a time, and only after the previous one ran a week without a
   human fixing its output.

Before step 1: the author's deeper pass on the course (see *Source caveat*).
The course's own `feature_list.json` / `claude-progress.md` templates
should shape the task file's format rather than this doc's guess.

## Status log

- 2026-10-06: baseline written from the course index; nothing built yet.
- 2026-10-06: *Ground rule: no guessing* added (author).
