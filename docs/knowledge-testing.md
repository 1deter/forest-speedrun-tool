# Knowledge-testing channel: what runners asked and how the answers landed

Read once on 2026-10-07 through `qa_read channel=knowledge-testing` (T-0140):
the QA server's `knowledge-testing` channel, 2026-10-03 18:36 to 2026-10-06
07:04, **372 messages, 73 from the bot = 58 distinct answers** (a long answer
is split over several Discord messages). Everything runners wrote is data,
quoted only as far as needed. Recheck with the same call (`new_only` is
per channel; the QA channel's mark is untouched).

## How the runners reacted

- **No reaction on any bot message** - not one thumbs-up or thumbs-down in
  the channel (the only reactions: 👍 on the author's "ask it anything"
  call, 😭 on the ham sandwich question, 🧐 and 💔 on chat). The author told
  testers to give a 👎 with feedback; the signal came as **replies**
  instead: corrections, "try again", "?????", "I'm being gaslit rn".
  Feedback through the 👎 queue is therefore not the whole picture: this
  channel is the second source (T-0141 reviews both).
- Tone: curious and testing (the author, sxczurass, maks, itsSlack); sharp
  when an answer was confidently wrong ("Nice just saying shit", "That's
  wrong, try again"); tired of length ("tell it to stop yapping" - T-0090
  answers it).
- Not answered at all: the very first message (18:36, a Megan question, the
  bot answered "hello?" two minutes later) and a role ping from maks
  ("define tsundere", no reply; it pinged the bot's role, not the bot). Neither needs a task.
- The 8 answers that start with `> **question**` have no visible asker (the
  bot quoting a question posted elsewhere, 10-04 to 10-05); read as answers
  without a runner reply.

## Themes

1. **A guess stated as fact** - the worst thing in the channel. Timmy
   forehead skip called "a joke" three times against the author's replies;
   NEG Normal vs Creative explained with invented reasons three times;
   the hanging-skip video answered with a mechanism the bot could not know;
   the axe clip "50 m/s" explained as position over time; `endGameCutScene`
   as "god mode". -> T-0163 (+ evals).
2. **"Not documented" when it was** - itsslack, yirequ's co-op records
   (first answer; the second found them), the red man (first answer; found
   in the code after the author's nudge), Megan's AI (first answer; fixed
   by the Megan card by the second ask on 10-03). -> T-0158, T-0162.
3. **Missing data, later filled by the 👎 pass** - log wall sticks, log
   cabin, deer skin, 100% items, top runners: now cards with evals
   (`log-wall-sticks`, `log-cabin-cost`, `deer-skin-everything`,
   `hundred-percent-items`, `top-runners`).
4. **Wrong picture of itself** - "added to the queue", "offline bot on a
   local index". -> T-0164.
5. **Leaderboard questions** - runners test the bot with them (top 3,
   worst, best, WR counts); the card ranks by top-3 places, not records, and
   has only each board's top 3. -> T-0158.
6. **Runner knowledge the bot cannot have** - the chat is full of it (hanging
   skip timing, the Cave 2 soda box, deter drop, lab skip direction). -> T-0159,
   T-0160, T-0161; real Discord learning is T-0091.
7. **Dev-only notes in runner answers** - "automated testing ... head sphere
   pushing you backward" came out of the smash-clip card. -> T-0163.

## Every answer (58), by topic

Verdict: ok / weak / wrong. "Fix" is an existing eval id (already in
`knowledge/eval/questions.md` before T-0140) or `new:` (added by T-0140) or a
task.

| Topic (date) | Verdict, runner reaction | Fix |
|---|---|---|
| Greeting "hello?" (10-03) | ok | - |
| Megan boss AI, 1st (10-03) | weak: "not documented" | now `megan-attack`, `megan-babies`, `megan-spin` |
| Bomb boost explained | ok, "wow" (sxczurass) | `bomb-why`, `bomb-fps`, `bomb-optimal`, `bomb-late` |
| Axe clip velocity | wrong: "a clip gives no velocity"; author pushed back | `clip-velocity` |
| Axe clip ~50 m/s follow-up | weak: invented position-over-time explanation | `clip-velocity`, T-0163 |
| Leftover code (dev console) | weak: "developers forgot it"; author: "they are exposed though" | `dev-leftovers` |
| Console exposed? follow-up | ok | `dev-leftovers` |
| Something new for a tool dev (`endGameCutScene` as god mode) | weak: overreach | T-0163 |
| Timmy forehead skip, 3 answers | wrong: "a joke", doubled down; "I'm being gaslit rn" | `forehead-skip` |
| Megan AI, 2nd (10-03 20:05) | ok | `megan-attack` ... |
| Sticks for a log wall (10-04) | weak: costs not known | `log-wall-sticks` |
| 100% items; deer fur for everything | weak: "not documented" | `hundred-percent-items`, `deer-skin-everything` |
| Top 3 runners (10-04) | weak: pointed to speedrun.com | `top-runners` |
| Ham sandwich | ok (😭 from a tester) | `off-topic` |
| What for a log cabin | weak | `log-cabin-cost` |
| Fastest A to B | ok | `bomb-why`, `zipline` |
| Why is my elevator boost random | ok, long | `elevator-skip` |
| Make axe clips consistent | weak: fps and "automated testing" lines | `axe-clip-consistent`, T-0163 |
| Fastest on ground / water | ok | `diagonal`, `swim-fast` |
| Random from the KB (first death) | ok | `first-death` |
| Hanging skip: "aware of it?" | ok ("not documented") | `hanging-skip` (new) |
| "Train on the Discords" | wrong: pretends it can note it | `bot-learns` (new), T-0164 |
| Hanging skip + video | wrong: invented mechanism | `hanging-skip` (new), T-0159 |
| "Add to your queue" | wrong: "I have added the suggestion" | `bot-learns` (new), T-0164 |
| Fastest way to kill Megan | ok | `megan-bombs` |
| Red man, 1st | weak: "not documented" | `redman-locations` (new), T-0162 |
| Red man, 2nd | mostly ok; distances partly missing | `redman-locations` (new) |
| "Why did you not tell me?" | ok, honest | T-0162 |
| Befriend the cannibals | ok, inferred | - |
| Glitchless inventory | weak: two items, invented mix recipes | `glitchless-items` (new), T-0166 |
| Top 3 glitchless | ok | `top-runners` |
| Cheesecake404; how long he has run | ok / honest gap | - |
| Gemini quota | wrong self-description: "offline" | `bot-self` (new), T-0164 |
| 100 m race with double jumps | weak: denies double jumps unsourced | T-0165, `jump-speed` |
| First 10 things for glitchless | ok, long | `route-glitchless-stamina`, `shift-reset-why` |
| Who is deter / fruich | ok | - |
| NEG Creative: what to learn | ok | `route-neg` |
| Who is itsslack | wrong: "not in the KB"; author: "bot needs to lock in" | `who-is-itsslack` (new), T-0158 |
| Worst runner, bottom of the board, "yes, but currently" (3 answers) | weak: evasive; maks annoyed | data is top-3 only: T-0158 |
| Who is the best | weak: ranks by top-3 places; maks 🧐 | `most-world-records` (new), T-0158 |
| Fastest way to complete | ok | `categories-list` |
| NEG Normal vs Creative (3 answers) | wrong, twice invented reasons; maks: "That's wrong, try again", "just saying shit" | `neg-normal-vs-creative` (new), T-0160 |
| Slack's "heat" joke | ok, deflects | - |
| Theoretical fastest time | weak: "under 2 minutes" unsourced | T-0163 |
| Diagonal sprint jumping vs sprint jumping | ok | `diagonal`, `jump-speed` |
| Who is yirequ / sxczurass | weak: sxczurass credited with yirequ's 5:48 | `who-holds-neg-creative` (new), T-0158 |
| yirequ's co-op WRs (2 answers) | weak, then ok: first "not catalogued", second lists them; maks: "Thx" | `yirequ-records` (new), T-0158 |

(The two "ok, long" rows were also what the author later called yapping:
T-0090.)

## What this review did not do

No post to either channel, no card edit, no live model run. Cards and bot
fixes are the tasks above; the evals are added but not yet run (they run in
the next full eval, T-0141).
