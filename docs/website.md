# forest.deter.cloud - brief for the website session

Next up 10, started next (author, 2026-09-27: "get everything done and
start working on the site - i'm pretty excited to see what can be done").
Built by Claude (author, 2026-09-25: "do whatever's easiest"). This file is
the starting point; decisions made while building go here too.

## What it is for

- **Shared runs and a web viewer** (runner request): everyone's runs of a
  spot against yours, in the style of Momentum Mod - lines, ghosts, splits.
- **Leaderboards are comparative, not competitive** (CLAUDE.md
  *Conventions*): lines and ghosts, no verified ranking, so no anti-cheat.
  Deleting a run and flagging a time faster than the golds is enough.
- Later: 3D terrain from the heightmap, caves (need a geometry dump), a
  scrub bar, annotations.

## Decided with the author (2026-09-25 / 27)

- **Spots stay curated in the repo, runs go to the site.** Community
  spots (`community/*.foseg` + `index.txt`) are approved by the author for
  now; later runner-managed and hands-off. The site should be able to
  take a *spot submission* (a `.foseg`) for the author to approve, since
  runners will not open pull requests.
- **Run uploads:** off until the site is live, then **automatic** (author:
  "once the site is live i don't see a reason not to submit the runs
  automatically"). The plugin then fetches each runner's PB for the
  current spot as split comparisons (the plugin side: attempts from other
  runners kept apart from the runner's own, per-runner PB - not built yet;
  `SplitTable` takes any `float[]` comparison already).
- **Identity:** runners are keyed on `RunnerId`, shown by their newest
  `RunnerName`. The id is `r-` + 16 hex digits of SHA-256 over
  `forestoverlay-runner|steam:<steamid64>` (`Game/RunnerIdentity`) - a
  plain Steam id would link to the profile from public files. A rename
  must never split one runner in two: show the name from their newest run.
  Non-Steam copies carry a random `r-` id from the config.
- **Only the same route compares:** key runs on segment id + route
  fingerprint (`Segment.RouteFingerprint`). A spot edited after publishing
  gets a new fingerprint; old runs show under "older version", never mixed.
- The site can serve the community index as a second URL
  (`Community.Url` in the plugin config, `Modules/CommunityModule`).

## The formats (all text, all already written by the plugin)

- **`.foseg`** - one segment: `Data/SegmentBundle` (header, `[segment]`,
  optional `[startstate]`, `[attempt]` sections). Tested round trip.
- **`[segment]` block** - `Data/SegmentFormat`: id, name, category, spawn,
  start / check / end triggers (`Data/Segments.cs`, `TriggerParser`),
  `split = <name>` after a check / end line (v0.24.146), restore,
  startstate hash, notes.
- **`.run` (an attempt)** - `Data/AttemptFormat` (pure, tested): `anchor`,
  `recorded`, `duration`, `route`, `splits|t1|t2...` (checkpoint times,
  cumulative, ms; the end is `duration`), `runner|<id>|<name>`,
  `channels|...`, `s|t|x|y|z|speed` (30 Hz path), `v|t|...` (5 Hz state).
  Files before v0.24.146 have no `splits` / `runner` lines.
- **Splits maths** - `Data/SplitTable` (PB, golds, sum of best, average,
  deltas, LiveSplit colours). The site should reproduce the same numbers;
  porting this file (or its tests) keeps both sides honest.
- Coordinates are Unity world metres (y up); the surface map spans about
  -1750..1750 on x / z; caves are below the terrain (y < 0 in places).

## Decided at the start of the build (2026-09-27)

- **Hosting (author):** the author's Oracle free-tier VPS (aarch64, Ubuntu
  minimal), Cloudflare in front (SSL Full), Caddy in Docker (`~/website`,
  network `caddy-navidrome`) as for deter.cloud and music.deter.cloud.
- **Stack (Claude, author: "up to you"):** ASP.NET Core (.NET 10) + SQLite
  + plain HTML / JS, one process, in the stock `aspnet:10.0` container on
  Caddy's network (`reverse_proxy forest-site:8080`, no host port). The
  server **links the plugin's own pure files** (`AttemptFormat`,
  `SplitTable`, `SegmentFormat`, `SegmentBundle`, with the tests' Unity
  shim) - one parser and one splits maths for game and site.
- **Repo (author: `site/` is fine):** `site/` in this repo.
- **Deploys (author asked for automatic):** `.github/workflows/site.yml` -
  test, publish linux-arm64, tarball over SSH to `site/deploy/deploy.sh`
  (a forced command: the key can run nothing else), container restart,
  live check. One-time setup: `site/deploy/README.md`.
- **Uploads:** a token per install (`POST /api/register`, first
  registration owns the runner id; the author can reset a token). Rate
  limits per IP (`CF-Connecting-IP`) and token, 4 MB bodies. A time under
  0.8 x the route's best (3+ runs) is flagged "under review".
- **First version (author agreed):** browse spots, upload / view runs per
  spot and route, per-runner best with splits, a 2D map with a scrub bar.
  3D terrain after.
