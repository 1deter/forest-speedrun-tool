---
name: weekly-cleanup
description: The weekly cleanup (docs/harness.md 10e, T-0014) - run scripts/audit.py (dead doc paths, stale lint-baseline entries, unused C# / Python code, orphan files), check every candidate by hand, file the real ones as tasks, record checked false positives in scripts/audit-ignore.txt, re-grade the docs/quality.md rows whose areas changed, and log the run. Use when the session-start report says "cleanup ... due", or when asked to run the cleanup / audit / look for dead code.
---

# The weekly cleanup

Why: stale docs, dead code and unchecked grades pile up quietly; a weekly
pass turns each into a small task so they are worked like everything else
(docs/harness.md 10e). It **files tasks and nothing more** - it never
deletes code or edits a doc itself (those are the tasks), except the
ignore list, the grades and the log below. Dead code here is "anything
redundant that isn't beneficial to the project long term and serves no
purpose / is no longer implemented or needed" (author, 2026-10-07).
Trigger: session-start's `cleanup: ... -> due` line (7 days after the
last *Cleanup log* row in `docs/quality.md`).

## Steps

1. **Run the finder** (read-only, ~2 s):
   ```bash
   python scripts/audit.py
   ```
   Kinds: `doc-path`, `baseline`, `cs-unused`, `py-unused`, `orphan`,
   `ignore` (an ignore line that matches nothing - delete it), `quality`
   (rows to re-grade, step 4). A `ValueError` names a bad ignore line.

2. **Check every candidate** before anything is filed - the finder is a
   word count, not a compiler. For each one, `git grep -nw <name>` and
   read the declaration:
   - **real** (nothing calls it, the path is gone, the file is not used
     by any workflow, script, page or doc) -> leave it for step 3;
   - **used after all** - by reflection or the bridge (a member the
     e2e suite, `docs/bridge.md` or a bridge `call` / `get` reads), an
     interface from outside the repo (BepInEx, Unity, ASP.NET), a file a
     tool loads by a built name, a link GitHub resolves (`../../releases`)
     - add a line to `scripts/audit-ignore.txt`:
     `kind<TAB>file<TAB>name<TAB>reason` (name `*` = the whole file).
     The reason says what uses it, so the next run does not ask again.
   - **unsure whether it is still wanted** (a dev helper nobody calls
     today, a half-built feature) -> it is still filed; the task's
     behaviour says "remove, or record why it stays", and its owner asks
     the author if the intent is not in the docs (rule 5).

3. **File the rest**, one P4 `needs: none` task per (kind, file), never a
   second while one with the same source (`audit:<kind>:<file>`) is open
   or wontfix:
   ```bash
   python scripts/audit.py --file
   ```
   Check the filed lines it prints (`tasks.py show T-n` for one).

4. **Re-grade the stale quality rows** (`quality` findings, also on
   session-start's quality line): for each area, read what changed since
   its *Reviewed* date (`git log --since=<date> --stat -- <its paths>`),
   then its row and section against the grade table at the top of
   `docs/quality.md`; update the four grades, the evidence lines, the
   tasks and *Reviewed* (today). A C / D row still needs an open task.

5. **Log the run** - one row at the top of the *Cleanup log* table in
   `docs/quality.md`:
   `| YYYY-MM-DD | <findings by kind> | T-n, T-m (or none) | <n> lines added | <areas re-graded or none> |`.
   Then `python scripts/lint.py`, commit (`weekly cleanup: ...`), push.
   Docs and tasks only: no release (skill `release` is for plugin code).

6. **Say in chat** what was found, filed, ignored and re-graded, in a few
   lines. The filed tasks are worked by later sessions or the loop; this
   session goes back to `tasks.py next`.
