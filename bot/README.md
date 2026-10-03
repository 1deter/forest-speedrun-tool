# forest-bot - the game-knowledge Discord bot

Runners ask about The Forest's mechanics and speedrun tech; the bot
researches the knowledge base (`knowledge/`), the game's internals notes, the
PlayMaker FSM exports and the game's decompiled code, then answers with
sources and an honest confidence. Design and decisions:
[`docs/knowledge-bot.md`](../docs/knowledge-bot.md).

## In Discord

- `/ask question:...`, a mention (`@Forest Knowledge how does ...`), or a DM.
- **Reply to an answer to follow up** - the earlier questions and answers of
  that thread are in context.
- 👍 / 👎 under every answer; 👎 asks what was wrong and queues it for
  research. Partial and "not documented yet" answers are queued too.
- Per-user limits (default 15 an hour, 60 a day) protect the free quota.

## How it answers

1. The system prompt carries the rules (research first, never guess, mark
   confidence, cite), the glossary and a one-line index of every card.
2. The model researches with tools: `search` (hybrid: SQLite FTS5 keyword +
   a local bge-small embedding model, merged by reciprocal rank),
   `read_card`, `read`, `fsm`, `code_search`, `code_read`.
3. It answers in Discord markdown and ends with `SOURCES:` / `STATUS:` lines,
   which the bot turns into a `-# Sources:` line and the queue decision.

Models are a list in order of preference; one out of quota rests and the
next answers; all resting = "busy, try again in N minutes".
`gemini:<model>` uses Gemini's API; `mistral:` / `groq:` / `cerebras:` /
`openrouter:` / `github:` / `openai:` use the OpenAI-compatible API (any
other: `name:<model>@<base url>`).

## Modes

```bash
forest-bot                     # the Discord bot
forest-bot check               # load everything, report knowledge problems
forest-bot search "<query>"    # what search finds (no model, no keys)
forest-bot code <Type.Member>  # the code tool's answer; code search <text>
forest-bot ask "<question>"    # one answer on the console, with its lookups
forest-bot chat                # a console conversation (follow-ups)
forest-bot eval [ids...]       # score knowledge/eval/questions.md (spends quota)
forest-bot queue               # the open research queue
forest-bot answer <id>         # a stored answer in full (the queue's "answer #n")
forest-bot resolve <id>        # close a queue item once it is dealt with
```

Locally: `dotnet run --project bot/ForestBot -c Release -- <mode> ...`; the
knowledge comes from the build output (`kb/`), the decompiled code from
`%LOCALAPPDATA%\ForestOverlay\game-src\Assembly-CSharp` by default.

## Settings (environment; on Windows User-scope variables are read too)

| Variable | Default | |
|---|---|---|
| `FOREST_BOT_DISCORD_TOKEN` | - | the bot's token (Discord developer portal) |
| `FOREST_BOT_MODELS` | `gemini:gemini-3.8-flash,gemini:gemini-3.5-flash-lite,mistral:mistral-medium-latest` | order = preference |
| `GEMINI_API_KEY`, `MISTRAL_API_KEY`, `<PROVIDER>_API_KEY` | - | a model without its key is skipped |
| `FOREST_BOT_THINKING` | model default | Gemini `thinkingLevel` (`low` / `high`) |
| `FOREST_BOT_CHANNELS` | all | channel ids it answers in (threads under them too) |
| `FOREST_BOT_DMS` | on | `off` to refuse DMs |
| `FOREST_BOT_QUEUE_CHANNEL` | - | a channel id for research-queue posts |
| `FOREST_BOT_PER_HOUR` / `_PER_DAY` | 15 / 60 | per-user limits |
| `FOREST_BOT_TEST_GUILD` | - | register `/ask` in one server instantly (global takes up to an hour) |
| `FOREST_BOT_KNOWLEDGE` | `<exe>/kb` | a folder with `knowledge/` + `docs/` |
| `FOREST_BOT_CODE` | the author's local copy | the decompiled Assembly-CSharp folder |
| `FOREST_BOT_DATA` | `<exe>/data` | `bot.db`, `vectors.db`, eval reports |
| `FOREST_BOT_EMBED_MODEL` | `<data>/embed` | `model.onnx` + `vocab.txt` (bge-small-en-v1.5); missing = keyword search only |

## Tests

`dotnet test bot/ForestBot.Tests` - the parsers, search, the code index,
both providers' request / response shapes, the agent loop with a scripted
model (tools, step limit, quota fallback, busy), the store, and a lint over
the real `knowledge/` (every card loads, has a summary and aliases; the
glossary and the test questions name real cards).

Deploying: [`deploy/README.md`](deploy/README.md).