- **Look (author):** minimalist, intuitive, The Forest's loading screen:
  its progress bar's yellow `rgb(229, 197, 1)` on black, `#222` backing,
  Montserrat (read from `HUD_Ngui/LoadCam` over the bridge). The logo's
  yellow is the same (229, 197, 0, from the author's cover art); its look
  is echoed with Anton (heavy condensed, uppercase) for the wordmark and
  titles - **not the logo image itself**, plus a "not affiliated with
  Endnight" footer (Claude's advice, 2026-09-27).

## What is built (2026-09-27, live at https://forest.deter.cloud)

- `site/ForestSite`: `Store` (SQLite + gzip run files), `Runs` (upload
  rules, community packs from `community/` shipped with the site, the
  JSON), `Program` (endpoints, rate limits, admin). Pages in `wwwroot`:
  spot list with search, a spot (map: zones, lines, ghost dots, play /
  scrub / speed; runners board with map toggles; splits vs #1 or best
  segments, LiveSplit colours; .run download; older route versions as
  chips), about. Phone width checked.
- `site/ForestSite.Tests`: 16 end-to-end tests (register, upload, board,
  golds, duplicates, refusals, moved zone = new route, flag, community,
  submissions + replace + refusals, page routes, admin tokens / log, spot
  delete). `dotnet test site/ForestSite.Tests` - stop a running
  `forest-site` preview first (it locks `ForestSite.exe`).
- Checked locally with the author's real `s-splitstest01` attempts: all 6
  accepted (route fingerprints match the plugin's).
- API: `GET /api/spots`, `/api/spots/{id}`, `/api/spots/{id}/{route}/runner/{runner}`,
  `/api/runs/{id}` (path `[t,x,y,z,speed]`), `/api/runs/{id}/file`,
  `/api/spots/{id}/{route}/board.txt` (each runner's best as text for the
  plugin, `Data/SiteBoard`; runs under review left out);
  `POST /api/register` `{runner, name}` -> `{token}`, `POST /api/runs`
  (a `.foseg` with `[attempt]`s, Bearer token), `POST /api/submissions`
  (-> `{id, replaced}`); `/api/admin/...` with `X-Admin-Token` (own rate
  limit, 120 / min): `check` (`{name, owner}`), `submissions[/{id}[/{status}]]`,
  `flagged`, `runs/{id}/hide|show|unflag`, `DELETE runs/{id}`, `runners`,
  `runners/{id}/ban|unban|reset-token`, `DELETE spots/{id}`, `log`, and
  the owner's `admins` (GET / POST a name -> `{token}` / `DELETE {id}`).
  Every non-GET admin call is logged (`admin_log`) by the auth filter.
- **Clean paths** (author, 2026-09-27: the `#/` "doesn't look clean"):
  `/spot/<id>[/<route>]`, `/about`, `/admin[/<tab>]` - the server answers
  each with the page (mapped by name: the fallback skips paths with a
  dot, and old ids have dots); links move by `history.pushState`; an old
  `#/...` link is rewritten on load (plugins before v0.24.158 open those).
- **The admin page** (`/admin`, not in the nav; `wwwroot/admin.js`): the
  admin token typed once, kept in that browser's localStorage. Tabs:
  Submissions (the `[segment]` block, start state size, Download as
  `<slug>.foseg`, Approve / Reject / Back to open; after Approve the three
  publish steps), Under review (flagged runs: Looks fine = unflag, Hide /
  Show, Delete with a second click), Spots (Delete a runner's spot with
  all its runs - an accidental upload; community spots are refused, and a
  deleted spot returns if its owner uploads on it again), Runners (runs,
  last upload, Ban, Reset token with a second click), Activity (every
  change: who, what, the answer's status - `admin_log`).
- **Admins (author, 2026-09-27: not one shared key):** the env token
  (`FOREST_ADMIN_TOKEN`, in `/opt/forest-site/.env` on the VPS) is the
  **owner**; the owner's Admins tab makes a named `fa_...` token per admin
  (shown once, only its hash kept - `admins`), and revokes it. Admins can
  do everything except manage admins. Locally the `forest-site` preview's
  owner token is `local-admin`.

**The local preview versions its assets at startup** (`app.js?v=<hash>`,
computed once): after editing `wwwroot`, restart the `forest-site`
preview or the page keeps the old script and CSS (a deploy restarts, so
live is fine). Test runs for the local site: post a `.foseg` with a
registered local runner (`/api/register`, then `/api/runs` with the
token); an attempt's `runner|` line must be that runner's, or it is
refused as "another runner's attempt".

**Testing the plugin against a local site** (preview `forest-site`,
`http://localhost:5080`, owner token `local-admin` from
`.claude/launch.json`): `set ..._modules[16]._url.Value
http://localhost:5080`, and back to `https://forest.deter.cloud` after.
The author's config holds a **live token** now: leave it in place (the
local site answers 401 - enough to test a request path; a session may
not copy the token aside) or, to test a full registration locally, clear
it and have the author **Reset token** for their runner on the live
`/admin` afterwards, so the plugin registers again live. A local test
that sets `_tokenBad` (401) needs `set ..._modules[16]._tokenBad false`.
Pressing Submit over the bridge: `call ..._modules[9].SubmitSelected`
(after `QuickSaveSpot`, which selects the new spot; remove it after per
*Removing test spots*).

The plugin side is done (v0.24.153-154, `Modules/RunUploadModule`):
each finished timed run is queued on disk and uploaded, on by default;
the Runs tab's Website section has the switch, the state, "Upload this
spot's saved runs" and "Open on the website".

## How spots get onto the site (2026-09-27)

- **A runner's own spot** appears by itself under "Runners' spots" with
  its first uploaded run (the upload carries the segment).
- **The spot's owner** is the runner who first uploaded a run on it
  (`routes.owner`, 2026-09-27): shown as "by <name>", and only their
  later uploads change its name, category and description (maks: a
  rename in game did not reach the site). A rename shows with the
  owner's next upload (a finished run, or *upload saved runs*).
- **Community spots** (the curated list every plugin fetches): the author
  approves. Today: Practice -> select -> Share -> Export writes
  `config/ForestOverlay/shared/<name>.foseg`; copy it into `community/`
  (an old entry with a `spot.my...` id gets a fresh `s-` id first -
  `community/README.md`), run `python scripts/community-index.py`, commit
  and push - CI checks the index, the site redeploys with it, plugins pick
  it up on their next startup check. Remove `demo-template.foseg` when the
  first real pack goes in.
- **Submissions (v0.24.158):** Practice -> Share -> *Submit to community*
  sends the saved entry + start state, never attempts
  (`RunUploadModule.Submit`; `SiteProtocol.SubmitRefusal`, shared with the
  site: an old-style id must be Duplicated first). A runner's second
  submit of a spot still open replaces it. The author approves on
  `/admin`; publishing is still the commit to `community/` above (the repo
  stays the source of truth) - maybe the GitHub API later.

## The photo map (2026-09-27 / 28)

Top-down photos of the whole island, the map's Photo / Ground layers,
with a Water switch.

1. **Capture** (game, bridge, ~25 min): `call BepInEx_Manager
   OverlayPlugin._host._modules[8].AerialStart -1750 -1742.631 1750
   1757.369 218.75 4 4 320` (x0 z0 x1 z1 tile settle rangeScale sunTime);
   read `_aerial.Status`. Writes `config/ForestOverlay/aerial/`
   `{canopy,ground}/<ix>_<iz>.jpg` + `tiles.txt`. Every tile, sea
   included, at one exposure (v0.24.178: `_aerial.ExposureEv`, -3.5 -
   game-notes *Eye adaptation*). The game's ocean never shows in these
   frames (game-notes *The ocean from above*). A test capture of one tile
   overwrites `tiles.txt` - recapture before the next bake. **The weather
   changed the light mid-run** on v0.24.178 (an overcast sky: rows 1.6x
   darker from the moment it rolled in, twice); v0.24.179 holds it clear
   (the start line says `weather held clear`, the progress lines show it).
   The bake still prints `WARNING: capture row N ...` for a step. **The
   player's state is in the frame too** (the camera is the game's): since
   v0.24.180 the player is kept fed / watered / rested per tile and the
   camera's blood / frost / grey overlays are held off; lakes switch to a
   black stand-in by `LOD_GroupToggle`'s own distances, scaled by
   rangeScale too; the far plane reaches y -320 (the sinkhole's floor).
   A full run took 17.5 min on 2026-10-01 (v0.24.180). **The south
   edge's mountains** (models past the terrain, up to y ~1100) had the
   camera, 400 m over the terrain (y ~650), inside them: their tops cut
   away by the near plane while still casting shadows on the snow (the
   author's "overlook's shadow", 2026-10-02). Since v0.24.221 a tile's
   camera clears the tallest renderer over it too (log: `camera raised to
   y ...`, 8 renderers, the south rows); a test of tiles 8_0 / 9_0 drew
   the mountains whole. **Not yet recaptured**: that game was at quality
   0 (Fastest) in a 1366x768 window - every tile ~3-15x darker, an
   untouched control too - so check the launcher's quality / resolution
   (1200 px high before) and one tile against the old one before a full
   run. Retake
   rows N+ with `AerialStart -1750 <z0 + N*218.75> 1750 1757.369 ...` -
   its files are numbered from row 0 again and it overwrites `tiles.txt`,
   so back the folder up first, rename the new `<ix>_<k>` to `<ix>_<k+N>`
   and put the full `tiles.txt` back (done this way on 2026-10-01).
2. **Bake**: `python scripts/aerial-bake.py` -> `site/aerial-out/` (not in
   git; pyramid L0-6, 256 px; `canopy` / `ground` get the sea drawn from
   the terrain's heights - open water joined to the map's edge, shaded by
   depth, the sinkhole and other pits stay dry; `canopy-dry` /
   `ground-dry` are the capture as it is: the Water button;
   `aerial.json` carries `build`, the tiles' `?v=`). The server refuses
   a file asked for with another build's `?v=`, and any `?v=` while the
   json is missing (mid-upload) - 404, `no-store` (`MetaBuild`, gotcha 71).
3. **Upload** (owner token): `python scripts/aerial-upload.py` - chunks
   under 90 MB to `/api/admin/aerial`, the first clears the folder.

## The 3D world (2026-09-28)

The game's own models and collision in the spot page's 3D view
(`wwwroot/world3d.js`, switches per kind - Trees / Rocks / Props / Pickups,
`kindOf`: layer + mesh / material names, 2026-10-01 - and Collision), streamed in 250 m
chunks within 700 m of the camera's target, one InstancedMesh per model.

1. **Pooled objects** (game, bridge, <1 s): `call
   static:ForestOverlay.Game.WorldDump Write` ->
   `config/ForestOverlay/world/spawned.txt` - every LOD placeholder (trees,
   bushes, saplings, rocks, cave walls; not greebles under `Pooling`) and
   its prefab's parts. Only needed again after a game update.
   **Endgame area members** (v0.24.182, in the endgame - its scene
   loaded): `call static:ForestOverlay.Game.WorldDump AreaMembers` ->
   `world/area-members.txt`, what each area switches on (below). Every
   `placed-*.txt` / `greebles-*.txt` in that folder goes into the export:
   keep diagnostic `Placed` dumps elsewhere (gotcha 78).
2. **Export** (offline, ~1 min, `pip install UnityPy meshoptimizer`):
   `python scripts/world-extract.py export` reads the scenes from the game's
   files (level2 main, 7 endgame, 11, 15-30 cave props) + spawned.txt ->
   `site/world-out/` (not in git): `world.json` (materials, meshes, models,
   chunks, files, `build`; `version` 3) and `b/<i>.bin` - every mesh,
   texture and chunk of instances packed (*Load size* below). `stats [level
   ...]` prints what a scene holds. The format is in the script's
   docstring; the packing and the far copies in `scripts/world_pack.py`'s.
   `python scripts/world_pack.py repack <world> <new folder>` packs an
   existing world (any version, e.g. the live one downloaded) the same way
   without the game - a new `build`.
3. **Upload**: `python scripts/aerial-upload.py --world` (to
   `/api/admin/world`, clears first; `world.json` last). Files are named
   by index, so the server refuses another build's `?v=` and every `?v=`
   mid-upload (404, `no-store`, `MetaBuild`); `world3d.js` re-reads
   `world.json` per 3D view and starts over when a file is refused
   (`stale()`, gotcha 71). A tab open across an upload recovers by itself.

**Load size** (2026-10-01 / 02, cloud sessions):
- **`version` 3 - one set of files** (2026-10-02, `scripts/world_pack.py`):
  every blob - a chunk's instances, a mesh, a texture (only the variant the
  page reads) - in `b/<i>.bin`, 4-byte aligned, its place `[file, offset,
  length]` (a mesh's / chunk's `at`, `textures[i]` = [jpg, png]). Grouped by
  the chunks that use it: a chunk's instances and what only it uses in its
  area's 500 m cell, what 8+ chunks use in the area's common files, what a
  few use in a 1 km cell of their mean column; cut at 8 MB. Picked by
  simulating layouts over 25 views of the live world (cells of 500 / 750 /
  1000 m, packs of 4 / 8 MB): fewer requests always cost bytes - a cell
  reaches past the view's 700 m (a prefetch: a pan loads it anyway).
  Measured on a local site with the live world (`scripts/site-measure.py`):
  the Labskip view **188 -> 60 requests, 38.6 -> 41.9 MB** on a desktop,
  pixel for pixel the same picture; other views 241-388 -> 79-97.
  **Live since 2026-10-02** (repacked from `site/world-out` with
  `world_pack.py repack`: 167 files, 60 LODs, 286,233 -> 104,760 triangles
  in 22 s; uploaded as 168 files, 60.4 MB): the live Labskip view measured
  **60 world requests (59 `b/` + world.json), 42.0 MB, 4 s** on a desktop
  (`SITE=https://forest.deter.cloud site-measure.py view live desktop`), the
  same picture; Collision loads on its click. Versions 1 (one file per
  mesh) and 2 (`p/` mesh packs + `q/` texture packs) are still read; a page older than
  version 3 shows no world for its json (no errors) - deploy the site
  before uploading one.
- **Far copies** (2026-10-02, live; every device): 90% of a view's
  triangles are 400 m+ from the target, so each mesh of 200+ triangles a
  rendered model draws gets a chain of lighter copies, each with its error
  `e` in mesh units (`meshes[i].far` = their indices, lightest last; a
  copy's `of` = i). world3d.js draws an instance with the lightest copy
  whose error, at its scale and distance from the camera (to its bounding
  sphere), is under `FAR_PIXELS` (1) of the drawing buffer - the Detail
  button's density moves the switches out by itself. Each such model is one
  InstancedMesh per copy, its instances dealt out by `split()` (when the
  camera moved 2 m+ or the focal length changed, at most every 400 ms; only
  a model whose instances changed copy is rewritten; ~4 ms for 46k instances
  from scratch on a desktop, 0.6 ms when nothing changed), each culled by a
  sphere round its own instances. Collision keeps the full mesh.
  `forest3d.world.setFar(0)` = full meshes only (site-measure `NOFAR=1`).
  The copies (`world_pack.py far_copies`, 16 s for the world): solid parts
  simplified by **meshoptimizer** as far as an error allows (border edges
  stay on their borders; fast-simplification slid branches' open ends up
  the branch - whole branches went, and every tree's error was metres);
  leaf cards (cut-out pieces of <= 64 triangles) thinned - 60 / 35 / 20 /
  10% kept, each grown by 1 / sqrt(keep) about its centre so the canopy
  keeps its cover (error: how far a grown card reaches past its old edge);
  each copy the lightest pairing for its error, kept only under 70% of the
  triangles before it (50% was tried: 26% more triangles, no faster). A copy
  that moves no vertex is **shared**: indices only, over the full mesh's
  vertices and normals. 3,185 copies of 931 meshes, 18.6 MB raw (11.6
  shared), beside their mesh in its file. Measured live, same views (four
  surface lookFroms, `site-measure.py`): **101.2M -> 33.0M triangles**,
  48.3 -> 46.0 MB, 91 -> 92 requests; the 4080's frame 11.2 -> 6.8 ms
  (a pixel read back after each; the draw calls 528 -> 663 cost ~1 ms of
  submitting - a desktop GPU is not triangle-bound, a phone's is); Labskip
  on a phone viewport 67.9M -> 23.1M, 61 requests, 40.1 MB. Pictures: 1-3%
  of pixels differ by more than 8 levels (far canopies, cliff edges), the
  same by eye at 2x. Seen by the author on a MacBook and an iPhone 13 mini.
  **View-cone culling** (2026-10-02): `split()` also leaves out a far
  model's instances whose sphere is outside the view cone (forward + the
  screen corner's half angle) widened by `CULL_MARGIN` 30 deg; turning
  `CULL_TURN` 10 deg re-picks at once (past the 400 ms limit), so only a
  turn of 20 deg+ between two frames can show a gap at the edge. Models
  without far copies (small meshes, collision) still draw whole. Labskip
  fit, local site: desktop 23.5M -> 5.6M triangles, 761 -> 460 draw
  calls, 7.8 -> 3.5 ms a frame; phone viewport 23.1M -> 3.5M, 695 -> 354,
  8.1 -> 2.8 ms; the pictures identical (<= 0.001% of pixels).
  **Every model culled per instance, and under the ground** (2026-10-02):
  a model without far copies goes through `split()` too (one level: culled
  or drawn), so its draw call goes when none of it is in view. And with
  the camera above opaque terrain (`fade` 1, `aboveGround`: higher than
  every height sample within `COARSE_MAX` of it), an instance wholly below
  the drawn terrain (`underGround`: its sphere's top under the lowest
  sample within `COARSE_MAX` of its footprint, block minima of 8 x 8
  samples) is not drawn unless the line from the camera to it passes over
  a hole, seen from above (`overHole`: 183 boxes round the sinkhole,
  widened by the cells the coarse terrain leaves out) - a line from above
  the terrain to below it crosses the drawn terrain, or goes through a
  hole. Measured (local site, desktop, `NORUNS=1`, one view per run - the
  script reads its counters at the end): plane wreck (360, 75, 1050) 461
  -> 305 draw calls, 13.2M -> 9.8M triangles, 5.1 -> 3.9 ms; overview
  (500, 80, 500, dist 600) 498 -> 293, 18.7M -> 13.5M, 5.4 -> 4.4 ms;
  saplings at y 100 453 -> 412; Labskip fit 460 -> 424, 5.6M -> 3.9M, 4.0
  -> 3.3 ms. Pictures identical (0.000% of pixels) with the surface
  opaque; underground (the surface faded) 0.2% differ - the see-through
  surface pieces blend in another order (three.js sorts them by their
  bounding sphere, which culling moves), neither more right.
  **Tighter, the same day**: `aboveGround` asks only the aligned 4 x 4
  cells under the camera's near plane (every drawn cell, step 1 / 2 / 4,
  lies in one; reach = near / cos(corner), `update`'s new `near`) - the
  10 x 10 window round the camera failed for a camera 1-2 m over a slope,
  so ground-level views never culled. `throughHole` (was `overHole`) counts
  a hole only when the line is lower than its box's highest sample while
  over it, and only when the line was not already under the terrain before
  (marched in blocks). Local site, `NORUNS=1`: tree spot (428, 78, -4) yaw 0
  634 -> 560 draw calls, 20.5M -> 18.9M triangles; yaw 180 362 -> 300;
  into the sinkhole (yaw 270) 484 -> 468; above it (300, 150, 60) 360 ->
  332; plane wreck / saplings at ground height unchanged. Pictures
  identical (0.000%; ~5 pixels differ between two runs of the old code
  too). Lines that really go down the sinkhole stay drawn - the next cut
  there would need the sinkhole's floor (a world model, not the heights).
  **BatchedMesh by material: tried and dropped** (2026-10-02, not
  shipped). A model with fewer than N instances went into a BatchedMesh
  per material + kind (each instance an item, split() switching its copy
  or hiding it). Local site, desktop Edge (D3D11, `WEBGL_multi_draw`
  present), `NORUNS=1`, submitting ms: tree spot 4.4-5.1 -> 11.0 (N 16) /
  9.7 (N 2), plane wreck 2.3 -> 9.7 / 6.3, overview 2.6 -> 10.2 / 6.9,
  Labskip fit 2.6 -> 9.9 / 5.3; draw calls barely fell (1196 -> 891 /
  1113). Why: (1) ANGLE runs a multi-draw as a loop - an item costs about
  a draw call (2689 items in 356 batches took 8.4 ms, the 548 instanced
  calls 2.5 ms), so a 2-15 instance model became several items instead of
  one instanced call; (2) materials barely repeat: 951 materials, 761
  distinct looks, 623 distinct textures; 532 materials in the tree view
  for 1196 calls - the floor for any per-material batching. The draw
  calls follow the textures: the next lever would be texture arrays /
  atlases in the export (models of one shader sharing a material, the
  layer per instance), an export-side job - only if frame times call for
  it (desktop 4-8 ms now). The batched code was not
  committed. Before (same day): 60
  phone-only copies within 2 cm (70.6M -> 67.9M; a quarter everywhere had
  holed the trees and sunk Cave 6's floor).
- **Collision is fetched when switched on** (2026-10-02): every collider's
  mesh was fetched and built, hidden; now a rebuild that finds a collision
  mesh missing fetches it ("loading...") and builds the model when it is here.
- `python scripts/world_pack.py test` checks a synthetic world round-trips
  (every blob's bytes, alignment, a repack) and its far copies (a hill's
  shared and facing up, a bush's thinned cards covering 80-125% of the
  leaves, lighter and erring more each, beside their mesh);
  `... synthetic <out> [1|3]` makes a 9-chunk world near the tree spot to
  upload to a local site.
- **Brotli + gzip**: the upload writes `x.br` and `x.gz` beside each
  `.bin` / `.json` when smaller (`Precompressed.cs`, both at the smallest
  size, via a `.tmp` name so a request never gets half a file); a client
  taking `br` gets the Brotli copy, else one taking `gzip` the gzip one
  (`Content-Encoding`, `Vary: Accept-Encoding`, the original's type; same
  `Cache-Control`, `?v=` refusal, ETag / 304). Brotli since 2026-10-02
  (~10% off the meshes); the world already on the server got its `.br`
  copies from a one-time startup pass (`Precompressed.Backfill`, logs
  `World: n Brotli copies written`). Measured live: b/0.bin 221 KB gzip ->
  197 KB br. Cloudflare does not compress `application/octet-stream`
  itself, and it fetches the Brotli copy from the server: a client
  sending only `gzip` now gets the file **uncompressed** (Cloudflare
  decodes, never re-encodes) - every browser sends `br` over HTTPS, so
  accepted; `curl -H 'Accept-Encoding: gzip'` measures that, not what
  visitors get. `.gz` / `.br` names are never
  accepted in an upload. The server compresses before it answers, so
  `aerial-upload.py --world` sends 16 MB chunks (a 61 MB one ran past
  Cloudflare's 100 s: HTTP 524 with the world already cleared - rerun the
  upload). Locally, an upload right after a start can race the startup pass
  over a world without `.br` copies (a 500, file in use): upload again. Textures stay single files: JPEG / PNG gain
  nothing from gzip, are shared across chunks (cached once) and load
  through three.js's image loader by URL.

What the export decides (details: gotchas 64-67):
- Areas: `surface` (fades with the terrain when the view is underground),
  `caves`, `endgame`.
- Kinds: `render`, `render-off` (switched off in the file - the endgame's
  props, turned on by Area as sections are entered; kept), `collide`.
  Switched-off primitives and Blocker / Water layer renderers are volumes
  with debug materials - dropped.
- Materials: `_MainTex` + `_Color`; `top` / `topScale` = the Lux shader's
  snow / grass / moss over faces that look up (`_WnAlbedoSmoothness`);
  `cut` (alpha test) for foliage / transparent / Standard-cutout shaders
  only (Lux keeps smoothness in its alpha); `fx` (particles, sheen) -
  skipped by the site.
- `scale` = `_MainTex` tiling when not 1 (cave walls 35-40x, cave ground
  12x - without it one texture spans a 200 m cave piece; 2026-09-28). The
  site keys a texture on index + alpha + scale (`texture(i, alpha, scale)`).
- Faces: solid materials draw front faces only (as Unity culls), cut-outs
  both sides (2026-09-28). Checked offline on box meshes: the turned indices
  + the z mirror leave CCW faces pointing out.

- Cave pieces (`LOD_Cave*`) take their placeholder's scale, not the
  prefab's (`LOD_Cave.SetLOD`; gotcha 69, 2026-10-01); other LOD types keep
  the prefab's.
- **Cutaway underground** (2026-10-01): the cave pieces are closed boulders,
  so a camera in the rock around a cave sees only their outsides. While the
  view is underground (the terrain fades), `World.setCut` clips everything
  between the camera and its target, 4 m short of the target - one clip
  plane on every world material (off = far away, so no shader recompiles).

- **Chunks by the mesh's bounds** (2026-10-01): an instance goes in the
  column of its mesh's world centre; one wider than 250 m in the column's
  `_L` chunk; every chunk carries `bb` (where its instances reach) and the
  site loads by it. Before, the cave grounds, the mountains and the
  sinkhole (origin 0,0,0, world-space vertices) all sat in the centre
  chunks - Cave 6's floor never loaded (gotcha 70).
- **Dropped** (2026-10-01): switched-off renderers with no material /
  `Default-Material` / `lambert2` (whole-map build leftovers:
  `navmesh_patch`, `cliff_COMBINED*`, `treesExport*`, `rocksExport*` - ~400k
  triangles), and the black `CaveN_Blocking` shells round each cave system
  (they hide the void past a cave's openings in game; on a map they hide
  the cave).
- **Under the terrain = caves** (2026-10-01): a "surface" instance whose
  whole mesh is 2 m+ under the terrain where it stands (the site's own
  `wwwroot/terrain` heights) is filed with the caves - Cave 6's wood panels
  (root `CaveWoodplanks`, layer treeMid) faded away with the surface before.
- **Greebles** (2026-10-01, v0.24.174-176): `call
  static:ForestOverlay.Game.WorldDump Greebles surface` on the surface and
  `... Greebles caves` in any cave (all 16 cave prop scenes load together;
  the surface's `MainSceneGreebles` unloads in caves) -> `world/greebles-
  <name>.txt`; the export reads every `greebles-*.txt` (duplicates once).
  ~44k placed (debris, flowers, corals, Cave 6's body piles, stalactites,
  the sticks round trees) + their LOD loaders (cave spikes, ferns, bushes);
  each zone as on a first visit (author: "one visit's set is fine"). Not
  placeable: the player's GreebleLayer sticks / small rocks (random per
  spawn - game-notes *Greebles*). Needed again after a game update only.
- Dropped too: `VRTreeRing` (the VR mode's black ring round trunks); the AFS
  tree shaders' `_Color` is ignored (0,0,0 on pines / fig trees - black
  trees on the site).
- **The endgame's area members** (2026-10-01, v0.24.182): every endgame
  renderer is switched off in the scene file; entering an area runs its
  `AreaMembers.TurnOnMembers`, which switches on exactly its `_renderers`
  list. A switched-off primitive (Cube / Quad / Plane ...) used to be
  dropped as a debug volume - the lab's office floors, ceiling tiles,
  concrete cubes, door signs, whiteboard drawings, bloody footprints went
  with it (~250; "the lab looks sparse"). Now one an area lists is kept
  (`area-members.txt`); a rock-textured blocker cube in the same section
  stays off in game and out of the export. Measured over the bridge: each
  of the 24 areas entered in turn (`call Sections/<area> Area.OnEnter
  null` - entering one leaves the last), its renderers dumped and diffed
  against the export: what is left is props that physics moved (0.1-0.3
  m), skinned meshes and the pickups' glints.
- **Glass** (2026-10-01): a Standard material in Fade / Transparent mode
  (`_Mode` 2 / 3) or a legacy transparent shader is `glass` (opacity = the
  colour's alpha, drawn see-through, both sides) - lab windows, glass
  walls, cabinet doors; with a texture full of holes it is a cut-out
  instead (grills, dirt decals).
- Missing so far: skinned meshes (Timmy, the dead girl, the artifact's
  door). The site has no lighting, so the lab reads white where the game
  is dark - its textures are right (checked).

**Terrain, sea and water in 3D (2026-10-01, `map3d.js` / `world3d.js`):**
- The island mesh is every 2nd height sample on desktop (~7 m), every 4th
  on phones; a full-resolution **detail patch** sits round the runs and,
  once the orbit's centre settles close outside it, follows the centre
  (`lookRegion`). Its coverage test is cut to the map (gotcha 74 - it
  looped at the coast before). The island's 4096 px photo is made on a
  layer change only (`islandTexture`); a new patch starts from the
  island's photo, then its finer tiles (`detailTexture`).
- **The sea plane** (one plane at sea level, 41.5) is drawn only where the
  water is open to the map's edge - the photo bake's flood fill
  (`seaMask`, a mask texture from the heights); it used to fill every pit
  below sea level inland (gotcha 75). Outside the map: sea everywhere.
- **Lakes** are the game's own models: `The Forest/Water` surfaces (drawn
  as dark water) over a black `LakeFake` plane. The capture never shows
  them (like the ocean), so in 3D these models are the only water. A
  surface chunk's lake is clipped to where the terrain's heights are
  below it (`groundClip`: the models overhang the shore); cave lakes are
  not clipped.
- **Water button**: a `-dry` layer hides the sea plane, the lakes and
  their `LakeFake` planes (`World.setWater`).
- **South at the top** (author, 2026-10-01): 2D draws turned 180 degrees
  (`map.js` `UP`, `toScreen` / `fromScreen` / `blit`), the 3D fit looks
  from the north (`yaw` pi).
- **The terrain's holes** (`terrainHoles`, a5a0077): height-0 samples away
  from the open sea - the sinkhole only (the game draws no terrain there;
  its floor, cliffs and water are models down to y -304). No terrain
  triangle uses them, and `setGround` gives them no ground (the lake clip
  keeps the sinkhole's water).
- **Moved objects**: the yacht is not where the scene file keeps it (the
  game moves it at run time): `call static:ForestOverlay.Game.WorldDump
  Placed yacht yachtWobblePrefab(Clone)` (v0.24.181, any time in game) ->
  `world/placed-yacht.txt`, read by the export; the scene's `Yacht` root
  is skipped (`MOVED_ROOTS`). Its hull is untextured in the game too
  (`BoatHull deferred`, a colour only).

**Looking at a spot on the local site** (gotcha 68 - do it before calling a
render fix done): `python scripts/site-look.py <scenario>` does all of
the below (its docstring lists the scenarios: edge / pan rebuild counts,
lake clip on / off, Models off, layer-hiding JS, raycast-ready eval).
With `forest-site` taken by another session (port 5080 serves *its*
copy): preview `forest-site-alt` (5081, this checkout's wwwroot, data in
the temp folder - copy `site/ForestSite/bin/Debug/net10.0/data/world`
into it, server stopped, for the current world) and `SITE=http://localhost:5081`.
By hand: preview `forest-site`, upload the world locally
(`FOREST_SITE_ADMIN_TOKEN=local-admin python scripts/aerial-upload.py
--world http://localhost:5080`; the photo tiles the same way without
`--world` - both are in the local site's data since 2026-10-01; an
underground spot fades the terrain: `forest3d.setRuns([], true)`), open
any spot, 3D, then in the page
`window.forest3d.lookFrom(x, y, z, yaw, pitch, dist)` (Unity coordinates,
yaw 0 = north, pitch down) - the game camera's `Transform.position` /
`eulerAngles` from the bridge give the same view as a `shot`. The game's
vertical FOV is 95 at 16:10, the site's 55 over a wider canvas - close
enough to compare shapes; for a like-for-like look set the site's
`forest3d.camera.fov = 95; forest3d.camera.near = 0.2;
forest3d.camera.updateProjectionMatrix()` first. Headless (the in-app pane pauses rendering when
hidden): `pip install playwright`, Edge via `channel="msedge"` - open a spot,
click 3D, `lookFrom`, wait for no chunk `loading`, page screenshot clipped to
`forest3d.canvas`. A second local site beside another session's (port 5081,
own build output and data): `.claude/launch.json` `forest-site-alt`.
**In a cloud session** (2026-10-02): `apt-get install dotnet-sdk-10.0`
(after `apt-get update`), restore with nuget.org only (BepInEx's feed is
blocked; the site does not need it: a `nuget.config` with `<clear />` +
nuget.org, `dotnet restore site/ForestSite.Tests --configfile <it>`), run
`dotnet <build>/ForestSite.dll --urls http://localhost:5080` from
`site/ForestSite` with `FOREST_ADMIN_TOKEN=local-admin
FOREST_DATA=<folder>`; Playwright's Chromium is pre-installed
(`/opt/pw-browsers`, software GL - `site-measure.py` finds it); the live
world downloads in ~20 s (`world.json`, then every file it lists with its
`?v=<build>`; Cloudflare refuses Python's default User-Agent) - put it
in a `site/world-*/` folder (git-ignored; never commit a world export).

Measured (the Labskip spot's 3D view, its own fit): live 2026-10-01 604
world requests (458 textures, 55 packs, 90 chunks) for 38 MB; live
2026-10-02 with texture packs (only the variant the page reads - both had
fetched 17.4 MB of textures for 10.4 used) 188 for 38.6 MB, reproduced
exactly on a local site; version 3 (local) 60 for 41.9 MB. Tool:
`scripts/site-measure.py` - `seed` copies a live spot and its runners' best
runs to a local site (the same 3D fit), `view` counts a view's requests /
bytes / triangles / draw calls / frame time and shoots it (desktop, phone,
narrow; `NOFAR`, `DELAY`).

**The 3D view's look depends on load order** (2026-10-02, found while
comparing): on the old (version 2) site, holding back random world files
flips some maple leaves near the tree spot between two looks, 1.32x
brighter or not (8.5% of a phone shot), with the same data. Every model's
InstancedMesh sits at the origin, so three.js's sort falls back to creation
order - the order chunks happened to arrive. Not the transparent pass
(only the lakes are transparent there) and no duplicate instances there;
cause not found. Compare shots taken the same way (single views; a second
view after a first loads in a different order), and when a diff flips, test
the old build with `DELAY` first (gotcha 83).

**The Elevator Boost end** (2026-10-02, headless vs a game `shot`): the
end box (-456, 707, -1969) sits inside the overlook room, an endgame
section only drawn in game once you arrive (a `tp` there shows a cliff);
the site draws the room. Past the terrain's south edge the mountains are
models (chunks to z -2931) and are there, but up close they are smooth
grey (TEX_MAX 256) with no snow on their tops, where the game shows
detailed rock with snow - not looked into further.

Author's notes (2026-10-02, live site): view-cone culling "fine"; in a
cave, *Follow*'s camera sat inside the rock and a wall covered part of the
view - **fixed 2026-10-02**: `World3D.firstHit` finds the first solid rock /
prop face (no cut-outs, front faces only) on the line ghost -> camera, and
Follow pulls the camera 0.4 m in front of it at once (eases back out;
closer than 4 m it aims lower so the ghost stays on screen). Not the
collision meshes (fetched only with Collision on) but the drawn ones, by
instance sphere, then triangles in groups of 32 with a box each (made once
per mesh): ~0.2 ms a test (all triangles: ~13 ms). The cut plane alone
left the 4 m in front of the ghost. Checked headless on 1st logboost's runs
(live API proxied to a local site): a clear passage instead of sliced
rock; not yet seen by the author. Caves not drawn on the 2D map:
**fixed 2026-10-02** (*Caves on the 2D map* below); the photo map's
"overlook's shadow" with no overlook: **fixed in the capture (v0.24.221),
the live map needs a recapture** (*The photo map*, step 1); the south
mountains' 3D textures: nice QoL, not a dealbreaker.

Open: the load-order look
(above); the web replay as fast
as possible (author, 2026-10-02) - **started**: the 3D view draws at one
pixel per CSS pixel with no antialiasing by default; its **Detail** button
(beside Collision, `forest.map3d.detail` in localStorage, `Map3D.sharp`;
it was `this.detail` at first - the terrain patch's own name, so with Detail
on the patch was a boolean: the two console errors in `buildDetail` /
`detailTexture`, and no patch; renamed 2026-10-02) draws at the
screen's density (max 2) and antialiases after a reload (a WebGL context
attribute) - more fast-by-default switches can join it (texture size, draw
distance); pickups spawned at run time and the player's random sticks / rocks are missing. The photo map's `aerial.json` is still read once per page
(map.js): a tab open across an aerial upload gets 404 tiles (holes) until
a reload.

## Caves on the 2D map (2026-10-02)

While the map is underground (every ghost 3 m+ under the terrain), map.js
draws `wwwroot/terrain/caves.webp` over the faded ground: the caves' and
the endgame's floors from above, coloured by height (pale = higher), slope
shaded, walls a dark edge. `python scripts/cave-bake.py` bakes it (~45 s)
from the 3D world's **collision** meshes (`site/world-v3`, areas caves +
endgame; 14.2M triangles) at 1 px a metre: lossy WebP, 0.44 MB (PNG 3.8
MB); `caves.json` holds the bounds and `build` (its `?v=`). Re-bake after a
new world export. What makes a floor (all in the script's docstring):
an up-facing face whose next face above is a down-facing one 2 m+ up -
the collision pieces are open surfaces stacked over each other; then
**walkable regions**: each pixel's 4 highest floors, neighbours within
1 m of height joined, regions under 300 m2 dropped. Without that step the
plan was 1.6 km2 of wide blobs: the pieces overlap and leave air slivers
(a 2.4 m gap just under the surface) that pass the floor test and, being
highest, hid the real floor - 1.06 km2 of passages after. Checked: the
Cave 6 test spot (1283.9, -70.6, 612.9) is on a drawn floor; the Labskip
route runs over the lab's floors; a few sampled floors visited over the
bridge were in caves (a `tp` into an unloaded cave shows sky - judge by
the walls, not the background). Headless shots only (Edge via
Playwright); not yet seen by the author. Some pieces sit kilometres off the
map (x -17840): the bake keeps |x|, |z| <= 2500.

## Item list (2026-10-02)

`wwwroot/items.json`: the game's 231 items (`ItemDatabase` id + name) and
each one's carry cap from `Inventory.GetMaxAmountOf` in Slot 1 (-1 = not
an inventory item). /admin's category editor picks item caps from it
(`itemCapsPicker` in admin.js: type part of a name, arrows / Enter or a
click add it at the game's cap, Remove per row; no free text, Log left
out - it has the log cap; a saved name not in the list shows "not a game
item"). The author asked for it in place of a text box (2026-10-02:
"autofills the names so they can't be mistakenly typed"). To refresh it
after a game update: over the bridge read
`BepInEx_Manager OverlayPlugin._host._modules[4].Ctx.Inventory._catalog[i].Id`
/ `.Name` for i < `_catalog.Count`, and `call
static:TheForest.Utils.LocalPlayer Inventory.GetMaxAmountOf <id>` per id
(a `-f` script, ~2 s). The local site stamps `?v=` once at startup:
restart it after editing a wwwroot file, or the page keeps the old one.

## Next

1. ~~Other runners' PBs as comparisons in game~~ done (v0.24.155:
   `board.txt` + `Data/SiteBoard`, `Modules/PracticeRunModule.Site.cs`).
2. ~~A spot submission button in Practice; an admin page~~ done
   (v0.24.158-159; the author submitted and rejected one live).
3. ~~The player's state at a point of a run~~ done (2026-09-27, site
   only): the spot page's *State* panel shows the selected run at the
   scrub time; a click on a line jumps there (that run, that moment).
   `/api/runs/{id}` serves `state: {channels, samples: [[t, v...]]}` -
   a preset (`Runs.ShownChannels`: health, stamina, energy, fullness,
   thirst, armour, cold armour, battery, body temp, stealth, cold, light;
   plus speed from the path) - and `?all=1` every channel, behind the
   panel's *Show all* (author: "a smaller preset useful for the runners").
   Values step (the last 5 Hz sample), not blended. **Carried items**
   (v0.24.161, author: "dynamically count items" + event-based):
   the `.run`'s item track, `i|<t>|<name>:<count>|...` - only changes
   (the first sample lists the bag, a used-up item is written 0), names
   are the game's database names (`Soda`, `EnergyMix`, `CamCorderTape`;
   the page spaces them out, `ITEMS` in app.js renames a few).
   `Game/ItemCounter` reads `PlayerInventory._possessedItems` (only what
   the player owns) with `AmountOf(id, false)` (held item included),
   **only after** a postfix on a writer of `InventoryItem._amount` ran
   (plus a full read every 5 s). `/api/runs/{id}` serves it as `items:
   [[t, name, count]]`; the panel's *Carrying* lists the items the run
   ever held, 0 dimmed. v0.24.160 recorded a fixed list as `item:<name>`
   channels instead - still read (only runs from that version).
4. **The map: terrain heightmap underlay, then 3D** - the 2D underlay
   is **done** (2026-09-27): v0.24.162's bridge call `call
   static:ForestOverlay.Game.TerrainDump Write` writes
   `config/ForestOverlay/terrain/` (heights uint16, splat alpha, texture
   colours), `python scripts/terrain-bake.py` (numpy + pillow) bakes
   `wwwroot/terrain/` (map.jpg 2048 px relief, heights.u16 1025^2,
   terrain.json; sea level 41.5 = Ceto `Ocean.level`); map.js draws it
   under the grid, fades it with "UNDERGROUND" while every ghost is 3 m+
   below the ground (caves, endgame), and draws the 12 plane crash sites
   (`PlaneCrashLocations.finalPositions`, HullRef). Heights checked
   against `Terrain.SampleHeight` at 4 points. **Aerial photo layer**
   (2026-09-27, site only): map.js draws the tiles of `/aerial/` (bake:
   `scripts/aerial-bake.py`, upload: `FOREST_SITE_ADMIN_TOKEN=... python
   scripts/aerial-upload.py`) over the relief, Photo / Ground / Relief
   on the map (remembered per browser; hidden with no `aerial.json`, which
   answers 204 then); tested with fake tiles only - no real bake uploaded
   yet. **3D view** (2026-09-27, site only): the map's 2D / 3D switch
   (2D stays the default) loads `wwwroot/map3d.js` (three.js 0.170, since
   2026-10-01 served from `wwwroot/vendor` - the CSP allows no CDN script;
   versioned via index.html's `data-map3d-src`): the heights as
   a coarse island mesh + a full-resolution patch around the runs, the
   relief or aerial tiles (level <= 5) on it, run lines bright up to the
   scrub time, ghosts, zones (spheres, turned boxes); orbit, or *Follow*
   behind the focused run's ghost; the terrain fades when the ghost is
   underground. Checked in headless Chromium (desktop, phone touch, fake
   aerial tiles); not yet on a real GPU / phone. Left:
   caves / endgame sections / overlook (not in the terrain - a mesh dump),
   which plane a save has. **Photo map** (v0.24.164-169, `Game/AerialCapture`
   -> `scripts/aerial-bake.py` -> `scripts/aerial-upload.py` -> the map's
   Photo / Ground layers): state and next steps in CLAUDE.md *Pick up here*.
   (The plane line is done too, v0.24.163.) Originally: Known (bridge, 2026-09-27, Slot 2): one
   terrain, `Terrain.activeTerrain` = `MainTerrain`,
   `terrainData.size` (3500, 250, 3500), at (-1750, 0, -1742.63) - so it
   covers x -1750..1750, z -1742.63..1757.37; `heightmapResolution` 2049
   (4.2 M heights, ~1.7 m per sample), `alphamapResolution` 512 (the
   ground textures' mix - a colour map source). Plan: dump the heights
   once from the game (`GetHeights(0, 0, 2049, 2049)` - a big read: do it
   in rows from a bridge `call` or a small one-off plugin/debug action,
   not from `OnGUI`), bake them offline into a shaded-relief PNG (plus
   maybe tiles / a lower-res 1025^2 for phones) shipped in
   `site/ForestSite/wwwroot`, and draw it under the grid in `map.js` at
   world coordinates (x east, z north; the image's row 0 is z min). Caves
   stay a later mesh dump. 3D after (the same heights as a mesh).

## Security (review 2026-10-01)

Checked: every endpoint in `Program.cs`, `Store` / `Runs` SQL, the pages'
DOM building, the map uploads, the deploy (`site/deploy`, `site.yml`).
**Fine as it was:** every SQL query is parameterised (the one string-built
`PRAGMA table_info(<const>)` takes constants); the pages build the DOM
with `textContent` / `setAttribute` only (no `innerHTML`), links are
site paths; tokens are 192-bit random, stored as SHA-256, compared in
constant time (owner); the admin token travels in a header, not a cookie
(no CSRF); hidden runs are left out of every public read; the run /
submission downloads are `attachment` text; map uploads are owner-only
and every zip entry must match `UploadPath`; the deploy key can only run
`deploy.sh`, which only restarts the container.

**Fixed (site, no plugin change; `ApiTests` *security review*):**
- Anyone could **rename anyone's spot**: a new route (a moved zone) under
  another runner's segment id was the uploader's own row, and the spot
  page shows the newest route's name / category / description / "by".
  Now a copy takes the spot's labels and owner
  (`Store.SpotHolder`, `SeeRoute(copy)`; gotcha 72).
- An upload whose attempts were all refused still recorded its route
  (and its up-to-4 MB block): attempts are checked first now.
- The upload rate limit was keyed on the `Authorization` header - a
  made-up header per request was a new limiter each (memory, an hour):
  keyed on the address now.
- Spot submissions: at most 20 waiting per runner (each keeps its file).
- three.js came from jsdelivr with no integrity check, on the origin
  that keeps the admin token in localStorage: now `wwwroot/vendor`
  (npm's file, checked against its sha512; same bytes as the CDN's).
- Headers on every answer: CSP (`script-src 'self'`, no `unsafe-*`;
  `el()` sets `style` through the CSSOM), `nosniff`, `X-Frame-Options
  DENY` + `frame-ancestors 'none'`, Referrer-Policy, COOP,
  Permissions-Policy, HSTS (no `includeSubDomains`: other deter.cloud
  hosts are the author's). Checked headless: home, about, a spot in 2D /
  3D (232 world files), every admin tab - no violation, fonts load.
- `UploadPath`: `\z` instead of `$` (which also matches before a final
  newline), `[0-9]` instead of `\d` (every Unicode digit).
- The origin lock (`FOREST_ORIGIN_SECRET`), the container's lockdown
  (`compose.yaml`) and a network shared with Caddy only (`forest-site`,
  not Navidrome's): **on since 2026-10-01** - the author ran
  `site/deploy/README.md` *Hardening* 1-3; straight to the VPS answers
  403, through Cloudflare 200.

**Decided (author, 2026-10-01, on Claude's recommendation): both left as
they are**; asked again 2026-10-02 (author: runners link their socials
anyway; "deal with it however you think is best") - still left: the name
already defaults to the Steam name, and a fix splits every identity; the About page says the runner id is not anonymous. Revisit the
first if a runner asks (fix: the plugin sends the Steam id once at
registration, the server derives the public id with a secret HMAC and maps
old ids over - it knows the old hash), the second if a takeover ever
happens.
- **Runner ids can be reversed to a Steam account.** The id is SHA-256 of
  a fixed prefix + the Steam id, and individual Steam ids are ~2^31
  values - a few minutes of hashing maps every id on the site back to its
  profile. The runner name defaults to the Steam name anyway, so little
  is hidden today; a real fix (a per-install secret, or a slow hash) would
  split every runner's identity in two. Decide before the site has many
  runners.
- After an admin's *Reset token*, the next registration of that id wins -
  anyone who knows the id (it is public in `board.txt`) could take it
  first. Rare; the admin can reset again. A one-time claim code shown to
  the admin would close it.
- No disk quota per runner beyond the rate limits (600 uploads / hour /
  address, 4 MB each, gzip-stored): watch `/var/lib/forest-site`'s size.

Re-check after admin features: new endpoints go under the `admin` group
(its filter checks the token), owner-only ones check `IsOwner`, and any
new page text goes through `el()`.

## Useful from the game later

- Terrain heightmap for 3D: readable live over the bridge
  (`Terrain.activeTerrain.terrainData` heights / size) - dump once to a
  file for the site.
- Caves need a mesh dump (not done; their colliders are in the cave
  scenes).
