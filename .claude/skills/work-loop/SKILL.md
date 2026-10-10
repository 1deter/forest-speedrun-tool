---
name: work-loop
description: Run the assisted loop (docs/harness.md 12, Stage A) - the main session orchestrates up to 5 rounds back to back, one task each from tasks.py next, each worked by a fresh subagent for its area, checked by forest-checker, evidenced, and closed with a one-paragraph summary; scripts/loop.py decides every next step from the task file and stops the run by machine checks. Use when the author says "run the loop", "work through the list", "loop", or starts a session to take several tasks.
---

# The assisted loop

`scripts/loop.py` is the goal runner: it reads the round's task state in
`tasks/tasks.jsonl` and names the **one** next action; you do it and ask
again. Never move a task by your own say-so - the gates in `tasks.py` and
`loop.py` do. The why and the author's calls: docs/harness.md 12 and
*Decisions* 7; the working rules: docs/areas/workflow.md *The loop*.

## Steps

1. **Begin** (after the session-start skill): `python scripts/loop.py begin`
   (`--bridge` when the game is up and the bridge answers). It lists the
   parked questions: ask the author them now, record each with
   `tasks.py set T-n --answer "..."` - an answer can free a task for this run.
2. **Repeat** `python scripts/loop.py next` and do what it says, until it
   prints `STOP` (exit 3). Each action, in short:
   - **contract** - look first (git log, the code, docs/confirmed.md: a
     migrated task can already be done), then `tasks.py start T-n --by main`
     with behavior / scope / verify / keep. An open choice is a question,
     not a default: park it (`--question ... --needs author-decision`) and
     end the round; the author can answer at the next run's begin.
   - **do the work** - spawn the agent it names (`run_in_background: false`
     when nothing else can go on), told only `T-n: the contract is
     tasks.py show T-n`. Tiny docs / harness edits: do them yourself. Merge
     its branch (`merge-keepboth.py` for add/add), then
     `tasks.py set T-n --status built --commit <sha> --by main`.
   - **check** - forest-checker with only `Check T-n`. revise: the faults
     go back to the maker (SendMessage to the same agent when it is still
     there); after the 2nd revise the next one parks the task by itself.
   - **release / ship / evidence** - skill `release`, skill `deploy-watch`,
     or one `tasks.py evidence` per verify step, then `--status confirmed`.
   - **end** - `python scripts/loop.py end --summary "..."`: one paragraph
     the author can skim - what changed, how it was proved, what is left.
     Say the same paragraph in chat.
3. **When the author steps in** (fixes output, corrects a call, answers
   mid-round): `python scripts/loop.py intervene "what they did"` - it is
   the measure of the loop (docs/harness.md *Measuring the harness*).
4. **At STOP**: `python scripts/loop.py report` into chat, then the
   handoff (docs current, router *Where we are*, quality rows of the areas
   the run touched), commit and push. The run ends the session's work:
   name the next run, don't start one. `loop.py` also stops a run once
   this session's context passes 300k (200k until 2026-10-10), and `begin` refuses a new run then
   (T-0249: one night session grew to 541k, 15 % of four days' usage) -
   a night of runs is one fresh session per run, never one long session.

## Boundaries

- One task per round, WIP = 1 (`--by main`); the round's work goes to a
  subagent so this session's context grows by summaries, not by work
  (author, 2026-10-07). At most 2-3 agents at once; one drives the game.
- To end a run early: finish or park the open round, then
  `loop.py stop --reason "..."`.
- Plugin rounds still release one at a time (rule 3) and need in-game
  evidence to be confirmed; with no game, a released round counts as
  progress and the in-game check waits.
