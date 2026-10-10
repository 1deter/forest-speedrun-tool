# forest.deter.cloud - reference

The site's reference detail, by feature. **Start at
[`docs/areas/site.md`](areas/site.md)** (what it is for, commands, the
formats, local work, which section here to read); the rules are in
[`site/CLAUDE.md`](../site/CLAUDE.md), the decisions in
[`docs/decisions.md`](decisions.md) *Site*. Decisions made while building
go to decisions.md; feature detail goes here.

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
  `/api/spots.txt` (the plugin's Website spots, `Data/SiteSpots`: a
  `spot|...` line per runner spot, then `owner|<id>|<runner id>` - the
  runner who first uploaded on it, so the plugin adds a runner's own spot
  back into their own list, T-0265; `/api/spots` has `owner` too);
  `POST /api/register` `{runner, name}` -> `{token}`, `POST /api/runs`
  (a `.foseg` with `[attempt]`s, Bearer token; answers `startstate:
  "wanted"` when the route has a `startstate` hash and the site no data -
  the plugin re-sends the bundle with its `[startstate]`, kept per route
  in `<data>/startstates/` only from the route's owner and when its data
  hash matches, served by
  `/api/spots/{id}/foseg`; T-0194), `POST /api/submissions`
  (-> `{id, replaced}`); `/api/admin/...` with `X-Admin-Token` (own rate
  limit, 120 / min): `check` (`{name, owner}`), `submissions[/{id}[/{status}]]`,
  `flagged`, `runs/{id}/hide|show|unflag`, `DELETE runs/{id}`, `runners`,
  `runners/{id}/ban|unban|reset-token`, `DELETE spots/{id}`, `log`, and
  the owner's `admins` (GET / POST a name -> `{token}` / `DELETE {id}`).
  Every non-GET admin call is logged (`admin_log`) by the auth filter.
