---
name: harness-review
description: The monthly harness review (docs/harness.md 12d, T-0014) - pick one harness component (a lint check, a hook check, a rule, a skill step, an agent brief line), switch it off for the next 5 finished tasks, compare tasks.py stats before and after, and give the author a keep / remove recommendation; logged in docs/quality.md's Simplification log. Use when the session-start report says "harness review ... due" or "compare and decide", or when asked to review / simplify the harness.
---

# The monthly harness review

Why: a harness only grows unless something takes parts away; a component
stays only if switching it off made results worse (docs/harness.md 12d,
L2 / L12). Rule hygiene (3f) rides along: a contradictory or dead rule is
named for deletion at the same review. Trigger: session-start's
`harness review: ...` line - `due` (30 days after the last row of the
*Simplification log* in `docs/quality.md`) starts one; `compare and
decide` (5 tasks finished while the component was off) ends one.

**Never switched off: the safety guards** - rule 2 (no hand deploy) and
the PreToolUse deploy ask, rule 4 (no `api.github.com`) and its refusal,
rule 13 (uploads off for tests), rule 15 (save backups), and the forced
push to main refusal. They guard rare, costly events that five tasks of
stats cannot measure (Claude's call, the author left it open,
2026-10-07). Everything else can be picked.

## Start a review (`due`)

1. **Pick one component** whose value is least proven: no failure it
   caught in `docs/gotchas.md` / the checker's faults / `tasks.py stats`
   failures by layer, or that sessions keep working around. One at a
   time, small enough to switch back in one edit (a single lint check, a
   Stop-hook line, one skill step, one brief paragraph, one router rule -
   not "the checker").
2. **Ask the author** (rule 5: a harness change is their call) - say
   which component, why it is the candidate, how it is switched off and
   back. Unattended: park the question (`tasks.py add ... --needs
   author-decision`) and stop here.
3. **Switch it off** the smallest way that is undone in one edit (a
   `return []` with a `# harness review <date>` comment, a skill line
   moved into an HTML comment, a rule marked *(off for the harness
   review since <date>)*), run `python scripts/lint.py` and the suite it
   touches, commit `harness review: <component> off`, push.
4. **Log it** - a row at the bottom of the *Simplification log*:
   `| YYYY-MM-DD | <component> | <how to switch it back: file + edit> | - | open |`.
   Start it at the beginning of a session: the window counts tasks
   finished from that day (whole days, `tasks.py stats --since`).

While it is open, session-start prints `n/5 tasks finished`; work goes on
normally. A task that hit trouble the component would have caught: note
it on the task (`tasks.py note`) - it is the strongest evidence.

## End a review (`compare and decide`)

5. **Compare** - the same number of finished tasks before and after:
   ```bash
   python scripts/tasks.py stats --since <start> --until <today>
   python scripts/tasks.py stats --since <a date giving ~5 tasks> --until <the day before start>
   ```
   Read: first-check pass rate (checker), reviews per task, failures by
   layer, loop interventions and rounds without progress; plus the notes
   from the window. Five tasks is a small sample: say how sure the
   numbers are.
6. **Recommend** to the author: *remove* (nothing got worse - delete the
   component and its docs, one home per fact), *keep* (something got
   worse, or a note shows it would have caught a fault - switch it back),
   or *change* (keep a smaller form). **The author decides** (2026-10-07).
7. **Do it and log it**: switch back or delete, lint + suites, fill the
   row's *Outcome* (the numbers, before -> after) and *Decision* (`keep`,
   `removed`, `changed: ...`, with "(author)"), update the component's
   docs and the router if a rule went, commit, push. Docs-only changes
   need no release; a change to plugin code goes through skill `release`.
