# Harness engineering: where the project stands and the plan to full autonomy

Written 2026-10-06 (author's request). This doc measures the project against
*Learn Harness Engineering*
(https://walkinglabs.github.io/learn-harness-engineering/en/, source repo
`walkinglabs/learn-harness-engineering`) and plans how to take each
principle as far as it can go.

**Goal (author, 2026-10-06):** "all of these categories at the best possible
level ... so the project is truly fully autonomous, and requires minimal
human input anymore aside from when new features are being added and built
out."

**Sources.** The first pass (2026-10-06) read only the course's index page.
The deeper pass (2026-10-07) read the whole English course from its repo:
all 14 lectures with their code notes, the 8 projects' briefs, the
templates (`AGENTS.md`, `CLAUDE.md`, `feature_list.json`,
`claude-progress.md`, `session-handoff.md`, `clean-state-checklist.md`,
`evaluator-rubric.md`, `quality-document.md`, `init.sh`), the reference
playbooks, the OpenAI repo template and SOPs, the `harness-creator` skill
and the four frontier breakdowns (Claude Code, Codex, Pi, DeepSeek).
Changes from that pass are marked "(L*n*)" for lecture *n*, "(T)" for a
template and "(CC)" for the Claude Code breakdown.

**How to read the course's numbers (all lectures):** every cutoff is a
"teaching default", and most worked examples are labelled as illustrations,
not measurements. The one study it cites on context files (ETH Zurich)
found they did *not* generally raise task success and cost ~20% more
inference (L4). So: copy the mechanisms, not the numbers, and measure
our own harness changes (see *Measuring the harness*).

## Ground rule: no guessing (author, 2026-10-06)

"The model should absolutely minimise (to ideally 0) the amount of things it
guesses. If it cannot infer something that's already obvious from the given
context and instructions, it should ask me."

The course reaches the same rule from the failure side: a vague
requirement leaves the agent "only able to guess" and a wrong guess costs
several times what being specific would have (L1); wherever the repo's map
is blank "the agent has to guess", and every new session guesses again
(L3); a fact the agent cannot find in the repo is "operationally
unavailable" (OpenAI core beliefs); the maker's brief says "if you don't
know something, say you don't know" (L13); and a loop's stop conditions
include "an issue requires human decision" (L13). Its answer is to settle
open choices *before* coding, in a short contract (L11), and to write
each decision into the repo in the same session it is made (OpenAI SOP).

The loop works around this rule, not against it:
- **An unknown is a question, never a default.** That covers intent, a
  design choice, a runner-facing word, scope, and which of two readings of
  a request is meant. A fact the code, the docs, a log or the bridge can
  settle is not a question: look it up (gotcha 25 still holds - a live
  read beats an IL theory). This rule is about what only the author knows.
- **Questions come before code** (L11 sprint contract). Every task gets a
  short contract before work starts: what it changes, what counts as done,
  what must not change. Writing the contract is where open choices show
  up; each one becomes a question, not a default.
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
  decision (as `CLAUDE.md` already does), in the same session (OpenAI SOP).
- **Feature design happens with the author.** New features, and any task
  whose spec leaves choices open, are `needs: author-present` and are not
  started unattended. Autonomous work is the well-specified rest: fixes
  with a clear expected behaviour, checks, tests, docs, cleanup and
  research that reports back.
- **A guess that slipped through is a defect** in the harness, not in the
  model (L1's diagnostic loop). When the author corrects something that
  was assumed, the correction becomes a decision in the docs, and the gap
  that allowed the guess (a missing rule, a vague task, a missing check)
  is fixed too - as a check where one is possible (L10, *review feedback
  promotion*).

This changes the roadmap in two places: the task record gets a
`question` field and an `author-present` value for `needs` (6a), and the
unattended loop (12) only ever takes tasks with no open question.

---

## The course in one page (what the deeper pass added)

- **A harness is five subsystems** (L2): instructions, tools, environment,
  state, feedback. When a task fails, name the subsystem at fault - task
  spec, context, environment, verification or state (L1) - fix that
  layer, and log it. "The model isn't good enough" is the last
  explanation, not the first. Our gotchas are this log in prose, without
  the layer.
- **The entry file is a router of 50-200 lines** (L4; OpenAI's is ~100):
  overview, run / verify commands, at most ~15 hard constraints at the top
  or bottom (never the middle - *lost in the middle*), links to topic docs
  of 50-150 lines. Every rule carries its source, when it applies and when
  it can be deleted.
- **The fresh-session test** (L3) is the acceptance check for the docs: a
  new session given only the repo answers five questions - what is this,
  how is it organised, how do I run it, how do I verify it, where are we
  now.
- **Feature list = the triple** (L8): behaviour, verification, state.
  States are changed by the harness, never by the agent's say-so; a pass
  needs recorded evidence; a passed item never goes back (a regression is
  a new item). Granularity: one item fits one session.
- **WIP = 1** (L7): one active task; the next one starts only when this
  one passes or is written up as blocked. Each task names what must
  *not* change (T: next-task template).
- **Three-layer done** (L9): static (build, lint), runtime (tests, the app
  starts), system (the end-to-end path). Unit tests passing is not done.
  No refactoring before the behaviour is verified.
- **Maker and checker are separate** (L9, L13, L14): a model grades its
  own work generously, so the check runs in a fresh context that never
  saw the maker's reasoning - a script, a test or another agent. "Someone
  in your crew must not believe you." Claude Code's `/goal` judges the
  stop condition in an independent session.
- **Error messages carry the fix** (L9, L10): what is wrong, why, how to
  fix it - so a failed check routes the agent instead of just stopping it.
- **Clean state has five parts** (L12): build passes, tests pass,
  progress recorded, no stale artifacts, the startup path works. A
  session is not done without them. Cleanup is idempotent.
- **A quality document** (L12, T) grades each area A-D (verification,
  legibility, test stability, gaps) so sessions know where the codebase
  is weak, and a **weekly cleanup loop** keeps it current.
- **Simplify the harness** (L2, L12): once a month switch one component
  off and see whether results degrade; delete it if they do not.
- **A loop is a goal, a verification and a stop condition** (L13). Stop
  conditions include: all checks pass, max rounds, no progress for 3
  rounds, a blocker that needs a human. `/goal` is for work with an end,
  `/loop` for watching. Loops have four silent costs: verification debt,
  comprehension rot, cognitive surrender, token blowout.
- **A graph only when it earns it** (L14): parallel units, real rollback
  paths, state worth checkpointing, verifiable nodes, coordination worth
  its cost - at least three of the five. The *orchestration tax*: agents
  are cheap to start, expensive to close, and the closer is the one human
  - the author's attention is the serial resource. Loops need **anchors**
  to reality (real outcomes, human spot checks) or they drift together.
- **Claude Code's own layers** (CC): `CLAUDE.md` for *what*, skills for
  *how* (procedures, loaded when triggered), MCP for *where*, hooks for
  *when to enforce*. Subfolder `CLAUDE.md` files load only when work
  touches that folder. Auto memory loads at most 200 lines / 25 KB of its
  index.

---

## Scorecard (2026-10-06, rescored 2026-10-07)

| # | Principle | Now | Target | Biggest gap |
|---|---|---|---|---|
| 1 | Closed-loop systems | Strong | Strong+ | The loop closes per task. Nothing loops *across* tasks unattended |
| 2 | Repository as system of record | Strong | Strong | Work state is prose spread over 5 files; see 6 |
| 3 | Modular instructions | Good (was Weak; T-0003) | Strong | Router + area docs + folder `CLAUDE.md` files; procedures are project skills (3e, T-0004) |
| 4 | Initialisation phase | Partial | Strong | The session-start ritual is prose, not a script, and runs no baseline check (L6) |
| 5 | Behavioural constraints | Strong | Strong+ | Rules are prose only; few are checked by a machine |
| 6 | Feature lists as primitives | Weak | Strong | There is no structured task list with status and checks |
| 7 | Early-victory prevention | Good | Strong | "Done" depends on discipline; the maker checks its own work (L9) |
| 8 | End-to-end verification | Good (was Partial; T-0010) | Strong | Six golden journeys + the post-release smoke run unattended; visual judgement still by eyes (8d) |
| 9 | Built-in observability | Strong | Strong | Runtime layer beyond the course; the *process* layer (contracts, rubric) is missing (L11) |
| 10 | State cleanup | Good (was Partial; T-0002, T-0010) | Strong | Cleanup and test hygiene are scripts now; the quality document grades every area (T-0013); the weekly cleanup and the monthly review are skills with a session-start trigger (T-0014) |
| 11 | Long-running context | Strong | Strong | The handoff works; it would shrink if 6 existed |
| 12 | Progressive automation | Midway | Loop, then graph | No agent picks, does, verifies and records work by itself |

On the course's own loop ladder (L13) the project is between level 2
(scheduled single tasks: routines, `/loop`) and level 3 (maker / checker
split, worktrees per task): the role agents and worktrees exist, but no
loop feeds itself from external state.

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
- 1c. **Failure attribution** (L1, L2): when a task fails or the author
  corrects something, the record names the harness layer at fault
  (`spec | context | environment | verification | state`) and what was
  changed in that layer. *Check:* `tasks.py stats` shows failures by
  layer; the most common layer is the next harness item.

### 2. Repository as system of record

**Today:** strong. The handoff, `docs/confirmed.md`, `investigations.md`,
`backlog.md`, the changelog, the gotchas and commit messages that carry
the why.

**Gap:** the state of the *work* lives in prose in five places: *Next up*,
*Pick up here*, `backlog.md`, *Awaiting an in-game check* and the QA to-do
message. They drift. On 2026-10-06 the author's QA notes from the day
before were in Discord only until a session copied them in.

**Work:** covered by 6 (one task file), and by 4 (session start files new
QA messages as tasks). Plus:
- 2a. **Decisions in one file** (L5 decision log): *Standing decisions and
  people* plus the dated "(author, date)" decisions scattered through
  `CLAUDE.md` move to `docs/decisions.md` - what, why, rejected
  alternative, who, when. *Check:* `grep -c "author, 20" CLAUDE.md` is
  near zero and every hit points there.

### 3. Modular instruction architecture

**Today:** some of it is split. There are 15 `docs/*.md`, six agent files
in `.claude/agents/` and memories that hold one fact each. But `CLAUDE.md`
is 1,061 lines. It holds the feature-to-file table, a 94-line gotcha index,
release lore, people, standing decisions and a long *Pick up here*. Every
session and subagent pays for all of it on every turn.

**Target (corrected, L4):** the always-loaded file is a router: overview,
build / test / deploy commands, the hard constraints (at most ~15, at the
top or bottom), and one-line links to topic docs, each with when to read
it. The course's range is 50-200 lines; the size for this project is
**decided: ~100-200 lines**, moved not deleted (see *Decisions*).

**Work:**
- 3a. Split `CLAUDE.md` into the router plus area docs:
  `docs/areas/plugin.md`, `site.md`, `bot.md`, `release.md`, and
  `docs/decisions.md` (2a). Use folder `CLAUDE.md` files (`site/`, `bot/`,
  `src/`) for rules that apply only there - Claude Code loads them only
  when work touches that folder (CC). *Check:* the **fresh-session test**
  (L3): a new session given only the router answers the five questions
  (what, how organised, how to run, how to verify, where are we now) and
  finds a given rule in one hop; no fact appears in two files.
- 3b. Gotchas by area. Keep `docs/gotchas.md` as the full text, but put
  each one-line index entry in the area doc it belongs to (render ones
  with the site, restore ones in `savestates.md`). *Check:* the root has
  no gotcha list, only a pointer.
- 3c. The agent files name the area docs to read, so a subagent loads
  its area and no more. *Check:* read each `.claude/agents/*.md` brief
  and confirm it lists only its area.
- 3d. *Pick up here* is replaced by the task file's "in progress" view
  (see 6), leaving at most 10 lines of free text.
- 3e. **Procedures become project skills** (CC: skills hold the *how*):
  the release steps, a bridge test session, the QA session start and the
  site deploy watch move from prose into `.claude/skills/<name>/SKILL.md`,
  loaded only when the task matches. *Check:* the router names each skill
  in one line, and its old prose is gone from `CLAUDE.md`.
- 3f. **Rule hygiene** (L4): each hard constraint carries its source and
  when it can be removed; contradictory or dead rules are deleted at each
  monthly harness review (12d).

### 4. Dedicated initialisation phase

**Today:** the rules exist as prose: `qa_read new_only` first;
`git fetch` and check `HEAD..origin/main` before a bump; check
`git worktree list` for unmerged work; read the log before the author
launches the game. Sessions skip steps, and nothing checks that the
baseline is green before new work starts.

**Work:**
- 4a. `scripts/session-start.py`, run first every session - the project's
  `init.sh` (L6, T). It does the following:
  - `git fetch` and report anything behind or ahead.
  - List worktrees and branches, split into unmerged and merged (merged
    ones are offered for cleanup; see 10).
  - **Baseline check** (L6, T: "if baseline verification is already
    failing, fix that first"): the plugin build and the test projects,
    or the last CI result for `HEAD` when that is enough. A red baseline
    becomes the first task.
  - Check that the latest tag's DLL is attached (asset URL, never
    `api.github.com`).
  - Check that the site is up (a new query string each time).
  - Report the bot container's health, if reachable without a prompt.
  - Show the open tasks by priority (from 6), anything marked
    `in-progress` by a session that ended, and the parked questions.

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

**Work:** turn the rules a machine can check into checks. Every failing
check prints **what, why and how to fix it** (L9, L10), e.g.
`ERROR: GUI.Label with variable text in src/Modules/X.cs:120 / WHY: clips
(UiText rule) / FIX: UiText.Draw(rect, text) and add the height to y`.
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

  *Check:* each lint fails on a planted violation, with its fix text.
- 5b. **PreToolUse hooks** for the dangerous ones:
  - Block copying the DLL into the game folder (no hand deploys).
  - Block `api.github.com` polling.
  - Block `git push --force` to main.
  - Warn on `Get-Content | Set-Content` (gotcha 9).

  *Check:* each blocked command is refused with the rule's reason.
- 5c. Keep the prose for the judgement rules (one button one job, plain
  words, legality labels). Those stay with the model.
- 5d. **Review feedback promotion** (L10): go through the 94 gotchas once
  and mark each `checkable` (a lint, a test or a log assertion can catch
  it) or `judgement`. Every `checkable` one becomes a task; from then on
  a new gotcha ships with its check when it can have one. *Check:* the
  gotcha index shows the check's name beside each checkable entry.

### 6. Feature lists as primitives (the keystone)

**Today:** there is none. Work state is spread over prose. A session has
to read about 1,000 lines to learn what to do next, and "confirmed" is a
move between files done by hand.

**Target:** one structured file owned by scripts, shaped by the course's
`feature_list.json` (L8, T): the triple (behaviour, verification, state)
plus evidence, priority, area and notes. Two deliberate differences from
the template, both because of how this project ships:
- **More states.** The template's four (`not_started`, `in_progress`,
  `blocked`, `passing`) assume "verified" happens in the repo. Here a
  fix is verified in three places: tests (built), the release runners
  get (released) and the live game or a tester (confirmed). So:
  `todo | in-progress | built | released | confirmed | wontfix | blocked`.
  `confirmed` is the template's `passing`: evidence required, never
  reversed (L8); a regression is a new task linked to the old one.
- **One record per line** (`tasks/tasks.jsonl`) instead of one JSON
  document, so two sessions adding tasks merge without conflicts (L3,
  ACID isolation).

```json
{"id":"T-0142","title":"Run lines off leaves the line drawn","area":"plugin",
 "behavior":"With run lines off, no run line is drawn, even with a marker or replay up",
 "source":"qa:1556808081755603048","priority":2,
 "status":"built",          // todo | in-progress | built | released | confirmed | wontfix | blocked
 "needs":"bridge",          // none | bridge | author-eyes | tester | moderators | author-decision | author-present
 "question":null,           // set when parked: the question, the options, what each changes
 "verify":["run lines off","marker up","shot + log: no RunLine draw"],
 "keep":"other late-pass drawers still draw",   // what must not change (L7)
 "commits":["706ac88"],"release":null,"evidence":[],
 "layer":null,              // on a failure: spec | context | environment | verification | state (1c)
 "blocked_by":[],"notes":"docs/backlog.md"}
```

**Work:**
- 6a. `scripts/tasks.py`: `add`, `list` (filters: status / area / needs),
  `next` (the highest-priority item an agent can do alone, i.e. `needs` is
  `none` or `bridge`, nothing `in-progress` already - WIP = 1, L7), `set`,
  `evidence`, `stats`, and views rendered into markdown (`docs/tasks.md`,
  generated, never hand-edited). *Check:* there are tests for its
  parsing, the `next` choice and the gates (7a).
- 6b. Migrate *Next up*, `backlog.md`, *Awaiting an in-game check*,
  investigations' open items and the author's 2026-10-05/06 QA notes into
  it. Granularity: one task fits one session (L8); bigger items are split
  or get a plan (11a). `confirmed.md` stays as history, and the tasks
  link to it. *Check:* the prose lists in `CLAUDE.md` are replaced by one
  line: "open work: `python scripts/tasks.py list`".
- 6c. `bump.py` marks `built` tasks whose commits are in the release as
  `released`. `forest-tester` marks them `confirmed` with evidence.
  *Check:* one release moves its tasks without anyone editing by hand.
  **Built** (T-0007): `tasks.release_plan` / `mark_released`, called by
  `bump.py` - built plugin tasks whose commits are all in HEAD (a
  worktree's are left for its merge; ones whose commits touch no plugin
  path - scripts / docs only - stay `built` and confirm on test / lint
  evidence, T-0196), the version of the first tag
  holding them or the new one; a checker task with no accept stops the
  bump before any file is edited (`scripts/tests/test_bump.py`).
- 6d. The QA to-do Discord message (`qa_todo`) is rendered from the tasks
  marked `needs: tester`, so the two never drift. **Built** (T-0007):
  a `qa` field (the line testers read), `tasks.py qa-todo`, `qa_todo
  from_tasks`; only what testers still have to do (author, 2026-10-07 -
  docs/decisions.md); `tasks.py check` refuses an open tester task with
  no `qa` line.

### 7. Early-victory prevention

**Today:** good culture. There is the *Awaiting an in-game check* list,
gotchas 25 / 44 / 51 / 68 and the "look before claiming a render fix"
memory. But it is enforced by discipline, and the session that wrote a
fix is the one that judges it. On 2026-10-06 a fix was committed with
build + tests only. It was honestly marked "not checked in game", but
nothing would have stopped a "done".

**Work:**
- 7a. A task cannot reach `confirmed` without `evidence` (a log excerpt,
  a shot path, a test name) that matches its `verify` steps. `tasks.py`
  refuses it otherwise, and refuses to move a `confirmed` task back
  (L8). *Check:* `tasks.py set T-x confirmed` without evidence fails.
- 7b. A **Stop hook** (runs when a turn ends) checks the clean-state list
  (L12, T): uncommitted changes, a version bump without a pushed tag,
  tasks left `in-progress` with no note, and (heuristic) a changelog
  claim with a number and no measurement (gotcha 44). It prints what is
  unfinished, so the model sees it before it stops. *Check:* end a turn
  with a dirty tree and see the warning.
- 7c. For releases: a release is not announced until the asset URL
  returns 200 and the post-release smoke check (8b) passes.
- 7d. **A separate checker** (L9, L13, L14): `confirmed` is set by a
  fresh context that did not write the change - the e2e script (8a),
  `forest-tester`, or a review agent briefed with the task's `verify`
  and `keep` only, told to find faults (T: checker prompt). How often to
  pay for an extra agent: **decided** - every behaviour change, a lean
  checker (see *Decisions*). **Built** (T-0006): `.claude/agents/forest-checker.md`,
  `tasks.py brief` / `review`, the gates at `released` / `confirmed` and
  a Stop-hook line (docs/areas/workflow.md *The checker*).
- 7e. **Done has three layers** (L9): the task's `verify` names which of
  static (build), runtime (tests, the game loads the plugin) and system
  (the in-game path, the live page) it needs; a plugin behaviour change
  always needs the system layer.

### 8. End-to-end verification

**Today:** 909 plugin unit tests, plus the site and bot tests, and CI on
every push. In-game checks are done by `forest-tester` or the author ad
hoc. No scripted check covers a whole flow. (L10's point applies
directly: our restore bugs are almost all component-boundary defects -
a keeper and the game's own load disagreeing - which unit tests cannot
see.)

**Work:**
- 8a. `tests/e2e/` holds bridge scripts (the `run` command already takes
  raw lines) with expectations on log lines. Each script is short and has
  timeouts on every wait (memory `validate-scripts-first`). The first set
  are the project's *golden journeys* (OpenAI observability SOP):
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
  minutes and writes a pass/fail report. **Built** (T-0010): six
  journeys in `tests/e2e/` (launch, restart, timed, restores, tabs, run
  mode), ~3 min, report in `tests/e2e/reports/`; driven through the MCP
  server's `--call` mode (docs/bridge.md *The e2e suite*). It also holds
  the gotcha checks T-0136..T-0139 (31, 38, 40, 82).
- 8b. Post-release smoke: after the asset is attached, `update_game`,
  then run 8a's first two scripts. The result goes to the release's
  tasks. *Check:* runs from the release step without being asked.
  **Built** (T-0010): `e2e.py --smoke --update --release vX`, step 5 of
  the release skill; a note on each released task.
- 8c. The site: a Playwright-style smoke over the local site via the
  `forest-site` preview (pages load, an attempt page renders, the API
  answers). The bot: an eval subset in CI on a cheap model, if the quota
  allows (gotcha 94). **Built** (T-0011): `scripts/site-smoke.py`
  (headless Chromium over a throwaway site, before the site deploy) and
  bot.yml's warn-only 7-question eval on the live key after the deploy.
- 8d. **What cannot be automated, batched:** visual judgement (gotcha 51)
  and game feel. These go to a `needs: author-eyes` queue with the shot
  or clip attached. The author clears it in one sitting, instead of being
  asked mid-task - the course's *orchestration tax* (L14): the author's
  attention is the one serial resource, so it is spent in batches. The
  author's eyes and the testers are also the loop's **anchors** to
  reality (L14).

### 9. Built-in observability

**Today:** the runtime layer is beyond the course. Perf lines, load
timing, the memory census, the profiler, the allocation tracker,
`Move seen:` lines, QA report zips, crash symbolisation and stack
sampling. The course's second layer, **process observability** (L11) -
plans, contracts, rubrics that say *why* a change should be accepted -
is missing.

**Work:**
- 9a. A log-line catalogue (prefix, the module that writes it, meaning)
  so e2e checks and agents grep the right prefix. Generate it by scanning
  `src/` for log calls. *Check:* `scripts/log-catalogue.py` writes
  `docs/log-lines.md`. **Built** (T-0012): 659 calls, 214 prefixes, each
  with a hand-written meaning the script keeps; `--check` in `lint.py`
  (gotcha 16's check) fails on a stale catalogue, an empty meaning or a
  call with no prefix (the 50 unprefixed calls fixed); tests
  `scripts/tests/test_log_catalogue.py`.
- 9b. A report-zip reader: `scripts/read-report.py <zip>` summarises a
  tester's zip (version, exceptions, slow ticks, perf lines, the last
  actions). Then a 13 MB zip costs a few hundred tokens instead of
  thousands. **Built** (T-0008): 23-73 lines for the 19 QA zips on
  hand (0.5-6.6 MB each); tests `scripts/tests/test_read_report.py`.
  The author's two zips of 2026-10-06 were not found on disk (asked).
- 9c. **Task contracts** (L11): before work starts, `tasks.py start T-x`
  asks for the contract - scope, the `verify` steps, the `keep` list,
  exclusions - and stores it on the task. Writing it is where open
  questions surface (*Ground rule*). *Check:* no task reaches `built`
  without one.
- 9d. **A rubric for the checker** (L11, T: evaluator rubric):
  correctness, verification ran with evidence, scope kept, survives a
  restart / reload, legible to the next session, handoff ready - each
  0-2, verdict accept / revise / block. *Check:* 7d's checker returns it.
  **Built** (T-0006): `tasks.py review --scores` refuses a partial rubric
  or an accept with correctness below 2 or any 0; `tasks.py stats` reports
  first-review accepts and reviews per task.

### 10. State cleanup protocols

**Today:** the rules exist (slot backups, delete test uploads, god mode
back, stop the MCP before building). But on 2026-10-06 three merged
worktrees (`spot-delete-fixes`, `site-spots-in-game`,
`worktree-agent-af0fc369729bc5c67`) were still on disk.

**Work:**
- 10a. `session-start.py` (4a) lists merged worktrees and branches.
  `scripts/cleanup.py` removes them, plus `__pycache__` and stale
  scratch; it is idempotent (L12). *Check:* it runs after a merge, runs
  twice with no change the second time, and `git worktree list` shows
  only live work.
- 10b. Test hygiene as code: e2e scripts (8a) turn uploads off at the
  start and restore them at the end. They back up a save slot and put it
  back with a size check. They put god mode back. Each step is logged.
  *Check:* after `e2e.py`, `git status`, the slot sizes and the config
  match the before state. **Built** (T-0010): the slot by content digest
  (`Slot1.e2e-backup`), the config by its values, god mode / infinite
  energy / the window; a crash aborts, closes the game and writes the
  config back.
- 10c. Site test data: `tasks.py` or the e2e report lists the test
  attempts it uploaded, and cleanup deletes them through the admin API
  (`FOREST_SITE_ADMIN_TOKEN`, never printed). **Built** (T-0010): run
  mode attempts deleted on the site and dropped from the Runs tab's
  list; test runs never upload (uploads off).
- 10d. **A quality document** (L12, T): `docs/quality.md` grades each
  area A-D - plugin modules by group (savestates, runs, practice, run
  mode, UI, perf), site, bot, release - on verification, legibility for
  an agent, test stability and known gaps. *Check:* each session that
  changes an area updates its row; the lowest grade feeds the task list.
  **Built** (T-0013): 16 areas - the plugin by group, site app, site maps
  / 3D, bot, knowledge, release, bridge / e2e / QA, dev tools, harness
  (author: every part of the project). `lint.py` checks the grades (the
  worst of four), an open task on every C / D row and that every tracked
  file sits in some area's paths; `session-start.py` names the rows whose
  paths changed after their review.
- 10e. **A weekly cleanup loop** (L12): stale docs, structural lint
  violations, quality grades, dead code - each finding a small task.
  *Check:* a scheduled routine runs it and files tasks, nothing more.
  **Built** (T-0014): `scripts/audit.py` finds dead doc paths, stale
  lint-baseline entries, unused C# / Python code, orphan files and stale
  quality rows; `scripts/audit-ignore.txt` keeps checked false positives
  with their reason; `--file` files one P4 task per (kind, file). Skill
  `weekly-cleanup` checks each candidate, files, re-grades, logs a row in
  `docs/quality.md`'s *Cleanup log*; session-start says when it is due
  (7 days) - decision 8.

### 11. Long-running context management

**Today:** strong. The handoff always current, the session-switching
rules, investigations staying in one session, `docs/session-log.md` for
history. The course agrees with the author's rule to keep one
investigation in one session: compaction keeps the "what" and loses the
"why" (L5), and on current Opus compaction is workable without forced
resets (L5).

**Work:** mostly follows from 3 and 6. *Pick up here* becomes a short note
plus `tasks.py list --status in-progress`, and investigations become
tasks with a running `notes` file. One addition:
- 11a. A per-task notes file (`tasks/notes/T-xxxx.md`) for anything
  multi-session: what was read, theories dropped, the decisions and why
  (L5), the next step. A cold session continues from it instead of from
  chat memory.

### 12. Progressive automation (manual, then loop, then graph)

**Today:** midway (L13 ladder: between 2 and 3). There are role subagents
with a model and effort each, worktree isolation, cloud offload via
routines, `/loop` and scheduled tasks. The main session still picks every
task.

**Every loop below is written as goal + verification + stop condition**
(L13). Stop conditions for all of them: the task's checks pass; a max of
rounds; no progress for 3 rounds (the same failure again); or a question
for the author (parks the task). Each loop keeps a state file (T: loop
state) and reports rounds, passes, failures and human interventions.

**Target, in stages:**
- **Stage A, assisted loop** (needs 4 + 6 + 7a): one session runs
  `tasks.py next`, writes the contract, does it, has it checked (7d),
  records the evidence and takes the next, asking only on `needs:
  author-*`. Ladder level 1 (a goal runner).
- **Stage B, unattended loop** (needs 8 + 10): a scheduled routine works
  through `needs: none` tasks (docs, site, tests, bot cards) without the
  game. With the game up and the bridge on, it also takes `needs: bridge`
  tasks. Each release passes the 8b smoke before the next task. What it
  may publish unattended: main and releases, once the smoke passes
  (see *Decisions*). Ladder levels 2 and 4 (scheduled, self-feeding).
- **Stage C, graph** - only if it scores at least three of L14's five
  criteria when we get there. A dispatcher fans independent tasks out to
  the role agents (`forest-dev` / `-site` / `-knowledge` in worktrees, at
  most 2-3 at once), each a node with its own context, handing off only
  through the task file (the shared state). It merges with
  `merge-keepboth.py`, releases with `bump.py`, and routes failures back
  as tasks. Only one agent drives the game at a time; this needs a lock
  file. The graph's routing rules are written down in this doc before
  any of it is built (L14: "a graph puts the problem on paper").

**Guarding against the silent costs** (L13): verification debt - stop
conditions are machine checks, never "looks fine"; comprehension rot and
cognitive surrender - every loop round leaves a one-paragraph summary the
author can skim, and the author's taste decisions stay with the author;
token blowout - each round starts from the task file, not from a growing
transcript.

- 12d. **Monthly harness review** (L2, L12): pick one harness component
  (a hook, a lint, a rule, an agent brief), switch it off for a few
  tasks, and keep it only if results got worse. Log it in
  `docs/quality.md`'s simplification table. *Check:* one review a month
  appears in the log.
  **Built** (T-0014): skill `harness-review`; `tasks.py stats --since /
  --until` compares a window of finished tasks; session-start says when
  a review is due (30 days) and counts an open one's tasks (n/5) -
  decision 8.

**What stays human, by design:**
- New feature direction.
- UI taste and anything visual (the `author-eyes` queue, batched).
- Legality and publishing categories (the moderators).
- Testers' in-game feel.
- Decisions recorded as `needs: author-decision`.

Everything else is meant to need no prompt.

---

## Measuring the harness

The course's numbers are illustrations (see the top), so we measure our
own. `tasks.py stats` reports, per week: tasks finished, the share that
passed their first check, rounds per task, author interventions, and
failures by layer (1c). A harness change (a new hook, the slimmed
router) is judged by these, before and after - not by how it reads.

---

## Decisions (author, 2026-10-07)

Asked after the deeper pass; each replaces an "open question" above.

1. **Router size** (3): a router of **~100-200 lines**; every other fact
   moves one hop away (area docs, `docs/decisions.md`, folder
   `CLAUDE.md` files) - moved, not deleted.
2. **The checker** (7d): first "in-game / visual tasks only", then
   widened the same day (author: a one-task subagent with one purpose and
   little context "could be pretty efficient"): **every behaviour change**
   (plugin, site, bot) gets a lean fresh-context checker - Sonnet, given
   only the task record and the diff, told to find faults; in-game
   behaviour goes through the e2e script or `forest-tester`. Docs and
   harness tasks rely on the machine gates.
   **How it runs** (author, 2026-10-07, T-0006): at `built`, **before**
   the push or release; *revise* hands the task back to its maker,
   *block* parks it with the checker's question. A plugin *accept* is a
   gate, not a confirmation - `confirmed` still needs in-game evidence
   (7e); for site and bot the accept plus the live check confirms. The
   checker **re-runs the suites the diff touches** (author left it to
   Claude: the review happens before CI, so it is the only independent
   test run; it costs ~20-60 s a suite).
3. **Unattended publishing** (12, Stage B): the loop **may commit to main
   and tag releases** once build, tests and the post-release in-game
   smoke (8b) pass; anything visual waits in the `author-eyes` queue.
4. **Procedures as skills** (3e): **yes** - release, bridge test, QA
   session start and site deploy watch become project skills.
5. **The log catalogue** (9a, T-0012): each prefix's **meaning is
   hand-written** in `docs/log-lines.md` and kept by the generator; the
   lint fails on a **stale catalogue and on a call with no prefix**, and
   all of today's unprefixed calls were fixed (no baseline); a message
   built elsewhere (a report builder, a StringBuilder) **declares** its
   prefix in a `// log: Name` comment instead of splitting the builder,
   and an indented report row needs none.
6. **The quality document** (10d, T-0013): an area's grade is **the
   worst of its four** dimensions; stale rows are named in the
   **session-start report** (the weekly loop re-grades them, no Stop-hook
   block); **every part of the project is graded** - the harness and the
   knowledge base too, and anything added later (a file in no area fails
   the lint) - "this helps evaluate the true scope of the project and
   removes blindsiding things that do need genuine work".

7. **The assisted loop** (12 Stage A, T-0015): the main session
   **orchestrates and subagents do each round's work** (the loop's
   exception to one session, one task; its context grows by summaries);
   **5 rounds** a run; the pool is **`needs: none`** (+ bridge when the
   game is up) - author-* tasks are skipped, their questions asked at the
   run's start; **2 checker revises** a task, the third parks it. The
   redesign tasks (T-0018..T-0023) are `author-present`: visual work is
   built with the author, so the loop skips them.
8. **The weekly cleanup and the monthly review** (10e, 12d; T-0014):
   both run **when session-start says they are due** (like the bot
   review: no scheduled routine, nothing unattended); the cleanup's dead
   code is **"anything redundant that isn't beneficial to the project
   long term and serves no purpose / is no longer implemented or
   needed"** - doc paths, baseline entries, C# and Python code, orphan
   files; a review switches one component off for **the next 5 finished
   tasks**, and **the author decides** keep / remove from the
   before / after stats. Which components may go was left to Claude:
   **all but the safety guards** (rules 2, 4, 13, 15 and their hooks) -
   five tasks of stats cannot show the worth of a guard against a rare,
   costly event.

---

## Roadmap (suggested order, one per session)

1. **Tasks file + `tasks.py` + migration** (6a, 6b, 9c, 11a). This is the
   keystone; the rest builds on it.
2. **Session start + SessionStart hook + cleanup** (4a, 4b, 10a).
3. **Slim `CLAUDE.md` into router + area docs + decisions + skills** (2a,
   3a-3f). Do this after 1, so the prose lists are already gone. Pass the
   fresh-session test.
4. **Gates:** evidence-required `confirmed`, Stop hook, lints with fix
   text, PreToolUse blocks, the checker (7a, 7b, 7d, 5a, 5b), then the
   gotcha audit (5d).
5. **E2E bridge suite + post-release smoke + test hygiene** (8a, 8b, 10b,
   10c). Needs the game; this is `forest-tester` / `forest-researcher`
   work.
6. **Report reader + log catalogue + quality document** (9a, 9b, 10d).
7. **Stage A loop**, then **Stage B**, then (only if it earns it)
   **Stage C** (12), with the weekly cleanup loop (10e) and the monthly
   harness review (12d). Promote one stage at a time, and only after the
   previous one ran a week without a human fixing its output.

## Status log

- 2026-10-06: baseline written from the course index; nothing built yet.
- 2026-10-06: *Ground rule: no guessing* added (author).
- 2026-10-07: deeper pass over the whole course (lectures 1-14,
  templates, designs). Rescored 3 (Weak); added the course summary,
  failure attribution (1c), decisions file (2a), skills (3e), rule
  hygiene (3f), the baseline check (4a), fix text + gotcha promotion
  (5d), the template's state model mapped to ours (6), the separate
  checker and three-layer done (7d, 7e), contracts and rubric (9c, 9d),
  the quality document and cleanup loop (10d, 10e), loop stop conditions,
  silent costs and the monthly review (12), *Measuring the harness*, and
  four questions for the author.
- 2026-10-07: the author's four decisions recorded. Roadmap step 1 begun:
  `scripts/tasks.py` + `tasks/tasks.jsonl` + generated `docs/tasks.md`
  built with its gates (contract to start, WIP = 1 per worker, commit to
  build, evidence to confirm, a second worker's evidence on `checker`
  tasks, confirmed never reversed) and 24 tests, run in CI.
- 2026-10-07: checker widened to every behaviour change (author). Roadmap
  step 1 done: 112 tasks migrated from CLAUDE.md, backlog.md,
  investigations.md, website.md, knowledge/README.md and run-mode.md
  (T-0001, T-0017); CLAUDE.md 1,061 -> 878 lines; 7 questions parked.
  Next: step 2 (T-0002).
- 2026-10-07: roadmap step 2 built (T-0002): `scripts/session-start.py`
  (4a), the SessionStart hook on startup + /clear (4b), `scripts/cleanup.py`
  (10a), 9 tests. Author's calls: baseline from the CI badges when HEAD is
  origin/main and clean, else local build + tests (~17 s warm); cleanup
  deletes merged branches on origin too; scratch = what has no long-term
  use (session scratchpads, site-look-shots, .claude/shots older than 7
  days, __pycache__; site/aerial-out kept). First cleanup: 1 local + 4
  origin branches, 85 empty scratch folders. Next: step 3 (T-0003).
- 2026-10-07: roadmap step 3 built (T-0003): `CLAUDE.md` 887 -> 179
  lines, a router with 15 hard rules (source + when removable) and a doc
  map; `docs/decisions.md`; `docs/areas/` plugin, plugin-concepts, site,
  bot, release, workflow (each with its gotcha index); folder `CLAUDE.md`
  in `src/`, `tests/`, `site/`, `bot/`; website.md / knowledge-bot.md
  decision sections moved to decisions.md; agent briefs point at their
  area. Author's calls: new area docs per 3a (not the old docs), restore
  gotchas in plugin.md (savestates.md is bot-indexed), the "Recent
  releases" list and test counts dropped. Checked by a coverage script
  (every old unit in one file) and a fresh Sonnet checker (5 questions +
  4 lookups in one hop; its faults fixed: moved links, the gotcha index
  map, rule 10's e2e wording). Next: step 3e (T-0004, skills).
- 2026-10-07: roadmap step 4, gates built (T-0005): `scripts/lint.py`
  (versions, CHANGELOG section, csproj Remove lines, UI heuristics against
  `scripts/lint-baseline.txt` - 30 hits on day one) in CI and git hooks
  (`.githooks/` pre-commit, pre-push tag check); PreToolUse hook
  (`scripts/hooks/pre_tool.py`: refuses api.github.com and a forced push
  to main, asks before a deploy into the author's install, warns on
  Get-Content | Set-Content); Stop hook (`scripts/hooks/stop.py`, blocks
  once). 32 tests. Author's calls: baseline the heuristics, the
  log-prefix lint waits for 9a, the deploy asks (an unattended loop must
  refuse instead). Left of step 4: the gotcha audit (5d).
- 2026-10-07: roadmap step 4, the checker built (T-0006, 7d + 9d):
  `.claude/agents/forest-checker.md` (Sonnet, read + Bash), `tasks.py
  brief` (contract + diff + suites) and `review` (rubric checked, revise
  back to the maker, block parks a question), gates at `released` /
  `confirmed`, a Stop-hook line for unpushed unreviewed commits; 43 task
  + hook tests. Author's calls: review at built before the push; a plugin
  accept gates, the game confirms; re-running the suites left to Claude
  (yes). First real run: T-0027 accepted, 909 tests re-run, 2 minor
  notes, 37 s / ~65k tokens. Left of step 4: the gotcha audit (T-0009).
- 2026-10-07: roadmap step 4 done with the gotcha audit (T-0009, 5d):
  every one of the 95 index lines in `docs/areas/*.md` ends with
  `[check: <name>]`, `[check: T-n]` or `[judgement]` - 28 checkable (10
  with a check already: lint.py versions / removes / alloc / label20,
  pre-push, the Stop hook, ReleaseJsonTests, CrossingTests, the move
  detector's teleport tests, the site's other-build refusal; gotcha 16
  waits on the log catalogue, T-0012), 67 judgement (research method:
  read the IL, test live, look before claiming). 18 check tasks filed
  (T-0122..T-0139: 10 lints, 2 tests, 2 world-export assertions, 4 e2e
  cases blocked by T-0010). `lint.py` now fails on an unmarked line, a
  missing / repeated number and a `[check: T-n]` whose task is closed,
  so a new gotcha ships with its marker. Next (author's order): T-0012,
  the log catalogue, then step 5 (T-0010).
- 2026-10-07: 9a built (T-0012): `scripts/log-catalogue.py` writes
  `docs/log-lines.md` (659 log calls, 214 prefixes, a hand-written meaning
  each) and its `--check` joins `lint.py` as gotcha 16's check. The 50
  calls with no literal prefix were fixed: 21 got one at the call
  (`Update:` / `Update download:` / `Item catalogue:` / `Segments:` ...),
  29 built elsewhere declare theirs (`// log: Memory census`). Author's
  calls: Decisions 5. Next: step 5 (T-0010, needs the game).
- 2026-10-07: roadmap step 5 built (T-0010, 8a / 8b / 10b / 10c):
  `scripts/e2e.py` + six journeys in `tests/e2e/` (launch, restart,
  timed, restores, tabs, run mode; ~3 min; two clean runs in a row) and
  the release skill's post-release smoke (`--smoke --update --release`);
  the MCP server gained `--call <tool> <json>` so the suite reuses its
  launcher, updater and bridge code. Hygiene as code: slot digest,
  uploads / god mode / window, config values, git status, its own files,
  its site attempts and their Runs-tab lines; a crash folder aborts the
  run (the first full run hit one: T-0143). The gotcha checks T-0137 /
  0138 / 0139 pass `--by e2e`; T-0136's shots were looked at. Found on
  the way: docs/bridge.md still said a new game starts a run (not since
  v0.24.213), Slot 1 is a Creative save, a cut bush's kept copy is
  inactive (a `find all` sees it). Next: T-0013 (quality document), then
  Stage A (T-0015).
- 2026-10-07: roadmap step 6's last part built (T-0013, 10d):
  `docs/quality.md` grades 16 areas on verification, legibility, test
  stability and known gaps (rubric in the doc); first grading: seven at C
  (savestates, practice, performance and loads, TAS and trajectory, dev
  tools, site maps / 3D, bot), none at D, one A (site app). `lint.py`
  checks the table and the coverage, `session-start.py` reports stale
  rows; the simplification log for 12d is in it. Found on the way: five
  script test files CI never runs (T-0147), TAS has no doc (T-0145), dev
  tools have no tests (T-0146). Decisions 6. Next: Stage A (T-0015).
- 2026-10-07: Stage A built (T-0015, 12): `scripts/loop.py` (begin /
  next / peek / end / intervene / stop / report) over an append-only
  `tasks/loop.jsonl`; `next` reads the round's task state and names one
  action (contract, the area's agent, forest-checker, release / ship /
  evidence, end), and stops the run by machine check (5 rounds, an empty
  pool, 3 rounds without progress); a third checker revise parks the
  task. Skill `work-loop`; `tasks.py stats` gains the loop line;
  `test_loop.py` (18) in CI. Decisions 7. Not run on a real task yet:
  the first run is the next session's. Next: T-0014 (cleanup loop),
  then Stage B (T-0016) once Stage A ran a week without a fix.
- 2026-10-07: the weekly cleanup and the monthly harness review built
  (T-0014, 10e, 12d): `scripts/audit.py` + `audit-ignore.txt`
  (`test_audit.py`, 14, in CI), skills `weekly-cleanup` and
  `harness-review`, `tasks.py stats --since / --until`, session-start's
  cleanup and harness review lines, the *Cleanup log* in
  `docs/quality.md`. Decisions 8. First cleanup run the same day (its
  row in the log). Next: Stage B (T-0016) once Stage A ran a week
  without a fix; the first harness review is due now.
- 2026-10-07: first harness review (12d) started: the Stop hook's
  "commits not pushed" line off (no recorded catch; it set off gotcha
  97), counted over the 5 tasks finished from 2026-10-08 - the row in
  `docs/quality.md`'s *Simplification log*; the author left the pick to
  Claude.
