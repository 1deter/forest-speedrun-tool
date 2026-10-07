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
release, site, VPS, tasks.

## Gates (machine checks, docs/harness.md 5a, 5b, 7b)

Every failure says WHAT / WHY / FIX; follow the FIX line.
- **`scripts/lint.py`** - the csproj version = `Plugin.PluginVersion` = a
  `CHANGELOG.md` section; every top-level folder with C# in the csproj's
  three `Remove` lines (gotcha 92); a fixed 20 px `GUI.Label` with
  variable text and allocations in `OnGUI` / `DrawTab` bodies (heuristics:
  the hits that existed on 2026-10-07 sit in `scripts/lint-baseline.txt`,
  only new ones fail; `--update-baseline` accepts a false positive or drops
  fixed ones). Runs in CI, before every commit (`.githooks/pre-commit`) and
  before a `v*` tag is pushed (`.githooks/pre-push`: the tag's commit
  carries that version). `session-start.py` turns the git hooks on
  (`core.hooksPath .githooks`). The community index: `CommunityPacksTests`.
  The log-prefix lint waits for the log catalogue (9a, author 2026-10-07).
- **PreToolUse** (`scripts/hooks/pre_tool.py`, Bash / PowerShell /
  WebFetch): refuses `api.github.com` fetches and a forced push to main;
  **asks** before a deploy into the author's install (`deploy.ps1` with
  no / the `FOREST_ROOT` game root, or a copy into its `BepInEx/plugins`
  - author 2026-10-07: "just ask me"; a test install passes; bridge tests
  use `update_game` and never hit it); warns on `Get-Content | Set-Content`.
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
never their maker (author, 2026-10-07). New requests become tasks
(`tasks.py add`), with their detail in backlog.md when it is long; the
author's feature list (*Next up* until 2026-10-07) is P2-P4 there.
Multi-session notes: `tasks.py note T-n "..."` (`tasks/notes/`).

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
update the router's *Where we are* and the task file, move confirmed items, add any lesson as a
gotcha (`docs/gotchas.md` + its area doc's index line). The author may switch session at any moment;
the docs on `main` must always be ready for it. With sessions running side
by side, `qa_read new_only` is shared - a message one session reads
is gone from the other's new list (tell the author what belongs where).

**Subagents (author, 2026-10-04/05: maximise usage).** Project agents in
`.claude/agents/`, each with its model, effort, a trimmed tool list and a
short brief naming the docs to read and the report to return:
`forest-dev` (Sonnet: plugin features / fixes whose game side is known),
`forest-researcher` (Opus, high: game internals, live research, hard
restore / physics / render bugs), `forest-tester` (Sonnet: in-game checks
over the bridge, writes docs/confirmed.md), `forest-site` (Sonnet: site/),
`forest-knowledge` (Sonnet: bot cards + the 👎 queue), `forest-qa` (Haiku:
the QA Discord). Run 2-3 at a time (5+ Opus agents emptied a 5-hour window
in under 15 minutes), one driving the game at a time; spawn with
`isolation: worktree` for code (worktrees in `.claude/worktrees/`, outside
the compile globs) and give the task in a few lines - the agent file holds
the rules. The main session merges (`scripts/merge-keepboth.py` for
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
  offer to carry on with it in this one.
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

One line each, numbered as in [`docs/gotchas.md`](../gotchas.md) (full story, version and fix - read the entry before working near it). A new lesson gets the next number there and its one line here, in the area it belongs to.

8. **Test against real payloads** - a trimmed real response, not a remembered one.
9. **Never round-trip text through PowerShell 5.1** - it garbles UTF-8 (`â€”`).
19. **Multi-line edits go through a script file** - Write a Python helper, raw strings; no long heredocs.
28. **Compare both sides the same way** - same dedup and filters before pairing lists.
