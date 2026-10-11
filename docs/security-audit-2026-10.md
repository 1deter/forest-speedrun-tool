# Security audit of forest.deter.cloud, 2026-10-11 (T-0084)

The full pre-1.0 audit of the site's code (`site/`, the `src/Data` files it
links, `site/deploy`, `.github/workflows/site.yml`). It starts from the
two earlier reviews in [`docs/website.md`](website.md) *Security* (2026-10-01
and 2026-10-04) and re-checks everything, including what was added since
(official runs T-0223, the PB embeds and runner-spot posts T-0232 /
T-0285..T-0288, `/api/admin/pbposts`, the attempt page's audit log and
replay path, the code look-up).

**How:** code reading only, on a cloud session (cloud branch
`cloud/site-security`) - the live site was never probed. Each fix was tested
in the site's own suite (`dotnet test site/ForestSite.Tests`, 125 tests,
Linux). `dotnet list site/ForestSite package --vulnerable
--include-transitive`: no vulnerable package.

Severity: **High** (an attacker gets data or control they should not),
**Medium** (integrity of what others see, or a cheap way to load the
server), **Low** (needs an account and effort, or a small impact), **Info**
(hardening, nothing exploitable found).

## Summary

| # | Severity | Finding | State |
|---|---|---|---|
| 1 | Medium | `/api/official` replays every finished log again once the judged-log cache is full | Fixed 8b257aa9 |
| 2 | Medium | Any runner can make their copy of another runner's spot the one the site serves | **Open - Q1** |
| 3 | Low-Medium | An attempt view replays its log on every cache miss; the cache clears whole when full | **Open - Q5** |
| 4 | Low | A code look-up replays the whole log on every request | Fixed a3768341, e27079df |
| 5 | Low | Two first uploads of one attempt's log at once: both written | Fixed 72e854b8 |
| 6 | Low | `GET /api/categories/<id>/<v>` answers draft and hidden categories | Fixed 85445b79 |
| 7 | Low | An attempt page shows the rules of any category version its log names, drafts included | **Open - Q3** |
| 8 | Low | A refused map upload with `?clear=1` had already emptied the map | Fixed 66b87f89 |
| 9 | Low | Unicode format characters (bidi overrides, zero-width) pass into names | **Open - Q2** |
| 10 | Low | No cap on attempts per upload or runs per runner on a spot | **Open - Q4** |
| 11 | Low | `GET /api/runs/<id>` parses the `.run` (up to 4 MB) on every request | Open (recommendation) |
| 12 | Low | CI installs Playwright unpinned in the job that later holds the deploy key | Open (recommendation) |
| 13 | Info | Admin answers (tokens, the webhook URL) were cacheable; `Server: Kestrel` | Fixed cacde0bc |
| 14 | Info | Runner ids took upper case: `r-00AB..` a second runner beside `r-00ab..` | Fixed 524a0e40 |
| 15 | Info | Rate limits key on `CF-Connecting-IP` - honest only with the origin lock on | Open (as designed) |
| 16 | Info | Two starts of one attempt id at once: the second is a 500 | Open (harmless) |
| 17 | Info | Owner-only map upload: 100 MB buffered, no unpacked-size cap | Open (owner only) |
| 18 | Info | speedrun.com sync reads its answer with no size cap | Open (fixed host) |
| 19 | Info | Decided earlier, still standing: reversible runner ids, the token-reset race, the container image | Open (author's decisions) |

## Fixed on `cloud/site-security`

### 1. Medium - `/api/official` replay amplification

- **Where:** `site/ForestSite/Attempts.cs` `Official()` (`Accepted`), `JudgedLog`
  (the cache clears whole past `MaxCachedLog`, 64 MB of log text).
- **Exploit sketch:** once a category is published, a registered runner
  uploads ~500 finished logs of 4 MB each in it (inside the 2 GB / day cap),
  each with a report that makes it red (another mod listed). Red runs are
  never "accepted", so every recompute of the list asks every one of their
  rows for its verdict. The views' cache holds 64 MB, so most of those
  verdicts are replays. The list is kept only until a log arrives: post one
  small log, `GET /api/official`, repeat - each request is ~2 GB of SHA-256
  replay on the VPS. A few requests a minute pin the CPU.
- **Fix:** the verdict alone is kept per attempt (`_verdicts`, a few bytes
  each) beside the views' cache and valid under the same rule (same log,
  same allow-list, the category version found or still missing); a list
  recompute replays only logs it has not judged. Test
  `Official_KeepsEachVerdict_EvenWhenTheViewsCacheHoldsNothing` (fails
  before: 7 replays where 4 are needed).
- **Not live yet:** no category is published, so `/api/official` answers
  `[]` without reading a log today.

### 4. Low - code look-up replays per request