- **Bot settings** (T-0028, `BotSettings.cs`; design docs/knowledge-bot.md
  *Bot settings page*): the owner's **Bot** tab on /admin (`GET / PUT
  /api/admin/bot`, owner only, validated, one JSON in `bot_settings`, a
  revision per save). The bot has its own token (`FOREST_BOT_TOKEN`, header
  `X-Bot-Token`, unset = 403): `GET /api/bot/settings` -> `{rev, settings}`
  (`channels`, `dms`, `perHour`, `perDay`, `models`, `thinking`,
  `queueChannel`; a missing field = the bot's .env default) and `POST
  /api/bot/report` `{version, rev, channels:[{id,name,guild,category}]}`,
  channels in Discord's sidebar order (`SeenChannel.InDiscordOrder`; the
  page groups them server > category > channel and shows "applied rev N").
  No secrets pass through. Edits are held until **Save changes** on a
  Discord-style unsaved-changes bar (Reset beside it); while it shows, tab
  links, Back and Sign out are refused (the bar turns red and shakes) and
  closing the page asks - `unsaved` / `leaveRefused` in app.js, the bar
  `unsavedBar` in admin.js (T-0231: the author's untick was never saved,
  the old Save sat below the fold). The **Categories** tab uses the same
  bar (T-0236): open editors are kept across redraws, a changed one will
  not close, its Save (new version) saves every changed editor (each a new
  version; a validation message stays under its form), and Check
  speedrun.com now / New category / Accept / Keep ours are refused while
  changes are unsaved (they reload the list).
  The bot side (poller, live reload, cache): docs/knowledge-bot.md.
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
  change: who, what, the answer's status - `admin_log`); the owner also
  has Bot, PB posts (Discord PB posts from runners' spots, *Spot
  categories, owners deleting spots, Discord PB posts*) and Admins.
- **Admins (author, 2026-09-27: not one shared key):** the env token
  (`FOREST_ADMIN_TOKEN`, in `/opt/forest-site/.env` on the VPS) is the
  **owner**; the owner's Admins tab makes a named `fa_...` token per admin
  (shown once, only its hash kept - `admins`), and revokes it. Admins can
  do everything except manage admins. Locally the `forest-site` preview's
  owner token is `local-admin`.

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
   the mountains whole. **Capture from the surface's own light**: Slot 1
   loads in the endgame lab, and before v0.24.222 a `tp` out left the sun
   off (cave lighting - every tile 3-15x darker, author spotted it); the
   capture now logs a warning when the sun is off. Check one tile against
   the old capture before a full run (the plane wreck tile 9_12: mean
   ~(84, 93, 57)). Recaptured 2026-10-03 on v0.24.222 (17.4 min, 1200 px,
   max graphics; the two south rows' cameras at y ~1500). The long N-S
   shadow south of the map's middle (tile 9_0) is real: the snow mountain
   model west of it (to y ~440) casts it, and it moves with `sunTime`.
   Retake
   rows N+ with `AerialStart -1750 <z0 + N*218.75> 1750 1757.369 ...` -
   its files are numbered from row 0 again and it overwrites `tiles.txt`,
   so back the folder up first, rename the new `<ix>_<k>` to `<ix>_<k+N>`
   and put the full `tiles.txt` back (done this way on 2026-10-01).
2. **Bake**: `python scripts/aerial-bake.py` -> `site/aerial-out/` (not in
   git; pyramid L0-6, 256 px; `canopy` / `ground` get the water drawn,
   shaded by depth: the sea from the terrain's heights - every pixel below
   41.5 except the sinkhole (the inland basins are water in the game:
   teleported into five, underwater, 2026-10-03) - and the lakes from the
   3D world export (`site/world-out`: every "The Forest/Water" model but
   the caves' `LakeCaveNew`, any area - the middle's lakes are one plane,
   `BigLake_v2` at y 48.4, filed under caves; the sinkhole's pool against
   its floor model, the terrain being a hole there); `canopy-dry` /
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
2. **Export** (offline, ~5 min with the far copies, `pip install UnityPy meshoptimizer`):
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
  not clipped. A lake counts as the surface's by its material, not its
  chunk (`World.surfaceWater`, 2026-10-03): the middle's `BigLake_v2`, a
  stream and the GeeseLakes (+ their `LakeFake` beds) are filed as caves
  (gotcha 70) and were drawn unclipped and, underground, as a solid blue
  sheet over the cave view; now clipped and faded with the surface. The
  sinkhole's pool stays with its floor (not faded). Checked headless
  (`site-look.py eval`, KEEPRUNS=1, under BigLake) before / after.
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
the old build with `DELAY` first (gotcha 83). **Gone in the current world**
(2026-10-03, version 3 + far copies + culling): the tree spot at ground
level (phone with touch, and 375 px without) and a desktop view from 100 m
up, each with no delay and three `DELAY` seeds (60% of `b/*` held back):
the draw order differed every time (`world.group.children`), the pictures
did not (largest pixel difference 2 / 255). Closed; the gotcha stands.

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
rock; seen by the author 2026-10-03 ("works great now"). Caves not drawn on the 2D map:
**fixed 2026-10-02** (*Caves on the 2D map* below); the photo map's
"overlook's shadow" with no overlook: **fixed and recaptured
(v0.24.221-222)** (*The photo map*, step 1); the south
mountains' 3D textures: **fixed 2026-10-03** - their maps kept at 1024 px
(`BIG_MATERIALS`) and the Standard shader's detail albedo (`_DETAIL_MULX2`,
116 materials: "detail" + "detailScale") multiplied in (`detailLayer`, x4.59
in linear light). Same day: 19 lab textures (the overlook, the boss room)
were solid black - Pillow's RGBA resize premultiplies by the alpha, where
Standard textures keep smoothness (0); colour and alpha now shrink apart
(gotcha 88). Live world build 1790991223; seen by the author
2026-10-03 (fixed).

Open: the web replay as fast
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
Playwright); seen by the author 2026-10-03 ("a little bit hard to read but it's fine"). Some pieces sit kilometres off the
map (x -17840): the bake keeps |x|, |z| <= 2500.

## Spot categories, owners deleting spots, Discord PB posts (2026-10-04)

From docs/backlog.md (*Site: spots*, *Discord webhooks*). Site + a small
plugin change (Practice's Share row).

- **Categories** are the segment's own `category` (the Practice editor's
  Category field - a simple field, the runner's choice). The owner's next
  upload carries a change (a finished run, or Runs tab -> *Upload this
  spot's saved runs*). The spot list groups each section by it; the
  plugin's defaults (`My spots`, `Segments`, `Spots`, empty) group as
  *Other*, last. One category in a section = no sub-headings. Admin-set
  categories: not built (later, if asked).
- **Folding groups** (app.js `homePage`): *Runners' spots* first (the
  timed ones with runs), *Community spots* after - folded by default while
  none of them has a run (mostly teleports today; a community teleport
  says "teleport", `/api/spots` has `timed`). Every section and category
  folds; the viewer's choice is kept in localStorage `forest.folded`
  (`{key: true|false}`, keys `runners`, `community`, `runners/<category>`,
  `official`, `official/<category id>`; read / written in try/catch). A search opens every group it finds.
- **A runner deleting their own spot**: the site has no browser login -
  a runner is their upload token (`/api/register`, kept by the plugin), so
  the delete goes through the game. `DELETE /api/spots/<id>` with
  `Authorization: Bearer <token>` (`Runs.DeleteOwnSpot`, rate limit
  `upload`): the spot goes with all its runs when every route's `owner` is
  this runner (the first uploader) and **no other runner has runs on it**
  (409 - their times are not the owner's to delete; an admin can);
  community spots 403, unknown 404, a bad / banned token 401. Each call is
  in /admin's Activity as `runner <id>`. In game: Practice -> select ->
  Share -> **Delete from the website** (second click within 3 s; the
  answer under the buttons - `SiteProtocol.DeleteSpotMessage`;
  `RunUploadModule.DeleteFromSite` drops the spot's queued upload files
  first). The spot returns with the owner's next upload on it (uploads are
  automatic) - the message says so. Practice's own **Delete** never
  touches the site (author, 2026-10-09, T-0265): the owner can add the
  spot back from Import -> Website spots (as their own, same id) and
  upload changes over it. The spot page tells a runner where the
  button is. A browser-side delete would need a login (e.g. a one-time
  link the game opens) - not built.
- **Discord PB posts** (`PbWebhook.cs`): `FOREST_DISCORD_WEBHOOK` (env or
  appsettings; unset = off, nothing logged per upload) - on the VPS in
  `/opt/forest-site/.env` (site/deploy/README.md *Day to day*). When an
  upload adds a run that is the runner's new best on a **community route**
  or on a **run spot** (`run = ` names a *published* category - none yet),
  one plain-text line is queued: "<runner> set a new PB on <spot>: 1:02.345
  (0.512 faster than 1:02.857)" or "<runner> finished <spot>: ... (their
  first run)", then the link `/spot/<id>/<route>?run=<run id>` (the spot
  page focuses that run). Not posted: a runner's own practice spot (anyone
  can make one - a spam path; the owner can switch those on, next item),
  a run under review (flagged), a re-upload of a run already there, a
  slower run. Names are markdown-escaped and the
  post sets `allowed_mentions: none` (no @everyone). Sending: a queue of
  20, one post at a time 2 s apart, at most 30 an hour, one retry after a
  429's retry-after; any failure is logged (`Discord webhook: ...`) and
  dropped - an upload never waits for or fails on it.
  `FOREST_SITE_URL` changes the link's address (default
  https://forest.deter.cloud). Tests: `ApiTests` *Owner_*, *Webhook_*,
  *PbNews_Decisions*.
- **PB posts from runners' spots** (T-0232; author, 2026-10-08: "site
  toggles for runner-spot PB posts, plus a choice of whether they go to
  the same channel as official-run PBs or a separate one"): the owner's
  **PB posts** tab on /admin (`GET / PUT /api/admin/pbposts`, owner only -
  the answer holds a secret; `PbSettings.cs`, one JSON in `pb_settings`
  with a revision, created on startup like `bot_settings`). *Post PBs on
  runners' spots* (off until saved on), then *The same channel as
  community and run spot PBs* (FOREST_DISCORD_WEBHOOK's) or *Their own
  channel* (a webhook URL pasted there: only
  `https://[ptb.|canary.]discord[app].com/api[/vN]/webhooks/<id>/<token>`
  is accepted, so the site still posts to no other host; a password field
  with Show; kept while off or on the same channel). A runner's spot is
  any route that is neither a community route nor a run spot. The same
  rules as above (new PB, not flagged, not a re-upload, the same line and
  link); `PbPosts` routes each PB (`PbNews.Target`) and runners' spots have
  **their own sender** - their own queue, 30 an hour and 5 a runner an hour
  - so they never crowd out a community / run spot post, even in one
  channel. Applies to the next PB, no restart. Saved with the Bot tab's
  unsaved-changes bar (Reset, leaving refused). The startup line ends
  `runners' spots off | same channel | own channel`; a failed post logs
  `Discord webhook (runners' spots): ...` - the sender's name, never its
  URL. Tests: `ApiTests` *Webhook_RunnerSpots_*, *PbPostsSettings_*,
  *Webhook_Sender_* (a fake `HttpMessageHandler` as Discord - the senders'
  `Http` and `Gap` are settable for that), *PbNews_Target*. Open: the
  post's look (the author's example is a KSF surf timer WR post) and
  whether a runner's-spot post says so - tasks/notes/T-0232.md.

## Official runs (2026-10-10, T-0223)

A QA request: runs in the official categories listed on the site so
anyone can watch them. The author (2026-10-08): **only once a category is
published** (the moderators publish on /admin; every live category is a
draft today, so the live site shows nothing yet), and **in their own
section**, apart from the runners' spots.

- **What counts**: a run mode attempt (`attempts`, docs/run-mode.md) whose
  category names a *published* category - by id, or by name as a run
  spot's `run = ` can (`Categories.IsPublished` reads it the same way) -
  that **finished** (`end_reason = finished`, its log in, a timer above 0)
  and whose **verdict is not red**. Resets, title-screen exits and attempts
  still running are not runs. "Approved by moderation" (author, 2026-10-10)
  is exactly that: the moderators' allow-list (`/admin`, *Allowed code*)
  is the only moderation step, a red run is out until it clears (no
  per-run flag). The verdict is the one the attempt page shows
  (`JudgedLog`, cached).
- **Each runner once** (author, 2026-10-10): the runner's fastest accepted
  run is the row; their latest `OfficialRecent` (5) accepted runs and the
  average of those come with it.
- **`GET /api/official`** (`Attempts.Official`, rate limit `read`, no
  sign-in): `[{id, name, count, runs: [{id, runner, runnerName, timerMs,
  at, avgMs, recent: [{id, timerMs, at}]}]}]` per published category in
  name order, `count` = runners listed, runs = the first `OfficialShown`
  (100) fastest; `at` = when the attempt started. While nothing is
  published the answer is `[]`: drafts and hidden categories are never
  named. Judging replays a log, so the answer is kept until a log arrives,
  the allow-list changes or two minutes pass (at most 2000 finished runs
  read per category). Tested: `AttemptTests.Official_OnlyPublished_...`;
  the smoke checks the empty answer. Index `attempts_category`.
- **The home page** (`app.js` `renderOfficial`): *Official runs* above the
  spots' search (author: placement stays above the spots), one fold per
  category, each row the runner, the date and the best timer, linking to
  the attempt; the button **Show recent runs** (per viewer, kept with the
  folds) adds under each row the latest runs as links and their average.
- **The attempt page's route replay** (`attempt.js`): `GET
  /api/attempts/<id>` carries `path` - `[seconds, x, y, z, speed]` per
  second from the log's `step` lines (`Attempts.PathOf`; "-" = a load,
  skipped; not in `AttemptChain.Replay`, so no `src/Data` change) - and the
  page draws it on `RunMap` (the spot page's map: photo / ground / relief
  layers, line and dot) with play, a scrub bar and 1-8x speed. No map when
  the log has fewer than two positions.

## Compare: two YouTube runs side by side (2026-10-04)

maks's request (QA `1554074251831672943`, with a retiming tool's panel as
the picture): a separate tab, one run's YouTube link on each side, each
run's real start and end frame set by hand, every segment timed, the two
played next to each other. Site only, no plugin change.

- **`/compare`** (nav: Spots / Compare / About; `wwwroot/compare.js`):
  per side a link (youtube.com/watch, youtu.be, /shorts/, /live/, /embed/
  or a bare id - anything else is refused under the box; a link's `t=`
  opens there), runner name, frame rate (default 60), frame steps
  +-1 / 10 / 100 / 1k / 10k, Start / End *Set to current* + *Go*, the run
  time live, *Split here* (fills the next empty split). The table: one row
  per split + End, each side's segment and running total (a click shows
  that moment), *Set* / clear per cell, names editable, B - A per segment
  and total (green = B faster; a split earlier than the one above is red).
  *Play both from* Start / a split / where each is now: both seek there and
  play. Pause both, both +-1 frame, speed 0.25-2x. **Skip both** -10 / -1 /
  +1 / +10 s and a **shared slider over run time** (video time minus each
  side's Start; seeks both on release). Keys `,` / `.` (Shift = 10) step the
  last-used side; space / `k` play-pause both, left / right (Shift: 10 s) and
  `j` / `l` (10 s) skip both.
- **Keeping the two together (2026-10-04, the author: desync, pause / replay
  loop):** one run-time clock (a reference per side), driven by a 100 ms
  timer (not rAF: a hidden tab stops frames). Modes: paused / starting
  (after a seek: wait until neither side buffers, then play both in one go) /
  playing / holding. Every command the page sends opens a 1-1.5 s quiet
  window in which state changes are not reacted to (the old ping-pong:
  a seek on a playing side buffers, which paused the other, whose pause
  paused both...). Playing: a side buffering 0.5 s pauses the other; when it
  plays again (stable 0.3 s) the other seeks to it and plays - one resume;
  a drift over 0.25 s for 0.6 s moves the one ahead back, at most once in
  3 s; a pause in a video's own controls pauses both. Checked live
  (two public Forest videos, hidden-pane Chromium): play lines up after one
  correction, forced pause / space, skips, slider, a forced seek of one side,
  a forged buffering hold + resume - no loops.
- **Frame-exact like the retiming tools:** the frame at time t is
  floor(t x fps); a set time is snapped to its frame, a step / Go seeks to
  the middle of the frame (checked live: +1 = exactly 1/60 s).
- **The address is the comparison; nothing is stored** (no new write
  endpoint, nothing to moderate): `?a=<id>~<fps>~<start ms>~<end ms>~<split
  ms>...&b=...&n=<name>|<name>&an=<runner>&bn=<runner>`, rewritten as you
  edit (`history.replaceState`), *Copy link* copies it. The server answers
  `/compare` with a fixed link preview (`Program.cs`).
- **Players:** `www.youtube-nocookie.com/embed/<id>?enablejsapi=1&origin=...`
  driven by the IFrame API's own postMessage protocol (`listening`, then
  `command` seekTo / playVideo / pauseVideo / setPlaybackRate / mute;
  `infoDelivery` brings currentTime about 4 times a second, interpolated
  between). **No YouTube script on the page**: the CSP gains only
  `frame-src https://www.youtube-nocookie.com`; `script-src 'self'` stands
  (test `Compare_PageWithPreviewAndYouTubeFrameOnly`). A video loaded with
  a start is played muted and paused on its first frame (a cued video shows
  no frame), then put back on the exact one. Player errors and a player
  that never answers (an extension blocking the frame) show under the video.
- Checked in the preview (Chromium, 1280 / 375 px): both play lined up
  (1 frame apart after 15 s), steps, split here, a refused link, an
  unavailable video's message, the share address round trip.
- **Not built (ideas, if runners ask):** a run on the site carrying its
  video (the owner sets the link from the game - a runner is their upload
  token, so a Runs tab text field + `POST /api/runs/<id>/video` checked
  like `DELETE /api/spots/<id>`), then *Compare videos* from a spot's board
  prefilled with the split names and each run's start from its splits; the
  run-mode attempt page beside its video (the run code ties the two); short
  links stored on the server (a write endpoint to rate-limit and moderate).

## Buildings and interaction markers on the maps (2026-10-04)

The spot page's 2D and 3D maps draw the **focused run's** structures and
interactions (the plugin's replay tracks, docs/run-audit-and-replays.md
part 2) - that run only, and only while its line is on the map, to keep
it readable; nothing when it has none. `/api/runs/<id>` serves
`events: [t, kind, label, x, y, z, group]` (group: `RunAudit.Group`, the
colour) and `buildings: [t, state, kind, x, y, z, yaw, sx, sy, sz, cx,
cy, cz, rx, rz, until]` (`Runs.BuildingsJson`: Unity Euler angles, the
box's size and centre in the structure's own frame, `until` = when a
placed blueprint is finished at the same place - 1.5 m, same kind, the
in-game `ReplayMarks.Until` - else null); the extras were appended, so
older readers' indices hold (`ApiTests` *Upload_ThenBoardShowsEachRunnersBest*).
- **2D** (`map.js`): a building is a footprint turned by its yaw (tilt
  left out from above), from its time on: a blueprint pale blue and
  dashed until finished, a finished one orange and filled; at least 5 px.
  An interaction is a diamond on the line in its group's colour (the
  in-game replay's, `Game/ReplayDraw.GroupColour`), full behind the scrub
  time, faded ahead. Hover (mouse) shows a label (`.maptip`, "11.0 s ·
  Enemy killed: Cannibal", "Log Cabin (blueprint) · built at 13.5 s");
  a click / tap pins it and moves the clock to its time; a tap elsewhere
  clears it. Markers win over buildings when both are under the pointer.
- **3D** (`map3d.js`): buildings as wireframe boxes with the full
  rotation (Unity's ZXY Euler through the view's -z mirror = three's
  `YXZ` with x and y negated), a finished one with a faint fill; markers
  one `THREE.Points` (a diamond shader, fixed pixel size, not depth
  tested - as the ghosts), hover / tap as in 2D (the label placed by
  projecting the marker each render).
- **Buildings / Markers** switches beside the map's other buttons
  (`markSwitches` in app.js), shown only when the focused run has some,
  kept per browser (`forest.mapBuildings` / `forest.mapMarkers`).
- Checked on a local copy with a test run (`b|` / `e|` lines): desktop and
  375 px, 2D and 3D, hover, tap, switches, hiding the run's line; no
  console error or CSP report. Not seen with a real run from the game yet.
- **Testing locally:** the `forest-site` preview runs the main checkout's
  `site/ForestSite` (launch.json's relative path), not a worktree's: from a
  worktree build to a scratch folder and run it on another port from the
  worktree's `site/ForestSite` (`FOREST_DATA` a scratch folder), then
  `preview_start` with its URL. A test run: register a runner, post a
  `.foseg` whose `[attempt]` has the server's route (the first refusal
  names it: "the segment is <route>").

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

**Open items here are tasks since 2026-10-07** (`python scripts/tasks.py list --open --area site`); this section keeps the detail.

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
   Photo / Ground layers): see *The photo map* below.
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

2026-10-04: one new runner write, `DELETE /api/spots/<id>` - token-checked
like uploads, owner-only, never another runner's runs, logged; the
Discord webhook's URL is a secret in `.env` and its posts carry no
mentions (*Spot categories, owners deleting spots, Discord PB posts*).

2026-10-10 (T-0232): `GET / PUT /api/admin/pbposts`, owner only (a named
admin's token gets 403 - the answer holds the runners'-spots webhook URL,
a secret). The URL must be a discord.com webhook address (no SSRF through
the admin page), is never logged (the Activity log keeps the request path,
not the body; the senders log their name), and reaches no public answer.
Runners' spots are a spam path the caps bound: their own 30 an hour and 5
a runner an hour, `allowed_mentions: none`, escaped and clipped names.

2026-10-04: the CSP's one frame, `frame-src https://www.youtube-nocookie.com`
(/compare's players, driven by postMessage; no YouTube script, no new
endpoint - *Compare: two YouTube runs side by side*).

### Audit 2026-10-04 (everything since 2026-10-01)

Checked: every endpoint again (run mode attempts / checkpoints / logs,
categories + their /admin editor and speedrun.com sync, spot submissions,
`DELETE /api/spots/<id>`, the Discord PB webhook, the attempt page's audit
log timeline), `Store` / `Runs` / `Attempts` / `Categories` SQL, every
`wwwroot/*.js` DOM sink, CSP / headers, rate limits, logs, the deploy,
`dotnet list package --vulnerable` (none). Probed locally only: path
traversal under `/world` / `/aerial` (404), a 5 MB chunked body (401
before the body is read without a token, 413 with one), admin with an
empty token (403). **Fine as it is:** all SQL parameterised (the
`Routes` / `Rows` string joins add constants only); no `innerHTML` anywhere
(the attempt page, timeline and admin categories build with `el()` /
`textContent`); attempt ids are checked (`IsAttemptId`) before any file
path; logs are never replaced and are someone else's only by a 409; the
webhook URL is config only (no SSRF), posts carry `allowed_mentions: none`
and escaped markdown, at most 30 an hour and only for community / run
spots; the speedrun.com sync calls a fixed URL; no token or webhook URL is
logged (startup says only on / off); reports name file names, not paths.

**Fixed (site, `ApiTests` *security audit (2026-10-04)*):**
- **Medium - a runner could wipe another spot's run files.** An owner's
  delete removed the whole `runs/<SafeName(id)>` folder, and `.s-x` and
  `s-x` share one (the dot is trimmed): upload a run on a look-alike id,
  delete it, and the other spot's `.run` files were gone (its rows stayed,
  its pages and downloads emptied). Files now go by run id; the folder
  only when empty.
- **Low - `$` in a spot or runner name broke the page.** `Pages.WithMeta`
  used `Regex.Replace` replacement strings: a name holding `$_` / `` $` ``
  pasted the page's own HTML into the description attribute (index.html
  twice, app.js run twice) on its spot / attempt page. Site markup only, no
  script of the attacker's (CSP); evaluators now.
- **Low - a long spot name stopped its PB post.** An upload's spot name
  reached `PbNews.Message` unclipped (Discord refuses > 2,000 characters):
  runner 40, spot 80 now.
- **Low - CI token.** `site.yml` had the default token permissions:
  `permissions: contents: read`.
- **Low - disk.** Run mode logs were capped per address only (3,000 an
  hour, up to 4 MB each). Now also per runner over 24 hours
  (`Attempts.MaxAttemptsPerDay` 5,000 starts / logs,
  `MaxLogBytesPerDay` 2 GB of log text; new `attempts.log_bytes`): past
  it a 413, which the plugin already treats as refused (the file goes to
  `uploads/attempts/refused` with the reason, never retried); a start
  refused runs offline. A reset every 10 s for 8 hours is ~2,900 attempts
  of a few KB. Left: a new runner id per registration (10 an hour per
  address) gets its own budget - the address limits still bound that.
- **Low - CPU per view.** The attempt view / page replayed and judged the
  whole log on every read. The judged result is cached per attempt
  (`Attempts.JudgedLog`, up to 64 MB of log text): logs are never
  replaced, so it is rebuilt only when the allow-list changes, the
  report's category version reaches the site, or the attempt is deleted.
- **Low - Discord spam.** Any registered runner could post up to the
  global 30 PB lines an hour: now at most 5 a runner an hour
  (`PbWebhook.PerRunnerPerHour`) on top.
- **Info - segment ids.** No length cap: now 1-80 characters on upload
  (`Runs.MaxSegmentId`; the plugin's ids are `s-` + 12-32 hex or legacy
  `spot.<category>.<name>` slugs of ~30, 80 matches the spot page's own
  limit and keeps a run folder's name under 255 bytes). Stored ids stay
  readable and deletable. 400 = the plugin sets the upload aside.
- **Info - link previews.** `og:url` came from the request's `Host`: now
  the configured `FOREST_SITE_URL` (default https://forest.deter.cloud).

**Open (not fixed - for the author):**
- **Medium - the container's runtime is never updated.** `compose.yaml`
  runs `mcr.microsoft.com/dotnet/aspnet:10.0`, and deploys only `docker
  restart`: ASP.NET / Kestrel security patches arrive only on a pull. On
  the VPS, monthly (Patch Tuesday): `cd /opt/forest-site && sudo docker
  compose pull && sudo docker compose up -d`. (`dotnet list package
  --vulnerable`: nothing in the app's own packages.)
- **Info:** GitHub actions are pinned by tag, not SHA. Still worth a look
  at `/var/lib/forest-site` now and then (disk). The two 2026-10-01
  decisions above (runner ids, token reset) stand.

Re-check after admin features: new endpoints go under the `admin` group
(its filter checks the token), owner-only ones check `IsOwner`, and any
new page text goes through `el()`.

## Useful from the game later

- Terrain heightmap for 3D: readable live over the bridge
  (`Terrain.activeTerrain.terrainData` heights / size) - dump once to a
  file for the site.
- Caves need a mesh dump (not done; their colliders are in the cave
  scenes).
