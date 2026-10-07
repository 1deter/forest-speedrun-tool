# Rules for the knowledge bot (`bot/`)

Loaded by itself when work touches `bot/`. The area doc:
[`docs/areas/bot.md`](../docs/areas/bot.md) (commands, the queue, gotchas).

- **A push to `main` deploys the bot** (`bot/`, `knowledge/`, and the
  docs it reads: game-notes, savestates, run-mode, fsm). Agents never push.
- **Never invent a mechanic.** Sources in order: `docs/game-notes.md`, the
  decompiled game source (`%LOCALAPPDATA%\ForestOverlay\game-src\`,
  quoted freely - author), `tools/ILScan`, speedrun.com's API for rules.
  A runner report stays labelled as one; guesses only under "Possible
  causes (guesses, not tested)".
- `dotnet test bot/ForestBot.Tests` must pass (it lints `knowledge/`).
  Add an eval question (`knowledge/eval/questions.md`) for each fixed
  wrong answer.
- **Do not run the live bot's model to test answers** - it shares a small
  free quota (gotcha 94); check that search finds the right card instead
  (`forest-bot search`). The one exception: CI's small warn-only eval
  subset after each deploy (bot.yml, author 2026-10-07).