- **Where:** `Attempts.FindCode` (`GET /api/attempts/<id>/code/<code>`).
- **Exploit sketch:** any visitor, 600 requests a minute per address, on an
  attempt with a 4 MB log: each one replays the whole log.
- **Fix:** the judged log keeps the steps by code (`Judged.Codes`); a look-up
  reads it (step, real and timer ms only, e27079df). Test `FindCode_ReadsTheJudgedLog_NotAReplayPerLookUp`.

### 5. Low - concurrent first log uploads

- **Where:** `Attempts.Log`.
- **Exploit sketch:** the attempt's own runner sends two different logs for
  one attempt at once. Both see no log in; both are judged and written - the
  file can be one log and the stored receipt verdict the other's ("logs are
  never replaced" broken), or one request fails with a 500 (UNIQUE / file
  in use).
- **Fix:** a lock striped by attempt id around the check and the write.
  Test `Log_TwoFirstUploadsAtOnce_OneIsKept_TheOtherRefused` (fails before
  with the 500s).

### 6. Low - draft category versions readable

- **Where:** `Program.cs` `GET /api/categories/{id}/{version}` -> `Categories.Version`.
- **Exploit sketch:** `GET /api/categories/manhunt/1` (the preset ids are
  in the source) returns a draft's full rules and settings, though the
  official list is careful never to name a draft. Nothing in the site or the
  plugin calls this endpoint.
- **Fix:** `Categories.PublishedVersion` - any version of a category that is
  published now; a draft or hidden one is a 404. Test in
  `CategoryTests.Api_AdminSaves_PluginReadsPublished`. See #7 for the
  other way a draft's rules can be read.

### 8. Low - map upload cleared before it was checked (owner only)

- **Where:** `Program.cs` `Upload` (`/api/admin/aerial`, `/api/admin/world`).
- **Exploit sketch:** not an attacker's - the owner's own mistake: a wrong
  file, a truncated chunk or one bad name with `?clear=1` emptied the live
  map; a bad name half-way left the files before it written.
- **Fix:** the zip is opened and every name checked before the folder is
  cleared or a file written. Test in `AerialTilesUploadByTheOwnerOnly`.

### 13. Info - admin answers cacheable, `Server` header

- **Where:** the `admin` group's filter; `ConfigureKestrel`.
- **Sketch:** `/api/admin/pbposts` holds the runners' spots' webhook URL,
  `POST /api/admin/admins` a new admin's token, `/api/admin/runners` the
  runner list - with no `Cache-Control`, a shared machine's browser cache
  keeps them. Kestrel named itself in `Server`.
- **Fix:** `Cache-Control: no-store` on every admin answer, refused ones
  included; `AddServerHeader = false`. Test in
  `SecurityHeaders_OnPagesApiAndFiles` (the test host has no Kestrel, so
  the `Server` header is not covered by a test).

### 14. Info - upper-case runner ids

- **Where:** `Program.cs` `RunnerIds.Valid`.
- **Sketch:** `r-00000000000000AA` registered as a new runner beside
  `r-00000000000000aa` - every comparison is ordinal, so nothing is taken
  over, but the admin page and `board.txt` show two "same" ids.
- **Fix:** lower-case hex only (the plugin writes lower case; attempt ids
  already refuse upper case). Existing tokens are untouched. Test in
  `Register_OwnsTheId`.

## Open - for the author (questions in `tasks/notes/T-0084.md`)

### 2. Medium - a copy of another runner's spot becomes the spot

- **Where:** `site/ForestSite/Runs.cs:98-115` (a copy takes the owner's
  labels but keeps the uploader's zones), `Runs.cs:546` `Newer` (the current
  route is the one run most recently), `Runs.cs:389` `SpotFoseg` (serves the
  current route).
- **Exploit sketch:** runner B downloads runner A's spot, moves its end zone
  next to the start, finishes it once and the plugin uploads. The site
  records B's route as a copy - under A's name, category and description,
  owned by A - and since it holds the newest run it becomes the spot's
  current route: the spot page draws B's zones, and every plugin that adds
  or updates the spot from "Website spots" gets B's zones as A's spot. B can
  repeat it after every run of A's. (The 2026-10-01 review stopped copies
  from renaming a spot; this is the zones.)
- **Fix options (changes what runners see - Q1):** the current route is the
  owner's newest route (a community route as now); copies stay listed as
  other routes on the spot page but are never current and never served by
  `/foseg`. Or: a copy becomes a spot of its own (a new id) instead of a
  route of A's.

### 3. Low-Medium - attempt view replay on a cache miss

- **Where:** `Attempts.View` -> `JudgedLog`; `Attempts.cs:450` clears the
  whole cache when the next judgement would pass 64 MB.
