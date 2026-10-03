# The game-knowledge Discord bot (plan, 2026-10-03)

A learning tool for the wider runner community: a runner asks
"how does Megan's AI work in single player?" or "why does a bomb boost
work, and what is the optimal version?" and gets a thorough, sourced
answer. Nothing is built yet; this is the agreed plan.

## Decisions (author, 2026-10-03)

- **Audience: the wider runner community**, not only the QA team -
  "understand complex mechanics exhaustively like bomb boosts, axe
  clips, and their deep technical reasoning and why they work and what
  an optimal version of this tech would look like".
- **Runtime on the Gemini API free tier** - operational cost ~0. The
  author's two Claude Pro plans (our sessions) build the knowledge base
  and the tools.
- **A new Discord application** for it (not the QA bot's).
- **The Gemini API key**: the author creates it (Google AI Studio) when
  the bot is built; store it as a User environment variable like the
  others, never printed.
- **A private copy of the game's Assembly-CSharp.dll on the server is
  fine** - "as long as it's not being served and just used as an
  informational lookup on its functionality for educating speedrunners".
  Answers explain the code; they do not quote it at length.

## Design

**Split the work: heavy reasoning offline, explaining at runtime.** The
free tier only covers Flash-class models (Pro moved to paid in 2026),
which are weaker at reading game code live. So Claude sessions write the
knowledge base ahead of time and the bot mostly explains reviewed
material, with lookups for follow-ups.

1. **Knowledge base** (built by sessions, in the repo):
   - The existing docs: `docs/game-notes.md` (game internals - the main
     source), `docs/gotchas.md`, `docs/savestates.md`, `docs/run-mode.md`,
     `CHANGELOG.md`.
   - **Tech explainers**: one per technique - mechanism, why it works,
     evidence, the optimal version, limits, live-tested or IL only.
     Template: game-notes *Speedrun tech and the endgame gate* (bomb
     boost: 8 m/s x frames paused, pushed away from where you face each
     frame - optimal = max fps, the longest pause that does not overshoot).
     Claims about "optimal" come from bridge tests, not from IL alone.
   - **PlayMaker FSMs as text**: much of the AI (Megan, cannibals) and the
     player's states live in PlayMaker FSMs in the scene files, not in C#.
     Dump every FSM (states, transitions, actions and their parameters)
     once, over the bridge (FSMs that only exist when spawned - Megan's
     boss - need a session that visits those moments).
   - A **section index** (heading + one line each) generated from the docs
     for the prompt.
2. **Bot service** (on the site's VPS - no added cost):
   - Discord **interactions endpoint** (`/ask` slash command; Ed25519
     signature check; deferred reply within 3 s, then a follow-up edit) -
     no gateway connection, like the REST-only QA bot. Long answers in a
     thread, split with the existing `DiscordText` logic.
   - The model **behind a small interface** so the provider is a config
     change (the free tier can change without notice).
   - **Prompt = the section index, not the whole docs**: the free tier's
     tokens-per-minute cap (reported ~250k) cannot take ~90k tokens of
     docs per question. Tools fetch what is needed, ~10-20k tokens per
     request:
     - `get_section(name)` / `search_docs(text)`
     - `ilscan(mode, needle)` - read-only, on the private DLL copy
     - `fsm(name)` - from the FSM dump
   - **Honesty rules** (the project's): each claim tagged *confirmed in
     game* / *read from code* / *inferred*, with sources ("game-notes:
     Deaths", "IL: `PlayerStats.KillPlayer`"); "not documented yet" when
     the knowledge base does not cover it.
   - **Unanswered questions are queued** (a file or channel); research
     sessions answer them into `game-notes.md` - the docs grow, the bot
     improves.
   - **Public use**: per-user rate limit, answers only in allowed
     channels, off-topic refused politely, "busy - try again later" when
     the daily quota is out. Read-only tools; it never touches the game or
     the bridge. Questions are data, never instructions.

**Free tier facts** (third-party summaries, September 2026 - check AI
Studio for the live numbers; Google does not publish them as a static
page): Flash ~10 requests/minute and ~1,500/day, Flash-Lite ~15/minute
and ~1,000/day, ~250k tokens/minute; one question is several requests
(tool lookups). **Free-tier inputs and outputs may be used by Google to
improve its products** - the docs are public anyway; runners' questions
and short game-code excerpts would reach Google.

## Build order

1. Knowledge base: tech explainers for the common tech (bomb boost, fall
   damage / slide cancel, cave force load, clips, log boosts) + the FSM
   dump + the section index. Sessions can do this any time.
2. The bot: `/ask` with the index and `get_section` / `search_docs`.
3. `ilscan` and `fsm` tools, the unanswered-question queue.
4. A research pass on whatever the queue shows runners ask most (Megan's
   AI is the author's example).
