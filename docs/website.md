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
   A full run took 17.5 min on 2026-10-01 (v0.24.180). Retake
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
(`wwwroot/world3d.js`, Models / Collision switches), streamed in 250 m
chunks within 700 m of the camera's target, one InstancedMesh per model.

1. **Pooled objects** (game, bridge, <1 s): `call
   static:ForestOverlay.Game.WorldDump Write` ->
   `config/ForestOverlay/world/spawned.txt` - every LOD placeholder (trees,
   bushes, saplings, rocks, cave walls; not greebles under `Pooling`) and
   its prefab's parts. Only needed again after a game update.
2. **Export** (offline, ~40 s, `pip install UnityPy`): `python
   scripts/world-extract.py export` reads the scenes from the game's files
   (level2 main, 7 endgame, 11, 15-30 cave props) + spawned.txt ->
   `site/world-out/` (not in git): `world.json` (materials, meshes, models,
   chunks, `build`), `m/<i>.bin` meshes, `t/<i>.jpg` (+ `.png` for
   cut-outs), `c/<area>_<x>_<z>.bin` instances. `stats [level ...]` prints
   what a scene holds. The format is in the script's docstring.
3. **Upload**: `python scripts/aerial-upload.py --world` (to
   `/api/admin/world`, clears first; `world.json` last). Files are named
   by index, so the server refuses another build's `?v=` and every `?v=`
   mid-upload (404, `no-store`, `MetaBuild`); `world3d.js` re-reads
   `world.json` per 3D view and starts over when a file is refused
   (`stale()`, gotcha 71). A tab open across an upload recovers by itself.

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
- Missing so far: skinned meshes.

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
enough to compare shapes. Headless (the in-app pane pauses rendering when
hidden): `pip install playwright`, Edge via `channel="msedge"` - open a spot,
click 3D, `lookFrom`, wait for no chunk `loading`, page screenshot clipped to
`forest3d.canvas`. A second local site beside another session's (port 5081,
own build output and data): `.claude/launch.json` `forest-site-alt`.

Open: the endgame lab looks sparse; a spot loads thousands of files and
`.bin` is uncompressed (pack / gzip); heavy chunks want LODs for phones;
pickups and the player's random sticks / rocks are missing; per-kind
toggles. The photo map's `aerial.json` is still read once per page
(map.js): a tab open across an aerial upload gets 404 tiles (holes) until
a reload.

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
they are**; the About page says the runner id is not anonymous. Revisit the
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