- **Exploit sketch:** a runner uploads ~20 logs of 4 MB (well inside the
  daily cap) and requests their attempt pages in turn: the cache can never
  hold them all, so each `GET /api/attempts/<id>` (and `/attempt/<id>`, which
  also builds the preview) replays and judges 4 MB. At the read limit's 600
  a minute per address that is about a core per address.
- **Fix (scope - Q5):** logs never change, so the judged view can be written
  once beside the log (`<id>.view.json.gz`) and served from disk, rebuilt
  only when the allow-list or the category version changes; or at least an
  LRU in place of the clear-all and a lower read limit on these two routes.

### 7. Low - draft rules through an attempt

- **Where:** `Attempts.CategoryOf` / `View` (`rules = j?.Category`).
- **Exploit sketch:** a registered runner uploads an offline log whose report
  holds the line `category = manhunt v1`; the attempt page
  shows that draft's rules. The judgement also uses the draft (that may be
  wanted: moderators trying a category before publishing).
- **Question (Q3):** should an attempt naming a draft be judged and shown
  against it (as now), or only against published versions?

### 9. Low - format characters in names

- **Where:** `src/Data/AttemptFormat.cs:330` `Clean` (strips `|` and control
  characters below U+0020 only); runner names (`/api/register`, each run's
  name), spot names and categories.
- **Exploit sketch:** a runner named `deter` + U+200B, or with U+202E
  (right-to-left override) reversing the rest of a line, looks like another
  runner on the board, in Discord PB posts and in the plugin's "Website
  spots" / comparisons. Names are free text and not unique, so this is
  display spoofing only - every check uses the runner id.
- **Fix (proposed, Q2):** the site drops Unicode format characters (category
  Cf: U+200B..U+200F, U+202A..U+202E, U+2060..U+2069, U+FEFF) from names when
  it stores them. Runner-facing: a name with an emoji joiner (U+200D) would
  lose it.

### 10. Low - attempts per upload, runs per spot

- **Where:** `Runs.Upload` (`b.Attempts`, no count cap); `Runs.Spot` and the
  spot page read every run row of a spot.
- **Exploit sketch:** one 4 MB upload can hold a few hundred tiny attempts;
  600 uploads an hour per address is ~100k runs an hour on one spot, each a
  row the spot page and `/api/spots` then read on every view.
- **Fix (Q4):** a cap per upload (the plugin sends what it queued - how many
  can a real queue hold?), and / or per runner per route per day.

### 11. Low - run view parse per request (recommendation)

- **Where:** `Runs.Run` (`GET /api/runs/<id>`, `?all=1`).
- **Sketch:** every request reads and parses the run's text (up to 4 MB) -
  600 a minute per address. Runs never change (hidden / deleted aside), so
  a small LRU of the built JSON (or an ETag + 304) removes the work.

### 12. Low - CI supply chain (recommendation)

- **Where:** `.github/workflows/site.yml:48` `pip install playwright` (no
  version), then `playwright install --with-deps chromium`, in the same job
  whose later step has `DEPLOY_KEY`.
- **Sketch:** a compromised Playwright release on PyPI runs on the runner
  before the deploy step and can leave something behind for it (a changed
  `ssh`, the tarball). The deploy key can only run `deploy.sh`, which
  installs whatever tarball it gets - i.e. the site.
- **Fix:** pin `playwright==<version>` (with `--require-hashes` if a lock is
  kept), or move the browser smoke to its own job with no secrets and make
  the deploy job `needs:` it. Actions are pinned by tag, not SHA (noted
  2026-10-04).

### 15. Info - `CF-Connecting-IP`

`Program.cs:65` keys every rate limit on `CF-Connecting-IP`, which a client
can set when it reaches the VPS directly. The origin lock
(`FOREST_ORIGIN_SECRET`, on since 2026-10-01) makes that impossible; if the
secret is ever unset, the rate limits are per made-up header. Keep the
secret; a startup warning when it is unset would make that visible.

### 16-18. Info

- **16:** `Attempts.Start` - two starts of one new attempt id at once: the
  second INSERT fails (UNIQUE) and answers 500. Only the runner's own id;
  the plugin retries and gets the nonce.
- **17:** `Program.cs:573` - an owner map upload is buffered whole (up to 100
  MB) and entries are written without an unpacked-size cap (a zip bomb by
  the owner fills `/var/lib/forest-site`). Owner-only.
- **18:** `Categories.cs:355` - the speedrun.com answer is read whole with no
  size cap; fixed `https://www.speedrun.com` URL, 30 s timeout.

### 19. Decided earlier, still standing

From docs/website.md *Security*: runner ids reverse to Steam accounts
(decided: left); after an admin's *Reset token* the next registration of
that id wins - note this also lets a **named admin** reset a runner and
register as them (logged in Activity); no disk quota per runner beyond the
rate limits; the container's runtime (`aspnet:10.0`) updates only on a
`docker compose pull` (the monthly step in docs/website.md - not checkable
from the repo).

