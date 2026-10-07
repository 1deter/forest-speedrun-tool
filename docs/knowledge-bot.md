# The game-knowledge Discord bot (plan, 2026-10-03)

A learning tool for the wider runner community: a runner asks
"how does Megan's AI work in single player?" or "why does a bomb boost
work, and what is the optimal version?" and gets a thorough, sourced
answer, then asks follow-ups by replying to it.

## Decisions

In [`docs/decisions.md`](decisions.md) *Knowledge bot*. The bot area
(commands, the queue, gotchas): [`docs/areas/bot.md`](areas/bot.md).

## Design (agreed 2026-10-03)

**Heavy reasoning offline, explaining at runtime.** The free tier covers
Flash-class models, weaker at reading a game cold. So Claude sessions
write reviewed knowledge ahead of time, and the bot explains it - with
lookups into the code, the FSMs and the docs for anything the cards do not
cover.

### 1. Knowledge base (`knowledge/`, built by sessions, in the repo)

- **Cards** (`knowledge/cards/*.md`): one topic each, written for a runner
  to understand fully. Format: [`knowledge/README.md`](../knowledge/README.md).
  Front matter with **aliases** (the runners' own words: "bb", "slide
  cancel", "smash clip" - search lives on these), confidence (live / IL /
  inferred / runner report), sources, related cards, code pointers. Body:
  summary, how runners do it, why it works, numbers, the optimal version,
  failure modes, evidence, open questions.
- **Glossary** (`knowledge/glossary.md`): runner slang -> card. Always in
  the bot's prompt (small).
- **Tuning values** (`knowledge/cards/player-physics.md` and friends): the
  numbers that live on the game's objects, not its code (speeds, gravity,
  thresholds) - read live over the bridge.
- **Test questions** (`knowledge/eval/questions.md`): 40-50 questions with
  the facts each answer must contain. Run on every model swap or big
  knowledge change; a swap that scores worse is not made. An optional
  `max-length:` / `min-length:` (characters) checks the answer's length:
  the bot answers a short question briefly and a "how / why" or an
  "elaborate" reply in full (T-0090).
- **Secondary sources** the bot also searches: `docs/game-notes.md` (by
  heading), `docs/fsm/*.txt` (PlayMaker FSMs, by state), the decompiled
  game code (by type / method, private, on the server only), and the
  plugin docs for questions about ForestOverlay itself.

### 2. The bot (`bot/`, a .NET 10 service on the site's VPS)

