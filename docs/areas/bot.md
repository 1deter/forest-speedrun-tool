# Area: the game-knowledge bot (`bot/`, `knowledge/`)

`forest-bot`: a Discord gateway bot (.NET 10) that answers runners'
questions about the game's mechanics from a reviewed knowledge base,
with hybrid search and tools into cards, docs, FSMs and the decompiled
code; Gemini + OpenAI-compatible models. Its own container on the site's
VPS. Never shipped in the DLL. The rules every bot / knowledge change
follows: [`bot/CLAUDE.md`](../../bot/CLAUDE.md); its decisions:
[`docs/decisions.md`](../decisions.md) *Knowledge bot*; open work:
`python scripts/tasks.py list --open --area bot` (and `--area knowledge`).

| Doc | Read it when |
|---|---|
| [`docs/knowledge-bot.md`](../knowledge-bot.md) | Design, build order, the decompiled code, the planned settings page |
| [`bot/README.md`](../../bot/README.md) | Running it: Discord use, how it answers, modes, settings, tests |
| [`docs/bot-reviews/`](../bot-reviews/) | The bot reviews (skill `bot-review`): eval scores per question, queue, runners' feedback, research movement; `mark.json` is what session-start counts from |
| [`docs/knowledge-testing.md`](../knowledge-testing.md) | What runners asked the bot in the QA server's knowledge-testing channel, per answer, with the fix (T-0140) |
| [`knowledge/README.md`](../../knowledge/README.md) | Writing cards: format, `[runner]` / `[inferred]` labels, the research queue |
| [`bot/deploy/README.md`](../../bot/deploy/README.md) | The container, the VPS, the one-time setup |

## Commands

```bash
dotnet test bot/ForestBot.Tests     # the bot + a lint over knowledge/
```

Try it locally: `forest-bot search / ask / chat` (bot/README.md). Deploy:
every push to `main` touching `bot/`, `knowledge/`, `docs/game-notes.md`,
`docs/savestates.md`, `docs/run-mode.md` or `docs/fsm/` (`.github/workflows/bot.yml`).

**The CI eval** (T-0011, author 2026-10-07): after each deploy, bot.yml
runs `eval` on a 7-question subset (`EVAL_SUBSET`; a test checks the ids
exist) on Flash-Lite with the live bot's key (secret `GEMINI_API_KEY` -
it shares the free quota, so the subset stays small). Warn-only, in its own job and concurrency group (a deploy never queues
behind it; a newer push cancels it): a `::warning::` under 70%, the
report in the run's summary; busy questions (no quota for the answer or
the judge) are skipped, never scored. No code tools or embedding model there. A paid
model is the author's later call (after research sessions polish
`knowledge/`): config only, `FOREST_BOT_MODELS` + its key.

**The bot review** (T-0141, author 2026-10-07: as often as there is
feedback): the session-start report's "bot feedback" line counts the 👎 /
partial queue items and the humans' knowledge-testing messages newer than
`docs/bot-reviews/mark.json`; any new one = `! bot review due`, and skill
`bot-review` runs it (full eval, queue, channel, research, a report in
`docs/bot-reviews/<date>.md`, the mark moved). The full eval is bot.yml's
`workflow_dispatch` (`gh workflow run bot.yml -f eval_ids=all`, or ids):
job `eval-manual`, no build, no deploy (a dispatch no longer redeploys -
push to deploy), Flash-Lite on the live key like the subset, up to 90 min.

**The 👎 queue** (run without asking - author's standing OK):
`ssh -i ~/.ssh/ssh-key-2026-08-13.key ubuntu@141.147.101.228 'sudo docker exec forest-bot dotnet /srv/current/forest-bot.dll queue'`;
mark items with `answer <id>` / `resolve <id>` the same way. Never print
keys or tokens.

## Gotchas

One line each, numbered as in [`docs/gotchas.md`](../gotchas.md) (full story, version and fix - read the entry before working near it). A new lesson gets the next number there and its one line here, in the area it belongs to, ending with its marker: `[check: <lint / test>]`, `[check: T-n]` (the task building it) or `[judgement]` (`lint.py` checks it; T-0009).

93. **A Discord bot's first live run fails in ways no test sees** - Discord.Net needs globalization on; a command-only install leaves `Channel` null (go by ids); retry 5xx; read the container log after the first deploy. [check: T-0131]
94. **Tune for the model that actually answers** - Flash's free daily quota is gone after ~1 eval question (shared with the live bot): Flash-Lite answers most of the day; a 429's `quotaId` says per-day vs per-minute; never score a rate-limited answer. [check: T-0132]