## Checked and fine

- **Auth:** every write endpoint checks a token: runner endpoints
  (`/runs`, `/submissions`, `/attempts...`, `DELETE /spots/<id>`) the
  Bearer token (SHA-256 stored, 192-bit random); every `/api/admin/*` route
  sits in the `admin` group whose filter checks the owner token
  (constant-time) or a named admin's; owner-only routes (`/admins`, `/bot`,
  `/pbposts`, map uploads, deleting attempts) check `IsOwner`; the bot's two
  routes check `X-Bot-Token` (constant-time, refused when unset). An empty
  token matches nothing (`AdminOf`, `RunnerOf` refuse empty).
- **SQL:** every query is parameterised, including the new ones (official
  runs, categories, PB / bot settings); the string-built parts add
  constants only (`Routes`' WHERE, `PRAGMA table_info(<const>)`,
  `RunColumns`).
- **Paths:** run files by `SafeName(segment id)` + numeric run id (ids 1-80
  chars, no separators survive); start states by a hash; attempt logs only
  after `IsAttemptId` (`a-` + 16 lower-case hex) on every route that reaches
  a file (`View`/`LogText` take ids that were validated on write, or a DB
  row); submissions by numeric id; map uploads by `UploadPath`'s anchored
  regexes; `/aerial`, `/world` through `PhysicalFileProvider` (no `..`).
- **Upload parsing:** bodies capped at 4 MB by Kestrel (100 MB for the
  owner's map upload only); the linked parsers (`SegmentBundle`,
  `AttemptFormat`, `SegmentFormat`, `SavestateFile`, `AttemptChain`,
  `RunReport`, `RunCategory`) are line-based and linear, allocate by line
  length, use no regex on input, and never recurse; a parse error is a 400.
- **XSS:** no `innerHTML`, `outerHTML`, `insertAdjacentHTML`,
  `document.write`, `eval` or `new Function` anywhere in `wwwroot` (vendor
  aside); every page builds through `el()` (`textContent` /
  `setAttribute`, `on*` only from code); every `href` built from data is a
  site path with `encodeURIComponent` (the route is the server's own
  fingerprint); `/compare` takes YouTube ids by `YT_ID` and only answers
  messages from `YT_ORIGIN`. Server-side HTML (`Pages.WithMeta`) is
  HTML-encoded and uses evaluators (2026-10-04 fix). Text answers
  (`.run`, logs, `.foseg`, board / spots text) are `text/plain` with
  `nosniff`.
- **CSRF:** no cookies anywhere; every privileged request carries a token
  in a header (`Authorization`, `X-Admin-Token`, `X-Bot-Token`); no CORS is
  configured, so another origin cannot send those headers. `/api/register`
  is open by design and needs a JSON content type.
- **Headers:** CSP `script-src 'self'` with no `unsafe-*`, one frame
  (`youtube-nocookie.com`), `object-src 'none'`, `base-uri 'none'`,
  `frame-ancestors 'none'`; `nosniff`, `X-Frame-Options DENY`,
  `Referrer-Policy`, COOP, `Permissions-Policy`, HSTS - on every answer.
- **Discord webhooks (SSRF):** the official URL is config only; the
  runners' spots URL must match a `discord.com` / `discordapp.com` webhook
  regex (`PbSettings`), set by the owner only; no runner input reaches a
  URL the site fetches (the run link is the site's own address with
  escaped ids). Posts carry `allowed_mentions: none`; runner text is
  escaped (markdown, `<`, `:` - so a name cannot become a link Discord
  unfurls) and clipped; 30 posts an hour, 5 per runner, a bounded queue.
- **Secrets in logs:** startup says only on / off for the admin token and
  the webhooks; the senders log their name, never the URL; the Activity log
  keeps the method + path + query, never a body (`PUT /pbposts`, `PUT /bot`);
  exception messages logged are `HttpClient`'s (host, not URL). The plugin
  logs a refused token's status, never the token.
- **Rate limits:** every API route has a policy (read 600 / min, register
  10 / h, upload 600 / h, attempt 3000 / h, admin 120 / min, bot 60 / min),
  keyed on the address (#15); per-runner caps on top for attempts (5,000 /
  day, 2 GB of logs), PB posts and open submissions (20).
- **Deploy:** the deploy key's forced command runs `deploy.sh` only (a
  tarball capped at 300 MB; GNU `tar` strips leading `/` and skips `..` members); the
  container runs as uid 1654, read-only, no capabilities, no new
  privileges, memory and pid caps, on a network shared with Caddy only;
  `site.yml` has `permissions: contents: read`; pull requests never deploy.