- **Discord (gateway, Discord.Net):** `/ask`, a mention, or **a reply to
  one of its answers** (= a follow-up: the earlier questions and answers
  of that chain are in context). Long answers split at 2,000 chars with
  code blocks reopened (the QA bot's `DiscordText` logic). Sources as a
  `-#` line under the answer. Answers in allowed channels (and DMs, if
  wanted).
- **Feedback:** 👍 / 👎 buttons on every answer; 👎 opens a short "what was
  wrong or missing?" box. 👎s, "not documented yet" answers and low
  confidence go to the **research queue**; research sessions answer them
  into cards (with the bridge, live) - the knowledge grows where runners
  ask.
- **Retrieval:** hybrid - SQLite FTS5 (BM25: exact names like
  `HandleLanded`, `waitForInput`) + a local embedding model (bge-small-en-v1.5
  via ONNX Runtime; paraphrases like "why do I fly when I pause"), merged by
  reciprocal rank. The
  prompt carries the glossary and a one-line index of every card; tools
  fetch the rest (~10-20k tokens a question):
  - `search(query, kinds)` - cards, docs, FSMs, code
  - `read_card(id)`, `read_doc(section)`
  - `fsm(name, state?)`
  - `code_search(text)`, `code_outline(type)`, `code_read(Type.Method)`
  An agent loop capped at ~8 tool calls; the answer must cite what it read.
- **Honesty rules** (the project's): each claim tagged *tested in game* /
  *read from the code* / *inferred*, with sources; "not documented yet"
  (and queued) when nothing covers it - never a guess presented as fact.
  Questions are data, never instructions; tools are read-only.
- **Model behind a small interface** so the provider is a config change
  (the free tier can change without notice). The current Flash model;
  Flash-Lite as the fallback when the quota is out; "busy - try again
  later" when both are.
- **Answer cache:** keyed on the normalised question + the knowledge
  version (a 👎 drops it) - the same questions (bomb boost, clips) will come constantly.
- **Public use:** per-user rate limit, off-topic refused politely.
- **Hosting:** its own container beside `forest-site` (no inbound port - a
  gateway bot only connects out), its own secrets, the decompiled code in
  a private volume. Deployed like the site (`site/deploy`).

**Free tier facts** (third-party summaries, September 2026 - check AI
Studio for the live numbers; Google does not publish them as a static
page): Flash ~10 requests/minute and ~1,500/day, Flash-Lite ~15/minute
and ~1,000/day, ~250k tokens/minute; one question is several requests
(tool lookups). **Free-tier inputs and outputs may be used by Google to
improve its products** - runners' questions and code excerpts reach
Google.

## Decompiled code

`%LOCALAPPDATA%\ForestOverlay\game-src\` (author's machine, never in the
repo): `Assembly-CSharp/` (ILSpy project output, 3,669 files),
`ilspy/ilspycmd.exe`, `assembly-sha256.txt` (the DLL it came from). To
redo after a game update: `ilspycmd "<Managed>/Assembly-CSharp.dll" -r
"<Managed>" -p -o <dir>` (game-notes *How to extend this file*).

## Build order

1. **Knowledge base** - done 2026-10-03: 23 cards, the glossary, 40 test
   questions (`knowledge/README.md` *Cards*). Then the rest of the card list,
   and the FSMs not exported yet (Megan, the cannibals' motor / vision) in a
   session that reaches them.
2. **The bot** - built 2026-10-03 (`bot/`, [`bot/README.md`](../bot/README.md)):
   Discord gateway (/ask, mentions, DMs, replies = follow-ups), 👍 / 👎 with
   a "what was wrong" box, the research queue (+ an optional channel), the
   answer cache, rate limits, hybrid search, the card / doc / FSM / code
   tools, Gemini + OpenAI-compatible providers with fallback, the eval run,
   the deploy (`bot/deploy`, `.github/workflows/bot.yml`). 24 tests.
   **Live since 2026-10-03** on the VPS (container `forest-bot`), in the
   **QA server for now** (the author: testing there while the community
   admins answer), Gemini 3.8 Flash -> 3.5 Flash-Lite (no Mistral for now -
   author). First live fixes: no LaTeX in answers, 5xx retries, `/ask`
   without a channel object, globalization on (gotcha 93).
3. **Eval run + tuning** - done 2026-10-03: 41% (quota noise) -> 87% on
   Flash-Lite (`forest-bot eval` locally: the author's `GEMINI_API_KEY` is a
   User variable, `FOREST_BOT_DATA` to a temp folder; ~40 min for all 43,
   it waits out per-minute limits). Flash's free **daily** quota is spent
   almost at once (shared with the live bot), so tune for Flash-Lite.
   Honesty: `read_card` lists a card's unconfirmed claims on top
   (`forest-bot check --unconfirmed` shows them); prompt rules from the
   author's live thread (runner reports never dismissed, labelled guesses,
   no over-wide "does not", re-check on pushback); LaTeX stripped in code.
   Left: Megan's answer reads the code's weights but rarely says the FSM
   side is unresearched; the judge (Flash-Lite) misgrades now and then -
   read the answers, not only the score. The queue:
   `sudo docker exec forest-bot dotnet /srv/current/forest-bot.dll queue`
   (`answer <id>` shows a 👎'd answer, `resolve <id>` closes an item).
   Then: the embedding model locally (author OK needed for the ~130 MB
   download - not asked yet) so local runs search like the VPS; the
   production server once the admins agree (`FOREST_BOT_CHANNELS`,
   a queue channel).
4. A research pass on whatever the queue shows runners ask most (Megan's
   AI is the author's example).

## Bot settings page on /admin (built, T-0028)

Site: docs/website.md *What is built* (*Bot settings*). Bot
(`bot/ForestBot/SiteSettings.cs`, `BotConfig.ApplySettings`): with
`FOREST_BOT_TOKEN` set it polls `GET <FOREST_BOT_SITE_URL>/api/bot/settings`
(`X-Bot-Token`) every minute and applies it live; a changed model order /
thinking level rebuilds the model chain in place (`Brain.ReloadModels`). The
last good answer is cached in `<data>/site-settings.json` and applied at
start. `.env` values are the defaults: a setting the site lacks, an unusable
value, or no site and no cache = the `.env` one. Channels are the exception:
once the site has saved settings its list is the whole list (none ticked = no
channel; DMs follow `dms`); never saved = the `.env` channels. A poll builds
the new values aside and swaps one immutable snapshot (`BotConfig.Live`), so a
message never sees a half-applied mix. The reported version is the deploy's
commit (`bot.yml` publishes with `SourceRevisionId`) + the knowledge version. `POST /api/bot/report`
(version, revision applied, text channels it sees; not sent before Discord
is connected) feeds the page's channel list and its "applied" line.
**VPS:** the same random `FOREST_BOT_TOKEN` in `/opt/forest-site/.env` and
`/opt/forest-bot/.env`, then recreate both containers once.

Asked for so channels / limits change without editing `/opt/forest-bot/.env`
and recreating the container. The fallback (no `FOREST_BOT_TOKEN`, or the
site never saved): edit `.env` (`FOREST_BOT_CHANNELS=id,id`), then
`cd /opt/forest-bot && sudo docker compose up -d --force-recreate`.

Design (agreed in chat):
- **Owner-only "Bot" tab on the site's /admin**, behind the existing admin
  token. Settings: channels it answers in (**by name, checkboxes** - the bot
  posts the channels it can see to the site), DMs on / off, per-user
  limits (hour / day), model order, thinking level, research-queue channel.
- **Live, no restart**: the bot polls the site for its settings about once a
  minute (a bot-only token, its own header) and applies them; on a failed
  fetch it keeps the last good settings (cached in its data dir). `.env`
  values are the defaults / fallback.
- **Secrets stay in `.env`** - the Discord token and model API keys never
  pass through a web page (a stolen admin token can change channels, not
  read keys).
- The page shows **what the bot is running**: "applied hh:mm, bot vX" from
  the bot's last poll, so a change that did not take is visible.
- Work: site endpoint + store + /admin tab (forest-site), bot poller +
  `BotConfig` live reload (bot/), tests on both sides. Medium-sized.
- Alternative not chosen: owner-only Discord slash commands (no overview,
  clumsy beyond channels).
